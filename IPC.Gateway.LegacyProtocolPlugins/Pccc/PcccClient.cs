using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Cip;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Pccc
{
    public sealed class PcccClient : IPlcClient, IPlcBatchReadClient, IAsyncPlcClient, IAsyncPlcBatchReadClient
    {
        private const byte ExecutePcccService = 0x4B;
        private const byte TypedReadFunction = 0xA2;
        private const byte TypedWriteFunction = 0xAA;
        private const int MaxPayloadBytes = 220;

        private readonly CipClient _transport;
        private ushort _transaction;

        public PcccClient(PlcConnectionOptions options)
        {
            _transport = new CipClient(options ?? throw new ArgumentNullException(nameof(options)));
            _transaction = 1;
        }

        public bool IsConnected => _transport.IsConnected;
        public PlcProtocol Protocol => PlcProtocol.RockwellPccc;

        public void Connect() => _transport.Connect();
        public ValueTask ConnectAsync(CancellationToken cancellationToken) => _transport.ConnectAsync(cancellationToken);
        public void Disconnect() => _transport.Disconnect();
        public ValueTask DisconnectAsync(CancellationToken cancellationToken) => _transport.DisconnectAsync(cancellationToken);

        public PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset)
        {
            PcccAddress parsed = PcccAddress.Parse(address).AddElementOffset(elementOffset);
            int byteCount = ValidateByteCount(parsed, dataType, elementCount);
            byte[] response = _transport.SendExplicitMessage(BuildReadRequest(parsed, byteCount));
            byte[] data = ParseResponse(response);
            object value = PcccDataCodec.Decode(parsed, dataType, data, elementCount);
            return new PlcReadResult(parsed.FileTypeCode, parsed.FileTypeName, value);
        }

        public async ValueTask<PlcReadResult> ReadAsync(
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            PcccAddress parsed = PcccAddress.Parse(address).AddElementOffset(elementOffset);
            int byteCount = ValidateByteCount(parsed, dataType, elementCount);
            byte[] response = await _transport.SendExplicitMessageAsync(BuildReadRequest(parsed, byteCount), cancellationToken).ConfigureAwait(false);
            byte[] data = ParseResponse(response);
            object value = PcccDataCodec.Decode(parsed, dataType, data, elementCount);
            return new PlcReadResult(parsed.FileTypeCode, parsed.FileTypeName, value);
        }

        public void Write(string address, PlcDataType dataType, string valueText, int elementOffset)
        {
            PcccAddress parsed = PcccAddress.Parse(address).AddElementOffset(elementOffset);
            byte[] data = BuildWriteData(parsed, dataType, valueText);
            ParseResponse(_transport.SendExplicitMessage(BuildWriteRequest(parsed, data)));
        }

        public async ValueTask WriteAsync(
            string address,
            PlcDataType dataType,
            string valueText,
            int elementOffset,
            CancellationToken cancellationToken)
        {
            PcccAddress parsed = PcccAddress.Parse(address).AddElementOffset(elementOffset);
            byte[] data = await BuildWriteDataAsync(parsed, dataType, valueText, cancellationToken).ConfigureAwait(false);
            byte[] response = await _transport.SendExplicitMessageAsync(BuildWriteRequest(parsed, data), cancellationToken).ConfigureAwait(false);
            ParseResponse(response);
        }

        public IList<PlcBatchReadResult> ReadMany(IList<PlcBatchReadRequest> requests)
        {
            return PcccBatchReadExecutor.ReadMany(requests, ReadRaw);
        }

        public async ValueTask<IList<PlcBatchReadResult>> ReadManyAsync(
            IList<PlcBatchReadRequest> requests,
            CancellationToken cancellationToken)
        {
            return await PcccBatchReadExecutor.ReadManyAsync(requests, ReadRawAsync, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose() => _transport.Dispose();

        private byte[] BuildWriteData(PcccAddress address, PlcDataType dataType, string valueText)
        {
            if (!address.BitNumber.HasValue)
                return PcccDataCodec.Encode(address, dataType, valueText);

            byte[] current = ParseResponse(_transport.SendExplicitMessage(BuildReadRequest(address, 2)));
            if (current.Length < 2)
                throw new InvalidOperationException("PCCC位写入前读取的数据长度不足。");
            ushort word = (ushort)(current[0] | (current[1] << 8));
            ushort mask = (ushort)(1 << address.BitNumber.Value);
            word = ParseBool(valueText) ? (ushort)(word | mask) : (ushort)(word & ~mask);
            return BitConverter.GetBytes(word);
        }

        private async ValueTask<byte[]> BuildWriteDataAsync(PcccAddress address, PlcDataType dataType, string valueText, CancellationToken cancellationToken)
        {
            if (!address.BitNumber.HasValue)
                return PcccDataCodec.Encode(address, dataType, valueText);

            byte[] response = await _transport.SendExplicitMessageAsync(BuildReadRequest(address, 2), cancellationToken).ConfigureAwait(false);
            byte[] current = ParseResponse(response);
            if (current.Length < 2)
                throw new InvalidOperationException("PCCC位写入前读取的数据长度不足。");
            ushort word = (ushort)(current[0] | (current[1] << 8));
            ushort mask = (ushort)(1 << address.BitNumber.Value);
            word = ParseBool(valueText) ? (ushort)(word | mask) : (ushort)(word & ~mask);
            return BitConverter.GetBytes(word);
        }

        private byte[] BuildReadRequest(PcccAddress address, int byteCount)
        {
            return BuildExecuteRequest(TypedReadFunction, address, byteCount, null);
        }

        private byte[] ReadRaw(PcccAddress address, int byteCount)
        {
            return ParseResponse(_transport.SendExplicitMessage(BuildReadRequest(address, byteCount)));
        }

        private async ValueTask<byte[]> ReadRawAsync(
            PcccAddress address,
            int byteCount,
            CancellationToken cancellationToken)
        {
            byte[] response = await _transport.SendExplicitMessageAsync(
                BuildReadRequest(address, byteCount),
                cancellationToken).ConfigureAwait(false);
            return ParseResponse(response);
        }

        private byte[] BuildWriteRequest(PcccAddress address, byte[] data)
        {
            if (data.Length > MaxPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(data), "PCCC单次写入最多220字节。");
            return BuildExecuteRequest(TypedWriteFunction, address, data.Length, data);
        }

        private byte[] BuildExecuteRequest(byte function, PcccAddress address, int byteCount, byte[] data)
        {
            ushort transaction = unchecked(++_transaction);
            using MemoryStream stream = new MemoryStream();
            stream.WriteByte(ExecutePcccService);
            stream.WriteByte(0x02);
            stream.WriteByte(0x20);
            stream.WriteByte(0x67);
            stream.WriteByte(0x24);
            stream.WriteByte(0x01);
            stream.WriteByte(0x07);
            WriteUInt16(stream, 0x1337);
            WriteUInt32(stream, 0x49504347);
            stream.WriteByte(0x0F);
            stream.WriteByte(0x00);
            WriteUInt16(stream, transaction);
            stream.WriteByte(function);
            stream.WriteByte((byte)byteCount);
            WriteLogicalAddress(stream, address.FileNumber);
            stream.WriteByte(address.FileTypeCode);
            WriteLogicalAddress(stream, address.ElementNumber);
            WriteLogicalAddress(stream, address.SubElement);
            if (data != null)
                stream.Write(data, 0, data.Length);
            return stream.ToArray();
        }

        private static byte[] ParseResponse(byte[] response)
        {
            if (response == null || response.Length < 4)
                throw new InvalidOperationException("PCCC的CIP响应过短。");
            if (response[0] != (ExecutePcccService | 0x80))
                throw new InvalidOperationException("PCCC的CIP响应服务码不匹配。");
            int offset = 4 + response[3] * 2;
            if (offset > response.Length)
                throw new InvalidOperationException("PCCC的CIP附加状态长度无效。");
            if (response[2] != 0)
                throw new PlcProtocolException(
                    ClassifyCipStatus(response[2]),
                    "PCCC CIP错误: 0x" + response[2].ToString("X2"),
                    "CIP-0x" + response[2].ToString("X2"));
            if (false)
                throw new InvalidOperationException("PCCC CIP错误：0x" + response[2].ToString("X2"));
            if (response.Length < offset + 4)
                throw new InvalidOperationException("PCCC响应头不完整。");
            byte pcccStatus = response[offset + 1];
            if (pcccStatus != 0)
                throw new PlcProtocolException(
                    pcccStatus == 0x50 ? PlcReadFailureScope.Tag : PlcReadFailureScope.Device,
                    "PCCC命令错误: 0x" + pcccStatus.ToString("X2"),
                    "PCCC-0x" + pcccStatus.ToString("X2"));
            if (false)
                throw new InvalidOperationException("PCCC命令错误：0x" + pcccStatus.ToString("X2"));
            int dataOffset = offset + 4;
            byte[] data = new byte[response.Length - dataOffset];
            Buffer.BlockCopy(response, dataOffset, data, 0, data.Length);
            return data;
        }

        private static PlcReadFailureScope ClassifyCipStatus(byte status)
        {
            switch (status)
            {
                case 0x13:
                case 0x14:
                case 0x15:
                case 0x16:
                    return PlcReadFailureScope.Tag;
                default:
                    return PlcReadFailureScope.Device;
            }
        }

        private static int ValidateByteCount(PcccAddress address, PlcDataType dataType, int elementCount)
        {
            if (elementCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementCount));
            int byteCount = PcccDataCodec.GetByteCount(address, dataType, elementCount);
            if (byteCount <= 0 || byteCount > MaxPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(elementCount), "PCCC单次读取最多220字节，请缩小元素数量。");
            return byteCount;
        }

        private static void WriteLogicalAddress(Stream stream, int value)
        {
            if (value < byte.MaxValue)
            {
                stream.WriteByte((byte)value);
                return;
            }
            stream.WriteByte(byte.MaxValue);
            WriteUInt16(stream, (ushort)value);
        }

        private static bool IsCommunicationFailure(Exception exception)
        {
            return exception is IOException || exception is SocketException || exception is TimeoutException;
        }

        private static bool ParseBool(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            return bool.Parse(value);
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }

        private static void WriteUInt32(Stream stream, uint value)
        {
            WriteUInt16(stream, (ushort)value);
            WriteUInt16(stream, (ushort)(value >> 16));
        }
    }
}
