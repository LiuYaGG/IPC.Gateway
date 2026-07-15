using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Engine;

namespace IPC.Gateway.WebHost;

public sealed class GatewayRuntimePushHostedService : BackgroundService
{
    private const int MaxTagBatchSize = 500;
    private const int MaxPendingTags = 5000;
    private static readonly TimeSpan TagFlushInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DeviceSampleInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StatusPatchInterval = TimeSpan.FromSeconds(10);

    private readonly object _syncRoot = new();
    private readonly GatewayCoreService _gateway;
    private readonly GatewayRuntimeEventHub _events;
    private readonly ILogger<GatewayRuntimePushHostedService> _logger;
    private readonly Dictionary<string, TagValueSnapshotDto> _pendingTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _deviceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastDeviceSampleUtc = DateTime.MinValue;
    private DateTime _lastStatusPatchUtc = DateTime.MinValue;

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
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _gateway.Runtime.TagValueChanged -= OnTagValueChanged;
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TagFlushInterval, stoppingToken);
                FlushPendingTags();
                PublishDeviceChangesIfDue();
                PublishStatusPatchIfDue();
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
        if (e?.Snapshot == null)
            return;

        TagValueSnapshotDto tag = GatewayConfigurationContractMapper.ToDto(e.Snapshot);
        string key = BuildTagKey(tag);
        if (string.IsNullOrWhiteSpace(key))
            return;

        lock (_syncRoot)
        {
            if (_pendingTags.Count >= MaxPendingTags && !_pendingTags.ContainsKey(key))
                return;

            _pendingTags[key] = tag;
        }
    }

    private void FlushPendingTags()
    {
        List<TagValueSnapshotDto> tags = new();
        int pendingCount;
        lock (_syncRoot)
        {
            foreach (KeyValuePair<string, TagValueSnapshotDto> item in _pendingTags.Take(MaxTagBatchSize).ToList())
            {
                tags.Add(item.Value);
                _pendingTags.Remove(item.Key);
            }

            pendingCount = _pendingTags.Count;
        }

        if (tags.Count > 0)
            _events.Publish("tags", new GatewayRuntimeTagsChangedEvent { Tags = tags, PendingCount = pendingCount });
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
        GatewayRuntimeStatusDto status = GatewayConfigurationContractMapper.ToDto(_gateway.GetStatus());
        status.Tags = new List<TagValueSnapshotDto>();
        status.Devices = new List<DeviceRuntimeStatusDto>();
        _events.Publish("status", new GatewayRuntimeStatusPatchEvent { Status = status });
    }

    private static string BuildTagKey(TagValueSnapshotDto tag)
    {
        return "id:" + string.Join("/",
            tag.ChannelId ?? string.Empty,
            tag.DeviceId ?? string.Empty,
            tag.GroupId ?? string.Empty,
            tag.TagId ?? string.Empty).Trim().ToLowerInvariant();
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
