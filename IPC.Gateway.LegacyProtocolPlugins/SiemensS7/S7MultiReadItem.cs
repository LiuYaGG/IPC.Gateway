using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    internal sealed class S7MultiReadItem
    {
        public int Index { get; set; }
        public PlcBatchReadRequest Request { get; set; }
        public S7Address Address { get; set; }
        public int ByteCount { get; set; }
        public byte[] Data { get; set; }
        public string ErrorMessage { get; set; }
        public PlcReadFailureScope FailureScope { get; set; } = PlcReadFailureScope.Tag;

        public int ResponseSize
        {
            get { return 4 + ByteCount + (ByteCount % 2); }
        }
    }
}
