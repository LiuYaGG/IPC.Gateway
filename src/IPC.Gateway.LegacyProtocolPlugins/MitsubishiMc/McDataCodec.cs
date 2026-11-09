/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiMc
* 项目描述 ：
* 类 名 称 ：McDataCodec
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiMc
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

namespace IPC.Plc.Communication.MitsubishiMc
{
    
    
    
    
    
    
    
    
    
    internal static class McDataCodec
    {
        public const int DefaultStringBytes = 82;

        public static int GetElementCount(PlcDataType dataType, string writeText, int readCount)
        {
            if (dataType == PlcDataType.String)
                return Math.Max(1, readCount);
            if (!PlcDataTypeHelper.IsArray(dataType))
                return 1;
            if (!string.IsNullOrWhiteSpace(writeText))
                return SplitValues(writeText).Length;
            return readCount;
        }

        public static int GetWordCount(PlcDataType dataType, int elementCount)
        {
            if (dataType == PlcDataType.String)
                return (GetStringByteCount(elementCount) + 1) / 2;
            return (PlcDataTypeHelper.GetElementSize(dataType) * elementCount + 1) / 2;
        }

        public static int GetDeviceOffset(PlcDataType dataType, int elementOffset)
        {
            if (!PlcDataTypeHelper.IsArray(dataType))
                return 0;
            if (dataType == PlcDataType.BoolArray)
                return elementOffset;
            return GetWordCount(GetScalarType(dataType), elementOffset);
        }

        public static object Decode(PlcDataType dataType, byte[] data, int count)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return data != null && data.Length > 0 && data[0] != 0;
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
                case PlcDataType.Float:
                    Require(data, 4);
                    return BitConverter.ToSingle(data, 0);
                case PlcDataType.Double:
                    Require(data, 8);
                    return BitConverter.ToDouble(data, 0);
                case PlcDataType.String:
                    return DecodeString(data, count);
                case PlcDataType.BoolArray:
                    return DecodeBoolArray(data, count);
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
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        public static byte[] Encode(PlcDataType dataType, string text)
        {
            return Encode(dataType, text, dataType == PlcDataType.String ? DefaultStringBytes : 1);
        }

        public static byte[] Encode(PlcDataType dataType, string text, int elementCount)
        {
            switch (dataType)
            {
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
                case PlcDataType.Float:
                    return BitConverter.GetBytes(float.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Double:
                    return BitConverter.GetBytes(double.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.String:
                    return EncodeString(text, elementCount);
                case PlcDataType.BoolArray:
                    return EncodeBoolArray(SplitValues(text));
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

        public static byte[] PackBits(byte[] boolValues, int count)
        {
            byte[] result = new byte[(count + 1) / 2];
            for (int i = 0; i < count; i++)
            {
                if (boolValues[i] == 0)
                    continue;

                if ((i % 2) == 0)
                    result[i / 2] |= 0x10;
                else
                    result[i / 2] |= 0x01;
            }
            return result;
        }

        public static bool[] UnpackBits(byte[] data, int count)
        {
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
            {
                byte value = data[i / 2];
                result[i] = (i % 2) == 0 ? (value & 0x10) != 0 : (value & 0x01) != 0;
            }
            return result;
        }

        public static void SetWordBit(byte[] wordBytes, int bitOffset, bool value)
        {
            ushort word = BitConverter.ToUInt16(wordBytes, 0);
            ushort mask = (ushort)(1 << bitOffset);
            if (value)
                word = (ushort)(word | mask);
            else
                word = (ushort)(word & ~mask);
            byte[] bytes = BitConverter.GetBytes(word);
            wordBytes[0] = bytes[0];
            wordBytes[1] = bytes[1];
        }

        public static PlcDataType GetScalarType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Int16Array:
                    return PlcDataType.Int16;
                case PlcDataType.UInt16Array:
                    return PlcDataType.UInt16;
                case PlcDataType.Int32Array:
                    return PlcDataType.Int32;
                case PlcDataType.UInt32Array:
                    return PlcDataType.UInt32;
                case PlcDataType.Int64Array:
                    return PlcDataType.Int64;
                case PlcDataType.UInt64Array:
                    return PlcDataType.UInt64;
                case PlcDataType.FloatArray:
                    return PlcDataType.Float;
                case PlcDataType.DoubleArray:
                    return PlcDataType.Double;
                default:
                    return dataType;
            }
        }

        private static bool[] DecodeBoolArray(byte[] data, int count)
        {
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = data[i] != 0;
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

        private static byte[] EncodeString(string text, int maxBytes)
        {
            if (text == null)
                text = string.Empty;

            int byteCount = GetStringByteCount(maxBytes);
            int alignedByteCount = (byteCount + 1) / 2 * 2;
            byte[] result = new byte[alignedByteCount];
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            int length = Math.Min(bytes.Length, byteCount);
            Buffer.BlockCopy(bytes, 0, result, 0, length);
            return result;
        }

        private static string DecodeString(byte[] data, int maxBytes)
        {
            int limit = Math.Min(data.Length, GetStringByteCount(maxBytes));
            int length = 0;
            while (length < limit && data[length] != 0)
                length++;
            return Encoding.ASCII.GetString(data, 0, length);
        }

        private static int GetStringByteCount(int requestedLength)
        {
            return requestedLength > 0 ? requestedLength : DefaultStringBytes;
        }

        private static byte[] EncodeBoolArray(string[] values)
        {
            byte[] result = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = ParseBool(values[i]) ? (byte)1 : (byte)0;
            return result;
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
    }
}
