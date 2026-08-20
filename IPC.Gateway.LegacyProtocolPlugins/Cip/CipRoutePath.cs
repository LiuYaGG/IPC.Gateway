using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace IPC.Plc.Communication.Cip
{
    internal static class CipRoutePath
    {
        public static byte[] Build(CipDriverOptions options, int slot, bool directByDefault = false)
        {
            if (directByDefault && !options.RouteModeSpecified)
                return Array.Empty<byte>();
            string mode = (options.RouteMode ?? string.Empty).Trim();
            if (mode.Equals("Direct", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<byte>();
            if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                return Parse(options.RoutePath);
            if (slot < 0 || slot > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(slot), "CIP Slot必须在0到255之间。");
            return new[] { (byte)0x01, (byte)slot };
        }

        public static byte[] Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("自定义CIP路由路径不能为空。");

            using MemoryStream stream = new MemoryStream();
            string[] hops = text.Split(new[] { '/', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string hopText in hops)
                WriteHop(stream, hopText.Trim());

            if ((stream.Length & 1) != 0)
                stream.WriteByte(0);
            if (stream.Length == 0 || stream.Length / 2 > byte.MaxValue)
                throw new FormatException("CIP路由路径长度无效。");
            return stream.ToArray();
        }

        private static void WriteHop(Stream stream, string hopText)
        {
            int separator = hopText.IndexOf(',');
            if (separator <= 0 || separator == hopText.Length - 1)
                throw new FormatException("CIP路由跳点应使用“端口,链路地址”，多个跳点用“/”分隔。");

            if (!byte.TryParse(hopText.Substring(0, separator).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out byte port) || port == 0 || port > 14)
                throw new FormatException("CIP路由端口目前支持1到14。");

            string linkText = hopText.Substring(separator + 1).Trim();
            if (byte.TryParse(linkText, NumberStyles.None, CultureInfo.InvariantCulture, out byte numericLink))
            {
                stream.WriteByte(port);
                stream.WriteByte(numericLink);
                return;
            }

            byte[] linkBytes = Encoding.ASCII.GetBytes(linkText);
            if (linkBytes.Length == 0 || linkBytes.Length > byte.MaxValue)
                throw new FormatException("CIP链路地址长度无效。");
            stream.WriteByte((byte)(0x10 | port));
            stream.WriteByte((byte)linkBytes.Length);
            stream.Write(linkBytes, 0, linkBytes.Length);
            if ((linkBytes.Length & 1) != 0)
                stream.WriteByte(0);
        }
    }
}
