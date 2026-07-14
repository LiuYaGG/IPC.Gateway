using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class PlcWriteRegressionTests
{
    [Fact]
    public void WriteTag_ValueTextIsConvertedFromEngineeringToRawValue()
    {
        string source = "scaled-write-" + Guid.NewGuid().ToString("N");
        DeviceConfig device = new DeviceConfig
        {
            Name = "ScaledDevice",
            Protocol = PlcProtocol.VirtualPlc,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.VirtualPlc,
                Host = source
            },
            DefaultScanRateMs = 60000,
            Tags = new List<TagConfig>
            {
                new TagConfig
                {
                    Name = "ScaledTag",
                    Address = "D0",
                    DataType = PlcDataType.Int16,
                    Scaling = new ScalingConfig
                    {
                        Enabled = true,
                        Multiplier = 2D,
                        Offset = 10D,
                        DecimalPlaces = 2
                    }
                }
            }
        };

        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions { SchedulerIntervalMs = 60000 });
        engine.Start(new ProjectConfig { Devices = new List<DeviceConfig> { device } });

        WriteTagResponse response = engine.WriteTag(new WriteTagRequest
        {
            DeviceName = device.Name,
            TagName = "ScaledTag",
            DataType = PlcDataType.Int16.ToString(),
            ValueText = "20"
        });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.True(engine.TryGetSnapshot(device.Name, string.Empty, "ScaledTag", out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal("5", snapshot!.RawValueText);
        Assert.Equal("20.00", snapshot.ValueText);
    }

    [Theory]
    [InlineData(PlcProtocol.ModbusTcp, "Unit ID")]
    [InlineData(PlcProtocol.ModbusRtu, "Slave ID")]
    public void ModbusConnectionParameters_ExposeSlaveAddress(PlcProtocol protocol, string expectedLabel)
    {
        PlcConnectionParameterDefinition parameter = Assert.Single(
            PlcConnectionParameterCatalog.ForProtocol(protocol),
            item => string.Equals(item.Key, "rack", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expectedLabel, parameter.Label);
        Assert.Equal("1", parameter.DefaultValue);
        Assert.Equal(1D, parameter.Min);
        Assert.Equal(247D, parameter.Max);
    }

    [Fact]
    public void ModbusTcpConnectionParameters_OnlyAllowTcp()
    {
        PlcConnectionParameterDefinition transport = Assert.Single(
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.ModbusTcp),
            item => string.Equals(item.Key, "transport", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { "Tcp" }, transport.Options);
        Assert.True(transport.ReadOnly);

        ProjectConfig project = new ProjectConfig
        {
            Devices = new List<DeviceConfig>
            {
                new DeviceConfig
                {
                    Protocol = PlcProtocol.ModbusTcp,
                    Connection = new PlcConnectionOptions { Transport = NetworkTransport.Udp }
                }
            }
        };

        ProjectConfigStore.Normalize(project);

        Assert.Equal(NetworkTransport.Tcp, project.Devices[0].Connection.Transport);
    }

    [Fact]
    public void WriteTag_ReadbackFailureReportsWarningWithoutRetryableWriteFailure()
    {
        string driverId = "write-readback-failure-" + Guid.NewGuid().ToString("N");
        WriteSucceedsReadFailsDriver driver = new WriteSucceedsReadFailsDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        DeviceConfig device = new DeviceConfig
        {
            Name = "ReadbackFailureDevice",
            Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions { Protocol = PlcProtocol.Plugin, DriverId = driverId },
            DefaultScanRateMs = 60000,
            Tags = new List<TagConfig>
            {
                new TagConfig { Name = "TagA", Address = "A", DataType = PlcDataType.Int16 }
            }
        };

        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions { SchedulerIntervalMs = 60000 });
        engine.Start(new ProjectConfig { Devices = new List<DeviceConfig> { device } });

        WriteTagResponse response = engine.WriteTag(new WriteTagRequest
        {
            DeviceName = device.Name,
            TagName = "TagA",
            DataType = PlcDataType.Int16.ToString(),
            ValueText = "7"
        });

        Assert.True(response.Success);
        Assert.Equal("ReadError", response.Quality);
        Assert.Contains("Write succeeded", response.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, driver.WriteCount);
        Assert.False(response.CurrentValue.Success);
    }

    private sealed class WriteSucceedsReadFailsDriver : IProtocolDriver
    {
        private readonly string _driverId;
        private int _writeCount;

        public WriteSucceedsReadFailsDriver(string driverId)
        {
            _driverId = driverId;
        }

        public string DriverId => _driverId;
        public string DisplayName => "Write succeeds, read fails";
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public int WriteCount => Volatile.Read(ref _writeCount);

        public bool Supports(PlcConnectionOptions options) =>
            string.Equals(options.DriverId, _driverId, StringComparison.OrdinalIgnoreCase);

        public IPlcClient CreateClient(PlcConnectionOptions options) => new Client(this);

        private sealed class Client : IPlcClient
        {
            private readonly WriteSucceedsReadFailsDriver _owner;

            public Client(WriteSucceedsReadFailsDriver owner)
            {
                _owner = owner;
            }

            public bool IsConnected { get; private set; }
            public PlcProtocol Protocol => PlcProtocol.Plugin;
            public void Connect() => IsConnected = true;
            public void Disconnect() => IsConnected = false;

            public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset) =>
                throw new PlcTagException("Readback is unavailable.");

            public void Write(string address, PlcDataType dataType, string valueText, int elementOffset) =>
                Interlocked.Increment(ref _owner._writeCount);

            public void Dispose() => Disconnect();
        }
    }
}
