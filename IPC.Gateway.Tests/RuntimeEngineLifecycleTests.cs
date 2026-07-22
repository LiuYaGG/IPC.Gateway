/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeEngineLifecycleTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using System.Threading;

namespace IPC.Gateway.Tests;

public sealed class RuntimeEngineLifecycleTests
{
    [Fact]
    public void TryGetSnapshot_MissingTagReturnsNullSnapshot()
    {
        using RuntimeEngine engine = new RuntimeEngine(CreateQuietSchedulerOptions());

        bool found = TryGetSnapshot(engine, "Device", "Group", "Tag", out TagValueSnapshot? snapshot);

        Assert.False(found);
        Assert.Null(snapshot);
    }

    [Fact]
    public void StartStop_UpdatesRunningState()
    {
        using RuntimeEngine engine = new RuntimeEngine(CreateQuietSchedulerOptions());

        engine.Start(new ProjectConfig());
        Assert.True(engine.IsRunning);

        engine.Stop();
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void DevicePollStagger_SpreadsSameProtocolUdpDevicesAcrossScanPeriod()
    {
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 100
        });
        ProjectConfig project = new ProjectConfig();
        project.Devices.Clear();
        for (int i = 0; i < 4; i++)
        {
            DeviceConfig device = new DeviceConfig
            {
                Id = "udp-device-" + i.ToString(),
                Name = "UdpDevice" + i.ToString(),
                Protocol = PlcProtocol.MitsubishiMc,
                DefaultScanRateMs = 1000,
                Connection = new PlcConnectionOptions
                {
                    Transport = NetworkTransport.Udp
                }
            };
            device.Tags.Add(new TagConfig
            {
                Id = "tag-" + i.ToString(),
                Name = "Tag",
                Address = "D0",
                DataType = PlcDataType.Int16,
                Enabled = true
            });
            project.Devices.Add(device);
        }

        typeof(RuntimeEngine)
            .GetField("_config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(engine, project);
        System.Reflection.MethodInfo alignMethod = typeof(RuntimeEngine)
            .GetMethod("AlignDevicePollTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        DateTime dueUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < project.Devices.Count; i++)
        {
            DateTime aligned = (DateTime)alignMethod.Invoke(engine, new object[] { project.Devices[i], dueUtc, dueUtc, false })!;
            Assert.Equal(i * 250, (int)(aligned - dueUtc).TotalMilliseconds);
        }
    }

    [Fact]
    public void DevicePollStagger_SkipsMissedPhaseWhenDeviceIsSlow()
    {
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 100
        });
        ProjectConfig project = CreateStaggeredUdpProject(4);
        typeof(RuntimeEngine)
            .GetField("_config", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(engine, project);
        System.Reflection.MethodInfo alignMethod = typeof(RuntimeEngine)
            .GetMethod("AlignDevicePollTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        DateTime baseUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        DateTime missedDue = baseUtc.AddMilliseconds(1000);
        DateTime slowFinished = baseUtc.AddMilliseconds(1500);
        DateTime alignedAfterSlow = (DateTime)alignMethod.Invoke(engine, new object[] { project.Devices[0], missedDue, slowFinished, true })!;

        DateTime slightlyLate = baseUtc.AddMilliseconds(1100);
        DateTime alignedSlightlyLate = (DateTime)alignMethod.Invoke(engine, new object[] { project.Devices[0], missedDue, slightlyLate, true })!;

        Assert.Equal(baseUtc.AddMilliseconds(2000), alignedAfterSlow);
        Assert.Equal(missedDue, alignedSlightlyLate);
    }

    [Fact]
    public void Start_UsesBatchReadClientWhenDriverSupportsIt()
    {
        string driverId = "test.batch." + Guid.NewGuid().ToString("N");
        BatchReadTestDriver driver = new BatchReadTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            ProtocolDriverCircuitBreaker = new IPC.Gateway.Core.Resilience.CircuitBreakerOptions
            {
                FailureThreshold = 2,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipDevicePoll"
            }
        });

        engine.Start(CreateBatchProject(driverId));

        bool read = SpinWait.SpinUntil(() =>
        {
            BatchReadTestClient? client = driver.Client;
            if (client == null || client.BatchReadCount <= 0 || client.ScalarReadCount != 0)
                return false;

            return TryGetSnapshot(engine, "BatchDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
                   snapshot != null &&
                   snapshot.Quality == TagQuality.Good &&
                   snapshot.ValueText == "11";
        }, TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(read);
        Assert.NotNull(driver.Client);
        Assert.True(driver.Client!.BatchReadCount > 0);
        Assert.Equal(0, driver.Client.ScalarReadCount);
    }

    [Fact]
    public void Start_BatchesDeviceAndGroupTagsInSingleReadMany()
    {
        string driverId = "test.mixed-batch." + Guid.NewGuid().ToString("N");
        BatchReadTestDriver driver = new BatchReadTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        engine.Start(CreateBatchProjectWithGroup(driverId));

        bool read = SpinWait.SpinUntil(() =>
        {
            BatchReadTestClient? client = driver.Client;
            if (client == null || client.BatchReadCount != 1 || client.ScalarReadCount != 0)
                return false;

            return TryGetSnapshot(engine, "MixedBatchDevice", string.Empty, "DeviceTag", out TagValueSnapshot? deviceTag) &&
                   TryGetSnapshot(engine, "MixedBatchDevice", "GroupA", "GroupTagA", out TagValueSnapshot? groupTagA) &&
                   TryGetSnapshot(engine, "MixedBatchDevice", "GroupA", "GroupTagB", out TagValueSnapshot? groupTagB) &&
                   deviceTag != null &&
                   groupTagA != null &&
                   groupTagB != null &&
                   deviceTag.Quality == TagQuality.Good &&
                   groupTagA.Quality == TagQuality.Good &&
                   groupTagB.Quality == TagQuality.Good;
        }, TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(read);
        Assert.NotNull(driver.Client);
        Assert.Equal(1, driver.Client!.BatchReadCount);
        Assert.Equal(new[] { 3 }, driver.Client.BatchSizes);
        Assert.Equal(0, driver.Client.ScalarReadCount);
    }

    [Fact]
    public void Start_UsesAsyncBatchReadClientWhenDriverSupportsIt()
    {
        string driverId = "test.async-batch." + Guid.NewGuid().ToString("N");
        AsyncBatchReadTestDriver driver = new AsyncBatchReadTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        engine.Start(CreateBatchProject(driverId));

        bool read = SpinWait.SpinUntil(() =>
        {
            AsyncBatchReadTestClient? client = driver.Client;
            if (client == null || client.AsyncBatchReadCount <= 0 || client.SyncReadCount != 0)
                return false;

            return TryGetSnapshot(engine, "BatchDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
                   snapshot != null &&
                   snapshot.Quality == TagQuality.Good &&
                   snapshot.ValueText == "31";
        }, TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(read);
        Assert.NotNull(driver.Client);
        Assert.True(driver.Client!.AsyncConnectCount > 0);
        Assert.True(driver.Client.AsyncBatchReadCount > 0);
        Assert.Equal(0, driver.Client.SyncReadCount);
    }

    [Fact]
    public void Start_UsesSubscriptionUpdatesWithoutPollingReads()
    {
        string driverId = "test.subscription." + Guid.NewGuid().ToString("N");
        SubscriptionTestDriver driver = new SubscriptionTestDriver(driverId, failBatchReads: true);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "SubscriptionDevice");
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool subscribed = SpinWait.SpinUntil(() =>
        {
            SubscriptionTestClient? client = driver.Client;
            return client != null &&
                   client.SubscribeCount == 1 &&
                   client.MonitoredCount == 1;
        }, TimeSpan.FromSeconds(3));

        Assert.True(subscribed);
        Assert.NotNull(driver.Client);
        Thread.Sleep(200);
        Assert.Equal(0, driver.Client!.BatchReadCount);
        Assert.Equal(0, driver.Client.DisconnectCount);

        driver.Client!.Publish("A", (short)44);

        bool pushed = SpinWait.SpinUntil(() =>
            TryGetSnapshot(engine, "SubscriptionDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
            snapshot != null &&
            snapshot.Quality == TagQuality.Good &&
            snapshot.ValueText == "44",
            TimeSpan.FromSeconds(3));
        int batchReadsAfterPush = driver.Client.BatchReadCount;
        int disconnectsBeforeStop = driver.Client.DisconnectCount;

        engine.Stop();

        Assert.True(pushed);
        Assert.Equal(0, batchReadsAfterPush);
        Assert.Equal(0, disconnectsBeforeStop);
    }

    [Fact]
    public void Start_MarksUdpSubscriptionOnlineAfterConfirmedUpdate()
    {
        string driverId = "test.udp-subscription." + Guid.NewGuid().ToString("N");
        SubscriptionTestDriver driver = new SubscriptionTestDriver(driverId, failBatchReads: true);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            DeviceStatusRecoveryDebounceCount = 1,
            DeviceStatusRecoveryDebounceMs = 0
        });
        ProjectConfig project = CreateSingleTagProject(
            driverId,
            "UdpSubscriptionDevice",
            NetworkTransport.Udp);
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool subscribed = SpinWait.SpinUntil(() =>
            driver.Client != null &&
            driver.Client.SubscribeCount == 1 &&
            driver.Client.MonitoredCount == 1,
            TimeSpan.FromSeconds(3));

        Assert.True(subscribed);
        driver.Client!.Publish("A", (short)44);

        bool online = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses()
                .FirstOrDefault(item => item.DeviceName == "UdpSubscriptionDevice");
            return status != null &&
                   status.IsConnected &&
                   status.Status == "Online" &&
                   status.SuccessfulReads == 1;
        }, TimeSpan.FromSeconds(3));
        int batchReadsBeforeStop = driver.Client.BatchReadCount;

        engine.Stop();

        Assert.True(online);
        Assert.Equal(0, batchReadsBeforeStop);
    }

    [Fact]
    public void ApplyProject_AddsSubscriptionItemWithoutReconnectingDevice()
    {
        string driverId = "test.subscription-update." + Guid.NewGuid().ToString("N");
        SubscriptionTestDriver driver = new SubscriptionTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "SubscriptionUpdateDevice");
        project.Devices[0].Id = "device-subscription-update";
        project.Devices[0].DefaultScanRateMs = 20;
        project.Devices[0].Tags[0].Id = "tag-a";

        engine.Start(project);

        bool subscribed = SpinWait.SpinUntil(() =>
            driver.Client != null &&
            driver.Client.SubscribeCount == 1 &&
            driver.Client.MonitoredCount == 1,
            TimeSpan.FromSeconds(3));

        Assert.True(subscribed);

        ProjectConfig updated = ProjectConfigCloner.Clone(project)!;
        updated.Devices[0].Tags.Add(new TagConfig
        {
            Id = "tag-b",
            Name = "TagB",
            Address = "B",
            DataType = PlcDataType.Int16
        });

        engine.ApplyProject(updated);

        bool updatedSubscription = SpinWait.SpinUntil(() =>
            driver.Client != null &&
            driver.Client.UpdateCount >= 1 &&
            driver.Client.MonitoredCount == 2,
            TimeSpan.FromSeconds(3));

        Assert.True(updatedSubscription);
        driver.Client!.Publish("A", (short)31);
        driver.Client.Publish("B", (short)42);

        bool oldTagStillUpdates = SpinWait.SpinUntil(() =>
            TryGetSnapshot(engine, "SubscriptionUpdateDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
            snapshot != null &&
            snapshot.Quality == TagQuality.Good &&
            snapshot.ValueText == "31",
            TimeSpan.FromSeconds(3));
        bool newTagUpdates = SpinWait.SpinUntil(() =>
            TryGetSnapshot(engine, "SubscriptionUpdateDevice", string.Empty, "TagB", out TagValueSnapshot? snapshot) &&
            snapshot != null &&
            snapshot.Quality == TagQuality.Good &&
            snapshot.ValueText == "42",
            TimeSpan.FromSeconds(3));
        int connectCount = driver.Client == null ? 0 : driver.Client.ConnectCount;
        int disconnectCount = driver.Client == null ? 0 : driver.Client.DisconnectCount;

        engine.Stop();

        Assert.True(oldTagStillUpdates);
        Assert.True(newTagUpdates);
        Assert.Equal(1, connectCount);
        Assert.Equal(0, disconnectCount);
    }

    [Fact]
    public void Start_CancelsAsyncReadWhenPollTimeoutExpires()
    {
        string driverId = "test.async-timeout." + Guid.NewGuid().ToString("N");
        CancellableAsyncReadTestDriver driver = new CancellableAsyncReadTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 200
        });

        engine.Start(CreateSingleTagProject(driverId, "AsyncTimeoutDevice"));

        bool canceled = SpinWait.SpinUntil(() =>
            driver.Client != null &&
            driver.Client.CancelObservedCount > 0 &&
            engine.GetSchedulerStatus().Timeout.ReadTimeoutCount > 0,
            TimeSpan.FromSeconds(3));
        DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "AsyncTimeoutDevice");

        engine.Stop();

        Assert.True(canceled);
        Assert.NotNull(status);
        Assert.True(status!.FailedReads > 0);
    }

    [Fact]
    public void Start_DoesNotBlockPollingOnSlowTagValueChangedSubscriber()
    {
        string driverId = "test.async-event." + Guid.NewGuid().ToString("N");
        BatchReadTestDriver driver = new BatchReadTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        int eventCount = 0;
        engine.TagValueChanged += delegate
        {
            if (Interlocked.Increment(ref eventCount) == 1)
                Thread.Sleep(800);
        };

        engine.Start(CreateBatchProject(driverId));

        bool completed = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "BatchDevice");
            return status != null &&
                   status.TotalReads > 0 &&
                   string.Equals(status.LastTaskStatus, "Completed", StringComparison.OrdinalIgnoreCase) &&
                   status.LastTaskDurationMs < 500;
        }, TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(completed);
        Assert.True(eventCount > 0);
    }

    [Fact]
    public void Start_PrioritizesReachableDeviceWhenOfflineDeviceIsRetrying()
    {
        string offlineDriverId = "test.offline-priority." + Guid.NewGuid().ToString("N");
        string onlineDriverId = "test.online-priority." + Guid.NewGuid().ToString("N");
        OfflineConnectFailureTestDriver offlineDriver = new OfflineConnectFailureTestDriver(offlineDriverId);
        BatchReadTestDriver onlineDriver = new BatchReadTestDriver(onlineDriverId);
        PlcDriverPluginRegistry.Register(offlineDriver);
        PlcDriverPluginRegistry.Register(onlineDriver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 100,
            MaxConcurrentDevicePolls = 1,
            MaxDevicePollsQueuedPerSchedulerTick = 1,
            BackpressureEnabled = false,
            PollTimeoutMs = 2000
        });

        engine.Start(CreatePriorityProject(offlineDriverId, onlineDriverId));

        bool reachableRead = SpinWait.SpinUntil(() =>
        {
            if (offlineDriver.ConnectAttempts <= 0)
                return false;

            return TryGetSnapshot(engine, "ReachablePriorityDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
                   snapshot != null &&
                   snapshot.Quality == TagQuality.Good &&
                   snapshot.ValueText == "11";
        }, TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(reachableRead);
        Assert.True(offlineDriver.ConnectAttempts > 0);
    }

    [Fact]
    public void Start_DoesNotMarkUnconfirmedUdpStyleClientOnline()
    {
        string driverId = "test.udp-unconfirmed." + Guid.NewGuid().ToString("N");
        UdpUnconfirmedTestDriver driver = new UdpUnconfirmedTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        engine.Start(CreateSingleTagProject(driverId, "UdpDevice", NetworkTransport.Udp));

        bool failedReadObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpDevice");
            return status != null && status.FailedReads > 0;
        }, TimeSpan.FromSeconds(1));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpDevice");

        engine.Stop();

        Assert.True(failedReadObserved);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.IsConnected);
        Assert.NotEqual("Online", deviceStatus.Status);
    }

    [Fact]
    public void Start_DoesNotMarkUdpDeviceOnlineAfterSingleSuccessfulRead()
    {
        string driverId = "test.udp-single-success." + Guid.NewGuid().ToString("N");
        FlakyUdpStyleTestDriver driver = new FlakyUdpStyleTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            DeviceStatusFailureDebounceCount = 2,
            DeviceStatusFailureDebounceMs = 2000,
            DeviceStatusRecoveryDebounceCount = 1,
            DeviceStatusRecoveryDebounceMs = 0
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "UdpFlakyDevice", NetworkTransport.Udp);
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool onlineObserved = false;
        bool failedReadObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpFlakyDevice");
            if (status != null && status.IsConnected)
                onlineObserved = true;
            return status != null && status.SuccessfulReads > 0 && status.FailedReads > 0;
        }, TimeSpan.FromSeconds(2));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpFlakyDevice");

        engine.Stop();

        Assert.True(failedReadObserved);
        Assert.False(onlineObserved);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.IsConnected);
        Assert.Equal("Degraded", deviceStatus.Status);
    }

    [Fact]
    public void Start_MarksUdpDeviceOnlineOnlyAfterStableSuccessfulReads()
    {
        string driverId = "test.udp-stable-success." + Guid.NewGuid().ToString("N");
        StableUdpStyleTestDriver driver = new StableUdpStyleTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            DeviceStatusRecoveryDebounceCount = 1,
            DeviceStatusRecoveryDebounceMs = 0
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "UdpStableDevice", NetworkTransport.Udp);
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool successObservedBeforeRecovery = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpStableDevice");
            return status != null && status.SuccessfulReads > 0;
        }, TimeSpan.FromSeconds(1));
        DeviceRuntimeStatus? earlyStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpStableDevice");

        bool onlineObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "UdpStableDevice");
            return status != null &&
                   status.IsConnected &&
                   string.Equals(status.Status, "Online", StringComparison.OrdinalIgnoreCase);
        }, TimeSpan.FromSeconds(5));

        engine.Stop();

        Assert.True(successObservedBeforeRecovery);
        Assert.NotNull(earlyStatus);
        Assert.False(earlyStatus!.IsConnected);
        Assert.NotEqual("Online", earlyStatus.Status);
        Assert.True(onlineObserved);
    }

    [Fact]
    public void Start_DoesNotMarkDeviceOnlineWhenOnlyTagLevelFailuresAreObserved()
    {
        string driverId = "test.tag-level-failure." + Guid.NewGuid().ToString("N");
        TagLevelFailureTestDriver driver = new TagLevelFailureTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "OpcUaStyleDevice");
        project.Devices[0].DefaultScanRateMs = 20;
        project.Devices[0].FailureRetryDelayMs = 100;
        project.Devices[0].MaxFailureRetryDelayMs = 1000;

        engine.Start(project);

        bool failuresObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaStyleDevice");
            return status != null && status.FailedReads >= 2;
        }, TimeSpan.FromSeconds(3));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaStyleDevice");
        bool snapshotFound = TryGetSnapshot(engine, "OpcUaStyleDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot);

        engine.Stop();

        Assert.True(failuresObserved);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.IsConnected);
        Assert.True(deviceStatus.TransportConnected);
        Assert.NotEqual("Online", deviceStatus.Status);
        Assert.Equal(string.Empty, deviceStatus.LastError);
        Assert.False(deviceStatus.ProtocolCircuitBreaker.IsOpen);
        Assert.True(snapshotFound);
        Assert.NotNull(snapshot);
        Assert.Equal(TagQuality.ReadError, snapshot!.Quality);
    }

    [Fact]
    public void Start_DoesNotMarkDeviceOnlineWhenOnlyBatchLevelFailuresAreObserved()
    {
        string driverId = "test.batch-level-failure." + Guid.NewGuid().ToString("N");
        ScopedBatchFailureTestDriver driver = new ScopedBatchFailureTestDriver(driverId, PlcReadFailureScope.Batch);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "OpcUaBatchStyleDevice");
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool failuresObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaBatchStyleDevice");
            return status != null && status.FailedReads >= 2;
        }, TimeSpan.FromSeconds(3));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaBatchStyleDevice");
        int disposeCountBeforeStop = driver.DisposeCount;

        engine.Stop();

        Assert.True(failuresObserved);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.IsConnected);
        Assert.True(deviceStatus.TransportConnected);
        Assert.NotEqual("Online", deviceStatus.Status);
        Assert.Equal(string.Empty, deviceStatus.LastError);
        Assert.Equal(0, disposeCountBeforeStop);
        Assert.False(deviceStatus.ProtocolCircuitBreaker.IsOpen);
    }

    [Fact]
    public void Start_DropsConnectionWhenBatchReadFailureIsTransportLevel()
    {
        string driverId = "test.transport-level-failure." + Guid.NewGuid().ToString("N");
        ScopedBatchFailureTestDriver driver = new ScopedBatchFailureTestDriver(driverId, PlcReadFailureScope.Transport);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            DeviceStatusFailureDebounceCount = 1,
            DeviceStatusFailureDebounceMs = 0,
            ProtocolDriverCircuitBreaker = new IPC.Gateway.Core.Resilience.CircuitBreakerOptions
            {
                FailureThreshold = 1,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipDevicePoll"
            }
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "TransportFailureDevice");
        project.Devices[0].DefaultScanRateMs = 20;

        engine.Start(project);

        bool connectionDropped = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "TransportFailureDevice");
            return status != null &&
                   status.FailedReads > 0 &&
                   driver.DisposeCount > 0 &&
                   status.ProtocolCircuitBreaker.IsOpen;
        }, TimeSpan.FromSeconds(3));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "TransportFailureDevice");
        int disposeCountBeforeStop = driver.DisposeCount;

        engine.Stop();

        Assert.True(connectionDropped);
        Assert.NotNull(deviceStatus);
        Assert.True(disposeCountBeforeStop > 0);
        Assert.True(deviceStatus!.ProtocolCircuitBreaker.IsOpen);
        Assert.NotEqual(string.Empty, deviceStatus.LastError);
    }

    [Fact]
    public void Start_CountsSingleTransportBatchFailureOnceForCircuitBreaker()
    {
        string driverId = "test.transport-batch-failure-once." + Guid.NewGuid().ToString("N");
        ScopedBatchFailureTestDriver driver = new ScopedBatchFailureTestDriver(driverId, PlcReadFailureScope.Transport);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000,
            ProtocolDriverCircuitBreaker = new IPC.Gateway.Core.Resilience.CircuitBreakerOptions
            {
                FailureThreshold = 3,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipDevicePoll"
            }
        });
        ProjectConfig project = CreateBatchProjectWithGroup(driverId);
        project.Devices[0].Name = "TransportBatchFailureOnceDevice";
        project.Devices[0].DefaultScanRateMs = 60000;
        project.Devices[0].FailureRetryDelayMs = 60000;

        engine.Start(project);

        bool batchFailed = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "TransportBatchFailureOnceDevice");
            return status != null && status.FailedReads >= 3 && driver.DisposeCount > 0;
        }, TimeSpan.FromSeconds(3));
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "TransportBatchFailureOnceDevice");

        engine.Stop();

        Assert.True(batchFailed);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.ProtocolCircuitBreaker.IsOpen);
        Assert.Equal(1, deviceStatus.ProtocolCircuitBreaker.ConsecutiveFailures);
    }

    [Fact]
    public void WriteTag_DoesNotPromoteConnectedDeviceWithoutSuccessfulRead()
    {
        string driverId = "test.tag-level-write-failure." + Guid.NewGuid().ToString("N");
        TagLevelFailureTestDriver driver = new TagLevelFailureTestDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });
        ProjectConfig project = CreateSingleTagProject(driverId, "OpcUaWriteStyleDevice");
        project.Devices[0].DefaultScanRateMs = 20;
        ProjectConfigStore.Normalize(project);

        engine.Start(project);

        bool onlineObserved = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaWriteStyleDevice");
            return status != null && status.TransportConnected;
        }, TimeSpan.FromSeconds(2));
        WriteTagResponse response = engine.WriteTag(new WriteTagRequest
        {
            ChannelId = project.Devices[0].ChannelId,
            DeviceId = project.Devices[0].Id,
            TagId = project.Devices[0].Tags[0].Id,
            DeviceName = "OpcUaWriteStyleDevice",
            TagName = "TagA",
            DataType = PlcDataType.Int16.ToString(),
            ValueText = "1"
        });
        DeviceRuntimeStatus? deviceStatus = engine.GetDeviceStatuses().FirstOrDefault(item => item.DeviceName == "OpcUaWriteStyleDevice");

        engine.Stop();

        Assert.True(onlineObserved);
        Assert.False(response.Success);
        Assert.Contains("BadNodeIdUnknown", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(deviceStatus);
        Assert.False(deviceStatus!.IsConnected);
        Assert.True(deviceStatus.TransportConnected);
        Assert.NotEqual("Online", deviceStatus.Status);
        Assert.Equal(string.Empty, deviceStatus.LastError);
        Assert.False(deviceStatus.ProtocolCircuitBreaker.IsOpen);
    }

    private static RuntimeSchedulerOptions CreateQuietSchedulerOptions()
    {
        return new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 60000
        };
    }

    private static bool TryGetSnapshot(RuntimeEngine engine, string deviceName, string groupName, string tagName, out TagValueSnapshot? snapshot)
    {
        snapshot = engine.GetSnapshots().FirstOrDefault(item =>
            string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.GroupName, groupName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TagName, tagName, StringComparison.OrdinalIgnoreCase));
        return snapshot != null;
    }

    private static ProjectConfig CreateStaggeredUdpProject(int deviceCount)
    {
        ProjectConfig project = new ProjectConfig();
        project.Devices.Clear();
        for (int i = 0; i < deviceCount; i++)
        {
            DeviceConfig device = new DeviceConfig
            {
                Id = "udp-device-" + i.ToString(),
                Name = "UdpDevice" + i.ToString(),
                Protocol = PlcProtocol.MitsubishiMc,
                DefaultScanRateMs = 1000,
                Connection = new PlcConnectionOptions
                {
                    Transport = NetworkTransport.Udp
                }
            };
            device.Tags.Add(new TagConfig
            {
                Id = "tag-" + i.ToString(),
                Name = "Tag",
                Address = "D0",
                DataType = PlcDataType.Int16,
                Enabled = true
            });
            project.Devices.Add(device);
        }

        return project;
    }

    private static ProjectConfig CreateBatchProject(string driverId)
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = "BatchDevice",
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Plugin,
                DriverId = driverId
            },
            DefaultScanRateMs = 1000,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "TagA", Address = "A", DataType = PlcDataType.Int16 },
                new TagConfig { Name = "TagB", Address = "B", DataType = PlcDataType.Int16 }
            }
        };

        return new ProjectConfig
        {
            Devices = new List<DeviceConfig> { device }
        };
    }

    private static ProjectConfig CreateBatchProjectWithGroup(string driverId)
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = "MixedBatchDevice",
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Plugin,
                DriverId = driverId
            },
            DefaultScanRateMs = 60000,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "DeviceTag", Address = "A", DataType = PlcDataType.Int16 }
            },
            Groups = new List<GroupConfig>
            {
                new GroupConfig
                {
                    Name = "GroupA",
                    Tags = new List<TagConfig>
                    {
                        new TagConfig { Name = "GroupTagA", Address = "B", DataType = PlcDataType.Int16 },
                        new TagConfig { Name = "GroupTagB", Address = "C", DataType = PlcDataType.Int16 }
                    }
                }
            }
        };

        return new ProjectConfig
        {
            Devices = new List<DeviceConfig> { device }
        };
    }

    private static ProjectConfig CreateSingleTagProject(string driverId, string deviceName, NetworkTransport transport = NetworkTransport.Tcp)
    {
        DeviceConfig device = new DeviceConfig
        {
            Name = deviceName,
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Plugin,
                DriverId = driverId,
                Transport = transport
            },
            DefaultScanRateMs = 1000,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "TagA", Address = "A", DataType = PlcDataType.Int16 }
            }
        };

        return new ProjectConfig
        {
            Devices = new List<DeviceConfig> { device }
        };
    }

    private static ProjectConfig CreatePriorityProject(string offlineDriverId, string onlineDriverId)
    {
        DeviceConfig offlineDevice = new DeviceConfig
        {
            Name = "OfflinePriorityDevice",
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Plugin,
                DriverId = offlineDriverId
            },
            DefaultScanRateMs = 100,
            FailureRetryDelayMs = 100,
            MaxFailureRetryDelayMs = 100,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "TagA", Address = "A", DataType = PlcDataType.Int16 }
            }
        };

        DeviceConfig onlineDevice = new DeviceConfig
        {
            Name = "ReachablePriorityDevice",
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Plugin,
                DriverId = onlineDriverId
            },
            DefaultScanRateMs = 100,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "TagA", Address = "A", DataType = PlcDataType.Int16 }
            }
        };

        return new ProjectConfig
        {
            Devices = new List<DeviceConfig> { offlineDevice, onlineDevice }
        };
    }

    private sealed class OfflineConnectFailureTestDriver : IProtocolDriver
    {
        private readonly string _driverId;
        private int _connectAttempts;

        public OfflineConnectFailureTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Offline Connect Failure Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int ConnectAttempts => Volatile.Read(ref _connectAttempts);

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new OfflineConnectFailureTestClient(this);
        }

        private void IncrementConnectAttempts()
        {
            Interlocked.Increment(ref _connectAttempts);
        }

        private sealed class OfflineConnectFailureTestClient : IPlcClient
        {
            private readonly OfflineConnectFailureTestDriver _driver;

            public OfflineConnectFailureTestClient(OfflineConnectFailureTestDriver driver)
            {
                _driver = driver;
            }

            public bool IsConnected => false;
            public PlcProtocol Protocol => PlcProtocol.Plugin;

            public void Connect()
            {
                _driver.IncrementConnectAttempts();
                throw new TimeoutException("Offline device did not answer.");
            }

            public void Disconnect()
            {
            }

            public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
            {
                throw new TimeoutException("Offline device did not answer.");
            }

            public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
            {
                throw new TimeoutException("Offline device did not answer.");
            }

            public void Dispose()
            {
                Disconnect();
            }
        }
    }

    private sealed class BatchReadTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public BatchReadTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Batch Read Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public BatchReadTestClient? Client { get; private set; }

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            Client = new BatchReadTestClient();
            return Client;
        }
    }

    private sealed class BatchReadTestClient : IPlcClient, IPlcBatchReadClient
    {
        private readonly object _syncRoot = new object();
        private readonly List<int> _batchSizes = new List<int>();
        private int _batchReadCount;
        private int _scalarReadCount;

        public bool IsConnected { get; private set; }
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int BatchReadCount => Volatile.Read(ref _batchReadCount);
        public int ScalarReadCount => Volatile.Read(ref _scalarReadCount);
        public int[] BatchSizes
        {
            get
            {
                lock (_syncRoot)
                    return _batchSizes.ToArray();
            }
        }

        public void Connect()
        {
            IsConnected = true;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            Interlocked.Increment(ref _scalarReadCount);
            return new PlcReadResult(0, dataType.ToString(), 0);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            Interlocked.Increment(ref _batchReadCount);
            lock (_syncRoot)
                _batchSizes.Add(requests.Count);
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i];
                short value = string.Equals(request.Address, "A", StringComparison.OrdinalIgnoreCase) ? (short)11 : (short)22;
                results.Add(PlcBatchReadResult.FromSuccess(request, new PlcReadResult(0, request.DataType.ToString(), value)));
            }

            return results;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class AsyncBatchReadTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public AsyncBatchReadTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Async Batch Read Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public AsyncBatchReadTestClient? Client { get; private set; }

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            Client = new AsyncBatchReadTestClient();
            return Client;
        }
    }

    private sealed class AsyncBatchReadTestClient : IPlcClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private int _connected;
        private int _asyncConnectCount;
        private int _asyncBatchReadCount;
        private int _syncReadCount;

        public bool IsConnected => Volatile.Read(ref _connected) == 1;
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int AsyncConnectCount => Volatile.Read(ref _asyncConnectCount);
        public int AsyncBatchReadCount => Volatile.Read(ref _asyncBatchReadCount);
        public int SyncReadCount => Volatile.Read(ref _syncReadCount);

        public void Connect()
        {
            throw new InvalidOperationException("Sync connect path should not be used.");
        }

        public void Disconnect()
        {
            Volatile.Write(ref _connected, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            Interlocked.Increment(ref _syncReadCount);
            throw new InvalidOperationException("Sync read path should not be used.");
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new InvalidOperationException("Sync write path should not be used.");
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _asyncConnectCount);
            Volatile.Write(ref _connected, 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Volatile.Write(ref _connected, 0);
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Scalar async read path should not be used.");
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _asyncBatchReadCount);
            IList<PlcBatchReadResult> results = requests
                .Select(request => PlcBatchReadResult.FromSuccess(
                    request,
                    new PlcReadResult(0, request.DataType.ToString(), (short)31)))
                .ToList();
            return new ValueTask<IList<PlcBatchReadResult>>(results);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class SubscriptionTestDriver : IProtocolDriver
    {
        private readonly string _driverId;
        private readonly bool _failBatchReads;

        public SubscriptionTestDriver(string driverId, bool failBatchReads = false)
        {
            _driverId = driverId;
            _failBatchReads = failBatchReads;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Subscription Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public SubscriptionTestClient? Client { get; private set; }

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            Client = new SubscriptionTestClient(_failBatchReads);
            return Client;
        }
    }

    private sealed class SubscriptionTestClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcSubscriptionClient
    {
        private readonly object _syncRoot = new object();
        private readonly bool _failBatchReads;
        private List<PlcSubscriptionRequest> _requests = new List<PlcSubscriptionRequest>();
        private Func<PlcSubscriptionUpdate, ValueTask>? _onUpdate;
        private int _connected;
        private int _connectCount;
        private int _disconnectCount;
        private int _batchReadCount;
        private int _subscribeCount;
        private int _updateCount;

        public SubscriptionTestClient(bool failBatchReads)
        {
            _failBatchReads = failBatchReads;
        }

        public bool IsConnected => Volatile.Read(ref _connected) == 1;
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int DisconnectCount => Volatile.Read(ref _disconnectCount);
        public int BatchReadCount => Volatile.Read(ref _batchReadCount);
        public int SubscribeCount => Volatile.Read(ref _subscribeCount);
        public int UpdateCount => Volatile.Read(ref _updateCount);
        public int MonitoredCount
        {
            get
            {
                lock (_syncRoot)
                    return _requests.Count;
            }
        }

        public void Connect()
        {
            Interlocked.Increment(ref _connectCount);
            Volatile.Write(ref _connected, 1);
        }

        public void Disconnect()
        {
            Interlocked.Increment(ref _disconnectCount);
            Volatile.Write(ref _connected, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            return new PlcReadResult(0, dataType.ToString(), GetValue(address));
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            Interlocked.Increment(ref _batchReadCount);
            if (_failBatchReads)
                throw new PlcCommunicationException("Subscription client should not be polled while the subscription is active.");

            return requests
                .Select(request => PlcBatchReadResult.FromSuccess(
                    request,
                    new PlcReadResult(0, request.DataType.ToString(), GetValue(request.Address))))
                .ToList();
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
        }

        public ValueTask<IPlcSubscription> SubscribeAsync(
            IList<PlcSubscriptionRequest> requests,
            PlcSubscriptionOptions options,
            Func<PlcSubscriptionUpdate, ValueTask> onUpdate,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _subscribeCount);
            lock (_syncRoot)
            {
                _requests = requests.ToList();
                _onUpdate = onUpdate;
            }

            return new ValueTask<IPlcSubscription>(new SubscriptionTestHandle(this));
        }

        public void Publish(string address, short value)
        {
            PlcSubscriptionRequest? request;
            Func<PlcSubscriptionUpdate, ValueTask>? onUpdate;
            lock (_syncRoot)
            {
                request = _requests.FirstOrDefault(item => string.Equals(item.Address, address, StringComparison.OrdinalIgnoreCase));
                onUpdate = _onUpdate;
            }

            if (request == null || onUpdate == null || !IsConnected)
                return;

            ValueTask updateTask = onUpdate(PlcSubscriptionUpdate.FromSuccess(
                request,
                new PlcReadResult(0, request.DataType.ToString(), value)));
            if (!updateTask.IsCompletedSuccessfully)
                updateTask.AsTask().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Disconnect();
        }

        private void UpdateSubscription(IList<PlcSubscriptionRequest> requests)
        {
            Interlocked.Increment(ref _updateCount);
            lock (_syncRoot)
                _requests = requests.ToList();
        }

        private static short GetValue(string address)
        {
            return string.Equals(address, "B", StringComparison.OrdinalIgnoreCase) ? (short)20 : (short)10;
        }

        private sealed class SubscriptionTestHandle : IPlcSubscription
        {
            private readonly SubscriptionTestClient _client;
            private int _active = 1;

            public SubscriptionTestHandle(SubscriptionTestClient client)
            {
                _client = client;
            }

            public bool IsActive => Volatile.Read(ref _active) == 1 && _client.IsConnected;
            public IReadOnlyCollection<string> MonitoredKeys
            {
                get
                {
                    lock (_client._syncRoot)
                        return _client._requests.Select(item => item.Key).ToArray();
                }
            }

            public ValueTask UpdateAsync(
                IList<PlcSubscriptionRequest> requests,
                PlcSubscriptionOptions options,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _client.UpdateSubscription(requests);
                return ValueTask.CompletedTask;
            }

            public void Dispose()
            {
                Volatile.Write(ref _active, 0);
            }
        }
    }

    private sealed class CancellableAsyncReadTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public CancellableAsyncReadTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Cancellable Async Read Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public CancellableAsyncReadTestClient? Client { get; private set; }

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            Client = new CancellableAsyncReadTestClient();
            return Client;
        }
    }

    private sealed class CancellableAsyncReadTestClient : IPlcClient, IAsyncPlcClient
    {
        private int _connected;
        private int _cancelObservedCount;

        public bool IsConnected => Volatile.Read(ref _connected) == 1;
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int CancelObservedCount => Volatile.Read(ref _cancelObservedCount);

        public void Connect()
        {
            throw new InvalidOperationException("Sync connect path should not be used.");
        }

        public void Disconnect()
        {
            Volatile.Write(ref _connected, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            throw new InvalidOperationException("Sync read path should not be used.");
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new InvalidOperationException("Sync write path should not be used.");
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _connected, 1);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            Volatile.Write(ref _connected, 0);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelObservedCount);
                throw;
            }

            return new PlcReadResult(0, dataType.ToString(), (short)0);
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class TagLevelFailureTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public TagLevelFailureTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Tag Level Failure Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new TagLevelFailureTestClient();
        }
    }

    private sealed class TagLevelFailureTestClient : IPlcClient, IPlcBatchReadClient
    {
        public bool IsConnected { get; private set; }
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public void Connect()
        {
            IsConnected = true;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            throw new PlcTagException("OPC UA read failed for " + address + ": BadNodeIdUnknown");
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i];
                results.Add(PlcBatchReadResult.FromFailure(
                    request,
                    "OPC UA read failed for " + request.Address + ": BadNodeIdUnknown",
                    false));
            }

            return results;
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new PlcTagException("OPC UA write failed for " + address + ": BadNodeIdUnknown");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class ScopedBatchFailureTestDriver : IProtocolDriver
    {
        private readonly string _driverId;
        private readonly PlcReadFailureScope _failureScope;
        private int _disposeCount;

        public ScopedBatchFailureTestDriver(string driverId, PlcReadFailureScope failureScope)
        {
            _driverId = driverId;
            _failureScope = failureScope;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Scoped Batch Failure Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new ScopedBatchFailureTestClient(this, _failureScope);
        }

        private void RecordDispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }

        private sealed class ScopedBatchFailureTestClient : IPlcClient, IPlcBatchReadClient
        {
            private readonly ScopedBatchFailureTestDriver _driver;
            private readonly PlcReadFailureScope _failureScope;

            public ScopedBatchFailureTestClient(ScopedBatchFailureTestDriver driver, PlcReadFailureScope failureScope)
            {
                _driver = driver;
                _failureScope = failureScope;
            }

            public bool IsConnected { get; private set; }
            public PlcProtocol Protocol => PlcProtocol.Plugin;

            public void Connect()
            {
                IsConnected = true;
            }

            public void Disconnect()
            {
                IsConnected = false;
            }

            public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
            {
                throw new PlcCommunicationException("Transport failed.");
            }

            public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
            {
                List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
                foreach (PlcBatchReadRequest request in requests)
                    results.Add(PlcBatchReadResult.FromFailure(request, "Scoped batch failure.", _failureScope));
                return results;
            }

            public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
            {
            }

            public void Dispose()
            {
                Disconnect();
                _driver.RecordDispose();
            }
        }
    }

    private sealed class UdpUnconfirmedTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public UdpUnconfirmedTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "UDP Unconfirmed Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new UdpUnconfirmedTestClient();
        }
    }

    private sealed class UdpUnconfirmedTestClient : IPlcClient
    {
        public bool IsConnected => false;
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public void Connect()
        {
        }

        public void Disconnect()
        {
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            throw new TimeoutException("UDP receive timed out.");
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new TimeoutException("UDP receive timed out.");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class FlakyUdpStyleTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public FlakyUdpStyleTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Flaky UDP Style Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new FlakyUdpStyleTestClient();
        }
    }

    private sealed class FlakyUdpStyleTestClient : IPlcClient
    {
        private int _confirmed;
        private int _readCount;

        public bool IsConnected => Volatile.Read(ref _confirmed) == 1;
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public void Connect()
        {
        }

        public void Disconnect()
        {
            Volatile.Write(ref _confirmed, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                Volatile.Write(ref _confirmed, 1);
                return new PlcReadResult(0, dataType.ToString(), (short)7);
            }

            Volatile.Write(ref _confirmed, 0);
            throw new TimeoutException("UDP receive timed out.");
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new TimeoutException("UDP receive timed out.");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    private sealed class StableUdpStyleTestDriver : IProtocolDriver
    {
        private readonly string _driverId;

        public StableUdpStyleTestDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Stable UDP Style Test Driver";
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public bool Supports(PlcConnectionOptions options)
        {
            return options != null && string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return new StableUdpStyleTestClient();
        }
    }

    private sealed class StableUdpStyleTestClient : IPlcClient
    {
        private int _confirmed;

        public bool IsConnected => Volatile.Read(ref _confirmed) == 1;
        public PlcProtocol Protocol => PlcProtocol.Plugin;

        public void Connect()
        {
        }

        public void Disconnect()
        {
            Volatile.Write(ref _confirmed, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            Volatile.Write(ref _confirmed, 1);
            return new PlcReadResult(0, dataType.ToString(), (short)17);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
