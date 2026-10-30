using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class PlcWriteRegressionTests
{
    [Theory]
    [InlineData(PlcDataType.Byte, "0")]
    [InlineData(PlcDataType.Byte, "255")]
    [InlineData(PlcDataType.UInt8, "255")]
    [InlineData(PlcDataType.Int8, "-128")]
    [InlineData(PlcDataType.Int8, "127")]
    [InlineData(PlcDataType.ByteArray, "0,128,255")]
    [InlineData(PlcDataType.UInt8Array, "0,128,255")]
    [InlineData(PlcDataType.Int8Array, "-128,0,127")]
    public void WriteTag_EightBitValuesReachDriverWithConfiguredType(PlcDataType dataType, string text)
    {
        var (response, driver) = WriteEightBitTag(dataType, text);

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal(1, driver.WriteCount);
        Assert.Equal(dataType, driver.LastWriteDataType);
        Assert.Equal(text, driver.LastWriteValueText);
        Assert.Equal(2, driver.LastWriteElementOffset);
    }

    [Theory]
    [InlineData(PlcDataType.Byte, "-1", "0～255")]
    [InlineData(PlcDataType.Byte, "256", "0～255")]
    [InlineData(PlcDataType.Byte, "1.5", "0～255")]
    [InlineData(PlcDataType.UInt8, "256", "0～255")]
    [InlineData(PlcDataType.Int8, "-129", "-128～127")]
    [InlineData(PlcDataType.Int8, "128", "-128～127")]
    [InlineData(PlcDataType.ByteArray, "0,256", "第 1 项数组值无效")]
    [InlineData(PlcDataType.UInt8Array, "0,-1", "第 1 项数组值无效")]
    [InlineData(PlcDataType.Int8Array, "0,128", "第 1 项数组值无效")]
    [InlineData(PlcDataType.ByteArray, "1,2,3,4", "写入值数量超过标签元素数量")]
    public void WriteTag_InvalidEightBitValuesAreRejectedBeforeDriver(PlcDataType dataType, string text, string error)
    {
        var (response, driver) = WriteEightBitTag(dataType, text);

        Assert.False(response.Success);
        Assert.Contains(error, response.ErrorMessage);
        Assert.Equal(0, driver.WriteCount);
    }

    [Theory]
    [InlineData(PlcDataType.Byte, "20", "5")]
    [InlineData(PlcDataType.UInt8, "20", "5")]
    [InlineData(PlcDataType.Int8, "0", "-5")]
    [InlineData(PlcDataType.ByteArray, "10,20,30", "0,5,10")]
    [InlineData(PlcDataType.UInt8Array, "10,20,30", "0,5,10")]
    [InlineData(PlcDataType.Int8Array, "0,10,20", "-5,0,5")]
    public void WriteTag_EightBitEngineeringValuesAreUnscaled(PlcDataType dataType, string text, string expectedRaw)
    {
        var (response, driver) = WriteEightBitTag(dataType, text, scaling: new ScalingConfig
        {
            Enabled = true, Multiplier = 2D, Offset = 10D
        });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal(expectedRaw, driver.LastWriteValueText);
        Assert.Equal(dataType, driver.LastWriteDataType);
    }

    [Theory]
    [InlineData(PlcDataType.ByteArray)]
    [InlineData(PlcDataType.UInt8Array)]
    public void WriteTag_ByteArrayObjectIsFormattedAsElements(PlcDataType dataType)
    {
        var (response, driver) = WriteEightBitTag(dataType, string.Empty, new byte[] { 0, 128, 255 });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Equal("0,128,255", driver.LastWriteValueText);
        Assert.Equal(dataType, driver.LastWriteDataType);
    }

    private static (WriteTagResponse Response, WriteSucceedsReadFailsDriver Driver) WriteEightBitTag(
        PlcDataType dataType, string text, object? value = null, ScalingConfig? scaling = null)
    {
        string driverId = "eight-bit-write-" + Guid.NewGuid().ToString("N");
        var driver = new WriteSucceedsReadFailsDriver(driverId);
        PlcDriverPluginRegistry.Register(driver);
        var tag = new TagConfig
        {
            Name = "EightBitTag", Address = "Bytes", DataType = dataType,
            ElementCount = PlcDataTypeHelper.IsArray(dataType) ? 3 : 1,
            ElementOffset = 2, AccessMode = TagAccessMode.WriteOnly,
            Scaling = scaling ?? ScalingConfig.Default()
        };
        var device = new DeviceConfig
        {
            Name = "EightBitDevice", Protocol = PlcProtocol.Plugin,
            Connection = new PlcConnectionOptions { Protocol = PlcProtocol.Plugin, DriverId = driverId },
            DefaultScanRateMs = 60000, Tags = new List<TagConfig> { tag }
        };
        var project = new ProjectConfig { Devices = new List<DeviceConfig> { device } };
        ProjectConfigStore.Normalize(project);
        using var engine = new RuntimeEngine(new RuntimeSchedulerOptions { SchedulerIntervalMs = 60000 });
        engine.Start(project);
        var response = engine.WriteTag(new WriteTagRequest
        {
            ChannelId = device.ChannelId, DeviceId = device.Id, TagId = tag.Id,
            DataType = dataType.ToString(), ValueText = text, Value = value ?? string.Empty
        });
        return (response, driver);
    }

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

        ProjectConfig project = new ProjectConfig { Devices = new List<DeviceConfig> { device } };
        ProjectConfigStore.Normalize(project);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions { SchedulerIntervalMs = 60000 });
        engine.Start(project);

        WriteTagResponse response = engine.WriteTag(new WriteTagRequest
        {
            ChannelId = device.ChannelId,
            DeviceId = device.Id,
            TagId = device.Tags[0].Id,
            DeviceName = device.Name,
            TagName = "ScaledTag",
            DataType = PlcDataType.Int16.ToString(),
            ValueText = "20"
        });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.True(engine.TryGetSnapshotById(device.ChannelId, device.Id, string.Empty, device.Tags[0].Id, out var snapshot));
        Assert.NotNull(snapshot);
        Assert.Equal("5", snapshot!.RawValueText);
        Assert.Equal("20.00", snapshot.ValueText);
    }

    [Theory]
    [InlineData(PlcProtocol.ModbusTcp, "Unit ID")]
    [InlineData(PlcProtocol.ModbusRtu, "Slave ID")]
    [InlineData(PlcProtocol.ModbusAscii, "Slave ID")]
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
    public void ModbusAsciiConnectionParameters_UseConventionalSerialDefaults()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.ModbusAscii);

        Assert.Equal("7", Assert.Single(parameters, item => item.Key == "dataBits").DefaultValue);
        Assert.Equal("Even", Assert.Single(parameters, item => item.Key == "serialParity").DefaultValue);
        Assert.Equal("One", Assert.Single(parameters, item => item.Key == "serialStopBits").DefaultValue);
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

        ProjectConfig project = new ProjectConfig { Devices = new List<DeviceConfig> { device } };
        ProjectConfigStore.Normalize(project);
        using RuntimeEngine engine = new RuntimeEngine(new RuntimeSchedulerOptions { SchedulerIntervalMs = 60000 });
        engine.Start(project);

        WriteTagResponse response = engine.WriteTag(new WriteTagRequest
        {
            ChannelId = device.ChannelId,
            DeviceId = device.Id,
            TagId = device.Tags[0].Id,
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
        public PlcDataType LastWriteDataType { get; private set; }
        public string LastWriteValueText { get; private set; } = string.Empty;
        public int LastWriteElementOffset { get; private set; }

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

            public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
            {
                _owner.LastWriteDataType = dataType;
                _owner.LastWriteValueText = valueText;
                _owner.LastWriteElementOffset = elementOffset;
                Interlocked.Increment(ref _owner._writeCount);
            }

            public void Dispose() => Disconnect();
        }
    }
}
