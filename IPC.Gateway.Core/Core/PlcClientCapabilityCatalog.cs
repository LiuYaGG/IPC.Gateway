namespace IPC.Plc.Communication.Core
{
    public static class PlcClientCapabilityCatalog
    {
        public static PlcClientCapabilities ForProtocol(PlcProtocol protocol)
        {
            switch (protocol)
            {
                case PlcProtocol.RockwellCip:
                case PlcProtocol.SiemensS7:
                case PlcProtocol.MitsubishiMc:
                case PlcProtocol.MitsubishiMc1E:
                case PlcProtocol.OmronFins:
                    return NativeIo(128);

                case PlcProtocol.ModbusTcp:
                    return NativeIo(256);

                case PlcProtocol.Dlt6452007:
                case PlcProtocol.Cjt1882004:
                    return NativeIo(64, supportsWrite: false);

                case PlcProtocol.MitsubishiSerial:
                case PlcProtocol.MitsubishiQlSerial:
                    return DedicatedThread(64);

                case PlcProtocol.ModbusRtu:
                    return DedicatedThread(128);

                case PlcProtocol.BacnetIp:
                    return SyncOnly(128, "BACnet/IP uses ReadPropertyMultiple for batch reads and falls back to single property reads when a non-transport batch error occurs.");

                case PlcProtocol.CanOpen:
                    return SyncOnly(64, "CANopen uses SDO object dictionary reads; batch read keeps one adapter session and returns per-object results.");

                case PlcProtocol.OpcUa:
                    return SubscriptionPreferred(256, 1000, "OPC UA supports server-side subscriptions; runtime should use monitored items first and keep batch reads as fallback.");

                case PlcProtocol.OpcDa:
                    return SyncOnly(512, "OPC DA currently uses synchronous COM IO.");

                case PlcProtocol.VirtualPlc:
                    return new PlcClientCapabilities
                    {
                        AsyncKind = PlcClientAsyncKind.SynchronousCompletion,
                        PreferredReadMode = PlcPreferredReadMode.Single,
                        SupportsRead = true,
                        SupportsWrite = true,
                        SupportsNativeAsync = false,
                        SupportsBatchRead = false,
                        SupportsSubscription = false,
                        SupportsAddressValidation = true,
                        SupportsConcurrentRequests = true,
                        RequiresSerializedAccess = false,
                        Notes = "Virtual PLC completes from memory without external IO."
                    };

                case PlcProtocol.Plugin:
                default:
                    return SyncOnly(0, "Plugin capabilities are unknown unless the driver provides them.", supportsAddressValidation: false);
            }
        }

        public static PlcClientCapabilities Normalize(PlcClientCapabilities? capabilities, PlcProtocol fallbackProtocol)
        {
            PlcClientCapabilities result = capabilities == null
                ? ForProtocol(fallbackProtocol)
                : capabilities.Clone();

            if (result.AsyncKind == PlcClientAsyncKind.NativeIo)
                result.SupportsNativeAsync = true;
            else
                result.SupportsNativeAsync = false;

            if (result.SupportsConcurrentRequests)
                result.RequiresSerializedAccess = false;

            if (!result.SupportsBatchRead)
                result.MaxBatchItems = 0;
            else if (result.MaxBatchItems <= 0)
                result.MaxBatchItems = 128;

            if (!result.SupportsSubscription)
                result.MaxSubscriptionItems = 0;
            else if (result.MaxSubscriptionItems <= 0)
                result.MaxSubscriptionItems = 1000;

            if (result.PreferredReadMode == PlcPreferredReadMode.Subscription && !result.SupportsSubscription)
                result.PreferredReadMode = result.SupportsBatchRead ? PlcPreferredReadMode.Batch : PlcPreferredReadMode.Single;
            if (result.PreferredReadMode == PlcPreferredReadMode.Batch && !result.SupportsBatchRead)
                result.PreferredReadMode = PlcPreferredReadMode.Single;

            result.Notes = result.Notes ?? string.Empty;
            return result;
        }

        private static PlcClientCapabilities NativeIo(int maxBatchItems, bool supportsWrite = true)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.NativeIo,
                PreferredReadMode = PlcPreferredReadMode.Batch,
                SupportsRead = true,
                SupportsWrite = supportsWrite,
                SupportsNativeAsync = true,
                SupportsBatchRead = true,
                SupportsSubscription = false,
                SupportsAddressValidation = true,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                MaxBatchItems = maxBatchItems,
                Notes = "Protocol can be migrated to native socket async IO; each client connection should remain serialized."
            };
        }

        private static PlcClientCapabilities DedicatedThread(int maxBatchItems)
        {
            return DedicatedThread(maxBatchItems, "Serial protocol should stay isolated on a serialized device execution path.");
        }

        private static PlcClientCapabilities DedicatedThread(int maxBatchItems, string notes)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.DedicatedThread,
                PreferredReadMode = PlcPreferredReadMode.Batch,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsNativeAsync = false,
                SupportsBatchRead = true,
                SupportsSubscription = false,
                SupportsAddressValidation = true,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                MaxBatchItems = maxBatchItems,
                Notes = notes ?? string.Empty
            };
        }

        private static PlcClientCapabilities SyncOnly(int maxBatchItems, string notes, bool supportsAddressValidation = true)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.SyncOnly,
                PreferredReadMode = maxBatchItems > 0 ? PlcPreferredReadMode.Batch : PlcPreferredReadMode.Single,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsNativeAsync = false,
                SupportsBatchRead = maxBatchItems > 0,
                SupportsSubscription = false,
                SupportsAddressValidation = supportsAddressValidation,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                MaxBatchItems = maxBatchItems,
                Notes = notes ?? string.Empty
            };
        }

        private static PlcClientCapabilities SubscriptionPreferred(int maxBatchItems, int maxSubscriptionItems, string notes)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.DedicatedThread,
                PreferredReadMode = PlcPreferredReadMode.Subscription,
                SupportsRead = true,
                SupportsWrite = true,
                SupportsNativeAsync = false,
                SupportsBatchRead = true,
                SupportsSubscription = true,
                SupportsAddressValidation = true,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                MaxBatchItems = maxBatchItems,
                MaxSubscriptionItems = maxSubscriptionItems,
                Notes = notes ?? string.Empty
            };
        }
    }
}
