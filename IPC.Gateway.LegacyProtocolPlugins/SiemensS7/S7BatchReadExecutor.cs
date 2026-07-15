using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    internal static class S7BatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests, S7BatchReadContext context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null || context.BuildAddress == null || context.ReadBytes == null || context.GetTypeName == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<S7BatchReadItem> items = BuildItems(requests, context, results);
            Dictionary<string, List<S7BatchReadItem>> groups = GroupItems(items);

            foreach (List<S7BatchReadItem> group in groups.Values)
            {
                group.Sort(CompareItems);
                ExecuteSegments(group, context, results);
            }

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "Batch read did not produce a result.", true));
            }

            return output;
        }

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            S7AsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null || context.BuildAddress == null || context.ReadBytesAsync == null || context.GetTypeName == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<S7BatchReadItem> items = BuildItems(requests, context, results);
            Dictionary<string, List<S7BatchReadItem>> groups = GroupItems(items);

            foreach (List<S7BatchReadItem> group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                group.Sort(CompareItems);
                await ExecuteSegmentsAsync(group, context, results, cancellationToken).ConfigureAwait(false);
            }

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "Batch read did not produce a result.", true));
            }

            return output;
        }

        private static List<S7BatchReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            S7BatchReadContext context,
            PlcBatchReadResult[] results)
        {
            List<S7BatchReadItem> items = new List<S7BatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    EnsureSupportedType(request.DataType);
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    items.Add(S7BatchReadItem.Create(i, request, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }

            return items;
        }

        private static List<S7BatchReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            S7AsyncBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            List<S7BatchReadItem> items = new List<S7BatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    EnsureSupportedType(request.DataType);
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    items.Add(S7BatchReadItem.Create(i, request, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }

            return items;
        }

        private static Dictionary<string, List<S7BatchReadItem>> GroupItems(List<S7BatchReadItem> items)
        {
            Dictionary<string, List<S7BatchReadItem>> groups = new Dictionary<string, List<S7BatchReadItem>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                S7BatchReadItem item = items[i];
                List<S7BatchReadItem> group;
                if (!groups.TryGetValue(item.GroupKey, out group))
                {
                    group = new List<S7BatchReadItem>();
                    groups[item.GroupKey] = group;
                }
                group.Add(item);
            }
            return groups;
        }

        private static void ExecuteSegments(List<S7BatchReadItem> items, S7BatchReadContext context, PlcBatchReadResult[] results)
        {
            int index = 0;
            while (index < items.Count)
            {
                int segmentStart = items[index].StartByte;
                int segmentEnd = items[index].EndByte;
                int segmentStartIndex = index;
                int maxBytes = context.MaxReadBytes > 0 ? context.MaxReadBytes : int.MaxValue;

                index++;
                while (index < items.Count)
                {
                    S7BatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndByte);
                    bool contiguousOrOverlapping = next.StartByte <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxBytes;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                ExecuteSegment(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results);
            }
        }

        private static async ValueTask ExecuteSegmentsAsync(
            List<S7BatchReadItem> items,
            S7AsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < items.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentStart = items[index].StartByte;
                int segmentEnd = items[index].EndByte;
                int segmentStartIndex = index;
                int maxBytes = context.MaxReadBytes > 0 ? context.MaxReadBytes : int.MaxValue;

                index++;
                while (index < items.Count)
                {
                    S7BatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndByte);
                    bool contiguousOrOverlapping = next.StartByte <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxBytes;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                await ExecuteSegmentAsync(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ExecuteSegment(
            List<S7BatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            S7BatchReadContext context,
            PlcBatchReadResult[] results)
        {
            S7BatchReadItem first = items[startIndex];
            int byteCount = segmentEnd - segmentStart + 1;
            S7Address segmentAddress = first.Address.AddByteOffset(segmentStart - first.StartByte);

            try
            {
                byte[] data = context.ReadBytes(segmentAddress, byteCount);
                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = DecodeResult(items[i], data, segmentStart, context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    throw;
                if (scope == PlcReadFailureScope.Tag && endIndex - startIndex > 1)
                {
                    RetrySegmentBySplitting(items, startIndex, endIndex, context, results);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, scope);
            }
        }

        private static void RetrySegmentBySplitting(
            List<S7BatchReadItem> items,
            int startIndex,
            int endIndex,
            S7BatchReadContext context,
            PlcBatchReadResult[] results)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            ExecuteSegment(items, startIndex, middle, items[startIndex].StartByte, GetSegmentEnd(items, startIndex, middle), context, results);
            ExecuteSegment(items, middle, endIndex, items[middle].StartByte, GetSegmentEnd(items, middle, endIndex), context, results);
        }

        private static async ValueTask ExecuteSegmentAsync(
            List<S7BatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            S7AsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            S7BatchReadItem first = items[startIndex];
            int byteCount = segmentEnd - segmentStart + 1;
            S7Address segmentAddress = first.Address.AddByteOffset(segmentStart - first.StartByte);

            try
            {
                byte[] data = await context.ReadBytesAsync(segmentAddress, byteCount, cancellationToken).ConfigureAwait(false);
                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = DecodeResult(items[i], data, segmentStart, context);
            }
            catch (Exception ex)
            {
                PlcReadFailureScope scope = PlcFailureClassifier.Classify(ex, PlcReadFailureScope.Tag);
                if (PlcBatchReadResult.IsConnectionFailureScope(scope))
                    throw;
                if (scope == PlcReadFailureScope.Tag && endIndex - startIndex > 1)
                {
                    await RetrySegmentBySplittingAsync(items, startIndex, endIndex, context, results, cancellationToken).ConfigureAwait(false);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, scope);
            }
        }

        private static async ValueTask RetrySegmentBySplittingAsync(
            List<S7BatchReadItem> items,
            int startIndex,
            int endIndex,
            S7AsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            await ExecuteSegmentAsync(items, startIndex, middle, items[startIndex].StartByte, GetSegmentEnd(items, startIndex, middle), context, results, cancellationToken).ConfigureAwait(false);
            await ExecuteSegmentAsync(items, middle, endIndex, items[middle].StartByte, GetSegmentEnd(items, middle, endIndex), context, results, cancellationToken).ConfigureAwait(false);
        }

        private static int GetSegmentEnd(List<S7BatchReadItem> items, int startIndex, int endIndex)
        {
            int segmentEnd = items[startIndex].EndByte;
            for (int i = startIndex + 1; i < endIndex; i++)
                segmentEnd = Math.Max(segmentEnd, items[i].EndByte);
            return segmentEnd;
        }

        private static PlcBatchReadResult DecodeResult(S7BatchReadItem item, byte[] data, int segmentStart, S7BatchReadContext context)
        {
            try
            {
                int offset = item.StartByte - segmentStart;
                byte[] itemData = new byte[item.ByteCount];
                Buffer.BlockCopy(data, offset, itemData, 0, itemData.Length);
                object value = S7DataCodec.Decode(item.Request.DataType, itemData, item.BitOffset, item.ValueCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeResult(S7BatchReadItem item, byte[] data, int segmentStart, S7AsyncBatchReadContext context)
        {
            try
            {
                int offset = item.StartByte - segmentStart;
                byte[] itemData = new byte[item.ByteCount];
                Buffer.BlockCopy(data, offset, itemData, 0, itemData.Length);
                object value = S7DataCodec.Decode(item.Request.DataType, itemData, item.BitOffset, item.ValueCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
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
                if (text.IndexOf("timeout", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("timed out", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("socket", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("closed", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("not connected", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("unreachable", StringComparison.Ordinal) >= 0)
                    return true;

                current = current.InnerException;
            }

            return false;
        }

        private static int CompareItems(S7BatchReadItem left, S7BatchReadItem right)
        {
            int result = left.StartByte.CompareTo(right.StartByte);
            if (result != 0)
                return result;
            return left.EndByte.CompareTo(right.EndByte);
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
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
