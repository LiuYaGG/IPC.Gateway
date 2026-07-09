namespace IPC.Plc.Communication.Core
{
    public sealed class PlcClientCapabilities
    {
        public PlcClientCapabilities()
        {
            AsyncKind = PlcClientAsyncKind.SyncOnly;
            RequiresSerializedAccess = true;
            Notes = string.Empty;
        }

        public PlcClientAsyncKind AsyncKind { get; set; }
        public bool SupportsNativeAsync { get; set; }
        public bool SupportsBatchRead { get; set; }
        public bool SupportsSubscription { get; set; }
        public bool SupportsConcurrentRequests { get; set; }
        public bool RequiresSerializedAccess { get; set; }
        public string Notes { get; set; }

        public PlcClientCapabilities Clone()
        {
            return new PlcClientCapabilities
            {
                AsyncKind = AsyncKind,
                SupportsNativeAsync = SupportsNativeAsync,
                SupportsBatchRead = SupportsBatchRead,
                SupportsSubscription = SupportsSubscription,
                SupportsConcurrentRequests = SupportsConcurrentRequests,
                RequiresSerializedAccess = RequiresSerializedAccess,
                Notes = Notes ?? string.Empty
            };
        }
    }
}
