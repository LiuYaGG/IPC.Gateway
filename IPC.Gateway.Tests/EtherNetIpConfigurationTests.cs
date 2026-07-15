using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Cip;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class EtherNetIpConfigurationTests
{
    [Fact]
    public void Drivers_KeepRockwellAndGenericEtherNetIpSeparate()
    {
        RockwellCipProtocolDriver rockwell = new();
        EtherNetIpProtocolDriver generic = new();

        Assert.Equal("Rockwell CIP", rockwell.DisplayName);
        Assert.Equal(PlcProtocol.RockwellCip, rockwell.Protocol);
        Assert.Equal("EtherNet/IP", generic.DisplayName);
        Assert.Equal(PlcProtocol.EtherNetIp, generic.Protocol);
    }

    [Fact]
    public void ConnectionParameters_ExposeExplicitAndClass1Settings()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.EtherNetIp);

        Assert.Contains(parameters, item => item.Key == "driverOptions.eipIoMode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.eipInputAssembly");
        Assert.Contains(parameters, item => item.Key == "driverOptions.eipOutputAssembly");
        Assert.Contains(parameters, item => item.Key == "driverOptions.eipRpiMilliseconds");
        Assert.Contains(parameters, item => item.Key == "driverOptions.eipIoStaleTimeoutMilliseconds");
    }

    [Theory]
    [InlineData("Assembly:100", "@4/100/3")]
    [InlineData("InputAssembly:0x65:1", "@4/101/3/1")]
    [InlineData("@0x01/1/7", "@1/1/7")]
    public void Address_NormalizesGenericObjectAndAssemblyAliases(string address, string expected)
    {
        Assert.Equal(expected, EtherNetIpAddress.Normalize(address));
    }

    [Fact]
    public void Driver_RequiresImplicitModeForInputOutputAddresses()
    {
        EtherNetIpProtocolDriver driver = new();
        PlcConnectionOptions explicitOptions = new()
        {
            Protocol = PlcProtocol.EtherNetIp,
            DriverOptionsJson = "{\"eipIoMode\":\"Explicit\"}"
        };
        PlcConnectionOptions implicitOptions = new()
        {
            Protocol = PlcProtocol.EtherNetIp,
            DriverOptionsJson = "{\"eipIoMode\":\"Implicit\"}"
        };

        Assert.False(driver.ValidateTag(explicitOptions, "Input:0.1", PlcDataType.Bool, 1, 0).IsValid);
        Assert.True(driver.ValidateTag(implicitOptions, "Input:0.1", PlcDataType.Bool, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(implicitOptions, "Output:0.1", PlcDataType.UInt16, 1, 0).IsValid);
        Assert.True(driver.ValidateTag(implicitOptions, "Output:2", PlcDataType.UInt16, 1, 0).IsValid);
    }

    [Fact]
    public void Driver_RejectsTagsBeyondConfiguredAssemblyLength()
    {
        EtherNetIpProtocolDriver driver = new();
        PlcConnectionOptions options = new()
        {
            Protocol = PlcProtocol.EtherNetIp,
            DriverOptionsJson = "{\"eipIoMode\":\"Implicit\",\"eipInputLength\":4,\"eipOutputLength\":2}"
        };

        Assert.True(driver.ValidateTag(options, "Input:2", PlcDataType.UInt16, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(options, "Input:3", PlcDataType.UInt16, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(options, "Output:1.7", PlcDataType.BoolArray, 2, 0).IsValid);
    }
}
