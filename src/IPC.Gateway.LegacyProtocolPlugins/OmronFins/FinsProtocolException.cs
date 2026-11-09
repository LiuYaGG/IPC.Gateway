using System;

namespace IPC.Plc.Communication.OmronFins
{
    internal enum FinsErrorScope
    {
        Tag,
        Device,
        Transport
    }

    internal sealed class FinsProtocolException : Exception
    {
        public FinsProtocolException(ushort endCode, string message, FinsErrorScope scope)
            : base(message)
        {
            EndCode = endCode;
            Scope = scope;
        }

        public ushort EndCode { get; }
        public FinsErrorScope Scope { get; }
    }
}
