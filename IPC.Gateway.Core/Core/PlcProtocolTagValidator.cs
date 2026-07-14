using System;
using System.Text.RegularExpressions;
using IPC.Plc.Communication.Metering.Cjt188;
using IPC.Plc.Communication.Metering.Dlt645;
using IPC.Plc.Communication.ModbusTcp;

namespace IPC.Plc.Communication.Core
{
    public static class PlcProtocolTagValidator
    {
        public static PlcTagValidationResult Validate(
            PlcProtocol protocol,
            string address,
            PlcDataType dataType,
            int elementCount,
            int elementOffset)
        {
            string normalizedAddress = (address ?? string.Empty).Trim();
            if (normalizedAddress.Length == 0)
                return PlcTagValidationResult.Invalid(normalizedAddress, "标签地址不能为空。");
            if (elementCount <= 0)
                return PlcTagValidationResult.Invalid(normalizedAddress, "标签元素数量必须大于 0。");
            if (elementOffset < 0)
                return PlcTagValidationResult.Invalid(normalizedAddress, "标签元素偏移不能小于 0。");

            try
            {
                switch (protocol)
                {
                    case PlcProtocol.RockwellCip:
                        if (!IsValidCipPath(normalizedAddress))
                            return Invalid("AB/CIP 标签路径格式无效。");
                        break;
                    case PlcProtocol.ModbusTcp:
                    case PlcProtocol.ModbusRtu:
                        _ = ModbusAddress.Parse(normalizedAddress, dataType);
                        break;
                    case PlcProtocol.Dlt6452007:
                        _ = Dlt645Address.Parse(normalizedAddress);
                        break;
                    case PlcProtocol.Cjt1882004:
                        _ = Cjt188Address.Parse(normalizedAddress);
                        break;
                    case PlcProtocol.SiemensS7:
                        if (!Regex.IsMatch(normalizedAddress, @"^(DB\d+\.DB[XBWDL]\d+(\.\d+)?|[MIQV][XBWDL]?\d+(\.\d+)?)$", RegexOptions.IgnoreCase))
                            return Invalid("西门子地址格式无效。");
                        break;
                    case PlcProtocol.MitsubishiMc:
                    case PlcProtocol.MitsubishiMc1E:
                    case PlcProtocol.MitsubishiSerial:
                    case PlcProtocol.MitsubishiQlSerial:
                        if (!Regex.IsMatch(normalizedAddress, @"^[A-Z]+[0-9A-FX]+(\.\d+)?$", RegexOptions.IgnoreCase))
                            return Invalid("三菱地址格式无效。");
                        break;
                    case PlcProtocol.OmronFins:
                        if (!Regex.IsMatch(normalizedAddress, @"^[A-Z0-9_]+(\.\d+)?$", RegexOptions.IgnoreCase))
                            return Invalid("欧姆龙 FINS 地址格式无效。");
                        break;
                    case PlcProtocol.CanOpen:
                        if (!Regex.IsMatch(normalizedAddress, @"^((0X)?[0-9A-F]{1,3}[:/])?(0X)?[0-9A-F]{1,4}[:/.](0X)?[0-9A-F]{1,2}$", RegexOptions.IgnoreCase))
                            return Invalid("CANopen 对象字典地址格式无效。");
                        break;
                    case PlcProtocol.BacnetIp:
                        if (!IsValidBacnetAddress(normalizedAddress))
                            return Invalid("BACnet 地址格式无效，应为 objectType:instance[:property[:arrayIndex]]。");
                        break;
                    case PlcProtocol.OpcUa:
                        if (!Regex.IsMatch(normalizedAddress, @"^((ns=\d+|nsu=.+);)?[isgb]=.+$", RegexOptions.IgnoreCase))
                            return Invalid("OPC UA NodeId 格式无效。");
                        break;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                return PlcTagValidationResult.Invalid(normalizedAddress, ex.Message);
            }

            return PlcTagValidationResult.Valid(normalizedAddress);

            PlcTagValidationResult Invalid(string message) =>
                PlcTagValidationResult.Invalid(normalizedAddress, message);
        }

        private static bool IsValidCipPath(string address)
        {
            string[] segments = address.Split('.');
            foreach (string segment in segments)
            {
                if (string.IsNullOrWhiteSpace(segment))
                    return false;

                int position = 0;
                while (position < segment.Length && segment[position] != '[')
                    position++;
                if (position == 0)
                    return false;

                while (position < segment.Length)
                {
                    if (segment[position] != '[')
                        return false;
                    int end = segment.IndexOf(']', position + 1);
                    if (end < 0)
                        return false;
                    string[] indexes = segment.Substring(position + 1, end - position - 1).Split(',');
                    foreach (string index in indexes)
                    {
                        if (!int.TryParse(index.Trim(), out int value) || value < 0)
                            return false;
                    }
                    position = end + 1;
                }
            }
            return true;
        }

        private static bool IsValidBacnetAddress(string address)
        {
            string[] parts = address.Split(new[] { ':', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 4 || string.IsNullOrWhiteSpace(parts[0]))
                return false;
            if (!uint.TryParse(parts[1], out _))
                return false;
            if (parts.Length >= 3 && string.IsNullOrWhiteSpace(parts[2]))
                return false;
            return parts.Length < 4 || uint.TryParse(parts[3], out _);
        }
    }
}
