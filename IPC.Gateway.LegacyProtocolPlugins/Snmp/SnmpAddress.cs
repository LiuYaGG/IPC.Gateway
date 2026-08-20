using System;
using System.Text.RegularExpressions;

namespace IPC.Plc.Communication.Snmp
{
    public static class SnmpAddress
    {
        private static readonly Regex OidPattern = new Regex(@"^\.?[0-9]+(?:\.[0-9]+)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Parse(string address)
        {
            string normalized = (address ?? string.Empty).Trim();
            if (!OidPattern.IsMatch(normalized))
                throw new FormatException("SNMP 标签地址应为数字 OID，例如 1.3.6.1.2.1.1.3.0。");
            return normalized[0] == '.' ? normalized.Substring(1) : normalized;
        }
    }
}
