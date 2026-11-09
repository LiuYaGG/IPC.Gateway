using System;
using System.Globalization;
using IPC.Plc.Communication.Core;
using Lextm.SharpSnmpLib;

namespace IPC.Plc.Communication.Snmp
{
    public static class SnmpDataCodec
    {
        public static object Decode(ISnmpData data, PlcDataType dataType)
        {
            if (data == null || data.TypeCode == SnmpType.NoSuchInstance || data.TypeCode == SnmpType.NoSuchObject || data.TypeCode == SnmpType.EndOfMibView)
                throw new SnmpTagException("SNMP 代理返回 OID 不存在。");

            string text = data.ToString();
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return text == "1" || bool.TryParse(text, out bool boolean) && boolean;
                case PlcDataType.Int8: return sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.UInt8: return byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.Int16: return short.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.UInt16: return ushort.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.Int32: return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.UInt32: return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.Int64: return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.UInt64: return ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.Float: return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                case PlcDataType.Double: return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                case PlcDataType.String: return text;
                default: throw new NotSupportedException("SNMP 不支持数据类型 " + dataType + "。");
            }
        }

        public static ISnmpData Encode(PlcDataType dataType, string valueText)
        {
            string text = valueText ?? string.Empty;
            switch (dataType)
            {
                case PlcDataType.Bool: return new Integer32(ParseBoolean(text) ? 1 : 0);
                case PlcDataType.Int8:
                case PlcDataType.Int16:
                case PlcDataType.Int32:
                    return new Integer32(int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
                case PlcDataType.UInt8:
                case PlcDataType.UInt16:
                case PlcDataType.UInt32:
                    return new Gauge32(uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
                case PlcDataType.UInt64:
                    return new Counter64(ulong.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture));
                case PlcDataType.String:
                    return new OctetString(text);
                default:
                    throw new NotSupportedException("SNMP SET 支持 Bool、32 位整数、UInt64 和 String。");
            }
        }

        private static bool ParseBoolean(string text)
        {
            if (bool.TryParse(text, out bool value)) return value;
            if (text == "1") return true;
            if (text == "0") return false;
            throw new FormatException("布尔值应为 true、false、1 或 0。");
        }
    }

    internal sealed class SnmpTagException : Exception
    {
        public SnmpTagException(string message) : base(message) { }
    }
}
