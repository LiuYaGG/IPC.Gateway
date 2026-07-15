using System.Text;
using System.Reflection;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Gateway.Tests;

public sealed class MitsubishiMcFrameCodecTests
{
    [Fact]
    public void Binary3E_PreservesLegacyFrameAndParsesWords()
    {
        object codec = CreateCodec("3E", "Binary");
        byte[] request = BuildRequest(codec, 0x0401, 0x0000, "D100", 1);

        Assert.Equal(new byte[] { 0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00 }, request.Take(7));

        byte[] response = { 0xD0, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x04, 0x00, 0x00, 0x00, 0x34, 0x12 };
        Assert.Equal(new byte[] { 0x34, 0x12 }, ParseResponse(codec, response, request));
    }

    [Fact]
    public void Binary4E_UsesAndValidatesSerialNumber()
    {
        object codec = CreateCodec("4E", "Binary");
        byte[] request = BuildRequest(codec, 0x0401, 0x0000, "D100", 1);
        byte serialLow = request[2];
        byte serialHigh = request[3];
        byte[] response = { 0xD4, 0x00, serialLow, serialHigh, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x04, 0x00, 0x00, 0x00, 0x78, 0x56 };

        Assert.Equal(new byte[] { 0x78, 0x56 }, ParseResponse(codec, response, request));
        response[2]++;
        Assert.Throws<TargetInvocationException>(() => ParseResponse(codec, response, request));
    }

    [Fact]
    public void Ascii3E_BuildsDocumentedLayoutAndParsesWordData()
    {
        object codec = CreateCodec("3E", "ASCII");
        byte[] request = BuildRequest(codec, 0x0401, 0x0000, "D100", 1);

        Assert.Equal("500000FF03FF000018001004010000D*0001000001", Encoding.ASCII.GetString(request));

        byte[] response = Encoding.ASCII.GetBytes("D00000FF03FF00000800001234");
        Assert.Equal(new byte[] { 0x34, 0x12 }, ParseResponse(codec, response, request));
    }

    [Fact]
    public void Ascii3E_ParsesBitDataIntoMcPackedFormat()
    {
        object codec = CreateCodec("3E", "ASCII");
        byte[] request = BuildRequest(codec, 0x0401, 0x0001, "M100", 3);
        byte[] response = Encoding.ASCII.GetBytes("D00000FF03FF0000070000101");

        Assert.Equal(new byte[] { 0x10, 0x10 }, ParseResponse(codec, response, request));
    }

    private static object CreateCodec(string frameType, string dataCode)
    {
        PlcConnectionOptions connection = new PlcConnectionOptions
        {
            Protocol = PlcProtocol.MitsubishiMc,
            Host = "127.0.0.1",
            Port = 5000,
            Rack = 0,
            Slot = 0,
            DriverOptionsJson = $"{{\"mcFrameType\":\"{frameType}\",\"mcDataCode\":\"{dataCode}\"}}"
        };
        using McClient client = new McClient(connection);
        FieldInfo field = typeof(McClient).GetField("_frameCodec", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return field.GetValue(client)!;
    }

    private static byte[] BuildRequest(object codec, ushort command, ushort subcommand, string address, int points)
    {
        Assembly assembly = typeof(McClient).Assembly;
        Type addressType = assembly.GetType("IPC.Plc.Communication.MitsubishiMc.McAddress", true)!;
        object parsed = addressType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { address })!;
        MethodInfo method = codec.GetType().GetMethod("BuildRequest", BindingFlags.Public | BindingFlags.Instance)!;
        return (byte[])method.Invoke(codec, new object?[] { command, subcommand, parsed, points, null })!;
    }

    private static byte[] ParseResponse(object codec, byte[] response, byte[] request)
    {
        MethodInfo method = codec.GetType().GetMethod("ParseResponse", BindingFlags.Public | BindingFlags.Instance)!;
        return (byte[])method.Invoke(codec, new object[] { response, request })!;
    }
}
