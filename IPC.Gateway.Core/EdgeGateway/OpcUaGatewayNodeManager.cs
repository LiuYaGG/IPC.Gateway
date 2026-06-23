/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：OpcUaGatewayNodeManager
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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
using System.Collections.Generic;
using System.Globalization;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;
using Opc.Ua;
using Opc.Ua.Server;

namespace IPC.EdgeGateway
{
    internal sealed class OpcUaGatewayNodeManager : CustomNodeManager2
    {
        private readonly object _syncRoot;
        private readonly IRuntimeService _runtime;
        private readonly Func<ProjectConfig> _projectProvider;
        private readonly OpcUaServerOptions _options;
        private readonly OpcUaServerStatus _status;
        private readonly Dictionary<string, BaseDataVariableState> _tagNodes;

        public OpcUaGatewayNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            IRuntimeService runtime,
            Func<ProjectConfig> projectProvider,
            OpcUaServerOptions options,
            OpcUaServerStatus status)
            : base(server, configuration, new[] { OpcUaServerOptions.Normalize(options).NamespaceUri })
        {
            _syncRoot = new object();
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _projectProvider = projectProvider ?? throw new ArgumentNullException(nameof(projectProvider));
            _options = OpcUaServerOptions.Normalize(options);
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _tagNodes = new Dictionary<string, BaseDataVariableState>(StringComparer.OrdinalIgnoreCase);
        }

        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (_syncRoot)
            {
                _tagNodes.Clear();

                ProjectConfig project = _projectProvider() ?? new ProjectConfig();
                FolderState root = CreateFolder(null, "gateway", string.IsNullOrWhiteSpace(project.Name) ? "IPC Gateway" : project.Name.Trim());
                AddObjectsFolderReference(externalReferences, root);

                int deviceCount = 0;
                int groupCount = 0;
                int tagCount = 0;

                foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
                {
                    if (device == null)
                        continue;

                    deviceCount++;
                    FolderState deviceFolder = CreateFolder(root, "device:" + SafeId(device.Id, device.Name), DisplayName(device.Name, "Device"));

                    foreach (TagConfig tag in device.Tags ?? new List<TagConfig>())
                    {
                        if (tag == null)
                            continue;

                        AddTagVariable(deviceFolder, device, null, tag);
                        tagCount++;
                    }

                    foreach (GroupConfig group in device.Groups ?? new List<GroupConfig>())
                    {
                        if (group == null)
                            continue;

                        groupCount++;
                        FolderState groupFolder = CreateFolder(deviceFolder, "group:" + SafeId(group.Id, group.Name), DisplayName(group.Name, "Group"));
                        foreach (TagConfig tag in group.Tags ?? new List<TagConfig>())
                        {
                            if (tag == null)
                                continue;

                            AddTagVariable(groupFolder, device, group, tag);
                            tagCount++;
                        }
                    }
                }

                AddPredefinedNode(SystemContext, root);
                _status.DeviceNodeCount = deviceCount;
                _status.GroupNodeCount = groupCount;
                _status.TagNodeCount = tagCount;
                _status.LastReloadTime = DateTime.Now;
            }
        }

        public void UpdateValue(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            string key = BuildSnapshotKey(snapshot);
            lock (_syncRoot)
            {
                BaseDataVariableState? variable;
                if (!_tagNodes.TryGetValue(key, out variable) || variable == null)
                    return;

                ApplySnapshot(variable, snapshot, notify: true);
                _status.ValueUpdateCount++;
                _status.LastValueUpdateTime = DateTime.Now;
                _status.LastError = string.Empty;
            }
        }

        private void AddObjectsFolderReference(IDictionary<NodeId, IList<IReference>> externalReferences, FolderState root)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                references = new List<IReference>();
                externalReferences[ObjectIds.ObjectsFolder] = references;
            }

            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
        }

        private FolderState CreateFolder(NodeState? parent, string nodeKey, string displayName)
        {
            string browseName = SanitizeBrowseName(displayName);
            FolderState folder = new FolderState(parent)
            {
                SymbolicName = browseName,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = ObjectTypeIds.FolderType,
                NodeId = new NodeId(nodeKey, NamespaceIndex),
                BrowseName = new QualifiedName(browseName, NamespaceIndex),
                DisplayName = displayName,
                Description = displayName,
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                EventNotifier = EventNotifiers.None
            };

            parent?.AddChild(folder);
            return folder;
        }

        private void AddTagVariable(NodeState parent, DeviceConfig device, GroupConfig? group, TagConfig tag)
        {
            string displayName = DisplayName(tag.Name, "Tag");
            string key = BuildTagKey(device, group, tag);
            BaseDataVariableState variable = new BaseDataVariableState(parent)
            {
                SymbolicName = SanitizeBrowseName(displayName),
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId("tag:" + SafeId(tag.Id, displayName), NamespaceIndex),
                BrowseName = new QualifiedName(SanitizeBrowseName(displayName), NamespaceIndex),
                DisplayName = displayName,
                Description = BuildDescription(device, group, tag),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                DataType = ResolveDataType(tag.DataType),
                ValueRank = IsArrayType(tag.DataType) ? ValueRanks.OneDimension : ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = OpcUaServerOptions.ClampSamplingInterval(_options.MinimumSamplingIntervalMs),
                Historizing = false,
                StatusCode = StatusCodes.BadWaitingForInitialData,
                Timestamp = DateTime.UtcNow
            };

            if (_runtime.TryGetSnapshot(device.Name, group == null ? string.Empty : group.Name, tag.Name, out TagValueSnapshot? snapshot) && snapshot != null)
                ApplySnapshot(variable, snapshot, notify: false);

            parent.AddChild(variable);
            _tagNodes[key] = variable;
        }

        private void ApplySnapshot(BaseDataVariableState variable, TagValueSnapshot snapshot, bool notify)
        {
            variable.Value = ConvertValue(snapshot);
            variable.StatusCode = ResolveStatusCode(snapshot.Quality);
            variable.Timestamp = NormalizeTimestamp(snapshot.Timestamp);
            if (notify)
                variable.ClearChangeMasks(SystemContext, true);
        }

        private static object ConvertValue(TagValueSnapshot snapshot)
        {
            if (snapshot.Value != null && !(snapshot.Value is string textValue && string.IsNullOrWhiteSpace(textValue)))
                return snapshot.Value;

            string text = string.IsNullOrWhiteSpace(snapshot.ValueText)
                ? Convert.ToString(snapshot.RawValue, CultureInfo.InvariantCulture) ?? string.Empty
                : snapshot.ValueText;

            string dataType = snapshot.DataType ?? string.Empty;
            if (dataType.EndsWith("Array", StringComparison.OrdinalIgnoreCase))
                return text;

            if (dataType.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("Coil", StringComparison.OrdinalIgnoreCase) ||
                dataType.Equals("DiscreteInput", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(text, out bool boolValue))
                    return boolValue;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericBool))
                    return Math.Abs(numericBool) > double.Epsilon;
            }

            if (dataType.Equals("Int16", StringComparison.OrdinalIgnoreCase) && short.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out short int16Value))
                return int16Value;
            if (dataType.Equals("UInt16", StringComparison.OrdinalIgnoreCase) && ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort uint16Value))
                return uint16Value;
            if (dataType.Equals("Int32", StringComparison.OrdinalIgnoreCase) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int int32Value))
                return int32Value;
            if (dataType.Equals("UInt32", StringComparison.OrdinalIgnoreCase) && uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint uint32Value))
                return uint32Value;
            if (dataType.Equals("Int64", StringComparison.OrdinalIgnoreCase) && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long int64Value))
                return int64Value;
            if (dataType.Equals("UInt64", StringComparison.OrdinalIgnoreCase) && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong uint64Value))
                return uint64Value;
            if (dataType.Equals("Float", StringComparison.OrdinalIgnoreCase) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
                return floatValue;
            if (dataType.Equals("Double", StringComparison.OrdinalIgnoreCase) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double doubleValue))
                return doubleValue;

            return text;
        }

        private static NodeId ResolveDataType(PlcDataType dataType)
        {
            switch (dataType)
            {
                case PlcDataType.Bool:
                case PlcDataType.BoolArray:
                case PlcDataType.Coil:
                case PlcDataType.CoilArray:
                case PlcDataType.DiscreteInput:
                case PlcDataType.DiscreteInputArray:
                    return DataTypeIds.Boolean;
                case PlcDataType.Int16:
                case PlcDataType.Int16Array:
                    return DataTypeIds.Int16;
                case PlcDataType.UInt16:
                case PlcDataType.UInt16Array:
                    return DataTypeIds.UInt16;
                case PlcDataType.Int32:
                case PlcDataType.Int32Array:
                    return DataTypeIds.Int32;
                case PlcDataType.UInt32:
                case PlcDataType.UInt32Array:
                    return DataTypeIds.UInt32;
                case PlcDataType.Int64:
                case PlcDataType.Int64Array:
                    return DataTypeIds.Int64;
                case PlcDataType.UInt64:
                case PlcDataType.UInt64Array:
                    return DataTypeIds.UInt64;
                case PlcDataType.Float:
                case PlcDataType.FloatArray:
                    return DataTypeIds.Float;
                case PlcDataType.Double:
                case PlcDataType.DoubleArray:
                    return DataTypeIds.Double;
                default:
                    return DataTypeIds.String;
            }
        }

        private static bool IsArrayType(PlcDataType dataType)
        {
            return dataType == PlcDataType.BoolArray ||
                   dataType == PlcDataType.Int16Array ||
                   dataType == PlcDataType.UInt16Array ||
                   dataType == PlcDataType.Int32Array ||
                   dataType == PlcDataType.UInt32Array ||
                   dataType == PlcDataType.Int64Array ||
                   dataType == PlcDataType.UInt64Array ||
                   dataType == PlcDataType.FloatArray ||
                   dataType == PlcDataType.DoubleArray ||
                   dataType == PlcDataType.CoilArray ||
                   dataType == PlcDataType.DiscreteInputArray;
        }

        private static StatusCode ResolveStatusCode(TagQuality quality)
        {
            switch (quality)
            {
                case TagQuality.Good:
                    return StatusCodes.Good;
                case TagQuality.Unknown:
                    return StatusCodes.BadWaitingForInitialData;
                case TagQuality.Disabled:
                    return StatusCodes.BadOutOfService;
                case TagQuality.AccessDenied:
                    return StatusCodes.BadUserAccessDenied;
                case TagQuality.OutOfRange:
                    return StatusCodes.BadOutOfRange;
                default:
                    return StatusCodes.Bad;
            }
        }

        private static DateTime NormalizeTimestamp(DateTime timestamp)
        {
            if (timestamp == DateTime.MinValue)
                return DateTime.UtcNow;
            return timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime();
        }

        private static string BuildDescription(DeviceConfig device, GroupConfig? group, TagConfig tag)
        {
            string groupName = group == null ? string.Empty : group.Name;
            string pointCode = string.IsNullOrWhiteSpace(tag.PointCode) ? tag.Address : tag.PointCode;
            return "Device=" + device.Name + "; Group=" + groupName + "; Address=" + tag.Address + "; PointCode=" + pointCode + "; DataType=" + tag.DataType;
        }

        private static string BuildTagKey(DeviceConfig device, GroupConfig? group, TagConfig tag)
        {
            if (!string.IsNullOrWhiteSpace(tag.Id))
                return "id:" + tag.Id.Trim();

            return BuildPathKey(device.Name, group == null ? string.Empty : group.Name, tag.Name);
        }

        private static string BuildSnapshotKey(TagValueSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.TagId))
                return "id:" + snapshot.TagId.Trim();

            return BuildPathKey(snapshot.DeviceName, snapshot.GroupName, snapshot.TagName);
        }

        private static string BuildPathKey(string deviceName, string groupName, string tagName)
        {
            return "path:" + (deviceName ?? string.Empty).Trim() + "/" + (groupName ?? string.Empty).Trim() + "/" + (tagName ?? string.Empty).Trim();
        }

        private static string SafeId(string id, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(id) ? fallback : id;
            return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : SanitizeNodeId(value);
        }

        private static string DisplayName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string SanitizeBrowseName(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "Node" : value.Trim();
            return text.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
        }

        private static string SanitizeNodeId(string value)
        {
            return SanitizeBrowseName(value).Replace(" ", "_");
        }
    }
}
