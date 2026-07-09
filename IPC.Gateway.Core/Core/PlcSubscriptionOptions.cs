namespace IPC.Plc.Communication.Core
{
    public sealed class PlcSubscriptionOptions
    {
        public PlcSubscriptionOptions()
        {
            PublishingIntervalMs = 1000;
            SamplingIntervalMs = 1000;
            QueueSize = 1;
            DiscardOldest = true;
        }

        public int PublishingIntervalMs { get; set; }
        public int SamplingIntervalMs { get; set; }
        public int QueueSize { get; set; }
        public bool DiscardOldest { get; set; }
    }
}
