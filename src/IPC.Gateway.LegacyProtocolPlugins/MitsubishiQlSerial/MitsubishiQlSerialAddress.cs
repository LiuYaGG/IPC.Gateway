/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiQlSerial
* 项目描述 ：
* 类 名 称 ：MitsubishiQlSerialAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiQlSerial
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

namespace IPC.Plc.Communication.MitsubishiQlSerial
{
    
    
    
    
    
    
    
    
    
    internal sealed class MitsubishiQlSerialAddress
    {
        public string DeviceName { get; private set; }
        public string DeviceCode { get; private set; }
        public int DeviceNumber { get; private set; }
        public int BitOffset { get; private set; }
        public bool IsBitDevice { get; private set; }
        public bool HexNumber { get; private set; }

        public MitsubishiQlSerialAddress AddDeviceOffset(int offset)
        {
            return new MitsubishiQlSerialAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + offset,
                BitOffset = BitOffset,
                IsBitDevice = IsBitDevice,
                HexNumber = HexNumber
            };
        }

        public MitsubishiQlSerialAddress AddBitOffset(int offset)
        {
            if (IsBitDevice)
                return AddDeviceOffset(offset);

            int totalBits = BitOffset + offset;
            return new MitsubishiQlSerialAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + totalBits / 16,
                BitOffset = totalBits % 16,
                IsBitDevice = false,
                HexNumber = HexNumber
            };
        }

        public string FormatDeviceNumber()
        {
            return HexNumber
                ? DeviceNumber.ToString("X6", CultureInfo.InvariantCulture)
                : DeviceNumber.ToString("D6", CultureInfo.InvariantCulture);
        }

        public static MitsubishiQlSerialAddress Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Mitsubishi Q/L 串口地址不能为空。", "text");

            string address = text.Trim().ToUpperInvariant();
            Match match = Regex.Match(address, @"^(ZR|[A-Z]+)([0-9A-FX]+)(?:\.(\d+))?$");
            if (!match.Success)
                throw new FormatException("不支持的 Mitsubishi Q/L 串口地址格式: " + text);

            DeviceInfo device = GetDeviceInfo(match.Groups[1].Value);
            int number = ParseNumber(device, match.Groups[2].Value);
            int bit = 0;
            if (match.Groups[3].Success)
            {
                bit = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (bit < 0 || bit > 15)
                    throw new FormatException("字设备位偏移必须在 0 到 15 之间。");
            }

            return new MitsubishiQlSerialAddress
            {
                DeviceName = device.Name,
                DeviceCode = device.Code,
                DeviceNumber = number,
                BitOffset = bit,
                IsBitDevice = device.IsBitDevice,
                HexNumber = device.HexNumber
            };
        }

        private static int ParseNumber(DeviceInfo device, string text)
        {
            string value = text;
            if (value.StartsWith("0X", StringComparison.Ordinal))
                return int.Parse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (device.HexNumber)
                return int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(value, CultureInfo.InvariantCulture);
        }

        private static DeviceInfo GetDeviceInfo(string name)
        {
            switch (name)
            {
                case "M":
                    return new DeviceInfo("M", "M*", true, false);
                case "L":
                    return new DeviceInfo("L", "L*", true, false);
                case "F":
                    return new DeviceInfo("F", "F*", true, false);
                case "V":
                    return new DeviceInfo("V", "V*", true, false);
                case "X":
                    return new DeviceInfo("X", "X*", true, true);
                case "Y":
                    return new DeviceInfo("Y", "Y*", true, true);
                case "B":
                    return new DeviceInfo("B", "B*", true, true);
                case "D":
                    return new DeviceInfo("D", "D*", false, false);
                case "W":
                    return new DeviceInfo("W", "W*", false, true);
                case "R":
                    return new DeviceInfo("R", "R*", false, false);
                case "ZR":
                    return new DeviceInfo("ZR", "ZR", false, false);
                default:
                    throw new FormatException("不支持的 Mitsubishi Q/L 串口软元件: " + name);
            }
        }

        
        
        
        
        
        
        
        
        
        private sealed class DeviceInfo
        {
            public DeviceInfo(string name, string code, bool isBitDevice, bool hexNumber)
            {
                Name = name;
                Code = code;
                IsBitDevice = isBitDevice;
                HexNumber = hexNumber;
            }

            public string Name { get; private set; }
            public string Code { get; private set; }
            public bool IsBitDevice { get; private set; }
            public bool HexNumber { get; private set; }
        }
    }
}
