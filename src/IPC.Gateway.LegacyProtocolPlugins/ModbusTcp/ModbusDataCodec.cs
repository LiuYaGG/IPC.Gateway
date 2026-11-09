/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.ModbusTcp
* 项目描述 ：
* 类 名 称 ：ModbusDataCodec
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.ModbusTcp
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

namespace IPC.Plc.Communication.ModbusTcp
{
    
    
    
    
    
    
    
    
    
    internal static class ModbusDataCodec
    {
        public const int DefaultStringBytes = 82;

        public static bool IsBitType(PlcDataType dataType)
        {
            return dataType == PlcDataType.Bool ||
                   dataType == PlcDataType.BoolArray ||
                   dataType == PlcDataType.Coil ||
                   dataType == PlcDataType.CoilArray ||
                   dataType == PlcDataType.DiscreteInput ||
                   dataType == PlcDataType.DiscreteInputArray;
        }

        public static bool IsRegisterType(PlcDataType dataType)
        {
            return !IsBitOnlyType(dataType);
        }

        public static bool IsBitOnlyType(PlcDataType dataType)
        {
            return dataType == PlcDataType.Coil ||
                   dataType == PlcDataType.CoilArray ||
                   dataType == PlcDataType.DiscreteInput ||
                   dataType == PlcDataType.DiscreteInputArray;
        }

        public static PlcDataType GetScalarType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                    return PlcDataType.Bool;
                case PlcDataType.CoilArray:
                    return PlcDataType.Coil;
                case PlcDataType.DiscreteInputArray:
                    return PlcDataType.DiscreteInput;
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

        public static int GetRegisterCount(PlcDataType dataType, int elementCount)
        {
            if (dataType == PlcDataType.String)
                return (GetStringByteCount(elementCount) + 1) / 2;

            return (PlcDataTypeHelper.GetElementSize(dataType) * elementCount + 1) / 2;
        }

        public static int GetElementCount(PlcDataType dataType, string writeText, int readCount)
        {
            if (!PlcDataTypeHelper.IsArray(dataType))
                return 1;
            if (!string.IsNullOrWhiteSpace(writeText))
                return SplitValues(writeText).Length;
            return readCount;
        }

        public static int GetRegisterOffset(PlcDataType dataType, int elementOffset)
        {
            if (!PlcDataTypeHelper.IsArray(dataType))
                return 0;
            return GetRegisterCount(GetScalarType(dataType), elementOffset);
        }

        public static object DecodeRegisters(PlcDataType dataType, byte[] data, int count)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    Require(data, 2);
                    return ReadUInt16(data, 0) != 0;
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
                    return DecodeString(data, count);
                case PlcDataType.BoolArray:
                    return DecodeRegisterBoolArray(data, 0, count);
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
                    throw new NotSupportedException("该数据类型不能按 Modbus 寄存器解码: " + dataType);
            }
        }

        public static object DecodeRegisterBits(PlcDataType dataType, byte[] data, int bitOffset, int count)
        {
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.Coil || dataType == PlcDataType.DiscreteInput)
                return GetRegisterBit(data, bitOffset);
            return DecodeRegisterBoolArray(data, bitOffset, count);
        }

        public static object DecodeBits(PlcDataType dataType, bool[] values, int count)
        {
            if (dataType == PlcDataType.Bool || dataType == PlcDataType.Coil || dataType == PlcDataType.DiscreteInput)
            {
                if (values == null || values.Length == 0)
                    throw new InvalidOperationException("PLC 返回的线圈/离散输入数据长度不足。");
                return values[0];
            }

            bool[] result = new bool[count];
            Array.Copy(values, result, Math.Min(values.Length, count));
            return result;
        }

        public static byte[] EncodeRegisters(PlcDataType dataType, string text)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                    return GetBytes((ushort)(ParseBool(text) ? 1 : 0));
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
                    return EncodeString(text);
                case PlcDataType.BoolArray:
                    return EncodeRegisterBoolArray(SplitValues(text));
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
                    throw new NotSupportedException("该数据类型不能按 Modbus 寄存器编码: " + dataType);
            }
        }

        public static bool[] EncodeBits(PlcDataType dataType, string text)
        {
            string[] values = PlcDataTypeHelper.IsArray(dataType) ? SplitValues(text) : new[] { text };
            bool[] result = new bool[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = ParseBool(values[i]);
            return result;
        }

        public static void SetRegisterBits(byte[] target, int bitOffset, bool[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                int absoluteBit = bitOffset + i;
                int byteIndex = (absoluteBit / 16) * 2;
                int bitIndex = absoluteBit % 16;
                ushort word = ReadUInt16(target, byteIndex);
                ushort mask = (ushort)(1 << bitIndex);
                if (values[i])
                    word = (ushort)(word | mask);
                else
                    word = (ushort)(word & ~mask);
                byte[] bytes = GetBytes(word);
                target[byteIndex] = bytes[0];
                target[byteIndex + 1] = bytes[1];
            }
        }

        public static bool[] UnpackCoils(byte[] data, int count)
        {
            bool[] values = new bool[count];
            for (int i = 0; i < count; i++)
                values[i] = (data[i / 8] & (1 << (i % 8))) != 0;
            return values;
        }

        public static byte[] PackCoils(bool[] values, int count)
        {
            byte[] data = new byte[(count + 7) / 8];
            for (int i = 0; i < count; i++)
            {
                if (values[i])
                    data[i / 8] = (byte)(data[i / 8] | (1 << (i % 8)));
            }
            return data;
        }

        private static bool GetRegisterBit(byte[] data, int bitOffset)
        {
            int byteIndex = (bitOffset / 16) * 2;
            int bitIndex = bitOffset % 16;
            Require(data, byteIndex + 2);
            return (ReadUInt16(data, byteIndex) & (1 << bitIndex)) != 0;
        }

        private static bool[] DecodeRegisterBoolArray(byte[] data, int bitOffset, int count)
        {
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = GetRegisterBit(data, bitOffset + i);
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

        private static byte[] EncodeRegisterBoolArray(string[] values)
        {
            byte[] result = new byte[((values.Length + 15) / 16) * 2];
            bool[] boolValues = new bool[values.Length];
            for (int i = 0; i < values.Length; i++)
                boolValues[i] = ParseBool(values[i]);
            SetRegisterBits(result, 0, boolValues);
            return result;
        }

        private static byte[] EncodeString(string text)
        {
            if (text == null)
                text = string.Empty;

            byte[] result = new byte[DefaultStringBytes];
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            int length = Math.Min(bytes.Length, result.Length);
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
            byte[] bytes = new[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset] };
            return BitConverter.ToSingle(bytes, 0);
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

        private static void Require(byte[] data, int minLength)
        {
            if (data == null || data.Length < minLength)
                throw new InvalidOperationException("PLC 返回的数据长度不足。");
        }
    }
}
