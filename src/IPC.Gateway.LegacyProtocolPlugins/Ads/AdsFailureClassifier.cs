using System;
using IPC.Plc.Communication.Core;
using TwinCAT.Ads;

namespace IPC.Plc.Communication.Ads
{
    internal static class AdsFailureClassifier
    {
        public static Exception Create(AdsErrorCode errorCode, string operation)
        {
            string message = operation + "失败：" + errorCode + " (0x" + ((uint)errorCode).ToString("X") + ")。";
            string name = errorCode.ToString();
            if (name.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("InvalidSize", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("InvalidOffset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("NotSupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return new AdsTagException(message);

            return new PlcCommunicationException(message);
        }
    }

    internal sealed class AdsTagException : Exception
    {
        public AdsTagException(string message) : base(message)
        {
        }
    }
}
