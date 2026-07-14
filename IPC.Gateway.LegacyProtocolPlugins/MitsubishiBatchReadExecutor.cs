using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.MitsubishiMc;

namespace IPC.Gateway.LegacyProtocolPlugins.Mitsubishi
{
    internal sealed class MitsubishiBatchReadContext<TAddress>
    {
        public Func<string, PlcDataType, int, TAddress> BuildAddress { get; set; }
        public Func<TAddress, string> GetAreaKey { get; set; }
        public Func<TAddress, int> GetDeviceNumber { get; set; }
        public Func<TAddress, int> GetBitOffset { get; set; }
        public Func<TAddress, bool> IsBitDevice { get; set; }
        public Func<TAddress, int, TAddress> AddDeviceOffset { get; set; }
        public Func<TAddress, int, byte[]> ReadWords { get; set; }
        public Func<TAddress, int, bool[]> ReadBits { get; set; }
        public Func<PlcDataType, string> GetTypeName { get; set; }
        public int MaxWordPoints { get; set; }
        public int MaxBitPoints { get; set; }
    }

    internal sealed class MitsubishiAsyncBatchReadContext<TAddress>
    {
        public Func<string, PlcDataType, int, TAddress> BuildAddress { get; set; }
        public Func<TAddress, string> GetAreaKey { get; set; }
        public Func<TAddress, int> GetDeviceNumber { get; set; }
        public Func<TAddress, int> GetBitOffset { get; set; }
        public Func<TAddress, bool> IsBitDevice { get; set; }
        public Func<TAddress, int, TAddress> AddDeviceOffset { get; set; }
        public Func<TAddress, int, CancellationToken, ValueTask<byte[]>> ReadWordsAsync { get; set; }
        public Func<TAddress, int, CancellationToken, ValueTask<bool[]>> ReadBitsAsync { get; set; }
        public Func<PlcDataType, string> GetTypeName { get; set; }
        public int MaxWordPoints { get; set; }
        public int MaxBitPoints { get; set; }
    }

    internal static class MitsubishiBatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MitsubishiBatchReadContext<TAddress> context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<BatchReadItem<TAddress>> items = BuildItems(requests, context, results);
            Dictionary<string, List<BatchReadItem<TAddress>>> groups = GroupItems(items);

            foreach (List<BatchReadItem<TAddress>> group in groups.Values)
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

        public static async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MitsubishiAsyncBatchReadContext<TAddress> context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<BatchReadItem<TAddress>> items = BuildItems(requests, context, results);
            Dictionary<string, List<BatchReadItem<TAddress>>> groups = GroupItems(items);

            foreach (List<BatchReadItem<TAddress>> group in groups.Values)
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

        private static List<BatchReadItem<TAddress>> BuildItems<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MitsubishiBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results)
        {
            List<BatchReadItem<TAddress>> items = new List<BatchReadItem<TAddress>>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    if (request.ElementCount <= 0)
                        throw new ArgumentOutOfRangeException("ElementCount");
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    TAddress address = context.BuildAddress(request.Address, request.DataType, request.ElementOffset);
                    bool readBits = context.IsBitDevice(address) &&
                                    (request.DataType == PlcDataType.Bool || request.DataType == PlcDataType.BoolArray);
                    int points = GetPointCount(request, address, readBits, context);
                    items.Add(new BatchReadItem<TAddress>(i, request, address, readBits, points, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }

            return items;
        }

        private static List<BatchReadItem<TAddress>> BuildItems<TAddress>(
            IList<PlcBatchReadRequest> requests,
            MitsubishiAsyncBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results)
        {
            List<BatchReadItem<TAddress>> items = new List<BatchReadItem<TAddress>>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    if (request.ElementCount <= 0)
                        throw new ArgumentOutOfRangeException("ElementCount");
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    TAddress address = context.BuildAddress(request.Address, request.DataType, request.ElementOffset);
                    bool readBits = context.IsBitDevice(address) &&
                                    (request.DataType == PlcDataType.Bool || request.DataType == PlcDataType.BoolArray);
                    int points = GetPointCount(request, address, readBits, context);
                    items.Add(new BatchReadItem<TAddress>(i, request, address, readBits, points, context));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }

            return items;
        }

        private static Dictionary<string, List<BatchReadItem<TAddress>>> GroupItems<TAddress>(List<BatchReadItem<TAddress>> items)
        {
            Dictionary<string, List<BatchReadItem<TAddress>>> groups = new Dictionary<string, List<BatchReadItem<TAddress>>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                BatchReadItem<TAddress> item = items[i];
                string key = item.GroupKey;
                List<BatchReadItem<TAddress>> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<BatchReadItem<TAddress>>();
                    groups[key] = group;
                }
                group.Add(item);
            }
            return groups;
        }

        private static void ExecuteSegments<TAddress>(
            List<BatchReadItem<TAddress>> items,
            MitsubishiBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results)
        {
            int index = 0;
            while (index < items.Count)
            {
                int segmentStart = items[index].StartNumber;
                int segmentEnd = items[index].EndNumber;
                int segmentStartIndex = index;
                int maxPoints = items[index].ReadBits ? Math.Max(1, context.MaxBitPoints) : Math.Max(1, context.MaxWordPoints);

                index++;
                while (index < items.Count)
                {
                    BatchReadItem<TAddress> next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndNumber);
                    bool contiguousOrOverlapping = next.StartNumber <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                ExecuteSegment(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results);
            }
        }

        private static async ValueTask ExecuteSegmentsAsync<TAddress>(
            List<BatchReadItem<TAddress>> items,
            MitsubishiAsyncBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < items.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentStart = items[index].StartNumber;
                int segmentEnd = items[index].EndNumber;
                int segmentStartIndex = index;
                int maxPoints = items[index].ReadBits ? Math.Max(1, context.MaxBitPoints) : Math.Max(1, context.MaxWordPoints);

                index++;
                while (index < items.Count)
                {
                    BatchReadItem<TAddress> next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndNumber);
                    bool contiguousOrOverlapping = next.StartNumber <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                await ExecuteSegmentAsync(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results, cancellationToken).ConfigureAwait(false);
            }
        }

        private static void ExecuteSegment<TAddress>(
            List<BatchReadItem<TAddress>> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            MitsubishiBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results)
        {
            BatchReadItem<TAddress> first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;
            TAddress startAddress = context.AddDeviceOffset(first.Address, segmentStart - first.StartNumber);

            try
            {
                if (first.ReadBits)
                {
                    bool[] values = context.ReadBits(startAddress, pointCount);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], values, segmentStart, context);
                }
                else
                {
                    byte[] data = context.ReadWords(startAddress, pointCount);
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

        private static void RetrySegmentBySplitting<TAddress>(
            List<BatchReadItem<TAddress>> items,
            int startIndex,
            int endIndex,
            MitsubishiBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            ExecuteSegment(items, startIndex, middle, items[startIndex].StartNumber, GetSegmentEnd(items, startIndex, middle), context, results);
            ExecuteSegment(items, middle, endIndex, items[middle].StartNumber, GetSegmentEnd(items, middle, endIndex), context, results);
        }

        private static async ValueTask ExecuteSegmentAsync<TAddress>(
            List<BatchReadItem<TAddress>> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            MitsubishiAsyncBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            BatchReadItem<TAddress> first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;
            TAddress startAddress = context.AddDeviceOffset(first.Address, segmentStart - first.StartNumber);

            try
            {
                if (first.ReadBits)
                {
                    bool[] values = await context.ReadBitsAsync(startAddress, pointCount, cancellationToken).ConfigureAwait(false);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], values, segmentStart, context);
                }
                else
                {
                    byte[] data = await context.ReadWordsAsync(startAddress, pointCount, cancellationToken).ConfigureAwait(false);
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

        private static async ValueTask RetrySegmentBySplittingAsync<TAddress>(
            List<BatchReadItem<TAddress>> items,
            int startIndex,
            int endIndex,
            MitsubishiAsyncBatchReadContext<TAddress> context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            await ExecuteSegmentAsync(items, startIndex, middle, items[startIndex].StartNumber, GetSegmentEnd(items, startIndex, middle), context, results, cancellationToken).ConfigureAwait(false);
            await ExecuteSegmentAsync(items, middle, endIndex, items[middle].StartNumber, GetSegmentEnd(items, middle, endIndex), context, results, cancellationToken).ConfigureAwait(false);
        }

        private static int GetSegmentEnd<TAddress>(List<BatchReadItem<TAddress>> items, int startIndex, int endIndex)
        {
            int segmentEnd = items[startIndex].EndNumber;
            for (int i = startIndex + 1; i < endIndex; i++)
                segmentEnd = Math.Max(segmentEnd, items[i].EndNumber);
            return segmentEnd;
        }

        private static PlcBatchReadResult DecodeBitResult<TAddress>(
            BatchReadItem<TAddress> item,
            bool[] values,
            int segmentStart,
            MitsubishiBatchReadContext<TAddress> context)
        {
            try
            {
                int offset = item.StartNumber - segmentStart;
                if (item.Request.DataType == PlcDataType.Bool)
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), values[offset]));

                bool[] result = new bool[item.Request.ElementCount];
                Array.Copy(values, offset, result, 0, result.Length);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), result));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeBitResult<TAddress>(
            BatchReadItem<TAddress> item,
            bool[] values,
            int segmentStart,
            MitsubishiAsyncBatchReadContext<TAddress> context)
        {
            try
            {
                int offset = item.StartNumber - segmentStart;
                if (item.Request.DataType == PlcDataType.Bool)
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), values[offset]));

                bool[] result = new bool[item.Request.ElementCount];
                Array.Copy(values, offset, result, 0, result.Length);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), result));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeWordResult<TAddress>(
            BatchReadItem<TAddress> item,
            byte[] data,
            int segmentStart,
            MitsubishiBatchReadContext<TAddress> context)
        {
            try
            {
                int byteOffset = (item.StartNumber - segmentStart) * 2;
                if (item.Request.DataType == PlcDataType.Bool)
                {
                    bool value = (BitConverter.ToUInt16(data, byteOffset) & (1 << item.BitOffset)) != 0;
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), value));
                }

                if (item.Request.DataType == PlcDataType.BoolArray)
                {
                    bool[] values = DecodeWordBits(data, byteOffset, item.BitOffset, item.Request.ElementCount);
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), values));
                }

                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);
                object decodedValue = McDataCodec.Decode(item.Request.DataType, itemData, item.Request.ElementCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), decodedValue));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeWordResult<TAddress>(
            BatchReadItem<TAddress> item,
            byte[] data,
            int segmentStart,
            MitsubishiAsyncBatchReadContext<TAddress> context)
        {
            try
            {
                int byteOffset = (item.StartNumber - segmentStart) * 2;
                if (item.Request.DataType == PlcDataType.Bool)
                {
                    bool value = (BitConverter.ToUInt16(data, byteOffset) & (1 << item.BitOffset)) != 0;
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), value));
                }

                if (item.Request.DataType == PlcDataType.BoolArray)
                {
                    bool[] values = DecodeWordBits(data, byteOffset, item.BitOffset, item.Request.ElementCount);
                    return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), values));
                }

                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);
                object decodedValue = McDataCodec.Decode(item.Request.DataType, itemData, item.Request.ElementCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(0, context.GetTypeName(item.Request.DataType), decodedValue));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static bool[] DecodeWordBits(byte[] data, int byteOffset, int bitOffset, int count)
        {
            bool[] values = new bool[count];
            for (int i = 0; i < count; i++)
            {
                int absoluteBit = bitOffset + i;
                int wordIndex = absoluteBit / 16;
                int bitIndex = absoluteBit % 16;
                ushort word = BitConverter.ToUInt16(data, byteOffset + wordIndex * 2);
                values[i] = (word & (1 << bitIndex)) != 0;
            }
            return values;
        }

        private static int GetPointCount<TAddress>(
            PlcBatchReadRequest request,
            TAddress address,
            bool readBits,
            MitsubishiBatchReadContext<TAddress> context)
        {
            if (request.DataType == PlcDataType.Bool)
                return 1;

            if (request.DataType == PlcDataType.BoolArray)
            {
                if (readBits)
                    return request.ElementCount;
                return (context.GetBitOffset(address) + request.ElementCount + 15) / 16;
            }

            return McDataCodec.GetWordCount(request.DataType, request.ElementCount);
        }

        private static int GetPointCount<TAddress>(
            PlcBatchReadRequest request,
            TAddress address,
            bool readBits,
            MitsubishiAsyncBatchReadContext<TAddress> context)
        {
            if (request.DataType == PlcDataType.Bool)
                return 1;

            if (request.DataType == PlcDataType.BoolArray)
            {
                if (readBits)
                    return request.ElementCount;
                return (context.GetBitOffset(address) + request.ElementCount + 15) / 16;
            }

            return McDataCodec.GetWordCount(request.DataType, request.ElementCount);
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

        private static int CompareItems<TAddress>(BatchReadItem<TAddress> left, BatchReadItem<TAddress> right)
        {
            int result = left.StartNumber.CompareTo(right.StartNumber);
            if (result != 0)
                return result;
            return left.EndNumber.CompareTo(right.EndNumber);
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private sealed class BatchReadItem<TAddress>
        {
            public BatchReadItem(
                int index,
                PlcBatchReadRequest request,
                TAddress address,
                bool readBits,
                int pointCount,
                MitsubishiBatchReadContext<TAddress> context)
            {
                Index = index;
                Request = request;
                Address = address;
                ReadBits = readBits;
                PointCount = pointCount;
                StartNumber = context.GetDeviceNumber(address);
                BitOffset = context.GetBitOffset(address);
                EndNumber = StartNumber + Math.Max(1, pointCount) - 1;
                GroupKey = context.GetAreaKey(address) + "|" + (readBits ? "bit" : "word");
            }

            public BatchReadItem(
                int index,
                PlcBatchReadRequest request,
                TAddress address,
                bool readBits,
                int pointCount,
                MitsubishiAsyncBatchReadContext<TAddress> context)
            {
                Index = index;
                Request = request;
                Address = address;
                ReadBits = readBits;
                PointCount = pointCount;
                StartNumber = context.GetDeviceNumber(address);
                BitOffset = context.GetBitOffset(address);
                EndNumber = StartNumber + Math.Max(1, pointCount) - 1;
                GroupKey = context.GetAreaKey(address) + "|" + (readBits ? "bit" : "word");
            }

            public int Index { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
            public TAddress Address { get; private set; }
            public bool ReadBits { get; private set; }
            public int PointCount { get; private set; }
            public int StartNumber { get; private set; }
            public int EndNumber { get; private set; }
            public int BitOffset { get; private set; }
            public string GroupKey { get; private set; }
        }
    }
}
