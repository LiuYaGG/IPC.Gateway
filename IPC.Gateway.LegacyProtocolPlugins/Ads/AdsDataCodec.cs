using System;
using System.Globalization;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Ads
{
    public static class AdsDataCodec
    {
        public static Type GetManagedType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return typeof(bool);
                case PlcDataType.Int8: return typeof(sbyte);
                case PlcDataType.UInt8: return typeof(byte);
                case PlcDataType.Int16: return typeof(short);
                case PlcDataType.UInt16: return typeof(ushort);
                case PlcDataType.Int32: return typeof(int);
                case PlcDataType.UInt32: return typeof(uint);
                case PlcDataType.Int64: return typeof(long);
                case PlcDataType.UInt64: return typeof(ulong);
                case PlcDataType.Float: return typeof(float);
                case PlcDataType.Double: return typeof(double);
                case PlcDataType.String: return typeof(string);
                case PlcDataType.BoolArray: return typeof(bool[]);
                case PlcDataType.Int8Array: return typeof(sbyte[]);
                case PlcDataType.UInt8Array: return typeof(byte[]);
                case PlcDataType.Int16Array: return typeof(short[]);
                case PlcDataType.UInt16Array: return typeof(ushort[]);
                case PlcDataType.Int32Array: return typeof(int[]);
                case PlcDataType.UInt32Array: return typeof(uint[]);
                case PlcDataType.Int64Array: return typeof(long[]);
                case PlcDataType.UInt64Array: return typeof(ulong[]);
                case PlcDataType.FloatArray: return typeof(float[]);
                case PlcDataType.DoubleArray: return typeof(double[]);
                default:
                    throw new NotSupportedException("ADS 不支持数据类型 " + dataType + "。");
            }
        }

        public static int[] GetMarshalArguments(PlcDataType dataType, int elementCount, int stringLength)
        {
            if (dataType == PlcDataType.String)
                return new[] { Math.Max(1, stringLength) };
            if (PlcDataTypeHelper.IsArray(dataType))
                return new[] { Math.Max(1, elementCount) };
            return Array.Empty<int>();
        }

        public static object ParseWriteValue(PlcDataType dataType, string valueText)
        {
            string text = valueText ?? string.Empty;
            switch (dataType)
            {
                case PlcDataType.Bool: return ParseBoolean(text);
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
                default:
                    throw new NotSupportedException("ADS 写入暂不接受数组文本，请写入单个数组元素地址。");
            }
        }

        private static bool ParseBoolean(string text)
        {
            if (bool.TryParse(text, out bool value))
                return value;
            if (text == "1") return true;
            if (text == "0") return false;
            throw new FormatException("布尔值应为 true、false、1 或 0。");
        }
    }
}
