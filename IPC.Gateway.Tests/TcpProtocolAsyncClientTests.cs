using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.Tests;

public sealed class TcpProtocolAsyncClientTests
{
    [Theory]
    [MemberData(nameof(BuiltInTcpDrivers))]
    public void BuiltInTcpDrivers_CreateAsyncClients(IProtocolDriver driver, PlcProtocol expectedProtocol)
    {
        IPlcClient client = driver.CreateClient(new PlcConnectionOptions
        {
            Protocol = expectedProtocol,
            Host = "127.0.0.1",
            Port = 4001,
            TimeoutMilliseconds = 100
        });

        Assert.IsAssignableFrom<IAsyncPlcClient>(client);
        Assert.IsAssignableFrom<IAsyncPlcBatchReadClient>(client);

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
        Assert.Equal(PlcClientAsyncKind.NativeIo, capabilities.AsyncKind);
        Assert.True(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsBatchRead);
    }

    [Theory]
    [MemberData(nameof(LegacyNetworkDrivers))]
    public void LegacyNetworkDrivers_CreateAsyncClients(IProtocolDriver driver, PlcProtocol expectedProtocol, NetworkTransport transport)
    {
        IPlcClient client = driver.CreateClient(new PlcConnectionOptions
        {
            Protocol = expectedProtocol,
            Host = "127.0.0.1",
            Port = expectedProtocol == PlcProtocol.RockwellCip || expectedProtocol == PlcProtocol.RockwellPccc ? 44818 : 5000,
            TimeoutMilliseconds = 100,
            Transport = transport
        });

        Assert.IsAssignableFrom<IAsyncPlcClient>(client);
        Assert.IsAssignableFrom<IAsyncPlcBatchReadClient>(client);

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
        Assert.Equal(PlcClientAsyncKind.NativeIo, capabilities.AsyncKind);
        Assert.True(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsBatchRead);
    }

    [Fact]
    public void OpcUaDriver_CreateAsyncAdapterClient()
    {
        IPlcClient client = new OpcUaProtocolDriver().CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OpcUa,
            Host = "opc.tcp://127.0.0.1",
            Port = 4840,
            TimeoutMilliseconds = 100
        });

        Assert.IsAssignableFrom<IAsyncPlcClient>(client);
        Assert.IsAssignableFrom<IAsyncPlcBatchReadClient>(client);

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
        Assert.Equal(PlcClientAsyncKind.DedicatedThread, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsBatchRead);
    }

    public static IEnumerable<object[]> BuiltInTcpDrivers()
    {
        yield return new object[] { new ModbusTcpProtocolDriver(), PlcProtocol.ModbusTcp };
        yield return new object[] { new Dlt645ProtocolDriver(), PlcProtocol.Dlt6452007 };
        yield return new object[] { new Cjt188ProtocolDriver(), PlcProtocol.Cjt1882004 };
    }

    public static IEnumerable<object[]> LegacyNetworkDrivers()
    {
        yield return new object[] { new SiemensS7ProtocolDriver(), PlcProtocol.SiemensS7, NetworkTransport.Tcp };
        yield return new object[] { new OmronFinsProtocolDriver(), PlcProtocol.OmronFins, NetworkTransport.Tcp };
        yield return new object[] { new MitsubishiMcProtocolDriver(), PlcProtocol.MitsubishiMc, NetworkTransport.Tcp };
        yield return new object[] { new MitsubishiMcProtocolDriver(), PlcProtocol.MitsubishiMc, NetworkTransport.Udp };
        yield return new object[] { new MitsubishiMc1EProtocolDriver(), PlcProtocol.MitsubishiMc1E, NetworkTransport.Tcp };
        yield return new object[] { new MitsubishiMc1EProtocolDriver(), PlcProtocol.MitsubishiMc1E, NetworkTransport.Udp };
        yield return new object[] { new RockwellCipProtocolDriver(), PlcProtocol.RockwellCip, NetworkTransport.Tcp };
        yield return new object[] { new RockwellPcccProtocolDriver(), PlcProtocol.RockwellPccc, NetworkTransport.Tcp };
    }
}
