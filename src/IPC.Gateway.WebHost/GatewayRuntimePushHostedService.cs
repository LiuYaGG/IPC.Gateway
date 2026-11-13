using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Engine;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimePushHostedService : BackgroundService
{
    private const int MaxTagBatchSize = 500;
    private const int MaxPendingTags = 5000;
    private readonly record struct TagIdentity(string ChannelId, string DeviceId, string GroupId, string TagId);
    private static readonly TimeSpan TagFlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DeviceSampleInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StatusPatchInterval = TimeSpan.FromSeconds(10);

    private readonly object _syncRoot = new();
    private readonly GatewayCoreService _gateway;
    private readonly GatewayRuntimeEventHub _events;
    private readonly ILogger<GatewayRuntimePushHostedService> _logger;
    private readonly Dictionary<string, TagIdentity> _pendingTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _deviceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDeviceSampleUtc = DateTime.MinValue;
    private DateTime _lastStatusPatchUtc = DateTime.MinValue;
    private bool _snapshotRequired;

    public GatewayRuntimePushHostedService(
        GatewayCoreService gateway,
        GatewayRuntimeEventHub events,
        ILogger<GatewayRuntimePushHostedService> logger)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _gateway.Runtime.TagValueChanged -= OnTagValueChanged;
        _gateway.Runtime.TagValueChanged += OnTagValueChanged;
        _gateway.Runtime.TagDiagnosticsChanged -= OnTagValueChanged;
        _gateway.Runtime.TagDiagnosticsChanged += OnTagValueChanged;
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _gateway.Runtime.TagValueChanged -= OnTagValueChanged;
        _gateway.Runtime.TagDiagnosticsChanged -= OnTagValueChanged;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string revision = _gateway.ProjectRevision;
        bool running = _gateway.Runtime.IsRunning;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TagFlushInterval, stoppingToken);
                string currentRevision = _gateway.ProjectRevision;
                bool isRunning = _gateway.Runtime.IsRunning;
                if (revision != currentRevision || running != isRunning)
                {
                    bool lifecycleChanged = running != isRunning;
                    revision = currentRevision;
                    running = isRunning;
                    _deviceFingerprints.Clear();
                    // A stop may not replace the project. Force a fresh capture even
                    // for a browser already showing that configuration revision.
                    _events.Publish("resync", new { projectRevision = lifecycleChanged ? null : revision });
                }
                if (_events.HasDeviceSubscribers)
                {
                    FlushPendingTags();
                    PublishDeviceChangesIfDue();
                }
                else
                {
                    // A later subscriber receives a fresh snapshot.
                    lock (_syncRoot)
                    {
                        if (!_events.HasDeviceSubscribers)
                        {
                            _pendingTags.Clear();
                            _snapshotRequired = false;
                        }
                    }
                    _deviceFingerprints.Clear();
                }
                if (_events.SubscriberCount != 0) PublishStatusPatchIfDue();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish runtime push event.");
            }
        }
    }

    private void OnTagValueChanged(object? sender, TagValueChangedEventArgs e)
    {
        if (!_events.HasDeviceSubscribers || e?.Snapshot == null)
            return;

        TagValueSnapshot tag = e.Snapshot;
        string key = TagPath.BuildIdentity(tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId);

        lock (_syncRoot)
        {
            if (_pendingTags.Count >= MaxPendingTags && !_pendingTags.ContainsKey(key))
            {
                _snapshotRequired = true;
                return;
            }

            _pendingTags[key] = new TagIdentity(tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId);
        }
    }

    private void FlushPendingTags()
    {
        List<TagIdentity> changed;
        bool snapshotRequired;
        lock (_syncRoot)
        {
            snapshotRequired = _snapshotRequired;
            _snapshotRequired = false;
            if (!snapshotRequired && _pendingTags.Count == 0)
                return;
            changed = _pendingTags.Values.ToList();
            _pendingTags.Clear();
        }

        List<TagValueSnapshotDto> pending;
        try
        {
            if (snapshotRequired)
            {
                pending = _gateway.Runtime.GetSnapshots()
                    .Where(tag => _gateway.ContainsRuntimeTag(tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId))
                    .Select(GatewayConfigurationContractMapper.ToDto).ToList();
            }
            else
            {
                pending = new List<TagValueSnapshotDto>(changed.Count);
                foreach (var notified in changed)
                {
                    // A historical value or an equal-source-time failure may arrive after a
                    // diagnostic update. Publish what the engine currently holds for this identity.
                    if (_gateway.ContainsRuntimeTag(notified.ChannelId, notified.DeviceId, notified.GroupId, notified.TagId) &&
                        _gateway.Runtime.TryGetSnapshotById(notified.ChannelId, notified.DeviceId,
                        notified.GroupId, notified.TagId, out var latest) && latest != null)
                        pending.Add(GatewayConfigurationContractMapper.ToDto(latest));
                }
            }
        }
        catch
        {
            lock (_syncRoot) _snapshotRequired = true;
            throw;
        }

        // Drain the complete captured set every tick, keeping each SSE event
        // bounded. Removing only the first batch caps throughput and lets
        // frequently updated dictionary slots starve other tags indefinitely.
        // New updates collected while publishing remain for the next tick.
        for (int offset = 0; offset < pending.Count; offset += MaxTagBatchSize)
        {
            int count = Math.Min(MaxTagBatchSize, pending.Count - offset);
            _events.Publish("tags", new GatewayRuntimeTagsChangedEvent
            {
                Tags = pending.GetRange(offset, count),
                PendingCount = pending.Count - offset - count
            });
        }
    }

    private void PublishDeviceChangesIfDue()
    {
        DateTime nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastDeviceSampleUtc < DeviceSampleInterval)
            return;

        _lastDeviceSampleUtc = nowUtc;
        List<DeviceRuntimeStatusDto> devices = _gateway.Runtime
            .GetDeviceStatuses()
            .Select(GatewayConfigurationContractMapper.ToDto)
            .ToList();

        List<DeviceRuntimeStatusDto> changed = new();
        HashSet<string> currentKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceRuntimeStatusDto device in devices)
        {
            string key = BuildDeviceKey(device);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            currentKeys.Add(key);
            string fingerprint = BuildDeviceFingerprint(device);
            if (!_deviceFingerprints.TryGetValue(key, out string? previous) || previous != fingerprint)
            {
                changed.Add(device);
                _deviceFingerprints[key] = fingerprint;
            }
        }

        List<string> removed = _deviceFingerprints.Keys
            .Where(key => !currentKeys.Contains(key))
            .ToList();
        foreach (string key in removed)
            _deviceFingerprints.Remove(key);

        if (changed.Count > 0 || removed.Count > 0)
            _events.Publish("devices", new GatewayRuntimeDevicesChangedEvent { Devices = changed, RemovedDeviceKeys = removed });
    }

    private void PublishStatusPatchIfDue()
    {
        DateTime nowUtc = DateTime.UtcNow;
        if (nowUtc - _lastStatusPatchUtc < StatusPatchInterval)
            return;

        _lastStatusPatchUtc = nowUtc;
        GatewayRuntimeStatusDto status = GatewayConfigurationContractMapper.ToDto(_gateway.GetStatusSummary());
        _events.Publish("status", new GatewayRuntimeStatusPatchEvent { Status = status });
    }

    private static string BuildDeviceKey(DeviceRuntimeStatusDto device)
    {
        return "id:" + string.Join("/", device.ChannelId ?? string.Empty, device.DeviceId ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
    }

    private static string BuildDeviceFingerprint(DeviceRuntimeStatusDto device)
    {
        return string.Join("|",
            device.ChannelId,
            device.ChannelName,
            device.DeviceId,
            device.DeviceName,
            device.Enabled,
            device.IsConnected,
            device.IsPolling,
            device.IsQueued,
            device.Status,
            device.ConsecutiveFailures,
            device.TotalReads,
            device.SuccessfulReads,
            device.FailedReads,
            device.SuccessRate,
            device.LastPollTime.Ticks,
            device.LastSuccessTime.Ticks,
            device.LastFailureTime.Ticks,
            device.NextPollTime.Ticks,
            device.LastTaskStatus,
            device.LastTaskDurationMs,
            device.SlowPollCount,
            device.TimeoutCount,
            device.LastError);
    }
}
