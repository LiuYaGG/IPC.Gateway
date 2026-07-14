using System.Net;
using System.Net.Sockets;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Gateway.Tests;

public sealed class NModbusTcpAdapterTests
{
    [Fact]
    public async Task Client_UsesNModbusForReadAndWriteWhileKeepingGatewayAddressSemantics()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        List<(byte UnitId, byte Function)> requests = new();

        Task serverTask = RunServerAsync(listener, requests, expectedRequests: 2);

        using ModbusTcpClient client = new(new PlcConnectionOptions
        {
            Host = "127.0.0.1",
            Port = port,
            Rack = 7,
            TimeoutMilliseconds = 2000
        });

        client.Connect();
        PlcReadResult result = client.Read("HR0", PlcDataType.UInt16, 1, 0);
        client.Write("HR1", PlcDataType.UInt16, "4660", 0);
        client.Disconnect();

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal((ushort)0x1234, result.Value);
        Assert.Equal(new[] { (UnitId: (byte)7, Function: (byte)3), (UnitId: (byte)7, Function: (byte)6) }, requests);
    }

    private static async Task RunServerAsync(
        TcpListener listener,
        List<(byte UnitId, byte Function)> requests,
        int expectedRequests)
    {
        using TcpClient connection = await listener.AcceptTcpClientAsync();
        using NetworkStream stream = connection.GetStream();

        for (int i = 0; i < expectedRequests; i++)
        {
            byte[] header = await ReadExactAsync(stream, 7);
            int pduLength = ReadUInt16(header, 4) - 1;
            byte[] pdu = await ReadExactAsync(stream, pduLength);
            requests.Add((header[6], pdu[0]));

            byte[] responsePdu = pdu[0] switch
            {
                3 => new byte[] { 3, 2, 0x12, 0x34 },
                6 => pdu,
                _ => throw new InvalidOperationException($"Unexpected Modbus function {pdu[0]}.")
            };
            await WriteResponseAsync(stream, header, responsePdu);
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, byte[] requestHeader, byte[] pdu)
    {
        byte[] response = new byte[7 + pdu.Length];
        response[0] = requestHeader[0];
        response[1] = requestHeader[1];
        response[4] = (byte)((pdu.Length + 1) >> 8);
        response[5] = (byte)(pdu.Length + 1);
        response[6] = requestHeader[6];
        Buffer.BlockCopy(pdu, 0, response, 7, pdu.Length);
        await stream.WriteAsync(response);
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count)
    {
        byte[] data = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(data.AsMemory(offset, count - offset));
            if (read == 0)
                throw new IOException("The test Modbus connection closed unexpectedly.");
            offset += read;
        }
        return data;
    }

    private static int ReadUInt16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }
}
