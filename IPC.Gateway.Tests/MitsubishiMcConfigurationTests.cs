using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class MitsubishiMcConfigurationTests
{
    [Theory]
    [InlineData("SM0")]
    [InlineData("SD100")]
    [InlineData("TS10")]
    [InlineData("TN10")]
    [InlineData("CS20")]
    [InlineData("CN20")]
    [InlineData("SB1A")]
    [InlineData("SW2F")]
    [InlineData("DX10")]
    [InlineData("DY10")]
    [InlineData("Z0")]
    public void McValidator_AcceptsExpandedCommonDevices(string address)
    {
        MitsubishiMcProtocolDriver driver = new MitsubishiMcProtocolDriver();
        PlcTagValidationResult result = driver.ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.MitsubishiMc },
            address,
            PlcDataType.Int16,
            1,
            0);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void McValidator_RejectsUnknownDeviceBeforePolling()
    {
        MitsubishiMcProtocolDriver driver = new MitsubishiMcProtocolDriver();
        PlcTagValidationResult result = driver.ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.MitsubishiMc },
            "UNKNOWN100",
            PlcDataType.Int16,
            1,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ConnectionParameters_ExposeFrameEncodingAndRouting()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.MitsubishiMc);

        Assert.Contains(parameters, item => item.Key == "driverOptions.mcFrameType");
        Assert.Contains(parameters, item => item.Key == "driverOptions.mcDataCode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.networkNumber");
        Assert.Contains(parameters, item => item.Key == "driverOptions.moduleIoNumber");
        Assert.Contains(parameters, item => item.Key == "driverOptions.stationNumber");
    }
}
