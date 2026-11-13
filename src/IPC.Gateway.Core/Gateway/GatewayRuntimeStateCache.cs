/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeStateCache
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway;

public sealed class GatewayRuntimeStateCache : IDisposable
{
    private const int CaptureIntervalMs = 1000;
    private const int MaxRecentErrors = 100;

    // Serialize full captures with lifecycle changes, without blocking tag notifications on PLC reads.
    private readonly object _captureGate = new();
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, DeviceRuntimeStatus> _devices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagValueSnapshot> _tags = new(StringComparer.OrdinalIgnoreCase);
    private List<RuntimeErrorDetail> _errors = new();
    private IReadOnlySet<string> _activeTagKeys = new HashSet<string>();
    private IRuntimeService? _runtime;
    private Timer? _timer;

    public void Start(IRuntimeService runtime, IReadOnlySet<string> activeTagKeys)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        lock (_captureGate)
        {
            Detach();
            lock (_syncRoot)
            {
                _tags.Clear();
                _devices.Clear();
                _errors.Clear();
                _activeTagKeys = activeTagKeys;
                _runtime = runtime;
            }

            runtime.TagValueChanged += OnTagValueChanged;
            CaptureFromRuntime(runtime);
            _timer = new Timer(OnTimer, null, CaptureIntervalMs, CaptureIntervalMs);
        }
    }

    public void Stop(bool markDevicesOffline)
    {
        lock (_captureGate)
        {
            IRuntimeService? runtime = Detach();
            if (runtime != null)
                CaptureFromRuntime(runtime);

            if (markDevicesOffline)
            {
                lock (_syncRoot)
                {
                    foreach (DeviceRuntimeStatus device in _devices.Values)
                    {
                        device.IsConnected = false;
                        device.IsPolling = false;
                        device.IsQueued = false;
                        device.Status = device.Enabled ? "Offline" : "Disabled";
                    }
                }
            }
        }
    }

    public void PrepareRuntimeReload(ProjectConfig previous, ProjectConfig current)
    {
        lock (_captureGate)
        {
            Stop(markDevicesOffline: true);
            var reusable = RuntimeConfigDiffer.GetReusableSnapshotKeys(previous, current);
            lock (_syncRoot)
            {
                foreach (string key in _tags.Keys.Where(key => !reusable.Contains(key)).ToList())
                    _tags.Remove(key);
            }
        }
    }

    public void AttachReloadedRuntime(IRuntimeService runtime, IReadOnlySet<string> activeTagKeys)
    {
        lock (_captureGate)
        {
            List<TagValueSnapshot> previous;
            lock (_syncRoot)
                previous = _tags.Values.Select(tag => tag.Clone()).ToList();

            runtime.RestoreSnapshots(previous);
            Start(runtime, activeTagKeys);
        }
    }

    public void ApplyRuntimeProject(IRuntimeService runtime, IReadOnlySet<string> activeTagKeys)
    {
        lock (_captureGate)
        {
            lock (_syncRoot)
            {
                _activeTagKeys = activeTagKeys;
                foreach (string key in _tags.Keys.Where(key => !activeTagKeys.Contains(key)).ToList())
                    _tags.Remove(key);
            }
            CaptureFromRuntime(runtime);
        }
    }

    public GatewayRuntimeStateSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return new GatewayRuntimeStateSnapshot
            {
                Devices = _devices.Values.Select(CloneDevice).OrderBy(item => item.DeviceName).ToList(),
                Tags = _tags.Values.Select(item => item.Clone()).OrderBy(item => item.DeviceName).ThenBy(item => item.GroupName).ThenBy(item => item.TagName).ToList(),
                RecentErrors = _errors.Select(error => error.Clone()).ToList(),
                UpdatedTime = DateTime.Now
            };
        }
    }

    internal void FillStatusSummary(GatewayRuntimeStatus status, HashSet<string> deviceKeys, HashSet<string> tagKeys)
    {
        lock (_syncRoot)
        {
            GatewayCoreService.FillRuntimeCounts(status,
                _tags.Where(entry => tagKeys.Contains(entry.Key)).Select(entry => entry.Value),
                _devices.Where(entry => deviceKeys.Contains(entry.Key)).Select(entry => entry.Value));
            status.RecentErrors = _errors
                .Where(error => string.IsNullOrWhiteSpace(error.DeviceId) || deviceKeys.Contains(GetDeviceKey(error.ChannelId, error.DeviceId)))
                .Take(20).Select(error => error.Clone()).ToList();
        }
    }

    public void Dispose()
    {
        lock (_captureGate)
            Detach();
    }

    private IRuntimeService? Detach()
    {
        _timer?.Dispose();
        _timer = null;
        IRuntimeService? runtime;
        lock (_syncRoot)
        {
            runtime = _runtime;
            _runtime = null;
        }
        if (runtime != null)
            runtime.TagValueChanged -= OnTagValueChanged;
        return runtime;
    }

    private void OnTagValueChanged(object? sender, TagValueChangedEventArgs args)
    {
        lock (_syncRoot)
        {
            if (_runtime != null && ReferenceEquals(_runtime, sender) && args?.Snapshot != null)
                MergeTagNoLock(args.Snapshot);
        }
    }

    private void OnTimer(object? state)
    {
        if (!Monitor.TryEnter(_captureGate))
            return;

        try
        {
            if (_runtime != null)
                CaptureFromRuntime(_runtime);
        }
        catch (Exception ex)
        {
            IpcLogService.WriteError("Runtime state cache timer failed.", ex);
        }
        finally
        {
            Monitor.Exit(_captureGate);
        }
    }

    private void CaptureFromRuntime(IRuntimeService runtime)
    {
        var devices = runtime.GetDeviceStatuses();
        var tags = runtime.GetSnapshots();
        var errors = runtime.GetRecentErrors(MaxRecentErrors);
        lock (_syncRoot)
        {
            _devices.Clear();
            foreach (DeviceRuntimeStatus device in devices)
                _devices[GetDeviceKey(device.ChannelId, device.DeviceId)] = device;

            foreach (TagValueSnapshot tag in tags)
                MergeTagNoLock(tag);

            _errors = errors.Where(error => RuntimeErrorActivityFilter.IsActive(error, _devices, _tags))
                .OrderByDescending(error => error.Timestamp).Take(MaxRecentErrors).ToList();
        }
    }

    private void MergeTagNoLock(TagValueSnapshot tag)
    {
        string key = TagPath.BuildIdentity(tag.ChannelId, tag.DeviceId, tag.GroupId, tag.TagId);
        if (!_activeTagKeys.Contains(key))
            return;
        if (_tags.TryGetValue(key, out var previous) && previous.RuntimeRevision > tag.RuntimeRevision)
            return;
        // Runtime snapshots and event payloads are already independent copies.
        _tags[key] = tag;
    }

    private static string GetDeviceKey(string channelId, string deviceId) =>
        TagPath.Normalize(channelId) + "/" + TagPath.Normalize(deviceId);

    private static DeviceRuntimeStatus CloneDevice(DeviceRuntimeStatus source)
    {
        return new DeviceRuntimeStatus
        {
            ChannelId = source.ChannelId ?? string.Empty,
            ChannelName = source.ChannelName ?? string.Empty,
            DeviceId = source.DeviceId ?? string.Empty,
            DeviceName = source.DeviceName ?? string.Empty,
            Protocol = source.Protocol ?? string.Empty,
            Enabled = source.Enabled,
            IsConnected = source.IsConnected,
            IsPolling = source.IsPolling,
            IsQueued = source.IsQueued,
            Status = source.Status ?? string.Empty,
            ConsecutiveFailures = source.ConsecutiveFailures,
            TotalReads = source.TotalReads,
            SuccessfulReads = source.SuccessfulReads,
            FailedReads = source.FailedReads,
            SuccessRate = source.SuccessRate,
            LastPollTime = source.LastPollTime,
            LastSuccessTime = source.LastSuccessTime,
            LastFailureTime = source.LastFailureTime,
            NextReconnectTime = source.NextReconnectTime,
            LastReconnectDelayMs = source.LastReconnectDelayMs,
            NextPollTime = source.NextPollTime,
            CurrentTaskId = source.CurrentTaskId,
            LastTaskStatus = source.LastTaskStatus ?? string.Empty,
            LastTaskDurationMs = source.LastTaskDurationMs,
            SlowPollCount = source.SlowPollCount,
            TimeoutCount = source.TimeoutCount,
            LastError = source.LastError ?? string.Empty,
            DeviceState = source.DeviceState ?? string.Empty,
            TransportConnected = source.TransportConnected,
            IsIsolated = source.IsIsolated,
            RecoveryState = source.RecoveryState ?? string.Empty,
            IsolatedSinceTime = source.IsolatedSinceTime,
            NextRecoveryProbeTime = source.NextRecoveryProbeTime,
            ChannelKey = source.ChannelKey ?? string.Empty,
            ChannelStatus = source.ChannelStatus ?? string.Empty,
            ChannelConsecutiveFailures = source.ChannelConsecutiveFailures,
            ChannelLastSuccessTime = source.ChannelLastSuccessTime,
            ChannelLastFailureTime = source.ChannelLastFailureTime,
            ChannelLastError = source.ChannelLastError ?? string.Empty,
            ProtocolCircuitBreaker = source.ProtocolCircuitBreaker
        };
    }
}
