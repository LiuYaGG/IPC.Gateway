using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal static class CipStatusClassifier
    {
        public static PlcReadFailureScope Classify(byte generalStatus, byte responseService)
        {
            if (generalStatus == 0)
                return PlcReadFailureScope.None;
            // Packet capacity and unsupported service are not a lost PLC connection.
            if (generalStatus == 0x06 || generalStatus == 0x1A || generalStatus == 0x1B || generalStatus == 0x08)
                return PlcReadFailureScope.Batch;
            // ab_server also reports an unknown symbol as an invalid request parameter.
            // Re-read individual items if the router rejected the multiple-service request itself.
            if (generalStatus == 0x03)
                return responseService switch
                {
                    0x8A => PlcReadFailureScope.Batch,  // Multiple Service Packet
                    0xCC or 0xCD or 0xCE => PlcReadFailureScope.Tag, // Read/Write/Read-Modify-Write Tag
                    0x8E or 0x90 => PlcReadFailureScope.Tag,         // Get/Set Attribute Single
                    _ => PlcReadFailureScope.Device                 // includes Unconnected Send (route)
                };
            if (responseService == 0x8A)
                return PlcReadFailureScope.Device;

            switch (generalStatus)
            {
                case 0x04: // path segment error
                case 0x05: // path destination unknown
                case 0x09: // invalid attribute value
                case 0x13: // not enough data
                case 0x14: // attribute not supported
                case 0x15: // too much data
                case 0x16: // object does not exist
                case 0x20: // invalid parameter
                case 0x26: // path size invalid
                    return PlcReadFailureScope.Tag;
                case 0x07: // connection lost
                    return PlcReadFailureScope.Session;
                default:
                    return PlcReadFailureScope.Device;
            }
        }
    }
}
