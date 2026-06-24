/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.ModbusTcp
* 项目描述 ：
* 类 名 称 ：ModbusAddress
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.ModbusTcp
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

namespace IPC.Plc.Communication.ModbusTcp
{
    
    
    
    
    
    
    
    
    
    internal sealed class ModbusAddress
    {
        public ModbusAddress(ModbusArea area, int address, int bitIndex)
        {
            Area = area;
            Address = address;
            BitIndex = bitIndex;
        }

        public ModbusArea Area { get; private set; }
        public int Address { get; private set; }
        public int BitIndex { get; private set; }

        public bool HasBitIndex
        {
            get { return BitIndex >= 0; }
        }

        public bool IsBitArea
        {
            get { return Area == ModbusArea.Coil || Area == ModbusArea.DiscreteInput; }
        }

        public bool IsReadOnly
        {
            get { return Area == ModbusArea.DiscreteInput || Area == ModbusArea.InputRegister; }
        }

        public ModbusAddress OffsetBits(int bitOffset)
        {
            if (IsBitArea)
                return new ModbusAddress(Area, Address + bitOffset, -1);

            int absoluteBit = (HasBitIndex ? BitIndex : 0) + bitOffset;
            return new ModbusAddress(Area, Address + absoluteBit / 16, absoluteBit % 16);
        }

        public ModbusAddress OffsetRegisters(int registerOffset)
        {
            return new ModbusAddress(Area, Address + registerOffset, BitIndex);
        }

        public static ModbusAddress Parse(string text, PlcDataType dataType)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Modbus 地址不能为空。");

            string value = text.Trim().ToUpperInvariant();
            int bitIndex = -1;
            int dot = value.LastIndexOf('.');
            if (dot >= 0)
            {
                bitIndex = int.Parse(value.Substring(dot + 1), CultureInfo.InvariantCulture);
                if (bitIndex < 0 || bitIndex > 15)
                    throw new FormatException("Modbus 寄存器位地址必须是 0 到 15。");
                value = value.Substring(0, dot);
            }

            ModbusArea area;
            string numberText;
            bool oneBased;
            if (TrySplitPrefixedAddress(value, out area, out numberText, out oneBased))
                return new ModbusAddress(area, ParseNumber(numberText, oneBased), bitIndex);

            area = IsClassicReference(value) ? GetClassicArea(value[0]) : GetDefaultArea(dataType);
            oneBased = false;
            return new ModbusAddress(area, ParseNumber(value, oneBased), bitIndex);
        }

        private static bool TrySplitPrefixedAddress(string value, out ModbusArea area, out string numberText, out bool oneBased)
        {
            area = ModbusArea.HoldingRegister;
            numberText = value;
            oneBased = false;

            string[] prefixes = new[]
            {
                "DISCRETE", "INPUTREG", "HOLDING", "COIL", "IR", "AI", "HR", "DI", "0X", "1X", "3X", "4X", "C", "Q", "R", "D"
            };

            for (int i = 0; i < prefixes.Length; i++)
            {
                string prefix = prefixes[i];
                if (!value.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                numberText = value.Substring(prefix.Length);
                if (string.IsNullOrEmpty(numberText))
                    continue;

                switch (prefix)
                {
                    case "COIL":
                    case "C":
                    case "Q":
                    case "0X":
                        area = ModbusArea.Coil;
                        oneBased = prefix == "0X";
                        return true;
                    case "DISCRETE":
                    case "DI":
                    case "1X":
                        area = ModbusArea.DiscreteInput;
                        oneBased = prefix == "1X";
                        return true;
                    case "INPUTREG":
                    case "IR":
                    case "AI":
                    case "3X":
                        area = ModbusArea.InputRegister;
                        oneBased = prefix == "3X";
                        return true;
                    case "HOLDING":
                    case "HR":
                    case "R":
                    case "D":
                    case "4X":
                        area = ModbusArea.HoldingRegister;
                        oneBased = prefix == "4X";
                        return true;
                }
            }

            return false;
        }

        private static int ParseNumber(string text, bool oneBased)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new FormatException("Modbus 地址缺少编号。");

            int value = int.Parse(text, CultureInfo.InvariantCulture);
            if (!oneBased)
            {
                if (IsClassicReference(text))
                    return ParseClassicReference(text, value);
                if (value < 0)
                    throw new FormatException("Modbus 地址不能小于 0。");
                return value;
            }

            int zeroBased = value - 1;
            if (zeroBased < 0)
                throw new FormatException("Modbus 1 基地址必须大于等于 1。");
            return zeroBased;
        }

        private static bool IsClassicReference(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 5)
                return false;

            char first = text[0];
            return first == '0' || first == '1' || first == '3' || first == '4';
        }

        private static int ParseClassicReference(string text, int value)
        {
            char first = text[0];
            int baseValue;
            if (text.Length >= 6)
                baseValue = int.Parse(first + "00001", CultureInfo.InvariantCulture);
            else
                baseValue = int.Parse(first + "0001", CultureInfo.InvariantCulture);

            int result = value - baseValue;
            if (result < 0)
                throw new FormatException("Modbus 传统引用地址格式不正确。");
            return result;
        }

        private static ModbusArea GetDefaultArea(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Coil:
                case PlcDataType.CoilArray:
                    return ModbusArea.Coil;
                case PlcDataType.DiscreteInput:
                case PlcDataType.DiscreteInputArray:
                    return ModbusArea.DiscreteInput;
                case PlcDataType.Bool:
                case PlcDataType.BoolArray:
                    return ModbusArea.Coil;
                default:
                    return ModbusArea.HoldingRegister;
            }
        }

        private static ModbusArea GetClassicArea(char first)
        {
            switch (first)
            {
                case '0':
                    return ModbusArea.Coil;
                case '1':
                    return ModbusArea.DiscreteInput;
                case '3':
                    return ModbusArea.InputRegister;
                case '4':
                    return ModbusArea.HoldingRegister;
                default:
                    return ModbusArea.HoldingRegister;
            }
        }
    }
}
