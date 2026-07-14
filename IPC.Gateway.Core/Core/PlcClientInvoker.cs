using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public static class PlcClientInvoker
    {
        private static readonly BoundedSynchronousIoExecutor SynchronousExecutor =
            new BoundedSynchronousIoExecutor(
                Math.Clamp(Environment.ProcessorCount, 4, 16),
                1024);

        public static PlcClientCapabilities GetCapabilities(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            PlcClientCapabilities capabilities = client is IPlcClientCapabilityProvider provider
                ? PlcClientCapabilityCatalog.Normalize(provider.GetCapabilities(), client.Protocol)
                : PlcClientCapabilityCatalog.ForProtocol(client.Protocol);

            bool supportsAsync = client is IAsyncPlcClient;
            bool supportsBatch = client is IPlcBatchReadClient || client is IAsyncPlcBatchReadClient;
            bool supportsSubscription = client is IAsyncPlcSubscriptionClient;
            bool declaredSubscription = capabilities.SupportsSubscription;

            capabilities.SupportsNativeAsync = supportsAsync && capabilities.AsyncKind == PlcClientAsyncKind.NativeIo;
            if (!supportsAsync && capabilities.AsyncKind == PlcClientAsyncKind.NativeIo)
                capabilities.AsyncKind = PlcClientAsyncKind.DedicatedThread;

            capabilities.SupportsBatchRead = supportsBatch;
            capabilities.SupportsSubscription = supportsSubscription;
            if (!supportsBatch)
                capabilities.MaxBatchItems = 0;
            else if (capabilities.MaxBatchItems <= 0)
                capabilities.MaxBatchItems = 128;
            if (!supportsSubscription)
                capabilities.MaxSubscriptionItems = 0;
            else if (capabilities.MaxSubscriptionItems <= 0)
                capabilities.MaxSubscriptionItems = 1000;

            if (supportsSubscription && !declaredSubscription)
                capabilities.PreferredReadMode = PlcPreferredReadMode.Subscription;

            if (capabilities.PreferredReadMode == PlcPreferredReadMode.Subscription && !supportsSubscription)
                capabilities.PreferredReadMode = supportsBatch ? PlcPreferredReadMode.Batch : PlcPreferredReadMode.Single;
            if (capabilities.PreferredReadMode == PlcPreferredReadMode.Batch && !supportsBatch)
                capabilities.PreferredReadMode = PlcPreferredReadMode.Single;

            return capabilities;
        }

        public static bool SupportsAsyncClient(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            return client is IAsyncPlcClient;
        }

        public static bool SupportsAsyncBatchRead(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            return client is IAsyncPlcBatchReadClient;
        }

        public static bool SupportsBatchRead(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            return client is IPlcBatchReadClient || client is IAsyncPlcBatchReadClient;
        }

        public static bool SupportsSubscription(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);
            return client is IAsyncPlcSubscriptionClient;
        }

        public static ValueTask ConnectAsync(IPlcClient client, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcClient asyncClient)
                return asyncClient.ConnectAsync(cancellationToken);

            return InvokeSynchronousAsync(client.Connect, cancellationToken);
        }

        public static ValueTask DisconnectAsync(IPlcClient client, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcClient asyncClient)
                return asyncClient.DisconnectAsync(cancellationToken);

            return InvokeSynchronousAsync(client.Disconnect, cancellationToken);
        }

        public static ValueTask<PlcReadResult> ReadAsync(
            IPlcClient client,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcClient asyncClient)
                return asyncClient.ReadAsync(address, dataType, elementCount, elementOffset, cancellationToken);

            return InvokeSynchronousAsync(
                () => client.Read(address, dataType, elementCount, elementOffset),
                cancellationToken);
        }

        public static ValueTask WriteAsync(
            IPlcClient client,
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcClient asyncClient)
                return asyncClient.WriteAsync(address, dataType, valueText, elementOffset, cancellationToken);

            return InvokeSynchronousAsync(
                () => client.Write(address, dataType, valueText, elementOffset),
                cancellationToken);
        }

        public static ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IPlcClient client,
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(requests);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcBatchReadClient asyncBatchClient)
                return asyncBatchClient.ReadManyAsync(requests, cancellationToken);

            if (client is IPlcBatchReadClient batchClient)
                return InvokeSynchronousAsync(() => batchClient.ReadMany(requests), cancellationToken);

            throw new NotSupportedException($"PLC client '{client.Protocol}' does not support batch read.");
        }

        public static async ValueTask InvokeSynchronousAsync(
            Action operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            cancellationToken.ThrowIfCancellationRequested();
            Task operationTask = SynchronousExecutor.InvokeAsync(operation, cancellationToken).AsTask();
            try
            {
                await operationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObserveLateFault(operationTask);
                throw;
            }
        }

        public static async ValueTask<T> InvokeSynchronousAsync<T>(
            Func<T> operation,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(operation);
            cancellationToken.ThrowIfCancellationRequested();
            Task<T> operationTask = SynchronousExecutor.InvokeAsync(operation, cancellationToken).AsTask();
            try
            {
                return await operationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ObserveLateFault(operationTask);
                throw;
            }
        }

        private static void ObserveLateFault(Task operationTask)
        {
            _ = operationTask.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        public static ValueTask<IPlcSubscription> SubscribeAsync(
            IPlcClient client,
            IList<PlcSubscriptionRequest> requests,
            PlcSubscriptionOptions options,
            Func<PlcSubscriptionUpdate, ValueTask> onUpdate,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(requests);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(onUpdate);
            cancellationToken.ThrowIfCancellationRequested();

            if (client is IAsyncPlcSubscriptionClient subscriptionClient)
                return subscriptionClient.SubscribeAsync(requests, options, onUpdate, cancellationToken);

            throw new NotSupportedException($"PLC client '{client.Protocol}' does not support subscriptions.");
        }
    }
}
