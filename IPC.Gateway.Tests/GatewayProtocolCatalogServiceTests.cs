using IPC.Gateway.WebHost;

namespace IPC.Gateway.Tests;

public sealed class GatewayProtocolCatalogServiceTests
{
    [Fact]
    public void MitsubishiFxSerial_IsHiddenFromNewDeviceProtocolCatalog()
    {
        Assert.False(GatewayProtocolCatalogService.IsVisibleProtocol("MitsubishiSerial"));
        Assert.True(GatewayProtocolCatalogService.IsVisibleProtocol("OpcUa"));
    }
}
