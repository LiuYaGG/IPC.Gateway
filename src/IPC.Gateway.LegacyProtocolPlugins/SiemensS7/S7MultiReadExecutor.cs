using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    internal static class S7MultiReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany(
            IList<PlcBatchReadRequest> requests,
            S7Client client)
        {
            if (requests == null || requests.Count == 0)
                return new List<PlcBatchReadResult>();

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<S7MultiReadItem> items = BuildItems(requests, client, results);
            List<List<S7MultiReadItem>> batches = BuildBatches(items, client);

            foreach (List<S7MultiReadItem> batch in batches)
            {
                try
                {
                    if (RequiresSegmentedRead(batch, client))
                    {
                        S7MultiReadItem item = batch[0];
                        PlcReadResult result = client.Read(
                            item.Request.Address,
                            item.Request.DataType,
                            item.Request.ElementCount,
                            item.Request.ElementOffset);
                        results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, result);
                        continue;
                    }
                    client.ReadItemBatch(batch);
                    DecodeBatch(batch, client, results);
                }
                catch (Exception ex)
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Batch);
                    FailBatch(batch, results, ex.Message, scope);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    {
                        FailRemaining(items, results, ex.Message, scope);
                        break;
                    }
                }
            }

            return CompleteResults(requests, results);
        }

        private static List<S7MultiReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            S7Client client,
            PlcBatchReadResult[] results)
        {
            List<S7MultiReadItem> items = new List<S7MultiReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i] ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                try
                {
                    EnsureSupportedType(request.DataType);
                    if (request.ElementCount <= 0)
                        throw new ArgumentOutOfRangeException(nameof(request.ElementCount));
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException(nameof(request.ElementOffset));

                    S7Address address = client.BuildBatchAddress(request);
                    items.Add(new S7MultiReadItem
                    {
                        Index = i,
                        Request = request,
                        Address = address,
                        ByteCount = S7DataCodec.GetReadByteCount(request.DataType, address.BitOffset, request.ElementCount)
                    });
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
            return items;
        }

        private static List<List<S7MultiReadItem>> BuildBatches(List<S7MultiReadItem> items, S7Client client)
        {
            List<List<S7MultiReadItem>> batches = new List<List<S7MultiReadItem>>();
            List<S7MultiReadItem> current = new List<S7MultiReadItem>();
            int requestSize = 12;
            int responseSize = 14;

            foreach (S7MultiReadItem item in items)
            {
                int nextRequestSize = requestSize + 12;
                int nextResponseSize = responseSize + item.ResponseSize;
                bool full = current.Count >= client.MaxItemsPerRequest ||
                            nextRequestSize > client.NegotiatedPduSize ||
                            nextResponseSize > client.NegotiatedPduSize;
                if (full && current.Count > 0)
                {
                    batches.Add(current);
                    current = new List<S7MultiReadItem>();
                    requestSize = 12;
                    responseSize = 14;
                }

                current.Add(item);
                requestSize += 12;
                responseSize += item.ResponseSize;
            }
            if (current.Count > 0)
                batches.Add(current);
            return batches;
        }

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            S7Client client,
            CancellationToken cancellationToken)
        {
            if (requests == null || requests.Count == 0)
                return new List<PlcBatchReadResult>();

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<S7MultiReadItem> items = BuildItems(requests, client, results);
            List<List<S7MultiReadItem>> batches = BuildBatches(items, client);

            foreach (List<S7MultiReadItem> batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (RequiresSegmentedRead(batch, client))
                    {
                        S7MultiReadItem item = batch[0];
                        PlcReadResult result = await client.ReadAsync(
                            item.Request.Address,
                            item.Request.DataType,
                            item.Request.ElementCount,
                            item.Request.ElementOffset,
                            cancellationToken).ConfigureAwait(false);
                        results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, result);
                        continue;
                    }
                    await client.ReadItemBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                    DecodeBatch(batch, client, results);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Batch);
                    FailBatch(batch, results, ex.Message, scope);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    {
                        FailRemaining(items, results, ex.Message, scope);
                        break;
                    }
                }
            }

            return CompleteResults(requests, results);
        }

        private static void DecodeBatch(
            List<S7MultiReadItem> batch,
            S7Client client,
            PlcBatchReadResult[] results)
        {
            foreach (S7MultiReadItem item in batch)
            {
                if (!string.IsNullOrEmpty(item.ErrorMessage))
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(
                        item.Request,
                        item.ErrorMessage,
                        item.FailureScope);
                    continue;
                }

                try
                {
                    object value = S7DataCodec.Decode(
                        item.Request.DataType,
                        item.Data,
                        item.Address.BitOffset,
                        item.Request.ElementCount);
                    PlcReadResult readResult = new PlcReadResult(
                        0,
                        client.GetBatchTypeName(item.Request.DataType),
                        value);
                    results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request, readResult);
                }
                catch (Exception ex)
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, ex.Message, PlcReadFailureScope.Tag);
                }
            }
        }

        private static void FailBatch(
            IEnumerable<S7MultiReadItem> batch,
            PlcBatchReadResult[] results,
            string message,
            PlcReadFailureScope scope)
        {
            foreach (S7MultiReadItem item in batch)
                results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, message, scope);
        }

        private static void FailRemaining(
            IEnumerable<S7MultiReadItem> items,
            PlcBatchReadResult[] results,
            string message,
            PlcReadFailureScope scope)
        {
            foreach (S7MultiReadItem item in items)
            {
                if (results[item.Index] == null)
                    results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, message, scope);
            }
        }

        private static IList<PlcBatchReadResult> CompleteResults(
            IList<PlcBatchReadRequest> requests,
            PlcBatchReadResult[] results)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>(requests.Count);
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = requests[i] ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(
                    request,
                    "S7 batch read did not produce a result.",
                    PlcReadFailureScope.Batch));
            }
            return output;
        }

        private static bool RequiresSegmentedRead(List<S7MultiReadItem> batch, S7Client client)
        {
            return batch.Count == 1 && 14 + batch[0].ResponseSize > client.NegotiatedPduSize;
        }

        private static bool IsCommunicationException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is PlcCommunicationException ||
                    current is TimeoutException ||
                    current is IOException ||
                    current is SocketException ||
                    current is ObjectDisposedException)
                    return true;

                string text = (current.Message ?? string.Empty).ToLowerInvariant();
                if (text.Contains("timeout") || text.Contains("timed out") ||
                    text.Contains("socket") || text.Contains("closed") ||
                    text.Contains("not connected") || text.Contains("unreachable"))
                    return true;
                current = current.InnerException;
            }
            return false;
        }

        private static void EnsureSupportedType(PlcDataType dataType)
        {
            if (dataType == PlcDataType.Coil ||
                dataType == PlcDataType.CoilArray ||
                dataType == PlcDataType.DiscreteInput ||
                dataType == PlcDataType.DiscreteInputArray)
                throw new NotSupportedException("Siemens S7 does not support Modbus Coil/Discrete Input data types. Use BOOL or BOOL[] instead.");
        }
    }
}
