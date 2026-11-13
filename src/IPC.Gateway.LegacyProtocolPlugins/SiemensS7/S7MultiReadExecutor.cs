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
            var batches = new Queue<List<S7MultiReadItem>>(BuildBatches(MergeRanges(items, client), client));
            int remainingDiagnosticBatches = 4;

            while (batches.TryDequeue(out var batch))
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
                    DecodeBatch(batch, client, results, batches, ref remainingDiagnosticBatches);
                }
                catch (Exception ex)
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Batch);
                    FailBatch(batch, results, ex.Message, scope);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    {
                        DeferRemaining(items, results, ex.Message);
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

        private static List<S7MultiReadItem> MergeRanges(List<S7MultiReadItem> items, S7Client client)
        {
            items.Sort((left, right) =>
            {
                int order = left.Address.Area.CompareTo(right.Address.Area);
                if (order == 0) order = left.Address.DbNumber.CompareTo(right.Address.DbNumber);
                if (order == 0) order = left.Address.ByteOffset.CompareTo(right.Address.ByteOffset);
                return order;
            });
            var ranges = new List<S7MultiReadItem>();
            S7MultiReadItem last = null;
            foreach (var item in items)
            {
                long end = (long)item.Address.ByteOffset + item.ByteCount;
                if (last != null && last.Address.Area == item.Address.Area &&
                    last.Address.DbNumber == item.Address.DbNumber &&
                    item.Address.ByteOffset <= (long)last.Address.ByteOffset + last.ByteCount)
                {
                    long length = Math.Max(last.ByteCount, end - last.Address.ByteOffset);
                    // 14 byte response header, 4 byte item header, even alignment.
                    if (18L + length + length % 2 <= client.NegotiatedPduSize)
                    {
                        if (last.Members == null)
                        {
                            last = new S7MultiReadItem { Address = last.Address, Members = new List<S7MultiReadItem> { last } };
                            ranges[ranges.Count - 1] = last;
                        }
                        last.Members.Add(item);
                        last.ByteCount = (int)length;
                        continue;
                    }
                }
                last = item;
                ranges.Add(item);
            }
            return ranges;
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
            var batches = new Queue<List<S7MultiReadItem>>(BuildBatches(MergeRanges(items, client), client));
            int remainingDiagnosticBatches = 4;

            while (batches.TryDequeue(out var batch))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    DeferRemaining(items, results, "S7 read cancelled before dispatch.");
                    break;
                }
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
                    DecodeBatch(batch, client, results, batches, ref remainingDiagnosticBatches);
                }
                catch (Exception ex)
                {
                    PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Batch);
                    FailBatch(batch, results, ex.Message, scope);
                    if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    {
                        DeferRemaining(items, results, ex.Message);
                        break;
                    }
                }
            }

            return CompleteResults(requests, results);
        }

        private static void DecodeBatch(
            List<S7MultiReadItem> batch,
            S7Client client,
            PlcBatchReadResult[] results,
            Queue<List<S7MultiReadItem>> batches, ref int remainingDiagnosticBatches)
        {
            List<S7MultiReadItem> retry = null;
            foreach (S7MultiReadItem item in batch)
            {
                if (item.Members != null)
                {
                    if (!string.IsNullOrEmpty(item.ErrorMessage))
                    {
                        // A range crossing the end of a DB can contain both readable and invalid tags.
                        if (item.FailureScope == PlcReadFailureScope.Tag)
                            (retry ??= new List<S7MultiReadItem>()).AddRange(item.Members);
                        else FailBatch(item.Members, results, item.ErrorMessage, item.FailureScope);
                        continue;
                    }
                    foreach (var member in item.Members)
                    {
                        int offset = member.Address.ByteOffset - item.Address.ByteOffset;
                        DecodeItem(member, item.Data.AsSpan(offset, member.ByteCount).ToArray(), client, results);
                    }
                    continue;
                }
                if (!string.IsNullOrEmpty(item.ErrorMessage))
                {
                    results[item.Index] = PlcBatchReadResult.FromFailure(
                        item.Request,
                        item.ErrorMessage,
                        item.FailureScope);
                    continue;
                }

                DecodeItem(item, item.Data, client, results);
            }
            if (retry != null)
                foreach (var separate in BuildBatches(retry, client))
                {
                    if (remainingDiagnosticBatches > 0)
                    {
                        remainingDiagnosticBatches--;
                        batches.Enqueue(separate);
                    }
                    else DeferRemaining(separate, results, "S7 range awaits independent verification.");
                }
        }

        private static void DecodeItem(S7MultiReadItem item, byte[] data, S7Client client, PlcBatchReadResult[] results)
        {
            try
            {
                object value = S7DataCodec.Decode(item.Request.DataType, data, item.Address.BitOffset, item.Request.ElementCount);
                results[item.Index] = PlcBatchReadResult.FromSuccess(item.Request,
                    new PlcReadResult(0, client.GetBatchTypeName(item.Request.DataType), value));
            }
            catch (Exception ex)
            {
                results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, ex.Message, PlcReadFailureScope.Tag);
            }
        }

        private static void FailBatch(
            IEnumerable<S7MultiReadItem> batch,
            PlcBatchReadResult[] results,
            string message,
            PlcReadFailureScope scope)
        {
            foreach (S7MultiReadItem item in batch)
            {
                if (item.Members != null) FailBatch(item.Members, results, message, scope);
                else results[item.Index] = PlcBatchReadResult.FromFailure(item.Request, message, scope);
            }
        }

        private static void DeferRemaining(
            IEnumerable<S7MultiReadItem> items,
            PlcBatchReadResult[] results,
            string message)
        {
            foreach (S7MultiReadItem item in items)
            {
                if (results[item.Index] == null)
                    results[item.Index] = PlcBatchReadResult.FromDeferred(item.Request, message);
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
