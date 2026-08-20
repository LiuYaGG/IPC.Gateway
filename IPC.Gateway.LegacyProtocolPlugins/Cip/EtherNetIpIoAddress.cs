using System;
using System.Globalization;

namespace IPC.Plc.Communication.Cip
{
    internal enum EtherNetIpIoDirection
    {
        Input,
        Output
    }

    internal readonly struct EtherNetIpIoAddress
    {
        private EtherNetIpIoAddress(EtherNetIpIoDirection direction, int byteOffset, int? bitOffset)
        {
            Direction = direction;
            ByteOffset = byteOffset;
            BitOffset = bitOffset;
        }

        public EtherNetIpIoDirection Direction { get; }
        public int ByteOffset { get; }
        public int? BitOffset { get; }

        public static bool IsIoAddress(string address)
        {
            string text = (address ?? string.Empty).Trim();
            return text.StartsWith("Input:", StringComparison.OrdinalIgnoreCase) ||
                   text.StartsWith("Output:", StringComparison.OrdinalIgnoreCase);
        }

        public static EtherNetIpIoAddress Parse(string address)
        {
            string text = (address ?? string.Empty).Trim();
            int separator = text.IndexOf(':');
            if (separator <= 0 || separator == text.Length - 1)
                throw new FormatException("周期 I/O 地址应为 Input:字节偏移[.位] 或 Output:字节偏移[.位]。");

            EtherNetIpIoDirection direction = text.Substring(0, separator).Equals("Input", StringComparison.OrdinalIgnoreCase)
                ? EtherNetIpIoDirection.Input
                : text.Substring(0, separator).Equals("Output", StringComparison.OrdinalIgnoreCase)
                    ? EtherNetIpIoDirection.Output
                    : throw new FormatException("周期 I/O 方向只能是 Input 或 Output。");

            string[] offsetParts = text.Substring(separator + 1).Split('.');
            if (offsetParts.Length is < 1 or > 2 ||
                !int.TryParse(offsetParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int byteOffset) ||
                byteOffset < 0)
                throw new FormatException("周期 I/O 字节偏移无效。");

            int? bitOffset = null;
            if (offsetParts.Length == 2)
            {
                if (!int.TryParse(offsetParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int bit) || bit is < 0 or > 7)
                    throw new FormatException("周期 I/O 位偏移必须在 0 到 7 之间。");
                bitOffset = bit;
            }

            return new EtherNetIpIoAddress(direction, byteOffset, bitOffset);
        }
    }
}
