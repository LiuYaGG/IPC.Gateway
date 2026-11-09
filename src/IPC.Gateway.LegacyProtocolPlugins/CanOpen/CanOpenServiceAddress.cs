#nullable enable

using System;
using System.Globalization;

namespace IPC.Plc.Communication.CanOpen
{
    internal enum CanOpenServiceKind
    {
        Sdo,
        Tpdo,
        Rpdo,
        Heartbeat,
        Emergency,
        Nmt,
        Sync,
        Time
    }

    internal sealed class CanOpenServiceAddress
    {
        public CanOpenServiceKind Kind { get; private set; }
        public CanOpenObjectAddress? ObjectAddress { get; private set; }
        public int NodeId { get; private set; }
        public int PdoNumber { get; private set; }
        public int ByteOffset { get; private set; }
        public int? BitOffset { get; private set; }

        public static CanOpenServiceAddress Parse(string text, int defaultNodeId)
        {
            string address = (text ?? string.Empty).Trim();
            if (address.Equals("SYNC", StringComparison.OrdinalIgnoreCase))
                return new CanOpenServiceAddress { Kind = CanOpenServiceKind.Sync };
            if (address.Equals("TIME", StringComparison.OrdinalIgnoreCase))
                return new CanOpenServiceAddress { Kind = CanOpenServiceKind.Time };

            string[] parts = address.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length >= 1 && TryParsePdoName(parts[0], out CanOpenServiceKind pdoKind, out int pdoNumber))
            {
                if (parts.Length is < 2 or > 3)
                    throw new FormatException("PDO 地址应为 TPDO1:Node[:ByteOffset[.Bit]] 或 RPDO1:Node[:ByteOffset[.Bit]]。");
                int nodeId = ParseNode(parts[1]);
                ParseOffset(parts.Length == 3 ? parts[2] : "0", out int byteOffset, out int? bitOffset);
                return new CanOpenServiceAddress
                {
                    Kind = pdoKind,
                    PdoNumber = pdoNumber,
                    NodeId = nodeId,
                    ByteOffset = byteOffset,
                    BitOffset = bitOffset
                };
            }

            if (parts.Length == 2 &&
                (parts[0].Equals("Heartbeat", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("EMCY", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("NMT", StringComparison.OrdinalIgnoreCase)))
            {
                return new CanOpenServiceAddress
                {
                    Kind = parts[0].Equals("Heartbeat", StringComparison.OrdinalIgnoreCase)
                        ? CanOpenServiceKind.Heartbeat
                        : parts[0].Equals("EMCY", StringComparison.OrdinalIgnoreCase)
                            ? CanOpenServiceKind.Emergency
                            : CanOpenServiceKind.Nmt,
                    NodeId = ParseNode(parts[1])
                };
            }

            return new CanOpenServiceAddress
            {
                Kind = CanOpenServiceKind.Sdo,
                ObjectAddress = CanOpenObjectAddress.Parse(address, defaultNodeId)
            };
        }

        private static bool TryParsePdoName(string value, out CanOpenServiceKind kind, out int number)
        {
            kind = CanOpenServiceKind.Sdo;
            number = 0;
            string name = (value ?? string.Empty).Trim();
            if (name.Length != 5 || (!name.StartsWith("TPDO", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("RPDO", StringComparison.OrdinalIgnoreCase)))
                return false;
            if (!int.TryParse(name.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out number) || number is < 1 or > 4)
                throw new FormatException("CANopen PDO 编号必须在 1 到 4 之间。");
            kind = name.StartsWith("TPDO", StringComparison.OrdinalIgnoreCase) ? CanOpenServiceKind.Tpdo : CanOpenServiceKind.Rpdo;
            return true;
        }

        private static int ParseNode(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int nodeId) || nodeId is < 1 or > 127)
                throw new FormatException("CANopen Node ID 必须在 1 到 127 之间。");
            return nodeId;
        }

        private static void ParseOffset(string value, out int byteOffset, out int? bitOffset)
        {
            string[] parts = value.Split('.');
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out byteOffset) || byteOffset < 0 || byteOffset > 7)
                throw new FormatException("CANopen PDO 字节偏移必须在 0 到 7 之间。");
            bitOffset = null;
            if (parts.Length == 2)
            {
                if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int bit) || bit is < 0 or > 7)
                    throw new FormatException("CANopen PDO 位偏移必须在 0 到 7 之间。");
                bitOffset = bit;
            }
        }
    }
}
