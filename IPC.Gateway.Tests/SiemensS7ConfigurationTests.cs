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
        Assert.Equal((byte)255, Decode(PlcDataType.Byte, Encode(PlcDataType.Byte, "255"), 1));
        Assert.Equal(new byte[] { 0, 128, 255 }, (byte[])Decode(PlcDataType.ByteArray, Encode(PlcDataType.ByteArray, "0,128,255"), 3));
        Assert.Equal((byte)255, Decode(PlcDataType.UInt8, Encode(PlcDataType.UInt8, "255"), 1));
        Assert.Equal(new byte[] { 0, 128, 255 }, (byte[])Decode(PlcDataType.UInt8Array, Encode(PlcDataType.UInt8Array, "0,128,255"), 3));
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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public async Task ReadMany_MixedBoolAndIntAcceptsPaddedAndUnpaddedResponses(
        bool boolFirst, bool padded, bool asynchronous)
    {
        byte[] boolItem = { 0xFF, 0x04, 0x00, 0x08, 0x01 };
        byte[] intItem = { 0xFF, 0x04, 0x00, 0x20, 0x12, 0x34, 0x56, 0x78 };
        List<byte> data = new List<byte>();
        data.AddRange(boolFirst ? boolItem : intItem);
        if (boolFirst && padded)
            data.Add(0x00);
        data.AddRange(boolFirst ? intItem : boolItem);
        byte[] response = BuildMultiReadResponse(data.ToArray());

        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task server = RunS7ServerAsync(listener, response);
        using S7Client client = new S7Client(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port,
            Rack = 0,
            Slot = 1
        });
        PlcBatchReadRequest boolRequest = new("DB1.DBX0.0", PlcDataType.Bool, 1, 0);
        PlcBatchReadRequest intRequest = new("DB1.DBD2", PlcDataType.Int32, 1, 0);
        List<PlcBatchReadRequest> requests = boolFirst
            ? new() { boolRequest, intRequest }
            : new() { intRequest, boolRequest };
        IList<PlcBatchReadResult> results;
        if (asynchronous)
        {
            await client.ConnectAsync(CancellationToken.None);
            results = await client.ReadManyAsync(requests, CancellationToken.None);
        }
        else
        {
            results = await Task.Run(() =>
            {
                client.Connect();
                return client.ReadMany(requests);
            });
        }

        Assert.All(results, result => Assert.True(result.Success, result.ErrorMessage));
        Assert.Equal(true, results[boolFirst ? 0 : 1].Result!.Value);
        Assert.Equal(0x12345678, results[boolFirst ? 1 : 0].Result!.Value);
        await server;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(true, 0x31)]
    [InlineData(true, 0xFF)]
    public async Task ReadManyAsync_OddLengthItemBeforeFailedTagPreservesFailureScope(bool padded, byte fillByte = 0)
    {
        List<byte> data = new() { 0xFF, 0x04, 0x00, 0x08, 0x01 };
        if (padded)
            data.Add(fillByte);
        data.AddRange(new byte[] { 0x05, 0x00, 0x00, 0x00 });
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Task server = RunS7ServerAsync(listener, BuildMultiReadResponse(data.ToArray()));
        using S7Client client = new(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7,
            Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port
        });
        IList<PlcBatchReadResult> results = await client.ReadManyAsync(new List<PlcBatchReadRequest>
        {
            new("DB1.DBX0.0", PlcDataType.Bool, 1, 0),
            new("DB99.DBD2", PlcDataType.Int32, 1, 0)
        }, CancellationToken.None);
        Assert.True(results[0].Success, results[0].ErrorMessage);
        Assert.Equal(true, results[0].Result!.Value);
        Assert.False(results[1].Success);
        Assert.Equal(PlcReadFailureScope.Tag, results[1].FailureScope);
        await server;
    }

    [Theory]
    [InlineData("short-item")]
    [InlineData("truncated-payload")]
    [InlineData("wrong-count")]
    [InlineData("wrong-data-length")]
    [InlineData("bad-error-length")]
    [InlineData("bad-error-transport")]
    public async Task ReadManyAsync_RejectsMalformedFramesWithoutReturningWrongTagValues(string fault)
    {
        byte[] response = BuildMultiReadResponse(new byte[]
        {
            0xFF, 0x04, 0, 8, 1, 0x31,
            0xFF, 0x04, 0, 32, 0x42, 0xF6, 0xE6, 0x66
        });
        switch (fault)
        {
            case "short-item": response[30] = 16; break;
            case "truncated-payload":
                response = response[..^1];
                response[3] = (byte)response.Length;
                break;
            case "wrong-count": response[20] = 3; break;
            case "wrong-data-length": response[16]++; break;
            case "bad-error-length":
                response = BuildMultiReadResponse(new byte[] { 0x0A, 0, 0, 8, 0xFF, 0x04, 0, 32, 0x42, 0xF6, 0xE6, 0x66 });
                break;
            case "bad-error-transport":
                response = BuildMultiReadResponse(new byte[] { 0x0A, 0x04, 0, 4, 0xFF, 0x04, 0, 32, 0x42, 0xF6, 0xE6, 0x66 });
                break;
        }
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task server = RunS7ServerAsync(listener, response);
        using S7Client client = new(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7, Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IList<PlcBatchReadResult> results = await client.ReadManyAsync(new List<PlcBatchReadRequest>
        {
            new("DB1.DBX0.0", PlcDataType.Bool, 1, 0),
            new("DB2.DBD2", PlcDataType.Float, 1, 0)
        }, timeout.Token);
        Assert.All(results, result =>
        {
            Assert.False(result.Success);
            Assert.Null(result.Result);
            Assert.Equal(PlcReadFailureScope.Batch, result.FailureScope);
        });
        await server;
    }

    [Theory]
    [InlineData(0, true, 0x0A)]
    [InlineData(1, true, 0x0A)]
    [InlineData(2, true, 0x0A)]
    [InlineData(0, false, 0x0A)]
    [InlineData(1, false, 0x0A)]
    [InlineData(2, false, 0x0A)]
    [InlineData(0, true, 0x05)]
    [InlineData(1, true, 0x05)]
    [InlineData(2, true, 0x05)]
    [InlineData(0, false, 0x05)]
    [InlineData(1, false, 0x05)]
    [InlineData(2, false, 0x05)]
    public async Task ReadMany_Snap7FailedItemHeaderDoesNotFailOtherTags(int failedIndex, bool asynchronous, byte errorCode)
    {
        var requests = new List<PlcBatchReadRequest>
        {
            new("DB1.DBX0.0", PlcDataType.Bool, 1, 0),
            new("DB1.DBD2", PlcDataType.Float, 1, 0)
        };
        requests.Insert(failedIndex, new("DB4.DBD0", PlcDataType.Float, 1, 0));
        var items = new List<byte[]>
        {
            new byte[] { 0xFF, 0x04, 0, 8, 1 },
            new byte[] { 0xFF, 0x04, 0, 32, 0x42, 0xF6, 0xE6, 0x66 }
        };
        // Snap7 reports length 4 for the failed status header, with no payload.
        items.Insert(failedIndex, new byte[] { errorCode, 0, 0, 4 });
        var data = new List<byte>();
        for (int i = 0; i < items.Count; i++)
        {
            data.AddRange(items[i]);
            if (items[i].Length % 2 != 0 && i < items.Count - 1)
                data.Add(0x31);
        }
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task server = RunS7ServerAsync(listener, BuildMultiReadResponse(data.ToArray(), 3), 3, 2);
        using S7Client client = new(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7, Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port, TimeoutMilliseconds = 2000
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (int attempt = 0; attempt < 2; attempt++)
        {
            IList<PlcBatchReadResult> results = asynchronous
                ? await client.ReadManyAsync(requests, timeout.Token)
                : await Task.Run(() => client.ReadMany(requests));
            Assert.Equal(3, results.Count);
            for (int i = 0; i < results.Count; i++)
            {
                if (i == failedIndex)
                {
                    Assert.False(results[i].Success);
                    Assert.Equal(PlcReadFailureScope.Tag, results[i].FailureScope);
                    Assert.False(results[i].IsCommunicationError);
                    Assert.Contains("0x" + errorCode.ToString("X2"), results[i].ErrorMessage);
                }
                else
                {
                    Assert.True(results[i].Success, results[i].ErrorMessage);
                    if (requests[i].DataType == PlcDataType.Bool)
                        Assert.Equal(true, results[i].Result!.Value);
                    else
                        Assert.Equal(123.45f, results[i].Result!.Value);
                }
            }
        }
        await server;
    }

    [Theory]
    [InlineData(true, 0x31)]
    [InlineData(false, 0x31)]
    [InlineData(true, 0xFF)]
    [InlineData(false, 0xFF)]
    public async Task ReadMany_BoolLastPreservesMixedValuesAfterFloatWrite(bool asynchronous, int fillByte)
    {
        // A second DB keeps this on the multi-variable path even after contiguous-plan fixes.
        List<PlcBatchReadRequest> requests = new()
        {
            new("DB1.DBX0.0", PlcDataType.Bool, 1, 0),
            new("DB1.DBX0.1", PlcDataType.Bool, 1, 0),
            new("DB1.DBX0.2", PlcDataType.Bool, 1, 0),
            new("DB2.DBD0", PlcDataType.Int16, 1, 0),
            new("DB1.DBD2", PlcDataType.Float, 1, 0),
            new("DB1.DBX0.3", PlcDataType.Bool, 1, 0)
        };
        await VerifyMixedReadWriteSequence(requests, asynchronous, false, (byte)fillByte);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadMany_OverlappingBoolLastKeepsContiguousReadAfterFloatWrite(bool asynchronous)
    {
        List<PlcBatchReadRequest> requests = new()
        {
            new("DB1.DBX0.0", PlcDataType.Bool, 1, 0),
            new("DB1.DBX0.1", PlcDataType.Bool, 1, 0),
            new("DB1.DBX0.2", PlcDataType.Bool, 1, 0),
            new("DB1.DBD0", PlcDataType.Int16, 1, 0),
            new("DB1.DBD2", PlcDataType.Float, 1, 0),
            new("DB1.DBX0.3", PlcDataType.Bool, 1, 0)
        };
        await VerifyMixedReadWriteSequence(requests, asynchronous, true, 0);
    }

    private static async Task VerifyMixedReadWriteSequence(
        List<PlcBatchReadRequest> requests, bool asynchronous, bool contiguous, byte fillByte)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        Task server = ServeMixedReadWriteSequence(listener, contiguous, fillByte, timeout.Token);
        using S7Client client = new(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.SiemensS7, Host = "127.0.0.1",
            Port = ((IPEndPoint)listener.LocalEndpoint).Port, TimeoutMilliseconds = 2000
        });
        async Task Run()
        {
            if (asynchronous)
                await client.ConnectAsync(timeout.Token);
            else
                client.Connect();
            for (int read = 0; read < 3; read++)
            {
                if (read == 1)
                {
                    if (asynchronous)
                        await client.WriteAsync("DB1.DBD2", PlcDataType.Float, "123.45", 0, timeout.Token);
                    else
                        client.Write("DB1.DBD2", PlcDataType.Float, "123.45", 0);
                }
                IList<PlcBatchReadResult> results = asynchronous
                    ? await client.ReadManyAsync(requests, timeout.Token)
                    : client.ReadMany(requests);
                Assert.All(results, result => Assert.True(result.Success, result.ErrorMessage));
                foreach (int index in new[] { 0, 1, 2, 5 })
                    Assert.Equal(true, results[index].Result!.Value);
                Assert.Equal((short)0x0F37, results[3].Result!.Value);
                Assert.Equal(read == 0 ? 0f : 123.45f, results[4].Result!.Value);
            }
        }
        await Task.WhenAll(Task.Run(async () =>
        {
            try { await Run(); }
            finally { client.Disconnect(); }
        }), server);
    }

    private static async Task ServeMixedReadWriteSequence(
        TcpListener listener, bool contiguous, byte fillByte, CancellationToken token)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync(token);
        using var cancellation = token.Register(connection.Dispose);
        using NetworkStream stream = connection.GetStream();
        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(new byte[] { 0x03, 0x00, 0x00, 0x07, 0x02, 0xD0, 0x00 }, token);
        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(BuildSetupResponse(), token);
        for (int read = 0; read < 3; read++)
        {
            if (read == 1)
            {
                byte[] write = await ReadTpktAsync(stream);
                Assert.Equal(0x05, write[17]);
                Assert.Equal(Encode(PlcDataType.Float, "123.45"), write[^4..]);
                byte[] ack = BuildMultiReadResponse(new byte[] { 0xFF }, 1);
                ack[19] = 0x05;
                await stream.WriteAsync(ack, token);
            }
            byte[] request = await ReadTpktAsync(stream);
            Assert.Equal(0x04, request[17]);
            Assert.Equal(contiguous ? 1 : 6, request[18]);
            byte[] floatBytes = read == 0 ? new byte[4] : Encode(PlcDataType.Float, "123.45");
            List<byte> data = new();
            if (contiguous)
            {
                Assert.Equal(6, request[23] << 8 | request[24]);
                data.AddRange(new byte[] { 0xFF, 0x04, 0, 48, 0x0F, 0x37 });
                data.AddRange(floatBytes);
            }
            else
            {
                for (int i = 0; i < 3; i++)
                    data.AddRange(new byte[] { 0xFF, 0x04, 0, 8, 0x0F, read == 0 ? (byte)0 : fillByte });
                data.AddRange(new byte[] { 0xFF, 0x04, 0, 16, 0x0F, 0x37, 0xFF, 0x04, 0, 32 });
                data.AddRange(floatBytes);
                data.AddRange(new byte[] { 0xFF, 0x04, 0, 8, 0x0F });
            }
            await stream.WriteAsync(BuildMultiReadResponse(data.ToArray(), contiguous ? 1 : 6), token);
        }
    }

    private static byte[] BuildMultiReadResponse(byte[] data, int itemCount = 2)
    {
        List<byte> response = new()
        {
            0x03, 0x00, 0x00, (byte)(21 + data.Length),
            0x02, 0xF0, 0x80,
            0x32, 0x03, 0x00, 0x00, 0x00, 0x02,
            0x00, 0x02, 0x00, (byte)data.Length, 0x00, 0x00,
            0x04, (byte)itemCount
        };
        response.AddRange(data);
        return response.ToArray();
    }

    private static async Task RunS7ServerAsync(TcpListener listener, byte[]? response = null, int itemCount = 2, int readCount = 1)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = connection.GetStream();

        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(new byte[] { 0x03, 0x00, 0x00, 0x07, 0x02, 0xD0, 0x00 });

        _ = await ReadTpktAsync(stream);
        await stream.WriteAsync(BuildSetupResponse());

        for (int i = 0; i < readCount; i++)
        {
            byte[] request = await ReadTpktAsync(stream);
            Assert.Equal(0x04, request[17]);
            Assert.Equal(itemCount, request[18]);
            await stream.WriteAsync(response ?? BuildMultiReadResponse());
        }
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

    [Theory]
    [InlineData("-1")]
    [InlineData("256")]
    public void ByteCodec_RejectsOutOfRangeValues(string text)
    {
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => Encode(PlcDataType.UInt8, text));
        Assert.IsType<OverflowException>(exception.InnerException);
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
