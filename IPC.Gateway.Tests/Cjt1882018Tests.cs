using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Plc.Communication.Metering.Cjt188;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class Cjt1882018Tests
{
    [Fact]
    public void Driver_RegistersAsIndependentReadOnlyProtocol()
    {
        Cjt1882018ProtocolDriver driver = new();
        using IPlcClient client = driver.CreateClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.Cjt1882018,
            Host = "127.0.0.1",
            Port = 4001
        });

        Assert.Equal(PlcProtocol.Cjt1882018, client.Protocol);
        PlcClientCapabilities capabilities = PlcClientCapabilityCatalog.ForProtocol(PlcProtocol.Cjt1882018);
        Assert.True(capabilities.SupportsRead);
        Assert.False(capabilities.SupportsWrite);
    }

    [Fact]
    public void CompiledPlan_Maps2018MeterTypeAndFriendlyFields()
    {
        TagConfig tag = new()
        {
            Id = "meter-value",
            Name = "热水累计量",
            MeterType = "热水表",
            MeterAddress = "12345678901234",
            MeterDataIdentifier = "901F",
            DataType = PlcDataType.Double,
            ElementCount = 1
        };
        DeviceConfig device = new()
        {
            Id = "meter",
            Name = "meter",
            Protocol = PlcProtocol.Cjt1882018,
            Connection = new PlcConnectionOptions
            {
                Protocol = PlcProtocol.Cjt1882018,
                Host = "127.0.0.1",
                Port = 4001
            },
            Tags = { tag }
        };

        CompiledTagRead compiled = CompiledDeviceReadPlan.Compile(device).Get(tag);

        Assert.True(compiled.IsStaticallyValid, compiled.ValidationError);
        Assert.Equal("CJ188:11:12345678901234:901F", compiled.Address);
    }

    [Fact]
    public void Frame_ExcludesSequenceByteAndChecksMeterIdentity()
    {
        Cjt188Address address = Cjt188Address.Parse("CJ188:10:12345678901234:901F");
        byte[] frame = BuildReadResponse(address, 0x00, new byte[] { 0x12, 0x34 });

        Assert.Equal(new byte[] { 0x12, 0x34 }, Cjt188Frame.ExtractReadData(frame, address));

        byte[] wrongSequence = BuildReadResponse(address, 0x01, new byte[] { 0x12, 0x34 });
        Assert.Throws<FormatException>(() => Cjt188Frame.ExtractReadData(wrongSequence, address));

        byte[] wrongMeter = (byte[])frame.Clone();
        wrongMeter[2] ^= 0x01;
        UpdateChecksum(wrongMeter);
        Assert.Throws<FormatException>(() => Cjt188Frame.ExtractReadData(wrongMeter, address));
    }

    private static byte[] BuildReadResponse(Cjt188Address address, byte sequence, byte[] value)
    {
        List<byte> frame = new() { 0x68, address.MeterType };
        frame.AddRange(address.GetAddressBytes());
        frame.Add(0x81);
        frame.Add(checked((byte)(value.Length + 3)));
        frame.AddRange(address.GetDataIdentifierBytes());
        frame.Add(sequence);
        frame.AddRange(value);
        frame.Add(0x00);
        frame.Add(0x16);
        byte[] result = frame.ToArray();
        UpdateChecksum(result);
        return result;
    }

    private static void UpdateChecksum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < frame.Length - 2; i++)
            sum += frame[i];
        frame[^2] = (byte)sum;
    }
}
