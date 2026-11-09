using System;
using System.Globalization;
using System.Linq;
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.CanOpen
{
    internal static class CanOpenDataCodec
    {
        public static object Decode(PlcDataType dataType, byte[] data)
        {
            byte[] value = Pad(data, GetScalarByteCount(dataType));
            switch (ToScalarType(dataType))
            {
                case PlcDataType.Int8:
                    return unchecked((sbyte)value[0]);
                case PlcDataType.UInt8:
                    return value[0];
                case PlcDataType.Bool:
                    return value[0] != 0;
                case PlcDataType.Int16:
                    return BitConverter.ToInt16(value, 0);
                case PlcDataType.UInt16:
                    return BitConverter.ToUInt16(value, 0);
                case PlcDataType.Int32:
                    return BitConverter.ToInt32(value, 0);
                case PlcDataType.UInt32:
                    return BitConverter.ToUInt32(value, 0);
                case PlcDataType.Int64:
                    return BitConverter.ToInt64(value, 0);
                case PlcDataType.UInt64:
                    return BitConverter.ToUInt64(value, 0);
                case PlcDataType.Float:
                    return BitConverter.ToSingle(value, 0);
                case PlcDataType.Double:
                    return BitConverter.ToDouble(value, 0);
                case PlcDataType.String:
                    return Encoding.ASCII.GetString(data ?? new byte[0]).TrimEnd('\0');
                default:
                    throw new NotSupportedException("CANopen expedited SDO does not support data type " + dataType + ".");
            }
        }

        public static byte[] Encode(PlcDataType dataType, string valueText)
        {
            string text = valueText ?? string.Empty;
            switch (ToScalarType(dataType))
            {
                case PlcDataType.Int8:
                    return new[] { unchecked((byte)sbyte.Parse(text, CultureInfo.InvariantCulture)) };
                case PlcDataType.UInt8:
                    return new[] { byte.Parse(text, CultureInfo.InvariantCulture) };
                case PlcDataType.Bool:
                    return new[] { ParseBoolean(text) ? (byte)1 : (byte)0 };
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
                    return Encoding.ASCII.GetBytes(text);
                default:
                    throw new NotSupportedException("CANopen expedited SDO does not support data type " + dataType + ".");
            }
        }

        public static PlcDataType ToScalarType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.BoolArray:
                case PlcDataType.Coil:
                case PlcDataType.CoilArray:
                case PlcDataType.DiscreteInput:
                case PlcDataType.DiscreteInputArray:
                    return PlcDataType.Bool;
                case PlcDataType.Int8Array:
                    return PlcDataType.Int8;
                case PlcDataType.UInt8Array:
                    return PlcDataType.UInt8;
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

        public static Array CreateArray(PlcDataType dataType, int length)
        {
            PlcDataType scalarType = ToScalarType(dataType);
            switch (scalarType)
            {
                case PlcDataType.Int8:
                    return new sbyte[length];
                case PlcDataType.UInt8:
                    return new byte[length];
                case PlcDataType.Bool:
                    return new bool[length];
                case PlcDataType.Int16:
                    return new short[length];
                case PlcDataType.UInt16:
                    return new ushort[length];
                case PlcDataType.Int32:
                    return new int[length];
                case PlcDataType.UInt32:
                    return new uint[length];
                case PlcDataType.Int64:
                    return new long[length];
                case PlcDataType.UInt64:
                    return new ulong[length];
                case PlcDataType.Float:
                    return new float[length];
                case PlcDataType.Double:
                    return new double[length];
                default:
                    throw new NotSupportedException("CANopen arrays do not support data type " + dataType + ".");
            }
        }

        public static string GetTypeName(PlcDataType dataType)
        {
            switch (ToScalarType(dataType))
            {
                case PlcDataType.Int8:
                    return "INTEGER8";
                case PlcDataType.UInt8:
                    return "UNSIGNED8";
                case PlcDataType.Bool:
                    return "BOOLEAN";
                case PlcDataType.Int16:
                    return "INTEGER16";
                case PlcDataType.UInt16:
                    return "UNSIGNED16";
                case PlcDataType.Int32:
                    return "INTEGER32";
                case PlcDataType.UInt32:
                    return "UNSIGNED32";
                case PlcDataType.Int64:
                    return "INTEGER64";
                case PlcDataType.UInt64:
                    return "UNSIGNED64";
                case PlcDataType.Float:
                    return "REAL32";
                case PlcDataType.Double:
                    return "REAL64";
                case PlcDataType.String:
                    return "VISIBLE_STRING";
                default:
                    return dataType.ToString();
            }
        }

        internal static int GetScalarByteCount(PlcDataType dataType)
        {
            switch (ToScalarType(dataType))
            {
                case PlcDataType.Bool:
                case PlcDataType.Int8:
                case PlcDataType.UInt8:
                    return 1;
                case PlcDataType.Int16:
                case PlcDataType.UInt16:
                    return 2;
                case PlcDataType.Int32:
                case PlcDataType.UInt32:
                case PlcDataType.Float:
                    return 4;
                case PlcDataType.Int64:
                case PlcDataType.UInt64:
                case PlcDataType.Double:
                    return 8;
                case PlcDataType.String:
                    return 0;
                default:
                    throw new NotSupportedException("CANopen expedited SDO does not support data type " + dataType + ".");
            }
        }

        private static byte[] Pad(byte[] data, int count)
        {
            if (count == 0)
                return data ?? new byte[0];
            byte[] value = new byte[count];
            if (data != null)
                Buffer.BlockCopy(data, 0, value, 0, Math.Min(data.Length, count));
            return value;
        }

        private static bool ParseBoolean(string text)
        {
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
                return false;

            double number;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && Math.Abs(number) > double.Epsilon;
        }
    }
}
