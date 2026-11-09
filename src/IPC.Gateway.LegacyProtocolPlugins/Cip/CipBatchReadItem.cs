using System;
using System.Collections.Generic;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal sealed class CipBatchReadItem
    {
        private readonly CipBatchReadKind _kind;
        private readonly int _firstBit;

        private CipBatchReadItem(int index, PlcBatchReadRequest request, CipBatchReadKind kind, int firstBit)
        {
            Index = index;
            Request = request;
            _kind = kind;
            _firstBit = firstBit;
            Operations = new List<CipBatchReadOperation>();
        }

        public int Index { get; private set; }
        public PlcBatchReadRequest Request { get; private set; }
        public List<CipBatchReadOperation> Operations { get; private set; }

        public static CipBatchReadItem Create(int index, PlcBatchReadRequest request, CipBatchReadContext context)
        {
            if (request.ElementCount <= 0)
                throw new ArgumentOutOfRangeException("ElementCount");
            if (request.ElementOffset < 0)
                throw new ArgumentOutOfRangeException("ElementOffset");

            if (CipExplicitAddress.IsExplicit(request.Address))
                return CreateExplicitItem(index, request, context);

            if (request.DataType == PlcDataType.BoolArray && !context.UseNativeBoolArrays)
                return CreatePackedBoolItem(index, request, context, false);

            if (request.DataType == PlcDataType.Bool && IsIndexedTag(request.Address) && !context.UseNativeBoolArrays)
                return CreatePackedBoolItem(index, request, context, true);

            if (PlcDataTypeHelper.IsArray(request.DataType) &&
                (request.ElementOffset > 0 || NeedsSegmentation(request.DataType, request.ElementCount)))
                return CreateSegmentedArrayItem(index, request, context);

            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.Direct, 0);
            string tagName = request.ElementOffset > 0 ? BuildArrayElementTag(request.Address, request.ElementOffset) : request.Address;
            item.AddOperation(context, tagName, request.DataType, request.ElementCount);
            return item;
        }

        public static CipBatchReadItem Create(int index, PlcBatchReadRequest request, CipAsyncBatchReadContext context)
        {
            if (request.ElementCount <= 0)
                throw new ArgumentOutOfRangeException("ElementCount");
            if (request.ElementOffset < 0)
                throw new ArgumentOutOfRangeException("ElementOffset");

            if (CipExplicitAddress.IsExplicit(request.Address))
                return CreateExplicitItem(index, request, context);

            if (request.DataType == PlcDataType.BoolArray && !context.UseNativeBoolArrays)
                return CreatePackedBoolItem(index, request, context, false);

            if (request.DataType == PlcDataType.Bool && IsIndexedTag(request.Address) && !context.UseNativeBoolArrays)
                return CreatePackedBoolItem(index, request, context, true);

            if (PlcDataTypeHelper.IsArray(request.DataType) &&
                (request.ElementOffset > 0 || NeedsSegmentation(request.DataType, request.ElementCount)))
                return CreateSegmentedArrayItem(index, request, context);

            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.Direct, 0);
            string tagName = request.ElementOffset > 0 ? BuildArrayElementTag(request.Address, request.ElementOffset) : request.Address;
            item.AddOperation(context, tagName, request.DataType, request.ElementCount);
            return item;
        }

        public PlcBatchReadResult BuildResult()
        {
            for (int i = 0; i < Operations.Count; i++)
            {
                CipBatchReadOperation operation = Operations[i];
                if (!operation.Success)
                    return PlcBatchReadResult.FromFailure(Request, operation.ErrorMessage, operation.FailureScope);
            }

            try
            {
                if (_kind == CipBatchReadKind.Direct)
                    return PlcBatchReadResult.FromSuccess(Request, Operations[0].Result);
                if (_kind == CipBatchReadKind.PackedBool || _kind == CipBatchReadKind.IndexedBool)
                    return PlcBatchReadResult.FromSuccess(Request, BuildPackedBoolResult());

                return PlcBatchReadResult.FromSuccess(Request, BuildSegmentedArrayResult());
            }
            catch (Exception ex)
            {
                return PlcBatchReadResult.FromFailure(Request, ex.Message, false);
            }
        }

        private static CipBatchReadItem CreatePackedBoolItem(
            int index,
            PlcBatchReadRequest request,
            CipBatchReadContext context,
            bool indexedBool)
        {
            string baseTag;
            int bitOffset;
            NormalizeBoolArrayTag(request.Address, request.ElementOffset, out baseTag, out bitOffset);

            int firstWord = bitOffset / 32;
            int firstBit = bitOffset % 32;
            int boolCount = indexedBool ? 1 : request.ElementCount;
            int wordCount = (firstBit + boolCount + 31) / 32;

            CipBatchReadItem item = new CipBatchReadItem(
                index,
                request,
                indexedBool ? CipBatchReadKind.IndexedBool : CipBatchReadKind.PackedBool,
                firstBit);
            item.AddOperation(context, BuildArrayElementTagAlways(baseTag, firstWord), PlcDataType.Int32Array, wordCount);
            return item;
        }

        private static CipBatchReadItem CreateExplicitItem(
            int index,
            PlcBatchReadRequest request,
            CipBatchReadContext context)
        {
            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.Direct, 0);
            item.AddOperation(context, request.Address, request.DataType, request.ElementCount);
            return item;
        }

        private static CipBatchReadItem CreateExplicitItem(
            int index,
            PlcBatchReadRequest request,
            CipAsyncBatchReadContext context)
        {
            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.Direct, 0);
            item.AddOperation(context, request.Address, request.DataType, request.ElementCount);
            return item;
        }

        private static CipBatchReadItem CreatePackedBoolItem(
            int index,
            PlcBatchReadRequest request,
            CipAsyncBatchReadContext context,
            bool indexedBool)
        {
            string baseTag;
            int bitOffset;
            NormalizeBoolArrayTag(request.Address, request.ElementOffset, out baseTag, out bitOffset);

            int firstWord = bitOffset / 32;
            int firstBit = bitOffset % 32;
            int boolCount = indexedBool ? 1 : request.ElementCount;
            int wordCount = (firstBit + boolCount + 31) / 32;

            CipBatchReadItem item = new CipBatchReadItem(
                index,
                request,
                indexedBool ? CipBatchReadKind.IndexedBool : CipBatchReadKind.PackedBool,
                firstBit);
            item.AddOperation(context, BuildArrayElementTagAlways(baseTag, firstWord), PlcDataType.Int32Array, wordCount);
            return item;
        }

        private static CipBatchReadItem CreateSegmentedArrayItem(
            int index,
            PlcBatchReadRequest request,
            CipBatchReadContext context)
        {
            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.SegmentedArray, 0);
            int copied = 0;
            int maxElements = GetMaxElementsPerPacket(request.DataType);
            while (copied < request.ElementCount)
            {
                int chunkCount = Math.Min(maxElements, request.ElementCount - copied);
                string chunkTag = BuildArrayElementTag(request.Address, request.ElementOffset + copied);
                item.AddOperation(context, chunkTag, request.DataType, chunkCount);
                copied += chunkCount;
            }
            return item;
        }

        private static CipBatchReadItem CreateSegmentedArrayItem(
            int index,
            PlcBatchReadRequest request,
            CipAsyncBatchReadContext context)
        {
            CipBatchReadItem item = new CipBatchReadItem(index, request, CipBatchReadKind.SegmentedArray, 0);
            int copied = 0;
            int maxElements = GetMaxElementsPerPacket(request.DataType);
            while (copied < request.ElementCount)
            {
                int chunkCount = Math.Min(maxElements, request.ElementCount - copied);
                string chunkTag = BuildArrayElementTag(request.Address, request.ElementOffset + copied);
                item.AddOperation(context, chunkTag, request.DataType, chunkCount);
                copied += chunkCount;
            }
            return item;
        }

        private void AddOperation(CipBatchReadContext context, string tagName, PlcDataType dataType, int elementCount)
        {
            byte[] requestBytes = context.BuildReadRequest(tagName, dataType, elementCount);
            Operations.Add(new CipBatchReadOperation(this, Operations.Count, tagName, dataType, elementCount, requestBytes));
        }

        private void AddOperation(CipAsyncBatchReadContext context, string tagName, PlcDataType dataType, int elementCount)
        {
            byte[] requestBytes = context.BuildReadRequest(tagName, dataType, elementCount);
            Operations.Add(new CipBatchReadOperation(this, Operations.Count, tagName, dataType, elementCount, requestBytes));
        }

        private PlcReadResult BuildPackedBoolResult()
        {
            PlcReadResult current = Operations[0].Result;
            int[] words = (int[])current.Value;
            int count = _kind == CipBatchReadKind.IndexedBool ? 1 : Request.ElementCount;
            bool[] values = new bool[count];

            for (int i = 0; i < count; i++)
            {
                int absoluteBit = _firstBit + i;
                int wordIndex = absoluteBit / 32;
                int bitIndex = absoluteBit % 32;
                values[i] = ((words[wordIndex] >> bitIndex) & 1) != 0;
            }

            object value = _kind == CipBatchReadKind.IndexedBool ? (object)values[0] : values;
            return new PlcReadResult(current.TypeCode, current.TypeName, value);
        }

        private PlcReadResult BuildSegmentedArrayResult()
        {
            Array values = PlcDataTypeHelper.CreateArray(Request.DataType, Request.ElementCount);
            ushort actualType = 0;
            int copied = 0;

            for (int i = 0; i < Operations.Count; i++)
            {
                CipBatchReadOperation operation = Operations[i];
                Array chunkValues = (Array)operation.Result.Value;
                Array.Copy(chunkValues, 0, values, copied, operation.ElementCount);
                actualType = operation.Result.TypeCode;
                copied += operation.ElementCount;
            }

            return new PlcReadResult(actualType, CipTypeCodes.ToName(actualType), values);
        }

        private static bool NeedsSegmentation(PlcDataType dataType, int elementCount)
        {
            return PlcDataTypeHelper.IsArray(dataType) && elementCount > GetMaxElementsPerPacket(dataType);
        }

        private static int GetMaxElementsPerPacket(PlcDataType dataType)
        {
            int elementSize = PlcDataTypeHelper.GetElementSize(dataType);
            return Math.Max(1, 400 / elementSize);
        }

        private static void NormalizeBoolArrayTag(string tagName, int elementOffset, out string baseTag, out int bitOffset)
        {
            int existingIndex;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                bitOffset = existingIndex + elementOffset;
            else
            {
                baseTag = tagName;
                bitOffset = elementOffset;
            }
        }

        private static bool IsIndexedTag(string tagName)
        {
            string baseTag;
            int index;
            return TrySplitTrailingSingleIndex(tagName, out baseTag, out index);
        }

        private static string BuildArrayElementTag(string tagName, int elementOffset)
        {
            if (elementOffset == 0)
                return tagName;

            int existingIndex;
            string baseTag;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                return baseTag + "[" + (existingIndex + elementOffset).ToString() + "]";

            return tagName + "[" + elementOffset.ToString() + "]";
        }

        private static string BuildArrayElementTagAlways(string tagName, int elementOffset)
        {
            int existingIndex;
            string baseTag;
            if (TrySplitTrailingSingleIndex(tagName, out baseTag, out existingIndex))
                return baseTag + "[" + (existingIndex + elementOffset).ToString() + "]";

            return tagName + "[" + elementOffset.ToString() + "]";
        }

        private static bool TrySplitTrailingSingleIndex(string tagName, out string baseTag, out int index)
        {
            baseTag = tagName;
            index = 0;
            if (string.IsNullOrEmpty(tagName) || !tagName.EndsWith("]", StringComparison.Ordinal))
                return false;

            int open = tagName.LastIndexOf('[');
            if (open < 0 || open == tagName.Length - 2)
                return false;

            string indexText = tagName.Substring(open + 1, tagName.Length - open - 2);
            if (indexText.IndexOf(',') >= 0 || !int.TryParse(indexText, out index) || index < 0)
                return false;

            baseTag = tagName.Substring(0, open);
            return true;
        }
    }

}
