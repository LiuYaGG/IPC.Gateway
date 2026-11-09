using System;
using System.Globalization;
using System.IO;

namespace IPC.Plc.Communication.Cip
{
    /// <summary>
    /// 表示通用 EtherNet/IP CIP 对象地址。
    /// 格式为 @Class/Instance/Attribute[/Member]，每段支持十进制或 0x 十六进制。
    /// </summary>
    public sealed class CipExplicitAddress
    {
        private CipExplicitAddress(uint classId, uint instanceId, uint attributeId, uint? memberId)
        {
            ClassId = classId;
            InstanceId = instanceId;
            AttributeId = attributeId;
            MemberId = memberId;
        }

        public uint ClassId { get; }
        public uint InstanceId { get; }
        public uint AttributeId { get; }
        public uint? MemberId { get; }

        public static bool IsExplicit(string address)
        {
            return !string.IsNullOrWhiteSpace(address) && address.TrimStart().StartsWith("@", StringComparison.Ordinal);
        }

        public static CipExplicitAddress Parse(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            if (!IsExplicit(normalized))
                throw new FormatException("CIP 对象地址必须以 @ 开头。");

            string[] parts = normalized.Substring(1).Split('/');
            if (parts.Length < 3 || parts.Length > 4)
                throw new FormatException("CIP 对象地址格式应为 @Class/Instance/Attribute[/Member]。");

            uint classId = ParseId(parts[0], "Class");
            uint instanceId = ParseId(parts[1], "Instance");
            uint attributeId = ParseId(parts[2], "Attribute");
            uint? memberId = parts.Length == 4 ? ParseId(parts[3], "Member") : null;
            return new CipExplicitAddress(classId, instanceId, attributeId, memberId);
        }

        public byte[] EncodePath()
        {
            using MemoryStream stream = new MemoryStream();
            WriteLogicalSegment(stream, 0x20, 0x21, 0x22, ClassId);
            WriteLogicalSegment(stream, 0x24, 0x25, 0x26, InstanceId);
            WriteLogicalSegment(stream, 0x30, 0x31, 0x32, AttributeId);
            if (MemberId.HasValue)
                WriteLogicalSegment(stream, 0x28, 0x29, 0x2A, MemberId.Value);
            if ((stream.Length & 1) != 0)
                stream.WriteByte(0);
            return stream.ToArray();
        }

        public override string ToString()
        {
            string value = "@" + ClassId.ToString(CultureInfo.InvariantCulture) +
                           "/" + InstanceId.ToString(CultureInfo.InvariantCulture) +
                           "/" + AttributeId.ToString(CultureInfo.InvariantCulture);
            return MemberId.HasValue
                ? value + "/" + MemberId.Value.ToString(CultureInfo.InvariantCulture)
                : value;
        }

        private static uint ParseId(string text, string name)
        {
            string normalized = (text ?? string.Empty).Trim();
            NumberStyles style = NumberStyles.None;
            if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(2);
                style = NumberStyles.AllowHexSpecifier;
            }

            if (normalized.Length == 0 ||
                !uint.TryParse(normalized, style, CultureInfo.InvariantCulture, out uint value))
                throw new FormatException("CIP " + name + " 标识无效：" + text);
            return value;
        }

        private static void WriteLogicalSegment(
            Stream stream,
            byte eightBitCode,
            byte sixteenBitCode,
            byte thirtyTwoBitCode,
            uint value)
        {
            if (value <= byte.MaxValue)
            {
                stream.WriteByte(eightBitCode);
                stream.WriteByte((byte)value);
                return;
            }

            if (value <= ushort.MaxValue)
            {
                stream.WriteByte(sixteenBitCode);
                stream.WriteByte(0);
                CipPath.WriteUInt16(stream, (ushort)value);
                return;
            }

            stream.WriteByte(thirtyTwoBitCode);
            stream.WriteByte(0);
            CipPath.WriteUInt32(stream, value);
        }
    }
}
