using System;
using System.Collections.Generic;
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
            var recovery = PlcBatchReadRecovery.Start(context.BuildAddress.Target, "S7:" + context.MaxReadBytes);

            try
            {
                foreach (List<S7BatchReadItem> group in groups.Values)
                {
                    group.Sort(CompareItems);
                    ExecuteSegments(group, context, results, recovery);
                }
            }
            catch (Exception ex)
            {
                PlcBatchReadResult.DeferPending(requests, results, ex.Message);
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
            var recovery = PlcBatchReadRecovery.Start(context.BuildAddress.Target, "S7:" + context.MaxReadBytes);

            try
            {
                foreach (List<S7BatchReadItem> group in groups.Values)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    group.Sort(CompareItems);
                    await ExecuteSegmentsAsync(group, context, results, recovery, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                PlcBatchReadResult.DeferPending(requests, results, ex.Message);
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

        private static void ExecuteSegments(List<S7BatchReadItem> items, S7BatchReadContext context, PlcBatchReadResult[] results, PlcBatchReadRecovery recovery)
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

                recovery.ReadRange(items, segmentStartIndex, index, item => item.Request, item => item.Index, (start, end) =>
                {
                    var first = items[start];
                    int length = GetSegmentEnd(items, start, end) - first.StartByte + 1;
                    byte[] data = context.ReadBytes(first.Address, length);
                    for (int i = start; i < end; i++)
                        results[items[i].Index] = DecodeResult(items[i], data, first.StartByte, context);
                }, results, items[segmentStartIndex].GroupKey);
            }
        }

        private static async ValueTask ExecuteSegmentsAsync(
            List<S7BatchReadItem> items,
            S7AsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            PlcBatchReadRecovery recovery,
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

                await recovery.ReadRangeAsync(items, segmentStartIndex, index, item => item.Request, item => item.Index, async (start, end) =>
                {
                    var first = items[start];
                    int length = GetSegmentEnd(items, start, end) - first.StartByte + 1;
                    byte[] data = await context.ReadBytesAsync(first.Address, length, cancellationToken).ConfigureAwait(false);
                    for (int i = start; i < end; i++)
                        results[items[i].Index] = DecodeResult(items[i], data, first.StartByte, context);
                }, results, items[segmentStartIndex].GroupKey, cancellationToken).ConfigureAwait(false);
            }
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
