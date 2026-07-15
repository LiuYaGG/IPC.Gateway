using System;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    internal sealed class FinsBatchReadItem
    {
        public int Index { get; private set; }
        public PlcBatchReadRequest Request { get; private set; }
        public FinsMemoryArea Area { get; private set; }
        public FinsBatchReadKind Kind { get; private set; }
        public byte AreaCode { get; private set; }
        public int StartPoint { get; private set; }
        public int EndPoint { get; private set; }
        public int PointCount { get; private set; }
        public int ValueCount { get; private set; }

        public static FinsBatchReadItem Create(int index, PlcBatchReadRequest request, FinsDriverOptions options)
        {
            FinsAddress address = FinsAddress.Parse(request.Address, request.DataType, options);
            return FinsDataCodec.IsBitType(request.DataType)
                ? CreateBitItem(index, request, address)
                : CreateWordItem(index, request, address);
        }

        private static FinsBatchReadItem CreateBitItem(int index, PlcBatchReadRequest request, FinsAddress address)
        {
            int elementCount = Math.Max(1, request.ElementCount);
            int valueCount = PlcDataTypeHelper.IsArray(request.DataType) ? elementCount : 1;
            FinsAddress start = address.OffsetBits(PlcDataTypeHelper.IsArray(request.DataType) ? request.ElementOffset : 0);
            start.EnsureRange(valueCount, true);
            int startPoint = start.Area.BitAddressUsesWordIndex
                ? start.WordAddress
                : start.WordAddress * 16 + start.BitIndex;
            return Create(index, request, start.Area, FinsBatchReadKind.Bit, start.Area.BitCode, startPoint, valueCount, valueCount);
        }

        private static FinsBatchReadItem CreateWordItem(int index, PlcBatchReadRequest request, FinsAddress address)
        {
            if (address.HasBitIndex)
                throw new NotSupportedException("Non-BOOL FINS reads cannot use bit addresses.");

            int elementCount = Math.Max(1, request.ElementCount);
            bool usesCount = PlcDataTypeHelper.IsArray(request.DataType) || request.DataType == PlcDataType.String;
            int valueCount = usesCount ? elementCount : 1;
            int wordOffset = PlcDataTypeHelper.IsArray(request.DataType)
                ? FinsDataCodec.GetWordOffset(request.DataType, request.ElementOffset)
                : 0;
            FinsAddress start = address.OffsetWords(wordOffset);
            int wordCount = FinsDataCodec.GetWordCount(request.DataType, valueCount);
            start.EnsureRange(wordCount, false);
            return Create(index, request, start.Area, FinsBatchReadKind.Word, start.Area.WordCode, start.WordAddress, wordCount, valueCount);
        }

        private static FinsBatchReadItem Create(
            int index,
            PlcBatchReadRequest request,
            FinsMemoryArea area,
            FinsBatchReadKind kind,
            byte areaCode,
            int startPoint,
            int pointCount,
            int valueCount)
        {
            int normalizedPointCount = Math.Max(1, pointCount);
            return new FinsBatchReadItem
            {
                Index = index,
                Request = request,
                Area = area,
                Kind = kind,
                AreaCode = areaCode,
                StartPoint = startPoint,
                PointCount = normalizedPointCount,
                ValueCount = Math.Max(1, valueCount),
                EndPoint = startPoint + normalizedPointCount - 1
            };
        }
    }

    internal enum FinsBatchReadKind
    {
        Bit,
        Word
    }
}
