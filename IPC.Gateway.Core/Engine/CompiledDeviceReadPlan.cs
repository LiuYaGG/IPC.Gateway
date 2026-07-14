using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Plc.Communication.Metering.Cjt188;
using IPC.Plc.Communication.Metering.Dlt645;
using IPC.Plc.Communication.ModbusTcp;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    internal sealed class CompiledDeviceReadPlan
    {
        private readonly Dictionary<string, CompiledTagRead> _entries;

        private CompiledDeviceReadPlan(Dictionary<string, CompiledTagRead> entries)
        {
            _entries = entries;
        }

        public static CompiledDeviceReadPlan Compile(DeviceConfig? device)
        {
            Dictionary<string, CompiledTagRead> entries =
                new Dictionary<string, CompiledTagRead>(StringComparer.OrdinalIgnoreCase);
            AddTags(device, null, device == null ? null : device.Tags, entries);
            if (device?.Groups != null)
            {
                foreach (GroupConfig group in device.Groups)
                    AddTags(device, group, group?.Tags, entries);
            }
            return new CompiledDeviceReadPlan(entries);
        }

        public CompiledTagRead Get(TagConfig tag)
        {
            string key = GetTagKey(tag);
            if (_entries.TryGetValue(key, out CompiledTagRead? entry))
                return entry;

            return CompiledTagRead.Invalid(tag, string.Empty, "标签不在已编译读取计划中。");
        }

        public CompiledTagRead? FindRecoveryProbe(string preferredTagId)
        {
            if (!string.IsNullOrWhiteSpace(preferredTagId) &&
                _entries.TryGetValue(preferredTagId, out CompiledTagRead? preferred) &&
                preferred.IsStaticallyValid && preferred.Tag.Enabled && (preferred.Group == null || preferred.Group.Enabled))
                return preferred;

            foreach (CompiledTagRead entry in _entries.Values)
            {
                if (entry.IsStaticallyValid && entry.Tag.Enabled &&
                    (entry.Group == null || entry.Group.Enabled) &&
                    entry.Tag.AccessMode != TagAccessMode.WriteOnly)
                    return entry;
            }
            return null;
        }

        private static void AddTags(
            DeviceConfig? device,
            GroupConfig? group,
            IList<TagConfig>? tags,
            Dictionary<string, CompiledTagRead> entries)
        {
            if (tags == null)
                return;

            foreach (TagConfig tag in tags)
            {
                if (tag == null)
                    continue;

                string address = ResolveAddress(device, tag);
                string error = ValidateTagDefinition(device, tag, address);
                entries[GetTagKey(tag)] = string.IsNullOrEmpty(error)
                    ? CompiledTagRead.Valid(group, tag, address)
                    : CompiledTagRead.Invalid(tag, address, error, group);
            }
        }

        internal static string ValidateTagDefinition(DeviceConfig? device, TagConfig tag, string? resolvedAddress = null)
        {
            string address = resolvedAddress ?? ResolveAddress(device, tag);
            if (device?.Connection != null &&
                PlcDriverPluginRegistry.TryValidateTag(
                    device.Connection,
                    device.Protocol,
                    address,
                    tag.DataType,
                    tag.ElementCount,
                    tag.ElementOffset,
                    out PlcTagValidationResult driverValidation))
                return driverValidation.IsValid ? string.Empty : driverValidation.ErrorMessage;

            if (string.IsNullOrWhiteSpace(address))
                return "标签地址不能为空。";
            if (tag.ElementCount <= 0)
                return "标签元素数量必须大于 0。";
            if (tag.ElementOffset < 0)
                return "标签元素偏移不能小于 0。";
            string driverId = device?.Connection?.DriverId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(driverId) &&
                !driverId.StartsWith("builtin.", StringComparison.OrdinalIgnoreCase) &&
                !driverId.StartsWith("legacy.", StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            try
            {
                switch (device?.Protocol ?? PlcProtocol.Plugin)
                {
                    case PlcProtocol.RockwellCip:
                        if (!IsValidCipPath(address))
                            return "AB/CIP 标签路径格式无效。";
                        break;
                    case PlcProtocol.ModbusTcp:
                    case PlcProtocol.ModbusRtu:
                        _ = ModbusAddress.Parse(address, tag.DataType);
                        break;
                    case PlcProtocol.Dlt6452007:
                        _ = Dlt645Address.Parse(address);
                        break;
                    case PlcProtocol.Cjt1882004:
                        _ = Cjt188Address.Parse(address);
                        break;
                    case PlcProtocol.SiemensS7:
                        if (!Regex.IsMatch(address, @"^(DB\d+\.DB[XBWDL]\d+(\.\d+)?|[MIQV][XBWDL]?\d+(\.\d+)?)$", RegexOptions.IgnoreCase))
                            return "西门子地址格式无效。";
                        break;
                    case PlcProtocol.MitsubishiMc:
                    case PlcProtocol.MitsubishiMc1E:
                    case PlcProtocol.MitsubishiSerial:
                    case PlcProtocol.MitsubishiQlSerial:
                        if (!Regex.IsMatch(address, @"^[A-Z]+[0-9A-FX]+(\.\d+)?$", RegexOptions.IgnoreCase))
                            return "三菱地址格式无效。";
                        break;
                    case PlcProtocol.OmronFins:
                        if (!Regex.IsMatch(address, @"^[A-Z0-9_]+(\.\d+)?$", RegexOptions.IgnoreCase))
                            return "欧姆龙 FINS 地址格式无效。";
                        break;
                    case PlcProtocol.CanOpen:
                        if (!Regex.IsMatch(address, @"^((0X)?[0-9A-F]{1,3}[:/])?(0X)?[0-9A-F]{1,4}[:/.](0X)?[0-9A-F]{1,2}$", RegexOptions.IgnoreCase))
                            return "CANopen 对象字典地址格式无效。";
                        break;
                    case PlcProtocol.BacnetIp:
                        if (!IsValidBacnetAddress(address))
                            return "BACnet 地址格式无效，应为 objectType:instance[:property[:arrayIndex]]。";
                        break;
                    case PlcProtocol.OpcUa:
                        if (!Regex.IsMatch(address, @"^((ns=\d+|nsu=.+);)?[isgb]=.+$", RegexOptions.IgnoreCase))
                            return "OPC UA NodeId 格式无效。";
                        break;
                }
            }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is OverflowException)
            {
                return ex.Message;
            }

            return string.Empty;
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

        private static string ResolveAddress(DeviceConfig? device, TagConfig tag)
        {
            if (!string.IsNullOrWhiteSpace(tag.Address))
                return tag.Address.Trim();
            if (device?.Protocol == PlcProtocol.Dlt6452007)
                return "DLT645:" + (tag.MeterAddress ?? string.Empty).Trim() + ":" + (tag.MeterDataIdentifier ?? string.Empty).Trim();
            if (device?.Protocol == PlcProtocol.Cjt1882004)
            {
                string meterType = string.IsNullOrWhiteSpace(tag.MeterType) ? string.Empty : tag.MeterType.Trim() + ":";
                return "CJ188:" + meterType + (tag.MeterAddress ?? string.Empty).Trim() + ":" + (tag.MeterDataIdentifier ?? string.Empty).Trim();
            }
            return string.Empty;
        }

        private static string GetTagKey(TagConfig tag)
        {
            return string.IsNullOrWhiteSpace(tag?.Id) ? tag?.Name ?? string.Empty : tag.Id;
        }
    }

    internal sealed class CompiledTagRead
    {
        private CompiledTagRead(GroupConfig? group, TagConfig tag, string address, bool isValid, string error)
        {
            Group = group;
            Tag = tag;
            Address = address;
            IsStaticallyValid = isValid;
            ValidationError = error;
            Runtime = new TagRuntimeState(!isValid, error);
            Request = new PlcBatchReadRequest(
                address,
                tag.DataType,
                PlcDataTypeHelper.IsArray(tag.DataType) || tag.DataType == PlcDataType.String ? Math.Max(1, tag.ElementCount) : 1,
                Math.Max(0, tag.ElementOffset));
        }

        public GroupConfig? Group { get; }
        public TagConfig Tag { get; }
        public string Address { get; }
        public bool IsStaticallyValid { get; }
        public string ValidationError { get; }
        public PlcBatchReadRequest Request { get; }
        public TagRuntimeState Runtime { get; }

        public static CompiledTagRead Valid(GroupConfig? group, TagConfig tag, string address) =>
            new CompiledTagRead(group, tag, address, true, string.Empty);

        public static CompiledTagRead Invalid(TagConfig tag, string address, string error, GroupConfig? group = null) =>
            new CompiledTagRead(group, tag, address, false, error);
    }

    internal sealed class TagRuntimeState
    {
        public TagRuntimeState(bool staticIsolation, string error)
        {
            IsIsolated = staticIsolation;
            IsStaticIsolation = staticIsolation;
            LastError = error ?? string.Empty;
            NextRecoveryProbeUtc = staticIsolation ? DateTime.MaxValue : DateTime.MinValue;
        }

        public bool IsIsolated { get; private set; }
        public bool IsStaticIsolation { get; }
        public int ConsecutiveFailures { get; private set; }
        public DateTime NextRecoveryProbeUtc { get; private set; }
        public DateTime LastSuccessUtc { get; private set; }
        public DateTime LastFailureUtc { get; private set; }
        public string LastError { get; private set; }

        public bool CanProbe(DateTime nowUtc) => !IsIsolated || (!IsStaticIsolation && nowUtc >= NextRecoveryProbeUtc);

        public void RecordSuccess()
        {
            IsIsolated = false;
            ConsecutiveFailures = 0;
            NextRecoveryProbeUtc = DateTime.MinValue;
            LastSuccessUtc = DateTime.UtcNow;
            LastError = string.Empty;
        }

        public void RecordFailure(string message)
        {
            ConsecutiveFailures++;
            LastFailureUtc = DateTime.UtcNow;
            LastError = message ?? string.Empty;
            if (ConsecutiveFailures < 3)
                return;

            IsIsolated = true;
            int exponent = Math.Min(8, ConsecutiveFailures - 3);
            int delaySeconds = Math.Min(300, 5 * (1 << exponent));
            NextRecoveryProbeUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        }
    }
}
