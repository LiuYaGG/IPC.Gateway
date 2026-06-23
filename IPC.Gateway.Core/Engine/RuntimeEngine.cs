/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：RuntimeEngine
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Engine
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
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Api;
using IPC.Runtime.Cleaning;
using IPC.Runtime.Configuration;
using IPC.Runtime.Indexing;
using IPC.Runtime.Scaling;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    
    
    
    
    
    
    
    
    
    public sealed class RuntimeEngine : IRuntimeService
    {
        private readonly object _syncRoot;
        private readonly Dictionary<string, DeviceRuntimeState> _deviceStatesById;
        private readonly Dictionary<string, TagValueSnapshot> _snapshotsByPath;
        private readonly Dictionary<string, DateTime> _nextReadUtcByTagId;
        private readonly object _queueSyncRoot;
        private readonly Queue<DeviceRuntimeState> _pendingDevicePolls;
        private readonly HashSet<string> _pendingDeviceIds;
        private readonly Semaphore _devicePollSemaphore;
        private readonly string _isolationStrategy;
        private readonly int _maxConcurrentDevicePolls;
        private readonly int _schedulerIntervalMs;
        private readonly int _devicePollQueueLimit;
        private readonly bool _backpressureEnabled;
        private readonly int _queueHighWatermarkPercent;
        private readonly int _queueLowWatermarkPercent;
        private readonly int _queueHighWatermarkCount;
        private readonly int _queueLowWatermarkCount;
        private readonly int _backpressureDelayMs;
        private readonly int _maxDevicePollsQueuedPerSchedulerTick;
        private readonly CircuitBreakerOptions _protocolDriverCircuitBreakerOptions;
        private readonly int _slowPollThresholdMs;
        private readonly int _pollTimeoutMs;
        private readonly int _minReconnectDelayMs;
        private readonly int _maxReconnectDelayMs;
        private readonly RuntimeErrorTimeline _runtimeEvents;
        private long _nextTaskId;
        private long _totalPollTasksQueued;
        private long _totalPollTasksStarted;
        private long _totalPollTasksCompleted;
        private long _totalPollTasksFailed;
        private long _totalPollTasksTimedOut;
        private long _totalPollTasksSlow;
        private long _totalPollTasksRejected;
        private long _totalPollTasksBackpressureThrottled;
        private long _totalPollTasksRateLimited;
        private long _totalReadTimeouts;
        private int _runningPollTaskCount;
        private int _maxObservedPendingCount;
        private int _backpressureActive;
        private int _nextScheduleDeviceIndex;
        private DateTime _lastTimeoutTime;
        private string _lastTimeoutDeviceName;
        private string _lastTimeoutMessage;
        private DateTime _lastBackpressureTime;
        private string _lastBackpressureMessage;

        private ProjectConfig? _config;
        private TagRuntimeIndex? _index;
        private Timer? _timer;
        private int _isScheduling;
        private int _runtimeGeneration;
        private bool _disposed;

        public RuntimeEngine()
            : this(new RuntimeSchedulerOptions())
        {
        }

        public RuntimeEngine(RuntimeSchedulerOptions schedulerOptions)
        {
            RuntimeSchedulerOptions options = (schedulerOptions ?? new RuntimeSchedulerOptions()).Normalize();
            _syncRoot = new object();
            _deviceStatesById = new Dictionary<string, DeviceRuntimeState>();
            _snapshotsByPath = new Dictionary<string, TagValueSnapshot>();
            _nextReadUtcByTagId = new Dictionary<string, DateTime>();
            _queueSyncRoot = new object();
            _pendingDevicePolls = new Queue<DeviceRuntimeState>();
            _pendingDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _isolationStrategy = options.IsolationStrategy;
            _maxConcurrentDevicePolls = options.MaxConcurrentDevicePolls;
            _schedulerIntervalMs = options.SchedulerIntervalMs;
            _devicePollQueueLimit = options.DevicePollQueueLimit;
            _backpressureEnabled = options.BackpressureEnabled;
            _queueHighWatermarkPercent = options.QueueHighWatermarkPercent;
            _queueLowWatermarkPercent = options.QueueLowWatermarkPercent;
            _queueHighWatermarkCount = CalculateWatermarkCount(_devicePollQueueLimit, _queueHighWatermarkPercent);
            _queueLowWatermarkCount = Math.Min(_queueHighWatermarkCount - 1, CalculateWatermarkCount(_devicePollQueueLimit, _queueLowWatermarkPercent));
            if (_queueLowWatermarkCount < 0)
                _queueLowWatermarkCount = 0;
            _backpressureDelayMs = options.BackpressureDelayMs;
            _maxDevicePollsQueuedPerSchedulerTick = options.MaxDevicePollsQueuedPerSchedulerTick;
            _protocolDriverCircuitBreakerOptions = options.ProtocolDriverCircuitBreaker;
            _slowPollThresholdMs = options.SlowPollThresholdMs;
            _pollTimeoutMs = options.PollTimeoutMs;
            _minReconnectDelayMs = 1000;
            _maxReconnectDelayMs = 30000;
            _runtimeEvents = new RuntimeErrorTimeline(100);
            _devicePollSemaphore = new Semaphore(_maxConcurrentDevicePolls, _maxConcurrentDevicePolls);
            _lastTimeoutDeviceName = string.Empty;
            _lastTimeoutMessage = string.Empty;
            _lastBackpressureMessage = string.Empty;
        }

        public event EventHandler<TagValueChangedEventArgs>? TagValueChanged;

        public bool IsRunning
        {
            get { return _timer != null; }
        }

        public int MaxConcurrentDevicePolls
        {
            get { return _maxConcurrentDevicePolls; }
        }

        public void Start(ProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            Stop();
            ProjectConfig runtimeConfig = ProjectConfigCloner.Clone(config) ?? throw new InvalidOperationException("Project configuration clone failed.");
            _runtimeEvents.Clear();

            lock (_syncRoot)
            {
                _config = runtimeConfig;
                _index = new TagRuntimeIndex(runtimeConfig);
                _snapshotsByPath.Clear();
                _nextReadUtcByTagId.Clear();
                _deviceStatesById.Clear();
                ClearPollingQueue();
                ResetSchedulerStats();
                InitializeDeviceStates(runtimeConfig);
                InitializeSnapshots(runtimeConfig);
                int runtimeGeneration = Interlocked.Increment(ref _runtimeGeneration);
                _timer = new Timer(SchedulePolls, runtimeGeneration, 0, _schedulerIntervalMs);
            }
        }

        public void Stop()
        {
            Interlocked.Increment(ref _runtimeGeneration);

            Timer? timer;
            lock (_syncRoot)
            {
                timer = _timer;
                _timer = null;
            }

            if (timer != null)
                timer.Dispose();

            List<DeviceRuntimeState> states;
            lock (_syncRoot)
            {
                states = new List<DeviceRuntimeState>(_deviceStatesById.Values);
            }

            for (int i = 0; i < states.Count; i++)
            {
                DeviceRuntimeState state = states[i];
                lock (state.SyncRoot)
                {
                    try
                    {
                        if (state.Client != null)
                            state.Client.Dispose();
                    }
                    catch
                    {
                    }

                    state.Client = null;
                    state.IsPolling = false;
                    state.IsQueued = false;
                }
            }

            lock (_syncRoot)
            {
                _deviceStatesById.Clear();
                _nextReadUtcByTagId.Clear();
            }

            ClearPollingQueue();
        }

        public bool TryGetSnapshot(string deviceName, string groupName, string tagName, out TagValueSnapshot? snapshot)
        {
            string key = TagPath.Build(deviceName, groupName, tagName);
            lock (_syncRoot)
            {
                TagValueSnapshot? current;
                if (_snapshotsByPath.TryGetValue(key, out current) && current != null)
                {
                    snapshot = current.Clone();
                    return true;
                }
            }

            snapshot = null;
            return false;
        }

        public IList<TagValueSnapshot> GetSnapshots()
        {
            List<TagValueSnapshot> snapshots = new List<TagValueSnapshot>();
            lock (_syncRoot)
            {
                foreach (TagValueSnapshot snapshot in _snapshotsByPath.Values)
                {
                    if (snapshot != null)
                        snapshots.Add(snapshot.Clone());
                }
            }

            return snapshots;
        }

        public void RestoreSnapshots(IList<TagValueSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                return;

            lock (_syncRoot)
            {
                Dictionary<string, string> pathByTagId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, TagValueSnapshot> pair in _snapshotsByPath)
                {
                    TagValueSnapshot current = pair.Value;
                    if (current != null && !string.IsNullOrWhiteSpace(current.TagId))
                        pathByTagId[current.TagId] = pair.Key;
                }

                for (int i = 0; i < snapshots.Count; i++)
                {
                    TagValueSnapshot persisted = snapshots[i];
                    if (persisted == null)
                        continue;

                    string key = string.Empty;
                    if (!string.IsNullOrWhiteSpace(persisted.TagId))
                    {
                        string? restoredKey;
                        if (pathByTagId.TryGetValue(persisted.TagId, out restoredKey))
                            key = restoredKey ?? string.Empty;
                    }
                    if (string.IsNullOrWhiteSpace(key))
                        key = TagPath.Build(persisted.DeviceName, persisted.GroupName, persisted.TagName);

                    TagValueSnapshot? current;
                    if (!_snapshotsByPath.TryGetValue(key, out current) || current == null)
                        continue;

                    if (CanRestoreSnapshot(current, persisted))
                        _snapshotsByPath[key] = MergeRestoredSnapshot(current, persisted);
                }
            }
        }

        public IList<DeviceRuntimeStatus> GetDeviceStatuses()
        {
            List<DeviceRuntimeStatus> statuses = new List<DeviceRuntimeStatus>();
            List<DeviceRuntimeState> states;
            lock (_syncRoot)
            {
                states = new List<DeviceRuntimeState>(_deviceStatesById.Values);
            }

            for (int i = 0; i < states.Count; i++)
            {
                DeviceRuntimeState state = states[i];
                if (state == null)
                    continue;

                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(state.SyncRoot, 0, ref lockTaken);
                    if (lockTaken)
                        statuses.Add(CreateDeviceRuntimeStatus(state));
                    else
                        statuses.Add(CreateBusyDeviceRuntimeStatus(state));
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                }
            }

            return statuses;
        }

        public RuntimeSchedulerStatus GetSchedulerStatus()
        {
            RuntimePollingQueueStatus queueStatus;
            lock (_queueSyncRoot)
            {
                int pendingCount = _pendingDevicePolls.Count;
                queueStatus = new RuntimePollingQueueStatus
                {
                    PendingCount = pendingCount,
                    RunningCount = Volatile.Read(ref _runningPollTaskCount),
                    QueueLimit = _devicePollQueueLimit,
                    HighWatermark = _queueHighWatermarkCount,
                    LowWatermark = _queueLowWatermarkCount,
                    UtilizationPercent = _devicePollQueueLimit <= 0 ? 0D : Math.Round(pendingCount * 100D / _devicePollQueueLimit, 2),
                    BackpressureActive = Volatile.Read(ref _backpressureActive) == 1,
                    AvailableWorkers = Math.Max(0, _maxConcurrentDevicePolls - Volatile.Read(ref _runningPollTaskCount)),
                    RejectedCount = Interlocked.Read(ref _totalPollTasksRejected),
                    BackpressureThrottledCount = Interlocked.Read(ref _totalPollTasksBackpressureThrottled),
                    RateLimitedCount = Interlocked.Read(ref _totalPollTasksRateLimited),
                    MaxObservedPendingCount = Volatile.Read(ref _maxObservedPendingCount),
                    LastBackpressureTime = _lastBackpressureTime,
                    LastBackpressureMessage = _lastBackpressureMessage ?? string.Empty
                };
            }

            RuntimeTimeoutStats timeoutStats;
            lock (_syncRoot)
            {
                timeoutStats = new RuntimeTimeoutStats
                {
                    PollTimeoutCount = Interlocked.Read(ref _totalPollTasksTimedOut),
                    ReadTimeoutCount = Interlocked.Read(ref _totalReadTimeouts),
                    LastTimeoutTime = _lastTimeoutTime,
                    LastTimeoutDeviceName = _lastTimeoutDeviceName ?? string.Empty,
                    LastTimeoutMessage = _lastTimeoutMessage ?? string.Empty
                };
            }

            RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
            {
                IsolationStrategy = _isolationStrategy,
                MaxConcurrentDevicePolls = _maxConcurrentDevicePolls,
                SchedulerIntervalMs = _schedulerIntervalMs,
                BackpressureEnabled = _backpressureEnabled,
                BackpressureActive = Volatile.Read(ref _backpressureActive) == 1,
                QueueHighWatermark = _queueHighWatermarkCount,
                QueueLowWatermark = _queueLowWatermarkCount,
                BackpressureDelayMs = _backpressureDelayMs,
                MaxDevicePollsQueuedPerSchedulerTick = _maxDevicePollsQueuedPerSchedulerTick,
                SlowPollThresholdMs = _slowPollThresholdMs,
                PollTimeoutMs = _pollTimeoutMs,
                TotalQueued = Interlocked.Read(ref _totalPollTasksQueued),
                TotalStarted = Interlocked.Read(ref _totalPollTasksStarted),
                TotalCompleted = Interlocked.Read(ref _totalPollTasksCompleted),
                TotalFailed = Interlocked.Read(ref _totalPollTasksFailed),
                TotalSlow = Interlocked.Read(ref _totalPollTasksSlow),
                TotalBackpressureThrottled = Interlocked.Read(ref _totalPollTasksBackpressureThrottled),
                TotalRateLimited = Interlocked.Read(ref _totalPollTasksRateLimited),
                Queue = queueStatus,
                Timeout = timeoutStats,
                Tasks = GetPollingTaskStatuses()
            };

            RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);
            status.HealthStatus = health.Status;
            status.HealthMessage = health.Message;
            return status;
        }

        public IList<RuntimeErrorDetail> GetRecentErrors(int maxCount)
        {
            int take = maxCount <= 0 ? 20 : maxCount;
            List<RuntimeErrorDetail> errors = new List<RuntimeErrorDetail>();

            List<DeviceRuntimeState> states;
            List<TagValueSnapshot> snapshots;
            lock (_syncRoot)
            {
                states = new List<DeviceRuntimeState>(_deviceStatesById.Values);
                snapshots = new List<TagValueSnapshot>();
                foreach (TagValueSnapshot snapshot in _snapshotsByPath.Values)
                {
                    if (snapshot != null)
                        snapshots.Add(snapshot.Clone());
                }
            }

            for (int i = 0; i < states.Count; i++)
            {
                DeviceRuntimeState state = states[i];
                if (state == null)
                    continue;

                string message;
                DateTime timestamp;
                string deviceName;

                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(state.SyncRoot, 0, ref lockTaken);
                    if (lockTaken)
                    {
                        message = state.LastConnectionError;
                        timestamp = state.LastConnectionErrorTime;
                        deviceName = state.Config == null ? string.Empty : state.Config.Name;
                    }
                    else
                    {
                        deviceName = state.Config == null ? string.Empty : state.Config.Name;
                        message = "设备正在连接或重连中，当前状态稍后刷新。";
                        timestamp = DateTime.Now;
                    }
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    errors.Add(new RuntimeErrorDetail
                    {
                        Category = "DeviceConnection",
                        DeviceName = deviceName,
                        Message = message,
                        Suggestion = SuggestDeviceConnectionCause(message),
                        Source = "RuntimeEngine",
                        Timestamp = timestamp == DateTime.MinValue ? DateTime.Now : timestamp
                    });
                }
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                TagValueSnapshot snapshot = snapshots[i];
                if (snapshot == null)
                    continue;

                if (snapshot.Quality == TagQuality.ReadError)
                {
                    errors.Add(new RuntimeErrorDetail
                    {
                        Category = "TagRead",
                        DeviceName = snapshot.DeviceName,
                        GroupName = snapshot.GroupName,
                        TagName = snapshot.TagName,
                        Message = snapshot.ErrorMessage,
                        Suggestion = SuggestTagReadCause(snapshot.ErrorMessage),
                        Source = snapshot.Source,
                        Timestamp = snapshot.Timestamp
                    });
                }
                else if (snapshot.Quality == TagQuality.NotConnected)
                {
                    errors.Add(new RuntimeErrorDetail
                    {
                        Category = "DeviceConnection",
                        DeviceName = snapshot.DeviceName,
                        GroupName = snapshot.GroupName,
                        TagName = snapshot.TagName,
                        Message = snapshot.ErrorMessage,
                        Suggestion = SuggestDeviceConnectionCause(snapshot.ErrorMessage),
                        Source = snapshot.Source,
                        Timestamp = snapshot.Timestamp
                    });
                }
            }

            errors.AddRange(_runtimeEvents.GetRecent(take));

            errors.Sort(delegate(RuntimeErrorDetail left, RuntimeErrorDetail right)
            {
                return right.Timestamp.CompareTo(left.Timestamp);
            });

            if (errors.Count > take)
                errors.RemoveRange(take, errors.Count - take);
            return errors;
        }

        public ReadTagResponse ReadCached(ReadTagRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            TagValueSnapshot? snapshot;
            if (!TryGetSnapshot(request.DeviceName, request.GroupName, request.TagName, out snapshot) || snapshot == null)
            {
                return new ReadTagResponse
                {
                    Success = false,
                    DeviceName = request.DeviceName,
                    GroupName = request.GroupName,
                    TagName = request.TagName,
                    Quality = TagQuality.NotFound.ToString(),
                    ErrorMessage = "Tag was not found."
                };
            }

            return ReadTagResponse.FromSnapshot(snapshot);
        }

        public ReadTagsResponse ReadCached(ReadTagsRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            ReadTagsResponse response = new ReadTagsResponse();
            response.Success = true;

            if (request.Tags == null || request.Tags.Count == 0)
            {
                response.Success = false;
                response.Results.Add(CreateErrorResponse(string.Empty, string.Empty, string.Empty, "No tags were requested."));
                return response;
            }

            for (int i = 0; i < request.Tags.Count; i++)
            {
                TagPathDto? path = request.Tags[i];
                ReadTagRequest itemRequest = new ReadTagRequest
                {
                    DeviceName = path == null ? string.Empty : path.DeviceName,
                    GroupName = path == null ? string.Empty : path.GroupName,
                    TagName = path == null ? string.Empty : path.TagName
                };
                ReadTagsResponse itemResponse = QueryCached(itemRequest);
                for (int r = 0; r < itemResponse.Results.Count; r++)
                    response.Results.Add(itemResponse.Results[r]);
                if (!itemResponse.Success)
                    response.Success = false;
            }

            return response;
        }

        public ReadTagsResponse QueryCached(ReadTagRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            bool hasDevice = !string.IsNullOrWhiteSpace(request.DeviceName);
            bool hasGroup = !string.IsNullOrWhiteSpace(request.GroupName);
            bool hasTag = !string.IsNullOrWhiteSpace(request.TagName);

            if (!hasDevice)
                return CreateErrorResponseList(request.DeviceName, request.GroupName, request.TagName, "DeviceName is required.");

            if (hasGroup && hasTag)
                return CreateResponseList(ReadCached(request));

            if (!hasGroup && hasTag)
                return ReadTagByDeviceCached(request.DeviceName, request.TagName);

            if (hasGroup && !hasTag)
                return ReadGroupCached(request.DeviceName, request.GroupName);

            return CreateErrorResponseList(request.DeviceName, request.GroupName, request.TagName, "GroupName or TagName is required.");
        }

        public ReadTagsResponse ReadTagByDeviceCached(string deviceName, string tagName)
        {
            string normalizedDeviceName = TagPath.Normalize(deviceName);
            string normalizedTagName = TagPath.Normalize(tagName);

            lock (_syncRoot)
            {
                foreach (TagValueSnapshot snapshot in _snapshotsByPath.Values)
                {
                    if (TagPath.Normalize(snapshot.DeviceName) == normalizedDeviceName &&
                        string.IsNullOrEmpty(TagPath.Normalize(snapshot.GroupName)) &&
                        TagPath.Normalize(snapshot.TagName) == normalizedTagName)
                    {
                        return CreateResponseList(ReadTagResponse.FromSnapshot(snapshot.Clone()));
                    }
                }
            }

            return CreateErrorResponseList(deviceName, string.Empty, tagName, "Device-level tag was not found under the device.");
        }

        public ReadTagsResponse ReadGroupCached(string deviceName, string groupName)
        {
            ReadTagsResponse response = new ReadTagsResponse();
            string normalizedDeviceName = TagPath.Normalize(deviceName);
            string normalizedGroupName = TagPath.Normalize(groupName);

            lock (_syncRoot)
            {
                foreach (TagValueSnapshot snapshot in _snapshotsByPath.Values)
                {
                    if (TagPath.Normalize(snapshot.DeviceName) == normalizedDeviceName &&
                        TagPath.Normalize(snapshot.GroupName) == normalizedGroupName)
                    {
                        response.Results.Add(ReadTagResponse.FromSnapshot(snapshot.Clone()));
                    }
                }
            }

            if (response.Results.Count == 0)
            {
                response.Success = false;
                response.Results.Add(CreateErrorResponse(deviceName, groupName, string.Empty, "Group was not found under the device, or it contains no tags."));
                return response;
            }

            response.Success = true;
            for (int i = 0; i < response.Results.Count; i++)
            {
                if (!response.Results[i].Success)
                {
                    response.Success = false;
                    break;
                }
            }

            return response;
        }

        public WriteTagResponse WriteTag(WriteTagRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            DeviceConfig? device;
            GroupConfig? group;
            TagConfig? tag;
            if (!TryFindWritableTag(request, out device, out group, out tag) || device == null || tag == null)
                return CreateWriteErrorResponse(request, "Tag was not found.");

            if (!device.Enabled)
                return CreateWriteErrorResponse(request, "Device is disabled.");
            if (group != null && !group.Enabled)
                return CreateWriteErrorResponse(request, "Group is disabled.");
            if (!tag.Enabled)
                return CreateWriteErrorResponse(request, "Tag is disabled.");
            if (!CanWrite(tag))
                return CreateWriteErrorResponse(request, "Tag is read-only.");

            PlcDataType requestDataType;
            if (!TryParseDataType(request.DataType, out requestDataType))
                return CreateWriteErrorResponse(request, "DataType is required and must be a valid PlcDataType name.");

            if (requestDataType != tag.DataType)
            {
                return CreateWriteErrorResponse(
                    request,
                    "DataType does not match tag configuration. Requested: " + requestDataType + ", Configured: " + tag.DataType + ".");
            }

            string valueText = BuildWriteValueText(request, tag);
            string validationError;
            if (!TryValidateWriteValue(tag.DataType, valueText, tag.ElementCount, out validationError))
                return CreateWriteErrorResponse(request, validationError);

            DeviceRuntimeState? writeDeviceState = null;
            try
            {
                IPlcClient? client;
                writeDeviceState = GetDeviceState(device);
                if (writeDeviceState == null)
                    return CreateWriteErrorResponse(request, "Device runtime state was not found.");

                lock (writeDeviceState.SyncRoot)
                {
                    if (!TryEnsureClient(writeDeviceState, out client) || client == null)
                        return CreateWriteErrorResponse(request, "Device is not connected.");

                    WriteTagValue(client, writeDeviceState.Config, tag, valueText);
                    writeDeviceState.ProtocolCircuitBreaker.RecordSuccess();

                    bool refreshSucceeded;
                    if (CanRead(tag) && !ReadTag(client, writeDeviceState, group, tag, out refreshSucceeded))
                        return CreateWriteErrorResponse(request, "Device communication failed while refreshing current value.");
                }

                TagValueSnapshot? snapshot;
                ReadTagResponse? currentValue = null;
                if (TryGetSnapshot(device.Name, group == null ? string.Empty : group.Name, tag.Name, out snapshot) && snapshot != null)
                    currentValue = ReadTagResponse.FromSnapshot(snapshot);

                return new WriteTagResponse
                {
                    Success = true,
                    DeviceName = device.Name,
                    GroupName = group == null ? string.Empty : group.Name,
                    TagName = tag.Name,
                    DataType = tag.DataType.ToString(),
                    Quality = TagQuality.Good.ToString(),
                    Timestamp = DateTime.Now,
                    CurrentValue = currentValue ?? new ReadTagResponse()
                };
            }
            catch (Exception ex)
            {
                if (writeDeviceState != null)
                    writeDeviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);

                if (writeDeviceState != null && IsCommunicationException(ex))
                {
                    lock (writeDeviceState.SyncRoot)
                    {
                        DropDeviceConnection(writeDeviceState, ex.Message);
                    }
                }

                return CreateWriteErrorResponse(request, ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Stop();
            _disposed = true;
        }

        private void InitializeSnapshots(ProjectConfig config)
        {
            if (config.Devices == null)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                InitializeDeviceTags(device);

                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || group.Tags == null)
                        continue;

                    group.DeviceId = device.Id;
                    for (int t = 0; t < group.Tags.Count; t++)
                    {
                        TagConfig tag = group.Tags[t];
                        if (tag == null)
                            continue;

                        tag.DeviceId = device.Id;
                        tag.GroupId = group.Id;
                        if (tag.Scaling == null)
                            tag.Scaling = ScalingConfig.Default();

                        string path = TagPath.Build(device.Name, group.Name, tag.Name);
                        _snapshotsByPath[path] = CanRead(tag)
                            ? CreateSnapshot(device, group, tag, TagQuality.Unknown, "Waiting for first scan.")
                            : CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only.");
                        _nextReadUtcByTagId[tag.Id] = DateTime.UtcNow;
                    }
                }
            }
        }

        private void InitializeDeviceStates(ProjectConfig config)
        {
            if (config == null || config.Devices == null)
                return;

            for (int i = 0; i < config.Devices.Count; i++)
            {
                DeviceConfig device = config.Devices[i];
                if (device == null)
                    continue;

                _deviceStatesById[device.Id] = new DeviceRuntimeState(device, _protocolDriverCircuitBreakerOptions);
            }
        }

        private void InitializeDeviceTags(DeviceConfig device)
        {
            if (device.Tags == null)
                return;

            for (int t = 0; t < device.Tags.Count; t++)
            {
                TagConfig tag = device.Tags[t];
                if (tag == null)
                    continue;

                tag.DeviceId = device.Id;
                tag.GroupId = string.Empty;
                if (tag.Scaling == null)
                    tag.Scaling = ScalingConfig.Default();

                string path = TagPath.Build(device.Name, string.Empty, tag.Name);
                _snapshotsByPath[path] = CanRead(tag)
                    ? CreateSnapshot(device, null, tag, TagQuality.Unknown, "Waiting for first scan.")
                    : CreateSnapshot(device, null, tag, TagQuality.AccessDenied, "Tag is write-only.");
                _nextReadUtcByTagId[tag.Id] = DateTime.UtcNow;
            }
        }

        private void SchedulePolls(object? state)
        {
            int runtimeGeneration = state is int ? (int)state : 0;
            if (!IsCurrentGeneration(runtimeGeneration))
                return;

            if (Interlocked.Exchange(ref _isScheduling, 1) == 1)
                return;

            try
            {
                ProjectConfig? config;
                lock (_syncRoot)
                {
                    if (!IsCurrentGeneration(runtimeGeneration))
                        return;

                    config = _config;
                }

                if (config == null || config.Devices == null)
                    return;

                TryStartQueuedDevicePolls(runtimeGeneration);

                DateTime now = DateTime.UtcNow;
                if (IsBackpressureAdmissionPaused(now))
                    return;

                int deviceCount = config.Devices.Count;
                if (deviceCount <= 0)
                    return;

                int startIndex = GetNextScheduleStartIndex(deviceCount);
                int nextStartIndex = startIndex;
                int queuedThisTick = 0;

                for (int offset = 0; offset < deviceCount; offset++)
                {
                    int d = (startIndex + offset) % deviceCount;
                    nextStartIndex = (d + 1) % deviceCount;
                    DeviceConfig device = config.Devices[d];
                    if (device == null)
                        continue;

                    DeviceRuntimeState? deviceState = GetDeviceState(device);
                    if (deviceState == null)
                        continue;

                    if (!device.Enabled)
                    {
                        MarkDevice(device, TagQuality.Disabled, "Device is disabled.");
                        ScheduleDeviceNextPoll(deviceState, now);
                        continue;
                    }

                    if (!IsDeviceDue(deviceState, now))
                        continue;

                    if (!HasDueReadableTags(device, now))
                        continue;

                    if (queuedThisTick >= _maxDevicePollsQueuedPerSchedulerTick)
                    {
                        RegisterRateLimitedAdmission(deviceState, now);
                        break;
                    }

                    PollAdmissionResult admission = QueueDevicePoll(deviceState, runtimeGeneration);
                    if (admission == PollAdmissionResult.Queued)
                    {
                        queuedThisTick++;
                        continue;
                    }

                    if (admission == PollAdmissionResult.BackpressureThrottled ||
                        admission == PollAdmissionResult.QueueRejected)
                    {
                        break;
                    }
                }

                SetNextScheduleStartIndex(nextStartIndex, deviceCount);
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _isScheduling, 0);
            }
        }

        private PollAdmissionResult QueueDevicePoll(DeviceRuntimeState deviceState, int runtimeGeneration)
        {
            if (!IsCurrentGeneration(runtimeGeneration))
                return PollAdmissionResult.Skipped;

            DateTime now = DateTime.UtcNow;
            string deviceId = GetDeviceStateKey(deviceState);
            long taskId;

            lock (_queueSyncRoot)
            {
                if (_pendingDeviceIds.Contains(deviceId))
                    return PollAdmissionResult.Skipped;

                lock (deviceState.SyncRoot)
                {
                    if (deviceState.IsPolling || deviceState.IsQueued)
                        return PollAdmissionResult.Skipped;

                    if (!IsDeviceDueUnsafe(deviceState, now))
                        return PollAdmissionResult.Skipped;
                }

                string backpressureMessage;
                if (IsBackpressureBlockedNoLock(_pendingDevicePolls.Count, now, out backpressureMessage))
                {
                    Interlocked.Increment(ref _totalPollTasksBackpressureThrottled);
                    DeferDeviceAdmission(deviceState, now, "BackpressureDelayed", backpressureMessage, _backpressureDelayMs);
                    return PollAdmissionResult.BackpressureThrottled;
                }

                if (_pendingDevicePolls.Count >= _devicePollQueueLimit)
                {
                    Interlocked.Increment(ref _totalPollTasksRejected);
                    DeferDeviceAdmission(
                        deviceState,
                        now,
                        "QueueRejected",
                        "Polling queue is full; admission is delayed for " + _backpressureDelayMs + " ms.",
                        _backpressureDelayMs);
                    return PollAdmissionResult.QueueRejected;
                }

                taskId = Interlocked.Increment(ref _nextTaskId);
                lock (deviceState.SyncRoot)
                {
                    deviceState.IsQueued = true;
                    deviceState.CurrentTaskId = taskId;
                    deviceState.CurrentTaskQueuedUtc = now;
                    deviceState.CurrentTaskStartedUtc = DateTime.MinValue;
                    deviceState.CurrentTaskFinishedUtc = DateTime.MinValue;
                    deviceState.LastTaskStatus = "Queued";
                    deviceState.LastTaskError = string.Empty;
                }

                _pendingDevicePolls.Enqueue(deviceState);
                _pendingDeviceIds.Add(deviceId);
                Interlocked.Increment(ref _totalPollTasksQueued);
                if (_pendingDevicePolls.Count > _maxObservedPendingCount)
                    _maxObservedPendingCount = _pendingDevicePolls.Count;
            }

            TryStartQueuedDevicePolls(runtimeGeneration);
            return PollAdmissionResult.Queued;
        }

        private void TryStartQueuedDevicePolls(int runtimeGeneration)
        {
            while (IsCurrentGeneration(runtimeGeneration))
            {
                if (!_devicePollSemaphore.WaitOne(0))
                    return;

                DeviceRuntimeState? deviceState = null;
                lock (_queueSyncRoot)
                {
                    DateTime now = DateTime.UtcNow;
                    while (_pendingDevicePolls.Count > 0)
                    {
                        DeviceRuntimeState candidate = _pendingDevicePolls.Dequeue();
                        _pendingDeviceIds.Remove(GetDeviceStateKey(candidate));

                        lock (candidate.SyncRoot)
                        {
                            candidate.IsQueued = false;
                            if (candidate.IsPolling || !IsDeviceDueUnsafe(candidate, now))
                            {
                                candidate.LastTaskStatus = "Skipped";
                                continue;
                            }

                            candidate.IsPolling = true;
                            candidate.CurrentTaskStartedUtc = now;
                            candidate.LastTaskStatus = "Running";
                            candidate.LastTaskError = string.Empty;
                            deviceState = candidate;
                            break;
                        }
                    }
                }

                if (deviceState == null)
                {
                    _devicePollSemaphore.Release();
                    return;
                }

                Interlocked.Increment(ref _runningPollTaskCount);
                Interlocked.Increment(ref _totalPollTasksStarted);
                ThreadPool.QueueUserWorkItem(delegate
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    Exception? pollError = null;
                    try
                    {
                        if (IsCurrentGeneration(runtimeGeneration))
                            PollDevice(deviceState, runtimeGeneration);
                    }
                    catch (Exception ex)
                    {
                        pollError = ex;
                        HandleUnexpectedPollError(deviceState, ex);
                    }
                    finally
                    {
                        stopwatch.Stop();
                        CompleteDevicePollTask(deviceState, runtimeGeneration, stopwatch.ElapsedMilliseconds, pollError);
                        _devicePollSemaphore.Release();
                        Interlocked.Decrement(ref _runningPollTaskCount);
                        TryStartQueuedDevicePolls(runtimeGeneration);
                    }
                });
            }
        }

        private void CompleteDevicePollTask(DeviceRuntimeState deviceState, int runtimeGeneration, long durationMs, Exception? pollError)
        {
            bool isTimeout = durationMs >= _pollTimeoutMs;
            bool isSlow = durationMs >= _slowPollThresholdMs;
            string status = pollError != null ? "Failed" : isTimeout ? "TimedOut" : isSlow ? "Slow" : "Completed";
            string message = pollError == null ? string.Empty : pollError.Message ?? string.Empty;
            DateTime finishedUtc = DateTime.UtcNow;

            lock (deviceState.SyncRoot)
            {
                deviceState.IsPolling = false;
                deviceState.CurrentTaskFinishedUtc = finishedUtc;
                deviceState.LastTaskDurationMs = durationMs;
                deviceState.LastTaskStatus = status;
                deviceState.LastTaskError = message;
                if (isSlow)
                    deviceState.SlowPollCount++;
                if (isTimeout)
                    deviceState.TimeoutCount++;
            }

            Interlocked.Increment(ref _totalPollTasksCompleted);
            if (pollError != null)
                Interlocked.Increment(ref _totalPollTasksFailed);
            if (isSlow)
                Interlocked.Increment(ref _totalPollTasksSlow);
            if (isTimeout)
                RegisterPollTimeout(deviceState, durationMs);

            if (IsCurrentGeneration(runtimeGeneration))
                ScheduleDeviceNextPoll(deviceState, DateTime.UtcNow);
        }

        private void PollDevice(DeviceRuntimeState deviceState, int runtimeGeneration)
        {
            if (!IsCurrentGeneration(runtimeGeneration))
                return;

            DeviceConfig device = deviceState.Config;
            DateTime now = DateTime.UtcNow;

            lock (deviceState.SyncRoot)
            {
                deviceState.LastPollTime = DateTime.Now;
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                if (DateTime.UtcNow < deviceState.NextReconnectUtc)
                    return;

                IPlcClient? client = null;
                bool deviceConnected = TryEnsureClient(deviceState, out client);

                PollDeviceTags(deviceState, client, deviceConnected, now, runtimeGeneration);

                if (device.Groups == null)
                    return;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    if (!IsCurrentGeneration(runtimeGeneration))
                        return;

                    GroupConfig group = device.Groups[g];
                    if (group == null || group.Tags == null)
                        continue;

                    if (!group.Enabled)
                    {
                        MarkGroup(device, group, TagQuality.Disabled, "Group is disabled.");
                        continue;
                    }

                    for (int t = 0; t < group.Tags.Count; t++)
                    {
                        if (!IsCurrentGeneration(runtimeGeneration))
                            return;

                        TagConfig tag = group.Tags[t];
                        if (tag == null)
                            continue;

                        if (!IsDue(tag, now))
                            continue;

                        if (!tag.Enabled)
                        {
                            ScheduleNextRead(device, group, tag, now, false);
                            UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.Disabled, "Tag is disabled."));
                            continue;
                        }

                        if (!CanRead(tag))
                        {
                            ScheduleNextRead(device, group, tag, now, false);
                            UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only."));
                            continue;
                        }

                        if (!deviceConnected || client == null)
                        {
                            ScheduleNextRead(device, group, tag, now, true);
                            UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.NotConnected, "Device is not connected."));
                            continue;
                        }

                        bool readSucceeded;
                        if (!ReadTag(client, deviceState, group, tag, out readSucceeded))
                        {
                            ScheduleNextRead(device, group, tag, now, true);
                            deviceConnected = false;
                            client = null;
                        }
                        else
                        {
                            ScheduleNextRead(device, group, tag, now, !readSucceeded);
                        }
                    }
                }
            }
        }

        private DeviceRuntimeState? GetDeviceState(DeviceConfig? device)
        {
            if (device == null)
                return null;

            lock (_syncRoot)
            {
                DeviceRuntimeState? state;
                if (!_deviceStatesById.TryGetValue(device.Id, out state) || state == null)
                {
                    state = new DeviceRuntimeState(device, _protocolDriverCircuitBreakerOptions);
                    _deviceStatesById[device.Id] = state;
                }
                return state;
            }
        }

        private bool HasDueReadableTags(DeviceConfig device, DateTime now)
        {
            if (device.Tags != null)
            {
                for (int i = 0; i < device.Tags.Count; i++)
                {
                    TagConfig tag = device.Tags[i];
                    if (tag != null && tag.Enabled && CanRead(tag) && IsDue(tag, now))
                        return true;
                }
            }

            if (device.Groups == null)
                return false;

            for (int g = 0; g < device.Groups.Count; g++)
            {
                GroupConfig group = device.Groups[g];
                if (group == null || !group.Enabled || group.Tags == null)
                    continue;

                for (int t = 0; t < group.Tags.Count; t++)
                {
                    TagConfig tag = group.Tags[t];
                    if (tag != null && tag.Enabled && CanRead(tag) && IsDue(tag, now))
                        return true;
                }
            }

            return false;
        }

        private bool TryEnsureClient(DeviceRuntimeState deviceState, out IPlcClient? client)
        {
            client = null;
            DeviceConfig device = deviceState.Config;

            if (!deviceState.ProtocolCircuitBreaker.CanExecute())
            {
                CircuitBreakerStatus breaker = deviceState.ProtocolCircuitBreaker.Snapshot();
                string message = "Protocol driver circuit breaker is open; device poll is degraded and skipped.";
                if (!string.IsNullOrWhiteSpace(breaker.LastFailureMessage))
                    message += " Last error: " + breaker.LastFailureMessage;
                deviceState.LastError = message;
                deviceState.LastConnectionError = message;
                deviceState.LastConnectionErrorTime = DateTime.Now;
                deviceState.NextReconnectUtc = breaker.NextRetryTime == DateTime.MinValue
                    ? DateTime.UtcNow.AddMilliseconds(GetDeviceFailureRetryDelayMs(device))
                    : breaker.NextRetryTime.ToUniversalTime();
                deviceState.NextPollUtc = deviceState.NextReconnectUtc;
                return false;
            }

            if (deviceState.Client != null && deviceState.Client.IsConnected)
            {
                client = deviceState.Client;
                return true;
            }

            if (DateTime.UtcNow < deviceState.NextReconnectUtc)
                return false;

            try
            {
                PlcConnectionOptions options = device.Connection ?? new PlcConnectionOptions();
                options.Protocol = device.Protocol;
                IPlcClient newClient = PlcClientFactory.Create(options);
                newClient.Connect();

                if (deviceState.Client != null)
                    deviceState.Client.Dispose();

                int recoveredFailureCount = deviceState.ConsecutiveFailures;
                string previousConnectionError = deviceState.LastConnectionError ?? string.Empty;
                deviceState.Client = newClient;
                if (recoveredFailureCount > 0)
                    RecordDeviceRecoveryEvent(deviceState, recoveredFailureCount, previousConnectionError);
                deviceState.ConsecutiveFailures = 0;
                deviceState.NextPollUtc = DateTime.MinValue;
                deviceState.NextReconnectUtc = DateTime.MinValue;
                deviceState.LastReconnectDelayMs = 0;
                deviceState.LastError = string.Empty;
                deviceState.LastSuccessTime = DateTime.Now;
                deviceState.ProtocolCircuitBreaker.RecordSuccess();

                client = newClient;
                return true;
            }
            catch (Exception ex)
            {
                if (deviceState.Client != null)
                {
                    deviceState.Client.Dispose();
                    deviceState.Client = null;
                }

                RegisterDeviceFailure(deviceState, ex.Message);
                deviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);
                return false;
            }
        }

        private void RegisterDeviceFailure(DeviceRuntimeState deviceState, string errorMessage)
        {
            deviceState.ConsecutiveFailures++;
            deviceState.LastError = errorMessage ?? string.Empty;
            deviceState.LastConnectionError = deviceState.LastError;
            deviceState.LastConnectionErrorTime = DateTime.Now;

            int delay = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(
                deviceState.ConsecutiveFailures,
                GetDeviceFailureRetryDelayMs(deviceState.Config),
                GetDeviceMaxFailureRetryDelayMs(deviceState.Config),
                GetDeviceReconnectJitterKey(deviceState.Config));

            deviceState.LastReconnectDelayMs = delay;
            deviceState.NextReconnectUtc = DateTime.UtcNow.AddMilliseconds(delay);
            deviceState.NextPollUtc = deviceState.NextReconnectUtc;
            RecordDeviceFailureEvent(deviceState, delay);
        }

        private static string GetDeviceReconnectJitterKey(DeviceConfig device)
        {
            if (device == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(device.Id))
                return device.Id;
            return device.Name ?? string.Empty;
        }

        private void RecordDeviceFailureEvent(DeviceRuntimeState? deviceState, int nextReconnectDelayMs)
        {
            DeviceConfig? device = deviceState == null ? null : deviceState.Config;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Device connection failed. Consecutive failures: {0}; next retry in {1} ms. Error: {2}",
                deviceState == null ? 0 : deviceState.ConsecutiveFailures,
                nextReconnectDelayMs,
                deviceState == null ? string.Empty : deviceState.LastConnectionError ?? string.Empty);

            _runtimeEvents.Add(new RuntimeErrorDetail
            {
                Category = "DeviceConnectionFailure",
                DeviceName = device == null ? string.Empty : device.Name ?? string.Empty,
                Message = message,
                Suggestion = SuggestDeviceConnectionCause(deviceState == null ? string.Empty : deviceState.LastConnectionError),
                Source = "RuntimeEngine",
                Timestamp = DateTime.Now
            });
        }

        private void RecordDeviceRecoveryEvent(DeviceRuntimeState? deviceState, int recoveredFailureCount, string? previousConnectionError)
        {
            DeviceConfig? device = deviceState == null ? null : deviceState.Config;
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "Device connection recovered after {0} failed attempt(s). Previous error: {1}",
                recoveredFailureCount,
                previousConnectionError ?? string.Empty);

            _runtimeEvents.Add(new RuntimeErrorDetail
            {
                Category = "DeviceConnectionRecovered",
                DeviceName = device == null ? string.Empty : device.Name ?? string.Empty,
                Message = message,
                Suggestion = "Device connection is back online. Review the previous failure event if the device keeps flapping.",
                Source = "RuntimeEngine",
                Timestamp = DateTime.Now
            });
        }

        private static string SuggestDeviceConnectionCause(string? errorMessage)
        {
            string text = (errorMessage ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(text, "0x80040154", "class not registered", "没有注册类", "未注册"))
                return "OPC DA 服务或 COM 组件未注册，优先检查 32/64 位是否匹配、KepServer ProgID 是否正确、DCOM 权限是否允许。";
            if (ContainsAny(text, "opc", "dcom", "com"))
                return "OPC 连接异常，检查 OPC 服务是否运行、ProgID/Endpoint 是否正确、COM/DCOM 权限、账号密码和证书策略。";
            if (ContainsAny(text, "timeout", "timed out", "超时"))
                return "连接超时，检查 PLC IP/端口、网络连通性、防火墙、站号/槽号/串口参数以及扫描周期是否过快。";
            if (ContainsAny(text, "refused", "unreachable", "not connected", "closed", "reset", "拒绝", "无法连接"))
                return "设备或端口不可达，检查设备电源、网线、IP/端口、协议服务开关和本机防火墙。";
            if (ContainsAny(text, "certificate", "security", "badsecurity", "user", "password"))
                return "安全或认证失败，检查 OPC UA 安全策略、证书信任、用户名密码和匿名访问配置。";
            return "检查设备是否在线、连接参数是否正确、协议类型是否匹配，以及网关与 PLC 之间的网络/串口链路。";
        }

        private static string SuggestTagReadCause(string? errorMessage)
        {
            string text = (errorMessage ?? string.Empty).ToLowerInvariant();
            if (ContainsAny(text, "0xc0040007", "address", "range", "illegal", "out of range", "地址", "范围"))
                return "点位地址或范围异常，检查寄存器区、偏移、元素数量、OPC ItemId/NodeId 是否存在。";
            if (ContainsAny(text, "type", "format", "convert", "parse", "类型", "格式", "转换"))
                return "数据类型不匹配，检查标签数据类型、字节序/字序、字符串长度和缩放配置。";
            if (ContainsAny(text, "access", "denied", "readonly", "权限", "拒绝"))
                return "读取权限不足或点位访问方式不匹配，检查标签读写权限、PLC/OPC 安全策略和访问模式。";
            if (ContainsAny(text, "timeout", "timed out", "超时"))
                return "读取超时，可能是通信不稳定、扫描周期过快、点位数量过多或设备响应慢。";
            if (ContainsAny(text, "badnodeid", "nodeid", "itemid", "unknown"))
                return "OPC 点位不存在或路径不正确，使用点位浏览确认 NodeId/ItemId 后重新配置。";
            return "检查标签地址、数据类型、长度、访问权限和所属设备连接状态。";
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrEmpty(text) || values == null)
                return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i]) && text.IndexOf(values[i].ToLowerInvariant(), StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private bool TryFindWritableTag(WriteTagRequest request, out DeviceConfig? device, out GroupConfig? group, out TagConfig? tag)
        {
            device = null;
            group = null;
            tag = null;

            if (string.IsNullOrWhiteSpace(request.DeviceName) || string.IsNullOrWhiteSpace(request.TagName))
                return false;

            ProjectConfig? config;
            lock (_syncRoot)
            {
                config = _config;
            }

            if (config == null || config.Devices == null)
                return false;

            string deviceName = TagPath.Normalize(request.DeviceName);
            string groupName = TagPath.Normalize(request.GroupName);
            string tagName = TagPath.Normalize(request.TagName);
            bool hasGroup = !string.IsNullOrWhiteSpace(request.GroupName);

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig candidateDevice = config.Devices[d];
                if (candidateDevice == null || TagPath.Normalize(candidateDevice.Name) != deviceName)
                    continue;

                device = candidateDevice;

                if (!hasGroup)
                {
                    tag = FindTag(candidateDevice.Tags, tagName);
                    return tag != null;
                }

                if (candidateDevice.Groups == null)
                    return false;

                for (int g = 0; g < candidateDevice.Groups.Count; g++)
                {
                    GroupConfig candidateGroup = candidateDevice.Groups[g];
                    if (candidateGroup == null || TagPath.Normalize(candidateGroup.Name) != groupName)
                        continue;

                    group = candidateGroup;
                    tag = FindTag(candidateGroup.Tags, tagName);
                    return tag != null;
                }

                return false;
            }

            return false;
        }

        private static TagConfig? FindTag(List<TagConfig>? tags, string normalizedTagName)
        {
            if (tags == null)
                return null;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag != null && TagPath.Normalize(tag.Name) == normalizedTagName)
                    return tag;
            }

            return null;
        }

        private static bool CanRead(TagConfig tag)
        {
            return tag != null && tag.AccessMode != TagAccessMode.WriteOnly;
        }

        private static bool CanWrite(TagConfig tag)
        {
            return tag != null && tag.AccessMode != TagAccessMode.ReadOnly;
        }

        private void PollDeviceTags(DeviceRuntimeState deviceState, IPlcClient? client, bool deviceConnected, DateTime now, int runtimeGeneration)
        {
            DeviceConfig device = deviceState.Config;
            if (device.Tags == null)
                return;

            for (int t = 0; t < device.Tags.Count; t++)
            {
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                TagConfig tag = device.Tags[t];
                if (tag == null)
                    continue;

                if (!IsDue(tag, now))
                    continue;

                if (!tag.Enabled)
                {
                    ScheduleNextRead(device, null, tag, now, false);
                    UpdateSnapshot(CreateSnapshot(device, null, tag, TagQuality.Disabled, "Tag is disabled."));
                    continue;
                }

                if (!CanRead(tag))
                {
                    ScheduleNextRead(device, null, tag, now, false);
                    UpdateSnapshot(CreateSnapshot(device, null, tag, TagQuality.AccessDenied, "Tag is write-only."));
                    continue;
                }

                if (!deviceConnected || client == null)
                {
                    ScheduleNextRead(device, null, tag, now, true);
                    UpdateSnapshot(CreateSnapshot(device, null, tag, TagQuality.NotConnected, "Device is not connected."));
                    continue;
                }

                bool readSucceeded;
                if (!ReadTag(client, deviceState, null, tag, out readSucceeded))
                {
                    ScheduleNextRead(device, null, tag, now, true);
                    deviceConnected = false;
                    client = null;
                }
                else
                {
                    ScheduleNextRead(device, null, tag, now, !readSucceeded);
                }
            }
        }

        private bool ReadTag(IPlcClient client, DeviceRuntimeState deviceState, GroupConfig? group, TagConfig tag, out bool readSucceeded)
        {
            DeviceConfig device = deviceState.Config;
            readSucceeded = false;
            try
            {
                deviceState.TotalReads++;
                int count = GetReadCount(tag);
                PlcReadResult result = client.Read(ResolveTagAddress(device, tag), tag.DataType, count, Math.Max(0, tag.ElementOffset));
                object rawValue = result.Value;
                object? scaledValue = TagValueScaler.Scale(rawValue, tag.Scaling);
                deviceState.SuccessfulReads++;
                deviceState.LastSuccessTime = DateTime.Now;
                deviceState.LastError = string.Empty;
                deviceState.ProtocolCircuitBreaker.RecordSuccess();

                TagValueSnapshot snapshot = CreateSnapshot(device, group, tag, TagQuality.Good, string.Empty);
                snapshot.RawValue = rawValue;
                snapshot.RawValueText = PlcValueFormatter.Format(rawValue);
                snapshot.Value = scaledValue ?? string.Empty;
                snapshot.ValueText = TagValueScaler.Format(scaledValue, tag.Scaling);
                snapshot.DataType = result.TypeName;

                TagValueSnapshot? previousSnapshot;
                TryGetSnapshot(device.Name, group == null ? string.Empty : group.Name, tag.Name, out previousSnapshot);
                TagDataCleaner.Clean(snapshot, tag, previousSnapshot);

                UpdateSnapshot(snapshot);
                readSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                deviceState.FailedReads++;
                deviceState.LastFailureTime = DateTime.Now;
                deviceState.LastError = ex.Message;
                deviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);
                if (LooksLikeTimeout(ex))
                    RegisterReadTimeout(deviceState, ex.Message);
                bool isCommunicationError = IsCommunicationException(ex);
                if (isCommunicationError)
                    DropDeviceConnection(deviceState, ex.Message);

                UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.ReadError, ex.Message));
                return !isCommunicationError;
            }
        }

        private void DropDeviceConnection(DeviceRuntimeState deviceState, string errorMessage)
        {
            try
            {
                if (deviceState.Client != null)
                    deviceState.Client.Dispose();
            }
            catch
            {
            }

            deviceState.Client = null;
            RegisterDeviceFailure(deviceState, errorMessage);
        }

        private void HandleUnexpectedPollError(DeviceRuntimeState deviceState, Exception ex)
        {
            if (deviceState == null)
                return;

            try
            {
                lock (deviceState.SyncRoot)
                {
                    DropDeviceConnection(deviceState, ex == null ? string.Empty : ex.Message);
                }

                MarkDevice(deviceState.Config, TagQuality.ReadError, ex == null ? "Unexpected polling error." : ex.Message);
            }
            catch
            {
            }
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception? current = exception;
            while (current != null)
            {
                if (current is PlcCommunicationException)
                    return true;
                if (current is PlcTagException)
                    return false;

                if (current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                if (current is ArgumentException ||
                    current is FormatException ||
                    current is OverflowException ||
                    current is NotSupportedException)
                    return false;

                InvalidOperationException? invalidOperation = current as InvalidOperationException;
                if (invalidOperation != null)
                {
                    string message = invalidOperation.Message ?? string.Empty;
                    if (LooksLikeTagLevelError(message))
                        return false;
                    if (LooksLikeCommunicationLevelError(message))
                        return true;
                }

                current = current.InnerException;
            }

            return false;
        }

        private static bool LooksLikeTimeout(Exception exception)
        {
            Exception? current = exception;
            while (current != null)
            {
                if (current is TimeoutException)
                    return true;

                string message = current.Message ?? string.Empty;
                string text = message.ToLowerInvariant();
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("瓒呮椂", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static bool LooksLikeTagLevelError(string message)
        {
            string text = (message ?? string.Empty).ToLowerInvariant();
            return text.IndexOf("illegal data address", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("illegal data value", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("illegal function", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("address range error", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("read-only area", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("undefined command", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("return code", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x04", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x05", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x06", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x13", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x1c", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("general status 0x20", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("end code 0x1103", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("end code 0x2101", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("return code", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("地址", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("只读", StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeCommunicationLevelError(string message)
        {
            string text = (message ?? string.Empty).ToLowerInvariant();
            return text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("forcibly", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("connection refused", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("unreachable", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("尚未连接", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("连接", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("断开", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("关闭", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("超时", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("timeout", StringComparison.Ordinal) >= 0;
        }

        private void WriteTagValue(IPlcClient client, DeviceConfig device, TagConfig tag, string valueText)
        {
            int elementOffset = Math.Max(0, tag.ElementOffset);
            if (tag.DataType == PlcDataType.String)
            {
                MethodInfo? writeWithCount = client.GetType().GetMethod(
                    "Write",
                    new[] { typeof(string), typeof(PlcDataType), typeof(string), typeof(int), typeof(int) });
                if (writeWithCount != null)
                {
                    try
                    {
                        writeWithCount.Invoke(client, new object[] { ResolveTagAddress(device, tag), tag.DataType, valueText, GetReadCount(tag), elementOffset });
                        return;
                    }
                    catch (TargetInvocationException ex)
                    {
                        if (ex.InnerException != null)
                            throw ex.InnerException;
                        throw;
                    }
                }
            }

            client.Write(ResolveTagAddress(device, tag), tag.DataType, valueText, elementOffset);
        }

        private static string ResolveTagAddress(DeviceConfig device, TagConfig tag)
        {
            if (tag == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(tag.Address))
                return tag.Address;
            if (device == null)
                return string.Empty;

            if (device.Protocol == PlcProtocol.Dlt6452007)
            {
                if (string.IsNullOrWhiteSpace(tag.MeterAddress) || string.IsNullOrWhiteSpace(tag.MeterDataIdentifier))
                    return string.Empty;
                return "DLT645:" + tag.MeterAddress.Trim() + ":" + tag.MeterDataIdentifier.Trim();
            }

            if (device.Protocol == PlcProtocol.Cjt1882004)
            {
                if (string.IsNullOrWhiteSpace(tag.MeterAddress) || string.IsNullOrWhiteSpace(tag.MeterDataIdentifier))
                    return string.Empty;
                if (!string.IsNullOrWhiteSpace(tag.MeterType))
                    return "CJ188:" + tag.MeterType.Trim() + ":" + tag.MeterAddress.Trim() + ":" + tag.MeterDataIdentifier.Trim();
                return "CJ188:" + tag.MeterAddress.Trim() + ":" + tag.MeterDataIdentifier.Trim();
            }

            return string.Empty;
        }


        private int GetReadCount(TagConfig tag)
        {
            bool usesCount = PlcDataTypeHelper.IsArray(tag.DataType) || tag.DataType == PlcDataType.String;
            if (!usesCount)
                return 1;
            return Math.Max(1, tag.ElementCount);
        }

        private bool IsDue(TagConfig tag, DateTime now)
        {
            DateTime next;
            lock (_syncRoot)
            {
                if (!_nextReadUtcByTagId.TryGetValue(tag.Id, out next))
                    return true;
            }

            return now >= next;
        }

        private bool IsCurrentGeneration(int runtimeGeneration)
        {
            return runtimeGeneration != 0 && Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0) == runtimeGeneration;
        }

        private bool IsDeviceDue(DeviceRuntimeState deviceState, DateTime now)
        {
            lock (deviceState.SyncRoot)
            {
                return IsDeviceDueUnsafe(deviceState, now);
            }
        }

        private static bool IsDeviceDueUnsafe(DeviceRuntimeState deviceState, DateTime now)
        {
            if (deviceState.NextReconnectUtc != DateTime.MinValue && now < deviceState.NextReconnectUtc)
                return false;
            if (deviceState.NextPollUtc != DateTime.MinValue && now < deviceState.NextPollUtc)
                return false;
            return true;
        }

        private bool IsBackpressureAdmissionPaused(DateTime now)
        {
            if (!_backpressureEnabled)
                return false;

            lock (_queueSyncRoot)
            {
                string message;
                return IsBackpressureBlockedNoLock(_pendingDevicePolls.Count, now, out message);
            }
        }

        private bool IsBackpressureBlockedNoLock(int pendingCount, DateTime now, out string message)
        {
            message = string.Empty;
            if (!_backpressureEnabled)
            {
                UpdateBackpressureState(false, pendingCount, now);
                return false;
            }

            bool currentlyActive = Volatile.Read(ref _backpressureActive) == 1;
            bool shouldBeActive = currentlyActive
                ? pendingCount > _queueLowWatermarkCount
                : pendingCount >= _queueHighWatermarkCount;

            UpdateBackpressureState(shouldBeActive, pendingCount, now);
            if (!shouldBeActive)
                return false;

            message = "Polling queue backpressure is active; pending " + pendingCount + "/" + _devicePollQueueLimit +
                      ", high watermark " + _queueHighWatermarkCount +
                      ", low watermark " + _queueLowWatermarkCount +
                      ". Device poll is delayed for " + _backpressureDelayMs + " ms.";
            return true;
        }

        private void UpdateBackpressureState(bool active, int pendingCount, DateTime now)
        {
            int desired = active ? 1 : 0;
            int previous = Interlocked.Exchange(ref _backpressureActive, desired);
            string message = active
                ? "Polling queue backpressure active: pending " + pendingCount + "/" + _devicePollQueueLimit +
                  ", high watermark " + _queueHighWatermarkCount +
                  ", low watermark " + _queueLowWatermarkCount + "."
                : "Polling queue backpressure recovered: pending " + pendingCount + "/" + _devicePollQueueLimit +
                  ", low watermark " + _queueLowWatermarkCount + ".";

            if (previous == desired)
            {
                if (active)
                {
                    _lastBackpressureTime = DateTime.Now;
                    _lastBackpressureMessage = message;
                }
                return;
            }

            _lastBackpressureTime = DateTime.Now;
            _lastBackpressureMessage = message;

            _runtimeEvents.Add(new RuntimeErrorDetail
            {
                Category = active ? "SchedulerBackpressureActive" : "SchedulerBackpressureRecovered",
                Message = message,
                Suggestion = active
                    ? "Reduce scan pressure, increase worker capacity, or increase queue limits after confirming host resources."
                    : "Scheduler admission has resumed because queue pressure dropped below the low watermark.",
                Source = "RuntimeEngine",
                Timestamp = DateTime.Now
            });
        }

        private void RegisterRateLimitedAdmission(DeviceRuntimeState deviceState, DateTime now)
        {
            Interlocked.Increment(ref _totalPollTasksRateLimited);
            DeferDeviceAdmission(
                deviceState,
                now,
                "RateLimited",
                "Scheduler tick admission limit reached (" + _maxDevicePollsQueuedPerSchedulerTick + " device poll(s) per tick).",
                _schedulerIntervalMs);
        }

        private void DeferDeviceAdmission(DeviceRuntimeState deviceState, DateTime now, string status, string message, int delayMs)
        {
            if (deviceState == null)
                return;

            int delay = Math.Max(_schedulerIntervalMs, delayMs);
            lock (deviceState.SyncRoot)
            {
                if (deviceState.IsPolling || deviceState.IsQueued)
                    return;

                deviceState.NextPollUtc = now.AddMilliseconds(delay);
                deviceState.CurrentTaskFinishedUtc = now;
                deviceState.LastTaskStatus = status ?? string.Empty;
                deviceState.LastTaskError = message ?? string.Empty;
            }
        }

        private int GetNextScheduleStartIndex(int deviceCount)
        {
            if (deviceCount <= 0)
                return 0;

            int index = Volatile.Read(ref _nextScheduleDeviceIndex);
            if (index < 0 || index >= deviceCount)
                return 0;
            return index;
        }

        private void SetNextScheduleStartIndex(int index, int deviceCount)
        {
            if (deviceCount <= 0)
            {
                Volatile.Write(ref _nextScheduleDeviceIndex, 0);
                return;
            }

            int normalized = index % deviceCount;
            if (normalized < 0)
                normalized = 0;
            Volatile.Write(ref _nextScheduleDeviceIndex, normalized);
        }

        private IList<RuntimePollingTaskStatus> GetPollingTaskStatuses()
        {
            List<RuntimePollingTaskStatus> tasks = new List<RuntimePollingTaskStatus>();
            List<DeviceRuntimeState> states;
            lock (_syncRoot)
            {
                states = new List<DeviceRuntimeState>(_deviceStatesById.Values);
            }

            for (int i = 0; i < states.Count; i++)
            {
                DeviceRuntimeState state = states[i];
                if (state == null)
                    continue;

                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(state.SyncRoot, 0, ref lockTaken);
                    if (!lockTaken)
                    {
                        DeviceConfig busyDevice = state.Config;
                        tasks.Add(new RuntimePollingTaskStatus
                        {
                            DeviceId = busyDevice == null ? string.Empty : busyDevice.Id,
                            DeviceName = busyDevice == null ? string.Empty : busyDevice.Name,
                            TaskId = state.CurrentTaskId,
                            Status = "Busy",
                            IsRunning = true,
                            LastError = "Device state is locked by polling."
                        });
                        continue;
                    }

                    DeviceConfig device = state.Config;
                    tasks.Add(new RuntimePollingTaskStatus
                    {
                        DeviceId = device == null ? string.Empty : device.Id,
                        DeviceName = device == null ? string.Empty : device.Name,
                        TaskId = state.CurrentTaskId,
                        Status = state.LastTaskStatus ?? string.Empty,
                        IsQueued = state.IsQueued,
                        IsRunning = state.IsPolling,
                        QueuedTime = state.CurrentTaskQueuedUtc == DateTime.MinValue ? DateTime.MinValue : state.CurrentTaskQueuedUtc.ToLocalTime(),
                        StartedTime = state.CurrentTaskStartedUtc == DateTime.MinValue ? DateTime.MinValue : state.CurrentTaskStartedUtc.ToLocalTime(),
                        FinishedTime = state.CurrentTaskFinishedUtc == DateTime.MinValue ? DateTime.MinValue : state.CurrentTaskFinishedUtc.ToLocalTime(),
                        LastDurationMs = state.LastTaskDurationMs,
                        SlowPollCount = state.SlowPollCount,
                        TimeoutCount = state.TimeoutCount,
                        LastError = state.LastTaskError ?? string.Empty
                    });
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                }
            }

            return tasks;
        }

        private void ClearPollingQueue()
        {
            lock (_queueSyncRoot)
            {
                _pendingDevicePolls.Clear();
                _pendingDeviceIds.Clear();
            }
        }

        private void ResetSchedulerStats()
        {
            Interlocked.Exchange(ref _nextTaskId, 0);
            Interlocked.Exchange(ref _totalPollTasksQueued, 0);
            Interlocked.Exchange(ref _totalPollTasksStarted, 0);
            Interlocked.Exchange(ref _totalPollTasksCompleted, 0);
            Interlocked.Exchange(ref _totalPollTasksFailed, 0);
            Interlocked.Exchange(ref _totalPollTasksTimedOut, 0);
            Interlocked.Exchange(ref _totalPollTasksSlow, 0);
            Interlocked.Exchange(ref _totalPollTasksRejected, 0);
            Interlocked.Exchange(ref _totalPollTasksBackpressureThrottled, 0);
            Interlocked.Exchange(ref _totalPollTasksRateLimited, 0);
            Interlocked.Exchange(ref _totalReadTimeouts, 0);
            Interlocked.Exchange(ref _runningPollTaskCount, 0);
            Interlocked.Exchange(ref _backpressureActive, 0);
            Volatile.Write(ref _nextScheduleDeviceIndex, 0);
            _maxObservedPendingCount = 0;
            lock (_syncRoot)
            {
                _lastTimeoutTime = DateTime.MinValue;
                _lastTimeoutDeviceName = string.Empty;
                _lastTimeoutMessage = string.Empty;
                _lastBackpressureTime = DateTime.MinValue;
                _lastBackpressureMessage = string.Empty;
            }
        }

        private void RegisterPollTimeout(DeviceRuntimeState deviceState, long durationMs)
        {
            Interlocked.Increment(ref _totalPollTasksTimedOut);
            string deviceName = deviceState == null || deviceState.Config == null ? string.Empty : deviceState.Config.Name;
            RegisterTimeout(deviceName, "Poll exceeded " + _pollTimeoutMs + " ms. Duration: " + durationMs + " ms.");
        }

        private void RegisterReadTimeout(DeviceRuntimeState deviceState, string message)
        {
            Interlocked.Increment(ref _totalReadTimeouts);
            string deviceName = deviceState == null || deviceState.Config == null ? string.Empty : deviceState.Config.Name;
            RegisterTimeout(deviceName, message);
        }

        private void RegisterTimeout(string deviceName, string message)
        {
            lock (_syncRoot)
            {
                _lastTimeoutTime = DateTime.Now;
                _lastTimeoutDeviceName = deviceName ?? string.Empty;
                _lastTimeoutMessage = message ?? string.Empty;
            }
        }

        private static string GetDeviceStateKey(DeviceRuntimeState deviceState)
        {
            if (deviceState == null || deviceState.Config == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(deviceState.Config.Id))
                return deviceState.Config.Id;
            return deviceState.Config.Name ?? string.Empty;
        }

        private void ScheduleDeviceNextPoll(DeviceRuntimeState deviceState, DateTime now)
        {
            if (deviceState == null || deviceState.Config == null)
                return;

            DateTime next = GetNextDeviceReadableTagDueUtc(deviceState.Config);
            if (next == DateTime.MinValue)
                next = now.AddMilliseconds(GetDeviceScanRateMs(deviceState.Config));

            if (deviceState.NextReconnectUtc != DateTime.MinValue && deviceState.NextReconnectUtc > next)
                next = deviceState.NextReconnectUtc;

            deviceState.NextPollUtc = next;
        }

        private DateTime GetNextDeviceReadableTagDueUtc(DeviceConfig? device)
        {
            DateTime next = DateTime.MinValue;
            CollectNextReadableTagDueUtc(device, null, device == null ? null : device.Tags, ref next);
            if (device != null && device.Groups != null)
            {
                for (int i = 0; i < device.Groups.Count; i++)
                {
                    GroupConfig group = device.Groups[i];
                    if (group == null || !group.Enabled)
                        continue;
                    CollectNextReadableTagDueUtc(device, group, group.Tags, ref next);
                }
            }

            return next;
        }

        private void CollectNextReadableTagDueUtc(DeviceConfig? device, GroupConfig? group, IList<TagConfig>? tags, ref DateTime next)
        {
            if (tags == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null || !tag.Enabled || !CanRead(tag))
                    continue;

                DateTime tagNext;
                lock (_syncRoot)
                {
                    if (!_nextReadUtcByTagId.TryGetValue(tag.Id, out tagNext))
                        tagNext = DateTime.UtcNow;
                }

                if (next == DateTime.MinValue || tagNext < next)
                    next = tagNext;
            }
        }

        private void ScheduleNextRead(DeviceConfig device, GroupConfig? group, TagConfig tag, DateTime now, bool failed)
        {
            int scanRate = failed
                ? GetFailureRetryDelayMs(device, tag)
                : GetEffectiveScanRateMs(device, group, tag);

            lock (_syncRoot)
            {
                _nextReadUtcByTagId[tag.Id] = now.AddMilliseconds(scanRate);
            }
        }

        private static int GetEffectiveScanRateMs(DeviceConfig device, GroupConfig? group, TagConfig tag)
        {
            int groupScanRate = group == null ? 0 : group.ScanRateMs;
            int scanRate = tag != null && tag.ScanRateMs > 0 ? tag.ScanRateMs : groupScanRate;
            if (scanRate <= 0)
                scanRate = GetDeviceScanRateMs(device);
            return ClampInterval(scanRate, 100, 86400000);
        }

        private static int GetDeviceScanRateMs(DeviceConfig device)
        {
            int scanRate = device == null ? 0 : device.DefaultScanRateMs;
            if (scanRate <= 0)
                scanRate = 1000;
            return ClampInterval(scanRate, 100, 86400000);
        }

        private int GetFailureRetryDelayMs(DeviceConfig device, TagConfig tag)
        {
            int retryDelay = tag != null && tag.FailureRetryDelayMs > 0
                ? tag.FailureRetryDelayMs
                : GetDeviceFailureRetryDelayMs(device);
            return ClampInterval(retryDelay, 100, GetDeviceMaxFailureRetryDelayMs(device));
        }

        private int GetDeviceFailureRetryDelayMs(DeviceConfig? device)
        {
            int retryDelay = device == null ? 0 : device.FailureRetryDelayMs;
            if (retryDelay <= 0)
                retryDelay = _minReconnectDelayMs;
            return ClampInterval(retryDelay, 100, GetDeviceMaxFailureRetryDelayMs(device));
        }

        private int GetDeviceMaxFailureRetryDelayMs(DeviceConfig? device)
        {
            int maxDelay = device == null ? 0 : device.MaxFailureRetryDelayMs;
            if (maxDelay <= 0)
                maxDelay = _maxReconnectDelayMs;
            return ClampInterval(maxDelay, 100, 86400000);
        }

        private static int ClampInterval(int value, int minValue, int maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }

        private static int CalculateWatermarkCount(int queueLimit, int percent)
        {
            if (queueLimit <= 0)
                return 0;

            int normalizedPercent = percent;
            if (normalizedPercent < 0)
                normalizedPercent = 0;
            if (normalizedPercent > 100)
                normalizedPercent = 100;

            int count = (int)Math.Ceiling(queueLimit * normalizedPercent / 100D);
            if (count <= 0 && normalizedPercent > 0)
                count = 1;
            if (count > queueLimit)
                count = queueLimit;
            return count;
        }

        private void MarkDevice(DeviceConfig device, TagQuality quality, string errorMessage)
        {
            if (device.Tags != null)
            {
                for (int t = 0; t < device.Tags.Count; t++)
                {
                    TagConfig tag = device.Tags[t];
                    if (tag != null)
                        UpdateSnapshot(CreateSnapshot(device, null, tag, quality, errorMessage));
                }
            }

            if (device.Groups != null)
            {
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    MarkGroup(device, device.Groups[g], quality, errorMessage);
                }
            }
        }

        private void MarkGroup(DeviceConfig device, GroupConfig? group, TagQuality quality, string errorMessage)
        {
            if (group == null || group.Tags == null)
                return;
            for (int t = 0; t < group.Tags.Count; t++)
            {
                TagConfig tag = group.Tags[t];
                if (tag != null)
                    UpdateSnapshot(CreateSnapshot(device, group, tag, quality, errorMessage));
            }
        }

        private TagValueSnapshot CreateSnapshot(DeviceConfig device, GroupConfig? group, TagConfig tag, TagQuality quality, string errorMessage)
        {
            return new TagValueSnapshot
            {
                DeviceId = device.Id,
                DeviceProtocol = device.Protocol.ToString(),
                GroupId = group == null ? string.Empty : group.Id,
                TagId = tag.Id,
                DeviceName = device.Name,
                GroupName = group == null ? string.Empty : group.Name,
                TagName = tag.Name,
                Unit = tag.Unit,
                PointCode = tag.PointCode,
                AssetPath = tag.AssetPath,
                BusinessType = tag.BusinessType,
                Source = tag.Source,
                Precision = tag.Precision,
                DataType = tag.DataType.ToString(),
                MqttPublishEnabled = tag.MqttPublishEnabled,
                Alarm = CloneAlarm(tag.Alarm),
                Quality = quality,
                Timestamp = DateTime.Now,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private static DeviceRuntimeStatus CreateDeviceRuntimeStatus(DeviceRuntimeState state)
        {
            DeviceConfig device = state.Config;
            bool connected = state.Client != null && state.Client.IsConnected;
            long totalReads = state.TotalReads;
            double successRate = totalReads <= 0 ? 0D : Math.Round(state.SuccessfulReads * 100D / totalReads, 2);
            string status;
            if (device == null || !device.Enabled)
                status = "Disabled";
            else if (connected)
                status = "Online";
            else if (state.ConsecutiveFailures > 0)
                status = "Error";
            else
                status = "Offline";

            return new DeviceRuntimeStatus
            {
                DeviceId = device == null ? string.Empty : device.Id,
                DeviceName = device == null ? string.Empty : device.Name,
                Protocol = device == null ? string.Empty : device.Protocol.ToString(),
                Enabled = device != null && device.Enabled,
                IsConnected = connected,
                IsPolling = state.IsPolling,
                IsQueued = state.IsQueued,
                Status = status,
                ConsecutiveFailures = state.ConsecutiveFailures,
                TotalReads = totalReads,
                SuccessfulReads = state.SuccessfulReads,
                FailedReads = state.FailedReads,
                SuccessRate = successRate,
                LastPollTime = state.LastPollTime,
                LastSuccessTime = state.LastSuccessTime,
                LastFailureTime = state.LastFailureTime,
                NextReconnectTime = state.NextReconnectUtc == DateTime.MinValue ? DateTime.MinValue : state.NextReconnectUtc.ToLocalTime(),
                LastReconnectDelayMs = state.LastReconnectDelayMs,
                NextPollTime = state.NextPollUtc == DateTime.MinValue ? DateTime.MinValue : state.NextPollUtc.ToLocalTime(),
                CurrentTaskId = state.CurrentTaskId,
                LastTaskStatus = state.LastTaskStatus ?? string.Empty,
                LastTaskDurationMs = state.LastTaskDurationMs,
                SlowPollCount = state.SlowPollCount,
                TimeoutCount = state.TimeoutCount,
                LastError = state.LastError ?? string.Empty,
                ProtocolCircuitBreaker = state.ProtocolCircuitBreaker.Snapshot()
            };
        }

        private static DeviceRuntimeStatus CreateBusyDeviceRuntimeStatus(DeviceRuntimeState state)
        {
            DeviceConfig device = state.Config;
            return new DeviceRuntimeStatus
            {
                DeviceId = device == null ? string.Empty : device.Id,
                DeviceName = device == null ? string.Empty : device.Name,
                Protocol = device == null ? string.Empty : device.Protocol.ToString(),
                Enabled = device != null && device.Enabled,
                IsPolling = true,
                IsQueued = state.IsQueued,
                Status = "Busy",
                CurrentTaskId = state.CurrentTaskId,
                LastTaskStatus = "Busy",
                LastError = "设备正在连接或采集中。",
                ProtocolCircuitBreaker = state.ProtocolCircuitBreaker.Snapshot()
            };
        }

        private static TagAlarmConfig CloneAlarm(TagAlarmConfig source)
        {
            if (source == null)
                return TagAlarmConfig.Default();

            return new TagAlarmConfig
            {
                Enabled = source.Enabled,
                LowLimit = source.LowLimit,
                HighLimit = source.HighLimit,
                LowAlarmMessage = source.LowAlarmMessage,
                HighAlarmMessage = source.HighAlarmMessage,
                WarningDeviation = source.WarningDeviation,
                LowWarningMessage = source.LowWarningMessage,
                HighWarningMessage = source.HighWarningMessage
            };
        }

        private void UpdateSnapshot(TagValueSnapshot snapshot)
        {
            string key = TagPath.Build(snapshot.DeviceName, snapshot.GroupName, snapshot.TagName);
            TagValueSnapshot clone = snapshot.Clone();
            lock (_syncRoot)
            {
                _snapshotsByPath[key] = clone;
            }

            EventHandler<TagValueChangedEventArgs>? handler = TagValueChanged;
            if (handler == null)
                return;

            Delegate[] subscribers = handler.GetInvocationList();
            for (int i = 0; i < subscribers.Length; i++)
            {
                EventHandler<TagValueChangedEventArgs>? subscriber = subscribers[i] as EventHandler<TagValueChangedEventArgs>;
                if (subscriber == null)
                    continue;

                try
                {
                    subscriber(this, new TagValueChangedEventArgs(clone.Clone()));
                }
                catch
                {
                }
            }
        }

        private static bool CanRestoreSnapshot(TagValueSnapshot current, TagValueSnapshot persisted)
        {
            if (persisted.Timestamp == DateTime.MinValue)
                return false;
            if (current.Quality == TagQuality.Unknown)
                return true;
            if (current.Timestamp == DateTime.MinValue)
                return true;
            return persisted.Timestamp >= current.Timestamp;
        }

        private static TagValueSnapshot MergeRestoredSnapshot(TagValueSnapshot current, TagValueSnapshot persisted)
        {
            TagValueSnapshot restored = current.Clone();
            restored.RawValue = persisted.RawValue;
            restored.RawValueText = persisted.RawValueText ?? string.Empty;
            restored.Value = persisted.Value;
            restored.ValueText = persisted.ValueText ?? string.Empty;
            restored.Quality = persisted.Quality;
            restored.Timestamp = persisted.Timestamp;
            restored.ErrorMessage = persisted.ErrorMessage ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(persisted.DataType))
                restored.DataType = persisted.DataType;
            return restored;
        }

        private static ReadTagsResponse CreateResponseList(ReadTagResponse response)
        {
            ReadTagsResponse list = new ReadTagsResponse();
            list.Results.Add(response);
            list.Success = response != null && response.Success;
            return list;
        }

        private static ReadTagsResponse CreateErrorResponseList(string deviceName, string groupName, string tagName, string errorMessage)
        {
            ReadTagsResponse response = new ReadTagsResponse();
            response.Success = false;
            response.Results.Add(CreateErrorResponse(deviceName, groupName, tagName, errorMessage));
            return response;
        }

        private static ReadTagResponse CreateErrorResponse(string deviceName, string groupName, string tagName, string errorMessage)
        {
            return new ReadTagResponse
            {
                Success = false,
                DeviceName = deviceName ?? string.Empty,
                GroupName = groupName ?? string.Empty,
                TagName = tagName ?? string.Empty,
                Quality = TagQuality.NotFound.ToString(),
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private static bool TryParseDataType(string dataTypeText, out PlcDataType dataType)
        {
            dataType = PlcDataType.Int16;
            if (string.IsNullOrWhiteSpace(dataTypeText))
                return false;

            try
            {
                dataType = (PlcDataType)Enum.Parse(typeof(PlcDataType), dataTypeText.Trim(), true);
                return Enum.IsDefined(typeof(PlcDataType), dataType);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildWriteValueText(WriteTagRequest request, TagConfig tag)
        {
            if (!string.IsNullOrWhiteSpace(request.ValueText))
                return request.ValueText;

            object value = request.Value;
            if (value == null)
                throw new ArgumentException("请输入写入值。");

            object? rawValue = TagValueScaler.Unscale(value, tag.Scaling);
            return FormatWriteValue(rawValue);
        }

        private static string FormatWriteValue(object? value)
        {
            if (value == null)
                return string.Empty;

            if (value is string text)
                return text;

            if (value is IEnumerable enumerable && !(value is byte[]))
            {
                List<string> parts = new List<string>();
                foreach (object item in enumerable)
                    parts.Add(FormatWriteValue(item));
                return string.Join(",", parts.ToArray());
            }

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture);

            return value.ToString() ?? string.Empty;
        }

        private static bool TryValidateWriteValue(PlcDataType dataType, string valueText, int elementCount, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(valueText))
            {
                errorMessage = "请输入写入值。";
                return false;
            }

            if (!PlcDataTypeHelper.IsArray(dataType))
                return TryValidateScalarWriteValue(dataType, valueText.Trim(), out errorMessage);

            string[] values = SplitWriteValues(valueText);
            if (values.Length == 0)
            {
                errorMessage = "请输入写入值。";
                return false;
            }

            int maxElementCount = Math.Max(1, elementCount);
            if (values.Length > maxElementCount)
            {
                errorMessage = "写入值数量超过标签元素数量（当前 " + values.Length + "，最大 " + maxElementCount + "）。";
                return false;
            }

            PlcDataType elementType = GetArrayElementDataType(dataType);
            for (int i = 0; i < values.Length; i++)
            {
                if (!TryValidateScalarWriteValue(elementType, values[i], out errorMessage))
                {
                    errorMessage = "第 " + i + " 项数组值无效：" + errorMessage;
                    return false;
                }
            }

            return true;
        }

        private static bool TryValidateScalarWriteValue(PlcDataType dataType, string valueText, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(valueText))
            {
                errorMessage = "值不能为空。";
                return false;
            }

            string text = valueText.Trim();
            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.Coil:
                case PlcDataType.DiscreteInput:
                    if (IsValidBoolText(text))
                        return true;
                    errorMessage = "布尔值只能填写 true/false 或 1/0。";
                    return false;
                case PlcDataType.Int16:
                    short int16Value;
                    if (short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int16Value))
                        return true;
                    errorMessage = "写入值必须是 Int16 数字。";
                    return false;
                case PlcDataType.UInt16:
                    ushort uint16Value;
                    if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint16Value))
                        return true;
                    errorMessage = "写入值必须是 UInt16 数字。";
                    return false;
                case PlcDataType.Int32:
                    int int32Value;
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int32Value))
                        return true;
                    errorMessage = "写入值必须是 Int32 数字。";
                    return false;
                case PlcDataType.UInt32:
                    uint uint32Value;
                    if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint32Value))
                        return true;
                    errorMessage = "写入值必须是 UInt32 数字。";
                    return false;
                case PlcDataType.Int64:
                    long int64Value;
                    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int64Value))
                        return true;
                    errorMessage = "写入值必须是 Int64 数字。";
                    return false;
                case PlcDataType.UInt64:
                    ulong uint64Value;
                    if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint64Value))
                        return true;
                    errorMessage = "写入值必须是 UInt64 数字。";
                    return false;
                case PlcDataType.Float:
                    float floatValue;
                    if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue) && !float.IsNaN(floatValue) && !float.IsInfinity(floatValue))
                        return true;
                    errorMessage = "写入值必须是有效的 Float 数字。";
                    return false;
                case PlcDataType.Double:
                    double doubleValue;
                    if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue) && !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue))
                        return true;
                    errorMessage = "写入值必须是有效的 Double 数字。";
                    return false;
                case PlcDataType.String:
                    return true;
                default:
                    errorMessage = "不支持写入的数据类型：" + dataType + "。";
                    return false;
            }
        }

        private static string[] SplitWriteValues(string valueText)
        {
            if (string.IsNullOrWhiteSpace(valueText))
                return new string[0];

            return valueText.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsValidBoolText(string text)
        {
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "off", StringComparison.OrdinalIgnoreCase);
        }

        private static PlcDataType GetArrayElementDataType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                    return PlcDataType.Bool;
                case PlcDataType.Int16Array:
                    return PlcDataType.Int16;
                case PlcDataType.UInt16Array:
                    return PlcDataType.UInt16;
                case PlcDataType.Int32Array:
                    return PlcDataType.Int32;
                case PlcDataType.UInt32Array:
                    return PlcDataType.UInt32;
                case PlcDataType.Int64Array:
                    return PlcDataType.Int64;
                case PlcDataType.UInt64Array:
                    return PlcDataType.UInt64;
                case PlcDataType.FloatArray:
                    return PlcDataType.Float;
                case PlcDataType.DoubleArray:
                    return PlcDataType.Double;
                case PlcDataType.CoilArray:
                    return PlcDataType.Coil;
                case PlcDataType.DiscreteInputArray:
                    return PlcDataType.DiscreteInput;
                default:
                    return dataType;
            }
        }

        private static WriteTagResponse CreateWriteErrorResponse(WriteTagRequest request, string errorMessage)
        {
            return new WriteTagResponse
            {
                Success = false,
                DeviceName = request == null ? string.Empty : request.DeviceName ?? string.Empty,
                GroupName = request == null ? string.Empty : request.GroupName ?? string.Empty,
                TagName = request == null ? string.Empty : request.TagName ?? string.Empty,
                DataType = request == null ? string.Empty : request.DataType ?? string.Empty,
                Quality = TagQuality.Bad.ToString(),
                Timestamp = DateTime.Now,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private enum PollAdmissionResult
        {
            Skipped,
            Queued,
            QueueRejected,
            BackpressureThrottled
        }
    }
}
