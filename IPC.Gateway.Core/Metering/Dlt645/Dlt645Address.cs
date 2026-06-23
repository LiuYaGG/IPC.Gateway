/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Metering.Dlt645
* 项目描述 ：
* 类 名 称 ：Dlt645Address
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Metering.Dlt645
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

namespace IPC.Plc.Communication.Metering.Dlt645
{
    
    
    
    
    
    
    
    
    
    internal sealed class Dlt645Address
    {
        private static readonly Regex AddressPattern = new Regex("^DLT645:([0-9A-Fa-f]{12}):([0-9A-Fa-f]{8})$", RegexOptions.Compiled);

        private Dlt645Address(string meterAddress, string dataIdentifier)
        {
            MeterAddress = meterAddress;
            DataIdentifier = dataIdentifier.ToUpperInvariant();
        }

        public string MeterAddress { get; private set; }
        public string DataIdentifier { get; private set; }

        public byte[] GetAddressBytes()
        {
            byte[] bytes = new byte[6];
            for (int i = 0; i < 6; i++)
            {
                int sourceIndex = MeterAddress.Length - 2 - (i * 2);
                bytes[i] = byte.Parse(MeterAddress.Substring(sourceIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        public byte[] GetDataIdentifierBytes()
        {
            byte[] bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                int sourceIndex = DataIdentifier.Length - 2 - (i * 2);
                bytes[i] = byte.Parse(DataIdentifier.Substring(sourceIndex, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return bytes;
        }

        public static Dlt645Address Parse(string addressText)
        {
            if (string.IsNullOrWhiteSpace(addressText))
                throw new ArgumentException("DLT645地址不能为空。", "addressText");

            Match match = AddressPattern.Match(addressText.Trim());
            if (!match.Success)
                throw new FormatException("DLT645地址格式应为 DLT645:000000000001:00010000。");

            return new Dlt645Address(match.Groups[1].Value, match.Groups[2].Value);
        }
    }
}
