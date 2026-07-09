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
                case PlcProtocol.ModbusTcp:
                case PlcProtocol.Dlt6452007:
                case PlcProtocol.Cjt1882004:
                    return NativeIo(batchRead: true);

                case PlcProtocol.MitsubishiSerial:
                case PlcProtocol.MitsubishiQlSerial:
                case PlcProtocol.ModbusRtu:
                    return DedicatedThread(batchRead: true);

                case PlcProtocol.BacnetIp:
                    return SyncOnly(batchRead: true, "BACnet/IP uses ReadPropertyMultiple for batch reads and falls back to single property reads when a non-transport batch error occurs.");

                case PlcProtocol.CanOpen:
                    return SyncOnly(batchRead: true, "CANopen uses SDO object dictionary reads; batch read keeps one adapter session and returns per-object results.");

                case PlcProtocol.OpcUa:
                    return SubscriptionPreferred(batchRead: true, "OPC UA supports server-side subscriptions; runtime should use monitored items first and keep batch reads as fallback.");

                case PlcProtocol.OpcDa:
                    return SyncOnly(batchRead: true, "OPC DA currently uses synchronous COM IO.");

                case PlcProtocol.VirtualPlc:
                    return new PlcClientCapabilities
                    {
                        AsyncKind = PlcClientAsyncKind.SynchronousCompletion,
                SupportsNativeAsync = false,
                SupportsBatchRead = false,
                SupportsSubscription = false,
                SupportsConcurrentRequests = true,
                RequiresSerializedAccess = false,
                Notes = "Virtual PLC completes from memory without external IO."
                    };

                case PlcProtocol.Plugin:
                default:
                    return SyncOnly(batchRead: false, "Plugin capabilities are unknown unless the driver provides them.");
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

            result.Notes = result.Notes ?? string.Empty;
            return result;
        }

        private static PlcClientCapabilities NativeIo(bool batchRead)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.NativeIo,
                SupportsNativeAsync = true,
                SupportsBatchRead = batchRead,
                SupportsSubscription = false,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                Notes = "Protocol can be migrated to native socket async IO; each client connection should remain serialized."
            };
        }

        private static PlcClientCapabilities DedicatedThread(bool batchRead)
        {
            return DedicatedThread(batchRead, "Serial protocol should stay isolated on a serialized device execution path.");
        }

        private static PlcClientCapabilities DedicatedThread(bool batchRead, string notes)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.DedicatedThread,
                SupportsNativeAsync = false,
                SupportsBatchRead = batchRead,
                SupportsSubscription = false,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                Notes = notes ?? string.Empty
            };
        }

        private static PlcClientCapabilities SyncOnly(bool batchRead, string notes)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.SyncOnly,
                SupportsNativeAsync = false,
                SupportsBatchRead = batchRead,
                SupportsSubscription = false,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                Notes = notes ?? string.Empty
            };
        }

        private static PlcClientCapabilities SubscriptionPreferred(bool batchRead, string notes)
        {
            return new PlcClientCapabilities
            {
                AsyncKind = PlcClientAsyncKind.DedicatedThread,
                SupportsNativeAsync = false,
                SupportsBatchRead = batchRead,
                SupportsSubscription = true,
                SupportsConcurrentRequests = false,
                RequiresSerializedAccess = true,
                Notes = notes ?? string.Empty
            };
        }
    }
}
