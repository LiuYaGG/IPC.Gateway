using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IPC.Plc.Communication.Pccc
{
    internal sealed class PcccAddress
    {
        private static readonly Regex AddressPattern = new Regex(
            @"^(?<type>ST|[NBFLOTICSR])(?<file>\d+):(?<element>\d+)(?:\.(?<member>[A-Z]+|\d+))?(?:/(?<bit>\d+))?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public string FileTypeName { get; private set; } = string.Empty;
        public byte FileTypeCode { get; private set; }
        public int FileNumber { get; private set; }
        public int ElementNumber { get; private set; }
        public int SubElement { get; private set; }
        public int? BitNumber { get; private set; }
        public int NativeElementSize { get; private set; }

        public PcccAddress AddElementOffset(int offset)
        {
            return new PcccAddress
            {
                FileTypeName = FileTypeName,
                FileTypeCode = FileTypeCode,
                FileNumber = FileNumber,
                ElementNumber = checked(ElementNumber + offset),
                SubElement = SubElement,
                BitNumber = BitNumber,
                NativeElementSize = NativeElementSize
            };
        }

        public static PcccAddress Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("PCCC地址不能为空。", nameof(text));

            Match match = AddressPattern.Match(text.Trim());
            if (!match.Success)
                throw new FormatException("PCCC地址格式无效，应类似N7:0、B3:0/1或T4:0.ACC。");

            string type = match.Groups["type"].Value.ToUpperInvariant();
            int file = ParseNonNegative(match.Groups["file"].Value, "文件号");
            int element = ParseNonNegative(match.Groups["element"].Value, "元素号");
            string member = match.Groups["member"].Value;
            int subElement = ParseSubElement(type, member);
            int? bit = match.Groups["bit"].Success
                ? ParseNonNegative(match.Groups["bit"].Value, "位号")
                : ParseMemberBit(type, member);
            if (bit > 15)
                throw new FormatException("PCCC位号必须在0到15之间。");

            GetFileType(type, out byte code, out int nativeSize);
            return new PcccAddress
            {
                FileTypeName = type,
                FileTypeCode = code,
                FileNumber = file,
                ElementNumber = element,
                SubElement = subElement,
                BitNumber = bit,
                NativeElementSize = nativeSize
            };
        }

        private static int ParseSubElement(string type, string member)
        {
            if (string.IsNullOrEmpty(member))
                return 0;
            if (int.TryParse(member, NumberStyles.None, CultureInfo.InvariantCulture, out int numeric) && numeric >= 0)
                return numeric;

            string normalized = member.ToUpperInvariant();
            if (type == "T" || type == "C")
            {
                if (normalized == "PRE") return 1;
                if (normalized == "ACC") return 2;
                if (normalized == "EN" || normalized == "CU" ||
                    normalized == "TT" || normalized == "CD" ||
                    normalized == "DN" || normalized == "OV" || normalized == "UN") return 0;
            }
            throw new FormatException("不支持的PCCC结构成员：" + member);
        }

        private static int? ParseMemberBit(string type, string member)
        {
            if (type != "T" && type != "C")
                return null;
            switch ((member ?? string.Empty).ToUpperInvariant())
            {
                case "EN":
                case "CU": return 15;
                case "TT":
                case "CD": return 14;
                case "DN": return 13;
                case "OV": return 12;
                case "UN": return 11;
                default: return null;
            }
        }

        private static void GetFileType(string type, out byte code, out int nativeSize)
        {
            switch (type)
            {
                case "S": code = 0x84; nativeSize = 2; return;
                case "B": code = 0x85; nativeSize = 2; return;
                case "T": code = 0x86; nativeSize = 2; return;
                case "C": code = 0x87; nativeSize = 2; return;
                case "R": code = 0x88; nativeSize = 2; return;
                case "N": code = 0x89; nativeSize = 2; return;
                case "F": code = 0x8A; nativeSize = 4; return;
                case "O": code = 0x8B; nativeSize = 2; return;
                case "I": code = 0x8C; nativeSize = 2; return;
                case "ST": code = 0x8D; nativeSize = 84; return;
                case "L": code = 0x91; nativeSize = 4; return;
                default: throw new FormatException("不支持的PCCC文件类型：" + type);
            }
        }

        private static int ParseNonNegative(string text, string field)
        {
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0 || value > ushort.MaxValue)
                throw new FormatException(field + "必须在0到65535之间。");
            return value;
        }
    }
}
