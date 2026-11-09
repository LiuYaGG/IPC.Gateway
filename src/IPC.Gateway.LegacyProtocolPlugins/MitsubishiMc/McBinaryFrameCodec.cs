using System;
using System.IO;
using System.Threading;

namespace IPC.Plc.Communication.MitsubishiMc
{
    internal sealed class McBinaryFrameCodec : IMcFrameCodec
    {
        private readonly McDriverOptions _options;
        private readonly bool _frame4E;
        private int _serial;

        public McBinaryFrameCodec(McDriverOptions options, bool frame4E)
        {
            _options = options;
            _frame4E = frame4E;
        }

        public int ResponseHeaderLength => _frame4E ? 13 : 9;

        public byte[] BuildRequest(ushort command, ushort subcommand, McAddress address, int points, byte[] data)
        {
            using MemoryStream body = new MemoryStream();
            WriteUInt16(body, 0x0010);
            WriteUInt16(body, command);
            WriteUInt16(body, subcommand);
            WriteDeviceAddress(body, address);
            WriteUInt16(body, checked((ushort)points));
            if (data != null && data.Length > 0)
                body.Write(data, 0, data.Length);

            byte[] bodyBytes = body.ToArray();
            using MemoryStream frame = new MemoryStream();
            WriteUInt16(frame, _frame4E ? (ushort)0x0054 : (ushort)0x0050);
            if (_frame4E)
            {
                WriteUInt16(frame, unchecked((ushort)Interlocked.Increment(ref _serial)));
                WriteUInt16(frame, 0);
            }
            frame.WriteByte(_options.NetworkNumber);
            frame.WriteByte(_options.PcNumber);
            WriteUInt16(frame, _options.ModuleIoNumber);
            frame.WriteByte(_options.StationNumber);
            WriteUInt16(frame, checked((ushort)bodyBytes.Length));
            frame.Write(bodyBytes, 0, bodyBytes.Length);
            return frame.ToArray();
        }

        public int GetResponseDataLength(byte[] header)
        {
            int offset = _frame4E ? 11 : 7;
            if (header == null || header.Length < offset + 2)
                throw McProtocolErrors.Frame("MC response header is too short.");
            return ReadUInt16(header, offset);
        }

        public byte[] ParseResponse(byte[] response, byte[] request)
        {
            int headerLength = ResponseHeaderLength;
            if (response == null || response.Length < headerLength + 2)
                throw McProtocolErrors.Frame("MC response is too short.");
            ushort expectedSubheader = _frame4E ? (ushort)0x00D4 : (ushort)0x00D0;
            ushort actualSubheader = ReadUInt16(response, 0);
            if (actualSubheader != expectedSubheader)
                throw McProtocolErrors.Frame("MC response frame type is invalid: 0x" + actualSubheader.ToString("X4"));
            if (_frame4E && ReadUInt16(response, 2) != ReadUInt16(request, 2))
                throw McProtocolErrors.Frame("MC 4E response serial number does not match the request.");

            int dataLength = GetResponseDataLength(response);
            if (dataLength < 2 || response.Length < headerLength + dataLength)
                throw McProtocolErrors.Frame("MC response data length is invalid.");
            ushort endCode = ReadUInt16(response, headerLength);
            if (endCode != 0)
                throw McProtocolErrors.EndCode(endCode);
            if (false && endCode != 0)
                throw new InvalidOperationException("MC请求失败：0x" + endCode.ToString("X4"));

            byte[] result = new byte[dataLength - 2];
            Buffer.BlockCopy(response, headerLength + 2, result, 0, result.Length);
            return result;
        }

        private static void WriteDeviceAddress(Stream stream, McAddress address)
        {
            int number = address.DeviceNumber;
            stream.WriteByte((byte)number);
            stream.WriteByte((byte)(number >> 8));
            stream.WriteByte((byte)(number >> 16));
            stream.WriteByte(address.DeviceCode);
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }
    }
}
