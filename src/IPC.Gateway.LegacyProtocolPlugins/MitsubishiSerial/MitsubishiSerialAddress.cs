/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiSerial
* 项目描述 ：
* 类 名 称 ：MitsubishiSerialAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiSerial
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

namespace IPC.Plc.Communication.MitsubishiSerial
{
    
    
    
    
    
    
    
    
    
    internal sealed class MitsubishiSerialAddress
    {
        public string DeviceName { get; private set; }
        public char DeviceCode { get; private set; }
        public int DeviceNumber { get; private set; }
        public int BitOffset { get; private set; }
        public bool IsBitDevice { get; private set; }

        public MitsubishiSerialAddress AddDeviceOffset(int offset)
        {
            return new MitsubishiSerialAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + offset,
                BitOffset = BitOffset,
                IsBitDevice = IsBitDevice
            };
        }

        public MitsubishiSerialAddress AddBitOffset(int offset)
        {
            if (IsBitDevice)
                return AddDeviceOffset(offset);

            int totalBits = BitOffset + offset;
            return new MitsubishiSerialAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + totalBits / 16,
                BitOffset = totalBits % 16,
                IsBitDevice = false
            };
        }

        public static MitsubishiSerialAddress Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Mitsubishi 串口地址不能为空。", "text");

            string address = text.Trim().ToUpperInvariant();
            Match match = Regex.Match(address, @"^([A-Z]+)([0-9A-FX]+)(?:\.(\d+))?$");
            if (!match.Success)
                throw new FormatException("不支持的 Mitsubishi 串口地址格式: " + text);

            DeviceInfo device = GetDeviceInfo(match.Groups[1].Value);
            int number = ParseNumber(device, match.Groups[2].Value);
            int bit = 0;
            if (match.Groups[3].Success)
            {
                bit = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (bit < 0 || bit > 15)
                    throw new FormatException("字设备位偏移必须在 0 到 15 之间。");
            }

            return new MitsubishiSerialAddress
            {
                DeviceName = device.Name,
                DeviceCode = device.Code,
                DeviceNumber = number,
                BitOffset = bit,
                IsBitDevice = device.IsBitDevice
            };
        }

        private static int ParseNumber(DeviceInfo device, string text)
        {
            string value = text;
            if (value.StartsWith("0X", StringComparison.Ordinal))
                return int.Parse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (device.OctalNumber)
                return Convert.ToInt32(value, 8);
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        private static DeviceInfo GetDeviceInfo(string name)
        {
            switch (name)
            {
                case "X":
                    return new DeviceInfo("X", 'X', true, true);
                case "Y":
                    return new DeviceInfo("Y", 'Y', true, true);
                case "M":
                    return new DeviceInfo("M", 'M', true, false);
                case "S":
                    return new DeviceInfo("S", 'S', true, false);
                case "D":
                    return new DeviceInfo("D", 'D', false, false);
                case "R":
                    return new DeviceInfo("R", 'R', false, false);
                case "T":
                case "TN":
                    return new DeviceInfo("TN", 'T', false, false);
                case "C":
                case "CN":
                    return new DeviceInfo("CN", 'C', false, false);
                default:
                    throw new FormatException("不支持的 Mitsubishi 串口软元件: " + name);
            }
        }

        
        
        
        
        
        
        
        
        
        private sealed class DeviceInfo
        {
            public DeviceInfo(string name, char code, bool isBitDevice, bool octalNumber)
            {
                Name = name;
                Code = code;
                IsBitDevice = isBitDevice;
                OctalNumber = octalNumber;
            }

            public string Name { get; private set; }
            public char Code { get; private set; }
            public bool IsBitDevice { get; private set; }
            public bool OctalNumber { get; private set; }
        }
    }
}
