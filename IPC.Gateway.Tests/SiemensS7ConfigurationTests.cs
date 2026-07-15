using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Gateway.LegacyProtocolPlugins;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.SiemensS7;

namespace IPC.Gateway.Tests;

public sealed class SiemensS7ConfigurationTests
{
    [Fact]
    public void Catalog_UsesPort102AndGroupedS7Options()
    {
        IList<PlcConnectionParameterDefinition> parameters = PlcConnectionParameterCatalog.ForProtocol(PlcProtocol.SiemensS7);

        Assert.Equal("102", Find(parameters, "port").DefaultValue);
        Assert.Equal("控制器", Find(parameters, "driverOptions.controllerProfile").Group);
        Assert.Equal("S7连接", Find(parameters, "driverOptions.s7TsapMode").Group);
        Assert.Equal("自定义TSAP", Find(parameters, "driverOptions.s7RemoteTsap").Group);
        Assert.Equal("批读优化", Find(parameters, "driverOptions.s7MaxItemsPerRequest").Group);
        Assert.DoesNotContain(parameters, item => item.Key == "wordOrder");
    }

    [Fact]
    public void Client_ResolvesRackSlotAndCustomTsap()
    {
        using S7Client rackSlot = new S7Client(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = 102,
            Rack = 0,
            Slot = 2,
            DriverOptionsJson = "{\"controllerProfile\":\"S7-300\",\"s7ConnectionType\":\"OP\"}"
        });
        Assert.Equal("S7-300", rackSlot.ControllerProfile);
        Assert.Equal((ushort)0x0100, rackSlot.LocalTsap);
        Assert.Equal((ushort)0x0202, rackSlot.RemoteTsap);

        using S7Client custom = new S7Client(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = 102,
            DriverOptionsJson = "{\"s7TsapMode\":\"Custom\",\"s7LocalTsap\":\"1200\",\"s7RemoteTsap\":\"13-02\"}"
        });
        Assert.Equal((ushort)0x1200, custom.LocalTsap);
        Assert.Equal((ushort)0x1302, custom.RemoteTsap);
    }

    [Fact]
    public void DriverValidation_AcceptsGermanIoAliasesAndRejectsNonBooleanBitAddress()
    {
        SiemensS7ProtocolDriver driver = new SiemensS7ProtocolDriver();
        PlcConnectionOptions options = new PlcConnectionOptions { Protocol = PlcProtocol.SiemensS7 };

        Assert.True(driver.ValidateTag(options, "E0.0", PlcDataType.Bool, 1, 0).IsValid);
        Assert.True(driver.ValidateTag(options, "AW10", PlcDataType.UInt16, 1, 0).IsValid);
        Assert.False(driver.ValidateTag(options, "DB1.DBX0.1", PlcDataType.Int16, 1, 0).IsValid);
    }

    [Fact]
    public void Codec_RoundTripsAllFrontendS7ScalarAndArrayTypes()
    {
        Assert.Equal(true, Decode(PlcDataType.Bool, Encode(PlcDataType.Bool, "true"), 1));
        Assert.Equal(short.MinValue, Decode(PlcDataType.Int16, Encode(PlcDataType.Int16, short.MinValue.ToString()), 1));
        Assert.Equal(ushort.MaxValue, Decode(PlcDataType.UInt16, Encode(PlcDataType.UInt16, ushort.MaxValue.ToString()), 1));
        Assert.Equal(int.MinValue, Decode(PlcDataType.Int32, Encode(PlcDataType.Int32, int.MinValue.ToString()), 1));
        Assert.Equal(uint.MaxValue, Decode(PlcDataType.UInt32, Encode(PlcDataType.UInt32, uint.MaxValue.ToString()), 1));
        Assert.Equal(long.MinValue, Decode(PlcDataType.Int64, Encode(PlcDataType.Int64, long.MinValue.ToString()), 1));
        Assert.Equal(ulong.MaxValue, Decode(PlcDataType.UInt64, Encode(PlcDataType.UInt64, ulong.MaxValue.ToString()), 1));
        Assert.Equal(12.5f, Decode(PlcDataType.Float, Encode(PlcDataType.Float, "12.5"), 1));
        Assert.Equal(-9876.125d, Decode(PlcDataType.Double, Encode(PlcDataType.Double, "-9876.125"), 1));
        Assert.Equal("S7-TEST", Decode(PlcDataType.String, Encode(PlcDataType.String, "S7-TEST"), 7));

        Assert.Equal(new[] { true, false, true }, (bool[])Decode(PlcDataType.BoolArray, new byte[] { 0x05 }, 3));
        Assert.Equal(new short[] { -1, 2 }, (short[])Decode(PlcDataType.Int16Array, Encode(PlcDataType.Int16Array, "-1,2"), 2));
        Assert.Equal(new ushort[] { 1, ushort.MaxValue }, (ushort[])Decode(PlcDataType.UInt16Array, Encode(PlcDataType.UInt16Array, "1,65535"), 2));
        Assert.Equal(new[] { int.MinValue, int.MaxValue }, (int[])Decode(PlcDataType.Int32Array, Encode(PlcDataType.Int32Array, "-2147483648,2147483647"), 2));
        Assert.Equal(new[] { 0U, uint.MaxValue }, (uint[])Decode(PlcDataType.UInt32Array, Encode(PlcDataType.UInt32Array, "0,4294967295"), 2));
        Assert.Equal(new[] { long.MinValue, long.MaxValue }, (long[])Decode(PlcDataType.Int64Array, Encode(PlcDataType.Int64Array, "-9223372036854775808,9223372036854775807"), 2));
        Assert.Equal(new[] { 0UL, ulong.MaxValue }, (ulong[])Decode(PlcDataType.UInt64Array, Encode(PlcDataType.UInt64Array, "0,18446744073709551615"), 2));
        Assert.Equal(new[] { 1.25f, -2.5f }, (float[])Decode(PlcDataType.FloatArray, Encode(PlcDataType.FloatArray, "1.25,-2.5"), 2));
        Assert.Equal(new[] { 1.25d, -2.5d }, (double[])Decode(PlcDataType.DoubleArray, Encode(PlcDataType.DoubleArray, "1.25,-2.5"), 2));
    }

    [Fact]
    public async Task ReadManyAsync_SendsOneMultiVariableRequestAndIsolatesBadTag()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task server = RunS7ServerAsync(listener);

        using S7Client client = new S7Client(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = port,
            Rack = 0,
            Slot = 1
        });
        await client.ConnectAsync(CancellationToken.None);
        IList<PlcBatchReadResult> results = await client.ReadManyAsync(new List<PlcBatchReadRequest>
        {
            new PlcBatchReadRequest("DB1.DBW0", PlcDataType.UInt16, 1, 0),
            new PlcBatchReadRequest("DB99.DBW0", PlcDataType.UInt16, 1, 0)
        }, CancellationToken.None);

        Assert.Equal((ushort)0x1234, results[0].Result!.Value);
        Assert.False(results[1].Success);
        Assert.Equal(PlcReadFailureScope.Tag, results[1].FailureScope);
        Assert.False(results[1].IsCommunicationError);
        await server;
    }

    private static async Task RunS7ServerAsync(TcpListener listener)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = connection.GetStream();

        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(new byte[] { 0x03, 0x00, 0x00, 0x07, 0x02, 0xD0, 0x00 });

        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(BuildSetupResponse());

        byte[] request = await ReadTpktAsync(stream);
        Assert.Equal(0x04, request[17]);
        Assert.Equal(2, request[18]);
        await stream.WriteAsync(BuildMultiReadResponse());
    }

    private static byte[] Encode(PlcDataType dataType, string value)
    {
        Type codec = typeof(S7Client).Assembly.GetType("IPC.Plc.Communication.SiemensS7.S7DataCodec", true)!;
        return (byte[])codec.GetMethod("Encode")!.Invoke(null, new object[] { dataType, value })!;
    }

    private static object Decode(PlcDataType dataType, byte[] data, int count)
    {
        Type codec = typeof(S7Client).Assembly.GetType("IPC.Plc.Communication.SiemensS7.S7DataCodec", true)!;
        return codec.GetMethod("Decode")!.Invoke(null, new object[] { dataType, data, 0, count })!;
    }

    private static byte[] BuildSetupResponse()
    {
        return new byte[]
        {
            0x03, 0x00, 0x00, 0x1B,
            0x02, 0xF0, 0x80,
            0x32, 0x03, 0x00, 0x00, 0x00, 0x01,
            0x00, 0x08, 0x00, 0x00, 0x00, 0x00,
            0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0
        };
    }

    private static byte[] BuildMultiReadResponse()
    {
        return new byte[]
        {
            0x03, 0x00, 0x00, 0x1F,
            0x02, 0xF0, 0x80,
            0x32, 0x03, 0x00, 0x00, 0x00, 0x02,
            0x00, 0x02, 0x00, 0x0A, 0x00, 0x00,
            0x04, 0x02,
            0xFF, 0x04, 0x00, 0x10, 0x12, 0x34,
            0x05, 0x00, 0x00, 0x00
        };
    }

    private static async Task<byte[]> ReadTpktAsync(NetworkStream stream)
    {
        byte[] header = await ReadExactAsync(stream, 4);
        int length = (header[2] << 8) | header[3];
        byte[] packet = new byte[length];
        Buffer.BlockCopy(header, 0, packet, 0, 4);
        byte[] body = await ReadExactAsync(stream, length - 4);
        Buffer.BlockCopy(body, 0, packet, 4, body.Length);
        return packet;
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        byte[] data = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(data.AsMemory(offset, count - offset));
            if (read == 0)
                throw new InvalidOperationException("Test S7 connection closed unexpectedly.");
            offset += read;
        }
        return data;
    }

    private static PlcConnectionParameterDefinition Find(
        IEnumerable<PlcConnectionParameterDefinition> parameters,
        string key)
    {
        return Assert.Single(parameters, item => item.Key == key);
    }
}
