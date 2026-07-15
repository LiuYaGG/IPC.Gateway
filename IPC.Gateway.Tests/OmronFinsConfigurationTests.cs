using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class OmronFinsConfigurationTests
{
    [Fact]
    public void Catalog_ExposesNetworkRoutingAndBatchOptions()
    {
        IList<PlcConnectionParameterDefinition> parameters = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.OmronFins);

        Assert.Contains(parameters, item => item.Key == "transport" && item.DefaultValue == "Udp");
        Assert.Contains(parameters, item => item.Key == "driverOptions.controllerProfile");
        Assert.Contains(parameters, item => item.Key == "driverOptions.sourceNode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.destinationNode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.sourceNetwork");
        Assert.Contains(parameters, item => item.Key == "driverOptions.network");
        Assert.Contains(parameters, item => item.Key == "driverOptions.destinationUnit");
        Assert.Contains(parameters, item => item.Key == "driverOptions.maxGapWords");
        Assert.Contains(parameters, item => item.Key == "driverOptions.udpReadRetries");
    }

    [Theory]
    [InlineData("E0_100", PlcDataType.UInt16)]
    [InlineData("EF_100", PlcDataType.UInt16)]
    [InlineData("E10_100", PlcDataType.UInt16)]
    [InlineData("E18_100", PlcDataType.UInt16)]
    [InlineData("T10", PlcDataType.UInt16)]
    [InlineData("C10", PlcDataType.UInt16)]
    [InlineData("TU10", PlcDataType.Bool)]
    public void DriverValidator_AcceptsSupportedNetworkAddresses(string address, PlcDataType dataType)
    {
        OmronFinsProtocolDriver driver = new OmronFinsProtocolDriver();
        PlcConnectionOptions options = CreateOptions("Auto");

        PlcTagValidationResult result = driver.ValidateTag(options, address, dataType, 1, 0);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void DriverValidator_RejectsTimerAreaForNjNxProfile()
    {
        OmronFinsProtocolDriver driver = new OmronFinsProtocolDriver();
        PlcConnectionOptions options = CreateOptions("NJ/NX");

        PlcTagValidationResult result = driver.ValidateTag(options, "T10", PlcDataType.UInt16, 1, 0);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("CIO6144")]
    [InlineData("W512")]
    [InlineData("H512")]
    [InlineData("D32768")]
    [InlineData("T4096")]
    [InlineData("A2000")]
    public void DriverValidator_RejectsOutOfRangeAddress(string address)
    {
        OmronFinsProtocolDriver driver = new OmronFinsProtocolDriver();

        PlcTagValidationResult result = driver.ValidateTag(CreateOptions("Auto"), address, PlcDataType.UInt16, 1, 0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DriverValidator_RejectsArrayCrossingAreaBoundary()
    {
        OmronFinsProtocolDriver driver = new OmronFinsProtocolDriver();

        PlcTagValidationResult result = driver.ValidateTag(
            CreateOptions("Auto"),
            "D32767",
            PlcDataType.UInt16Array,
            2,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DriverValidator_ValidatesTimerCompletionFlagsByTimerNumber()
    {
        OmronFinsProtocolDriver driver = new OmronFinsProtocolDriver();
        PlcConnectionOptions options = CreateOptions("Auto");

        PlcTagValidationResult valid = driver.ValidateTag(options, "T4094", PlcDataType.BoolArray, 2, 0);
        PlcTagValidationResult crossingBoundary = driver.ValidateTag(options, "T4094", PlcDataType.BoolArray, 3, 0);
        PlcTagValidationResult invalidBitSuffix = driver.ValidateTag(options, "T10.1", PlcDataType.Bool, 1, 0);

        Assert.True(valid.IsValid, valid.ErrorMessage);
        Assert.False(crossingBoundary.IsValid);
        Assert.False(invalidBitSuffix.IsValid);
    }

    private static PlcConnectionOptions CreateOptions(string profile)
    {
        return new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OmronFins,
            Host = "127.0.0.1",
            Port = 9600,
            Transport = NetworkTransport.Udp,
            DriverOptionsJson = "{\"controllerProfile\":\"" + profile + "\",\"maxEmBank\":24}"
        };
    }
}
