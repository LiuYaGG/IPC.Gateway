using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OpcUa;
using IPC.Plc.Communication.SiemensS7;

namespace IPC.Gateway.Tests;

public sealed class ProtocolDriverCreationTests
{
    [Fact]
    public void SiemensDriver_CreatesProtocolClientWithAsyncBatchCapabilities()
    {
        SiemensS7ProtocolDriver driver = new();
        using IPlcClient client = driver.CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = 102,
            Rack = 0,
            Slot = 1
        });
        Assert.IsType<S7Client>(client);
        Assert.IsAssignableFrom<IAsyncPlcClient>(client);
        Assert.IsAssignableFrom<IAsyncPlcBatchReadClient>(client);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public void OpcUaDriver_CreatesProtocolClientWithSubscriptionCapabilities()
    {
        OpcUaProtocolDriver driver = new();
        using IPlcClient client = driver.CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            Host = "opc.tcp://127.0.0.1:4840"
        });
        Assert.IsType<OpcUaClient>(client);
        Assert.IsAssignableFrom<IAsyncPlcSubscriptionClient>(client);
        Assert.True(PlcClientInvoker.GetCapabilities(client).SupportsSubscription);
        Assert.False(client.IsConnected);
    }
}
