using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal static class CipStatusClassifier
    {
        public static PlcReadFailureScope Classify(byte generalStatus, bool multipleServiceEnvelope)
        {
            if (generalStatus == 0)
                return PlcReadFailureScope.None;
            if (multipleServiceEnvelope)
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
