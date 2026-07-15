using System.Net;
using System.Net.Sockets;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.OmronFins;

namespace IPC.Gateway.Tests;

public sealed class OmronFinsUdpClientTests
{
    [Fact]
    public async Task ReadAsync_RetriesOneTimedOutUdpRequest()
    {
        using UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task serverTask = Task.Run(async () =>
        {
            _ = await server.ReceiveAsync(testTimeout.Token);
            UdpReceiveResult retry = await server.ReceiveAsync(testTimeout.Token);
            byte[] response = BuildReadResponse(retry.Buffer, 0x1234);
            await server.SendAsync(response, retry.RemoteEndPoint, testTimeout.Token);
        }, testTimeout.Token);

        using FinsClient client = new FinsClient(CreateOptions(port, 120));
        await client.ConnectAsync(testTimeout.Token);
        PlcReadResult result = await client.ReadAsync("D100", PlcDataType.UInt16, 1, 0, testTimeout.Token);

        Assert.Equal((ushort)0x1234, result.Value);
        await serverTask;
    }

    [Fact]
    public async Task WriteAsync_DoesNotRetryTimedOutUdpRequest()
    {
        using UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using FinsClient client = new FinsClient(CreateOptions(port, 100));
        await client.ConnectAsync(testTimeout.Token);

        Task<UdpReceiveResult> firstRequest = server.ReceiveAsync(testTimeout.Token).AsTask();
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.WriteAsync("D100", PlcDataType.UInt16, "1", 0, testTimeout.Token));
        _ = await firstRequest;

        using CancellationTokenSource noRetryWindow = new CancellationTokenSource(300);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.ReceiveAsync(noRetryWindow.Token));
    }

    [Fact]
    public async Task ReadAsync_UsesTimerNumberForConsecutiveCompletionFlags()
    {
        using UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task serverTask = Task.Run(async () =>
        {
            UdpReceiveResult read = await server.ReceiveAsync(testTimeout.Token);
            Assert.Equal(0x09, read.Buffer[12]);
            Assert.Equal(0x0F, read.Buffer[13]);
            Assert.Equal(0xFE, read.Buffer[14]);
            Assert.Equal(0, read.Buffer[15]);
            Assert.Equal(0, read.Buffer[16]);
            Assert.Equal(2, read.Buffer[17]);
            await server.SendAsync(BuildBitReadResponse(read.Buffer, 1, 0), read.RemoteEndPoint, testTimeout.Token);
        }, testTimeout.Token);

        using FinsClient client = new FinsClient(CreateOptions(port, 500));
        await client.ConnectAsync(testTimeout.Token);
        PlcReadResult result = await client.ReadAsync("T4094", PlcDataType.BoolArray, 2, 0, testTimeout.Token);

        Assert.Equal(new[] { true, false }, Assert.IsType<bool[]>(result.Value));
        await serverTask;
    }

    private static PlcConnectionOptions CreateOptions(int port, int timeoutMilliseconds)
    {
        return new PlcConnectionOptions
        {
            Protocol = PlcProtocol.OmronFins,
            Host = "127.0.0.1",
            Port = port,
            Transport = NetworkTransport.Udp,
            TimeoutMilliseconds = timeoutMilliseconds,
            DriverOptionsJson = "{\"sourceNode\":1,\"destinationNode\":1,\"udpReadRetries\":1}"
        };
    }

    private static byte[] BuildReadResponse(byte[] request, ushort value)
    {
        byte[] response = new byte[16];
        response[0] = (byte)(request[0] | 0x40);
        response[1] = request[1];
        response[2] = request[2];
        response[3] = request[6];
        response[4] = request[7];
        response[5] = request[8];
        response[6] = request[3];
        response[7] = request[4];
        response[8] = request[5];
        response[9] = request[9];
        response[10] = request[10];
        response[11] = request[11];
        response[14] = (byte)(value >> 8);
        response[15] = (byte)value;
        return response;
    }

    private static byte[] BuildBitReadResponse(byte[] request, params byte[] values)
    {
        byte[] response = new byte[14 + values.Length];
        response[0] = (byte)(request[0] | 0x40);
        response[1] = request[1];
        response[2] = request[2];
        response[3] = request[6];
        response[4] = request[7];
        response[5] = request[8];
        response[6] = request[3];
        response[7] = request[4];
        response[8] = request[5];
        response[9] = request[9];
        response[10] = request[10];
        response[11] = request[11];
        Buffer.BlockCopy(values, 0, response, 14, values.Length);
        return response;
    }
}
