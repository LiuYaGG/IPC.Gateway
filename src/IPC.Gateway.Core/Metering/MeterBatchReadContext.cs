using System;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering
{
    internal sealed class MeterBatchReadContext<TAddress>
    {
        public Func<string, TAddress> ParseAddress { get; set; } = _ => throw new InvalidOperationException("Meter address parser is not configured.");
        public Func<TAddress, string> GetMeterKey { get; set; } = _ => string.Empty;
        public Func<TAddress, string> GetAddressKey { get; set; } = _ => string.Empty;
        public Func<TAddress, byte[]> ReadRawData { get; set; } = _ => throw new InvalidOperationException("Meter raw reader is not configured.");
        public Func<TAddress, byte[], PlcDataType, object> DecodeValue { get; set; } = (_, _, _) => throw new InvalidOperationException("Meter decoder is not configured.");
        public ushort TypeCode { get; set; }
        public string TypeName { get; set; } = string.Empty;
    }

    internal sealed class MeterAsyncBatchReadContext<TAddress>
    {
        public Func<string, TAddress> ParseAddress { get; set; } = _ => throw new InvalidOperationException("Meter address parser is not configured.");
        public Func<TAddress, string> GetMeterKey { get; set; } = _ => string.Empty;
        public Func<TAddress, string> GetAddressKey { get; set; } = _ => string.Empty;
        public Func<TAddress, CancellationToken, ValueTask<byte[]>> ReadRawDataAsync { get; set; } = (_, _) => throw new InvalidOperationException("Meter raw reader is not configured.");
        public Func<TAddress, byte[], PlcDataType, object> DecodeValue { get; set; } = (_, _, _) => throw new InvalidOperationException("Meter decoder is not configured.");
        public ushort TypeCode { get; set; }
        public string TypeName { get; set; } = string.Empty;
    }
}
