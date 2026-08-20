using System;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Pccc
{
    internal sealed class PcccBatchReadItem
    {
        private PcccBatchReadItem()
        {
        }

        public int Index { get; private set; }
        public PlcBatchReadRequest Request { get; private set; }
        public PcccAddress Address { get; private set; }
        public int ByteCount { get; private set; }
        public int StartElement => Address.ElementNumber;

        public string GroupKey => Address.FileTypeCode + "|" + Address.FileNumber + "|" + Address.SubElement;

        public static PcccBatchReadItem Create(int index, PlcBatchReadRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.ElementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(request.ElementCount));
            if (request.ElementOffset < 0)
                throw new ArgumentOutOfRangeException(nameof(request.ElementOffset));

            PcccAddress address = PcccAddress.Parse(request.Address).AddElementOffset(request.ElementOffset);
            int byteCount = PcccDataCodec.GetByteCount(address, request.DataType, request.ElementCount);
            if (byteCount <= 0 || byteCount > 220)
                throw new ArgumentOutOfRangeException(nameof(request.ElementCount), "PCCC单个标签读取不能超过220字节。");

            return new PcccBatchReadItem
            {
                Index = index,
                Request = request,
                Address = address,
                ByteCount = byteCount
            };
        }

        public int GetEndByteOffset(int segmentStartElement)
        {
            return checked((StartElement - segmentStartElement) * Address.NativeElementSize + ByteCount);
        }
    }
}
