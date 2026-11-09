using System;
using System.Globalization;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Dnp3
{
    internal static class Dnp3ValueCodec
    {
        public static object ConvertValue(object value, PlcDataType dataType)
        {
            if (PlcDataTypeHelper.IsArray(dataType) || dataType == PlcDataType.String ||
                dataType == PlcDataType.Coil || dataType == PlcDataType.DiscreteInput)
                throw new NotSupportedException("DNP3 点位仅支持标量 Bool 和数值类型。");
            if (dataType == PlcDataType.Bool)
                return value is bool boolean ? boolean : Convert.ToDouble(value, CultureInfo.InvariantCulture) != 0;
            return Convert.ChangeType(value, GetTargetType(dataType), CultureInfo.InvariantCulture);
        }

        public static object ParseCommand(string text, PlcDataType dataType)
        {
            if (dataType == PlcDataType.Bool)
            {
                if (bool.TryParse(text, out bool boolean)) return boolean;
                if (text == "1") return true;
                if (text == "0") return false;
                throw new FormatException("DNP3 布尔命令值应为 true、false、1 或 0。");
            }
            return Convert.ChangeType(text, GetTargetType(dataType), CultureInfo.InvariantCulture);
        }

        private static Type GetTargetType(PlcDataType dataType) => dataType switch
        {
            PlcDataType.Int8 => typeof(sbyte), PlcDataType.UInt8 => typeof(byte),
            PlcDataType.Int16 => typeof(short), PlcDataType.UInt16 => typeof(ushort),
            PlcDataType.Int32 => typeof(int), PlcDataType.UInt32 => typeof(uint),
            PlcDataType.Int64 => typeof(long), PlcDataType.UInt64 => typeof(ulong),
            PlcDataType.Float => typeof(float), PlcDataType.Double => typeof(double),
            _ => throw new NotSupportedException("DNP3 不支持数据类型 " + dataType + "。")
        };
    }

    internal sealed class Dnp3TagException : Exception
    {
        public Dnp3TagException(string message) : base(message) { }
    }
}
