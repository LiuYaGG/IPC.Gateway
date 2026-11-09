namespace IPC.Plc.Communication.Core
{
    public sealed class PlcSubscriptionRequest
    {
        public PlcSubscriptionRequest(string key, string address, PlcDataType dataType, int elementCount, int elementOffset)
            : this(key, address, dataType, elementCount, elementOffset, 0)
        {
        }

        public PlcSubscriptionRequest(string key, string address, PlcDataType dataType, int elementCount, int elementOffset, int samplingIntervalMs)
        {
            Key = string.IsNullOrWhiteSpace(key) ? address ?? string.Empty : key;
            Address = address ?? string.Empty;
            DataType = dataType;
            ElementCount = elementCount;
            ElementOffset = elementOffset;
            SamplingIntervalMs = samplingIntervalMs;
        }

        public string Key { get; private set; }
        public string Address { get; private set; }
        public PlcDataType DataType { get; private set; }
        public int ElementCount { get; private set; }
        public int ElementOffset { get; private set; }
        public int SamplingIntervalMs { get; private set; }

        public PlcBatchReadRequest ToBatchReadRequest()
        {
            return new PlcBatchReadRequest(Address, DataType, ElementCount, ElementOffset);
        }
    }
}
