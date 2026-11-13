/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Cjt188
* 项目描述 ：
* 类 名 称 ：Cjt188DataCodec
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Cjt188
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
using System.Globalization;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering.Cjt188
{
    
    
    
    
    
    
    
    
    
    internal static class Cjt188DataCodec
    {
        public static object Decode(byte[] data, Cjt188Address address, PlcDataType dataType)
        {
            if (data.Length == 0) throw new FormatException("CJ/T188 回复缺少数值。");
            string di = address.DataIdentifier;
            int decimals = InferDecimalPlaces(di);
            ReadOnlySpan<byte> field = data;
            bool data1 = di is "901F" or "1F90";
            bool data2 = di is "911F" or "1F91";
            if (data1 || data2)
            {
                // Current cumulative flow (water/gas), or current heat following
                // the settlement heat/cold fields. The next byte is a unit code.
                bool heat = address.MeterType is >= 0x20 and <= 0x29;
                int offset = heat ? (data1 ? 5 : 10) : 0;
                if (data.Length < offset + 5 || data[offset + 4] is 0xFF or 0xEE)
                    throw new FormatException("CJ/T188 回复缺少有效的当前计量值或单位。");
                field = data.AsSpan(offset, 4);
                decimals = 2;
            }
            double raw = 0;
            for (int i = field.Length - 1; i >= 0; i--)
            {
                int high = field[i] >> 4, low = field[i] & 15;
                if (high > 9 || low > 9) throw new FormatException("CJ/T188 数值包含无效 BCD。");
                raw = raw * 100 + high * 10 + low;
            }
            double numeric = raw / Math.Pow(10, decimals);
            return ConvertValue(numeric, FormatNumber(numeric, decimals), dataType);
        }

        private static object ConvertValue(double numeric, string text, PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.String:
                    return text;
                case PlcDataType.Float:
                    return (float)numeric;
                case PlcDataType.Double:
                    return numeric;
                case PlcDataType.Int16:
                    return Convert.ToInt16(Math.Round(numeric), CultureInfo.InvariantCulture);
                case PlcDataType.UInt16:
                    return Convert.ToUInt16(Math.Round(numeric), CultureInfo.InvariantCulture);
                case PlcDataType.Int32:
                    return Convert.ToInt32(Math.Round(numeric), CultureInfo.InvariantCulture);
                case PlcDataType.UInt32:
                    return Convert.ToUInt32(Math.Round(numeric), CultureInfo.InvariantCulture);
                case PlcDataType.Int64:
                    return Convert.ToInt64(Math.Round(numeric), CultureInfo.InvariantCulture);
                case PlcDataType.UInt64:
                    return Convert.ToUInt64(Math.Round(numeric), CultureInfo.InvariantCulture);
                default:
                    return numeric;
            }
        }

        private static string FormatNumber(double value, int decimalPlaces)
        {
            if (decimalPlaces <= 0)
                return Math.Round(value).ToString("0", CultureInfo.InvariantCulture);
            return value.ToString("F" + decimalPlaces.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static int InferDecimalPlaces(string dataIdentifier)
        {
            string di = (dataIdentifier ?? string.Empty).Trim().ToUpperInvariant();
            if (di == "901F")
                return 2;
            if (di == "902F")
                return 3;
            return 0;
        }
    }
}
