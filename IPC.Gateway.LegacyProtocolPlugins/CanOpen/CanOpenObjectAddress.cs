using System;
using System.Globalization;
using System.Linq;

namespace IPC.Plc.Communication.CanOpen
{
    internal sealed class CanOpenObjectAddress
    {
        public CanOpenObjectAddress(int nodeId, ushort index, byte subIndex)
        {
            if (nodeId < 1 || nodeId > 127)
                throw new ArgumentOutOfRangeException("nodeId");

            NodeId = nodeId;
            Index = index;
            SubIndex = subIndex;
        }

        public int NodeId { get; private set; }
        public ushort Index { get; private set; }
        public byte SubIndex { get; private set; }

        public CanOpenObjectAddress AddSubIndexOffset(int offset)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");

            int subIndex = SubIndex + offset;
            if (subIndex > byte.MaxValue)
                throw new ArgumentOutOfRangeException("offset");

            return new CanOpenObjectAddress(NodeId, Index, (byte)subIndex);
        }

        public static CanOpenObjectAddress Parse(string address, int defaultNodeId)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new FormatException("CANopen object address cannot be empty.");

            string[] parts = address.Split(new[] { ':', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3)
                throw new FormatException("CANopen address must be index:subIndex or nodeId:index:subIndex.");

            int nodeId;
            string indexToken;
            string subIndexToken;
            if (parts.Length == 2)
            {
                nodeId = defaultNodeId;
                indexToken = parts[0];
                subIndexToken = parts[1];
            }
            else
            {
                nodeId = ParseNumber(parts[0], preferHexForFourDigits: false);
                indexToken = parts[1];
                subIndexToken = parts[2];
            }

            ushort index = checked((ushort)ParseNumber(indexToken, preferHexForFourDigits: true));
            byte subIndex = checked((byte)ParseNumber(subIndexToken, preferHexForFourDigits: false));
            return new CanOpenObjectAddress(nodeId, index, subIndex);
        }

        private static int ParseNumber(string value, bool preferHexForFourDigits)
        {
            string token = (value ?? string.Empty).Trim();
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(token.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            if (token.Any(c => (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')) ||
                (preferHexForFourDigits && token.Length == 4))
                return int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
    }
}
