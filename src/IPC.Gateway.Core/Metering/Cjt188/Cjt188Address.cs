/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Cjt188
* 项目描述 ：
* 类 名 称 ：Cjt188Address
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
using System.Text.RegularExpressions;

namespace IPC.Plc.Communication.Metering.Cjt188
{
    
    
    
    
    
    
    
    
    
    internal sealed class Cjt188Address
    {
        private static readonly Regex ShortPattern = new Regex("^CJ188:([0-9A-Fa-f]{14}):([0-9A-Fa-f]{4})$", RegexOptions.Compiled);
        private static readonly Regex FullPattern = new Regex("^CJ188:([0-9A-Fa-f]{2}):([0-9A-Fa-f]{14}):([0-9A-Fa-f]{4})$", RegexOptions.Compiled);

        private Cjt188Address(byte meterType, string meterAddress, string dataIdentifier)
        {
            MeterType = meterType;
            MeterAddress = meterAddress.ToUpperInvariant();
            DataIdentifier = dataIdentifier.ToUpperInvariant();
        }

        public byte MeterType { get; private set; }
        public string MeterAddress { get; private set; }
        public string DataIdentifier { get; private set; }

        public byte[] GetAddressBytes()
        {
            byte[] bytes = new byte[7];
            for (int i = 0; i < 7; i++)
            {
                int sourceIndex = MeterAddress.Length - 2 - (i * 2);
                bytes[i] = byte.Parse(MeterAddress.Substring(sourceIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        public byte[] GetDataIdentifierBytes()
        {
            return new[]
            {
                byte.Parse(DataIdentifier.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(DataIdentifier.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            };
        }

        public static Cjt188Address Parse(string addressText)
        {
            if (string.IsNullOrWhiteSpace(addressText))
                throw new ArgumentException("CJ/T188地址不能为空。", "addressText");

            string text = addressText.Trim();
            Match full = FullPattern.Match(text);
            if (full.Success)
            {
                return new Cjt188Address(
                    byte.Parse(full.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    full.Groups[2].Value,
                    full.Groups[3].Value);
            }

            Match shortMatch = ShortPattern.Match(text);
            if (shortMatch.Success)
                return new Cjt188Address(0x10, shortMatch.Groups[1].Value, shortMatch.Groups[2].Value);

            throw new FormatException("CJ/T188地址格式应为 CJ188:12345678901234:901F 或 CJ188:10:12345678901234:901F。");
        }
    }
}
