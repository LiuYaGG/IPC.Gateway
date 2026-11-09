using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.SiemensS7
{
    public static class S7ProtocolErrors
    {
        public static PlcProtocolException Ack(byte errorClass, byte errorCode)
        {
            PlcReadFailureScope scope;
            switch (errorClass)
            {
                case 0x81: // application relationship / connection state
                case 0x85: // supplies / job sequencing
                    scope = PlcReadFailureScope.Session;
                    break;
                case 0x82: // object definition
                case 0x87: // access error
                    scope = PlcReadFailureScope.Tag;
                    break;
                default:
                    scope = PlcReadFailureScope.Device;
                    break;
            }

            return new PlcProtocolException(
                scope,
                $"S7 response error: class 0x{errorClass:X2}, code 0x{errorCode:X2}.",
                $"S7-{errorClass:X2}{errorCode:X2}");
        }

        public static PlcProtocolException Item(byte returnCode, string operation)
        {
            PlcReadFailureScope scope = returnCode == 0x01
                ? PlcReadFailureScope.Device
                : PlcReadFailureScope.Tag;
            return new PlcProtocolException(
                scope,
                $"S7 {operation} failed: {Describe(returnCode)}",
                $"S7-ITEM-{returnCode:X2}");
        }

        public static string Describe(byte returnCode)
        {
            switch (returnCode)
            {
                case 0x01: return "hardware fault (0x01).";
                case 0x03: return "object access is not permitted (0x03).";
                case 0x05: return "address is outside the permitted range (0x05).";
                case 0x06: return "data type is not supported (0x06).";
                case 0x07: return "data type is inconsistent (0x07).";
                case 0x0A: return "object does not exist (0x0A).";
                default: return $"return code 0x{returnCode:X2}.";
            }
        }
    }
}
