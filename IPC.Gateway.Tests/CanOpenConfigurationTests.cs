using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class CanOpenConfigurationTests
{
    [Fact]
    public void ConnectionParameters_ExposeHeartbeatPdoAndSyncSettings()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.CanOpen);

        Assert.Contains(parameters, item => item.Key == "driverOptions.resetCommunicationOnConnect");
        Assert.Contains(parameters, item => item.Key == "driverOptions.heartbeatTimeoutMilliseconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.pdoMaxAgeMilliseconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.syncIntervalMilliseconds");
    }

    [Theory]
    [InlineData("1:0x6041:0", PlcDataType.UInt16)]
    [InlineData("TPDO1:1:0", PlcDataType.UInt16)]
    [InlineData("TPDO4:127:7.0", PlcDataType.Bool)]
    [InlineData("RPDO2:10:2", PlcDataType.UInt32)]
    [InlineData("Heartbeat:1", PlcDataType.String)]
    [InlineData("EMCY:127", PlcDataType.UInt16)]
    [InlineData("NMT:1", PlcDataType.UInt8)]
    [InlineData("SYNC", PlcDataType.Bool)]
    [InlineData("TIME", PlcDataType.String)]
    public void Driver_AcceptsSupportedServiceAddresses(string address, PlcDataType dataType)
    {
        PlcTagValidationResult result = new CanOpenProtocolDriver().ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.CanOpen },
            address,
            dataType,
            1,
            0);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Theory]
    [InlineData("TPDO5:1:0", PlcDataType.UInt16, 1, 0)]
    [InlineData("TPDO1:1:7", PlcDataType.UInt16, 1, 0)]
    [InlineData("TPDO1:1:7.7", PlcDataType.BoolArray, 2, 0)]
    [InlineData("Heartbeat:1", PlcDataType.UInt16Array, 2, 0)]
    public void Driver_RejectsInvalidOrOutOfRangeServiceTags(
        string address,
        PlcDataType dataType,
        int elementCount,
        int elementOffset)
    {
        PlcTagValidationResult result = new CanOpenProtocolDriver().ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.CanOpen },
            address,
            dataType,
            elementCount,
            elementOffset);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("TPDO1:1:0")]
    [InlineData("Heartbeat:1")]
    [InlineData("EMCY:1")]
    [InlineData("NMT:1")]
    [InlineData("SYNC")]
    [InlineData("TIME")]
    public void CoreStaticValidator_AcceptsExtendedCanOpenAddresses(string address)
    {
        Assert.True(PlcProtocolTagValidator.Validate(
            PlcProtocol.CanOpen,
            address,
            PlcDataType.UInt16,
            1,
            0).IsValid);
    }
}
