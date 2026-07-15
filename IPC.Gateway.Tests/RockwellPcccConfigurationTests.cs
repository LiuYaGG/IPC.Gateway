using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class RockwellPcccConfigurationTests
{
    private readonly RockwellPcccProtocolDriver _driver = new RockwellPcccProtocolDriver();

    [Theory]
    [InlineData("N7:0", PlcDataType.Int16)]
    [InlineData("B3:0/1", PlcDataType.Bool)]
    [InlineData("T4:0.ACC", PlcDataType.Int16)]
    [InlineData("C5:0.DN", PlcDataType.Bool)]
    [InlineData("F8:0", PlcDataType.Float)]
    [InlineData("ST9:0", PlcDataType.String)]
    public void Validator_AcceptsCommonFileAddresses(string address, PlcDataType dataType)
    {
        PlcTagValidationResult result = _driver.ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.RockwellPccc },
            address,
            dataType,
            1,
            0);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Theory]
    [InlineData("N7")]
    [InlineData("B3:0/16")]
    [InlineData("T4:0.UNKNOWN")]
    public void Validator_RejectsInvalidFileAddresses(string address)
    {
        PlcTagValidationResult result = _driver.ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.RockwellPccc },
            address,
            PlcDataType.Int16,
            1,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ConnectionParameters_DefaultToDirectRouting()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.RockwellPccc);

        PlcConnectionParameterDefinition route = Assert.Single(parameters, item => item.Key == "driverOptions.cipRouteMode");
        Assert.Equal("Direct", route.DefaultValue);
    }
}
