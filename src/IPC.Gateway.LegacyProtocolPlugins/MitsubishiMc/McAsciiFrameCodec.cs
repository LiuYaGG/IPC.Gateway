using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace IPC.Plc.Communication.MitsubishiMc
{
    internal sealed class McAsciiFrameCodec : IMcFrameCodec
    {
        private readonly McDriverOptions _options;
        private readonly bool _frame4E;
        private int _serial;

        public McAsciiFrameCodec(McDriverOptions options, bool frame4E)
        {
            _options = options;
            _frame4E = frame4E;
        }

        public int ResponseHeaderLength => _frame4E ? 26 : 18;

        public byte[] BuildRequest(ushort command, ushort subcommand, McAddress address, int points, byte[] data)
        {
            string payloadData = BuildPayloadData(subcommand, points, data);
            string body = "0010" + command.ToString("X4") + subcommand.ToString("X4") +
                          FormatDeviceName(address.DeviceName) + FormatDeviceNumber(address) +
                          checked((ushort)points).ToString("X4") + payloadData;

            StringBuilder frame = new StringBuilder();
            frame.Append(_frame4E ? "5400" : "5000");
            if (_frame4E)
            {
                frame.Append(unchecked((ushort)Interlocked.Increment(ref _serial)).ToString("X4"));
                frame.Append("0000");
            }
            frame.Append(_options.NetworkNumber.ToString("X2"));
            frame.Append(_options.PcNumber.ToString("X2"));
            frame.Append(_options.ModuleIoNumber.ToString("X4"));
            frame.Append(_options.StationNumber.ToString("X2"));
            frame.Append(body.Length.ToString("X4"));
            frame.Append(body);
            return Encoding.ASCII.GetBytes(frame.ToString());
        }

        public int GetResponseDataLength(byte[] header)
        {
            int offset = _frame4E ? 22 : 14;
            string text = Encoding.ASCII.GetString(header, offset, 4);
            return int.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public byte[] ParseResponse(byte[] response, byte[] request)
        {
            int headerLength = ResponseHeaderLength;
            if (response == null || response.Length < headerLength + 4)
                throw McProtocolErrors.Frame("MC ASCII response is too short.");
            string expected = _frame4E ? "D400" : "D000";
            if (!Encoding.ASCII.GetString(response, 0, 4).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw McProtocolErrors.Frame("MC ASCII response frame type is invalid.");
            if (_frame4E && !AsciiEquals(response, 4, request, 4, 4))
                throw McProtocolErrors.Frame("MC 4E ASCII response serial number does not match the request.");

            int dataLength = GetResponseDataLength(response);
            if (dataLength < 4 || response.Length < headerLength + dataLength)
                throw McProtocolErrors.Frame("MC ASCII response data length is invalid.");
            ushort endCode = ParseHexUInt16(response, headerLength);
            if (endCode != 0)
                throw McProtocolErrors.EndCode(endCode, "MC-ASCII");
            if (false && endCode != 0)
                throw new InvalidOperationException("MC ASCII请求失败：0x" + endCode.ToString("X4"));

            int payloadOffset = headerLength + 4;
            int payloadLength = dataLength - 4;
            if (IsWriteRequest(request) || payloadLength == 0)
                return Array.Empty<byte>();
            return IsBitRequest(request)
                ? DecodeBitPayload(response, payloadOffset, payloadLength)
                : DecodeWordPayload(response, payloadOffset, payloadLength);
        }

        private static string BuildPayloadData(ushort subcommand, int points, byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;
            if (subcommand == 0x0001)
            {
                StringBuilder bits = new StringBuilder(points);
                for (int i = 0; i < points; i++)
                {
                    byte packed = data[i / 2];
                    bool value = (i & 1) == 0 ? (packed & 0x10) != 0 : (packed & 0x01) != 0;
                    bits.Append(value ? '1' : '0');
                }
                return bits.ToString();
            }

            if ((data.Length & 1) != 0)
                throw new ArgumentException("MC ASCII字写入数据必须按字对齐。", nameof(data));
            StringBuilder words = new StringBuilder(data.Length * 2);
            for (int i = 0; i < data.Length; i += 2)
                words.Append(((ushort)(data[i] | (data[i + 1] << 8))).ToString("X4"));
            return words.ToString();
        }

        private static byte[] DecodeBitPayload(byte[] response, int offset, int length)
        {
            byte[] packed = new byte[(length + 1) / 2];
            for (int i = 0; i < length; i++)
            {
                byte value = response[offset + i];
                if (value != (byte)'0' && value != (byte)'1')
                    throw new InvalidOperationException("MC ASCII位响应包含无效字符。");
                if (value == (byte)'1')
                    packed[i / 2] |= (i & 1) == 0 ? (byte)0x10 : (byte)0x01;
            }
            return packed;
        }

        private static byte[] DecodeWordPayload(byte[] response, int offset, int length)
        {
            if ((length % 4) != 0)
                throw new InvalidOperationException("MC ASCII字响应长度不是4的倍数。");
            byte[] data = new byte[(length / 4) * 2];
            for (int i = 0; i < length / 4; i++)
            {
                ushort word = ParseHexUInt16(response, offset + i * 4);
                data[i * 2] = (byte)word;
                data[i * 2 + 1] = (byte)(word >> 8);
            }
            return data;
        }

        private bool IsBitRequest(byte[] request)
        {
            int offset = ResponseHeaderLength + 8;
            return Encoding.ASCII.GetString(request, offset, 4).Equals("0001", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsWriteRequest(byte[] request)
        {
            int offset = ResponseHeaderLength + 4;
            return Encoding.ASCII.GetString(request, offset, 4).Equals("1401", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatDeviceName(string name)
        {
            return name.Length == 1 ? name + "*" : name;
        }

        private static string FormatDeviceNumber(McAddress address)
        {
            return address.DeviceNumber.ToString(address.UsesHexAddress ? "X6" : "D6", CultureInfo.InvariantCulture);
        }

        private static ushort ParseHexUInt16(byte[] data, int offset)
        {
            string text = Encoding.ASCII.GetString(data, offset, 4);
            return ushort.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static bool AsciiEquals(byte[] left, int leftOffset, byte[] right, int rightOffset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (char.ToUpperInvariant((char)left[leftOffset + i]) != char.ToUpperInvariant((char)right[rightOffset + i]))
                    return false;
            }
            return true;
        }
    }
}
