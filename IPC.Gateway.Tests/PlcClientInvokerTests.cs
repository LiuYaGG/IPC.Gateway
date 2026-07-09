using IPC.Plc.Communication.Core;

namespace IPC.Gateway.Tests;

public sealed class PlcClientInvokerTests
{
    [Fact]
    public async Task Invoker_UsesSyncFallbackWhenAsyncInterfaceIsMissing()
    {
        SyncBatchClient client = new SyncBatchClient();
        IList<PlcBatchReadRequest> requests = new List<PlcBatchReadRequest>
        {
            new PlcBatchReadRequest("D100", PlcDataType.Int16, 1, 0)
        };

        await PlcClientInvoker.ConnectAsync(client);
        PlcReadResult read = await PlcClientInvoker.ReadAsync(client, "D100", PlcDataType.Int16, 1, 0);
        await PlcClientInvoker.WriteAsync(client, "D100", PlcDataType.Int16, "12", 0);
        IList<PlcBatchReadResult> batch = await PlcClientInvoker.ReadManyAsync(client, requests);
        await PlcClientInvoker.DisconnectAsync(client);

        Assert.Equal(1, client.ConnectCalls);
        Assert.Equal(1, client.ReadCalls);
        Assert.Equal(1, client.WriteCalls);
        Assert.Equal(1, client.ReadManyCalls);
        Assert.Equal(1, client.DisconnectCalls);
        Assert.Equal("sync:D100", read.Value);
        Assert.Single(batch);
        Assert.True(batch[0].Success);
        Assert.False(PlcClientInvoker.SupportsAsyncClient(client));
        Assert.False(PlcClientInvoker.SupportsAsyncBatchRead(client));
        Assert.True(PlcClientInvoker.SupportsBatchRead(client));
    }

    [Fact]
    public async Task Invoker_PrefersAsyncInterfacesWhenAvailable()
    {
        AsyncClient client = new AsyncClient();
        IList<PlcBatchReadRequest> requests = new List<PlcBatchReadRequest>
        {
            new PlcBatchReadRequest("D200", PlcDataType.Int16, 1, 0)
        };

        await PlcClientInvoker.ConnectAsync(client);
        PlcReadResult read = await PlcClientInvoker.ReadAsync(client, "D200", PlcDataType.Int16, 1, 0);
        await PlcClientInvoker.WriteAsync(client, "D200", PlcDataType.Int16, "42", 0);
        IList<PlcBatchReadResult> batch = await PlcClientInvoker.ReadManyAsync(client, requests);
        await PlcClientInvoker.DisconnectAsync(client);

        Assert.Equal(1, client.AsyncConnectCalls);
        Assert.Equal(1, client.AsyncReadCalls);
        Assert.Equal(1, client.AsyncWriteCalls);
        Assert.Equal(1, client.AsyncReadManyCalls);
        Assert.Equal(1, client.AsyncDisconnectCalls);
        Assert.Equal("async:D200", read.Value);
        Assert.Single(batch);
        Assert.True(batch[0].Success);
        Assert.True(PlcClientInvoker.SupportsAsyncClient(client));
        Assert.True(PlcClientInvoker.SupportsAsyncBatchRead(client));
        Assert.True(PlcClientInvoker.SupportsBatchRead(client));
    }

    [Fact]
    public async Task Invoker_ChecksCancellationBeforeSyncFallback()
    {
        SyncBatchClient client = new SyncBatchClient();
        using CancellationTokenSource cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlcClientInvoker.ConnectAsync(client, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlcClientInvoker.ReadAsync(client, "D100", PlcDataType.Int16, 1, 0, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlcClientInvoker.WriteAsync(client, "D100", PlcDataType.Int16, "1", 0, cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PlcClientInvoker.ReadManyAsync(client, new List<PlcBatchReadRequest>(), cancellation.Token));

        Assert.Equal(0, client.ConnectCalls);
        Assert.Equal(0, client.ReadCalls);
        Assert.Equal(0, client.WriteCalls);
        Assert.Equal(0, client.ReadManyCalls);
    }

    [Fact]
    public async Task ReadManyAsync_ThrowsWhenClientDoesNotSupportBatchRead()
    {
        BasicClient client = new BasicClient();

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await PlcClientInvoker.ReadManyAsync(client, new List<PlcBatchReadRequest>()));

        Assert.False(PlcClientInvoker.SupportsBatchRead(client));
    }

    [Fact]
    public void GetCapabilities_ReflectsActualBatchInterface()
    {
        SyncBatchClient client = new SyncBatchClient { ProtocolOverride = PlcProtocol.Plugin };

        PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);

        Assert.Equal(PlcClientAsyncKind.SyncOnly, capabilities.AsyncKind);
        Assert.True(capabilities.SupportsBatchRead);
        Assert.True(capabilities.RequiresSerializedAccess);
    }

    private class BasicClient : IPlcClient
    {
        public bool IsConnected { get; private set; }
        public PlcProtocol Protocol { get; set; } = PlcProtocol.VirtualPlc;

        public virtual void Connect()
        {
            IsConnected = true;
        }

        public virtual void Disconnect()
        {
            IsConnected = false;
        }

        public virtual PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            return new PlcReadResult(0, dataType.ToString(), "basic:" + address);
        }

        public virtual void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class SyncBatchClient : BasicClient, IPlcBatchReadClient
    {
        public int ConnectCalls { get; private set; }
        public int DisconnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public int ReadManyCalls { get; private set; }
        public PlcProtocol ProtocolOverride
        {
            set { Protocol = value; }
        }

        public override void Connect()
        {
            ConnectCalls++;
            base.Connect();
        }

        public override void Disconnect()
        {
            DisconnectCalls++;
            base.Disconnect();
        }

        public override PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            ReadCalls++;
            return new PlcReadResult(0, dataType.ToString(), "sync:" + address);
        }

        public override void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            WriteCalls++;
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            ReadManyCalls++;
            return requests
                .Select(request => PlcBatchReadResult.FromSuccess(
                    request,
                    new PlcReadResult(0, request.DataType.ToString(), "batch:" + request.Address)))
                .ToList();
        }
    }

    private sealed class AsyncClient : IPlcClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        public int AsyncConnectCalls { get; private set; }
        public int AsyncDisconnectCalls { get; private set; }
        public int AsyncReadCalls { get; private set; }
        public int AsyncWriteCalls { get; private set; }
        public int AsyncReadManyCalls { get; private set; }
        public bool IsConnected { get; private set; }
        public PlcProtocol Protocol
        {
            get { return PlcProtocol.Plugin; }
        }

        public void Connect()
        {
            throw new InvalidOperationException("Sync connect path should not be used.");
        }

        public void Disconnect()
        {
            throw new InvalidOperationException("Sync disconnect path should not be used.");
        }

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            throw new InvalidOperationException("Sync read path should not be used.");
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            throw new InvalidOperationException("Sync write path should not be used.");
        }

        public ValueTask ConnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncConnectCalls++;
            IsConnected = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisconnectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncDisconnectCalls++;
            IsConnected = false;
            return ValueTask.CompletedTask;
        }

        public ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncReadCalls++;
            return new ValueTask<PlcReadResult>(new PlcReadResult(0, dataType.ToString(), "async:" + address));
        }

        public ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncWriteCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AsyncReadManyCalls++;
            IList<PlcBatchReadResult> results = requests
                .Select(request => PlcBatchReadResult.FromSuccess(
                    request,
                    new PlcReadResult(0, request.DataType.ToString(), "async-batch:" + request.Address)))
                .ToList();
            return new ValueTask<IList<PlcBatchReadResult>>(results);
        }

        public void Dispose()
        {
        }
    }
}
