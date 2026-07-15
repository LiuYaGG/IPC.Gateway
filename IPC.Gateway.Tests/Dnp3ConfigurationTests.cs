using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Dnp3;

namespace IPC.Gateway.Tests;

public sealed class Dnp3ConfigurationTests
{
    [Fact]
    public void ConnectionParameters_ExposeLinkScanAndCommandOptions()
    {
        IList<PlcConnectionParameterDefinition> parameters = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.Dnp3);

        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3LocalAddress");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3RemoteAddress");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3ScanGapLimit");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3SelectBeforeOperate");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3StartupIntegrity");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3EnableUnsolicited");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3EventScanIntervalSeconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3IntegrityScanIntervalSeconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3CacheMaxAgeMilliseconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.dnp3TimeSyncMode");
    }

    [Theory]
    [InlineData("Binary:0", Dnp3PointType.Binary, 0)]
    [InlineData("analog:12", Dnp3PointType.Analog, 12)]
    [InlineData("BinaryOutput:65535", Dnp3PointType.BinaryOutput, 65535)]
    public void Address_ParsesPointTypeAndIndex(string text, Dnp3PointType pointType, ushort index)
    {
        Dnp3Address address = Dnp3Address.Parse(text);

        Assert.Equal(pointType, address.PointType);
        Assert.Equal(index, address.Index);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Analog")]
    [InlineData("Unknown:1")]
    [InlineData("Analog:65536")]
    public void Address_RejectsInvalidPoint(string text)
    {
        Assert.Throws<FormatException>(() => Dnp3Address.Parse(text));
    }

    [Fact]
    public void Driver_ValidatesScalarPointDefinition()
    {
        Dnp3ProtocolDriver driver = new Dnp3ProtocolDriver();

        Assert.True(driver.ValidateTag(new PlcConnectionOptions(), "Analog:8", PlcDataType.Double, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(new PlcConnectionOptions(), "Analog:8", PlcDataType.DoubleArray, 2, 0).IsValid);
    }
}
