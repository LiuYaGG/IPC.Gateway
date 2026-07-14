using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class PlcSynchronousInvokerTimeoutTests
{
    [Fact]
    public async Task CancellationReleasesCallerFromBlockingSyncRead()
    {
        using ManualResetEventSlim release = new ManualResetEventSlim(false);
        BlockingSyncReadClient client = new BlockingSyncReadClient(release);
        using CancellationTokenSource cancellation = new CancellationTokenSource(50);

        Task<PlcReadResult> readTask = PlcClientInvoker
            .ReadAsync(client, "D100", PlcDataType.Int16, 1, 0, cancellation.Token)
            .AsTask();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => readTask);
        }
        finally
        {
            release.Set();
        }
    }

    private sealed class BlockingSyncReadClient : IPlcClient
    {
        private readonly ManualResetEventSlim _release;

        public BlockingSyncReadClient(ManualResetEventSlim release)
        {
            _release = release;
        }

        public bool IsConnected { get; private set; }
        public PlcProtocol Protocol => PlcProtocol.Plugin;
        public void Connect() => IsConnected = true;
        public void Disconnect() => IsConnected = false;

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            _release.Wait();
            return new PlcReadResult(0, dataType.ToString(), (short)1);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
        }

        public void Dispose() => Disconnect();
    }
}
