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
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway;

public sealed class GatewayRuntimeStateCache : IDisposable
{
    private const int CaptureIntervalMs = 1000;
    private const int PersistIntervalMs = 5000;
    private const int MaxRecentErrors = 100;

    private readonly object _syncRoot = new object();
    private readonly SemaphoreSlim _persistSemaphore = new SemaphoreSlim(1, 1);
    private readonly SqlSugarRuntimeStateRepository _repository;
    private readonly Dictionary<string, DeviceRuntimeStatus> _devices = new Dictionary<string, DeviceRuntimeStatus>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagValueSnapshot> _tags = new Dictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuntimeErrorDetail> _errors = new Dictionary<string, RuntimeErrorDetail>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeDeviceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeTagKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private IRuntimeService? _runtime;
    private string _projectId = "default";
    private Timer? _timer;
    private DateTime _lastPersistUtc = DateTime.MinValue;
    private long _changeVersion;
    private bool _dirty;
    private bool _disposed;

    public GatewayRuntimeStateCache(SqlSugarRuntimeStateRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public void Start(IRuntimeService runtime, ProjectConfig project)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));

        Stop(markDevicesOffline: false);

        string projectId = GetProjectId(project);
        GatewayRuntimeStateSnapshot persisted = LoadPersisted(projectId);
        lock (_syncRoot)
        {
            _projectId = projectId;
            SetActiveProjectNoLock(project);
            LoadIntoCache(persisted);
            PruneToActiveProjectNoLock();
            _runtime = runtime;
        }

        runtime.RestoreSnapshots(persisted.Tags);
        runtime.TagValueChanged += OnTagValueChanged;
        CaptureFromRuntime(runtime);
        Flush();
        StartTimer();
    }

    public void Stop(bool markDevicesOffline)
    {
        IRuntimeService? runtime;
        lock (_syncRoot)
        {
            runtime = _runtime;
            _runtime = null;
        }

        if (runtime != null)
            runtime.TagValueChanged -= OnTagValueChanged;

        StopTimer();

        if (runtime != null)
            CaptureFromRuntime(runtime);

        if (markDevicesOffline)
            MarkDevicesOffline();

        Flush();
    }

    public void PrepareRuntimeReload(ProjectConfig project, bool markDevicesOffline)
    {
        IRuntimeService? runtime;
        lock (_syncRoot)
        {
            runtime = _runtime;
            _runtime = null;
        }

        if (runtime != null)
            runtime.TagValueChanged -= OnTagValueChanged;

        StopTimer();

        if (runtime != null)
            CaptureFromRuntime(runtime);

        lock (_syncRoot)
        {
            _projectId = GetProjectId(project);
            SetActiveProjectNoLock(project);
            PruneToActiveProjectNoLock();
        }

        if (markDevicesOffline)
            MarkDevicesOffline();
    }

    public void AttachReloadedRuntime(IRuntimeService runtime, ProjectConfig project)
    {
        if (runtime == null)
            throw new ArgumentNullException(nameof(runtime));

        IRuntimeService? previousRuntime;
        lock (_syncRoot)
        {
            previousRuntime = _runtime;
            _runtime = null;
        }

        if (previousRuntime != null)
            previousRuntime.TagValueChanged -= OnTagValueChanged;

        StopTimer();

        IList<TagValueSnapshot> snapshots;
        lock (_syncRoot)
        {
            _projectId = GetProjectId(project);
            SetActiveProjectNoLock(project);
            PruneToActiveProjectNoLock();
            snapshots = _tags.Values.Select(item => item.Clone()).ToList();
            MarkDirtyNoLock();
        }

        runtime.RestoreSnapshots(snapshots);
        runtime.TagValueChanged += OnTagValueChanged;
        lock (_syncRoot)
            _runtime = runtime;
        CaptureFromRuntime(runtime);
        StartTimer();
    }

    public void ApplyRuntimeProject(IRuntimeService runtime, ProjectConfig project)
    {
        IRuntimeService? previousRuntime;
        lock (_syncRoot)
        {
            previousRuntime = _runtime;
            _projectId = GetProjectId(project);
            SetActiveProjectNoLock(project);
            PruneToActiveProjectNoLock();
            _runtime = runtime;
            MarkDirtyNoLock();
        }

        if (runtime != null && !ReferenceEquals(previousRuntime, runtime))
        {
            if (previousRuntime != null)
                previousRuntime.TagValueChanged -= OnTagValueChanged;
            runtime.TagValueChanged -= OnTagValueChanged;
            runtime.TagValueChanged += OnTagValueChanged;
        }

        if (runtime != null)
            CaptureFromRuntime(runtime);
    }

    public GatewayRuntimeStateSnapshot CaptureNow(IRuntimeService runtime)
    {
        if (runtime != null)
            CaptureFromRuntime(runtime);

        FlushIfDue();
        return GetSnapshot();
    }

    public GatewayRuntimeStateSnapshot GetSnapshot()
    {
        lock (_syncRoot)
        {
            return CreateSnapshotNoLock();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop(markDevicesOffline: true);
        _persistSemaphore.Dispose();
    }

    private void OnTagValueChanged(object? sender, TagValueChangedEventArgs args)
    {
        if (args == null || args.Snapshot == null)
            return;

        lock (_syncRoot)
        {
            _tags[GetTagKey(args.Snapshot)] = args.Snapshot.Clone();
            MarkDirtyNoLock();
        }
    }

    private void OnTimer(object? state)
    {
        try
        {
            IRuntimeService? runtime;
            lock (_syncRoot)
                runtime = _runtime;

            if (runtime != null)
                CaptureFromRuntime(runtime);

            FlushIfDue();
        }
        catch (Exception ex)
        {
            IpcLogService.WriteError("Runtime state cache timer failed.", ex);
        }
    }

    private void CaptureFromRuntime(IRuntimeService runtime)
    {
        if (runtime == null)
            return;

        IList<DeviceRuntimeStatus> devices = runtime.GetDeviceStatuses();
        IList<TagValueSnapshot> tags = runtime.GetSnapshots();
        IList<RuntimeErrorDetail> errors = runtime.GetRecentErrors(MaxRecentErrors);

        lock (_syncRoot)
        {
            MergeDevicesNoLock(devices);
            MergeTagsNoLock(tags);
            MergeErrorsNoLock(errors);
            PruneToActiveProjectNoLock();
            MarkDirtyNoLock();
        }
    }

    private void LoadIntoCache(GatewayRuntimeStateSnapshot snapshot)
    {
        _devices.Clear();
        _tags.Clear();
        _errors.Clear();

        if (snapshot == null)
            return;

        MergeDevicesNoLock(snapshot.Devices);
        MergeTagsNoLock(snapshot.Tags);
        MergeErrorsNoLock(snapshot.RecentErrors);
        _dirty = false;
        _changeVersion = 0L;
    }

    private void MergeDevicesNoLock(IList<DeviceRuntimeStatus> devices)
    {
        if (devices == null)
            return;

        foreach (DeviceRuntimeStatus device in devices)
        {
            if (device == null)
                continue;
            _devices[GetDeviceKey(device.ChannelId, device.DeviceId)] = CloneDevice(device);
        }
    }

    private void MergeTagsNoLock(IList<TagValueSnapshot> tags)
    {
        if (tags == null)
            return;

        foreach (TagValueSnapshot tag in tags)
        {
            if (tag == null)
                continue;
            _tags[GetTagKey(tag)] = tag.Clone();
        }
    }

    private void MergeErrorsNoLock(IList<RuntimeErrorDetail> errors)
    {
        if (errors == null)
            return;

        foreach (RuntimeErrorDetail error in errors)
        {
            if (error == null || string.IsNullOrWhiteSpace(error.Message))
                continue;
            _errors[GetErrorKey(error)] = error.Clone();
        }

        TrimErrorsNoLock();
    }

    private void TrimErrorsNoLock()
    {
        if (_errors.Count <= MaxRecentErrors)
            return;

        List<KeyValuePair<string, RuntimeErrorDetail>> ordered = _errors
            .OrderByDescending(item => item.Value.Timestamp)
            .ToList();

        for (int i = MaxRecentErrors; i < ordered.Count; i++)
            _errors.Remove(ordered[i].Key);
    }

    private void MarkDevicesOffline()
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

            MarkDirtyNoLock();
        }
    }

    private void SetActiveProjectNoLock(ProjectConfig project)
    {
        _activeDeviceKeys.Clear();
        _activeTagKeys.Clear();

        if (project == null || project.Devices == null)
            return;

        foreach (DeviceConfig device in project.Devices)
        {
            if (device == null)
                continue;

            AddIfNotEmpty(_activeDeviceKeys, GetDeviceKey(device.ChannelId, device.Id));
            CollectTagKeysNoLock(device, null, device.Tags);

            if (device.Groups == null)
                continue;

            foreach (GroupConfig group in device.Groups)
            {
                if (group == null)
                    continue;

                CollectTagKeysNoLock(device, group, group.Tags);
            }
        }
    }

    private void CollectTagKeysNoLock(DeviceConfig device, GroupConfig? group, IList<TagConfig> tags)
    {
        if (tags == null)
            return;

        foreach (TagConfig tag in tags)
        {
            if (tag == null)
                continue;

            AddIfNotEmpty(_activeTagKeys, TagPath.BuildIdentity(
                device.ChannelId,
                device.Id,
                group == null ? string.Empty : group.Id,
                tag.Id));
        }
    }

    private void PruneToActiveProjectNoLock()
    {
        bool removed = false;

        if (_activeDeviceKeys.Count > 0)
        {
            foreach (string key in _devices.Keys.ToList())
            {
                DeviceRuntimeStatus device = _devices[key];
                if (!IsActiveDeviceNoLock(device))
                {
                    _devices.Remove(key);
                    removed = true;
                }
            }
        }
        else if (_devices.Count > 0)
        {
            _devices.Clear();
            removed = true;
        }

        if (_activeTagKeys.Count > 0)
        {
            foreach (string key in _tags.Keys.ToList())
            {
                TagValueSnapshot tag = _tags[key];
                if (!IsActiveTagNoLock(tag))
                {
                    _tags.Remove(key);
                    removed = true;
                }
            }
        }
        else if (_tags.Count > 0)
        {
            _tags.Clear();
            removed = true;
        }

        if (_activeDeviceKeys.Count > 0)
        {
            foreach (string key in _errors.Keys.ToList())
            {
                RuntimeErrorDetail error = _errors[key];
                if (!string.IsNullOrWhiteSpace(error.DeviceId) &&
                    !_activeDeviceKeys.Contains(GetDeviceKey(error.ChannelId, error.DeviceId)))
                {
                    _errors.Remove(key);
                    removed = true;
                }
            }
        }
        else if (_errors.Count > 0)
        {
            _errors.Clear();
            removed = true;
        }

        if (removed)
            MarkDirtyNoLock();
    }

    private bool IsActiveDeviceNoLock(DeviceRuntimeStatus device)
    {
        if (device == null)
            return false;
        return _activeDeviceKeys.Contains(GetDeviceKey(device.ChannelId, device.DeviceId));
    }

    private bool IsActiveTagNoLock(TagValueSnapshot tag)
    {
        if (tag == null)
            return false;
        return _activeTagKeys.Contains(GetTagKey(tag));
    }

    private void FlushIfDue()
    {
        bool shouldFlush;
        lock (_syncRoot)
        {
            shouldFlush = _dirty && (DateTime.UtcNow - _lastPersistUtc).TotalMilliseconds >= PersistIntervalMs;
        }

        if (shouldFlush)
            _ = FlushAsync(skipIfBusy: true);
    }

    private void Flush()
    {
        FlushAsync(skipIfBusy: false).GetAwaiter().GetResult();
    }

    private async Task FlushAsync(bool skipIfBusy)
    {
        GatewayRuntimeStateSnapshot snapshot;
        string projectId;
        long snapshotVersion;
        lock (_syncRoot)
        {
            if (!_dirty)
                return;

            snapshot = CreateSnapshotNoLock();
            projectId = _projectId;
            snapshotVersion = _changeVersion;
        }

        try
        {
            bool lockTaken;
            if (skipIfBusy)
            {
                lockTaken = _persistSemaphore.Wait(0);
                if (!lockTaken)
                    return;
            }
            else
            {
                await _persistSemaphore.WaitAsync();
                lockTaken = true;
            }

            try
            {
                await _repository.SaveAsync(projectId, snapshot);
            }
            finally
            {
                if (lockTaken)
                    _persistSemaphore.Release();
            }

            lock (_syncRoot)
            {
                if (_changeVersion == snapshotVersion)
                    _dirty = false;
                _lastPersistUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            IpcLogService.WriteError("Runtime state persist failed.", ex);
        }
    }

    private void MarkDirtyNoLock()
    {
        _dirty = true;
        _changeVersion++;
    }

    private GatewayRuntimeStateSnapshot CreateSnapshotNoLock()
    {
        return new GatewayRuntimeStateSnapshot
        {
            Devices = _devices.Values.Select(CloneDevice).OrderBy(item => item.DeviceName).ToList(),
            Tags = _tags.Values.Select(item => item.Clone()).OrderBy(item => item.DeviceName).ThenBy(item => item.GroupName).ThenBy(item => item.TagName).ToList(),
            RecentErrors = _errors.Values.Select(item => item.Clone()).OrderByDescending(item => item.Timestamp).Take(MaxRecentErrors).ToList(),
            UpdatedTime = DateTime.Now
        };
    }

    private GatewayRuntimeStateSnapshot LoadPersisted(string projectId)
    {
        try
        {
            return _repository.Load(projectId);
        }
        catch (Exception ex)
        {
            IpcLogService.WriteError("Runtime state restore failed.", ex);
            return new GatewayRuntimeStateSnapshot();
        }
    }

    private void StartTimer()
    {
        StopTimer();
        _timer = new Timer(OnTimer, null, CaptureIntervalMs, CaptureIntervalMs);
    }

    private void StopTimer()
    {
        Timer? timer = Interlocked.Exchange(ref _timer, null);
        if (timer != null)
            timer.Dispose();
    }

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

    private static string GetProjectId(ProjectConfig project)
    {
        return project == null || string.IsNullOrWhiteSpace(project.ProjectId)
            ? "default"
            : project.ProjectId.Trim();
    }

    private static string GetDeviceKey(string channelId, string deviceId)
    {
        return TagPath.Normalize(channelId) + "/" + TagPath.Normalize(deviceId);
    }

    private static void AddIfNotEmpty(HashSet<string> set, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            set.Add(value.Trim());
    }

    private static string GetTagKey(TagValueSnapshot snapshot)
    {
        return TagPath.BuildIdentity(snapshot.ChannelId, snapshot.DeviceId, snapshot.GroupId, snapshot.TagId);
    }

    private static string GetErrorKey(RuntimeErrorDetail error)
    {
        return string.Join("|",
            error.Category ?? string.Empty,
            error.ChannelId ?? string.Empty,
            error.DeviceId ?? string.Empty,
            error.GroupId ?? string.Empty,
            error.TagId ?? string.Empty,
            error.Message ?? string.Empty,
            error.Timestamp.Ticks.ToString());
    }
}
