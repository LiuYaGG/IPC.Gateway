/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OmronFins
* 项目描述 ：
* 类 名 称 ：FinsAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OmronFins
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

namespace IPC.Plc.Communication.OmronFins
{
    
    
    
    
    
    
    
    
    
    internal sealed class FinsAddress
    {
        public FinsAddress(FinsMemoryArea area, int wordAddress, int bitIndex)
        {
            Area = area;
            WordAddress = wordAddress;
            BitIndex = bitIndex;
        }

        public FinsMemoryArea Area { get; private set; }
        public int WordAddress { get; private set; }
        public int BitIndex { get; private set; }

        public bool HasBitIndex
        {
            get { return BitIndex >= 0; }
        }

        public FinsAddress OffsetBits(int bitOffset)
        {
            int absoluteBit = (HasBitIndex ? BitIndex : 0) + bitOffset;
            return new FinsAddress(Area, WordAddress + absoluteBit / 16, absoluteBit % 16);
        }

        public FinsAddress OffsetWords(int wordOffset)
        {
            return new FinsAddress(Area, WordAddress + wordOffset, BitIndex);
        }

        public static FinsAddress Parse(string text, PlcDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("FINS 地址不能为空。");

            string value = text.Trim().ToUpperInvariant();
            int bitIndex = -1;
            int dot = value.LastIndexOf('.');
            if (dot >= 0)
            {
                bitIndex = int.Parse(value.Substring(dot + 1), CultureInfo.InvariantCulture);
                if (bitIndex < 0 || bitIndex > 15)
                    throw new FormatException("FINS 位地址必须是 0 到 15。");
                value = value.Substring(0, dot);
            }

            FinsMemoryArea area;
            string numberText;
            SplitArea(value, out area, out numberText);

            int wordAddress = int.Parse(numberText, CultureInfo.InvariantCulture);
            if (wordAddress < 0 || wordAddress > 0xFFFF)
                throw new FormatException("FINS 字地址必须在 0 到 65535 之间。");

            if ((dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray) && bitIndex < 0)
                bitIndex = 0;

            return new FinsAddress(area, wordAddress, bitIndex);
        }

        private static void SplitArea(string value, out FinsMemoryArea area, out string numberText)
        {
            string[] prefixes = new[] { "CIO", "WR", "HR", "AR", "DM", "EM", "W", "H", "A", "D", "E", "C" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                area = GetArea(prefix);
                numberText = value.Substring(prefix.Length);
                if (string.IsNullOrWhiteSpace(numberText))
                    throw new FormatException("FINS 地址缺少字编号。");

                if (area.Name == "EM")
                    numberText = RemoveEmBank(numberText);
                return;
            }

            area = GetArea("DM");
            numberText = value;
        }

        private static string RemoveEmBank(string numberText)
        {
            if (numberText.StartsWith("_", StringComparison.Ordinal))
                return numberText.Substring(1);

            int underscore = numberText.IndexOf('_');
            if (underscore >= 0)
                return numberText.Substring(underscore + 1);

            return numberText;
        }

        private static FinsMemoryArea GetArea(string prefix)
        {
            switch (prefix)
            {
                case "CIO":
                case "C":
                    return new FinsMemoryArea("CIO", 0xB0, 0x30);
                case "WR":
                case "W":
                    return new FinsMemoryArea("WR", 0xB1, 0x31);
                case "HR":
                case "H":
                    return new FinsMemoryArea("HR", 0xB2, 0x32);
                case "AR":
                case "A":
                    return new FinsMemoryArea("AR", 0xB3, 0x33);
                case "DM":
                case "D":
                    return new FinsMemoryArea("DM", 0x82, 0x02);
                case "EM":
                case "E":
                    return new FinsMemoryArea("EM", 0xA0, 0x20);
                default:
                    throw new FormatException("不支持的 FINS 区域: " + prefix);
            }
        }
    }
}
