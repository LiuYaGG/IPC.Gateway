using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly CompiledTagRead[] _readOrder;

        private CompiledDeviceReadPlan(Dictionary<string, CompiledTagRead> entries)
        {
            _entries = entries;
            // Stable for drivers without an address layout. Sort before selecting a
            // bounded turn, so a UI group boundary cannot fragment a physical run.
            _readOrder = entries.Values.OrderBy(entry => entry.ReadArea == null)
                .ThenBy(entry => entry.ReadArea, StringComparer.Ordinal)
                .ThenBy(entry => entry.ReadPosition).ToArray();
            var firstByRequest = new Dictionary<(string, PlcDataType, int, int), CompiledTagRead>();
            for (int i = 0; i < _readOrder.Length; i++)
            {
                var entry = _readOrder[i];
                entry.Ordinal = i;
                if (entry.ReadArea == null) continue;
                var request = entry.Request;
                var key = (request.Address, request.DataType, request.ElementCount, request.ElementOffset);
                if (firstByRequest.TryGetValue(key, out var first))
                {
                    entry.Request = first.Request;
                    entry.SharesRequest = first.SharesRequest = true;
                }
                else firstByRequest.Add(key, entry);
            }
        }

        public static CompiledDeviceReadPlan Compile(DeviceConfig? device, CompiledDeviceReadPlan? previous = null)
        {
            Dictionary<string, CompiledTagRead> entries =
                new Dictionary<string, CompiledTagRead>(StringComparer.OrdinalIgnoreCase);
            AddTags(device, null, device == null ? null : device.Tags, entries);
            if (device?.Groups != null)
            {
                foreach (GroupConfig group in device.Groups)
                    AddTags(device, group, group?.Tags, entries);
            }
            if (previous != null)
            {
                foreach (KeyValuePair<string, CompiledTagRead> pair in entries)
                {
                    if (previous._entries.TryGetValue(pair.Key, out CompiledTagRead? oldEntry) &&
                        pair.Value.HasSameReadDefinition(oldEntry))
                        pair.Value.PreserveRuntime(oldEntry);
                }
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

        public IReadOnlyList<CompiledTagRead> Entries => _readOrder;

        public bool TryGet(string tagId, out CompiledTagRead? entry) => _entries.TryGetValue(tagId, out entry);

        public CompiledTagRead? FindRecoveryProbe(string preferredTagId, string previousProbeTagId, DateTime nowUtc)
        {
            // A newly added/corrected point gets a first attempt before another
            // retry of a point that may have caused device isolation.
            CompiledTagRead? unattempted = null;
            foreach (CompiledTagRead entry in _entries.Values)
            {
                if (CanProbe(entry, nowUtc) && entry.Runtime.LastReadAttemptUtc == DateTime.MinValue)
                {
                    if (!entry.PreservesReadState)
                        return entry;
                    unattempted ??= entry;
                }
            }
            if (unattempted != null)
                return unattempted;

            if (string.IsNullOrWhiteSpace(previousProbeTagId) && !string.IsNullOrWhiteSpace(preferredTagId) &&
                _entries.TryGetValue(preferredTagId, out CompiledTagRead? preferred) &&
                CanProbe(preferred, nowUtc))
                return preferred;

            CompiledTagRead? first = null;
            bool afterPrevious = string.IsNullOrWhiteSpace(previousProbeTagId);
            foreach (CompiledTagRead entry in _entries.Values)
            {
                if (CanProbe(entry, nowUtc))
                {
                    first ??= entry;
                    if (afterPrevious)
                        return entry;
                }
                if (string.Equals(GetTagKey(entry.Tag), previousProbeTagId, StringComparison.OrdinalIgnoreCase))
                    afterPrevious = true;
            }
            return first;
        }

        private static bool CanProbe(CompiledTagRead entry, DateTime nowUtc)
        {
            return entry.IsStaticallyValid && entry.Tag.Enabled && !entry.Tag.IsVirtual &&
                   entry.Tag.AccessMode != TagAccessMode.WriteOnly &&
                   (entry.Group == null || entry.Group.Enabled) && entry.Runtime.CanProbe(nowUtc);
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
                string error = ValidateTagDefinition(device, tag, address, out var validation);
                var entry = string.IsNullOrEmpty(error)
                    ? CompiledTagRead.Valid(group, tag, address)
                    : CompiledTagRead.Invalid(tag, address, error, group);
                entry.ReadArea = validation?.ReadArea;
                entry.ReadPosition = validation?.ReadPosition ?? 0;
                entries[GetTagKey(tag)] = entry;
            }
        }

        internal static string ValidateTagDefinition(DeviceConfig? device, TagConfig tag, string? resolvedAddress = null) =>
            ValidateTagDefinition(device, tag, resolvedAddress ?? ResolveAddress(device, tag), out _);

        private static string ValidateTagDefinition(DeviceConfig? device, TagConfig tag, string address, out PlcTagValidationResult? validation)
        {
            validation = null;
            if (device?.Connection != null &&
                PlcDriverPluginRegistry.TryValidateTag(
                    device.Connection,
                    device.Protocol,
                    address,
                    tag.DataType,
                    tag.ElementCount,
                    tag.ElementOffset,
                    out PlcTagValidationResult driverValidation))
            {
                validation = driverValidation;
                return driverValidation.IsValid ? string.Empty : driverValidation.ErrorMessage;
            }

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
                    case PlcProtocol.EtherNetIp:
                        if (!Regex.IsMatch(address, @"^@(0[xX][0-9A-Fa-f]+|[0-9]+)/(0[xX][0-9A-Fa-f]+|[0-9]+)/(0[xX][0-9A-Fa-f]+|[0-9]+)(/(0[xX][0-9A-Fa-f]+|[0-9]+))?$") &&
                            !Regex.IsMatch(address, @"^(Assembly|InputAssembly|OutputAssembly|ConfigAssembly):(0[xX][0-9A-Fa-f]+|[0-9]+)(:(0[xX][0-9A-Fa-f]+|[0-9]+))?$", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(address, @"^(Input|Output):[0-9]+(\.[0-7])?$", RegexOptions.IgnoreCase))
                            return "EtherNet/IP 地址格式无效。";
                        break;
                    case PlcProtocol.RockwellCip:
                        if (!IsValidCipPath(address))
                            return "AB/CIP 标签路径格式无效。";
                        break;
                    case PlcProtocol.RockwellPccc:
                        if (!Regex.IsMatch(address, @"^(ST|[NBFLOTICSR])\d+:\d+(\.[A-Z0-9]+)?(/\d+)?$", RegexOptions.IgnoreCase))
                            return "AB/PCCC地址格式无效。";
                        break;
                    case PlcProtocol.ModbusTcp:
                    case PlcProtocol.ModbusUdp:
                    case PlcProtocol.ModbusRtu:
                    case PlcProtocol.ModbusAscii:
                        _ = ModbusAddress.Parse(address, tag.DataType);
                        break;
                    case PlcProtocol.Dlt6452007:
                        _ = Dlt645Address.Parse(address);
                        break;
                    case PlcProtocol.Cjt1882004:
                    case PlcProtocol.Cjt1882018:
                        _ = Cjt188Address.Parse(address);
                        break;
                    case PlcProtocol.SiemensS7:
                        if (!Regex.IsMatch(address, @"^(DB\d+\.DB[XBWDL]\d+(\.\d+)?|[MIQVEA][XBWDL]?\d+(\.\d+)?)$", RegexOptions.IgnoreCase))
                            return "西门子地址格式无效。";
                        if (Regex.IsMatch(address, @"(?:DBX|[MIQVEA]X?)[0-9]+\.[0-9]+$", RegexOptions.IgnoreCase) &&
                            tag.DataType != PlcDataType.Bool && tag.DataType != PlcDataType.BoolArray)
                            return "带 .bit 后缀的 S7 地址必须使用 Bool 或 BoolArray。";
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
                        if (!Regex.IsMatch(address, @"^((0X)?[0-9A-F]{1,3}[:/])?(0X)?[0-9A-F]{1,4}[:/.](0X)?[0-9A-F]{1,2}$", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(address, @"^(TPDO[1-4]|RPDO[1-4]):([1-9]|[1-9][0-9]|1[01][0-9]|12[0-7])(:[0-7](\.[0-7])?)?$", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(address, @"^(Heartbeat|EMCY|NMT):([1-9]|[1-9][0-9]|1[01][0-9]|12[0-7])$", RegexOptions.IgnoreCase) &&
                            !Regex.IsMatch(address, @"^(SYNC|TIME)$", RegexOptions.IgnoreCase))
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

        internal static string ResolveAddress(DeviceConfig? device, TagConfig tag)
        {
            if (!string.IsNullOrWhiteSpace(tag.Address))
                return tag.Address.Trim();
            if (device?.Protocol == PlcProtocol.Dlt6452007)
                return "DLT645:" + (tag.MeterAddress ?? string.Empty).Trim() + ":" + (tag.MeterDataIdentifier ?? string.Empty).Trim();
            if (device?.Protocol == PlcProtocol.Cjt1882004 || device?.Protocol == PlcProtocol.Cjt1882018)
            {
                string meterType = NormalizeCjtMeterType(tag.MeterType);
                meterType = string.IsNullOrWhiteSpace(meterType) ? string.Empty : meterType + ":";
                return "CJ188:" + meterType + (tag.MeterAddress ?? string.Empty).Trim() + ":" + (tag.MeterDataIdentifier ?? string.Empty).Trim();
            }
            return string.Empty;
        }

        private static string NormalizeCjtMeterType(string? meterType)
        {
            string value = (meterType ?? string.Empty).Trim();
            if (Regex.IsMatch(value, "^[0-9A-Fa-f]{2}$"))
                return value.ToUpperInvariant();
            return value switch
            {
                "水表" or "冷水表" => "10",
                "热水表" => "11",
                "直饮水表" => "12",
                "中水表" => "13",
                "热量表" => "20",
                "冷量表" => "21",
                "冷热量表" => "22",
                "燃气表" => "30",
                _ => value
            };
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
        public PlcBatchReadRequest Request { get; internal set; }
        public string? ReadArea { get; internal set; }
        public long ReadPosition { get; internal set; }
        public bool SharesRequest { get; internal set; }
        public int Ordinal { get; internal set; }
        public TagRuntimeState Runtime { get; private set; }
        public bool PreservesReadState { get; private set; }
        public bool PreservesSchedule { get; private set; }

        public bool HasSameReadDefinition(CompiledTagRead previous)
        {
            return IsStaticallyValid == previous.IsStaticallyValid &&
                   string.Equals(ValidationError, previous.ValidationError, StringComparison.Ordinal) &&
                   HasSameSource(Tag, Address, previous.Tag, previous.Address) &&
                   Tag.Enabled == previous.Tag.Enabled && (Group?.Enabled ?? true) == (previous.Group?.Enabled ?? true) &&
                   (Tag.AccessMode == TagAccessMode.WriteOnly) == (previous.Tag.AccessMode == TagAccessMode.WriteOnly) &&
                   string.Equals(Group?.Id ?? string.Empty, previous.Group?.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasSameSource(TagConfig tag, string address, TagConfig other, string otherAddress)
        {
            return string.Equals(address, otherAddress, StringComparison.Ordinal) &&
                   tag.DataType == other.DataType && tag.ElementCount == other.ElementCount && tag.ElementOffset == other.ElementOffset &&
                   tag.IsVirtual == other.IsVirtual &&
                   (!tag.IsVirtual || System.Text.Json.JsonSerializer.Serialize(tag.VirtualModel) == System.Text.Json.JsonSerializer.Serialize(other.VirtualModel));
        }

        public void PreserveRuntime(CompiledTagRead previous)
        {
            // Share the state with an in-flight old-generation read rather than
            // copy a partially updated failure count/recovery deadline.
            Runtime = previous.Runtime;
            PreservesReadState = true;
            PreservesSchedule = Tag.ScanRateMs == previous.Tag.ScanRateMs &&
                                Tag.FailureRetryDelayMs == previous.Tag.FailureRetryDelayMs &&
                                (Group?.ScanRateMs ?? 0) == (previous.Group?.ScanRateMs ?? 0);
        }

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
            FirstQueuedUtc = DateTime.UtcNow;
        }

        public bool IsIsolated { get; private set; }
        public bool IsStaticIsolation { get; }
        public int ConsecutiveFailures { get; private set; }
        public DateTime NextRecoveryProbeUtc { get; private set; }
        public DateTime LastSuccessUtc { get; private set; }
        public DateTime LastFailureUtc { get; private set; }
        public DateTime LastSourceTimestampUtc { get; set; }
        public DateTime LastReadAttemptUtc { get; private set; }
        public DateTime FirstQueuedUtc { get; }
        // A timeout or an undecodable batch is not proof of an invalid tag.
        // Keep it out of the healthy batch until an independent read succeeds.
        public bool NeedsIndependentProbe { get; private set; }
        public bool BatchVerificationPending { get; set; }
        public bool HasRecoveryWork => IsIsolated || NeedsIndependentProbe;
        public int RecoveryIntervalMs { get; set; } = 1000;
        public int ConsecutiveDeferredReads { get; private set; }
        public bool HasSuccessfulRead { get; private set; }
        public DateTime HealthyReadBaselineUtc { get; set; }
        public string LastError { get; private set; }

        public bool CanProbe(DateTime nowUtc) => !HasRecoveryWork || (!IsStaticIsolation && nowUtc >= NextRecoveryProbeUtc);

        public void RecordReadAttempt() => LastReadAttemptUtc = DateTime.UtcNow;

        public void RecordSuccess()
        {
            RecordReadAttempt();
            IsIsolated = false;
            NeedsIndependentProbe = false;
            BatchVerificationPending = false;
            ConsecutiveDeferredReads = 0;
            ConsecutiveFailures = 0;
            NextRecoveryProbeUtc = DateTime.MinValue;
            LastSuccessUtc = DateTime.UtcNow;
            HasSuccessfulRead = true;
            HealthyReadBaselineUtc = LastSuccessUtc;
            LastError = string.Empty;
        }

        public void RecordFailure(string message)
        {
            ResetHealthyBaseline();
            ConsecutiveDeferredReads = 0;
            RecordReadAttempt();
            ConsecutiveFailures++;
            LastFailureUtc = DateTime.UtcNow;
            LastError = message ?? string.Empty;
            IsIsolated = true;
            NeedsIndependentProbe = false;
            BatchVerificationPending = false;
            NextRecoveryProbeUtc = DateTime.UtcNow.AddMilliseconds(Math.Clamp(RecoveryIntervalMs, 100, 60000));
        }

        public void RecordUncertainFailure(string message, int recoveryIntervalMs = 1000)
        {
            ResetHealthyBaseline();
            ConsecutiveDeferredReads = 0;
            RecordReadAttempt();
            LastFailureUtc = DateTime.UtcNow;
            LastError = message ?? string.Empty;
            NeedsIndependentProbe = true;
            BatchVerificationPending = false;
            if (!IsStaticIsolation)
                NextRecoveryProbeUtc = DateTime.UtcNow.AddMilliseconds(Math.Clamp(recoveryIntervalMs, 100, 60000));
        }

        public void DeferProbe(DateTime nextProbeUtc)
        {
            if (HasRecoveryWork && !IsStaticIsolation && nextProbeUtc > NextRecoveryProbeUtc)
                NextRecoveryProbeUtc = nextProbeUtc;
        }

        public void ResetHealthyBaseline() => HealthyReadBaselineUtc = DateTime.MinValue;

        public void RecordDeferred(DateTime nextAttemptUtc)
        {
            ConsecutiveDeferredReads = Math.Min(3, ConsecutiveDeferredReads + 1);
            if (ConsecutiveDeferredReads >= 3 && !IsStaticIsolation)
            {
                // A plugin that keeps returning no item result must not leave
                // a point uninitialized forever. This is verification, not a
                // tag failure or an actual read attempt.
                NeedsIndependentProbe = true;
                BatchVerificationPending = false;
                NextRecoveryProbeUtc = nextAttemptUtc;
            }
            else DeferProbe(nextAttemptUtc);
        }
    }
}
