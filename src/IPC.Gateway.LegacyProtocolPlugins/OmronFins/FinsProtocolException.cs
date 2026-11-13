using System;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.OmronFins
{
    internal enum FinsErrorScope
    {
        Tag,
        Device,
        Transport
    }

    internal sealed class FinsProtocolException : PlcProtocolException
    {
        public FinsProtocolException(ushort endCode, string message, FinsErrorScope scope)
            : base(scope == FinsErrorScope.Tag ? PlcReadFailureScope.Tag :
                scope == FinsErrorScope.Device ? PlcReadFailureScope.Device : PlcReadFailureScope.Transport,
                message, "FINS-" + endCode.ToString("X4"))
        {
            EndCode = endCode;
            Scope = scope;
        }

        public ushort EndCode { get; }
        public FinsErrorScope Scope { get; }
    }
}
