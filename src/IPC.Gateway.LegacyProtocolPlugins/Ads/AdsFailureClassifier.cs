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
            if (errorCode == AdsErrorCode.DeviceInvalidArrayIndex ||
                errorCode == AdsErrorCode.DeviceInvalidGroup ||
                errorCode == AdsErrorCode.DeviceInvalidOffset ||
                errorCode == AdsErrorCode.DeviceInvalidAccess ||
                errorCode == AdsErrorCode.DeviceInvalidSize ||
                errorCode == AdsErrorCode.DeviceInvalidData ||
                errorCode == AdsErrorCode.DeviceInvalidParam ||
                errorCode == AdsErrorCode.DeviceNotFound ||
                name.IndexOf("Symbol", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("NotSupported", StringComparison.OrdinalIgnoreCase) >= 0)
                return new AdsTagException(message);

            bool transport = name.IndexOf("TimeOut", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("TargetPortNotFound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("TargetMachineNotFound", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("NotConnected", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Tcp", StringComparison.OrdinalIgnoreCase) >= 0;
            return new PlcProtocolException(transport ? PlcReadFailureScope.Transport : PlcReadFailureScope.Device,
                message, "ADS-" + ((uint)errorCode).ToString("X"));
        }
    }

    internal sealed class AdsTagException : PlcProtocolException
    {
        public AdsTagException(string message) : base(PlcReadFailureScope.Tag, message)
        {
        }
    }
}
