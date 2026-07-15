/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Cip
* 项目描述 ：
* 类 名 称 ：CipDataCodec
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Cip
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using IPC.Plc.Communication.Core;





namespace IPC.Plc.Communication.Cip
{
    
    
    
    
    
    
    
    
    
    public static class CipDataCodec
    {
        public static object Decode(PlcDataType requestedType, ushort actualType, byte[] data, int count)
        {
            ValidateActualType(requestedType, actualType);
            switch (requestedType)
            {
                case PlcDataType.Int8:
                    Require(data, 1);
                    return unchecked((sbyte)data[0]);
                case PlcDataType.UInt8:
                    Require(data, 1);
                    return data[0];
                case PlcDataType.Bool:
                    Require(data, 1);
                    return data[0] != 0;
                case PlcDataType.Int16:
                    Require(data, 2);
                    return BitConverter.ToInt16(data, 0);
                case PlcDataType.UInt16:
                    Require(data, 2);
                    return BitConverter.ToUInt16(data, 0);
                case PlcDataType.Int32:
                    Require(data, 4);
                    return BitConverter.ToInt32(data, 0);
                case PlcDataType.UInt32:
                    Require(data, 4);
                    return BitConverter.ToUInt32(data, 0);
                case PlcDataType.Int64:
                    Require(data, 8);
                    return BitConverter.ToInt64(data, 0);
                case PlcDataType.UInt64:
                    Require(data, 8);
                    return BitConverter.ToUInt64(data, 0);
                case PlcDataType.String:
                    return DecodeRockwellString(data, count);
                case PlcDataType.Float:
                    Require(data, 4);
                    return BitConverter.ToSingle(data, 0);
                case PlcDataType.Double:
                    Require(data, 8);
                    return BitConverter.ToDouble(data, 0);
                case PlcDataType.BoolArray:
                    return DecodeBoolArray(actualType, data, count);
                case PlcDataType.Int8Array:
                    return DecodeArray<sbyte>(data, count, 1, delegate(byte[] b, int o) { return unchecked((sbyte)b[o]); });
                case PlcDataType.UInt8Array:
                    return DecodeArray<byte>(data, count, 1, delegate(byte[] b, int o) { return b[o]; });
                case PlcDataType.Int16Array:
                    return DecodeArray<short>(data, count, 2, delegate(byte[] b, int o) { return BitConverter.ToInt16(b, o); });
                case PlcDataType.UInt16Array:
                    return DecodeArray<ushort>(data, count, 2, delegate(byte[] b, int o) { return BitConverter.ToUInt16(b, o); });
                case PlcDataType.Int32Array:
                    return DecodeArray<int>(data, count, 4, delegate(byte[] b, int o) { return BitConverter.ToInt32(b, o); });
                case PlcDataType.UInt32Array:
                    return DecodeArray<uint>(data, count, 4, delegate(byte[] b, int o) { return BitConverter.ToUInt32(b, o); });
                case PlcDataType.Int64Array:
                    return DecodeArray<long>(data, count, 8, delegate(byte[] b, int o) { return BitConverter.ToInt64(b, o); });
                case PlcDataType.UInt64Array:
                    return DecodeArray<ulong>(data, count, 8, delegate(byte[] b, int o) { return BitConverter.ToUInt64(b, o); });
                case PlcDataType.FloatArray:
                    return DecodeArray<float>(data, count, 4, delegate(byte[] b, int o) { return BitConverter.ToSingle(b, o); });
                case PlcDataType.DoubleArray:
                    return DecodeArray<double>(data, count, 8, delegate(byte[] b, int o) { return BitConverter.ToDouble(b, o); });
                default:
                    throw new ArgumentOutOfRangeException("requestedType");
            }
        }

        private static void ValidateActualType(PlcDataType requestedType, ushort actualType)
        {
            if (requestedType == PlcDataType.BoolArray &&
                (actualType == CipTypeCodes.Bool || actualType == CipTypeCodes.Dword || actualType == CipTypeCodes.Dint))
                return;
            if (requestedType == PlcDataType.Int32Array && actualType == CipTypeCodes.Dword)
                return;
            if (requestedType == PlcDataType.String &&
                (actualType == CipTypeCodes.String || actualType == CipTypeCodes.AbbreviatedStructure))
                return;

            ushort expected = CipTypeCodes.FromPlcDataType(requestedType);
            if (actualType != expected)
                throw new InvalidOperationException(
                    "CIP标签数据类型不匹配，配置为" + CipTypeCodes.ToName(expected) +
                    "，PLC返回" + CipTypeCodes.ToName(actualType) + "。");
        }

        public static byte[] Encode(PlcDataType dataType, string text)
        {
            switch (dataType)
            {
                case PlcDataType.Int8:
                    return new[] { unchecked((byte)sbyte.Parse(text, CultureInfo.InvariantCulture)) };
                case PlcDataType.UInt8:
                    return new[] { byte.Parse(text, CultureInfo.InvariantCulture) };
                case PlcDataType.Bool:
                    return new[] { ParseBool(text) ? (byte)1 : (byte)0 };
                case PlcDataType.Int16:
                    return BitConverter.GetBytes(short.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt16:
                    return BitConverter.GetBytes(ushort.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int32:
                    return BitConverter.GetBytes(int.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt32:
                    return BitConverter.GetBytes(uint.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int64:
                    return BitConverter.GetBytes(long.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt64:
                    return BitConverter.GetBytes(ulong.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.String:
                    return EncodeRockwellString(text);
                case PlcDataType.Float:
                    return BitConverter.GetBytes(float.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Double:
                    return BitConverter.GetBytes(double.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.BoolArray:
                    return EncodeBoolArray(SplitValues(text));
                case PlcDataType.Int8Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return new[] { unchecked((byte)sbyte.Parse(s, CultureInfo.InvariantCulture)) }; });
                case PlcDataType.UInt8Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return new[] { byte.Parse(s, CultureInfo.InvariantCulture) }; });
                case PlcDataType.Int16Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(short.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt16Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(ushort.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.Int32Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(int.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt32Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(uint.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.Int64Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(long.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt64Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(ulong.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.FloatArray:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(float.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.DoubleArray:
                    return EncodeArray(SplitValues(text), delegate(string s) { return BitConverter.GetBytes(double.Parse(s, CultureInfo.InvariantCulture)); });
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        public static int GetElementCount(PlcDataType dataType, string writeText, int readCount)
        {
            if (!PlcDataTypeHelper.IsArray(dataType))
                return 1;
            if (!string.IsNullOrWhiteSpace(writeText))
                return SplitValues(writeText).Length;
            return readCount;
        }

        public static string FormatValue(object value)
        {
            Array array = value as Array;
            if (array == null)
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            List<string> values = new List<string>();
            foreach (object item in array)
                values.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
            return string.Join(", ", values.ToArray());
        }

        private static string DecodeRockwellString(byte[] data, int maxBytes)
        {
            Require(data, 4);
            int length = BitConverter.ToInt32(data, 0);
            if (length < 0)
                length = 0;
            if (length > data.Length - 4)
                length = data.Length - 4;
            if (maxBytes > 0 && length > maxBytes)
                length = maxBytes;
            return Encoding.ASCII.GetString(data, 4, length);
        }

        private static byte[] EncodeRockwellString(string text)
        {
            if (text == null)
                text = string.Empty;

            byte[] bytes = Encoding.ASCII.GetBytes(text);
            if (bytes.Length > 82)
                throw new ArgumentException("默认 Rockwell STRING 最大长度为 82 字节。");

            MemoryStream stream = new MemoryStream();
            WriteInt32(stream, bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
            for (int i = bytes.Length; i < 82; i++)
                stream.WriteByte(0);
            return stream.ToArray();
        }

        private static bool[] DecodeBoolArray(ushort actualType, byte[] data, int count)
        {
            bool[] result = new bool[count];
            if (actualType == CipTypeCodes.Bool && data.Length >= count)
            {
                for (int i = 0; i < count; i++)
                    result[i] = data[i] != 0;
                return result;
            }

            int wordCount = (count + 31) / 32;
            Require(data, wordCount * 4);
            for (int i = 0; i < count; i++)
            {
                int word = BitConverter.ToInt32(data, (i / 32) * 4);
                result[i] = ((word >> (i % 32)) & 1) != 0;
            }
            return result;
        }

        private static T[] DecodeArray<T>(byte[] data, int count, int elementSize, Func<byte[], int, T> converter)
        {
            Require(data, count * elementSize);
            T[] values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = converter(data, i * elementSize);
            return values;
        }

        private static byte[] EncodeBoolArray(string[] values)
        {
            byte[] bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                bytes[i] = ParseBool(values[i]) ? (byte)1 : (byte)0;
            return bytes;
        }

        private static byte[] EncodeArray(string[] values, Func<string, byte[]> converter)
        {
            MemoryStream stream = new MemoryStream();
            for (int i = 0; i < values.Length; i++)
            {
                byte[] bytes = converter(values[i]);
                stream.Write(bytes, 0, bytes.Length);
            }
            return stream.ToArray();
        }

        private static string[] SplitValues(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new string[0];

            return text.Split(new[] { ',', ';', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool ParseBool(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
            return bool.Parse(value);
        }

        private static void Require(byte[] data, int minLength)
        {
            if (data == null || data.Length < minLength)
                throw new InvalidOperationException("PLC 返回的数据长度不足。");
        }

        private static void WriteInt32(Stream stream, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
