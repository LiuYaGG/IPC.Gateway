using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Snmp;
using Lextm.SharpSnmpLib;

namespace IPC.Gateway.Tests;

public sealed class SnmpConfigurationTests
{
    [Fact]
    public void ConnectionParameters_ExposeCommunityAndV3Security()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.Snmp);

        Assert.Contains(parameters, item => item.Key == "driverOptions.snmpVersion");
        Assert.Contains(parameters, item => item.Key == "driverOptions.snmpCommunity" && item.Secret);
        Assert.Contains(parameters, item => item.Key == "driverOptions.snmpAuthPassword" && item.Secret);
        Assert.Contains(parameters, item => item.Key == "driverOptions.snmpPrivacyProtocol");
        Assert.Contains(parameters, item => item.Key == "driverOptions.snmpMaxOidsPerRequest");
    }

    [Theory]
    [InlineData("1.3.6.1.2.1.1.3.0", "1.3.6.1.2.1.1.3.0")]
    [InlineData(".1.3.6.1.2.1.1.1.0", "1.3.6.1.2.1.1.1.0")]
    public void Address_NormalizesNumericOid(string address, string expected)
    {
        Assert.Equal(expected, SnmpAddress.Parse(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("sysUpTime.0")]
    [InlineData("1")]
    [InlineData("1.3..6")]
    public void Address_RejectsInvalidOid(string address)
    {
        Assert.Throws<FormatException>(() => SnmpAddress.Parse(address));
    }

    [Fact]
    public void DataCodec_DecodesCommonSnmpValues()
    {
        Assert.Equal(42, SnmpDataCodec.Decode(new Integer32(42), PlcDataType.Int32));
        Assert.Equal((uint)7, SnmpDataCodec.Decode(new Gauge32(7), PlcDataType.UInt32));
        Assert.Equal((ulong)99, SnmpDataCodec.Decode(new Counter64(99), PlcDataType.UInt64));
        Assert.Equal("gateway", SnmpDataCodec.Decode(new OctetString("gateway"), PlcDataType.String));
    }

    [Fact]
    public void DataCodec_RejectsNoSuchInstanceAsBadTag()
    {
        Assert.ThrowsAny<Exception>(() => SnmpDataCodec.Decode(new NoSuchInstance(), PlcDataType.String));
    }

    [Fact]
    public void Driver_RejectsArraysAndOffsets()
    {
        SnmpProtocolDriver driver = new SnmpProtocolDriver();
        PlcConnectionOptions options = new PlcConnectionOptions { Protocol = PlcProtocol.Snmp };

        Assert.False(driver.ValidateTag(options, "1.3.6.1.2.1.1.3.0", PlcDataType.Int32Array, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(options, "1.3.6.1.2.1.1.3.0", PlcDataType.Int32, 1, 1).IsValid);
    }
}
