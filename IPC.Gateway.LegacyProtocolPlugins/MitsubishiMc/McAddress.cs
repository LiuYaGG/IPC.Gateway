/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiMc
* 项目描述 ：
* 类 名 称 ：McAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiMc
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
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.MitsubishiMc
{
    
    
    
    
    
    
    
    
    
    internal sealed class McAddress
    {
        public string DeviceName { get; private set; }
        public byte DeviceCode { get; private set; }
        public int DeviceNumber { get; private set; }
        public int BitOffset { get; private set; }
        public bool HasBitOffset { get; private set; }
        public bool IsBitDevice { get; private set; }

        public McAddress AddDeviceOffset(int offset)
        {
            return new McAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + offset,
                BitOffset = BitOffset,
                HasBitOffset = HasBitOffset,
                IsBitDevice = IsBitDevice
            };
        }

        public McAddress AddBitOffset(int offset)
        {
            if (IsBitDevice)
                return AddDeviceOffset(offset);

            int totalBits = BitOffset + offset;
            return new McAddress
            {
                DeviceName = DeviceName,
                DeviceCode = DeviceCode,
                DeviceNumber = DeviceNumber + totalBits / 16,
                BitOffset = totalBits % 16,
                HasBitOffset = true,
                IsBitDevice = false
            };
        }

        public static McAddress Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("MC 地址不能为空。", "text");

            string address = text.Trim().ToUpperInvariant();
            Match match = Regex.Match(address, @"^(ZR|[A-Z]+)([0-9A-FX]+)(?:\.(\d+))?$");
            if (!match.Success)
                throw new FormatException("不支持的 MC 地址格式: " + text);

            DeviceInfo device = GetDeviceInfo(match.Groups[1].Value);
            int number = ParseDeviceNumber(device, match.Groups[2].Value);
            int bit = 0;
            bool hasBit = match.Groups[3].Success;
            if (hasBit)
            {
                bit = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (bit < 0 || bit > 15)
                    throw new FormatException("字设备位偏移必须在 0 到 15 之间。");
            }

            return new McAddress
            {
                DeviceName = device.Name,
                DeviceCode = device.Code,
                DeviceNumber = number,
                BitOffset = bit,
                HasBitOffset = hasBit,
                IsBitDevice = device.IsBitDevice
            };
        }

        private static int ParseDeviceNumber(DeviceInfo device, string text)
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
                    return new DeviceInfo("M", 0x90, true, false);
                case "L":
                    return new DeviceInfo("L", 0x92, true, false);
                case "F":
                    return new DeviceInfo("F", 0x93, true, false);
                case "V":
                    return new DeviceInfo("V", 0x94, true, false);
                case "X":
                    return new DeviceInfo("X", 0x9C, true, true);
                case "Y":
                    return new DeviceInfo("Y", 0x9D, true, true);
                case "B":
                    return new DeviceInfo("B", 0xA0, true, true);
                case "D":
                    return new DeviceInfo("D", 0xA8, false, false);
                case "W":
                    return new DeviceInfo("W", 0xB4, false, true);
                case "R":
                    return new DeviceInfo("R", 0xAF, false, false);
                case "ZR":
                    return new DeviceInfo("ZR", 0xB0, false, false);
                default:
                    throw new FormatException("不支持的 MC 软元件: " + name);
            }
        }

        
        
        
        
        
        
        
        
        
        private sealed class DeviceInfo
        {
            public DeviceInfo(string name, byte code, bool isBitDevice, bool hexNumber)
            {
                Name = name;
                Code = code;
                IsBitDevice = isBitDevice;
                HexNumber = hexNumber;
            }

            public string Name { get; private set; }
            public byte Code { get; private set; }
            public bool IsBitDevice { get; private set; }
            public bool HexNumber { get; private set; }
        }
    }
}
