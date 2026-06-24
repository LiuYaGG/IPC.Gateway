/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.SiemensS7
* 项目描述 ：
* 类 名 称 ：S7Address
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.SiemensS7
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

namespace IPC.Plc.Communication.SiemensS7
{
    
    
    
    
    
    
    
    
    
    internal sealed class S7Address
    {
        public byte Area { get; private set; }
        public ushort DbNumber { get; private set; }
        public int ByteOffset { get; private set; }
        public int BitOffset { get; private set; }

        public S7Address AddByteOffset(int bytes)
        {
            return new S7Address
            {
                Area = Area,
                DbNumber = DbNumber,
                ByteOffset = ByteOffset + bytes,
                BitOffset = BitOffset
            };
        }

        public S7Address AddBitOffset(int bits)
        {
            int totalBits = ByteOffset * 8 + BitOffset + bits;
            return new S7Address
            {
                Area = Area,
                DbNumber = DbNumber,
                ByteOffset = totalBits / 8,
                BitOffset = totalBits % 8
            };
        }

        public static S7Address Parse(string text, PlcDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("S7 地址不能为空。", "text");

            string address = text.Trim().ToUpperInvariant();
            S7Address parsed;
            if (TryParseDb(address, out parsed))
                return parsed;
            if (TryParseArea(address, out parsed))
                return parsed;
            if (TryParseV(address, out parsed))
                return parsed;

            throw new FormatException("不支持的 S7 地址格式: " + text);
        }

        private static bool TryParseDb(string address, out S7Address result)
        {
            result = null;
            Match match = Regex.Match(address, @"^DB(\d+)\.DB([XBWDL])(\d+)(?:\.(\d+))?$");
            if (!match.Success)
                return false;

            ushort db = ushort.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            char kind = match.Groups[2].Value[0];
            int byteOffset = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            int bitOffset = ParseBit(kind, match.Groups[4].Value);
            result = Create(0x84, db, byteOffset, bitOffset);
            return true;
        }

        private static bool TryParseArea(string address, out S7Address result)
        {
            result = null;
            Match match = Regex.Match(address, @"^([MIQ])([XBWDL])?(\d+)(?:\.(\d+))?$");
            if (!match.Success)
                return false;

            char areaName = match.Groups[1].Value[0];
            char kind = match.Groups[2].Success ? match.Groups[2].Value[0] : (match.Groups[4].Success ? 'X' : 'B');
            int byteOffset = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            int bitOffset = ParseBit(kind, match.Groups[4].Value);
            result = Create(GetArea(areaName), 0, byteOffset, bitOffset);
            return true;
        }

        private static bool TryParseV(string address, out S7Address result)
        {
            result = null;
            Match match = Regex.Match(address, @"^V([XBWDL])?(\d+)(?:\.(\d+))?$");
            if (!match.Success)
                return false;

            char kind = match.Groups[1].Success ? match.Groups[1].Value[0] : (match.Groups[3].Success ? 'X' : 'B');
            int byteOffset = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            int bitOffset = ParseBit(kind, match.Groups[3].Value);
            result = Create(0x84, 1, byteOffset, bitOffset);
            return true;
        }

        private static S7Address Create(byte area, ushort dbNumber, int byteOffset, int bitOffset)
        {
            if (byteOffset < 0)
                throw new ArgumentOutOfRangeException("byteOffset");
            if (bitOffset < 0 || bitOffset > 7)
                throw new ArgumentOutOfRangeException("bitOffset");

            return new S7Address
            {
                Area = area,
                DbNumber = dbNumber,
                ByteOffset = byteOffset,
                BitOffset = bitOffset
            };
        }

        private static int ParseBit(char kind, string bitText)
        {
            if (kind == 'X')
            {
                if (string.IsNullOrEmpty(bitText))
                    return 0;

                int bit = int.Parse(bitText, CultureInfo.InvariantCulture);
                if (bit < 0 || bit > 7)
                    throw new FormatException("S7 位地址必须在 0 到 7 之间。");
                return bit;
            }

            if (!string.IsNullOrEmpty(bitText))
                throw new FormatException("只有 X 位地址可以带 .bit 后缀。");
            return 0;
        }

        private static byte GetArea(char areaName)
        {
            switch (areaName)
            {
                case 'I':
                    return 0x81;
                case 'Q':
                    return 0x82;
                case 'M':
                    return 0x83;
                default:
                    throw new FormatException("不支持的 S7 区域: " + areaName);
            }
        }
    }
}
