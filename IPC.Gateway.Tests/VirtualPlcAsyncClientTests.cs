using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.VirtualPlc;

namespace IPC.Gateway.Tests;

public sealed class VirtualPlcAsyncClientTests
{
    [Fact]
    public async Task VirtualPlc_ImplementsAsyncClientWithSynchronousCompletionCapability()
    {
        VirtualPlcClient client = new VirtualPlcClient(new PlcConnectionOptions
        {
            Host = "virtual-async-" + Guid.NewGuid().ToString("N")
        });

        Assert.True(PlcClientInvoker.SupportsAsyncClient(client));

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
        Assert.Equal(PlcClientAsyncKind.SynchronousCompletion, capabilities.AsyncKind);
        Assert.False(capabilities.SupportsNativeAsync);
        Assert.True(capabilities.SupportsConcurrentRequests);

        await PlcClientInvoker.ConnectAsync(client);
        await PlcClientInvoker.WriteAsync(client, "D100", PlcDataType.Int16, "123", 0);
        PlcReadResult result = await PlcClientInvoker.ReadAsync(client, "D100", PlcDataType.Int16, 1, 0);
        await PlcClientInvoker.DisconnectAsync(client);

        Assert.Equal((short)123, result.Value);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task VirtualPlcAsyncRead_ObservesCancellationBeforeAccessingStore()
    {
        VirtualPlcClient client = new VirtualPlcClient(new PlcConnectionOptions
        {
            Host = "virtual-async-cancel-" + Guid.NewGuid().ToString("N")
        });
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlcClientInvoker.ConnectAsync(client, cancellation.Token));

        Assert.False(client.IsConnected);
    }
}
