using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusAscii;
using NModbus;
using NModbus.IO;

namespace IPC.Gateway.Tests;

public sealed class ModbusAsciiConfigurationTests
{
    [Fact]
    public void NModbusFactory_CreatesAsciiTransport()
    {
        using TestStreamResource resource = new TestStreamResource();
        using IModbusMaster master = new ModbusFactory().CreateAsciiMaster(resource);

        Assert.IsAssignableFrom<IModbusAsciiTransport>(master.Transport);
    }

    [Fact]
    public void Driver_CreatesModbusAsciiClient()
    {
        ModbusAsciiProtocolDriver driver = new ModbusAsciiProtocolDriver();
        using IPlcClient client = driver.CreateClient(new PlcConnectionOptions());

        Assert.Equal("legacy.modbus-ascii", driver.DriverId);
        Assert.IsType<ModbusAsciiClient>(client);
        Assert.Equal(PlcProtocol.ModbusAscii, client.Protocol);
    }

    private sealed class TestStreamResource : IStreamResource
    {
        public int InfiniteTimeout => -1;
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }

        public void DiscardInBuffer()
        {
        }

        public int Read(byte[] buffer, int offset, int count) => 0;

        public void Write(byte[] buffer, int offset, int count)
        {
        }

        public void Dispose()
        {
        }
    }
}
