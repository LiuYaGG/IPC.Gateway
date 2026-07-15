using System;
using System.Globalization;
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Pccc
{
    internal static class PcccDataCodec
    {
        public static object Decode(PcccAddress address, PlcDataType dataType, byte[] data, int count)
        {
            if (address.BitNumber.HasValue)
            {
                Require(data, 2);
                return (ReadUInt16(data, 0) & (1 << address.BitNumber.Value)) != 0;
            }

            switch (dataType)
            {
                case PlcDataType.Bool: Require(data, 2); return ReadUInt16(data, 0) != 0;
                case PlcDataType.Int16: Require(data, 2); return (short)ReadUInt16(data, 0);
                case PlcDataType.UInt16: Require(data, 2); return ReadUInt16(data, 0);
                case PlcDataType.Int32: Require(data, 4); return BitConverter.ToInt32(data, 0);
                case PlcDataType.UInt32: Require(data, 4); return BitConverter.ToUInt32(data, 0);
                case PlcDataType.Float: Require(data, 4); return BitConverter.ToSingle(data, 0);
                case PlcDataType.String: return DecodeString(data);
                case PlcDataType.BoolArray: return DecodeBoolArray(data, count);
                case PlcDataType.Int16Array: return DecodeArray(data, count, 2, offset => (short)ReadUInt16(data, offset));
                case PlcDataType.UInt16Array: return DecodeArray(data, count, 2, offset => ReadUInt16(data, offset));
                case PlcDataType.Int32Array: return DecodeArray(data, count, 4, offset => BitConverter.ToInt32(data, offset));
                case PlcDataType.UInt32Array: return DecodeArray(data, count, 4, offset => BitConverter.ToUInt32(data, offset));
                case PlcDataType.FloatArray: return DecodeArray(data, count, 4, offset => BitConverter.ToSingle(data, offset));
                default: throw new NotSupportedException("PCCC暂不支持数据类型：" + dataType);
            }
        }

        public static byte[] Encode(PcccAddress address, PlcDataType dataType, string text)
        {
            switch (dataType)
            {
                case PlcDataType.Bool: return BitConverter.GetBytes(ParseBool(text) ? (ushort)1 : (ushort)0);
                case PlcDataType.Int16: return BitConverter.GetBytes(short.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt16: return BitConverter.GetBytes(ushort.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Int32: return BitConverter.GetBytes(int.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.UInt32: return BitConverter.GetBytes(uint.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.Float: return BitConverter.GetBytes(float.Parse(text, CultureInfo.InvariantCulture));
                case PlcDataType.String: return EncodeString(text);
                default: throw new NotSupportedException("PCCC暂不支持写入数据类型：" + dataType);
            }
        }

        public static int GetByteCount(PcccAddress address, PlcDataType dataType, int count)
        {
            if (address.BitNumber.HasValue)
                return 2;
            if (dataType == PlcDataType.String)
                return address.NativeElementSize;
            return checked(PlcDataTypeHelper.GetElementSize(dataType) * Math.Max(1, count));
        }

        private static string DecodeString(byte[] data)
        {
            Require(data, 2);
            int length = Math.Min(ReadUInt16(data, 0), Math.Max(0, data.Length - 2));
            byte[] text = new byte[length];
            for (int i = 0; i < length; i++)
            {
                int source = 2 + (i ^ 1);
                if (source < data.Length)
                    text[i] = data[source];
            }
            return Encoding.ASCII.GetString(text);
        }

        private static byte[] EncodeString(string value)
        {
            byte[] text = Encoding.ASCII.GetBytes(value ?? string.Empty);
            if (text.Length > 82)
                throw new ArgumentException("PCCC字符串最大长度为82字节。", nameof(value));
            byte[] result = new byte[84];
            byte[] length = BitConverter.GetBytes((ushort)text.Length);
            result[0] = length[0];
            result[1] = length[1];
            for (int i = 0; i < text.Length; i++)
                result[2 + (i ^ 1)] = text[i];
            return result;
        }

        private static bool[] DecodeBoolArray(byte[] data, int count)
        {
            Require(data, ((count + 15) / 16) * 2);
            bool[] result = new bool[count];
            for (int i = 0; i < count; i++)
                result[i] = (ReadUInt16(data, (i / 16) * 2) & (1 << (i % 16))) != 0;
            return result;
        }

        private static T[] DecodeArray<T>(byte[] data, int count, int size, Func<int, T> read)
        {
            Require(data, count * size);
            T[] result = new T[count];
            for (int i = 0; i < count; i++)
                result[i] = read(i * size);
            return result;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        private static bool ParseBool(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            return bool.Parse(value);
        }

        private static void Require(byte[] data, int length)
        {
            if (data == null || data.Length < length)
                throw new InvalidOperationException("PCCC响应数据长度不足。");
        }
    }
}
