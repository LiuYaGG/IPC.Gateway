/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.SiemensS7
* 项目描述 ：
* 类 名 称 ：S7DataCodec
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.SiemensS7
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

namespace IPC.Plc.Communication.SiemensS7
{
    
    
    
    
    
    
    
    
    
    internal static class S7DataCodec
    {
        public static object Decode(PlcDataType dataType, byte[] data, int bitOffset, int count)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return GetBit(data, bitOffset);
                case PlcDataType.BoolArray:
                    return DecodeBoolArray(data, bitOffset, count);
                case PlcDataType.Int16:
                    Require(data, 2);
                    return (short)ReadUInt16(data, 0);
                case PlcDataType.UInt16:
                    Require(data, 2);
                    return ReadUInt16(data, 0);
                case PlcDataType.Int32:
                    Require(data, 4);
                    return (int)ReadUInt32(data, 0);
                case PlcDataType.UInt32:
                    Require(data, 4);
                    return ReadUInt32(data, 0);
                case PlcDataType.Int64:
                    Require(data, 8);
                    return (long)ReadUInt64(data, 0);
                case PlcDataType.UInt64:
                    Require(data, 8);
                    return ReadUInt64(data, 0);
                case PlcDataType.Float:
                    Require(data, 4);
                    return ReadSingle(data, 0);
                case PlcDataType.Double:
                    Require(data, 8);
                    return ReadDouble(data, 0);
                case PlcDataType.String:
                    return DecodeString(data);
                case PlcDataType.Int16Array:
                    return DecodeArray<short>(data, count, 2, delegate(byte[] b, int o) { return (short)ReadUInt16(b, o); });
                case PlcDataType.UInt16Array:
                    return DecodeArray<ushort>(data, count, 2, ReadUInt16);
                case PlcDataType.Int32Array:
                    return DecodeArray<int>(data, count, 4, delegate(byte[] b, int o) { return (int)ReadUInt32(b, o); });
                case PlcDataType.UInt32Array:
                    return DecodeArray<uint>(data, count, 4, ReadUInt32);
                case PlcDataType.Int64Array:
                    return DecodeArray<long>(data, count, 8, delegate(byte[] b, int o) { return (long)ReadUInt64(b, o); });
                case PlcDataType.UInt64Array:
                    return DecodeArray<ulong>(data, count, 8, ReadUInt64);
                case PlcDataType.FloatArray:
                    return DecodeArray<float>(data, count, 4, ReadSingle);
                case PlcDataType.DoubleArray:
                    return DecodeArray<double>(data, count, 8, ReadDouble);
                default:
                    throw new ArgumentOutOfRangeException("dataType");
            }
        }

        public static byte[] Encode(PlcDataType dataType, string text)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return new[] { ParseBool(text) ? (byte)1 : (byte)0 };
                case PlcDataType.BoolArray:
                    return EncodeBoolValues(SplitValues(text));
                case PlcDataType.Int16:
                    return GetBytes((ushort)short.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt16:
                    return GetBytes(ushort.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int32:
                    return GetBytes((uint)int.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt32:
                    return GetBytes(uint.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int64:
                    return GetBytes((ulong)long.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt64:
                    return GetBytes(ulong.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Float:
                    return GetBytes(float.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Double:
                    return GetBytes(double.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.String:
                    return EncodeString(text, 254);
                case PlcDataType.Int16Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes((ushort)short.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt16Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes(ushort.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.Int32Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes((uint)int.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt32Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes(uint.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.Int64Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes((ulong)long.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.UInt64Array:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes(ulong.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.FloatArray:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes(float.Parse(s, CultureInfo.InvariantCulture)); });
                case PlcDataType.DoubleArray:
                    return EncodeArray(SplitValues(text), delegate(string s) { return GetBytes(double.Parse(s, CultureInfo.InvariantCulture)); });
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

        public static byte[] EncodeString(string text, int maxLength)
        {
            if (text == null)
                text = string.Empty;
            if (maxLength <= 0 || maxLength > 254)
                maxLength = 254;

            byte[] textBytes = Encoding.ASCII.GetBytes(text);
            int length = Math.Min(textBytes.Length, maxLength);
            byte[] result = new byte[maxLength + 2];
            result[0] = (byte)maxLength;
            result[1] = (byte)length;
            Buffer.BlockCopy(textBytes, 0, result, 2, length);
            return result;
        }

        public static bool IsBoolType(PlcDataType dataType)
        {
            return dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray;
        }

        public static int GetReadByteCount(PlcDataType dataType, int bitOffset, int elementCount)
        {
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray)
                return (bitOffset + elementCount + 7) / 8;
            if (dataType == PlcDataType.String)
                return GetStringByteCount(elementCount) + 2;
            return PlcDataTypeHelper.GetElementSize(dataType) * elementCount;
        }

        public static void SetBits(byte[] target, int bitOffset, byte[] boolValues, int count)
        {
            for (int i = 0; i < count; i++)
            {
                int absoluteBit = bitOffset + i;
                int byteIndex = absoluteBit / 8;
                int bitIndex = absoluteBit % 8;
                byte mask = (byte)(1 << bitIndex);
                if (boolValues[i] == 0)
                    target[byteIndex] = (byte)(target[byteIndex] & ~mask);
                else
                    target[byteIndex] = (byte)(target[byteIndex] | mask);
            }
        }

        private static bool[] DecodeBoolArray(byte[] data, int bitOffset, int count)
        {
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = GetBit(data, bitOffset + i);
            return result;
        }

        private static bool GetBit(byte[] data, int bitOffset)
        {
            int byteIndex = bitOffset / 8;
            int bitIndex = bitOffset % 8;
            Require(data, byteIndex + 1);
            return (data[byteIndex] & (1 << bitIndex)) != 0;
        }

        private static T[] DecodeArray<T>(byte[] data, int count, int elementSize, Func<byte[], int, T> converter)
        {
            Require(data, count * elementSize);
            T[] values = new T[count];
            for (int i = 0; i < count; i++)
                values[i] = converter(data, i * elementSize);
            return values;
        }

        private static byte[] EncodeBoolValues(string[] values)
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

        private static string DecodeString(byte[] data)
        {
            Require(data, 2);
            int max = data[0];
            int length = data[1];
            if (max <= 0 || max > data.Length - 2)
                max = data.Length - 2;
            if (length < 0)
                length = 0;
            if (length > max)
                length = max;
            return Encoding.ASCII.GetString(data, 2, length);
        }

        private static int GetStringByteCount(int requestedLength)
        {
            if (requestedLength <= 0 || requestedLength > 254)
                return 254;
            return requestedLength;
        }

        private static byte[] SplitAndReverse(byte[] bytes)
        {
            Array.Reverse(bytes);
            return bytes;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            Require(data, offset + 2);
            return (ushort)((data[offset] << 8) | data[offset + 1]);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            Require(data, offset + 4);
            return (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        }

        private static ulong ReadUInt64(byte[] data, int offset)
        {
            Require(data, offset + 8);
            return ((ulong)ReadUInt32(data, offset) << 32) | ReadUInt32(data, offset + 4);
        }

        private static float ReadSingle(byte[] data, int offset)
        {
            Require(data, offset + 4);
            return BitConverter.ToSingle(SplitAndReverse(new[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] }), 0);
        }

        private static double ReadDouble(byte[] data, int offset)
        {
            Require(data, offset + 8);
            byte[] bytes = new byte[8];
            Buffer.BlockCopy(data, offset, bytes, 0, 8);
            Array.Reverse(bytes);
            return BitConverter.ToDouble(bytes, 0);
        }

        private static byte[] GetBytes(ushort value)
        {
            return new[] { (byte)(value >> 8), (byte)(value & 0xFF) };
        }

        private static byte[] GetBytes(uint value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)(value & 0xFF) };
        }

        private static byte[] GetBytes(ulong value)
        {
            byte[] result = new byte[8];
            byte[] high = GetBytes((uint)(value >> 32));
            byte[] low = GetBytes((uint)(value & 0xFFFFFFFF));
            Buffer.BlockCopy(high, 0, result, 0, 4);
            Buffer.BlockCopy(low, 0, result, 4, 4);
            return result;
        }

        private static byte[] GetBytes(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return bytes;
        }

        private static byte[] GetBytes(double value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);
            return bytes;
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
