using System.Net;
using System.Net.Sockets;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Gateway.Tests;

public sealed class MitsubishiMcUdpRetryTests
{
    [Fact]
    public async Task ReadAsync_RetriesOnceAfterUdpTimeout()
    {
        using UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        int requestCount = 0;

        Task responder = Task.Run(async () =>
        {
            UdpReceiveResult first = await server.ReceiveAsync(testTimeout.Token);
            Interlocked.Increment(ref requestCount);
            UdpReceiveResult second = await server.ReceiveAsync(testTimeout.Token);
            Interlocked.Increment(ref requestCount);
            byte[] response = CreateReadResponse(second.Buffer, 0x1234);
            await server.SendAsync(response, second.RemoteEndPoint, testTimeout.Token);
        }, testTimeout.Token);

        using McClient client = CreateClient(port);
        PlcReadResult result = await client.ReadAsync("D0", PlcDataType.Int16, 1, 0, testTimeout.Token);
        await responder;

        Assert.Equal(2, requestCount);
        Assert.Equal((short)0x1234, Assert.IsType<short>(result.Value));
        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task WriteAsync_DoesNotRetryAfterUdpTimeout()
    {
        using UdpClient server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using CancellationTokenSource testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task<UdpReceiveResult> firstRequest = server.ReceiveAsync(testTimeout.Token).AsTask();

        using McClient client = CreateClient(port);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
            await client.WriteAsync("D0", PlcDataType.Int16, "1", 0, testTimeout.Token));
        await firstRequest;

        using CancellationTokenSource noSecondRequest = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await server.ReceiveAsync(noSecondRequest.Token));
        Assert.True(client.IsConnected);
    }

    private static McClient CreateClient(int port)
    {
        return new McClient(new PlcConnectionOptions
        {
            Protocol = PlcProtocol.MitsubishiMc,
            Transport = NetworkTransport.Udp,
            Host = IPAddress.Loopback.ToString(),
            Port = port,
            TimeoutMilliseconds = 100
        });
    }

    private static byte[] CreateReadResponse(byte[] request, ushort value)
    {
        return new byte[]
        {
            0xD0, 0x00,
            request[2], request[3], request[4], request[5], request[6],
            0x04, 0x00,
            0x00, 0x00,
            (byte)(value & 0xFF), (byte)(value >> 8)
        };
    }
}
