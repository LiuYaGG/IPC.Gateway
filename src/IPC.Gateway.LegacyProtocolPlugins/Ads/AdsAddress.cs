using System;
using System.Text.RegularExpressions;

namespace IPC.Plc.Communication.Ads
{
    public static class AdsAddress
    {
        private static readonly Regex SymbolPattern = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*|\[[0-9]+(?:,[0-9]+)*\])*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Parse(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            if (!SymbolPattern.IsMatch(normalized))
                throw new FormatException("ADS 标签地址应为 TwinCAT 符号路径，例如 MAIN.Counter 或 MAIN.Values[0]。");
            return normalized;
        }

        public static string WithElementOffset(string address, int elementOffset)
        {
            string normalized = Parse(address);
            if (elementOffset <= 0)
                return normalized;
            Match indexed = Regex.Match(normalized, @"\[([0-9]+)\]$");
            if (indexed.Success)
            {
                int start = int.Parse(indexed.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int absolute = checked(start + elementOffset);
                return normalized.Substring(0, indexed.Index) + "[" + absolute + "]";
            }
            if (normalized.EndsWith("]", StringComparison.Ordinal))
                throw new NotSupportedException("多维 ADS 数组地址不能再叠加元素偏移，请填写完整下标。");
            return normalized + "[" + elementOffset + "]";
        }
    }
}
