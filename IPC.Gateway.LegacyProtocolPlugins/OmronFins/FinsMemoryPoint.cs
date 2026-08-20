namespace IPC.Plc.Communication.OmronFins
{
    internal readonly struct FinsMemoryPoint
    {
        public FinsMemoryPoint(byte areaCode, int wordAddress, int bitIndex, bool isBit)
        {
            AreaCode = areaCode;
            WordAddress = wordAddress;
            BitIndex = bitIndex;
            IsBit = isBit;
        }

        public byte AreaCode { get; }
        public int WordAddress { get; }
        public int BitIndex { get; }
        public bool IsBit { get; }
        public int ByteCount => IsBit ? 1 : 2;
    }
}
