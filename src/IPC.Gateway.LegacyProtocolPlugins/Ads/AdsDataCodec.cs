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
                case PlcDataType.Byte:
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
                case PlcDataType.ByteArray:
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

        public static byte[] EncodeWriteValue(object value)
        {
            if (value == null) throw new NotSupportedException("ADS 不支持写入 null 值。");
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                if (value is Array array)
                {
                    foreach (object element in array)
                        WriteValue(writer, element);
                }
                else
                {
                    WriteValue(writer, value);
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static void WriteValue(System.IO.BinaryWriter writer, object value)
        {
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.Boolean: writer.Write((bool)value ? (byte)1 : (byte)0); break;
                case TypeCode.SByte: writer.Write((sbyte)value); break;
                case TypeCode.Byte: writer.Write((byte)value); break;
                case TypeCode.Int16: writer.Write((short)value); break;
                case TypeCode.UInt16: writer.Write((ushort)value); break;
                case TypeCode.Int32: writer.Write((int)value); break;
                case TypeCode.UInt32: writer.Write((uint)value); break;
                case TypeCode.Int64: writer.Write((long)value); break;
                case TypeCode.UInt64: writer.Write((ulong)value); break;
                case TypeCode.Single: writer.Write((float)value); break;
                case TypeCode.Double: writer.Write((double)value); break;
                default: throw new NotSupportedException("ADS 不支持写入类型 " + value.GetType().Name + "。");
            }
        }

        public static object ParseWriteValue(PlcDataType dataType, string valueText)
        {
            string text = valueText ?? string.Empty;
            if (PlcDataTypeHelper.IsArray(dataType))
            {
                string[] parts = text.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) throw new FormatException("ADS 数组至少需要一个元素。");
                Array values = PlcDataTypeHelper.CreateArray(dataType, parts.Length);
                Type elementType = GetManagedType(dataType).GetElementType();
                for (int i = 0; i < parts.Length; i++)
                    values.SetValue(elementType == typeof(bool) ? ParseBoolean(parts[i])
                        : Convert.ChangeType(parts[i], elementType, CultureInfo.InvariantCulture), i);
                return values;
            }
            switch (dataType)
            {
                case PlcDataType.Bool: return ParseBoolean(text);
                case PlcDataType.Int8: return sbyte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
                case PlcDataType.Byte:
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
                    throw new NotSupportedException("ADS 不支持写入该数据类型。");
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
