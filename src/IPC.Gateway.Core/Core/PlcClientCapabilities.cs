namespace IPC.Plc.Communication.Core
{
    public sealed class PlcClientCapabilities
    {
        public PlcClientCapabilities()
        {
            AsyncKind = PlcClientAsyncKind.SyncOnly;
            PreferredReadMode = PlcPreferredReadMode.Single;
            SupportsRead = true;
            SupportsWrite = true;
            RequiresSerializedAccess = true;
            Notes = string.Empty;
        }

        public PlcClientAsyncKind AsyncKind { get; set; }
        public PlcPreferredReadMode PreferredReadMode { get; set; }
        public bool SupportsRead { get; set; }
        public bool SupportsWrite { get; set; }
        public bool SupportsNativeAsync { get; set; }
        public bool SupportsBatchRead { get; set; }
        public bool SupportsSubscription { get; set; }
        public bool SupportsAddressValidation { get; set; }
        public bool SupportsConcurrentRequests { get; set; }
        public bool RequiresSerializedAccess { get; set; }
        public int MaxBatchItems { get; set; }
        public int MaxSubscriptionItems { get; set; }
        public string Notes { get; set; }

        public PlcClientCapabilities Clone()
        {
            return new PlcClientCapabilities
            {
                AsyncKind = AsyncKind,
                PreferredReadMode = PreferredReadMode,
                SupportsRead = SupportsRead,
                SupportsWrite = SupportsWrite,
                SupportsNativeAsync = SupportsNativeAsync,
                SupportsBatchRead = SupportsBatchRead,
                SupportsSubscription = SupportsSubscription,
                SupportsAddressValidation = SupportsAddressValidation,
                SupportsConcurrentRequests = SupportsConcurrentRequests,
                RequiresSerializedAccess = RequiresSerializedAccess,
                MaxBatchItems = MaxBatchItems,
                MaxSubscriptionItems = MaxSubscriptionItems,
                Notes = Notes ?? string.Empty
            };
        }
    }
}
