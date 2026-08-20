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
            List<FinsBatchReadItem> items = BuildItems(requests, results, context.DriverOptions);
            Dictionary<string, List<FinsBatchReadItem>> groups = GroupItems(items);

            foreach (List<FinsBatchReadItem> group in groups.Values)
            {
                group.Sort(CompareItems);
                if (TryExecuteSparseGroup(group, context, results))
                    continue;
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
            List<FinsBatchReadItem> items = BuildItems(requests, results, context.DriverOptions);
            Dictionary<string, List<FinsBatchReadItem>> groups = GroupItems(items);

            foreach (List<FinsBatchReadItem> group in groups.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                group.Sort(CompareItems);
                if (await TryExecuteSparseGroupAsync(group, context, results, cancellationToken).ConfigureAwait(false))
                    continue;
                await ExecuteSegmentsAsync(group, context, results, cancellationToken).ConfigureAwait(false);
            }

            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                output.Add(results[i] ?? PlcBatchReadResult.FromFailure(request, "Batch read did not produce a result.", true));
            }

            return output;
        }

        private static List<FinsBatchReadItem> BuildItems(
            IList<PlcBatchReadRequest> requests,
            PlcBatchReadResult[] results,
            FinsDriverOptions driverOptions)
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

                    items.Add(FinsBatchReadItem.Create(i, request, driverOptions));
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

        private static bool TryExecuteSparseGroup(
            List<FinsBatchReadItem> items,
            FinsBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            if (!ShouldUseSparseRead(items, context.MaxSparseItems) || context.ReadMultipleMemory == null)
                return false;

            List<FinsMemoryPoint> points = BuildSparsePoints(items);
            try
            {
                byte[] data = context.ReadMultipleMemory(points);
                DecodeSparseResults(items, data, context.WordOrder, results);
                return true;
            }
            catch (FinsProtocolException ex) when (ex.Scope == FinsErrorScope.Tag)
            {
                return false;
            }
        }

        private static async ValueTask<bool> TryExecuteSparseGroupAsync(
            List<FinsBatchReadItem> items,
            FinsAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            if (!ShouldUseSparseRead(items, context.MaxSparseItems) || context.ReadMultipleMemoryAsync == null)
                return false;

            List<FinsMemoryPoint> points = BuildSparsePoints(items);
            try
            {
                byte[] data = await context.ReadMultipleMemoryAsync(points, cancellationToken).ConfigureAwait(false);
                DecodeSparseResults(items, data, context.WordOrder, results);
                return true;
            }
            catch (FinsProtocolException ex) when (ex.Scope == FinsErrorScope.Tag)
            {
                return false;
            }
        }

        private static bool ShouldUseSparseRead(List<FinsBatchReadItem> items, int maxSparseItems)
        {
            if (items.Count < 2 || maxSparseItems <= 0)
                return false;

            int pointCount = 0;
            for (int i = 0; i < items.Count; i++)
                pointCount = checked(pointCount + items[i].PointCount);
            if (pointCount > Math.Min(167, maxSparseItems))
                return false;

            int span = items[items.Count - 1].EndPoint - items[0].StartPoint + 1;
            return span > pointCount + 4;
        }

        private static List<FinsMemoryPoint> BuildSparsePoints(List<FinsBatchReadItem> items)
        {
            List<FinsMemoryPoint> points = new List<FinsMemoryPoint>();
            for (int i = 0; i < items.Count; i++)
            {
                FinsBatchReadItem item = items[i];
                for (int pointIndex = 0; pointIndex < item.PointCount; pointIndex++)
                {
                    if (item.Kind == FinsBatchReadKind.Bit)
                    {
                        if (item.Area.BitAddressUsesWordIndex)
                        {
                            points.Add(new FinsMemoryPoint(item.AreaCode, item.StartPoint + pointIndex, 0, true));
                        }
                        else
                        {
                            int absoluteBit = item.StartPoint + pointIndex;
                            points.Add(new FinsMemoryPoint(item.AreaCode, absoluteBit / 16, absoluteBit % 16, true));
                        }
                    }
                    else
                    {
                        points.Add(new FinsMemoryPoint(item.AreaCode, item.StartPoint + pointIndex, 0, false));
                    }
                }
            }
            return points;
        }

        private static void DecodeSparseResults(
            List<FinsBatchReadItem> items,
            byte[] data,
            PlcWordOrder wordOrder,
            PlcBatchReadResult[] results)
        {
            int offset = 0;
            for (int i = 0; i < items.Count; i++)
            {
                FinsBatchReadItem item = items[i];
                int length = item.Kind == FinsBatchReadKind.Bit ? item.PointCount : item.PointCount * 2;
                if (data == null || data.Length < offset + length)
                    throw new IOException("FINS sparse batch response is too short.");

                byte[] itemData = new byte[length];
                Buffer.BlockCopy(data, offset, itemData, 0, length);
                object value = item.Kind == FinsBatchReadKind.Bit
                    ? FinsDataCodec.DecodeBits(item.Request.DataType, itemData, item.ValueCount)
                    : FinsDataCodec.DecodeWords(item.Request.DataType, itemData, item.ValueCount, wordOrder);
                string areaName = item.Area.Name + (item.Kind == FinsBatchReadKind.Bit ? ".BIT" : ".WORD");
                results[item.Index] = PlcBatchReadResult.FromSuccess(
                    item.Request,
                    new PlcReadResult(item.AreaCode, areaName, value));
                offset += length;
            }
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
                int gapAllowance = GetGapAllowance(items[index], context.MaxGapWords);

                index++;
                while (index < items.Count)
                {
                    FinsBatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndPoint);
                    bool contiguousOrOverlapping = next.StartPoint <= segmentEnd + 1 + gapAllowance;
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
                int gapAllowance = GetGapAllowance(items[index], context.MaxGapWords);

                index++;
                while (index < items.Count)
                {
                    FinsBatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndPoint);
                    bool contiguousOrOverlapping = next.StartPoint <= segmentEnd + 1 + gapAllowance;
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
                    int wordAddress = first.Area.BitAddressUsesWordIndex ? segmentStart : segmentStart / 16;
                    int bitIndex = first.Area.BitAddressUsesWordIndex ? 0 : segmentStart % 16;
                    byte[] bitBytes = context.ReadMemory(first.Area.BitCode, wordAddress, bitIndex, pointCount, limit);
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
                if (communicationError || IsTerminalBatchException(ex))
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
                    int wordAddress = first.Area.BitAddressUsesWordIndex ? segmentStart : segmentStart / 16;
                    int bitIndex = first.Area.BitAddressUsesWordIndex ? 0 : segmentStart % 16;
                    byte[] bitBytes = await context.ReadMemoryAsync(first.Area.BitCode, wordAddress, bitIndex, pointCount, limit, cancellationToken).ConfigureAwait(false);
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
                if (communicationError || IsTerminalBatchException(ex))
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

        private static int GetGapAllowance(FinsBatchReadItem item, int maxGapWords)
        {
            int gap = Math.Max(0, maxGapWords);
            return item.Kind == FinsBatchReadKind.Bit && !item.Area.BitAddressUsesWordIndex
                ? gap * 16
                : gap;
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

        private static bool IsTerminalBatchException(Exception exception)
        {
            Exception current = exception;
            while (current != null)
            {
                if (current is FinsProtocolException finsException &&
                    finsException.Scope != FinsErrorScope.Tag)
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
