using System.Diagnostics;
using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class RuntimeRecoveryTests
{
    [Theory]
    [InlineData(PlcProtocol.SiemensS7, "DB1.DBW0", false)]
    [InlineData(PlcProtocol.ModbusTcp, "HR:0", false)]
    [InlineData(PlcProtocol.MitsubishiMc, "D0", false)]
    [InlineData(PlcProtocol.OmronFins, "D0", false)]
    [InlineData(PlcProtocol.OpcUa, "ns=2;s=Value", false)]
    [InlineData(PlcProtocol.Plugin, "Value", true)]
    public async Task Recovery_OverridesLongBackoffAndOpenCircuitAcrossProtocols(PlcProtocol protocol, string address, bool synchronous)
    {
        TestDriver driver = new(protocol) { Synchronous = synchronous };
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new(Options());
        DeviceConfig device = Device(driver, address);
        engine.Start(new ProjectConfig { Devices = new() { device } });
        await Until(() => engine.GetDeviceStatuses().Any(s => s.IsIsolated && s.ProtocolCircuitBreaker.IsOpen));
        await driver.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(engine.GetDeviceStatuses().Single().IsConnected);

        Stopwatch recovery = Stopwatch.StartNew();
        driver.Available = true;
        await Until(() => engine.GetDeviceStatuses().Single().IsConnected);

        Assert.True(recovery.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(engine.GetDeviceStatuses().Single().ProtocolCircuitBreaker.IsOpen);
        Assert.Equal(1, driver.MaxActiveConnections);
        Assert.True(driver.Reads > 0);
    }

    [Theory]
    [InlineData(NetworkTransport.Tcp)]
    [InlineData(NetworkTransport.Udp)]
    public async Task Recovery_AfterPreviouslyOnlineDeviceDropsRequiresSuccessfulRead(NetworkTransport transport)
    {
        TestDriver driver = new(PlcProtocol.Plugin) { Available = true };
        PlcDriverPluginRegistry.Register(driver);
        DeviceConfig device = Device(driver, "Value");
        device.Connection.Transport = transport;
        device.DefaultScanRateMs = device.Tags[0].ScanRateMs = 20;
        using RuntimeEngine engine = new(Options());
        engine.Start(new ProjectConfig { Devices = new() { device } });
        await Until(() => engine.GetDeviceStatuses().Single().IsConnected);
        driver.Available = false;
        await Until(() => engine.GetDeviceStatuses().Single().IsIsolated);
        long failedReadsBeforeRecovery = engine.GetDeviceStatuses().Single().FailedReads;
        driver.FailReads = true;
        driver.Available = true;
        await driver.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Until(() => engine.GetDeviceStatuses().Single().FailedReads > failedReadsBeforeRecovery);
        Assert.False(engine.GetDeviceStatuses().Single().IsConnected);
        driver.FailReads = false;
        await Until(() => engine.GetDeviceStatuses().Single().IsConnected);
        Assert.Equal(1, driver.MaxActiveConnections);
    }

    [Fact]
    public async Task Recovery_HungConnectDoesNotTakeNormalWorkerOrConfiguredChannelSlot()
    {
        TestDriver offline = new(PlcProtocol.Plugin) { SlowOffline = true };
        TestDriver healthy = new(PlcProtocol.Plugin) { Available = true };
        offline.HealthyPeer = healthy;
        PlcDriverPluginRegistry.Register(offline);
        ChannelConfig channel = new() { Name = "Shared", Protocol = PlcProtocol.Plugin, DriverId = offline.DriverId, MaxConcurrentDevicePolls = 1 };
        DeviceConfig bad = Device(offline, "Value");
        DeviceConfig good = Device(healthy, "Value");
        bad.ChannelId = good.ChannelId = channel.Id;
        good.Connection.DriverId = offline.DriverId;
        bad.DefaultScanRateMs = 20;
        bad.Tags[0].ScanRateMs = 20;
        good.DefaultScanRateMs = 20;
        good.Tags[0].ScanRateMs = 20;
        using RuntimeEngine engine = new(Options());
        engine.Start(new ProjectConfig { Channels = new() { channel }, Devices = new() { bad, good } });
        await offline.ProbeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        int readsBefore = healthy.Reads;
        await Until(() => healthy.Reads >= readsBefore + 5);
        Assert.True(engine.GetDeviceStatuses().Single(s => s.DeviceName == good.Name).IsConnected);
        Assert.True(offline.MaxActiveConnections <= 1);
    }

    [Fact]
    public async Task Recovery_FactoryConfigurationFailureKeepsSlowBackoff()
    {
        TestDriver driver = new(PlcProtocol.Plugin) { FactoryFailure = true };
        PlcDriverPluginRegistry.Register(driver);
        using RuntimeEngine engine = new(Options());
        engine.Start(new ProjectConfig { Devices = new() { Device(driver, "Value") } });
        await Until(() => engine.GetDeviceStatuses().Any(s => s.IsIsolated && s.ProtocolCircuitBreaker.IsOpen));
        var status = engine.GetDeviceStatuses().Single();
        Assert.Equal(60000, status.LastReconnectDelayMs);
        Assert.True(status.ProtocolCircuitBreaker.IsOpen);
        Assert.Equal(0, driver.Attempts);
    }

    private static RuntimeSchedulerOptions Options() => new()
    {
        SchedulerIntervalMs = 20,
        MaxConcurrentDevicePolls = 1,
        MaxConcurrentRecoveryProbes = 1,
        RecoveryProbeIntervalMs = 100,
        RecoveryProbeTimeoutMs = 500,
        DeviceStatusFailureDebounceCount = 1,
        DeviceStatusFailureDebounceMs = 0,
        ProtocolDriverCircuitBreaker = new CircuitBreakerOptions { FailureThreshold = 1, BreakDurationSeconds = 30 }
    };

    private static DeviceConfig Device(TestDriver driver, string address) => new()
    {
        Name = driver.DriverId,
        Protocol = driver.Protocol,
        Connection = new() { Protocol = driver.Protocol, DriverId = driver.DriverId, Host = driver.DriverId },
        DefaultScanRateMs = 60000,
        FailureRetryDelayMs = 60000,
        MaxFailureRetryDelayMs = 60000,
        Tags = new() { new() { Name = "Value", Address = address, DataType = PlcDataType.Int16, ScanRateMs = 60000 } }
    };

    private static async Task Until(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition() && elapsed.Elapsed < TimeSpan.FromSeconds(4))
            await Task.Delay(10);
        Assert.True(condition(), "Runtime did not reach the expected recovery state.");
    }

    private sealed class TestDriver(PlcProtocol protocol) : IProtocolDriver
    {
        public string DriverId { get; } = "test.recovery." + Guid.NewGuid().ToString("N");
        public string DisplayName => DriverId;
        public PlcProtocol Protocol => protocol;
        public volatile bool Available;
        public bool SlowOffline;
        public bool FactoryFailure;
        public bool Synchronous;
        public volatile bool FailReads;
        public TestDriver? HealthyPeer;
        public int Attempts;
        public int Reads;
        public int ActiveConnections;
        public int MaxActiveConnections;
        public TaskCompletionSource ProbeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(PlcConnectionOptions options) => options.DriverId == DriverId;
        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            if (FactoryFailure) throw new InvalidOperationException("Driver configuration is invalid.");
            Client client = new(options.Host == HealthyPeer?.DriverId ? HealthyPeer : this);
            return Synchronous ? new SynchronousClient(client) : client;
        }

        private sealed class SynchronousClient(Client client) : IPlcClient
        {
            public bool IsConnected => client.IsConnected;
            public PlcProtocol Protocol => client.Protocol;
            public void Connect() => client.Connect();
            public void Disconnect() => client.Disconnect();
            public PlcReadResult Read(string address, PlcDataType type, int count, int offset) => client.Read(address, type, count, offset);
            public void Write(string address, PlcDataType type, string value, int offset) => client.Write(address, type, value, offset);
            public void Dispose() => client.Dispose();
        }

        private sealed class Client(TestDriver driver) : IPlcClient, IAsyncPlcClient
        {
            public bool IsConnected { get; private set; }
            public PlcProtocol Protocol => driver.Protocol;
            public void Connect() => ConnectAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult();
            public async ValueTask ConnectAsync(CancellationToken cancellationToken)
            {
                int attempt = Interlocked.Increment(ref driver.Attempts);
                int active = Interlocked.Increment(ref driver.ActiveConnections);
                driver.MaxActiveConnections = Math.Max(driver.MaxActiveConnections, active);
                try
                {
                    if (attempt > 1)
                        driver.ProbeStarted.TrySetResult();
                    if (!driver.Available)
                    {
                        if (attempt > 1 && driver.SlowOffline)
                            await Task.Delay(Timeout.Infinite, cancellationToken);
                        throw new IOException("Device is offline.");
                    }
                    IsConnected = true;
                }
                finally { Interlocked.Decrement(ref driver.ActiveConnections); }
            }
            public void Disconnect() => IsConnected = false;
            public ValueTask DisconnectAsync(CancellationToken cancellationToken) { Disconnect(); return ValueTask.CompletedTask; }
            public PlcReadResult Read(string address, PlcDataType type, int count, int offset)
            {
                if (!driver.Available || driver.FailReads) throw new IOException("Device is offline.");
                Interlocked.Increment(ref driver.Reads);
                return new PlcReadResult(0, "Int16", (short)42);
            }
            public ValueTask<PlcReadResult> ReadAsync(string address, PlcDataType type, int count, int offset, CancellationToken cancellationToken)
                => new(Read(address, type, count, offset));
            public void Write(string address, PlcDataType type, string value, int offset) => throw new NotSupportedException();
            public ValueTask WriteAsync(string address, PlcDataType type, string value, int offset, CancellationToken cancellationToken)
                => throw new NotSupportedException();
            public void Dispose() => Disconnect();
        }
    }
}
