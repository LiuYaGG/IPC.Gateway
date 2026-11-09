/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.MitsubishiMc1E
* 项目描述 ：
* 类 名 称 ：Mc1EAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.MitsubishiMc1E
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

namespace IPC.Plc.Communication.MitsubishiMc1E
{
    
    
    
    
    
    
    
    
    
    internal sealed class Mc1EAddress
    {
        public string DeviceName { get; private set; }
        public byte Code1 { get; private set; }
        public byte Code2 { get; private set; }
        public int DeviceNumber { get; private set; }
        public int BitOffset { get; private set; }
        public bool IsBitDevice { get; private set; }

        public Mc1EAddress AddDeviceOffset(int offset)
        {
            return new Mc1EAddress
            {
                DeviceName = DeviceName,
                Code1 = Code1,
                Code2 = Code2,
                DeviceNumber = DeviceNumber + offset,
                BitOffset = BitOffset,
                IsBitDevice = IsBitDevice
            };
        }

        public Mc1EAddress AddBitOffset(int offset)
        {
            if (IsBitDevice)
                return AddDeviceOffset(offset);

            int totalBits = BitOffset + offset;
            return new Mc1EAddress
            {
                DeviceName = DeviceName,
                Code1 = Code1,
                Code2 = Code2,
                DeviceNumber = DeviceNumber + totalBits / 16,
                BitOffset = totalBits % 16,
                IsBitDevice = false
            };
        }

        public static Mc1EAddress Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("MC 1E 地址不能为空。", "text");

            string address = text.Trim().ToUpperInvariant();
            Match match = Regex.Match(address, @"^([A-Z]+)([0-9A-FX]+)(?:\.(\d+))?$");
            if (!match.Success)
                throw new FormatException("不支持的 MC 1E 地址格式: " + text);

            DeviceInfo device = GetDeviceInfo(match.Groups[1].Value);
            int number = ParseNumber(device, match.Groups[2].Value);
            int bit = 0;
            if (match.Groups[3].Success)
            {
                bit = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                if (bit < 0 || bit > 15)
                    throw new FormatException("字设备位偏移必须在 0 到 15 之间。");
            }

            return new Mc1EAddress
            {
                DeviceName = device.Name,
                Code1 = device.Code1,
                Code2 = device.Code2,
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
                    return new DeviceInfo("X", 0x20, 0x58, true, true);
                case "Y":
                    return new DeviceInfo("Y", 0x20, 0x59, true, true);
                case "M":
                    return new DeviceInfo("M", 0x20, 0x4D, true, false);
                case "S":
                    return new DeviceInfo("S", 0x20, 0x53, true, false);
                case "D":
                    return new DeviceInfo("D", 0x20, 0x44, false, false);
                case "R":
                    return new DeviceInfo("R", 0x20, 0x52, false, false);
                case "T":
                case "TN":
                    return new DeviceInfo("TN", 0x54, 0x4E, false, false);
                case "C":
                case "CN":
                    return new DeviceInfo("CN", 0x43, 0x4E, false, false);
                default:
                    throw new FormatException("不支持的 MC 1E 软元件: " + name);
            }
        }

        
        
        
        
        
        
        
        
        
        private sealed class DeviceInfo
        {
            public DeviceInfo(string name, byte code1, byte code2, bool isBitDevice, bool octalNumber)
            {
                Name = name;
                Code1 = code1;
                Code2 = code2;
                IsBitDevice = isBitDevice;
                OctalNumber = octalNumber;
            }

            public string Name { get; private set; }
            public byte Code1 { get; private set; }
            public byte Code2 { get; private set; }
            public bool IsBitDevice { get; private set; }
            public bool OctalNumber { get; private set; }
        }
    }
}
