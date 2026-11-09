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
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Metering.Cjt188
{
    
    
    
    
    
    
    
    
    
    internal static class Cjt188DataCodec
    {
        public static object Decode(byte[] data, string dataIdentifier, PlcDataType dataType)
        {
            if (data == null || data.Length == 0)
                return ConvertValue(0D, string.Empty, dataType);

            string digits = DecodeBcdLittleEndian(data);
            int decimalPlaces = InferDecimalPlaces(dataIdentifier);
            double numeric = ToNumber(digits, decimalPlaces);
            return ConvertValue(numeric, FormatNumber(numeric, decimalPlaces), dataType);
        }

        private static string DecodeBcdLittleEndian(byte[] data)
        {
            StringBuilder builder = new StringBuilder(data.Length * 2);
            for (int i = data.Length - 1; i >= 0; i--)
            {
                int high = (data[i] >> 4) & 0x0F;
                int low = data[i] & 0x0F;
                if (high > 9 || low > 9)
                    return BitConverter.ToString(data).Replace("-", string.Empty);
                builder.Append(high.ToString(CultureInfo.InvariantCulture));
                builder.Append(low.ToString(CultureInfo.InvariantCulture));
            }

            string text = builder.ToString().TrimStart('0');
            return text.Length == 0 ? "0" : text;
        }

        private static double ToNumber(string digits, int decimalPlaces)
        {
            double raw;
            if (!double.TryParse(string.IsNullOrWhiteSpace(digits) ? "0" : digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw))
                return 0D;
            if (decimalPlaces <= 0)
                return raw;
            return raw / Math.Pow(10D, decimalPlaces);
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
