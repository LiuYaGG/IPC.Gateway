using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Cip;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class RockwellCipConfigurationTests
{
    [Theory]
    [InlineData(PlcDataType.Byte, 0xD1, "255", 255, typeof(byte))]
    [InlineData(PlcDataType.UInt8, 0xC6, "255", 255, typeof(byte))]
    [InlineData(PlcDataType.Int8, 0xC2, "-1", -1, typeof(sbyte))]
    public void TagCodec_DistinguishesEightBitWireTypes(PlcDataType type, int wireType, string text, int expected, Type managedType)
    {
        Assert.Equal((ushort)wireType, CipTypeCodes.FromPlcDataType(type));
        byte[] encoded = CipDataCodec.Encode(type, text);
        Assert.Equal(new byte[] { 0xFF }, encoded);
        // A complete Read Tag reply must use the PLC's actual type, not infer it from the requested type.
        byte[] reply = { 0xCC, 0, 0, 0, (byte)wireType, 0, 0xFF };
        var decode = typeof(CipClient).GetMethod("DecodeReadTagResponse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var result = (PlcReadResult)decode.Invoke(null, new object[] { reply, type, 1 })!;
        Assert.Equal((ushort)wireType, result.TypeCode);
        Assert.Equal(managedType, result.Value.GetType());
        Assert.Equal(expected, Convert.ToInt32(result.Value));
        foreach (ushort otherType in new ushort[] { 0xD1, 0xC6, 0xC2 }.Where(item => item != wireType))
        {
            var error = Assert.Throws<InvalidOperationException>(() => CipDataCodec.Decode(type, otherType, encoded, 1));
            Assert.Contains("配置为" + CipTypeCodes.ToName((ushort)wireType), error.Message);
            Assert.Contains("PLC返回" + CipTypeCodes.ToName(otherType), error.Message);
        }
    }

    [Theory]
    [InlineData(PlcDataType.ByteArray, 0xD1, "0,128,255")]
    [InlineData(PlcDataType.UInt8Array, 0xC6, "0,128,255")]
    [InlineData(PlcDataType.Int8Array, 0xC2, "0,-128,-1")]
    public void ArrayCodec_DistinguishesEightBitTypesAndPreservesOffsets(PlcDataType type, int wireType, string text)
    {
        Assert.True(PlcDataTypeHelper.IsArray(type));
        Assert.Equal(1, PlcDataTypeHelper.GetElementSize(type));
        byte[] encoded = CipExplicitDataCodec.Encode(type, text, 0);
        Assert.Equal(new byte[] { 0, 128, 255 }, encoded);
        var result = CipExplicitDataCodec.Decode(type, encoded, 2, 1);
        Assert.Equal((ushort)wireType, result.TypeCode);
        if (type == PlcDataType.Int8Array)
            Assert.Equal(new sbyte[] { -128, -1 }, Assert.IsType<sbyte[]>(result.Value));
        else
            Assert.Equal(new byte[] { 128, 255 }, Assert.IsType<byte[]>(result.Value));
        foreach (ushort otherType in new ushort[] { 0xD1, 0xC6, 0xC2 }.Where(item => item != wireType))
            Assert.Throws<InvalidOperationException>(() => CipDataCodec.Decode(type, otherType, encoded, 3));
    }

    [Theory]
    [InlineData(PlcDataType.Byte, "-1")]
    [InlineData(PlcDataType.Byte, "256")]
    [InlineData(PlcDataType.ByteArray, "0,256")]
    [InlineData(PlcDataType.UInt8, "256")]
    [InlineData(PlcDataType.Int8, "128")]
    public void EightBitWrites_RejectValuesOutsideConfiguredRange(PlcDataType type, string text)
    {
        Assert.Throws<OverflowException>(() => CipDataCodec.Encode(type, text));
    }

    [Fact]
    public void EightBitTypes_PreserveExistingConfigurationValues()
    {
        Assert.Equal(23, (int)PlcDataType.Int8);
        Assert.Equal(24, (int)PlcDataType.UInt8);
        Assert.Equal(25, (int)PlcDataType.Int8Array);
        Assert.Equal(26, (int)PlcDataType.UInt8Array);
        Assert.NotEqual(PlcDataType.UInt8, PlcDataType.Byte);
        var options = new System.Text.Json.JsonSerializerOptions();
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        foreach (var type in new[] { PlcDataType.Byte, PlcDataType.ByteArray, PlcDataType.UInt8, PlcDataType.UInt8Array, PlcDataType.Int8, PlcDataType.Int8Array })
        {
            string json = System.Text.Json.JsonSerializer.Serialize(type, options);
            Assert.Equal("\"" + type + "\"", json);
            Assert.Equal(type, System.Text.Json.JsonSerializer.Deserialize<PlcDataType>(json, options));
        }
        Assert.Throws<InvalidOperationException>(() => CipDataCodec.Decode(PlcDataType.String, CipTypeCodes.Byte, new byte[] { 65 }, 1));
    }

    [Fact]
    public void ConnectionParameters_ExposeSlotAndRouteSettings()
    {
        IList<PlcConnectionParameterDefinition> parameters =
            PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.RockwellCip);

        Assert.Contains(parameters, item => item.Key == "slot" && item.Max == 255);
        Assert.Contains(parameters, item => item.Key == "driverOptions.cipRouteMode");
        Assert.Contains(parameters, item => item.Key == "driverOptions.cipRoutePath" && item.ParameterType == "textarea");
        Assert.Contains(parameters, item => item.Key == "driverOptions.cipBoolArrayMode" && item.Options.Contains("NativeBool"));
        Assert.Contains(parameters, item => item.Key == "driverOptions.cipStringFormat" && item.Options.Contains("CipString"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"cipRouteMode\":\"Slot\"}")]
    [InlineData("{\"cipRouteMode\":\"Direct\"}")]
    [InlineData("{\"cipRouteMode\":\"Custom\",\"cipRoutePath\":\"1,0/2,192.168.1.20/1,3\"}")]
    public void Driver_AcceptsCompatibleRouteConfigurations(string driverOptionsJson)
    {
        using IPlcClient client = new RockwellCipProtocolDriver().CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.RockwellCip,
            Host = "127.0.0.1",
            Port = 44818,
            Slot = 3,
            TimeoutMilliseconds = 100,
            DriverOptionsJson = driverOptionsJson
        });

        Assert.Equal(PlcProtocol.RockwellCip, client.Protocol);
    }

    [Fact]
    public void Driver_RejectsMalformedCustomRoute()
    {
        Assert.Throws<FormatException>(() => new RockwellCipProtocolDriver().CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.RockwellCip,
            Host = "127.0.0.1",
            Port = 44818,
            DriverOptionsJson = "{\"cipRouteMode\":\"Custom\",\"cipRoutePath\":\"invalid\"}"
        }));
    }

    [Theory]
    [InlineData("@1/1/7")]
    [InlineData("@0x01/0x01/0x07")]
    [InlineData("@4/100/3/1")]
    public void Driver_AcceptsGenericCipObjectAddresses(string address)
    {
        PlcTagValidationResult result = new RockwellCipProtocolDriver().ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.RockwellCip },
            address,
            PlcDataType.UInt16,
            1,
            0);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Theory]
    [InlineData("@1/1")]
    [InlineData("@1/1/7/1/2")]
    [InlineData("@class/1/7")]
    public void Driver_RejectsMalformedGenericCipObjectAddresses(string address)
    {
        PlcTagValidationResult result = new RockwellCipProtocolDriver().ValidateTag(
            new PlcConnectionOptions { Protocol = PlcProtocol.RockwellCip },
            address,
            PlcDataType.UInt16,
            1,
            0);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ExplicitAddress_EncodesLogicalClassInstanceAttributePath()
    {
        byte[] path = CipExplicitAddress.Parse("@0x01/1/7").EncodePath();

        Assert.Equal(new byte[] { 0x20, 0x01, 0x24, 0x01, 0x30, 0x07 }, path);
    }

    [Fact]
    public void ExplicitCodec_DecodesArraysWithClientSideOffset()
    {
        byte[] data = { 1, 0, 2, 0, 3, 0 };

        PlcReadResult result = CipExplicitDataCodec.Decode(PlcDataType.UInt16Array, data, 2, 1);

        Assert.Equal(new ushort[] { 2, 3 }, Assert.IsType<ushort[]>(result.Value));
    }

    [Fact]
    public void ExplicitCodec_SupportsCipSintAndUsint()
    {
        PlcReadResult sint = CipExplicitDataCodec.Decode(PlcDataType.Int8, new byte[] { 0xFF }, 1, 0);
        PlcReadResult usint = CipExplicitDataCodec.Decode(PlcDataType.UInt8, new byte[] { 0xFF }, 1, 0);

        Assert.Equal((sbyte)-1, sint.Value);
        Assert.Equal((byte)255, usint.Value);
        Assert.Equal(CipTypeCodes.Sint, sint.TypeCode);
        Assert.Equal(CipTypeCodes.Usint, usint.TypeCode);
    }

    [Fact]
    public void ExplicitCodec_DecodesCipShortString()
    {
        byte[] data = { 4, (byte)'T', (byte)'e', (byte)'s', (byte)'t' };

        PlcReadResult result = CipExplicitDataCodec.Decode(PlcDataType.String, data, 1, 0);

        Assert.Equal("Test", result.Value);
        Assert.Equal(CipTypeCodes.ShortString, result.TypeCode);
    }

    [Fact]
    public void TagCodec_DecodesStandardCipStringWithTwoByteLength()
    {
        byte[] data = { 4, 0, (byte)'T', (byte)'e', (byte)'s', (byte)'t' };

        object value = CipDataCodec.Decode(PlcDataType.String, CipTypeCodes.String, data, 1);

        Assert.Equal("Test", value);
    }

    [Fact]
    public void TagCodec_DecodesEmptyStandardCipString()
    {
        object value = CipDataCodec.Decode(PlcDataType.String, CipTypeCodes.String, new byte[] { 0, 0 }, 1);

        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void TagCodec_DecodesLogixStringWithoutUsingElementCountAsByteLimit()
    {
        byte[] data = { 4, 0, 0, 0, (byte)'T', (byte)'e', (byte)'s', (byte)'t' };

        object value = CipDataCodec.Decode(PlcDataType.String, CipTypeCodes.AbbreviatedStructure, data, 1);

        Assert.Equal("Test", value);
    }

    [Fact]
    public void TagCodec_EncodesStandardCipStringWithTwoByteLengthAndPadding()
    {
        byte[] data = CipDataCodec.EncodeStandardCipString("ABC");

        Assert.Equal(new byte[] { 3, 0, (byte)'A', (byte)'B', (byte)'C', 0 }, data);
    }

    [Fact]
    public void TagCodec_DecodesNativeBoolArray()
    {
        object value = CipDataCodec.Decode(
            PlcDataType.BoolArray,
            CipTypeCodes.Bool,
            new byte[] { 1, 0, 1, 0 },
            4);

        Assert.Equal(new[] { true, false, true, false }, Assert.IsType<bool[]>(value));
    }
}
