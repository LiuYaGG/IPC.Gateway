using System;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Cip
{
    internal sealed class CipBatchReadContext
    {
        public Func<string, PlcDataType, int, byte[]> BuildReadRequest { get; set; }
        public Func<byte[], byte[]> SendConnectedMessage { get; set; }
        public Func<byte[], string, PlcDataType, int, int, PlcReadResult> DecodeReadResponse { get; set; }
        public Func<string, PlcDataType, int, int, PlcReadResult> ReadTag { get; set; }
        public int MaxRequestBytes { get; set; }
        public int MaxServicesPerPacket { get; set; }
        public bool UseNativeBoolArrays { get; set; }
    }

    internal sealed class CipAsyncBatchReadContext
    {
        public Func<string, PlcDataType, int, byte[]> BuildReadRequest { get; set; }
        public Func<byte[], CancellationToken, ValueTask<byte[]>> SendConnectedMessageAsync { get; set; }
        public Func<byte[], string, PlcDataType, int, int, PlcReadResult> DecodeReadResponse { get; set; }
        public int MaxRequestBytes { get; set; }
        public int MaxServicesPerPacket { get; set; }
        public bool UseNativeBoolArrays { get; set; }
    }
}
