namespace IPC.Plc.Communication.CanOpen
{
    internal readonly struct CanFrame
    {
        public CanFrame(int identifier, byte[] data)
        {
            Identifier = identifier;
            Data = data ?? new byte[0];
        }

        public int Identifier { get; }
        public byte[] Data { get; }
    }
}
