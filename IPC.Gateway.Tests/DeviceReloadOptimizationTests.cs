using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using System.Threading;

namespace IPC.Gateway.Tests;

public sealed class DeviceReloadOptimizationTests
{
    [Fact]
    public void IsSameDeviceUpdate_IgnoresNonEditableTagPayloadAndNormalizesConnectionProtocol()
    {
        DeviceConfig current = CreateVirtualDevice("CompareDevice");
        DeviceConfig input = ProjectConfigCloner.Clone(new ProjectConfig
        {
            Devices = new List<DeviceConfig> { current }
        }).Devices[0];

        input.Connection.Protocol = PlcProtocol.ModbusTcp;
        input.Tags.Add(new TagConfig { Name = "PayloadOnlyTag", Address = "D2" });

        Assert.True(DeviceConfigComparer.IsSameDeviceUpdate(current, input));

        input.Connection.Host = "changed-host";

        Assert.False(DeviceConfigComparer.IsSameDeviceUpdate(current, input));
    }

    [Fact]
    public void CanReuseRuntimeStateForEnabledChange_OnlyMatchesEnabledToggle()
    {
        DeviceConfig current = CreateVirtualDevice("ToggleCompareDevice");
        DeviceConfig disabled = CloneDevice(current);
        disabled.Enabled = false;
        disabled.Tags.Add(new TagConfig { Id = Guid.NewGuid().ToString("N"), Name = "AddedWhileDisabled", Address = "D2" });

        Assert.True(DeviceConfigComparer.CanReuseRuntimeStateForEnabledChange(current, disabled));

        DeviceConfig unchanged = CloneDevice(current);
        Assert.False(DeviceConfigComparer.CanReuseRuntimeStateForEnabledChange(current, unchanged));

        disabled.Connection.Host = "changed-host";
        Assert.False(DeviceConfigComparer.CanReuseRuntimeStateForEnabledChange(current, disabled));
    }

    [Fact]
    public void CanReuseRuntimeState_IgnoresTagsGroupsAndDisplayName()
    {
        DeviceConfig current = CreateVirtualDevice("RuntimeReuseCompareDevice");
        DeviceConfig changed = CloneDevice(current);
        changed.Name = "RenamedRuntimeReuseCompareDevice";
        changed.Tags.Add(new TagConfig { Id = Guid.NewGuid().ToString("N"), Name = "AddedTag", Address = "D2" });
        changed.Groups.Add(new GroupConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "AddedGroup",
            Enabled = true,
            Tags = new List<TagConfig>
            {
                new TagConfig { Id = Guid.NewGuid().ToString("N"), Name = "AddedGroupTag", Address = "D3" }
            }
        });

        Assert.True(DeviceConfigComparer.CanReuseRuntimeState(current, changed));

        changed.Enabled = false;
        Assert.False(DeviceConfigComparer.CanReuseRuntimeState(current, changed));

        changed = CloneDevice(current);
        changed.Connection.Host = "changed-host";
        Assert.False(DeviceConfigComparer.CanReuseRuntimeState(current, changed));
    }

    [Fact]
    public void ReuseConfig_EnabledTransitionClearsErrorBackoffAndCircuitBreaker()
    {
        DeviceConfig enabled = CreateVirtualDevice("ToggleStateDevice");
        DeviceRuntimeState state = new DeviceRuntimeState(enabled, new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            SuccessThreshold = 1,
            BreakDurationSeconds = 30
        });

        PutStateInError(state, "down");

        DeviceConfig disabled = CloneDevice(enabled);
        disabled.Enabled = false;

        DeviceRuntimeConfigTransition disabledTransition = state.ReuseConfig(disabled);

        Assert.Equal(DeviceRuntimeConfigTransition.Disabled, disabledTransition);
        Assert.Equal("Disabled", state.StableStatus);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Equal(DateTime.MinValue, state.NextReconnectUtc);
        Assert.Equal(DateTime.MinValue, state.NextPollUtc);
        Assert.Equal("Closed", state.ProtocolCircuitBreaker.Snapshot().State);

        PutStateInError(state, "stale error");

        DeviceConfig reenabled = CloneDevice(disabled);
        reenabled.Enabled = true;

        DeviceRuntimeConfigTransition enabledTransition = state.ReuseConfig(reenabled);

        Assert.Equal(DeviceRuntimeConfigTransition.Enabled, enabledTransition);
        Assert.Equal("Offline", state.StableStatus);
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.Equal(DateTime.MinValue, state.NextReconnectUtc);
        Assert.Equal(DateTime.MinValue, state.NextPollUtc);
        Assert.Equal(string.Empty, state.LastError);
        Assert.Equal(string.Empty, state.LastConnectionError);
        Assert.False(state.UnavailableTagsMarked);
        Assert.Equal("Closed", state.ProtocolCircuitBreaker.Snapshot().State);
    }

    [Fact]
    public void ApplyProject_ReusesUnchangedDeviceRuntimeState()
    {
        ProjectConfig project = new ProjectConfig
        {
            Devices = new List<DeviceConfig> { CreateVirtualDevice("ReuseDevice") }
        };

        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        engine.Start(project);

        bool online = SpinWait.SpinUntil(() =>
        {
            DeviceRuntimeStatus? status = GetDeviceStatus(engine, "ReuseDevice");
            return status != null &&
                   status.IsConnected &&
                   status.SuccessfulReads > 0 &&
                   TryGetSnapshot(engine, "ReuseDevice", string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
                   snapshot != null &&
                   snapshot.Quality == TagQuality.Good;
        }, TimeSpan.FromSeconds(3));

        Assert.True(online);

        DeviceRuntimeStatus before = GetDeviceStatus(engine, "ReuseDevice")!;
        engine.ApplyProject(ProjectConfigCloner.Clone(project));
        DeviceRuntimeStatus after = GetDeviceStatus(engine, "ReuseDevice")!;

        engine.Stop();

        Assert.True(after.IsConnected);
        Assert.Equal("Online", after.Status);
        Assert.True(after.TotalReads >= before.TotalReads);
        Assert.True(after.SuccessfulReads >= before.SuccessfulReads);
    }

    [Fact]
    public void ApplyProject_AddsReadableTagWithoutRecreatingClientAndPollsImmediately()
    {
        string driverId = "test.hot-tag-reload." + Guid.NewGuid().ToString("N");
        CountingBatchReadDriver driver = new CountingBatchReadDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        ProjectConfig project = new ProjectConfig
        {
            Devices = new List<DeviceConfig> { CreateDriverDevice(driverId, "HotTagReloadDevice") }
        };

        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        try
        {
            engine.Start(project);

            bool initiallyRead = SpinWait.SpinUntil(() =>
            {
                DeviceRuntimeStatus? status = GetDeviceStatus(engine, "HotTagReloadDevice");
                return status != null &&
                       status.IsConnected &&
                       driver.CreateClientCount == 1 &&
                       driver.ConnectCount == 1 &&
                       IsTagGood(engine, "HotTagReloadDevice", "TagA", "11");
            }, TimeSpan.FromSeconds(3));

            Assert.True(initiallyRead);
            DeviceRuntimeStatus before = GetDeviceStatus(engine, "HotTagReloadDevice")!;

            ProjectConfig updated = ProjectConfigCloner.Clone(project);
            updated.Devices[0].Tags.Add(new TagConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "TagB",
                Address = "D2",
                DataType = PlcDataType.Int16,
                Enabled = true,
                AccessMode = TagAccessMode.ReadWrite,
                ScanRateMs = 60000
            });

            engine.ApplyProject(updated);

            DeviceRuntimeStatus afterApply = GetDeviceStatus(engine, "HotTagReloadDevice")!;
            Assert.True(afterApply.IsConnected);
            Assert.Equal("Online", afterApply.Status);
            Assert.Equal(1, driver.CreateClientCount);
            Assert.Equal(1, driver.ConnectCount);
            Assert.Equal(0, driver.DisposeCount);

            bool addedTagRead = SpinWait.SpinUntil(() =>
                IsTagGood(engine, "HotTagReloadDevice", "TagB", "22"),
                TimeSpan.FromSeconds(3));
            DeviceRuntimeStatus afterRead = GetDeviceStatus(engine, "HotTagReloadDevice")!;

            Assert.True(addedTagRead);
            Assert.True(afterRead.SuccessfulReads > before.SuccessfulReads);
            Assert.Equal(1, driver.CreateClientCount);
            Assert.Equal(1, driver.ConnectCount);
            Assert.Equal(0, driver.DisposeCount);
        }
        finally
        {
            engine.Stop();
        }
    }

    [Fact]
    public void ApplyProject_ReenabledDevicePollsImmediatelyAndRestoresOnlineStatus()
    {
        ProjectConfig project = new ProjectConfig
        {
            Devices = new List<DeviceConfig> { CreateVirtualDevice("ToggleRuntimeDevice") }
        };

        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions
        {
            SchedulerIntervalMs = 20,
            MaxConcurrentDevicePolls = 1,
            PollTimeoutMs = 2000
        });

        engine.Start(project);

        bool initiallyOnline = SpinWait.SpinUntil(() => IsDeviceOnlineWithGoodTag(engine, "ToggleRuntimeDevice"), TimeSpan.FromSeconds(3));
        Assert.True(initiallyOnline);

        ProjectConfig disabledProject = ProjectConfigCloner.Clone(project);
        disabledProject.Devices[0].Enabled = false;
        engine.ApplyProject(disabledProject);

        DeviceRuntimeStatus disabledStatus = GetDeviceStatus(engine, "ToggleRuntimeDevice")!;
        Assert.Equal("Disabled", disabledStatus.Status);
        Assert.False(disabledStatus.IsConnected);
        Assert.True(TryGetSnapshot(engine, "ToggleRuntimeDevice", string.Empty, "TagA", out TagValueSnapshot? disabledSnapshot));
        Assert.NotNull(disabledSnapshot);
        Assert.Equal(TagQuality.Disabled, disabledSnapshot!.Quality);

        ProjectConfig enabledProject = ProjectConfigCloner.Clone(disabledProject);
        enabledProject.Devices[0].Enabled = true;
        engine.ApplyProject(enabledProject);

        DeviceRuntimeStatus immediateStatus = GetDeviceStatus(engine, "ToggleRuntimeDevice")!;
        Assert.NotEqual("Error", immediateStatus.Status);

        bool recovered = SpinWait.SpinUntil(() => IsDeviceOnlineWithGoodTag(engine, "ToggleRuntimeDevice"), TimeSpan.FromSeconds(3));

        engine.Stop();

        Assert.True(recovered);
    }

    private static DeviceRuntimeStatus? GetDeviceStatus(RuntimeEngine engine, string deviceName)
    {
        return engine.GetDeviceStatuses().FirstOrDefault(item =>
            string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDeviceOnlineWithGoodTag(RuntimeEngine engine, string deviceName)
    {
        DeviceRuntimeStatus? status = GetDeviceStatus(engine, deviceName);
        return status != null &&
               status.IsConnected &&
               status.Status == "Online" &&
               status.SuccessfulReads > 0 &&
               TryGetSnapshot(engine, deviceName, string.Empty, "TagA", out TagValueSnapshot? snapshot) &&
               snapshot != null &&
               snapshot.Quality == TagQuality.Good;
    }

    private static bool IsTagGood(RuntimeEngine engine, string deviceName, string tagName, string valueText)
    {
        return TryGetSnapshot(engine, deviceName, string.Empty, tagName, out TagValueSnapshot? snapshot) &&
               snapshot != null &&
               snapshot.Quality == TagQuality.Good &&
               string.Equals(snapshot.ValueText, valueText, StringComparison.Ordinal);
    }

    private static bool TryGetSnapshot(RuntimeEngine engine, string deviceName, string groupName, string tagName, out TagValueSnapshot? snapshot)
    {
        snapshot = engine.GetSnapshots().FirstOrDefault(item =>
            string.Equals(item.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.GroupName, groupName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TagName, tagName, StringComparison.OrdinalIgnoreCase));
        return snapshot != null;
    }

    private static void PutStateInError(DeviceRuntimeState state, string message)
    {
        state.ConsecutiveFailures = 3;
        state.NextReconnectUtc = DateTime.UtcNow.AddMinutes(1);
        state.NextPollUtc = state.NextReconnectUtc;
        state.LastReconnectDelayMs = 60000;
        state.LastError = message;
        state.LastConnectionError = message;
        state.LastConnectionErrorTime = DateTime.Now;
        state.UnavailableTagsMarked = true;
        state.ProtocolCircuitBreaker.RecordFailure(message);
        state.ForceStatus("Error", DateTime.UtcNow);
    }

    private static DeviceConfig CloneDevice(DeviceConfig device)
    {
        return ProjectConfigCloner.Clone(new ProjectConfig
        {
            Devices = new List<DeviceConfig> { device }
        }).Devices[0];
    }

    private static DeviceConfig CreateVirtualDevice(string name)
    {
        return new DeviceConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Enabled = true,
            Protocol = PlcProtocol.VirtualPlc,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = name,
                Port = 0,
                Transport = NetworkTransport.Tcp,
                TimeoutMilliseconds = 1000
            },
            DefaultScanRateMs = 20,
            FailureRetryDelayMs = 1000,
            MaxFailureRetryDelayMs = 30000,
            Tags = new List<TagConfig>
            {
                new TagConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "TagA",
                    Address = "D1",
                    DataType = PlcDataType.Int16,
                    Enabled = true,
                    AccessMode = TagAccessMode.ReadWrite,
                    ScanRateMs = 20
                }
            },
            Groups = new List<GroupConfig>()
        };
    }

    private static DeviceConfig CreateDriverDevice(string driverId, string name)
    {
        return new DeviceConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Enabled = true,
            Protocol = PlcProtocol.VirtualPlc,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                DriverId = driverId,
                Host = name,
                TimeoutMilliseconds = 1000
            },
            DefaultScanRateMs = 60000,
            FailureRetryDelayMs = 1000,
            MaxFailureRetryDelayMs = 30000,
            Tags = new List<TagConfig>
            {
                new TagConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "TagA",
                    Address = "D1",
                    DataType = PlcDataType.Int16,
                    Enabled = true,
                    AccessMode = TagAccessMode.ReadWrite,
                    ScanRateMs = 60000
                }
            },
            Groups = new List<GroupConfig>()
        };
    }

    private sealed class CountingBatchReadDriver : IPlcDriverPlugin
    {
        private int _createClientCount;
        private int _connectCount;
        private int _disposeCount;
        private int _batchReadCount;

        public CountingBatchReadDriver(string driverId)
        {
            DriverId = driverId;
        }

        public string DriverId { get; }
        public string DisplayName => "Counting Batch Read Test Driver";
        public int CreateClientCount => Volatile.Read(ref _createClientCount);
        public int ConnectCount => Volatile.Read(ref _connectCount);
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int BatchReadCount => Volatile.Read(ref _batchReadCount);

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            Interlocked.Increment(ref _createClientCount);
            return new CountingBatchReadClient(this, options.Protocol);
        }

        public void RecordConnect() => Interlocked.Increment(ref _connectCount);
        public void RecordDispose() => Interlocked.Increment(ref _disposeCount);
        public void RecordBatchRead() => Interlocked.Increment(ref _batchReadCount);
    }

    private sealed class CountingBatchReadClient : IPlcClient, IPlcBatchReadClient
    {
        private readonly CountingBatchReadDriver _driver;
        private int _connected;

        public CountingBatchReadClient(CountingBatchReadDriver driver, PlcProtocol protocol)
        {
            _driver = driver;
            Protocol = protocol;
        }

        public bool IsConnected => Volatile.Read(ref _connected) == 1;
        public PlcProtocol Protocol { get; }

        public void Connect()
        {
            Interlocked.Exchange(ref _connected, 1);
            _driver.RecordConnect();
        }

        public void Disconnect()
        {
            Interlocked.Exchange(ref _connected, 0);
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            return CreateReadResult(address, dataType);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            _driver.RecordBatchRead();
            List<PlcBatchReadResult> results = new List<PlcBatchReadResult>();
            foreach (PlcBatchReadRequest request in requests)
                results.Add(PlcBatchReadResult.FromSuccess(request, CreateReadResult(request.Address, request.DataType)));
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

        private static PlcReadResult CreateReadResult(string address, PlcDataType dataType)
        {
            int value = string.Equals(address, "D2", StringComparison.OrdinalIgnoreCase) ? 22 : 11;
            return new PlcReadResult(0, dataType.ToString(), value);
        }
    }
}
