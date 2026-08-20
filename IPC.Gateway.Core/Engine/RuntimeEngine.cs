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
using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPC.Gateway.Core.Gateway;
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
        private readonly ConcurrentDictionary<string, TagValueSnapshot> _snapshotsByPath;
        private readonly ConcurrentDictionary<string, DateTime> _nextReadUtcByTagId;
        private readonly Dictionary<string, string> _channelNamesById;
        private readonly Dictionary<string, int> _devicePollStaggerOffsetMsByKey;
        private readonly object _queueSyncRoot;
        private readonly Queue<DeviceRuntimeState> _pendingHighPriorityDevicePolls;
        private readonly Queue<DeviceRuntimeState> _pendingRecoveryDevicePolls;
        private readonly Queue<DeviceRuntimeState> _pendingLowPriorityDevicePolls;
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
        private readonly int _deviceStatusFailureDebounceCount;
        private readonly int _deviceStatusFailureDebounceMs;
        private readonly int _deviceStatusRecoveryDebounceCount;
        private readonly int _deviceStatusRecoveryDebounceMs;
        private readonly int _tagValueChangedQueueLimit;
        private readonly int _minReconnectDelayMs;
        private readonly int _maxReconnectDelayMs;
        private readonly RuntimeErrorTimeline _runtimeEvents;
        private readonly RuntimeEventBus _eventBus;
        private readonly RuntimeConfigDiffer _configDiffer;
        private readonly RuntimeHealthEvaluator _healthEvaluator;
        private readonly RuntimeSnapshotStore _snapshotStore;
        private readonly PhysicalChannelManager _physicalChannelManager;
        private readonly ConfiguredChannelScheduler _configuredChannelScheduler;
        private readonly IValueTransformScriptRuntime _valueTransformScripts;
        private readonly Queue<DateTime> _recentPollTimeoutUtc;
        private readonly Queue<DateTime> _recentReadTimeoutUtc;
        private const int UdpOfflineFailureThreshold = 3;
        private const int UdpRecoveryDebounceCount = 3;
        private const int UdpRecoveryDebounceMs = 3000;
        private const int MinimumSubscriptionOperationTimeoutMs = 60000;
        private static readonly TimeSpan SchedulerTimeoutHealthWindow = TimeSpan.FromMinutes(5);

        private enum PlcOperationTimeoutKind
        {
            Device,
            Batch,
            Subscription
        }
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
        private int _pollDequeueSequence;
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
            : this(new RuntimeSchedulerOptions(), null)
        {
        }

        public RuntimeEngine(RuntimeSchedulerOptions schedulerOptions)
            : this(schedulerOptions, null)
        {
        }

        /// <summary>
        /// 创建运行时并注入可选的值处理脚本执行边界。
        /// </summary>
        public RuntimeEngine(
            RuntimeSchedulerOptions schedulerOptions,
            IValueTransformScriptRuntime? valueTransformScripts)
        {
            RuntimeSchedulerOptions options = (schedulerOptions ?? new RuntimeSchedulerOptions()).Normalize();
            _syncRoot = new object();
            _deviceStatesById = new Dictionary<string, DeviceRuntimeState>();
            _snapshotsByPath = new ConcurrentDictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
            _nextReadUtcByTagId = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _channelNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _devicePollStaggerOffsetMsByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _queueSyncRoot = new object();
            _pendingHighPriorityDevicePolls = new Queue<DeviceRuntimeState>();
            _pendingRecoveryDevicePolls = new Queue<DeviceRuntimeState>();
            _pendingLowPriorityDevicePolls = new Queue<DeviceRuntimeState>();
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
            _deviceStatusFailureDebounceCount = options.DeviceStatusFailureDebounceCount;
            _deviceStatusFailureDebounceMs = options.DeviceStatusFailureDebounceMs;
            _deviceStatusRecoveryDebounceCount = options.DeviceStatusRecoveryDebounceCount;
            _deviceStatusRecoveryDebounceMs = options.DeviceStatusRecoveryDebounceMs;
            _tagValueChangedQueueLimit = options.TagValueChangedQueueLimit;
            _minReconnectDelayMs = 1000;
            _maxReconnectDelayMs = 30000;
            _runtimeEvents = new RuntimeErrorTimeline(100);
            _eventBus = new RuntimeEventBus(_tagValueChangedQueueLimit, DispatchTagValueChanged, IsCurrentGeneration);
            _configDiffer = new RuntimeConfigDiffer();
            _healthEvaluator = new RuntimeHealthEvaluator();
            _snapshotStore = new RuntimeSnapshotStore();
            _physicalChannelManager = new PhysicalChannelManager();
            _configuredChannelScheduler = new ConfiguredChannelScheduler();
            _valueTransformScripts = valueTransformScripts ?? NoopValueTransformScriptRuntime.Instance;
            _recentPollTimeoutUtc = new Queue<DateTime>();
            _recentReadTimeoutUtc = new Queue<DateTime>();
            _devicePollSemaphore = new Semaphore(_maxConcurrentDevicePolls, _maxConcurrentDevicePolls);
            ThreadPool.GetMinThreads(out int minimumWorkers, out int minimumIoWorkers);
            if (minimumWorkers < _maxConcurrentDevicePolls)
                ThreadPool.SetMinThreads(_maxConcurrentDevicePolls, minimumIoWorkers);
            _lastTimeoutDeviceName = string.Empty;
            _lastTimeoutMessage = string.Empty;
            _lastBackpressureMessage = string.Empty;
            StartTagValueChangedDispatcher();
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
            ProjectConfigStore.Normalize(runtimeConfig);
            _runtimeEvents.Clear();

            lock (_syncRoot)
            {
                _configuredChannelScheduler.Configure(runtimeConfig);
                _config = runtimeConfig;
                RebuildChannelNameCacheNoLock(runtimeConfig);
                RebuildDevicePollStaggerCacheNoLock(runtimeConfig);
                _index = new TagRuntimeIndex(runtimeConfig);
                _snapshotsByPath.Clear();
                _nextReadUtcByTagId.Clear();
                _deviceStatesById.Clear();
                ClearPollingQueue();
                ResetSchedulerStats();
                InitializeDeviceStates(runtimeConfig);
                InitializeSnapshots(runtimeConfig);
                InitializeDevicePollStaggerNoLock(runtimeConfig, DateTime.UtcNow);
                int runtimeGeneration = Interlocked.Increment(ref _runtimeGeneration);
                _timer = new Timer(SchedulePolls, runtimeGeneration, 0, _schedulerIntervalMs);
            }
        }

        public void ApplyProject(ProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            ProjectConfig runtimeConfig = ProjectConfigCloner.Clone(config) ?? throw new InvalidOperationException("Project configuration clone failed.");
            ProjectConfigStore.Normalize(runtimeConfig);
            Dictionary<string, TagValueSnapshot> previousSnapshots;
            Dictionary<string, TagValueSnapshot> previousSnapshotsByTagId;
            Dictionary<string, DateTime> previousNextReadUtcByTagId;
            Dictionary<string, DeviceRuntimeState> previousDeviceStatesById;
            List<DeviceRuntimeState> statesToRelease = new List<DeviceRuntimeState>();
            List<DeviceRuntimeState> statesToDisconnect = new List<DeviceRuntimeState>();
            HashSet<string> devicesToPollImmediately = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<TagValueSnapshot> changedSnapshots = new List<TagValueSnapshot>();
            Timer? oldTimer = null;
            bool restartTimer;
            int runtimeGeneration = Interlocked.Increment(ref _runtimeGeneration);

            lock (_syncRoot)
            {
                _configuredChannelScheduler.Configure(runtimeConfig);
                restartTimer = _timer != null;
                if (restartTimer)
                {
                    oldTimer = _timer;
                    _timer = null;
                }

                previousSnapshots = new Dictionary<string, TagValueSnapshot>(_snapshotsByPath, StringComparer.OrdinalIgnoreCase);
                previousSnapshotsByTagId = BuildSnapshotLookupByTagIdNoLock(_snapshotsByPath);
                previousNextReadUtcByTagId = new Dictionary<string, DateTime>(_nextReadUtcByTagId, StringComparer.OrdinalIgnoreCase);
                previousDeviceStatesById = new Dictionary<string, DeviceRuntimeState>(_deviceStatesById, StringComparer.OrdinalIgnoreCase);

                _config = runtimeConfig;
                RebuildChannelNameCacheNoLock(runtimeConfig);
                RebuildDevicePollStaggerCacheNoLock(runtimeConfig);
                _index = new TagRuntimeIndex(runtimeConfig);
                _snapshotsByPath.Clear();
                _nextReadUtcByTagId.Clear();
                _deviceStatesById.Clear();
                ClearPollingQueue();
                InitializeDeviceStates(runtimeConfig, previousDeviceStatesById, statesToRelease, statesToDisconnect, devicesToPollImmediately);
                DateTime nowUtc = DateTime.UtcNow;
                RebuildSnapshotsNoLock(runtimeConfig, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);
                ForceDeviceReadableTagsDueNoLock(runtimeConfig, devicesToPollImmediately, nowUtc);

                if (restartTimer)
                    _timer = new Timer(SchedulePolls, runtimeGeneration, 0, _schedulerIntervalMs);
            }

            if (oldTimer != null)
                oldTimer.Dispose();

            ForceDevicePollsDue(devicesToPollImmediately, DateTime.UtcNow);

            for (int i = 0; i < statesToRelease.Count; i++)
                ReleaseDeviceStateWithoutWaiting(statesToRelease[i]);

            for (int i = 0; i < statesToDisconnect.Count; i++)
                DisconnectDisabledDeviceStateClientWithoutWaiting(statesToDisconnect[i]);

            for (int i = 0; i < changedSnapshots.Count; i++)
                EnqueueTagValueChanged(changedSnapshots[i], runtimeGeneration);
        }

        public void Stop()
        {
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

            ClearPollingQueue();
            _eventBus.Drain(TimeSpan.FromSeconds(2));
            Interlocked.Increment(ref _runtimeGeneration);

            for (int i = 0; i < states.Count; i++)
                ReleaseDeviceStateWithoutWaiting(states[i]);

            lock (_syncRoot)
            {
                _deviceStatesById.Clear();
                _nextReadUtcByTagId.Clear();
            }

            ClearTagValueChangedQueue();
        }

        public bool TryGetSnapshotById(string channelId, string deviceId, string groupId, string tagId, out TagValueSnapshot? snapshot)
        {
            return _snapshotStore.TryGetById(_snapshotsByPath, channelId, deviceId, groupId, tagId, out snapshot);
        }

        public IList<TagValueSnapshot> GetSnapshots()
        {
            return _snapshotStore.GetAll(_snapshotsByPath);
        }

        public void RestoreSnapshots(IList<TagValueSnapshot> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                return;

            lock (_syncRoot)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    TagValueSnapshot persisted = snapshots[i];
                    if (persisted == null)
                        continue;

                    string key = TagPath.BuildIdentity(persisted.ChannelId, persisted.DeviceId, persisted.GroupId, persisted.TagId);

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
                int pendingCount = GetPendingDevicePollCountNoLock();
                queueStatus = new RuntimePollingQueueStatus
                {
                    PendingCount = pendingCount,
                    RecoveryPendingCount = _pendingRecoveryDevicePolls.Count,
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
                DateTime nowUtc = DateTime.UtcNow;
                TrimTimeoutWindowNoLock(_recentPollTimeoutUtc, nowUtc);
                TrimTimeoutWindowNoLock(_recentReadTimeoutUtc, nowUtc);
                timeoutStats = new RuntimeTimeoutStats
                {
                    PollTimeoutCount = Interlocked.Read(ref _totalPollTasksTimedOut),
                    ReadTimeoutCount = Interlocked.Read(ref _totalReadTimeouts),
                    RecentPollTimeoutCount = _recentPollTimeoutUtc.Count,
                    RecentReadTimeoutCount = _recentReadTimeoutUtc.Count,
                    TimeoutWindowSeconds = (int)SchedulerTimeoutHealthWindow.TotalSeconds,
                    LastTimeoutTime = _lastTimeoutTime,
                    LastTimeoutDeviceName = _lastTimeoutDeviceName ?? string.Empty,
                    LastTimeoutMessage = _lastTimeoutMessage ?? string.Empty
                };
            }

            RuntimeEventBusStats eventBusStats = _eventBus.GetStats();

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
                TagValueChangedPendingCount = eventBusStats.PendingCount,
                TagValueChangedQueueLimit = eventBusStats.QueueLimit,
                TagValueChangedMaxObservedPendingCount = eventBusStats.MaxObservedPendingCount,
                TotalTagValueChangedQueued = eventBusStats.TotalQueued,
                TotalTagValueChangedDispatched = eventBusStats.TotalDispatched,
                TotalTagValueChangedDropped = eventBusStats.TotalDropped,
                Queue = queueStatus,
                Timeout = timeoutStats,
                Tasks = GetPollingTaskStatuses()
            };

            RuntimeSchedulerHealth health = _healthEvaluator.Evaluate(status);
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

                DeviceConfig? deviceConfig = state.Config;
                string message = string.Empty;
                DateTime timestamp = DateTime.MinValue;
                string deviceName = string.Empty;
                bool stateCaptured = false;

                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(state.SyncRoot, 0, ref lockTaken);
                    if (lockTaken)
                    {
                        message = state.LastConnectionError;
                        timestamp = state.LastConnectionErrorTime;
                        deviceName = state.Config == null ? string.Empty : state.Config.Name;
                        stateCaptured = true;
                    }
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                }

                // 抢锁失败仅表示状态正在更新，不能据此生成一条新的连接错误。
                if (!stateCaptured)
                    continue;

                if (!string.IsNullOrWhiteSpace(message))
                {
                    errors.Add(new RuntimeErrorDetail
                    {
                        Category = "DeviceConnection",
                        ChannelId = deviceConfig == null ? string.Empty : deviceConfig.ChannelId,
                        ChannelName = deviceConfig == null ? string.Empty : GetChannelName(deviceConfig.ChannelId),
                        DeviceId = deviceConfig == null ? string.Empty : deviceConfig.Id,
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
                        ChannelId = snapshot.ChannelId,
                        ChannelName = snapshot.ChannelName,
                        DeviceId = snapshot.DeviceId,
                        DeviceName = snapshot.DeviceName,
                        GroupId = snapshot.GroupId,
                        GroupName = snapshot.GroupName,
                        TagId = snapshot.TagId,
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
                        ChannelId = snapshot.ChannelId,
                        ChannelName = snapshot.ChannelName,
                        DeviceId = snapshot.DeviceId,
                        DeviceName = snapshot.DeviceName,
                        GroupId = snapshot.GroupId,
                        GroupName = snapshot.GroupName,
                        TagId = snapshot.TagId,
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
            if (!TryGetSnapshotById(request.ChannelId, request.DeviceId, request.GroupId, request.TagId, out snapshot) || snapshot == null)
            {
                return new ReadTagResponse
                {
                    Success = false,
                    ChannelId = request.ChannelId,
                    DeviceId = request.DeviceId,
                    GroupId = request.GroupId,
                    TagId = request.TagId,
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
                response.Results.Add(CreateErrorResponse(string.Empty, string.Empty, string.Empty, string.Empty, "No tags were requested."));
                return response;
            }

            for (int i = 0; i < request.Tags.Count; i++)
            {
                TagPathDto? path = request.Tags[i];
                ReadTagRequest itemRequest = new ReadTagRequest
                {
                    ChannelId = path == null ? string.Empty : path.ChannelId,
                    DeviceId = path == null ? string.Empty : path.DeviceId,
                    GroupId = path == null ? string.Empty : path.GroupId,
                    TagId = path == null ? string.Empty : path.TagId,
                    ChannelName = path == null ? string.Empty : path.ChannelName,
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

            bool hasChannel = !string.IsNullOrWhiteSpace(request.ChannelId);
            bool hasDevice = !string.IsNullOrWhiteSpace(request.DeviceId);
            bool hasGroup = !string.IsNullOrWhiteSpace(request.GroupId);
            bool hasTag = !string.IsNullOrWhiteSpace(request.TagId);

            if (!hasChannel || !hasDevice)
                return CreateErrorResponseList(request.ChannelId, request.DeviceId, request.GroupId, request.TagId, "ChannelId and DeviceId are required.");

            if (hasGroup && hasTag)
                return CreateResponseList(ReadCached(request));

            if (!hasGroup && hasTag)
                return ReadTagByDeviceCached(request.ChannelId, request.DeviceId, request.TagId);

            if (hasGroup && !hasTag)
                return ReadGroupCached(request.ChannelId, request.DeviceId, request.GroupId);

            return CreateErrorResponseList(request.ChannelId, request.DeviceId, request.GroupId, request.TagId, "GroupId or TagId is required.");
        }

        public ReadTagsResponse ReadTagByDeviceCached(string channelId, string deviceId, string tagId)
        {
            if (TryGetSnapshotById(channelId, deviceId, string.Empty, tagId, out TagValueSnapshot? snapshot) && snapshot != null)
                return CreateResponseList(ReadTagResponse.FromSnapshot(snapshot.Clone()));

            return CreateErrorResponseList(channelId, deviceId, string.Empty, tagId, "Device-level tag was not found under the device.");
        }

        public ReadTagsResponse ReadGroupCached(string channelId, string deviceId, string groupId)
        {
            ReadTagsResponse response = new ReadTagsResponse();
            string normalizedChannelId = TagPath.Normalize(channelId);
            string normalizedDeviceId = TagPath.Normalize(deviceId);
            string normalizedGroupId = TagPath.Normalize(groupId);

            lock (_syncRoot)
            {
                foreach (TagValueSnapshot snapshot in _snapshotsByPath.Values)
                {
                    if (TagPath.Normalize(snapshot.ChannelId) == normalizedChannelId &&
                        TagPath.Normalize(snapshot.DeviceId) == normalizedDeviceId &&
                        TagPath.Normalize(snapshot.GroupId) == normalizedGroupId)
                    {
                        response.Results.Add(ReadTagResponse.FromSnapshot(snapshot.Clone()));
                    }
                }
            }

            if (response.Results.Count == 0)
            {
                response.Success = false;
                response.Results.Add(CreateErrorResponse(channelId, deviceId, groupId, string.Empty, "Group was not found under the device, or it contains no tags."));
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
            if (!_configuredChannelScheduler.IsEnabled(device))
                return CreateWriteErrorResponse(request, "Configured channel is disabled.");
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
                writeDeviceState = GetDeviceState(device);
                if (writeDeviceState == null)
                    return CreateWriteErrorResponse(request, "Device runtime state was not found.");

                using CancellationTokenSource writeCancellation = CreateOperationTimeoutCancellationTokenSource(device, PlcOperationTimeoutKind.Device);
                ConfiguredChannelLease? configuredWriteLease = null;
                try
                {
                    configuredWriteLease = _configuredChannelScheduler
                        .AcquireWriteAsync(device, writeCancellation.Token)
                        .AsTask().GetAwaiter().GetResult();
                    return writeDeviceState.Actor.InvokeAsync(async actorToken =>
                    {
                        (bool connected, IPlcClient? client) = await TryEnsureClientAsync(writeDeviceState, actorToken).ConfigureAwait(false);
                        if (!connected || client == null)
                            return CreateWriteErrorResponse(request, "Device is not connected.");

                        await WriteTagValueAsync(client, writeDeviceState, tag, valueText, actorToken).ConfigureAwait(false);
                        writeDeviceState.ProtocolCircuitBreaker.RecordSuccess();

                    int runtimeGeneration = Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0);
                    if (CanRead(tag))
                    {
                        (bool keepConnected, bool readSucceeded) = await ReadTagAsync(client, writeDeviceState, group, tag, runtimeGeneration, actorToken).ConfigureAwait(false);
                        if (!readSucceeded)
                        {
                            string refreshError = keepConnected
                                ? "Write succeeded, but the current value could not be refreshed."
                                : "Write succeeded, but device communication failed while refreshing the current value.";
                            return CreateWriteRefreshWarningResponse(device, group, tag, refreshError);
                        }
                    }

                    TagValueSnapshot? snapshot;
                    ReadTagResponse? currentValue = null;
                    if (TryGetSnapshotById(device.ChannelId, device.Id, group == null ? string.Empty : group.Id, tag.Id, out snapshot) && snapshot != null)
                        currentValue = ReadTagResponse.FromSnapshot(snapshot);

                        return new WriteTagResponse
                        {
                            Success = true,
                            ChannelId = device.ChannelId,
                            ChannelName = GetChannelName(device.ChannelId),
                            DeviceId = device.Id,
                            GroupId = group == null ? string.Empty : group.Id,
                            TagId = tag.Id,
                            DeviceName = device.Name,
                            GroupName = group == null ? string.Empty : group.Name,
                            TagName = tag.Name,
                            DataType = tag.DataType.ToString(),
                            Quality = currentValue == null ? TagQuality.Good.ToString() : currentValue.Quality,
                            Timestamp = DateTime.Now,
                            CurrentValue = currentValue ?? new ReadTagResponse()
                        };
                    }, writeCancellation.Token).GetAwaiter().GetResult();
                }
                finally
                {
                    configuredWriteLease?.Dispose();
                    TryStartQueuedDevicePolls(Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0));
                }
            }
            catch (Exception ex)
            {
                bool communicationError = IsCommunicationException(ex);
                if (writeDeviceState != null && communicationError)
                    writeDeviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);

                if (writeDeviceState != null && communicationError)
                {
                    writeDeviceState.Actor.ExecuteAsync(async token =>
                    {
                        await DropDeviceConnectionAsync(writeDeviceState, ex.Message, token).ConfigureAwait(false);
                    }).GetAwaiter().GetResult();
                }

                return CreateWriteErrorResponse(request, ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Stop();
            StopTagValueChangedDispatcher();
            _disposed = true;
        }

        private void ReleaseDeviceStateWithoutWaiting(DeviceRuntimeState state)
        {
            if (state == null)
                return;

            state.Actor.Post(delegate
            {
                state.DisposeSubscription();
                IPlcClient? client = state.Client;
                state.Client = null;
                state.IsQueued = false;
                state.IsPolling = false;
                state.CurrentTaskFinishedUtc = DateTime.UtcNow;
                state.LastTaskStatus = "Stopped";
                DisposeClientAsync(client);
            });
        }

        private void DisconnectDisabledDeviceStateClientWithoutWaiting(DeviceRuntimeState state)
        {
            if (state == null)
                return;

            state.Actor.Post(delegate
            {
                if (state.Config == null || state.Config.Enabled)
                    return;

                state.DisposeSubscription();
                IPlcClient? client = state.Client;
                state.Client = null;
                state.IsQueued = false;
                if (!state.IsPolling && string.Equals(state.LastTaskStatus, "Queued", StringComparison.OrdinalIgnoreCase))
                    state.LastTaskStatus = "Idle";
                DisposeClientAsync(client);
            });
        }

        private void QueueDeferredDisabledDeviceStateClientDisconnect(DeviceRuntimeState state)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                IPlcClient? client = null;
                bool lockTaken = false;
                try
                {
                    if (!Monitor.TryEnter(state.SyncRoot, TimeSpan.FromSeconds(2)))
                        return;

                    lockTaken = true;
                    if (state.Config == null || state.Config.Enabled)
                        return;

                    state.DisposeSubscription();
                    client = state.Client;
                    state.Client = null;
                    state.IsQueued = false;
                    if (!state.IsPolling && string.Equals(state.LastTaskStatus, "Queued", StringComparison.OrdinalIgnoreCase))
                        state.LastTaskStatus = "Idle";
                }
                catch
                {
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                    DisposeClientAsync(client);
                }
            });
        }

        private void QueueDeferredDeviceStateRelease(DeviceRuntimeState state)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                IPlcClient? client = null;
                bool lockTaken = false;
                try
                {
                    if (!Monitor.TryEnter(state.SyncRoot, TimeSpan.FromSeconds(2)))
                        return;

                    lockTaken = true;
                    state.DisposeSubscription();
                    client = state.Client;
                    state.Client = null;
                    state.IsQueued = false;
                    state.IsPolling = false;
                    state.CurrentTaskFinishedUtc = DateTime.UtcNow;
                    state.LastTaskStatus = "Stopped";
                }
                catch
                {
                }
                finally
                {
                    if (lockTaken)
                        Monitor.Exit(state.SyncRoot);
                    DisposeClientAsync(client);
                }
            });
        }

        private static void DisposeClientAsync(IPlcClient? client)
        {
            if (client == null)
                return;

            _ = Task.Run(async delegate
            {
                try
                {
                    await PlcClientInvoker.DisconnectAsync(client).ConfigureAwait(false);
                }
                catch
                {
                }

                try
                {
                    await PlcClientInvoker.InvokeSynchronousAsync(client.Dispose).ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }

        private static Dictionary<string, TagValueSnapshot> BuildSnapshotLookupByTagIdNoLock(IDictionary<string, TagValueSnapshot> snapshots)
        {
            Dictionary<string, TagValueSnapshot> result = new Dictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
            foreach (TagValueSnapshot snapshot in snapshots.Values)
            {
                if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.TagId))
                    result[snapshot.TagId] = snapshot;
            }

            return result;
        }

        private void RebuildSnapshotsNoLock(
            ProjectConfig config,
            IDictionary<string, TagValueSnapshot> previousSnapshots,
            IDictionary<string, TagValueSnapshot> previousSnapshotsByTagId,
            IDictionary<string, DateTime> previousNextReadUtcByTagId,
            IList<TagValueSnapshot> changedSnapshots,
            ISet<string>? devicesToPollImmediately,
            DateTime nowUtc)
        {
            if (config == null || config.Devices == null)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                RebuildTagSnapshotsNoLock(device, null, device.Tags, device.Enabled, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);

                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null)
                        continue;

                    group.DeviceId = device.Id;
                    RebuildTagSnapshotsNoLock(device, group, group.Tags, device.Enabled && group.Enabled, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);
                }
            }
        }

        private void RebuildTagSnapshotsNoLock(
            DeviceConfig device,
            GroupConfig? group,
            IList<TagConfig>? tags,
            bool parentEnabled,
            IDictionary<string, TagValueSnapshot> previousSnapshots,
            IDictionary<string, TagValueSnapshot> previousSnapshotsByTagId,
            IDictionary<string, DateTime> previousNextReadUtcByTagId,
            IList<TagValueSnapshot> changedSnapshots,
            ISet<string>? devicesToPollImmediately,
            DateTime nowUtc)
        {
            if (tags == null)
                return;

            for (int t = 0; t < tags.Count; t++)
            {
                TagConfig tag = tags[t];
                if (tag == null)
                    continue;

                tag.DeviceId = device.Id;
                tag.GroupId = group == null ? string.Empty : group.Id;
                if (tag.Scaling == null)
                    tag.Scaling = ScalingConfig.Default();

                TagValueSnapshot snapshot = CreateConfiguredSnapshot(device, group, tag, parentEnabled);
                TagValueSnapshot? previous = FindPreviousSnapshot(device, group, tag, previousSnapshots, previousSnapshotsByTagId);
                if (previous != null && parentEnabled && tag.Enabled && CanRead(tag) && CanRestoreConfiguredSnapshot(previous))
                    snapshot = MergeRestoredSnapshot(snapshot, previous);

                string path = TagPath.BuildIdentity(device.ChannelId, device.Id, group == null ? string.Empty : group.Id, tag.Id);
                _snapshotsByPath[path] = snapshot;

                DateTime nextReadUtc;
                if (!string.IsNullOrWhiteSpace(tag.Id) && previousNextReadUtcByTagId.TryGetValue(tag.Id, out nextReadUtc))
                    _nextReadUtcByTagId[tag.Id] = nextReadUtc;
                else
                    _nextReadUtcByTagId[tag.Id] = nowUtc;

                if (previous == null || HasTagValueChanged(previous, snapshot))
                    changedSnapshots.Add(snapshot.Clone());

                if (previous == null && parentEnabled && tag.Enabled && CanDeviceRead(tag) && !string.IsNullOrWhiteSpace(device.Id))
                    devicesToPollImmediately?.Add(device.Id);
            }
        }

        private void ForceDeviceReadableTagsDueNoLock(ProjectConfig config, ISet<string> deviceIds, DateTime nowUtc)
        {
            if (config == null || config.Devices == null || deviceIds == null || deviceIds.Count == 0)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null || !device.Enabled || string.IsNullOrWhiteSpace(device.Id) || !deviceIds.Contains(device.Id))
                    continue;

                ForceReadableTagsDueNoLock(device.Tags, nowUtc);
                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || !group.Enabled)
                        continue;

                    ForceReadableTagsDueNoLock(group.Tags, nowUtc);
                }
            }
        }

        private void ForceReadableTagsDueNoLock(IList<TagConfig>? tags, DateTime nowUtc)
        {
            if (tags == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null || !tag.Enabled || !CanDeviceRead(tag) || string.IsNullOrWhiteSpace(tag.Id))
                    continue;

                _nextReadUtcByTagId[tag.Id] = nowUtc;
            }
        }

        private void ForceDevicePollsDue(ISet<string> deviceIds, DateTime nowUtc)
        {
            if (deviceIds == null || deviceIds.Count == 0)
                return;

            List<DeviceRuntimeState> states = new List<DeviceRuntimeState>();
            lock (_syncRoot)
            {
                foreach (string deviceId in deviceIds)
                {
                    if (string.IsNullOrWhiteSpace(deviceId))
                        continue;

                    DeviceRuntimeState? state;
                    if (_deviceStatesById.TryGetValue(deviceId, out state) && state != null)
                        states.Add(state);
                }
            }

            for (int i = 0; i < states.Count; i++)
            {
                DeviceRuntimeState state = states[i];
                if (state == null)
                    continue;

                lock (state.SyncRoot)
                {
                    if (state.Config == null || !state.Config.Enabled || state.IsPolling || state.IsQueued)
                        continue;

                    state.NextPollUtc = state.NextReconnectUtc != DateTime.MinValue && state.NextReconnectUtc > nowUtc
                        ? state.NextReconnectUtc
                        : nowUtc;
                }
            }
        }

        private static TagValueSnapshot? FindPreviousSnapshot(
            DeviceConfig device,
            GroupConfig? group,
            TagConfig tag,
            IDictionary<string, TagValueSnapshot> previousSnapshots,
            IDictionary<string, TagValueSnapshot> previousSnapshotsByTagId)
        {
            if (!string.IsNullOrWhiteSpace(tag.Id))
            {
                TagValueSnapshot? byTagId;
                if (previousSnapshotsByTagId.TryGetValue(tag.Id, out byTagId))
                    return byTagId;
            }

            string path = TagPath.BuildIdentity(device.ChannelId, device.Id, group == null ? string.Empty : group.Id, tag.Id);
            TagValueSnapshot? byPath;
            return previousSnapshots.TryGetValue(path, out byPath) ? byPath : null;
        }

        private static bool CanRestoreConfiguredSnapshot(TagValueSnapshot previous)
        {
            return previous.Quality != TagQuality.Disabled;
        }

        private TagValueSnapshot CreateConfiguredSnapshot(DeviceConfig device, GroupConfig? group, TagConfig tag, bool parentEnabled)
        {
            if (!parentEnabled)
                return CreateSnapshot(device, group, tag, TagQuality.Disabled, group == null && !device.Enabled ? "Device is disabled." : "Group is disabled.");
            if (!tag.Enabled)
                return CreateSnapshot(device, group, tag, TagQuality.Disabled, "Tag is disabled.");
            if (!CanRead(tag))
                return CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only.");
            return CreateSnapshot(device, group, tag, TagQuality.Unknown, "Waiting for first scan.");
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

                        string path = TagPath.BuildIdentity(device.ChannelId, device.Id, group.Id, tag.Id);
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
            InitializeDeviceStates(config, null, null, null, null);
        }

        private void InitializeDeviceStates(
            ProjectConfig config,
            IDictionary<string, DeviceRuntimeState>? previousStatesById,
            IList<DeviceRuntimeState>? statesToRelease,
            IList<DeviceRuntimeState>? statesToDisconnect,
            ISet<string>? devicesToPollImmediately)
        {
            if (config == null || config.Devices == null)
                return;

            HashSet<string>? reusedDeviceIds = previousStatesById == null
                ? null
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < config.Devices.Count; i++)
            {
                DeviceConfig device = config.Devices[i];
                if (device == null)
                    continue;

                DeviceRuntimeState? previousState;
                if (previousStatesById != null &&
                    previousStatesById.TryGetValue(device.Id, out previousState) &&
                    _configDiffer.CanReuseDeviceState(previousState.Config, device))
                {
                    DeviceRuntimeConfigTransition transition = previousState.ReuseConfig(device);
                    _deviceStatesById[device.Id] = previousState;
                    reusedDeviceIds?.Add(device.Id);
                    if (transition == DeviceRuntimeConfigTransition.Disabled)
                        statesToDisconnect?.Add(previousState);
                    else if (transition == DeviceRuntimeConfigTransition.Enabled)
                        devicesToPollImmediately?.Add(device.Id);
                    continue;
                }

                _deviceStatesById[device.Id] = new DeviceRuntimeState(device, _protocolDriverCircuitBreakerOptions);
            }

            if (previousStatesById == null || statesToRelease == null)
                return;

            foreach (KeyValuePair<string, DeviceRuntimeState> pair in previousStatesById)
            {
                if (reusedDeviceIds != null && reusedDeviceIds.Contains(pair.Key))
                    continue;
                if (pair.Value != null)
                    statesToRelease.Add(pair.Value);
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

                string path = TagPath.BuildIdentity(device.ChannelId, device.Id, string.Empty, tag.Id);
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
                List<DeviceRuntimeState>? lowPriorityCandidates = null;

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

                    if (!_configuredChannelScheduler.IsEnabled(device))
                    {
                        MarkDevice(device, TagQuality.Disabled, "Configured channel is disabled.");
                        ScheduleDeviceNextPoll(deviceState, now);
                        continue;
                    }

                    if (!IsDeviceDue(deviceState, now))
                        continue;

                    if (!HasDueReadableTags(device, now))
                        continue;

                    if (IsLowPriorityPoll(deviceState))
                    {
                        if (lowPriorityCandidates == null)
                            lowPriorityCandidates = new List<DeviceRuntimeState>();
                        lowPriorityCandidates.Add(deviceState);
                        continue;
                    }

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

                if (lowPriorityCandidates != null)
                {
                    for (int i = 0; i < lowPriorityCandidates.Count; i++)
                    {
                        DeviceRuntimeState deviceState = lowPriorityCandidates[i];
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
            bool lowPriority;
            bool recoveryProbe;

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
                int pendingCount = GetPendingDevicePollCountNoLock();
                if (IsBackpressureBlockedNoLock(pendingCount, now, out backpressureMessage))
                {
                    Interlocked.Increment(ref _totalPollTasksBackpressureThrottled);
                    DeferDeviceAdmission(deviceState, now, "BackpressureDelayed", backpressureMessage, _backpressureDelayMs);
                    return PollAdmissionResult.BackpressureThrottled;
                }

                if (pendingCount >= _devicePollQueueLimit)
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
                    lowPriority = IsLowPriorityPollUnsafe(deviceState);
                    recoveryProbe = deviceState.IsIsolated;
                }

                if (recoveryProbe)
                    _pendingRecoveryDevicePolls.Enqueue(deviceState);
                else if (lowPriority)
                    _pendingLowPriorityDevicePolls.Enqueue(deviceState);
                else
                    _pendingHighPriorityDevicePolls.Enqueue(deviceState);
                _pendingDeviceIds.Add(deviceId);
                Interlocked.Increment(ref _totalPollTasksQueued);
                pendingCount = GetPendingDevicePollCountNoLock();
                if (pendingCount > _maxObservedPendingCount)
                    _maxObservedPendingCount = pendingCount;
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
                ConfiguredChannelLease? configuredChannelLease = null;
                lock (_queueSyncRoot)
                {
                    deviceState = DequeueNextDevicePollNoLock(DateTime.UtcNow, out configuredChannelLease);
                }

                if (deviceState == null || configuredChannelLease == null)
                {
                    _devicePollSemaphore.Release();
                    return;
                }

                Interlocked.Increment(ref _runningPollTaskCount);
                Interlocked.Increment(ref _totalPollTasksStarted);
                // Do not execute synchronously-completing drivers on the scheduler thread.
                // A fast ValueTask chain can otherwise monopolize admission and serialize all devices.
                _ = Task.Run(() => RunQueuedDevicePollAsync(deviceState, configuredChannelLease, runtimeGeneration));
            }
        }

        private async Task RunQueuedDevicePollAsync(
            DeviceRuntimeState deviceState,
            ConfiguredChannelLease configuredChannelLease,
            int runtimeGeneration)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Exception? pollError = null;
            using CancellationTokenSource pollCancellation = CreatePollCancellationTokenSource(deviceState);
            try
            {
                if (IsCurrentGeneration(runtimeGeneration))
                    await PollDeviceAsync(deviceState, runtimeGeneration, pollCancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                pollError = ex;
                await HandleUnexpectedPollErrorAsync(deviceState, ex).ConfigureAwait(false);
            }
            finally
            {
                stopwatch.Stop();
                CompleteDevicePollTask(deviceState, runtimeGeneration, stopwatch.ElapsedMilliseconds, pollError);
                configuredChannelLease.Dispose();
                _devicePollSemaphore.Release();
                Interlocked.Decrement(ref _runningPollTaskCount);
                TryStartQueuedDevicePolls(runtimeGeneration);
            }
        }

        private DeviceRuntimeState? DequeueNextDevicePollNoLock(DateTime now, out ConfiguredChannelLease? configuredChannelLease)
        {
            configuredChannelLease = null;
            bool preferRecovery = unchecked(++_pollDequeueSequence) % 4 == 0;
            if (preferRecovery)
            {
                DeviceRuntimeState? recovery = TryDequeueDevicePollNoLock(_pendingRecoveryDevicePolls, now, out configuredChannelLease);
                if (recovery != null)
                    return recovery;
            }

            DeviceRuntimeState? deviceState = TryDequeueDevicePollNoLock(_pendingHighPriorityDevicePolls, now, out configuredChannelLease);
            if (deviceState != null)
                return deviceState;

            deviceState = TryDequeueDevicePollNoLock(_pendingRecoveryDevicePolls, now, out configuredChannelLease);
            if (deviceState != null)
                return deviceState;

            return TryDequeueDevicePollNoLock(_pendingLowPriorityDevicePolls, now, out configuredChannelLease);
        }

        private DeviceRuntimeState? TryDequeueDevicePollNoLock(
            Queue<DeviceRuntimeState> queue,
            DateTime now,
            out ConfiguredChannelLease? configuredChannelLease)
        {
            configuredChannelLease = null;
            int candidateCount = queue.Count;
            DeviceRuntimeState? selected = null;
            double selectedScore = double.MaxValue;

            for (int i = 0; i < candidateCount; i++)
            {
                DeviceRuntimeState candidate = queue.Dequeue();
                bool valid;

                lock (candidate.SyncRoot)
                {
                    valid = !candidate.IsPolling && IsDeviceDueUnsafe(candidate, now);
                    if (!valid)
                    {
                        candidate.IsQueued = false;
                        candidate.LastTaskStatus = "Skipped";
                    }
                }

                if (!valid)
                {
                    _pendingDeviceIds.Remove(GetDeviceStateKey(candidate));
                    continue;
                }

                if (!_configuredChannelScheduler.TryGetDispatchScore(candidate.Config, out double score))
                {
                    queue.Enqueue(candidate);
                    continue;
                }

                if (selected == null || score < selectedScore)
                {
                    if (selected != null)
                        queue.Enqueue(selected);
                    selected = candidate;
                    selectedScore = score;
                }
                else
                {
                    queue.Enqueue(candidate);
                }
            }

            if (selected == null)
                return null;

            if (!_configuredChannelScheduler.TryAcquirePoll(selected.Config, out configuredChannelLease) || configuredChannelLease == null)
            {
                queue.Enqueue(selected);
                return null;
            }

            _pendingDeviceIds.Remove(GetDeviceStateKey(selected));
            lock (selected.SyncRoot)
            {
                selected.IsQueued = false;
                selected.IsPolling = true;
                selected.CurrentTaskStartedUtc = now;
                selected.LastTaskStatus = "Running";
                selected.LastTaskError = string.Empty;
            }
            return selected;
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

        private async Task PollDeviceAsync(DeviceRuntimeState deviceState, int runtimeGeneration, CancellationToken cancellationToken)
        {
            if (!IsCurrentGeneration(runtimeGeneration))
                return;

            DeviceConfig device = deviceState.Config;
            DateTime now = DateTime.UtcNow;

            await deviceState.Actor.ExecuteAsync(async actorToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                actorToken.ThrowIfCancellationRequested();
                deviceState.LastPollTime = DateTime.Now;
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                if (DateTime.UtcNow < deviceState.NextReconnectUtc)
                    return;

                (bool deviceConnected, IPlcClient? client) = await TryEnsureClientAsync(deviceState, actorToken).ConfigureAwait(false);
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                if (!deviceConnected || client == null)
                {
                    MarkDeviceUnavailableTagsOnce(deviceState);
                    return;
                }

                if (deviceState.IsIsolated)
                {
                    await ProbeDeviceRecoveryAsync(deviceState, client, runtimeGeneration, actorToken).ConfigureAwait(false);
                    return;
                }

                bool subscriptionFallback = false;
                if (CanUseSubscription(client, device))
                {
                    subscriptionFallback = await TryEnsureDeviceSubscriptionAsync(
                        deviceState,
                        client,
                        runtimeGeneration,
                        actorToken).ConfigureAwait(false);
                    if (!IsCurrentGeneration(runtimeGeneration))
                        return;

                    if (deviceState.Client == null || !deviceState.Client.IsConnected)
                    {
                        MarkDeviceUnavailableTagsOnce(deviceState);
                        return;
                    }

                    client = deviceState.Client;
                }

                DevicePollReadContext pollContext = new DevicePollReadContext(client, deviceConnected);
                List<PendingTagRead>? batchReads = PlcClientInvoker.SupportsBatchRead(client) ? new List<PendingTagRead>() : null;

                await PollTagCollectionAsync(deviceState, null, device.Tags, now, runtimeGeneration, pollContext, batchReads, subscriptionFallback, actorToken).ConfigureAwait(false);
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                if (device.Groups == null)
                {
                    await ReadPendingBatchTagsAsync(deviceState, batchReads, now, runtimeGeneration, pollContext, subscriptionFallback, actorToken).ConfigureAwait(false);
                    return;
                }

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

                    await PollTagCollectionAsync(deviceState, group, group.Tags, now, runtimeGeneration, pollContext, batchReads, subscriptionFallback, actorToken).ConfigureAwait(false);
                    if (!IsCurrentGeneration(runtimeGeneration))
                        return;
                }

                await ReadPendingBatchTagsAsync(deviceState, batchReads, now, runtimeGeneration, pollContext, subscriptionFallback, actorToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ProbeDeviceRecoveryAsync(
            DeviceRuntimeState deviceState,
            IPlcClient client,
            int runtimeGeneration,
            CancellationToken cancellationToken)
        {
            deviceState.RecoveryState = "Probing";
            deviceState.DeviceState = "Probing";
            CompiledTagRead? probe = deviceState.ReadPlan.FindRecoveryProbe(deviceState.LastKnownGoodTagId);
            if (probe == null)
            {
                RegisterDeviceFailure(deviceState, "设备没有可用于恢复探测的有效只读标签。");
                return;
            }

            (bool keepConnected, bool succeeded) = await ReadTagAsync(
                client,
                deviceState,
                probe.Group,
                probe.Tag,
                runtimeGeneration,
                cancellationToken).ConfigureAwait(false);

            if (succeeded)
            {
                deviceState.RecoveryState = "Recovered";
                deviceState.NextPollUtc = DateTime.MinValue;
            }
            else if (keepConnected)
            {
                RegisterDeviceFailure(deviceState, "恢复探测标签读取失败：" + probe.Tag.Name);
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
                    if (tag != null && tag.Enabled && CanDeviceRead(tag) && IsDue(tag, now))
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
                    if (tag != null && tag.Enabled && CanDeviceRead(tag) && IsDue(tag, now))
                        return true;
                }
            }

            return false;
        }

        private static bool CanUseSubscription(IPlcClient client, DeviceConfig device)
        {
            PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
            if (PlcDriverPluginRegistry.TryGetCapabilities(device.Connection, device.Protocol, out PlcClientCapabilities driverCapabilities) &&
                driverCapabilities.SupportsSubscription)
                capabilities.PreferredReadMode = driverCapabilities.PreferredReadMode;
            return client != null &&
                   device != null &&
                   device.Enabled &&
                   PlcClientInvoker.SupportsSubscription(client) &&
                   capabilities.SupportsSubscription &&
                   capabilities.PreferredReadMode == PlcPreferredReadMode.Subscription;
        }

        private async ValueTask<bool> TryEnsureDeviceSubscriptionAsync(
            DeviceRuntimeState deviceState,
            IPlcClient client,
            int runtimeGeneration,
            CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (deviceState.SubscriptionUnavailable && now < deviceState.NextSubscriptionRetryUtc)
                return deviceState.Subscription != null && deviceState.Subscription.IsActive;

            DeviceConfig device = deviceState.Config;
            List<PlcSubscriptionRequest> requests = BuildSubscriptionRequests(deviceState);
            if (requests.Count == 0)
            {
                deviceState.DisposeSubscription();
                deviceState.SubscriptionFingerprint = string.Empty;
                return false;
            }

            PlcSubscriptionOptions options = CreateSubscriptionOptions(requests);
            string fingerprint = CreateSubscriptionFingerprint(requests, options);
            if (deviceState.Subscription != null &&
                deviceState.Subscription.IsActive &&
                string.Equals(deviceState.SubscriptionFingerprint, fingerprint, StringComparison.Ordinal))
                return true;

            try
            {
                Func<PlcSubscriptionUpdate, ValueTask> onUpdate = update =>
                {
                    int updateGeneration = GetCurrentRuntimeGeneration();
                    deviceState.Actor.PostAsync(token => ProcessSubscriptionUpdateAsync(deviceState, update, updateGeneration, token));
                    return ValueTask.CompletedTask;
                };

                if (deviceState.Subscription == null || !deviceState.Subscription.IsActive)
                {
                    deviceState.DisposeSubscription();
                    deviceState.Subscription = await ExecutePlcOperationAsync(
                        deviceState,
                        token => PlcClientInvoker.SubscribeAsync(client, requests, options, onUpdate, token),
                        CancellationToken.None,
                        PlcOperationTimeoutKind.Subscription,
                        false).ConfigureAwait(false);
                }
                else
                {
                    await ExecutePlcOperationAsync(
                        deviceState,
                        token => deviceState.Subscription.UpdateAsync(requests, options, token),
                        CancellationToken.None,
                        PlcOperationTimeoutKind.Subscription,
                        false).ConfigureAwait(false);
                }

                deviceState.SubscriptionFingerprint = fingerprint;
                deviceState.SubscriptionUnavailable = false;
                deviceState.LastSubscriptionError = string.Empty;
                deviceState.NextSubscriptionRetryUtc = DateTime.MinValue;
                return true;
            }
            catch (Exception ex)
            {
                deviceState.DisposeSubscription();
                deviceState.SubscriptionFingerprint = string.Empty;
                deviceState.SubscriptionUnavailable = true;
                deviceState.LastSubscriptionError = ex.Message ?? string.Empty;
                deviceState.NextSubscriptionRetryUtc = DateTime.UtcNow.AddMilliseconds(GetDeviceFailureRetryDelayMs(device));
                deviceState.LastTaskStatus = "SubscriptionFallback";
                deviceState.LastTaskError = deviceState.LastSubscriptionError;

                if (ShouldDropDeviceConnectionAfterSubscriptionFailure(deviceState, ex))
                    await DropDeviceConnectionAsync(deviceState, deviceState.LastSubscriptionError, cancellationToken).ConfigureAwait(false);

                return false;
            }
        }

        private List<PlcSubscriptionRequest> BuildSubscriptionRequests(DeviceRuntimeState deviceState)
        {
            DeviceConfig device = deviceState.Config;
            List<PlcSubscriptionRequest> requests = new List<PlcSubscriptionRequest>();
            AddSubscriptionRequests(deviceState, null, device == null ? null : device.Tags, requests);
            if (device != null && device.Groups != null)
            {
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || !group.Enabled)
                        continue;
                    AddSubscriptionRequests(deviceState, group, group.Tags, requests);
                }
            }

            return requests;
        }

        private void AddSubscriptionRequests(
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            IList<TagConfig>? tags,
            IList<PlcSubscriptionRequest> requests)
        {
            DeviceConfig device = deviceState.Config;
            if (device == null || tags == null || requests == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null || !tag.Enabled || !CanDeviceRead(tag))
                    continue;

                CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
                if (!compiledRead.IsStaticallyValid || compiledRead.Runtime.IsIsolated)
                    continue;

                requests.Add(new PlcSubscriptionRequest(
                    tag.Id,
                    compiledRead.Address,
                    tag.DataType,
                    GetReadCount(tag),
                    Math.Max(0, tag.ElementOffset),
                    GetEffectiveScanRateMs(device, group, tag)));
            }
        }

        private static PlcSubscriptionOptions CreateSubscriptionOptions(IList<PlcSubscriptionRequest> requests)
        {
            int minInterval = 1000;
            bool hasInterval = false;
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    PlcSubscriptionRequest request = requests[i];
                    if (request == null || request.SamplingIntervalMs <= 0)
                        continue;

                    if (!hasInterval || request.SamplingIntervalMs < minInterval)
                        minInterval = request.SamplingIntervalMs;
                    hasInterval = true;
                }
            }

            minInterval = ClampInterval(minInterval, 100, 86400000);
            return new PlcSubscriptionOptions
            {
                PublishingIntervalMs = minInterval,
                SamplingIntervalMs = minInterval,
                QueueSize = 1,
                DiscardOldest = true
            };
        }

        private static string CreateSubscriptionFingerprint(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options)
        {
            List<string> parts = new List<string>();
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    PlcSubscriptionRequest request = requests[i];
                    if (request == null)
                        continue;

                    parts.Add(
                        (request.Key ?? string.Empty) + "\u001f" +
                        (request.Address ?? string.Empty) + "\u001f" +
                        request.DataType + "\u001f" +
                        request.ElementCount + "\u001f" +
                        request.ElementOffset + "\u001f" +
                        request.SamplingIntervalMs);
                }
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return (options == null ? string.Empty : options.PublishingIntervalMs + ":" + options.QueueSize) +
                   "\u001e" +
                   string.Join("\u001e", parts.ToArray());
        }

        private async ValueTask ProcessSubscriptionUpdateAsync(
            DeviceRuntimeState deviceState,
            PlcSubscriptionUpdate update,
            int runtimeGeneration,
            CancellationToken cancellationToken)
        {
            if (deviceState == null ||
                update == null ||
                !IsCurrentGeneration(runtimeGeneration) ||
                !IsCurrentDeviceState(deviceState))
                return;

            DeviceConfig device = deviceState.Config;
            GroupConfig? group;
            TagConfig? tag;
            if (!TryFindTagById(device, update.Key, out group, out tag) || tag == null)
                return;

            DateTime now = DateTime.UtcNow;
            deviceState.LastSubscriptionNotificationUtc = now;
            if (!tag.Enabled)
            {
                ScheduleNextSubscriptionFallback(device, group, tag, now);
                UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.Disabled, "Tag is disabled."));
                return;
            }

            if (!CanDeviceRead(tag))
            {
                ScheduleNextSubscriptionFallback(device, group, tag, now);
                UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only."));
                return;
            }

            deviceState.TotalReads++;
            if (update.Success && update.Result != null)
            {
                ApplyReadSuccess(deviceState, group, tag, update.Result, recordDeviceSuccess: false);
                RecordDeviceCommunicationSuccess(deviceState, confirmedSubscriptionUpdate: true);
                ScheduleNextSubscriptionFallback(device, group, tag, now);
                return;
            }

            bool connectionFailure = IsConnectionFailureScope(update.FailureScope);
            await RecordReadFailureAsync(
                deviceState,
                group,
                tag,
                update.ErrorMessage,
                connectionFailure,
                LooksLikeTimeoutMessage(update.ErrorMessage),
                connectionFailure,
                update.FailureScope == PlcReadFailureScope.Tag,
                cancellationToken).ConfigureAwait(false);
            ScheduleNextRead(device, group, tag, now, true);
        }

        private static bool TryFindTagById(DeviceConfig device, string tagId, out GroupConfig? group, out TagConfig? tag)
        {
            group = null;
            tag = null;
            if (device == null || string.IsNullOrWhiteSpace(tagId))
                return false;

            if (device.Tags != null)
            {
                for (int i = 0; i < device.Tags.Count; i++)
                {
                    TagConfig candidate = device.Tags[i];
                    if (candidate != null && string.Equals(candidate.Id, tagId, StringComparison.OrdinalIgnoreCase))
                    {
                        tag = candidate;
                        return true;
                    }
                }
            }

            if (device.Groups == null)
                return false;

            for (int g = 0; g < device.Groups.Count; g++)
            {
                GroupConfig candidateGroup = device.Groups[g];
                if (candidateGroup == null || candidateGroup.Tags == null)
                    continue;

                for (int t = 0; t < candidateGroup.Tags.Count; t++)
                {
                    TagConfig candidate = candidateGroup.Tags[t];
                    if (candidate != null && string.Equals(candidate.Id, tagId, StringComparison.OrdinalIgnoreCase))
                    {
                        group = candidateGroup;
                        tag = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private async ValueTask<(bool Connected, IPlcClient? Client)> TryEnsureClientAsync(
            DeviceRuntimeState deviceState,
            CancellationToken cancellationToken)
        {
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
                RecordDeviceStatusSample(deviceState, "Error");
                return (false, null);
            }

            if (deviceState.Client != null &&
                (deviceState.Client.IsConnected || IsUdpTransport(deviceState)))
                return (true, deviceState.Client);

            if (DateTime.UtcNow < deviceState.NextReconnectUtc)
                return (false, null);

            try
            {
                PlcConnectionOptions options = device.Connection ?? new PlcConnectionOptions();
                options.Protocol = device.Protocol;
                IPlcClient newClient = PlcClientFactory.Create(options);
                try
                {
                    await ExecutePlcOperationAsync(
                        deviceState,
                        token => PlcClientInvoker.ConnectAsync(newClient, token),
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    DisposeClientAsync(newClient);
                    throw;
                }

                if (deviceState.Client != null)
                {
                    deviceState.DisposeSubscription();
                    DisposeClientAsync(deviceState.Client);
                }

                deviceState.Client = newClient;
                deviceState.DeviceState = "Probing";

                return (true, newClient);
            }
            catch (Exception ex)
            {
                if (deviceState.Client != null)
                {
                    deviceState.DisposeSubscription();
                    DisposeClientAsync(deviceState.Client);
                    deviceState.Client = null;
                }

                RegisterDeviceFailure(deviceState, ex.Message);
                deviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);
                return (false, null);
            }
        }

        private void RegisterDeviceFailure(DeviceRuntimeState deviceState, string errorMessage)
        {
            deviceState.ConsecutiveFailures++;
            deviceState.LastError = errorMessage ?? string.Empty;
            deviceState.LastConnectionError = deviceState.LastError;
            deviceState.LastConnectionErrorTime = DateTime.Now;
            deviceState.PendingRecoveryFailureCount = 0;
            deviceState.PendingRecoveryConnectionError = string.Empty;
            deviceState.DeviceState = "Isolated";
            deviceState.RecoveryState = "Waiting";
            if (!deviceState.IsIsolated)
                deviceState.IsolatedSinceUtc = DateTime.UtcNow;
            deviceState.IsIsolated = true;

            int delay = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(
                deviceState.ConsecutiveFailures,
                GetDeviceFailureRetryDelayMs(deviceState.Config),
                GetDeviceMaxFailureRetryDelayMs(deviceState.Config),
                GetDeviceReconnectJitterKey(deviceState.Config));

            deviceState.LastReconnectDelayMs = delay;
            deviceState.NextReconnectUtc = DateTime.UtcNow.AddMilliseconds(delay);
            deviceState.NextRecoveryProbeUtc = deviceState.NextReconnectUtc;
            deviceState.NextPollUtc = deviceState.NextReconnectUtc;
            deviceState.ForceStatus("Error", DateTime.UtcNow);
            RecordDeviceFailureEvent(deviceState, delay);
        }

        private string RecordDeviceStatusSample(
            DeviceRuntimeState deviceState,
            string candidateStatus,
            bool applyUdpRecoveryDebounce = true)
        {
            if (deviceState == null)
                return string.Empty;

            int recoveryDebounceCount = _deviceStatusRecoveryDebounceCount;
            int recoveryDebounceMs = _deviceStatusRecoveryDebounceMs;
            if (applyUdpRecoveryDebounce &&
                IsUdpTransport(deviceState) &&
                string.Equals(candidateStatus, "Online", StringComparison.OrdinalIgnoreCase))
            {
                recoveryDebounceCount = Math.Max(recoveryDebounceCount, UdpRecoveryDebounceCount);
                recoveryDebounceMs = Math.Max(recoveryDebounceMs, UdpRecoveryDebounceMs);
            }

            return deviceState.ApplyStatusSample(
                candidateStatus,
                DateTime.UtcNow,
                _deviceStatusFailureDebounceCount,
                _deviceStatusFailureDebounceMs,
                recoveryDebounceCount,
                recoveryDebounceMs);
        }

        private void RecordDeviceCommunicationSuccess(
            DeviceRuntimeState deviceState,
            bool confirmedSubscriptionUpdate = false)
        {
            int recoveredFailureCount = deviceState.ConsecutiveFailures;
            string previousConnectionError = deviceState.LastConnectionError ?? string.Empty;
            if (recoveredFailureCount > 0)
            {
                deviceState.PendingRecoveryFailureCount = Math.Max(deviceState.PendingRecoveryFailureCount, recoveredFailureCount);
                if (!string.IsNullOrWhiteSpace(previousConnectionError))
                    deviceState.PendingRecoveryConnectionError = previousConnectionError;
            }

            deviceState.ConsecutiveFailures = 0;
            deviceState.NextPollUtc = DateTime.MinValue;
            deviceState.NextReconnectUtc = DateTime.MinValue;
            deviceState.LastReconnectDelayMs = 0;
            deviceState.UnavailableTagsMarked = false;
            deviceState.LastSuccessTime = DateTime.Now;
            deviceState.ProtocolCircuitBreaker.RecordSuccess();
            bool wasIsolated = deviceState.IsIsolated;
            deviceState.IsIsolated = false;
            deviceState.IsolatedSinceUtc = DateTime.MinValue;
            deviceState.NextRecoveryProbeUtc = DateTime.MinValue;
            deviceState.RecoveryState = wasIsolated ? "Recovered" : "Idle";
            deviceState.DeviceState = wasIsolated ? "Recovering" : "Online";
            string status = RecordDeviceStatusSample(
                deviceState,
                "Online",
                applyUdpRecoveryDebounce: !confirmedSubscriptionUpdate);
            if (IsOnlineDeviceStatus(status))
            {
                if (deviceState.PendingRecoveryFailureCount > 0)
                {
                    RecordDeviceRecoveryEvent(
                        deviceState,
                        deviceState.PendingRecoveryFailureCount,
                        deviceState.PendingRecoveryConnectionError);
                }

                deviceState.PendingRecoveryFailureCount = 0;
                deviceState.PendingRecoveryConnectionError = string.Empty;
                deviceState.LastError = string.Empty;
                deviceState.DeviceState = "Online";
                deviceState.RecoveryState = "Idle";
            }
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
                ChannelId = device == null ? string.Empty : device.ChannelId ?? string.Empty,
                ChannelName = device == null ? string.Empty : GetChannelName(device.ChannelId ?? string.Empty),
                DeviceId = device == null ? string.Empty : device.Id ?? string.Empty,
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
                ChannelId = device == null ? string.Empty : device.ChannelId ?? string.Empty,
                ChannelName = device == null ? string.Empty : GetChannelName(device.ChannelId ?? string.Empty),
                DeviceId = device == null ? string.Empty : device.Id ?? string.Empty,
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

            if (string.IsNullOrWhiteSpace(request.ChannelId) ||
                string.IsNullOrWhiteSpace(request.DeviceId) ||
                string.IsNullOrWhiteSpace(request.TagId))
                return false;

            ProjectConfig? config;
            lock (_syncRoot)
            {
                config = _config;
            }

            if (config == null || config.Devices == null)
                return false;

            string channelId = TagPath.Normalize(request.ChannelId);
            string deviceId = TagPath.Normalize(request.DeviceId);
            string groupId = TagPath.Normalize(request.GroupId);
            string tagId = TagPath.Normalize(request.TagId);
            bool hasGroup = !string.IsNullOrWhiteSpace(request.GroupId);

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig candidateDevice = config.Devices[d];
                if (candidateDevice == null ||
                    TagPath.Normalize(candidateDevice.ChannelId) != channelId ||
                    TagPath.Normalize(candidateDevice.Id) != deviceId)
                    continue;

                device = candidateDevice;

                if (!hasGroup)
                {
                    tag = FindTagById(candidateDevice.Tags, tagId);
                    return tag != null;
                }

                if (candidateDevice.Groups == null)
                    return false;

                for (int g = 0; g < candidateDevice.Groups.Count; g++)
                {
                    GroupConfig candidateGroup = candidateDevice.Groups[g];
                    if (candidateGroup == null || TagPath.Normalize(candidateGroup.Id) != groupId)
                        continue;

                    group = candidateGroup;
                    tag = FindTagById(candidateGroup.Tags, tagId);
                    return tag != null;
                }

                return false;
            }

            return false;
        }

        private static TagConfig? FindTagById(List<TagConfig>? tags, string normalizedTagId)
        {
            if (tags == null)
                return null;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag != null && TagPath.Normalize(tag.Id) == normalizedTagId)
                    return tag;
            }

            return null;
        }

        private static bool CanRead(TagConfig tag)
        {
            return tag != null && tag.AccessMode != TagAccessMode.WriteOnly;
        }

        /// <summary>
        /// 判断标签是否应交给设备驱动采集，虚拟标签由独立的模型运行服务计算。
        /// </summary>
        private static bool CanDeviceRead(TagConfig tag)
        {
            return tag != null && !tag.IsVirtual && CanRead(tag);
        }

        private static bool CanWrite(TagConfig tag)
        {
            return tag != null && !tag.IsVirtual && tag.AccessMode != TagAccessMode.ReadOnly;
        }

        /// <summary>
        /// 将异步模型推理结果写入统一快照存储并触发历史、MQTT、规则和前端事件。
        /// </summary>
        public void PublishVirtualSnapshot(TagValueSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.TagId) || !IsRunning)
                return;
            UpdateSnapshot(snapshot);
        }

        private void MarkDeviceUnavailableTagsOnce(DeviceRuntimeState deviceState)
        {
            if (deviceState.UnavailableTagsMarked)
                return;

            DeviceConfig device = deviceState.Config;
            MarkReadableTagsUnavailable(device, null, device.Tags);

            if (device.Groups != null)
            {
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || group.Tags == null)
                        continue;

                    if (!group.Enabled)
                    {
                        MarkGroup(device, group, TagQuality.Disabled, "Group is disabled.");
                        continue;
                    }

                    MarkReadableTagsUnavailable(device, group, group.Tags);
                }
            }

            deviceState.UnavailableTagsMarked = true;
        }

        private void MarkReadableTagsUnavailable(DeviceConfig device, GroupConfig? group, IList<TagConfig>? tags)
        {
            if (tags == null)
                return;

            for (int t = 0; t < tags.Count; t++)
            {
                TagConfig tag = tags[t];
                if (tag == null)
                    continue;

                if (tag.IsVirtual)
                    continue;

                if (!tag.Enabled)
                {
                    UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.Disabled, "Tag is disabled."));
                    continue;
                }

                if (!CanRead(tag))
                {
                    UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only."));
                    continue;
                }

                UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.NotConnected, "Device is not connected."));
            }
        }

        private async ValueTask PollTagCollectionAsync(
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            IList<TagConfig>? tags,
            DateTime now,
            int runtimeGeneration,
            DevicePollReadContext pollContext,
            List<PendingTagRead>? sharedBatchReads,
            bool subscriptionFallback,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            if (tags == null)
                return;

            bool supportsBatchRead = pollContext.Client != null && PlcClientInvoker.SupportsBatchRead(pollContext.Client);
            List<PendingTagRead>? batchReads = sharedBatchReads ?? (supportsBatchRead ? new List<PendingTagRead>() : null);

            for (int t = 0; t < tags.Count; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentGeneration(runtimeGeneration))
                    return;

                TagConfig tag = tags[t];
                if (tag == null)
                    continue;

                if (tag.IsVirtual)
                    continue;

                if (!IsDue(tag, now))
                    continue;

                if (!tag.Enabled)
                {
                    if (subscriptionFallback)
                        ScheduleNextSubscriptionFallback(device, group, tag, now);
                    else
                        ScheduleNextRead(device, group, tag, now, false);
                    UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.Disabled, "Tag is disabled."));
                    continue;
                }

                if (!CanRead(tag))
                {
                    if (subscriptionFallback)
                        ScheduleNextSubscriptionFallback(device, group, tag, now);
                    else
                        ScheduleNextRead(device, group, tag, now, false);
                    UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.AccessDenied, "Tag is write-only."));
                    continue;
                }

                CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
                if (!compiledRead.IsStaticallyValid)
                {
                    ScheduleNextRead(device, group, tag, now, true);
                    TagValueSnapshot invalidSnapshot = CreateSnapshot(device, group, tag, TagQuality.ReadError, compiledRead.ValidationError);
                    ApplyTagRuntimeState(invalidSnapshot, compiledRead.Runtime);
                    UpdateSnapshot(invalidSnapshot);
                    continue;
                }

                if (!compiledRead.Runtime.CanProbe(DateTime.UtcNow))
                {
                    ScheduleTagRecoveryProbe(tag, compiledRead.Runtime.NextRecoveryProbeUtc);
                    TagValueSnapshot isolatedSnapshot = CreateSnapshot(
                        device,
                        group,
                        tag,
                        TagQuality.ReadError,
                        "标签已隔离，等待独立恢复探测。");
                    ApplyTagRuntimeState(isolatedSnapshot, compiledRead.Runtime);
                    UpdateSnapshot(isolatedSnapshot);
                    continue;
                }

                if (!pollContext.DeviceConnected || pollContext.Client == null)
                {
                    ScheduleNextRead(device, group, tag, now, true);
                    UpdateSnapshot(CreateSnapshot(device, group, tag, TagQuality.NotConnected, "Device is not connected."));
                    continue;
                }

                if (subscriptionFallback)
                {
                    ScheduleNextSubscriptionFallback(device, group, tag, now);
                    continue;
                }

                if (supportsBatchRead && batchReads != null)
                {
                    AddPendingBatchRead(deviceState, group, tag, batchReads);
                    continue;
                }

                (bool keepConnected, bool readSucceeded) = await ReadTagAsync(
                    pollContext.Client,
                    deviceState,
                    group,
                    tag,
                    runtimeGeneration,
                    cancellationToken).ConfigureAwait(false);
                if (!keepConnected)
                {
                    ScheduleNextRead(device, group, tag, now, true);
                    pollContext.DeviceConnected = false;
                    pollContext.Client = null;
                }
                else
                {
                    if (subscriptionFallback && readSucceeded)
                        ScheduleNextSubscriptionFallback(device, group, tag, now);
                    else
                        ScheduleNextRead(device, group, tag, now, !readSucceeded);
                }
            }

            if (sharedBatchReads == null)
                await ReadPendingBatchTagsAsync(deviceState, batchReads, now, runtimeGeneration, pollContext, subscriptionFallback, cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask ReadPendingBatchTagsAsync(
            DeviceRuntimeState deviceState,
            List<PendingTagRead>? batchReads,
            DateTime now,
            int runtimeGeneration,
            DevicePollReadContext pollContext,
            bool subscriptionFallback,
            CancellationToken cancellationToken)
        {
            if (batchReads != null &&
                batchReads.Count > 0 &&
                pollContext.DeviceConnected &&
                pollContext.Client != null &&
                PlcClientInvoker.SupportsBatchRead(pollContext.Client))
            {
                PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(pollContext.Client);
                if (PlcDriverPluginRegistry.TryGetCapabilities(
                    deviceState.Config.Connection,
                    deviceState.Config.Protocol,
                    out PlcClientCapabilities driverCapabilities) &&
                    driverCapabilities.SupportsBatchRead)
                    capabilities.MaxBatchItems = driverCapabilities.MaxBatchItems;
                int maxBatchItems = capabilities.MaxBatchItems > 0
                    ? capabilities.MaxBatchItems
                    : batchReads.Count;

                for (int offset = 0; offset < batchReads.Count; offset += maxBatchItems)
                {
                    int count = Math.Min(maxBatchItems, batchReads.Count - offset);
                    List<PendingTagRead> chunk = batchReads.GetRange(offset, count);
                    if (await ReadBatchTagsAsync(pollContext.Client, deviceState, chunk, now, runtimeGeneration, subscriptionFallback, cancellationToken).ConfigureAwait(false))
                        continue;

                    MarkUnattemptedBatchReads(deviceState, batchReads, offset + count, now, "协议批读因传输错误提前终止。");
                    pollContext.DeviceConnected = false;
                    pollContext.Client = null;
                    break;
                }

                batchReads.Clear();
            }
        }

        private void AddPendingBatchRead(DeviceRuntimeState deviceState, GroupConfig? group, TagConfig tag, List<PendingTagRead> batchReads)
        {
            CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
            batchReads.Add(new PendingTagRead(group, tag, compiledRead.Request));
        }

        private async ValueTask<bool> ReadBatchTagsAsync(
            IPlcClient client,
            DeviceRuntimeState deviceState,
            IList<PendingTagRead> batchReads,
            DateTime now,
            int runtimeGeneration,
            bool subscriptionFallback,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            List<PlcBatchReadRequest> requests = new List<PlcBatchReadRequest>();
            for (int i = 0; i < batchReads.Count; i++)
            {
                deviceState.TotalReads++;
                requests.Add(batchReads[i].Request);
            }

            IList<PlcBatchReadResult> results;
            try
            {
                results = await ExecutePlcOperationAsync(
                    deviceState,
                    token => PlcClientInvoker.ReadManyAsync(client, requests, token),
                    cancellationToken,
                    PlcOperationTimeoutKind.Batch).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!IsCurrentGeneration(runtimeGeneration))
                    return false;

                PlcReadFailureScope failureScope = PlcFailureClassifier.Classify(
                    ex,
                    IsCommunicationException(ex) ? PlcReadFailureScope.Transport : PlcReadFailureScope.Batch);
                bool timeout = LooksLikeTimeout(ex);
                return await RecordBatchReadExceptionAsync(deviceState, batchReads, now, ex.Message, failureScope, timeout, cancellationToken).ConfigureAwait(false);
            }

            if (!IsCurrentGeneration(runtimeGeneration))
                return true;

            bool stillConnected = true;
            bool connectionDropped = false;
            bool anyReadSucceeded = false;
            for (int i = 0; i < batchReads.Count; i++)
            {
                PendingTagRead read = batchReads[i];
                PlcBatchReadResult? result = results != null && i < results.Count ? results[i] : null;
                if (result != null && result.Success && result.Result != null)
                {
                    ApplyReadSuccess(deviceState, read.Group, read.Tag, result.Result, false);
                    anyReadSucceeded = true;
                    if (subscriptionFallback)
                        ScheduleNextSubscriptionFallback(device, read.Group, read.Tag, now);
                    else
                        ScheduleNextRead(device, read.Group, read.Tag, now, false);
                    continue;
                }

                string errorMessage = result == null ? "Batch read did not return a result for the tag." : result.ErrorMessage;
                PlcReadFailureScope failureScope = result == null ? PlcReadFailureScope.Batch : result.FailureScope;
                bool connectionFailure = IsConnectionFailureScope(failureScope);
                bool keepConnected = await RecordReadFailureAsync(
                    deviceState,
                    read.Group,
                    read.Tag,
                    errorMessage,
                    connectionFailure,
                    LooksLikeTimeoutMessage(errorMessage),
                    connectionFailure && !connectionDropped,
                    failureScope == PlcReadFailureScope.Tag,
                    cancellationToken).ConfigureAwait(false);
                ScheduleNextRead(device, read.Group, read.Tag, now, true);

                if (connectionFailure)
                {
                    connectionDropped = true;
                    MarkUnattemptedBatchReads(deviceState, batchReads, i + 1, now, errorMessage);
                    return false;
                }
                if (!keepConnected)
                    stillConnected = false;
            }

            if (anyReadSucceeded)
                RecordDeviceCommunicationSuccess(deviceState);

            return stillConnected;
        }

        private async ValueTask<bool> RecordBatchReadExceptionAsync(
            DeviceRuntimeState deviceState,
            IList<PendingTagRead> batchReads,
            DateTime now,
            string errorMessage,
            PlcReadFailureScope failureScope,
            bool timeout,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            bool connectionFailure = IsConnectionFailureScope(failureScope);
            bool stillConnected = true;
            bool connectionDropped = false;
            for (int i = 0; i < batchReads.Count; i++)
            {
                PendingTagRead read = batchReads[i];
                bool keepConnected = await RecordReadFailureAsync(
                    deviceState,
                    read.Group,
                    read.Tag,
                    errorMessage,
                    connectionFailure,
                    timeout,
                    connectionFailure && !connectionDropped,
                    false,
                    cancellationToken).ConfigureAwait(false);
                ScheduleNextRead(device, read.Group, read.Tag, now, true);

                if (connectionFailure)
                {
                    connectionDropped = true;
                    MarkUnattemptedBatchReads(deviceState, batchReads, i + 1, now, errorMessage);
                    return false;
                }
                if (!keepConnected)
                    stillConnected = false;
            }

            return stillConnected;
        }

        private void MarkUnattemptedBatchReads(
            DeviceRuntimeState deviceState,
            IList<PendingTagRead> batchReads,
            int startIndex,
            DateTime now,
            string transportError)
        {
            DeviceConfig device = deviceState.Config;
            string message = "设备批次因传输错误提前终止。";
            if (!string.IsNullOrWhiteSpace(transportError))
                message += " " + transportError;

            for (int index = startIndex; index < batchReads.Count; index++)
            {
                PendingTagRead read = batchReads[index];
                deviceState.FailedReads++;
                ScheduleNextRead(device, read.Group, read.Tag, now, true);
                TagValueSnapshot snapshot = CreateSnapshot(device, read.Group, read.Tag, TagQuality.NotConnected, message);
                ApplyTagRuntimeState(snapshot, deviceState.ReadPlan.Get(read.Tag).Runtime);
                UpdateSnapshot(snapshot);
            }
        }

        private static bool IsConnectionFailureScope(PlcReadFailureScope failureScope)
        {
            return PlcBatchReadResult.IsConnectionFailureScope(failureScope);
        }

        private sealed class DevicePollReadContext
        {
            public DevicePollReadContext(IPlcClient? client, bool deviceConnected)
            {
                Client = client;
                DeviceConnected = deviceConnected;
            }

            public IPlcClient? Client { get; set; }
            public bool DeviceConnected { get; set; }
        }

        private sealed class PendingTagRead
        {
            public PendingTagRead(GroupConfig? group, TagConfig tag, PlcBatchReadRequest request)
            {
                Group = group;
                Tag = tag;
                Request = request;
            }

            public GroupConfig? Group { get; private set; }
            public TagConfig Tag { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
        }

        private async ValueTask<(bool KeepConnected, bool ReadSucceeded)> ReadTagAsync(
            IPlcClient client,
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            TagConfig tag,
            int runtimeGeneration,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            try
            {
                deviceState.TotalReads++;
                int count = GetReadCount(tag);
                PlcReadResult result = await ExecutePlcOperationAsync(
                    deviceState,
                    token => PlcClientInvoker.ReadAsync(
                        client,
                        deviceState.ReadPlan.Get(tag).Address,
                        tag.DataType,
                        count,
                        Math.Max(0, tag.ElementOffset),
                        token),
                    cancellationToken).ConfigureAwait(false);
                if (!IsCurrentGeneration(runtimeGeneration))
                    return (true, false);

                ApplyReadSuccess(deviceState, group, tag, result);
                return (true, true);
            }
            catch (Exception ex)
            {
                if (!IsCurrentGeneration(runtimeGeneration))
                    return (false, false);

                bool isCommunicationError = IsCommunicationException(ex);
                bool keepConnected = await RecordReadFailureAsync(
                    deviceState,
                    group,
                    tag,
                    ex.Message,
                    isCommunicationError,
                    LooksLikeTimeout(ex),
                    isCommunicationError,
                    ex is PlcTagException || LooksLikeTagLevelError(ex.Message),
                    cancellationToken).ConfigureAwait(false);
                return (keepConnected, false);
            }
        }

        private void ApplyReadSuccess(
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            TagConfig tag,
            PlcReadResult result,
            bool recordDeviceSuccess = true)
        {
            DeviceConfig device = deviceState.Config;
            object rawValue = result.Value;
            object? scaledValue = TagValueScaler.Scale(rawValue, tag.Scaling);
            deviceState.SuccessfulReads++;
            CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
            compiledRead.Runtime.RecordSuccess();
            deviceState.LastKnownGoodTagId = tag.Id ?? string.Empty;
            if (recordDeviceSuccess)
                RecordDeviceCommunicationSuccess(deviceState);

            TagValueSnapshot snapshot = CreateSnapshot(device, group, tag, TagQuality.Good, string.Empty);
            snapshot.RawValue = rawValue;
            snapshot.RawValueText = PlcValueFormatter.Format(rawValue);
            snapshot.Value = scaledValue ?? string.Empty;
            snapshot.ValueText = TagValueScaler.Format(scaledValue, tag.Scaling);
            snapshot.DataType = result.TypeName;
            ApplyTagRuntimeState(snapshot, compiledRead.Runtime);

            TagValueSnapshot? previousSnapshot;
            TryGetSnapshotById(device.ChannelId, device.Id, group == null ? string.Empty : group.Id, tag.Id ?? string.Empty, out previousSnapshot);
            TagDataCleaner.Clean(snapshot, tag, previousSnapshot);
            ApplyValueTransformCleaning(snapshot, tag, previousSnapshot);

            UpdateSnapshot(snapshot);
        }

        /// <summary>
        /// 在内置清洗后执行标签配置的值处理脚本，并按失败策略更新质量和值。
        /// </summary>
        private void ApplyValueTransformCleaning(
            TagValueSnapshot snapshot,
            TagConfig tag,
            TagValueSnapshot? previousSnapshot)
        {
            DataCleaningConfig cleaning = tag.Cleaning ?? DataCleaningConfig.Default();
            if (!cleaning.Enabled ||
                !cleaning.ValueScriptEnabled ||
                string.IsNullOrWhiteSpace(cleaning.ValueScriptId))
            {
                return;
            }

            object? valueBeforeScript = snapshot.Value;
            string textBeforeScript = snapshot.ValueText;
            ValueTransformExecutionResult result = _valueTransformScripts.Execute(new ValueTransformExecutionRequest
            {
                ScriptId = cleaning.ValueScriptId,
                ScriptVersion = cleaning.ValueScriptVersion,
                Value = snapshot.Value,
                ValueText = snapshot.ValueText,
                DataType = snapshot.DataType,
                ChannelId = snapshot.ChannelId,
                ChannelName = snapshot.ChannelName,
                DeviceId = snapshot.DeviceId,
                DeviceName = snapshot.DeviceName,
                GroupId = snapshot.GroupId,
                GroupName = snapshot.GroupName,
                TagId = snapshot.TagId,
                TagName = snapshot.TagName,
                PointCode = snapshot.PointCode,
                Quality = snapshot.Quality.ToString(),
                Timestamp = new DateTimeOffset(snapshot.Timestamp),
                ExpectedOutputDataType = string.Empty,
                Usage = "TagCleaning",
                TimeoutMilliseconds = Math.Clamp(cleaning.ValueScriptTimeoutMilliseconds, 10, 5000)
            });

            if (result.Success)
            {
                snapshot.Value = result.Value ?? string.Empty;
                snapshot.ValueText = result.ValueText;
                snapshot.DataType = result.OutputDataType;
                AppendCleaningMark(snapshot, "ValueScript", $"值处理脚本 v{cleaning.ValueScriptVersion} 已执行。");
                return;
            }

            string policy = cleaning.ValueScriptFailurePolicy ?? "KeepLastGood";
            string failureMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "值处理脚本执行失败。"
                : result.ErrorMessage;
            if (string.Equals(policy, "KeepLastGood", StringComparison.OrdinalIgnoreCase) &&
                previousSnapshot is not null)
            {
                snapshot.Value = previousSnapshot.Value;
                snapshot.ValueText = previousSnapshot.ValueText;
                snapshot.Unit = previousSnapshot.Unit;
            }
            else
            {
                snapshot.Value = valueBeforeScript ?? string.Empty;
                snapshot.ValueText = textBeforeScript;
            }

            if (!string.Equals(policy, "UseOriginal", StringComparison.OrdinalIgnoreCase))
            {
                snapshot.Quality = TagQuality.Bad;
                snapshot.ErrorMessage = failureMessage;
            }
            AppendCleaningMark(snapshot, "ValueScriptFailed", failureMessage);
        }

        /// <summary>
        /// 在保留已有清洗记录的同时追加脚本处理结果。
        /// </summary>
        private static void AppendCleaningMark(TagValueSnapshot snapshot, string action, string message)
        {
            snapshot.CleaningApplied = true;
            snapshot.CleaningAction = string.IsNullOrWhiteSpace(snapshot.CleaningAction)
                ? action
                : snapshot.CleaningAction + "+" + action;
            snapshot.CleaningMessage = string.IsNullOrWhiteSpace(snapshot.CleaningMessage)
                ? message
                : snapshot.CleaningMessage + " " + message;
        }

        /// <summary>
        /// 将协议专用布尔类型转换为脚本运行时可识别的通用类型名称。
        /// </summary>
        private static string NormalizeValueScriptDataType(string dataType)
        {
            if (string.Equals(dataType, "Coil", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dataType, "DiscreteInput", StringComparison.OrdinalIgnoreCase))
            {
                return "Bool";
            }
            if (string.Equals(dataType, "CoilArray", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dataType, "DiscreteInputArray", StringComparison.OrdinalIgnoreCase))
            {
                return "BoolArray";
            }
            return dataType;
        }

        private async ValueTask<bool> RecordReadFailureAsync(
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            TagConfig tag,
            string errorMessage,
            bool isCommunicationError,
            bool isTimeout,
            bool dropConnection,
            bool isolateTag,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            string message = errorMessage ?? string.Empty;
            deviceState.FailedReads++;
            deviceState.LastFailureTime = DateTime.Now;
            bool retainUdpConnection = isCommunicationError &&
                                       dropConnection &&
                                       isTimeout &&
                                       TryRegisterTransientUdpTimeout(deviceState, message);
            if (isCommunicationError && dropConnection && !retainUdpConnection)
            {
                deviceState.LastError = message;
                deviceState.ProtocolCircuitBreaker.RecordFailure(message);
            }
            if (isTimeout)
                RegisterReadTimeout(deviceState, message);
            if (isCommunicationError && dropConnection && !retainUdpConnection)
                await DropDeviceConnectionAsync(deviceState, message, cancellationToken).ConfigureAwait(false);

            CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
            if (!isCommunicationError && isolateTag)
            {
                bool wasIsolated = compiledRead.Runtime.IsIsolated;
                compiledRead.Runtime.RecordFailure(message);
                if (!wasIsolated && compiledRead.Runtime.IsIsolated)
                {
                    deviceState.SubscriptionFingerprint = string.Empty;
                    deviceState.NextPollUtc = DateTime.MinValue;
                }
            }

            TagValueSnapshot failureSnapshot = CreateSnapshot(device, group, tag, TagQuality.ReadError, message);
            ApplyTagRuntimeState(failureSnapshot, compiledRead.Runtime);
            UpdateSnapshot(failureSnapshot);
            return !isCommunicationError;
        }

        internal static bool TryRegisterTransientUdpTimeout(DeviceRuntimeState deviceState, string message)
        {
            if (!IsUdpTransport(deviceState) || deviceState.ConsecutiveFailures + 1 >= UdpOfflineFailureThreshold)
                return false;

            deviceState.ConsecutiveFailures++;
            deviceState.LastError = message ?? string.Empty;
            deviceState.LastConnectionError = deviceState.LastError;
            deviceState.LastConnectionErrorTime = DateTime.Now;
            deviceState.LastReconnectDelayMs = 0;
            deviceState.NextReconnectUtc = DateTime.MinValue;
            deviceState.NextRecoveryProbeUtc = DateTime.MinValue;
            deviceState.DeviceState = "Degraded";
            deviceState.RecoveryState = "Monitoring";
            deviceState.IsIsolated = false;
            deviceState.ForceStatus("Degraded", DateTime.UtcNow);
            return true;
        }

        private void ScheduleTagRecoveryProbe(TagConfig tag, DateTime nextProbeUtc)
        {
            if (tag == null || nextProbeUtc == DateTime.MinValue || nextProbeUtc == DateTime.MaxValue)
                return;
            _nextReadUtcByTagId[tag.Id] = nextProbeUtc;
        }

        private static void ApplyTagRuntimeState(TagValueSnapshot snapshot, TagRuntimeState runtime)
        {
            snapshot.TagState = runtime.IsIsolated ? "Isolated" : "Active";
            snapshot.IsTagIsolated = runtime.IsIsolated;
            snapshot.IsStaticValidationError = runtime.IsStaticIsolation;
            snapshot.TagConsecutiveFailures = runtime.ConsecutiveFailures;
            snapshot.NextTagRecoveryProbeTime = runtime.NextRecoveryProbeUtc == DateTime.MinValue || runtime.NextRecoveryProbeUtc == DateTime.MaxValue
                ? runtime.NextRecoveryProbeUtc
                : runtime.NextRecoveryProbeUtc.ToLocalTime();
        }

        private async ValueTask DropDeviceConnectionAsync(
            DeviceRuntimeState deviceState,
            string errorMessage,
            CancellationToken cancellationToken)
        {
            deviceState.DisposeSubscription();
            IPlcClient? client = deviceState.Client;
            deviceState.Client = null;
            try
            {
                if (client != null)
                {
                    await ExecutePlcOperationAsync(
                        deviceState,
                        token => PlcClientInvoker.DisconnectAsync(client, token),
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                DisposeClientAsync(client);
            }

            RegisterDeviceFailure(deviceState, errorMessage);
        }

        private static bool ShouldDropDeviceConnectionAfterSubscriptionFailure(
            DeviceRuntimeState deviceState,
            Exception exception)
        {
            if (!IsCommunicationException(exception))
                return false;

            return deviceState == null ||
                   deviceState.Client == null ||
                   !deviceState.Client.IsConnected;
        }

        private async Task HandleUnexpectedPollErrorAsync(DeviceRuntimeState deviceState, Exception ex)
        {
            if (deviceState == null)
                return;

            try
            {
                await deviceState.Actor.ExecuteAsync(async token =>
                {
                    await DropDeviceConnectionAsync(deviceState, ex == null ? string.Empty : ex.Message, token).ConfigureAwait(false);
                }).ConfigureAwait(false);

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
                    current is OperationCanceledException ||
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
                if (current is TimeoutException ||
                    current is OperationCanceledException)
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

        private static bool LooksLikeTimeoutMessage(string? message)
        {
            string text = (message ?? string.Empty).ToLowerInvariant();
            return text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                   text.IndexOf("超时", StringComparison.Ordinal) >= 0;
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

        private async ValueTask WriteTagValueAsync(
            IPlcClient client,
            DeviceRuntimeState deviceState,
            TagConfig tag,
            string valueText,
            CancellationToken cancellationToken)
        {
            DeviceConfig device = deviceState.Config;
            int elementOffset = Math.Max(0, tag.ElementOffset);
            if (tag.DataType == PlcDataType.String)
            {
                MethodInfo? writeWithCount = client.GetType().GetMethod(
                    "Write",
                    new[] { typeof(string), typeof(PlcDataType), typeof(string), typeof(int), typeof(int) });
                if (writeWithCount != null)
                {
                    await ExecutePlcOperationAsync(
                        deviceState,
                        token => PlcClientInvoker.InvokeSynchronousAsync(delegate
                        {
                            try
                            {
                                writeWithCount.Invoke(client, new object[] { ResolveTagAddress(device, tag), tag.DataType, valueText, GetReadCount(tag), elementOffset });
                            }
                            catch (TargetInvocationException ex)
                            {
                                if (ex.InnerException != null)
                                    throw ex.InnerException;
                                throw;
                            }
                        }, token),
                        cancellationToken).ConfigureAwait(false);
                    return;
                }
            }

            await ExecutePlcOperationAsync(
                deviceState,
                token => PlcClientInvoker.WriteAsync(
                    client,
                    ResolveTagAddress(device, tag),
                    tag.DataType,
                    valueText,
                    elementOffset,
                    token),
                cancellationToken).ConfigureAwait(false);
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

            if (device.Protocol == PlcProtocol.Cjt1882004 || device.Protocol == PlcProtocol.Cjt1882018)
            {
                if (string.IsNullOrWhiteSpace(tag.MeterAddress) || string.IsNullOrWhiteSpace(tag.MeterDataIdentifier))
                    return string.Empty;
                string prefix = device.Protocol == PlcProtocol.Cjt1882018 ? "CJ188-2018:" : "CJ188:";
                if (!string.IsNullOrWhiteSpace(tag.MeterType))
                    return prefix + tag.MeterType.Trim() + ":" + tag.MeterAddress.Trim() + ":" + tag.MeterDataIdentifier.Trim();
                return prefix + tag.MeterAddress.Trim() + ":" + tag.MeterDataIdentifier.Trim();
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
            if (!_nextReadUtcByTagId.TryGetValue(tag.Id, out DateTime next))
                return true;

            return now >= next;
        }

        private bool IsCurrentGeneration(int runtimeGeneration)
        {
            return runtimeGeneration != 0 && Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0) == runtimeGeneration;
        }

        private int GetCurrentRuntimeGeneration()
        {
            return Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0);
        }

        private bool IsCurrentDeviceState(DeviceRuntimeState deviceState)
        {
            if (deviceState == null || deviceState.Config == null)
                return false;

            string deviceId = GetDeviceStateKey(deviceState);
            if (string.IsNullOrWhiteSpace(deviceId))
                return false;

            lock (_syncRoot)
            {
                DeviceRuntimeState? currentState;
                return _deviceStatesById.TryGetValue(deviceId, out currentState) &&
                       ReferenceEquals(currentState, deviceState);
            }
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
                return IsBackpressureBlockedNoLock(GetPendingDevicePollCountNoLock(), now, out message);
            }
        }

        private int GetPendingDevicePollCountNoLock()
        {
            return _pendingHighPriorityDevicePolls.Count + _pendingRecoveryDevicePolls.Count + _pendingLowPriorityDevicePolls.Count;
        }

        private static bool IsLowPriorityPoll(DeviceRuntimeState deviceState)
        {
            lock (deviceState.SyncRoot)
            {
                return IsLowPriorityPollUnsafe(deviceState);
            }
        }

        private static bool IsLowPriorityPollUnsafe(DeviceRuntimeState deviceState)
        {
            if (deviceState == null)
                return false;

            if (deviceState.Client != null && deviceState.Client.IsConnected)
                return false;

            if (deviceState.ConsecutiveFailures > 0)
                return true;

            if (deviceState.NextReconnectUtc != DateTime.MinValue)
                return true;

            if (string.Equals(deviceState.StableStatus, "Error", StringComparison.OrdinalIgnoreCase))
                return true;

            CircuitBreakerStatus breaker = deviceState.ProtocolCircuitBreaker.Snapshot();
            return breaker.IsOpen;
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
                _pendingHighPriorityDevicePolls.Clear();
                _pendingRecoveryDevicePolls.Clear();
                _pendingLowPriorityDevicePolls.Clear();
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
            _eventBus.ResetStats();
            Interlocked.Exchange(ref _backpressureActive, 0);
            Volatile.Write(ref _nextScheduleDeviceIndex, 0);
            _maxObservedPendingCount = 0;
            lock (_syncRoot)
            {
                _lastTimeoutTime = DateTime.MinValue;
                _lastTimeoutDeviceName = string.Empty;
                _lastTimeoutMessage = string.Empty;
                _recentPollTimeoutUtc.Clear();
                _recentReadTimeoutUtc.Clear();
                _lastBackpressureTime = DateTime.MinValue;
                _lastBackpressureMessage = string.Empty;
            }
        }

        private void RegisterPollTimeout(DeviceRuntimeState deviceState, long durationMs)
        {
            Interlocked.Increment(ref _totalPollTasksTimedOut);
            string deviceName = deviceState == null || deviceState.Config == null ? string.Empty : deviceState.Config.Name;
            RegisterTimeout(deviceName, "Poll exceeded " + _pollTimeoutMs + " ms. Duration: " + durationMs + " ms.", true);
        }

        private void RegisterReadTimeout(DeviceRuntimeState deviceState, string message)
        {
            Interlocked.Increment(ref _totalReadTimeouts);
            string deviceName = deviceState == null || deviceState.Config == null ? string.Empty : deviceState.Config.Name;
            RegisterTimeout(deviceName, message, false);
        }

        private void RegisterTimeout(string deviceName, string message, bool pollTimeout)
        {
            DateTime nowUtc = DateTime.UtcNow;
            lock (_syncRoot)
            {
                _lastTimeoutTime = DateTime.Now;
                _lastTimeoutDeviceName = deviceName ?? string.Empty;
                _lastTimeoutMessage = message ?? string.Empty;
                Queue<DateTime> target = pollTimeout ? _recentPollTimeoutUtc : _recentReadTimeoutUtc;
                target.Enqueue(nowUtc);
                TrimTimeoutWindowNoLock(target, nowUtc);
            }
        }

        private static void TrimTimeoutWindowNoLock(Queue<DateTime> samples, DateTime nowUtc)
        {
            if (samples == null)
                return;

            DateTime cutoffUtc = nowUtc - SchedulerTimeoutHealthWindow;
            while (samples.Count > 0 && samples.Peek() < cutoffUtc)
                samples.Dequeue();
        }

        private CancellationTokenSource CreatePollCancellationTokenSource(DeviceRuntimeState deviceState)
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(GetPollTimeoutMs());
            return cancellation;
        }

        private async ValueTask ExecutePlcOperationAsync(
            DeviceRuntimeState deviceState,
            Func<CancellationToken, ValueTask> operation,
            CancellationToken parentToken,
            PlcOperationTimeoutKind timeoutKind = PlcOperationTimeoutKind.Device,
            bool linkParentCancellation = true)
        {
            using CancellationTokenSource timeoutCancellation = CreateOperationTimeoutCancellationTokenSource(deviceState == null ? null : deviceState.Config, timeoutKind);
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationTokenSource(parentToken, timeoutCancellation.Token, linkParentCancellation);
            try
            {
                using PhysicalChannelLease channelLease = await _physicalChannelManager
                    .AcquireAsync(deviceState == null ? null : deviceState.Config, operationCancellation.Token)
                    .ConfigureAwait(false);
                try
                {
                    await operation(operationCancellation.Token).ConfigureAwait(false);
                    channelLease.RecordSuccess();
                }
                catch (Exception ex) when (IsCommunicationException(ex))
                {
                    channelLease.RecordFailure(ex.Message);
                    throw;
                }
            }
            catch (OperationCanceledException ex) when (timeoutCancellation.IsCancellationRequested &&
                                                       (!linkParentCancellation || !parentToken.IsCancellationRequested))
            {
                throw new TimeoutException(CreateDeviceOperationTimeoutMessage(deviceState == null ? null : deviceState.Config, timeoutKind), ex);
            }
        }

        private async ValueTask<T> ExecutePlcOperationAsync<T>(
            DeviceRuntimeState deviceState,
            Func<CancellationToken, ValueTask<T>> operation,
            CancellationToken parentToken,
            PlcOperationTimeoutKind timeoutKind = PlcOperationTimeoutKind.Device,
            bool linkParentCancellation = true)
        {
            using CancellationTokenSource timeoutCancellation = CreateOperationTimeoutCancellationTokenSource(deviceState == null ? null : deviceState.Config, timeoutKind);
            using CancellationTokenSource operationCancellation =
                CreateOperationCancellationTokenSource(parentToken, timeoutCancellation.Token, linkParentCancellation);
            try
            {
                using PhysicalChannelLease channelLease = await _physicalChannelManager
                    .AcquireAsync(deviceState == null ? null : deviceState.Config, operationCancellation.Token)
                    .ConfigureAwait(false);
                try
                {
                    T result = await operation(operationCancellation.Token).ConfigureAwait(false);
                    channelLease.RecordSuccess();
                    return result;
                }
                catch (Exception ex) when (IsCommunicationException(ex))
                {
                    channelLease.RecordFailure(ex.Message);
                    throw;
                }
            }
            catch (OperationCanceledException ex) when (timeoutCancellation.IsCancellationRequested &&
                                                       (!linkParentCancellation || !parentToken.IsCancellationRequested))
            {
                throw new TimeoutException(CreateDeviceOperationTimeoutMessage(deviceState == null ? null : deviceState.Config, timeoutKind), ex);
            }
        }

        private CancellationTokenSource CreateOperationTimeoutCancellationTokenSource(
            DeviceConfig? device,
            PlcOperationTimeoutKind timeoutKind)
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(GetPlcOperationTimeoutMs(device, timeoutKind));
            return cancellation;
        }

        private static CancellationTokenSource CreateOperationCancellationTokenSource(
            CancellationToken parentToken,
            CancellationToken timeoutToken,
            bool linkParentCancellation)
        {
            if (linkParentCancellation && parentToken.CanBeCanceled)
                return CancellationTokenSource.CreateLinkedTokenSource(parentToken, timeoutToken);

            return CancellationTokenSource.CreateLinkedTokenSource(timeoutToken);
        }

        private int GetPollTimeoutMs()
        {
            return ClampInterval(_pollTimeoutMs, 100, 86400000);
        }

        private int GetDeviceOperationTimeoutMs(DeviceConfig? device)
        {
            int timeout = device == null || device.Connection == null ? 0 : device.Connection.TimeoutMilliseconds;
            if (timeout <= 0)
                timeout = _pollTimeoutMs;

            return ClampInterval(timeout, 100, GetPollTimeoutMs());
        }

        private int GetPlcOperationTimeoutMs(DeviceConfig? device, PlcOperationTimeoutKind timeoutKind)
        {
            if (timeoutKind == PlcOperationTimeoutKind.Batch)
                return GetBatchOperationTimeoutMs(device);
            if (timeoutKind == PlcOperationTimeoutKind.Subscription)
                return GetSubscriptionOperationTimeoutMs(device);

            return GetDeviceOperationTimeoutMs(device);
        }

        private int GetBatchOperationTimeoutMs(DeviceConfig? device)
        {
            int timeout = Math.Max(GetDeviceOperationTimeoutMs(device), GetPollTimeoutMs());
            return ClampInterval(timeout, 100, 86400000);
        }

        private int GetSubscriptionOperationTimeoutMs(DeviceConfig? device)
        {
            int timeout = Math.Max(GetDeviceOperationTimeoutMs(device), GetPollTimeoutMs());
            timeout = Math.Max(timeout, MinimumSubscriptionOperationTimeoutMs);
            return ClampInterval(timeout, 100, 86400000);
        }

        private string CreateDeviceOperationTimeoutMessage(DeviceConfig? device, PlcOperationTimeoutKind timeoutKind)
        {
            string operationName = timeoutKind == PlcOperationTimeoutKind.Subscription
                ? "PLC subscription operation"
                : timeoutKind == PlcOperationTimeoutKind.Batch
                    ? "PLC batch operation"
                    : "PLC operation";
            return operationName + " timed out after " + GetPlcOperationTimeoutMs(device, timeoutKind) + " ms.";
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

            next = AlignDevicePollTime(deviceState.Config, next, now, true);
            deviceState.NextPollUtc = next;
        }

        private void InitializeDevicePollStaggerNoLock(ProjectConfig config, DateTime nowUtc)
        {
            if (config == null || config.Devices == null)
                return;

            for (int i = 0; i < config.Devices.Count; i++)
            {
                DeviceConfig device = config.Devices[i];
                if (device == null || !device.Enabled || string.IsNullOrWhiteSpace(device.Id) || !HasReadableTagsForStagger(device))
                    continue;

                DeviceRuntimeState? state;
                if (!_deviceStatesById.TryGetValue(device.Id, out state) || state == null)
                    continue;

                lock (state.SyncRoot)
                {
                    state.NextPollUtc = AlignDevicePollTime(device, nowUtc, nowUtc, false);
                }
            }
        }

        private DateTime AlignDevicePollTime(DeviceConfig device, DateTime dueUtc, DateTime referenceUtc, bool allowRecentPreviousPhase)
        {
            if (device == null || dueUtc == DateTime.MinValue)
                return dueUtc;

            int periodMs = GetDevicePollStaggerPeriodMs(device);
            if (periodMs <= _schedulerIntervalMs)
                return dueUtc;

            int toleranceMs = Math.Max(100, _schedulerIntervalMs * 2);
            bool overdue = dueUtc < referenceUtc;
            bool significantlyOverdue = overdue && (referenceUtc - dueUtc).TotalMilliseconds > toleranceMs;
            int offsetMs;
            if (!TryGetDevicePollStaggerOffsetMs(device, periodMs, out offsetMs))
            {
                if (!significantlyOverdue)
                    return dueUtc;
                offsetMs = GetStableDevicePollOffsetMs(device, periodMs);
            }

            if (overdue && !significantlyOverdue)
                return dueUtc;

            DateTime aligned = AlignUtcAfter(dueUtc, periodMs, offsetMs);
            if (allowRecentPreviousPhase)
            {
                DateTime previous = aligned.AddMilliseconds(-periodMs);
                if (previous >= referenceUtc && (dueUtc - previous).TotalMilliseconds <= toleranceMs)
                    aligned = previous;
            }

            if (aligned < referenceUtc)
                aligned = AlignUtcAfter(referenceUtc, periodMs, offsetMs);

            return aligned;
        }

        private DateTime AlignDeviceReadDueTime(DeviceConfig device, int periodMs, DateTime dueUtc, DateTime referenceUtc)
        {
            if (device == null || dueUtc == DateTime.MinValue || periodMs <= _schedulerIntervalMs)
                return dueUtc;

            int offsetMs;
            if (!TryGetDevicePollStaggerOffsetMs(device, periodMs, out offsetMs))
                return dueUtc;

            DateTime aligned = AlignUtcAfter(dueUtc, periodMs, offsetMs);
            DateTime previous = aligned.AddMilliseconds(-periodMs);
            int toleranceMs = Math.Max(100, _schedulerIntervalMs * 2);
            if (previous >= referenceUtc && (dueUtc - previous).TotalMilliseconds <= toleranceMs)
                return previous;

            return aligned;
        }

        private static DateTime AlignUtcAfter(DateTime dueUtc, int periodMs, int offsetMs)
        {
            long dueMs = dueUtc.Ticks / TimeSpan.TicksPerMillisecond;
            int phaseMs = PositiveModulo(dueMs, periodMs);
            int deltaMs = offsetMs - phaseMs;
            if (deltaMs < 0)
                deltaMs += periodMs;
            return dueUtc.AddMilliseconds(deltaMs);
        }

        private static int PositiveModulo(long value, int divisor)
        {
            if (divisor <= 0)
                return 0;

            long result = value % divisor;
            if (result < 0)
                result += divisor;
            return (int)result;
        }

        private static int GetStableDevicePollOffsetMs(DeviceConfig device, int periodMs)
        {
            if (device == null || periodMs <= 0)
                return 0;

            string key = !string.IsNullOrWhiteSpace(device.Id) ? device.Id : device.Name ?? string.Empty;
            unchecked
            {
                uint hash = 2166136261;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= 16777619;
                }

                return (int)(hash % (uint)periodMs);
            }
        }

        private bool TryGetDevicePollStaggerOffsetMs(DeviceConfig device, int periodMs, out int offsetMs)
        {
            offsetMs = 0;
            lock (_syncRoot)
            {
                ProjectConfig? config = _config;
                if (config == null || config.Devices == null || config.Devices.Count <= 1)
                    return false;

                string key = BuildDevicePollStaggerKey(device, periodMs);
                if (!_devicePollStaggerOffsetMsByKey.TryGetValue(key, out offsetMs))
                {
                    RebuildDevicePollStaggerCacheNoLock(config);
                    if (!_devicePollStaggerOffsetMsByKey.TryGetValue(key, out offsetMs))
                        return false;
                }
                return true;
            }
        }

        private void RebuildChannelNameCacheNoLock(ProjectConfig config)
        {
            _channelNamesById.Clear();
            if (config?.Channels == null)
                return;

            for (int i = 0; i < config.Channels.Count; i++)
            {
                ChannelConfig channel = config.Channels[i];
                if (channel != null && !string.IsNullOrWhiteSpace(channel.Id))
                    _channelNamesById[channel.Id] = channel.Name ?? string.Empty;
            }
        }

        private string GetChannelName(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                return string.Empty;

            lock (_syncRoot)
            {
                return _channelNamesById.TryGetValue(channelId, out string? name)
                    ? name ?? string.Empty
                    : string.Empty;
            }
        }

        private void RebuildDevicePollStaggerCacheNoLock(ProjectConfig config)
        {
            _devicePollStaggerOffsetMsByKey.Clear();
            if (config?.Devices == null || config.Devices.Count <= 1)
                return;

            Dictionary<string, List<(DeviceConfig Device, int PeriodMs)>> groups =
                new Dictionary<string, List<(DeviceConfig, int)>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < config.Devices.Count; i++)
            {
                DeviceConfig candidate = config.Devices[i];
                if (candidate == null || !candidate.Enabled || !HasReadableTagsForStagger(candidate))
                    continue;
                int periodMs = GetDevicePollStaggerPeriodMs(candidate);
                string groupKey = candidate.Protocol + "|" + IsUdpTransport(candidate) + "|" + periodMs;
                if (!groups.TryGetValue(groupKey, out List<(DeviceConfig Device, int PeriodMs)>? group))
                {
                    group = new List<(DeviceConfig, int)>();
                    groups[groupKey] = group;
                }
                group.Add((candidate, periodMs));
            }

            foreach (List<(DeviceConfig Device, int PeriodMs)> group in groups.Values)
            {
                for (int index = 0; index < group.Count; index++)
                {
                    (DeviceConfig candidate, int periodMs) = group[index];
                    _devicePollStaggerOffsetMsByKey[BuildDevicePollStaggerKey(candidate, periodMs)] =
                        (int)((long)periodMs * index / group.Count);
                }
            }
        }

        private static string BuildDevicePollStaggerKey(DeviceConfig device, int periodMs)
        {
            string identity = !string.IsNullOrWhiteSpace(device?.Id) ? device.Id : device?.Name ?? string.Empty;
            return identity + "|" + periodMs.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsSameDevicePollStaggerGroup(DeviceConfig device, DeviceConfig candidate, int periodMs)
        {
            if (device == null || candidate == null || !candidate.Enabled || !HasReadableTagsForStagger(candidate))
                return false;

            return candidate.Protocol == device.Protocol &&
                   IsUdpTransport(candidate) == IsUdpTransport(device) &&
                   GetDevicePollStaggerPeriodMs(candidate) == periodMs;
        }

        private static int GetDevicePollStaggerPeriodMs(DeviceConfig device)
        {
            int periodMs = GetFastestReadableScanRateMs(device);
            if (periodMs <= 0)
                periodMs = GetDeviceScanRateMs(device);
            return ClampInterval(periodMs, 100, 86400000);
        }

        private static int GetFastestReadableScanRateMs(DeviceConfig device)
        {
            int scanRate = 0;
            CollectFastestReadableScanRateMs(device, null, device == null ? null : device.Tags, ref scanRate);
            if (device != null && device.Groups != null)
            {
                for (int i = 0; i < device.Groups.Count; i++)
                {
                    GroupConfig group = device.Groups[i];
                    if (group == null || !group.Enabled)
                        continue;
                    CollectFastestReadableScanRateMs(device, group, group.Tags, ref scanRate);
                }
            }

            return scanRate;
        }

        private static void CollectFastestReadableScanRateMs(DeviceConfig device, GroupConfig? group, IList<TagConfig>? tags, ref int scanRate)
        {
            if (device == null || tags == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null || !tag.Enabled || !CanDeviceRead(tag))
                    continue;

                int tagScanRate = GetEffectiveScanRateMs(device, group, tag);
                if (scanRate <= 0 || tagScanRate < scanRate)
                    scanRate = tagScanRate;
            }
        }

        private static bool HasReadableTagsForStagger(DeviceConfig device)
        {
            return GetFastestReadableScanRateMs(device) > 0;
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
                if (tag == null || !tag.Enabled || !CanDeviceRead(tag))
                    continue;

                if (!_nextReadUtcByTagId.TryGetValue(tag.Id, out DateTime tagNext))
                    tagNext = DateTime.UtcNow;

                if (next == DateTime.MinValue || tagNext < next)
                    next = tagNext;
            }
        }

        private void ScheduleNextRead(DeviceConfig device, GroupConfig? group, TagConfig tag, DateTime now, bool failed)
        {
            int scanRate = failed
                ? GetFailureRetryDelayMs(device, tag)
                : GetEffectiveScanRateMs(device, group, tag);
            DateTime next = now.AddMilliseconds(scanRate);
            if (!failed)
                next = AlignDeviceReadDueTime(device, scanRate, next, now);

            _nextReadUtcByTagId[tag.Id] = next;
        }

        private void ScheduleNextSubscriptionFallback(DeviceConfig device, GroupConfig? group, TagConfig tag, DateTime now)
        {
            int interval = GetSubscriptionFallbackIntervalMs(device, group, tag);
            _nextReadUtcByTagId[tag.Id] = now.AddMilliseconds(interval);
        }

        private static int GetSubscriptionFallbackIntervalMs(DeviceConfig device, GroupConfig? group, TagConfig tag)
        {
            int scanRate = GetEffectiveScanRateMs(device, group, tag);
            long interval = Math.Max(scanRate * 10L, 30000L);
            if (interval > 300000L)
                interval = 300000L;
            return (int)interval;
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
                ChannelId = device.ChannelId,
                ChannelName = GetChannelName(device.ChannelId),
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

        private static string CreateDeviceStatusCandidate(DeviceRuntimeState state)
        {
            DeviceConfig device = state.Config;
            if (device == null || !device.Enabled)
                return "Disabled";
            if (state.ConsecutiveFailures > 0)
                return IsUdpTransport(state) && !state.IsIsolated ? "Degraded" : "Error";
            if (state.LastSuccessTime != DateTime.MinValue)
                return "Online";
            return "Offline";
        }

        private static string GetStableDeviceStatus(DeviceRuntimeState state)
        {
            string status = state.StableStatus;
            if (!string.IsNullOrWhiteSpace(status))
                return status;

            return CreateDeviceStatusCandidate(state);
        }

        private static bool IsOnlineDeviceStatus(string status)
        {
            return string.Equals(status, "Online", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUdpTransport(DeviceRuntimeState state)
        {
            return state != null && IsUdpTransport(state.Config);
        }

        private static bool IsUdpTransport(DeviceConfig device)
        {
            return device != null &&
                   device.Connection != null &&
                   device.Connection.Transport == NetworkTransport.Udp;
        }

        private DeviceRuntimeStatus CreateDeviceRuntimeStatus(DeviceRuntimeState state)
        {
            DeviceConfig device = state.Config;
            bool connected = state.Client != null && state.Client.IsConnected;
            long totalReads = state.TotalReads;
            double successRate = totalReads <= 0 ? 0D : Math.Round(state.SuccessfulReads * 100D / totalReads, 2);
            string status = GetStableDeviceStatus(state);
            bool effectiveConnected = IsOnlineDeviceStatus(status);
            PhysicalChannelSnapshot channel = _physicalChannelManager.GetSnapshot(device);

            return new DeviceRuntimeStatus
            {
                ChannelId = device == null ? string.Empty : device.ChannelId,
                ChannelName = device == null ? string.Empty : GetChannelName(device.ChannelId),
                DeviceId = device == null ? string.Empty : device.Id,
                DeviceName = device == null ? string.Empty : device.Name,
                Protocol = device == null ? string.Empty : device.Protocol.ToString(),
                Enabled = device != null && device.Enabled,
                IsConnected = effectiveConnected,
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
                ProtocolCircuitBreaker = state.ProtocolCircuitBreaker.Snapshot(),
                DeviceState = state.DeviceState ?? status,
                TransportConnected = connected,
                IsIsolated = state.IsIsolated,
                RecoveryState = state.RecoveryState ?? string.Empty,
                IsolatedSinceTime = state.IsolatedSinceUtc == DateTime.MinValue ? DateTime.MinValue : state.IsolatedSinceUtc.ToLocalTime(),
                NextRecoveryProbeTime = state.NextRecoveryProbeUtc == DateTime.MinValue ? DateTime.MinValue : state.NextRecoveryProbeUtc.ToLocalTime(),
                ChannelKey = channel.Key,
                ChannelStatus = channel.Status,
                ChannelConsecutiveFailures = channel.ConsecutiveFailures,
                ChannelLastSuccessTime = channel.LastSuccessUtc == DateTime.MinValue ? DateTime.MinValue : channel.LastSuccessUtc.ToLocalTime(),
                ChannelLastFailureTime = channel.LastFailureUtc == DateTime.MinValue ? DateTime.MinValue : channel.LastFailureUtc.ToLocalTime(),
                ChannelLastError = channel.LastError
            };
        }

        private DeviceRuntimeStatus CreateBusyDeviceRuntimeStatus(DeviceRuntimeState state)
        {
            DeviceConfig device = state.Config;
            bool connected = state.Client != null && state.Client.IsConnected;
            long totalReads = state.TotalReads;
            double successRate = totalReads <= 0 ? 0D : Math.Round(state.SuccessfulReads * 100D / totalReads, 2);
            string status = GetStableDeviceStatus(state);
            bool effectiveConnected = IsOnlineDeviceStatus(status);
            PhysicalChannelSnapshot channel = _physicalChannelManager.GetSnapshot(device);

            return new DeviceRuntimeStatus
            {
                ChannelId = device == null ? string.Empty : device.ChannelId,
                ChannelName = device == null ? string.Empty : GetChannelName(device.ChannelId),
                DeviceId = device == null ? string.Empty : device.Id,
                DeviceName = device == null ? string.Empty : device.Name,
                Protocol = device == null ? string.Empty : device.Protocol.ToString(),
                Enabled = device != null && device.Enabled,
                IsConnected = effectiveConnected,
                IsPolling = true,
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
                LastTaskStatus = string.IsNullOrWhiteSpace(state.LastTaskStatus) ? "Polling" : state.LastTaskStatus,
                LastTaskDurationMs = state.LastTaskDurationMs,
                SlowPollCount = state.SlowPollCount,
                TimeoutCount = state.TimeoutCount,
                LastError = state.LastError ?? string.Empty,
                ProtocolCircuitBreaker = state.ProtocolCircuitBreaker.Snapshot(),
                DeviceState = state.DeviceState ?? status,
                TransportConnected = connected,
                IsIsolated = state.IsIsolated,
                RecoveryState = state.RecoveryState ?? string.Empty,
                IsolatedSinceTime = state.IsolatedSinceUtc == DateTime.MinValue ? DateTime.MinValue : state.IsolatedSinceUtc.ToLocalTime(),
                NextRecoveryProbeTime = state.NextRecoveryProbeUtc == DateTime.MinValue ? DateTime.MinValue : state.NextRecoveryProbeUtc.ToLocalTime(),
                ChannelKey = channel.Key,
                ChannelStatus = channel.Status,
                ChannelConsecutiveFailures = channel.ConsecutiveFailures,
                ChannelLastSuccessTime = channel.LastSuccessUtc == DateTime.MinValue ? DateTime.MinValue : channel.LastSuccessUtc.ToLocalTime(),
                ChannelLastFailureTime = channel.LastFailureUtc == DateTime.MinValue ? DateTime.MinValue : channel.LastFailureUtc.ToLocalTime(),
                ChannelLastError = channel.LastError
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
            TagValueSnapshot clone;
            bool changed = _snapshotStore.Upsert(_snapshotsByPath, snapshot, out clone);

            if (changed)
                EnqueueTagValueChanged(clone, Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0));
        }

        private static bool HasTagValueChanged(TagValueSnapshot? previous, TagValueSnapshot current)
        {
            if (previous == null)
                return true;
            if (previous.Timestamp == DateTime.MinValue)
                return true;

            return new RuntimeSnapshotStore().HasChanged(previous, current);
        }

        private void EnqueueTagValueChanged(TagValueSnapshot snapshot, int runtimeGeneration)
        {
            _eventBus.Publish(snapshot, runtimeGeneration);
        }

        private void StartTagValueChangedDispatcher()
        {
            _eventBus.Start();
        }

        private void StopTagValueChangedDispatcher()
        {
            _eventBus.Stop();
        }

        private void ClearTagValueChangedQueue()
        {
            _eventBus.Clear();
        }

        private void DispatchTagValueChanged(TagValueSnapshot snapshot)
        {
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
                    subscriber(this, new TagValueChangedEventArgs(snapshot.Clone()));
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

        private static ReadTagsResponse CreateErrorResponseList(string channelId, string deviceId, string groupId, string tagId, string errorMessage)
        {
            ReadTagsResponse response = new ReadTagsResponse();
            response.Success = false;
            response.Results.Add(CreateErrorResponse(channelId, deviceId, groupId, tagId, errorMessage));
            return response;
        }

        private static ReadTagResponse CreateErrorResponse(string channelId, string deviceId, string groupId, string tagId, string errorMessage)
        {
            return new ReadTagResponse
            {
                Success = false,
                ChannelId = channelId ?? string.Empty,
                DeviceId = deviceId ?? string.Empty,
                GroupId = groupId ?? string.Empty,
                TagId = tagId ?? string.Empty,
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
                return UnscaleWriteValueText(request.ValueText, tag);

            object value = request.Value;
            if (value == null)
                throw new ArgumentException("请输入写入值。");

            object? rawValue = TagValueScaler.Unscale(value, tag.Scaling);
            return FormatWriteValue(rawValue);
        }

        private static string UnscaleWriteValueText(string valueText, TagConfig tag)
        {
            if (tag.Scaling == null || !tag.Scaling.Enabled || !IsNumericWriteDataType(tag.DataType))
                return valueText;

            if (!PlcDataTypeHelper.IsArray(tag.DataType))
            {
                if (!double.TryParse(valueText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double engineeringValue))
                    return valueText;
                return FormatWriteValue(TagValueScaler.Unscale(engineeringValue, tag.Scaling));
            }

            string[] values = SplitWriteValues(valueText);
            double[] engineeringValues = new double[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out engineeringValues[i]))
                    return valueText;
            }

            return FormatWriteValue(TagValueScaler.Unscale(engineeringValues, tag.Scaling));
        }

        private static bool IsNumericWriteDataType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Int16:
                case PlcDataType.UInt16:
                case PlcDataType.Int32:
                case PlcDataType.UInt32:
                case PlcDataType.Int64:
                case PlcDataType.UInt64:
                case PlcDataType.Float:
                case PlcDataType.Double:
                case PlcDataType.Int16Array:
                case PlcDataType.UInt16Array:
                case PlcDataType.Int32Array:
                case PlcDataType.UInt32Array:
                case PlcDataType.Int64Array:
                case PlcDataType.UInt64Array:
                case PlcDataType.FloatArray:
                case PlcDataType.DoubleArray:
                    return true;
                default:
                    return false;
            }
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
                ChannelId = request == null ? string.Empty : request.ChannelId ?? string.Empty,
                DeviceId = request == null ? string.Empty : request.DeviceId ?? string.Empty,
                GroupId = request == null ? string.Empty : request.GroupId ?? string.Empty,
                TagId = request == null ? string.Empty : request.TagId ?? string.Empty,
                ChannelName = request == null ? string.Empty : request.ChannelName ?? string.Empty,
                DeviceName = request == null ? string.Empty : request.DeviceName ?? string.Empty,
                GroupName = request == null ? string.Empty : request.GroupName ?? string.Empty,
                TagName = request == null ? string.Empty : request.TagName ?? string.Empty,
                DataType = request == null ? string.Empty : request.DataType ?? string.Empty,
                Quality = TagQuality.Bad.ToString(),
                Timestamp = DateTime.Now,
                ErrorMessage = errorMessage ?? string.Empty
            };
        }

        private WriteTagResponse CreateWriteRefreshWarningResponse(
            DeviceConfig device,
            GroupConfig? group,
            TagConfig tag,
            string errorMessage)
        {
            return new WriteTagResponse
            {
                Success = true,
                ChannelId = device.ChannelId,
                ChannelName = GetChannelName(device.ChannelId),
                DeviceId = device.Id,
                GroupId = group == null ? string.Empty : group.Id,
                TagId = tag.Id,
                DeviceName = device.Name,
                GroupName = group == null ? string.Empty : group.Name,
                TagName = tag.Name,
                DataType = tag.DataType.ToString(),
                Quality = TagQuality.ReadError.ToString(),
                Timestamp = DateTime.Now,
                ErrorMessage = errorMessage ?? string.Empty,
                CurrentValue = new ReadTagResponse()
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
