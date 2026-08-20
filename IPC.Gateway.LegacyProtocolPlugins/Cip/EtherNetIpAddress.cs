using System;
using System.Globalization;

namespace IPC.Plc.Communication.Cip
{
    /// <summary>
    /// EtherNet/IP 通用对象地址。标准形式为 @Class/Instance/Attribute[/Member]；
    /// Assembly:Instance[:Member] 是 Assembly Object（Class 0x04、Attribute 3）的便捷写法。
    /// </summary>
    public static class EtherNetIpAddress
    {
        public static string Normalize(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            if (CipExplicitAddress.IsExplicit(normalized))
                return CipExplicitAddress.Parse(normalized).ToString();

            string[] parts = normalized.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is 2 or 3 &&
                (parts[0].Equals("Assembly", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("InputAssembly", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("OutputAssembly", StringComparison.OrdinalIgnoreCase) ||
                 parts[0].Equals("ConfigAssembly", StringComparison.OrdinalIgnoreCase)))
            {
                uint instance = ParseId(parts[1], "Assembly Instance");
                string result = "@4/" + instance.ToString(CultureInfo.InvariantCulture) + "/3";
                if (parts.Length == 3)
                    result += "/" + ParseId(parts[2], "Assembly Member").ToString(CultureInfo.InvariantCulture);
                return result;
            }

            throw new FormatException(
                "EtherNet/IP 地址应为 @Class/Instance/Attribute[/Member] 或 Assembly:Instance[:Member]。");
        }

        private static uint ParseId(string text, string name)
        {
            string token = (text ?? string.Empty).Trim();
            NumberStyles style = NumberStyles.None;
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                token = token.Substring(2);
                style = NumberStyles.AllowHexSpecifier;
            }

            if (token.Length == 0 || !uint.TryParse(token, style, CultureInfo.InvariantCulture, out uint value))
                throw new FormatException(name + " 无效：" + text);
            return value;
        }
    }
}
