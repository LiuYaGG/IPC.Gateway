using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Ads;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class BeckhoffAdsConfigurationTests
{
    [Fact]
    public void ConnectionParameters_ExposeAmsRouteAndRuntimePort()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.BeckhoffAds);

        Assert.Contains(parameters, item => item.Key == "driverOptions.amsNetId");
        Assert.Contains(parameters, item => item.Key == "driverOptions.adsPort" && item.DefaultValue == "851");
        Assert.Contains(parameters, item => item.Key == "driverOptions.adsMaxBatchItems");
    }

    [Fact]
    public void Driver_CreatesAdsClientWithoutOpeningRoute()
    {
        using IPlcClient client = new BeckhoffAdsProtocolDriver().CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.BeckhoffAds,
            Host = "192.168.1.20",
            Port = 48898,
            DriverOptionsJson = "{\"amsNetId\":\"192.168.1.20.1.1\",\"adsPort\":851}"
        });

        Assert.Equal(PlcProtocol.BeckhoffAds, client.Protocol);
    }

    [Theory]
    [InlineData("MAIN.Counter")]
    [InlineData("MAIN.Values[0]")]
    [InlineData("GVL.Axis_1.Position")]
    public void Address_AcceptsTwinCatSymbolPaths(string address)
    {
        Assert.Equal(address, AdsAddress.Parse(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("MAIN..Counter")]
    [InlineData("MAIN.Values[-1]")]
    [InlineData("1MAIN.Counter")]
    public void Address_RejectsMalformedSymbolPaths(string address)
    {
        Assert.Throws<FormatException>(() => AdsAddress.Parse(address));
    }

    [Fact]
    public void Address_ElementOffsetAdvancesExistingArrayIndex()
    {
        Assert.Equal("MAIN.Values[7]", AdsAddress.WithElementOffset("MAIN.Values[2]", 5));
        Assert.Equal("MAIN.Values[5]", AdsAddress.WithElementOffset("MAIN.Values", 5));
    }

    [Theory]
    [InlineData(PlcDataType.Int8, typeof(sbyte))]
    [InlineData(PlcDataType.UInt8, typeof(byte))]
    [InlineData(PlcDataType.Int64Array, typeof(long[]))]
    [InlineData(PlcDataType.DoubleArray, typeof(double[]))]
    public void DataCodec_MapsSupportedManagedTypes(PlcDataType dataType, Type expected)
    {
        Assert.Equal(expected, AdsDataCodec.GetManagedType(dataType));
    }

    [Fact]
    public void Driver_RejectsModbusOnlyBitType()
    {
        PlcTagValidationResult result = new BeckhoffAdsProtocolDriver().ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.BeckhoffAds },
            "MAIN.Bit",
            PlcDataType.Coil,
            1,
            0);

        Assert.False(result.IsValid);
    }
}
