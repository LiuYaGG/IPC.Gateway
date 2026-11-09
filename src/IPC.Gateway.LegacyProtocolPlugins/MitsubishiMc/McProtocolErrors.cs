using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.MitsubishiMc
{
    public static class McProtocolErrors
    {
        public static PlcProtocolException EndCode(ushort code, string protocol = "MC")
        {
            return new PlcProtocolException(
                Classify(code),
                $"{protocol} request failed with end code 0x{code:X4}.",
                $"{protocol}-{code:X4}");
        }

        public static PlcProtocolException Mc1E(byte status, ushort detail = 0)
        {
            PlcReadFailureScope scope = status == 0x5B
                ? PlcReadFailureScope.Tag
                : PlcReadFailureScope.Device;
            string code = detail == 0 ? $"{status:X2}" : $"{status:X2}-{detail:X4}";
            return new PlcProtocolException(
                scope,
                $"MC 1E request failed with response code 0x{code}.",
                "MC1E-" + code);
        }

        public static PlcProtocolException Frame(string message)
        {
            return new PlcProtocolException(
                PlcReadFailureScope.Session,
                message,
                "MC-FRAME");
        }

        private static PlcReadFailureScope Classify(ushort code)
        {
            if ((code >= 0xC051 && code <= 0xC05A) ||
                (code >= 0xC05C && code <= 0xC05F))
                return PlcReadFailureScope.Tag;
            if (code >= 0xC060 && code <= 0xC06F)
                return PlcReadFailureScope.Batch;
            return PlcReadFailureScope.Device;
        }
    }
}
