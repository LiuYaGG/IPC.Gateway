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
            if (Area.BitAddressUsesWordIndex)
                return new FinsAddress(Area, checked(WordAddress + bitOffset), 0);

            int absoluteBit = (HasBitIndex ? BitIndex : 0) + bitOffset;
            return new FinsAddress(Area, WordAddress + absoluteBit / 16, absoluteBit % 16);
        }

        public FinsAddress OffsetWords(int wordOffset)
        {
            return new FinsAddress(Area, WordAddress + wordOffset, BitIndex);
        }

        public static FinsAddress Parse(string text, PlcDataType dataType)
        {
            return Parse(text, dataType, null);
        }

        public static FinsAddress Parse(string text, PlcDataType dataType, FinsDriverOptions options)
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
            SplitArea(value, options, out area, out numberText);

            int wordAddress = int.Parse(numberText, CultureInfo.InvariantCulture);
            ValidateAddressRange(area, wordAddress);

            if (options != null && options.IsNjNx && IsTimerCounterArea(area))
                throw new NotSupportedException("NJ/NX 的 FINS 兼容内存不支持 TIM/CNT 区域，请改用发布变量或映射到 CIO/WR/HR/DM/EM。");

            if (area.BitAddressUsesWordIndex && bitIndex > 0)
                throw new FormatException("TIM/CNT 完成标志按编号寻址，不支持 .1 到 .15 位后缀。");

            bool isBitType = dataType == PlcDataType.Bool || dataType == PlcDataType.BoolArray;
            if (isBitType && !area.SupportsBit)
                throw new NotSupportedException("该 FINS 区域不支持位访问: " + area.Name);
            if (!isBitType && !area.SupportsWord)
                throw new NotSupportedException("该 FINS 区域只支持完成标志位访问: " + area.Name);

            if (isBitType && bitIndex < 0)
                bitIndex = 0;

            return new FinsAddress(area, wordAddress, bitIndex);
        }

        public void EnsureRange(int pointCount, bool bitAccess)
        {
            if (pointCount <= 0)
                throw new ArgumentOutOfRangeException("pointCount");

            int finalWord = bitAccess
                ? Area.BitAddressUsesWordIndex
                    ? checked(WordAddress + pointCount - 1)
                    : checked(WordAddress + ((HasBitIndex ? BitIndex : 0) + pointCount - 1) / 16)
                : checked(WordAddress + pointCount - 1);
            ValidateAddressRange(Area, finalWord);
        }

        private static void SplitArea(
            string value,
            FinsDriverOptions options,
            out FinsMemoryArea area,
            out string numberText)
        {
            string[] prefixes = new[] { "CIO", "WR", "HR", "AR", "DM", "EM", "TU", "CU", "W", "H", "A", "D", "E", "T", "C" };
            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                numberText = value.Substring(prefix.Length);
                if (string.IsNullOrWhiteSpace(numberText))
                    throw new FormatException("FINS 地址缺少字编号。");

                if (prefix == "EM" || prefix == "E")
                {
                    ParseEmArea(numberText, options, out area, out numberText);
                    return;
                }

                area = GetArea(prefix);
                return;
            }

            area = GetArea("DM");
            numberText = value;
        }

        private static void ParseEmArea(
            string text,
            FinsDriverOptions options,
            out FinsMemoryArea area,
            out string numberText)
        {
            int underscore = text.IndexOf('_');
            if (underscore < 0)
            {
                area = CreateEmBankArea(0, options);
                numberText = text;
                return;
            }

            string bankText = text.Substring(0, underscore);
            numberText = text.Substring(underscore + 1);
            if (numberText.Length == 0)
                throw new FormatException("EM 地址缺少字地址。");
            if (bankText.Length == 0)
            {
                area = new FinsMemoryArea("EM_CURRENT", 0x98, 0x0A, 32767);
                return;
            }

            if (!int.TryParse(bankText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int bank))
                throw new FormatException("EM 银行号必须是 0 到 18 的十六进制数。");
            area = CreateEmBankArea(bank, options);
        }

        private static FinsMemoryArea CreateEmBankArea(int bank, FinsDriverOptions options)
        {
            int maxBank = options == null ? 0x18 : options.MaxEmBank;
            if (bank < 0 || bank > maxBank || bank > 0x18)
                throw new FormatException("EM 银行号超出控制器配置范围: " + bank.ToString("X", CultureInfo.InvariantCulture));

            byte wordCode = bank <= 0x0F
                ? checked((byte)(0xA0 + bank))
                : checked((byte)(0x60 + bank - 0x10));
            byte bitCode = bank <= 0x0F
                ? checked((byte)(0x20 + bank))
                : checked((byte)(0xE0 + bank - 0x10));
            return new FinsMemoryArea("EM" + bank.ToString("X", CultureInfo.InvariantCulture), wordCode, bitCode, 32767);
        }

        private static FinsMemoryArea GetArea(string prefix)
        {
            switch (prefix)
            {
                case "CIO":
                    return new FinsMemoryArea("CIO", 0xB0, 0x30, 6143);
                case "WR":
                case "W":
                    return new FinsMemoryArea("WR", 0xB1, 0x31, 511);
                case "HR":
                case "H":
                    return new FinsMemoryArea("HR", 0xB2, 0x32, 511);
                case "AR":
                case "A":
                    return new FinsMemoryArea("AR", 0xB3, 0x33, 11535);
                case "DM":
                case "D":
                    return new FinsMemoryArea("DM", 0x82, 0x02, 32767);
                case "T":
                    return new FinsMemoryArea("TIM", 0x89, 0x09, 4095, true, true, true);
                case "C":
                    return new FinsMemoryArea("CNT", 0x89, 0x09, 4095, true, true, true);
                case "TU":
                    return new FinsMemoryArea("TU", 0x89, 0x09, 4095, false, true, true);
                case "CU":
                    return new FinsMemoryArea("CU", 0x89, 0x09, 4095, false, true, true);
                default:
                    throw new FormatException("不支持的 FINS 区域: " + prefix);
            }
        }

        private static bool IsTimerCounterArea(FinsMemoryArea area)
        {
            return area.Name == "TIM" || area.Name == "CNT" || area.Name == "TU" || area.Name == "CU";
        }

        private static void ValidateAddressRange(FinsMemoryArea area, int wordAddress)
        {
            bool valid = wordAddress >= 0 && wordAddress <= area.MaximumAddress;
            if (valid && area.Name == "AR")
                valid = wordAddress <= 1471 || wordAddress >= 10000;
            if (!valid)
                throw new FormatException("FINS 地址超出 " + area.Name + " 区域范围: " + wordAddress);
        }
    }
}
