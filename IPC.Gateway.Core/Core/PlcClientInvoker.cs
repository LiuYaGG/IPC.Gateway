using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public static class PlcClientInvoker
    {
        public static PlcClientCapabilities GetCapabilities(IPlcClient client)
        {
            ArgumentNullException.ThrowIfNull(client);

            PlcClientCapabilities capabilities = client is IPlcClientCapabilityProvider provider
                ? PlcClientCapabilityCatalog.Normalize(provider.GetCapabilities(), client.Protocol)
                : PlcClientCapabilityCatalog.ForProtocol(client.Protocol);

            if (client is IPlcBatchReadClient || client is IAsyncPlcBatchReadClient)
                capabilities.SupportsBatchRead = true;
            if (client is IAsyncPlcSubscriptionClient)
                capabilities.SupportsSubscription = true;

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

            client.Connect();
            return ValueTask.CompletedTask;
        }

        public static ValueTask DisconnectAsync(IPlcClient client, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(client);

            if (client is IAsyncPlcClient asyncClient)
                return asyncClient.DisconnectAsync(cancellationToken);

            client.Disconnect();
            return ValueTask.CompletedTask;
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

            PlcReadResult result = client.Read(address, dataType, elementCount, elementOffset);
            return new ValueTask<PlcReadResult>(result);
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

            client.Write(address, dataType, valueText, elementOffset);
            return ValueTask.CompletedTask;
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
                return new ValueTask<IList<PlcBatchReadResult>>(batchClient.ReadMany(requests));

            throw new NotSupportedException($"PLC client '{client.Protocol}' does not support batch read.");
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
