using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    internal static class FinsBatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests, FinsBatchReadContext context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null || context.ReadMemory == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<FinsBatchReadItem> items = BuildItems(requests, results);
            Dictionary<string, List<FinsBatchReadItem>> groups = GroupItems(items);

            foreach (List<FinsBatchReadItem> group in groups.Values)
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
            FinsAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;
            if (context == null || context.ReadMemoryAsync == null)
                throw new ArgumentNullException("context");

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<FinsBatchReadItem> items = BuildItems(requests, results);
            Dictionary<string, List<FinsBatchReadItem>> groups = GroupItems(items);

            foreach (List<FinsBatchReadItem> group in groups.Values)
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

        private static List<FinsBatchReadItem> BuildItems(IList<PlcBatchReadRequest> requests, PlcBatchReadResult[] results)
        {
            List<FinsBatchReadItem> items = new List<FinsBatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    EnsureSupportedType(request.DataType);
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    items.Add(FinsBatchReadItem.Create(i, request));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }

            return items;
        }

        private static Dictionary<string, List<FinsBatchReadItem>> GroupItems(List<FinsBatchReadItem> items)
        {
            Dictionary<string, List<FinsBatchReadItem>> groups = new Dictionary<string, List<FinsBatchReadItem>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                FinsBatchReadItem item = items[i];
                string key = item.Area.Name + "|" + item.AreaCode.ToString("X2") + "|" + item.Kind;
                List<FinsBatchReadItem> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<FinsBatchReadItem>();
                    groups[key] = group;
                }
                group.Add(item);
            }
            return groups;
        }

        private static void ExecuteSegments(List<FinsBatchReadItem> items, FinsBatchReadContext context, PlcBatchReadResult[] results)
        {
            int index = 0;
            while (index < items.Count)
            {
                int segmentStart = items[index].StartPoint;
                int segmentEnd = items[index].EndPoint;
                int segmentStartIndex = index;
                int maxPoints = items[index].Kind == FinsBatchReadKind.Bit
                    ? Math.Max(1, context.MaxBitCount)
                    : Math.Max(1, context.MaxWordCount);

                index++;
                while (index < items.Count)
                {
                    FinsBatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndPoint);
                    bool contiguousOrOverlapping = next.StartPoint <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                ExecuteSegment(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results);
            }
        }

        private static async ValueTask ExecuteSegmentsAsync(
            List<FinsBatchReadItem> items,
            FinsAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < items.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentStart = items[index].StartPoint;
                int segmentEnd = items[index].EndPoint;
                int segmentStartIndex = index;
                int maxPoints = items[index].Kind == FinsBatchReadKind.Bit
                    ? Math.Max(1, context.MaxBitCount)
                    : Math.Max(1, context.MaxWordCount);

                index++;
                while (index < items.Count)
                {
                    FinsBatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndPoint);
                    bool contiguousOrOverlapping = next.StartPoint <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                await ExecuteSegmentAsync(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ExecuteSegment(
            List<FinsBatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            FinsBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            FinsBatchReadItem first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;

            try
            {
                if (first.Kind == FinsBatchReadKind.Bit)
                {
                    int limit = Math.Max(1, context.MaxBitCount);
                    byte[] bitBytes = context.ReadMemory(first.Area.BitCode, segmentStart / 16, segmentStart % 16, pointCount, limit);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], bitBytes, segmentStart);
                }
                else
                {
                    int limit = Math.Max(1, context.MaxWordCount);
                    byte[] data = context.ReadMemory(first.Area.WordCode, segmentStart, 0, pointCount, limit);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeWordResult(items[i], data, segmentStart, context);
                }
            }
            catch (Exception ex)
            {
                bool communicationError = IsCommunicationException(ex);
                if (communicationError)
                    throw;
                if (!communicationError && endIndex - startIndex > 1)
                {
                    RetrySegmentBySplitting(items, startIndex, endIndex, context, results);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, communicationError);
            }
        }

        private static void RetrySegmentBySplitting(
            List<FinsBatchReadItem> items,
            int startIndex,
            int endIndex,
            FinsBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            ExecuteSegment(items, startIndex, middle, items[startIndex].StartPoint, GetSegmentEnd(items, startIndex, middle), context, results);
            ExecuteSegment(items, middle, endIndex, items[middle].StartPoint, GetSegmentEnd(items, middle, endIndex), context, results);
        }

        private static async ValueTask ExecuteSegmentAsync(
            List<FinsBatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            FinsAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            FinsBatchReadItem first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;

            try
            {
                if (first.Kind == FinsBatchReadKind.Bit)
                {
                    int limit = Math.Max(1, context.MaxBitCount);
                    byte[] bitBytes = await context.ReadMemoryAsync(first.Area.BitCode, segmentStart / 16, segmentStart % 16, pointCount, limit, cancellationToken).ConfigureAwait(false);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], bitBytes, segmentStart);
                }
                else
                {
                    int limit = Math.Max(1, context.MaxWordCount);
                    byte[] data = await context.ReadMemoryAsync(first.Area.WordCode, segmentStart, 0, pointCount, limit, cancellationToken).ConfigureAwait(false);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeWordResult(items[i], data, segmentStart, context);
                }
            }
            catch (Exception ex)
            {
                bool communicationError = IsCommunicationException(ex);
                if (communicationError)
                    throw;
                if (!communicationError && endIndex - startIndex > 1)
                {
                    await RetrySegmentBySplittingAsync(items, startIndex, endIndex, context, results, cancellationToken).ConfigureAwait(false);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, communicationError);
            }
        }

        private static async ValueTask RetrySegmentBySplittingAsync(
            List<FinsBatchReadItem> items,
            int startIndex,
            int endIndex,
            FinsAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            await ExecuteSegmentAsync(items, startIndex, middle, items[startIndex].StartPoint, GetSegmentEnd(items, startIndex, middle), context, results, cancellationToken).ConfigureAwait(false);
            await ExecuteSegmentAsync(items, middle, endIndex, items[middle].StartPoint, GetSegmentEnd(items, middle, endIndex), context, results, cancellationToken).ConfigureAwait(false);
        }

        private static int GetSegmentEnd(List<FinsBatchReadItem> items, int startIndex, int endIndex)
        {
            int segmentEnd = items[startIndex].EndPoint;
            for (int i = startIndex + 1; i < endIndex; i++)
                segmentEnd = Math.Max(segmentEnd, items[i].EndPoint);
            return segmentEnd;
        }

        private static PlcBatchReadResult DecodeBitResult(FinsBatchReadItem item, byte[] data, int segmentStart)
        {
            try
            {
                int offset = item.StartPoint - segmentStart;
                byte[] itemData = new byte[item.ValueCount];
                Buffer.BlockCopy(data, offset, itemData, 0, itemData.Length);
                object value = FinsDataCodec.DecodeBits(item.Request.DataType, itemData, item.ValueCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(item.Area.BitCode, item.Area.Name + ".BIT", value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeWordResult(FinsBatchReadItem item, byte[] data, int segmentStart, FinsBatchReadContext context)
        {
            try
            {
                int byteOffset = (item.StartPoint - segmentStart) * 2;
                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);
                object value = FinsDataCodec.DecodeWords(item.Request.DataType, itemData, item.ValueCount, context.WordOrder);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(item.Area.WordCode, item.Area.Name + ".WORD", value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeWordResult(FinsBatchReadItem item, byte[] data, int segmentStart, FinsAsyncBatchReadContext context)
        {
            try
            {
                int byteOffset = (item.StartPoint - segmentStart) * 2;
                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);
                object value = FinsDataCodec.DecodeWords(item.Request.DataType, itemData, item.ValueCount, context.WordOrder);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(item.Area.WordCode, item.Area.Name + ".WORD", value));
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

        private static int CompareItems(FinsBatchReadItem left, FinsBatchReadItem right)
        {
            int result = left.StartPoint.CompareTo(right.StartPoint);
            if (result != 0)
                return result;
            return left.EndPoint.CompareTo(right.EndPoint);
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
                throw new NotSupportedException("FINS does not support Modbus Coil/Discrete Input data types. Use BOOL or BOOL[] instead.");
        }

    }
}
