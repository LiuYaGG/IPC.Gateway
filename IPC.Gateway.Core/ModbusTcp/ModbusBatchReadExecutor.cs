#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.ModbusTcp
{
    internal sealed class ModbusBatchReadContext
    {
        public Func<ModbusArea, int, int, bool[]> ReadBits { get; set; }
        public Func<ModbusArea, int, int, byte[]> ReadRegisters { get; set; }
        public Func<ModbusArea, ushort> GetTypeCode { get; set; }
        public Func<ModbusArea, string> GetTypeName { get; set; }
        public int MaxReadBits { get; set; }
        public int MaxReadRegisters { get; set; }
    }

    internal sealed class ModbusAsyncBatchReadContext
    {
        public Func<ModbusArea, int, int, CancellationToken, ValueTask<bool[]>> ReadBitsAsync { get; set; }
        public Func<ModbusArea, int, int, CancellationToken, ValueTask<byte[]>> ReadRegistersAsync { get; set; }
        public Func<ModbusArea, ushort> GetTypeCode { get; set; }
        public Func<ModbusArea, string> GetTypeName { get; set; }
        public int MaxReadBits { get; set; }
        public int MaxReadRegisters { get; set; }
    }

    internal static class ModbusBatchReadExecutor
    {
        public static IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests, ModbusBatchReadContext context)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<BatchReadItem> items = BuildItems(requests, results);
            Dictionary<string, List<BatchReadItem>> groups = GroupItems(items);

            foreach (List<BatchReadItem> group in groups.Values)
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
            ModbusAsyncBatchReadContext context,
            CancellationToken cancellationToken)
        {
            List<PlcBatchReadResult> output = new List<PlcBatchReadResult>();
            if (requests == null || requests.Count == 0)
                return output;

            cancellationToken.ThrowIfCancellationRequested();

            PlcBatchReadResult[] results = new PlcBatchReadResult[requests.Count];
            List<BatchReadItem> items = BuildItems(requests, results);
            Dictionary<string, List<BatchReadItem>> groups = GroupItems(items);

            foreach (List<BatchReadItem> group in groups.Values)
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

        private static List<BatchReadItem> BuildItems(IList<PlcBatchReadRequest> requests, PlcBatchReadResult[] results)
        {
            List<BatchReadItem> items = new List<BatchReadItem>();
            for (int i = 0; i < requests.Count; i++)
            {
                PlcBatchReadRequest request = EnsureRequest(requests[i]);
                try
                {
                    if (request.ElementCount <= 0)
                        throw new ArgumentOutOfRangeException("ElementCount");
                    if (request.ElementOffset < 0)
                        throw new ArgumentOutOfRangeException("ElementOffset");

                    items.Add(BatchReadItem.Create(i, request));
                }
                catch (Exception ex)
                {
                    results[i] = PlcBatchReadResult.FromFailure(request, ex.Message, false);
                }
            }
            return items;
        }

        private static Dictionary<string, List<BatchReadItem>> GroupItems(List<BatchReadItem> items)
        {
            Dictionary<string, List<BatchReadItem>> groups = new Dictionary<string, List<BatchReadItem>>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                BatchReadItem item = items[i];
                string key = item.Area + "|" + (item.Kind == ReadKind.Bit ? "bit" : "register");
                List<BatchReadItem> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<BatchReadItem>();
                    groups[key] = group;
                }
                group.Add(item);
            }
            return groups;
        }

        private static void ExecuteSegments(List<BatchReadItem> items, ModbusBatchReadContext context, PlcBatchReadResult[] results)
        {
            int index = 0;
            while (index < items.Count)
            {
                int segmentStart = items[index].StartAddress;
                int segmentEnd = items[index].EndAddress;
                int segmentStartIndex = index;
                int maxPoints = items[index].Kind == ReadKind.Bit
                    ? Math.Max(1, context.MaxReadBits)
                    : Math.Max(1, context.MaxReadRegisters);

                index++;
                while (index < items.Count)
                {
                    BatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndAddress);
                    bool contiguousOrOverlapping = next.StartAddress <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                ExecuteSegment(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results);
                if (HasConnectionFailure(results))
                    return;
            }
        }

        private static async ValueTask ExecuteSegmentsAsync(
            List<BatchReadItem> items,
            ModbusAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int index = 0;
            while (index < items.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int segmentStart = items[index].StartAddress;
                int segmentEnd = items[index].EndAddress;
                int segmentStartIndex = index;
                int maxPoints = items[index].Kind == ReadKind.Bit
                    ? Math.Max(1, context.MaxReadBits)
                    : Math.Max(1, context.MaxReadRegisters);

                index++;
                while (index < items.Count)
                {
                    BatchReadItem next = items[index];
                    int mergedEnd = Math.Max(segmentEnd, next.EndAddress);
                    bool contiguousOrOverlapping = next.StartAddress <= segmentEnd + 1;
                    bool withinLimit = mergedEnd - segmentStart + 1 <= maxPoints;
                    if (!contiguousOrOverlapping || !withinLimit)
                        break;

                    segmentEnd = mergedEnd;
                    index++;
                }

                await ExecuteSegmentAsync(items, segmentStartIndex, index, segmentStart, segmentEnd, context, results, cancellationToken).ConfigureAwait(false);
                if (HasConnectionFailure(results))
                    return;
            }
        }

        private static void ExecuteSegment(
            List<BatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            ModbusBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            BatchReadItem first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;

            try
            {
                if (first.Kind == ReadKind.Bit)
                {
                    bool[] values = context.ReadBits(first.Area, segmentStart, pointCount);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], values, segmentStart, context);
                }
                else
                {
                    byte[] data = context.ReadRegisters(first.Area, segmentStart, pointCount);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeRegisterResult(items[i], data, segmentStart, context);
                }
            }
            catch (Exception ex)
            {
                bool communicationError = IsCommunicationException(ex);
                if (!communicationError && endIndex - startIndex > 1)
                {
                    RetrySegmentBySplitting(items, startIndex, endIndex, context, results);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, communicationError);
            }
        }

        private static async ValueTask ExecuteSegmentAsync(
            List<BatchReadItem> items,
            int startIndex,
            int endIndex,
            int segmentStart,
            int segmentEnd,
            ModbusAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            BatchReadItem first = items[startIndex];
            int pointCount = segmentEnd - segmentStart + 1;

            try
            {
                if (first.Kind == ReadKind.Bit)
                {
                    bool[] values = await context.ReadBitsAsync(first.Area, segmentStart, pointCount, cancellationToken).ConfigureAwait(false);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeBitResult(items[i], values, segmentStart, context);
                }
                else
                {
                    byte[] data = await context.ReadRegistersAsync(first.Area, segmentStart, pointCount, cancellationToken).ConfigureAwait(false);
                    for (int i = startIndex; i < endIndex; i++)
                        results[items[i].Index] = DecodeRegisterResult(items[i], data, segmentStart, context);
                }
            }
            catch (Exception ex)
            {
                bool communicationError = IsCommunicationException(ex);
                if (!communicationError && endIndex - startIndex > 1)
                {
                    await RetrySegmentBySplittingAsync(items, startIndex, endIndex, context, results, cancellationToken).ConfigureAwait(false);
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                    results[items[i].Index] = PlcBatchReadResult.FromFailure(items[i].Request, ex.Message, communicationError);
            }
        }

        private static bool HasConnectionFailure(PlcBatchReadResult[] results)
        {
            for (int index = 0; index < results.Length; index++)
            {
                PlcBatchReadResult result = results[index];
                if (result != null && PlcBatchReadResult.IsConnectionFailureScope(result.FailureScope))
                    return true;
            }
            return false;
        }

        private static void RetrySegmentBySplitting(
            List<BatchReadItem> items,
            int startIndex,
            int endIndex,
            ModbusBatchReadContext context,
            PlcBatchReadResult[] results)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            ExecuteSegment(items, startIndex, middle, items[startIndex].StartAddress, GetSegmentEnd(items, startIndex, middle), context, results);
            ExecuteSegment(items, middle, endIndex, items[middle].StartAddress, GetSegmentEnd(items, middle, endIndex), context, results);
        }

        private static async ValueTask RetrySegmentBySplittingAsync(
            List<BatchReadItem> items,
            int startIndex,
            int endIndex,
            ModbusAsyncBatchReadContext context,
            PlcBatchReadResult[] results,
            CancellationToken cancellationToken)
        {
            int middle = startIndex + (endIndex - startIndex) / 2;
            await ExecuteSegmentAsync(items, startIndex, middle, items[startIndex].StartAddress, GetSegmentEnd(items, startIndex, middle), context, results, cancellationToken).ConfigureAwait(false);
            await ExecuteSegmentAsync(items, middle, endIndex, items[middle].StartAddress, GetSegmentEnd(items, middle, endIndex), context, results, cancellationToken).ConfigureAwait(false);
        }

        private static int GetSegmentEnd(List<BatchReadItem> items, int startIndex, int endIndex)
        {
            int segmentEnd = items[startIndex].EndAddress;
            for (int i = startIndex + 1; i < endIndex; i++)
                segmentEnd = Math.Max(segmentEnd, items[i].EndAddress);
            return segmentEnd;
        }

        private static PlcBatchReadResult DecodeBitResult(BatchReadItem item, bool[] values, int segmentStart, ModbusBatchReadContext context)
        {
            try
            {
                int offset = item.StartAddress - segmentStart;
                bool[] itemValues = new bool[item.ValueCount];
                Array.Copy(values, offset, itemValues, 0, itemValues.Length);
                object value = ModbusDataCodec.DecodeBits(item.Request.DataType, itemValues, item.ValueCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(context.GetTypeCode(item.Area), context.GetTypeName(item.Area), value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeRegisterResult(BatchReadItem item, byte[] data, int segmentStart, ModbusBatchReadContext context)
        {
            try
            {
                int byteOffset = (item.StartAddress - segmentStart) * 2;
                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);

                object value = item.Kind == ReadKind.RegisterBit
                    ? ModbusDataCodec.DecodeRegisterBits(item.Request.DataType, itemData, item.BitIndex, item.ValueCount)
                    : ModbusDataCodec.DecodeRegisters(item.Request.DataType, itemData, item.ValueCount);
                string typeName = context.GetTypeName(item.Area) + (item.Kind == ReadKind.RegisterBit ? ".BIT" : string.Empty);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(context.GetTypeCode(item.Area), typeName, value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeBitResult(BatchReadItem item, bool[] values, int segmentStart, ModbusAsyncBatchReadContext context)
        {
            try
            {
                int offset = item.StartAddress - segmentStart;
                bool[] itemValues = new bool[item.ValueCount];
                Array.Copy(values, offset, itemValues, 0, itemValues.Length);
                object value = ModbusDataCodec.DecodeBits(item.Request.DataType, itemValues, item.ValueCount);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(context.GetTypeCode(item.Area), context.GetTypeName(item.Area), value));
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(item.Request, ex.Message, false);
            }
        }

        private static PlcBatchReadResult DecodeRegisterResult(BatchReadItem item, byte[] data, int segmentStart, ModbusAsyncBatchReadContext context)
        {
            try
            {
                int byteOffset = (item.StartAddress - segmentStart) * 2;
                byte[] itemData = new byte[item.PointCount * 2];
                Buffer.BlockCopy(data, byteOffset, itemData, 0, itemData.Length);

                object value = item.Kind == ReadKind.RegisterBit
                    ? ModbusDataCodec.DecodeRegisterBits(item.Request.DataType, itemData, item.BitIndex, item.ValueCount)
                    : ModbusDataCodec.DecodeRegisters(item.Request.DataType, itemData, item.ValueCount);
                string typeName = context.GetTypeName(item.Area) + (item.Kind == ReadKind.RegisterBit ? ".BIT" : string.Empty);
                return PlcBatchReadResult.FromSuccess(item.Request, new PlcReadResult(context.GetTypeCode(item.Area), typeName, value));
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

        private static int CompareItems(BatchReadItem left, BatchReadItem right)
        {
            int result = left.StartAddress.CompareTo(right.StartAddress);
            if (result != 0)
                return result;
            return left.EndAddress.CompareTo(right.EndAddress);
        }

        private static PlcBatchReadRequest EnsureRequest(PlcBatchReadRequest request)
        {
            return request ?? new PlcBatchReadRequest(string.Empty, PlcDataType.Int16, 1, 0);
        }

        private sealed class BatchReadItem
        {
            private BatchReadItem()
            {
            }

            public int Index { get; private set; }
            public PlcBatchReadRequest Request { get; private set; }
            public ModbusArea Area { get; private set; }
            public ReadKind Kind { get; private set; }
            public int StartAddress { get; private set; }
            public int EndAddress { get; private set; }
            public int PointCount { get; private set; }
            public int ValueCount { get; private set; }
            public int BitIndex { get; private set; }

            public static BatchReadItem Create(int index, PlcBatchReadRequest request)
            {
                ModbusAddress address = ModbusAddress.Parse(request.Address, request.DataType);
                if (address.IsBitArea)
                    return CreateBitItem(index, request, address);
                if (ModbusDataCodec.IsBitOnlyType(request.DataType))
                    throw new NotSupportedException("Coil/Discrete Input data types can only be used with Modbus bit-area addresses.");
                if (IsRegisterBitAccess(address, request.DataType))
                    return CreateRegisterBitItem(index, request, address);
                return CreateRegisterItem(index, request, address);
            }

            private static BatchReadItem CreateBitItem(int index, PlcBatchReadRequest request, ModbusAddress address)
            {
                if (!ModbusDataCodec.IsBitType(request.DataType))
                    throw new NotSupportedException("Modbus bit-area addresses can only read BOOL/Coil/Discrete Input data types.");

                int count = PlcDataTypeHelper.IsArray(request.DataType) ? request.ElementCount : 1;
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(request.DataType) ? request.ElementOffset : 0);
                return Create(index, request, start.Area, ReadKind.Bit, start.Address, count, count, -1);
            }

            private static BatchReadItem CreateRegisterBitItem(int index, PlcBatchReadRequest request, ModbusAddress address)
            {
                int count = PlcDataTypeHelper.IsArray(request.DataType) ? request.ElementCount : 1;
                ModbusAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(request.DataType) ? request.ElementOffset : 0);
                int registerCount = (start.BitIndex + count + 15) / 16;
                return Create(index, request, start.Area, ReadKind.RegisterBit, start.Address, registerCount, count, start.BitIndex);
            }

            private static BatchReadItem CreateRegisterItem(int index, PlcBatchReadRequest request, ModbusAddress address)
            {
                bool usesCount = PlcDataTypeHelper.IsArray(request.DataType) || request.DataType == PlcDataType.String;
                int valueCount = usesCount ? request.ElementCount : 1;
                int registerOffset = PlcDataTypeHelper.IsArray(request.DataType)
                    ? ModbusDataCodec.GetRegisterOffset(request.DataType, request.ElementOffset)
                    : 0;
                ModbusAddress start = address.OffsetRegisters(registerOffset);
                int registerCount = ModbusDataCodec.GetRegisterCount(request.DataType, valueCount);
                return Create(index, request, start.Area, ReadKind.Register, start.Address, registerCount, valueCount, -1);
            }

            private static BatchReadItem Create(
                int index,
                PlcBatchReadRequest request,
                ModbusArea area,
                ReadKind kind,
                int startAddress,
                int pointCount,
                int valueCount,
                int bitIndex)
            {
                return new BatchReadItem
                {
                    Index = index,
                    Request = request,
                    Area = area,
                    Kind = kind,
                    StartAddress = startAddress,
                    PointCount = Math.Max(1, pointCount),
                    ValueCount = Math.Max(1, valueCount),
                    BitIndex = bitIndex,
                    EndAddress = startAddress + Math.Max(1, pointCount) - 1
                };
            }

            private static bool IsRegisterBitAccess(ModbusAddress address, PlcDataType dataType)
            {
                return (dataType == PlcDataType.Bool && address.HasBitIndex) ||
                       dataType == PlcDataType.BoolArray;
            }
        }

        private enum ReadKind
        {
            Bit,
            Register,
            RegisterBit
        }
    }
}
