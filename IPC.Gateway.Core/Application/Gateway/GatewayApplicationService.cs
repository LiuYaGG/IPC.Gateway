/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayApplicationService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Gateway
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
using IPC.EdgeGateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public sealed class GatewayApplicationService : IGatewayApplicationService
{
    private readonly GatewayCoreService _gateway;
    private readonly IGatewayProjectApplicationService _projects;
    private readonly IGatewayDeviceConfigurationApplicationService _devices;
    private readonly IGatewayRuleConfigurationApplicationService _rules;
    private readonly IGatewayMqttConfigurationApplicationService _mqtt;
    private readonly IGatewayOpcUaConfigurationApplicationService _opcUa;
    private readonly IGatewayHistoryConfigurationApplicationService _history;

    public GatewayApplicationService(
        GatewayCoreService gateway,
        IGatewayProjectApplicationService projects,
        IGatewayDeviceConfigurationApplicationService devices,
        IGatewayRuleConfigurationApplicationService rules,
        IGatewayMqttConfigurationApplicationService mqtt,
        IGatewayOpcUaConfigurationApplicationService opcUa,
        IGatewayHistoryConfigurationApplicationService history)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _devices = devices ?? throw new ArgumentNullException(nameof(devices));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _mqtt = mqtt ?? throw new ArgumentNullException(nameof(mqtt));
        _opcUa = opcUa ?? throw new ArgumentNullException(nameof(opcUa));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public void Start() => _gateway.Start();

    public void Stop() => _gateway.Stop();

    public GatewayRuntimeStatusDto GetStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetStatus());

    public MqttRuntimeStatusDto GetMqttStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetMqttStatus());

    public OpcUaServerRuntimeStatusDto GetOpcUaStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetOpcUaStatus());

    public HistoryStatsDto GetHistoryStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetHistoryStats());

    public GatewaySyncDto GetSync()
    {
        return GatewayConfigurationSecretPolicy.SanitizeSync(GatewayConfigurationContractMapper.ToDto(new GatewaySyncResult
        {
            Status = _gateway.GetStatus(),
            Project = _projects.GetProject(),
            Mqtt = _mqtt.GetMqttOptions(),
            OpcUa = _opcUa.GetOpcUaOptions(),
            History = _history.GetHistoryOptions(),
            StorageHealth = _gateway.CurrentStorageHealthThresholds
        }));
    }

    public IList<TagValueSnapshotDto> GetTagSnapshots(RuntimeTagSnapshotQuery query)
    {
        query ??= new RuntimeTagSnapshotQuery();
        IEnumerable<TagValueSnapshotDto> tags = GetStatus().Tags;

        tags = tags.Where(tag =>
            Matches(tag.ChannelId, query.ChannelId) &&
            Matches(tag.ChannelName, query.ChannelName) &&
            Matches(tag.DeviceId, query.DeviceId) &&
            Matches(tag.DeviceName, query.DeviceName) &&
            Matches(tag.GroupId, query.GroupId) &&
            Matches(tag.GroupName, query.GroupName) &&
            Matches(tag.TagId, query.TagId) &&
            Matches(tag.TagName, query.TagName));

        return tags.ToList();
    }

    public IList<GatewayConfigurationVersionDto> GetConfigurationVersions(ConfigurationVersionsQuery query)
    {
        query ??= new ConfigurationVersionsQuery();
        return _gateway
            .GetConfigurationVersions(query.ConfigType ?? string.Empty, query.Limit <= 0 ? 50 : query.Limit)
            .Select(GatewayConfigurationContractMapper.ToDto)
            .ToList();
    }

    public async Task<IList<GatewayConfigurationVersionDto>> GetConfigurationVersionsAsync(ConfigurationVersionsQuery query)
    {
        query ??= new ConfigurationVersionsQuery();
        IList<GatewayConfigurationVersionInfo> versions = await _gateway.GetConfigurationVersionsAsync(query.ConfigType ?? string.Empty, query.Limit <= 0 ? 50 : query.Limit);
        return versions.Select(GatewayConfigurationContractMapper.ToDto).ToList();
    }

    public GatewayRuntimeStatusDto RollbackConfiguration(RollbackConfigurationCommand command)
    {
        command ??= new RollbackConfigurationCommand();
        _gateway.RollbackConfiguration(command.ConfigType, command.Version);
        return GetStatus();
    }

    public async Task<GatewayRuntimeStatusDto> RollbackConfigurationAsync(RollbackConfigurationCommand command)
    {
        command ??= new RollbackConfigurationCommand();
        await _gateway.RollbackConfigurationAsync(command.ConfigType, command.Version);
        return GetStatus();
    }

    public GatewayRuntimeStatusDto ApplyConfigurationCommand(RawConfigurationCommand command)
    {
        command ??= new RawConfigurationCommand();
        _gateway.ApplyConfigurationCommand(command.Source, command.Payload);
        return GetStatus();
    }

    public async Task<GatewayRuntimeStatusDto> ApplyConfigurationCommandAsync(RawConfigurationCommand command)
    {
        command ??= new RawConfigurationCommand();
        await _gateway.ApplyConfigurationCommandAsync(command.Source, command.Payload);
        return GetStatus();
    }

    public ProjectConfigurationDto GetProject() => GatewayConfigurationSecretPolicy.SanitizeProject(GatewayConfigurationContractMapper.ToDto(_projects.GetProject()));

    public ProjectConfigurationDto SaveProject(SaveProjectConfigurationCommand command)
    {
        command ??= new SaveProjectConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveProjectSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()));
        return GatewayConfigurationSecretPolicy.SanitizeProject(GatewayConfigurationContractMapper.ToDto(_projects.SaveProject(GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<ProjectConfigurationDto> SaveProjectAsync(SaveProjectConfigurationCommand command)
    {
        command ??= new SaveProjectConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveProjectSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()));
        ProjectConfig saved = await _projects.SaveProjectAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeProject(GatewayConfigurationContractMapper.ToDto(saved));
    }

    public ProjectValidationResultDto ValidateProject(ValidateProjectConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_projects.ValidateProject(GatewayConfigurationContractMapper.ToConfig(command)));
    public IList<ChannelConfigurationDto> GetChannels() => _devices.GetChannels().Select(GatewayConfigurationContractMapper.ToDto).ToList();

    public ChannelConfigurationDto AddChannel(SaveChannelConfigurationCommand command) =>
        GatewayConfigurationContractMapper.ToDto(_devices.AddChannel(GatewayConfigurationContractMapper.ToConfig(command ?? new SaveChannelConfigurationCommand())));

    public async Task<ChannelConfigurationDto> AddChannelAsync(SaveChannelConfigurationCommand command) =>
        GatewayConfigurationContractMapper.ToDto(await _devices.AddChannelAsync(GatewayConfigurationContractMapper.ToConfig(command ?? new SaveChannelConfigurationCommand())));

    public ChannelConfigurationDto UpdateChannel(string channelId, SaveChannelConfigurationCommand command) =>
        GatewayConfigurationContractMapper.ToDto(_devices.UpdateChannel(channelId, GatewayConfigurationContractMapper.ToConfig(command ?? new SaveChannelConfigurationCommand())));

    public async Task<ChannelConfigurationDto> UpdateChannelAsync(string channelId, SaveChannelConfigurationCommand command) =>
        GatewayConfigurationContractMapper.ToDto(await _devices.UpdateChannelAsync(channelId, GatewayConfigurationContractMapper.ToConfig(command ?? new SaveChannelConfigurationCommand())));

    public ChannelConfigurationDto DeleteChannel(string channelId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteChannel(channelId));

    public async Task<ChannelConfigurationDto> DeleteChannelAsync(string channelId) => GatewayConfigurationContractMapper.ToDto(await _devices.DeleteChannelAsync(channelId));

    public IList<DeviceConfigurationDto> GetDevices() => GatewayConfigurationSecretPolicy.SanitizeDevices(_devices.GetDevices().Select(GatewayConfigurationContractMapper.ToDto));

    public DeviceConfigurationDto AddDevice(SaveDeviceConfigurationCommand command)
    {
        command ??= new SaveDeviceConfigurationCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedDeviceSecrets(command);
        return GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(_devices.AddDevice(GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<DeviceConfigurationDto> AddDeviceAsync(SaveDeviceConfigurationCommand command)
    {
        command ??= new SaveDeviceConfigurationCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedDeviceSecrets(command);
        DeviceConfig device = await _devices.AddDeviceAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(device));
    }

    public DeviceConfigurationDto UpdateDevice(string deviceId, SaveDeviceConfigurationCommand command)
    {
        command ??= new SaveDeviceConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveDeviceSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), deviceId);
        return GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(_devices.UpdateDevice(deviceId, GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<DeviceConfigurationDto> UpdateDeviceAsync(string deviceId, SaveDeviceConfigurationCommand command)
    {
        command ??= new SaveDeviceConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveDeviceSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), deviceId);
        DeviceConfig device = await _devices.UpdateDeviceAsync(deviceId, GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(device));
    }

    public DeviceConfigurationDto DeleteDevice(string deviceId) => GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(_devices.DeleteDevice(deviceId)));
    public async Task<DeviceConfigurationDto> DeleteDeviceAsync(string deviceId) => GatewayConfigurationSecretPolicy.SanitizeDevice(GatewayConfigurationContractMapper.ToDto(await _devices.DeleteDeviceAsync(deviceId)));
    public IList<GroupConfigurationDto> GetDeviceGroups(string deviceId) => _devices.GetDeviceGroups(deviceId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public GroupConfigurationDto AddGroup(string deviceId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddGroup(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<GroupConfigurationDto> AddGroupAsync(string deviceId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _devices.AddGroupAsync(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public GroupConfigurationDto UpdateGroup(string groupId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.UpdateGroup(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<GroupConfigurationDto> UpdateGroupAsync(string groupId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _devices.UpdateGroupAsync(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public GroupConfigurationDto DeleteGroup(string groupId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteGroup(groupId));
    public async Task<GroupConfigurationDto> DeleteGroupAsync(string groupId) => GatewayConfigurationContractMapper.ToDto(await _devices.DeleteGroupAsync(groupId));
    public IList<TagConfigurationDto> GetDeviceTags(string deviceId) => _devices.GetDeviceTags(deviceId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public TagConfigurationDto AddDeviceTag(string deviceId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddDeviceTag(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<TagConfigurationDto> AddDeviceTagAsync(string deviceId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _devices.AddDeviceTagAsync(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public IList<TagConfigurationDto> GetGroupTags(string groupId) => _devices.GetGroupTags(groupId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public TagConfigurationDto AddGroupTag(string groupId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddGroupTag(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<TagConfigurationDto> AddGroupTagAsync(string groupId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _devices.AddGroupTagAsync(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public TagConfigurationDto UpdateTag(string tagId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.UpdateTag(tagId, GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<TagConfigurationDto> UpdateTagAsync(string tagId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _devices.UpdateTagAsync(tagId, GatewayConfigurationContractMapper.ToConfig(command)));
    public TagConfigurationDto DeleteTag(string tagId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteTag(tagId));
    public async Task<TagConfigurationDto> DeleteTagAsync(string tagId) => GatewayConfigurationContractMapper.ToDto(await _devices.DeleteTagAsync(tagId));
    public async Task<WriteTagResultDto> WriteTagAsync(WriteTagCommand command)
    {
        command ??= new WriteTagCommand();
        WriteTagRequest request = new WriteTagRequest
        {
            ChannelId = command.ChannelId ?? string.Empty,
            DeviceId = command.DeviceId ?? string.Empty,
            GroupId = command.GroupId ?? string.Empty,
            TagId = command.TagId ?? string.Empty,
            DeviceName = command.DeviceName ?? string.Empty,
            GroupName = command.GroupName ?? string.Empty,
            TagName = command.TagName ?? string.Empty,
            DataType = command.DataType ?? string.Empty,
            ValueText = command.ValueText ?? string.Empty
        };

        // RuntimeEngine owns the device-operation timeout. Do not return while the
        // serialized device write is still running, otherwise a reported timeout
        // can be followed by a late successful PLC write.
        return await Task.Run(() => GatewayConfigurationContractMapper.ToDto(_gateway.Runtime.WriteTag(request)));
    }
    public IList<EdgeRuleConfigurationDto> GetRules() => GatewayConfigurationSecretPolicy.SanitizeRules(_rules.GetRules().Select(GatewayConfigurationContractMapper.ToDto));

    public EdgeRuleConfigurationDto AddRule(SaveRuleConfigurationCommand command)
    {
        command ??= new SaveRuleConfigurationCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedRuleSecrets(command);
        return GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(_rules.AddRule(GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<EdgeRuleConfigurationDto> AddRuleAsync(SaveRuleConfigurationCommand command)
    {
        command ??= new SaveRuleConfigurationCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedRuleSecrets(command);
        EdgeRuleConfig rule = await _rules.AddRuleAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(rule));
    }

    public EdgeRuleConfigurationDto UpdateRule(string ruleId, SaveRuleConfigurationCommand command)
    {
        command ??= new SaveRuleConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveRuleSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), ruleId);
        return GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(_rules.UpdateRule(ruleId, GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<EdgeRuleConfigurationDto> UpdateRuleAsync(string ruleId, SaveRuleConfigurationCommand command)
    {
        command ??= new SaveRuleConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveRuleSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), ruleId);
        EdgeRuleConfig rule = await _rules.UpdateRuleAsync(ruleId, GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(rule));
    }

    public EdgeRuleConfigurationDto DeleteRule(string ruleId) => GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(_rules.DeleteRule(ruleId)));
    public async Task<EdgeRuleConfigurationDto> DeleteRuleAsync(string ruleId) => GatewayConfigurationSecretPolicy.SanitizeRule(GatewayConfigurationContractMapper.ToDto(await _rules.DeleteRuleAsync(ruleId)));
    public IList<FlowRuleDefinitionDto> GetFlowRules() => GatewayConfigurationSecretPolicy.SanitizeFlowRules(_rules.GetFlowRules().Select(GatewayConfigurationContractMapper.ToDto));

    public FlowRuleDefinitionDto AddFlowRule(SaveFlowRuleDefinitionCommand command)
    {
        command ??= new SaveFlowRuleDefinitionCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedFlowRuleSecrets(command);
        return GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(_rules.AddFlowRule(GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<FlowRuleDefinitionDto> AddFlowRuleAsync(SaveFlowRuleDefinitionCommand command)
    {
        command ??= new SaveFlowRuleDefinitionCommand();
        GatewayConfigurationSecretPolicy.ClearRedactedFlowRuleSecrets(command);
        FlowRuleDefinition rule = await _rules.AddFlowRuleAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(rule));
    }

    public FlowRuleDefinitionDto UpdateFlowRule(string ruleId, SaveFlowRuleDefinitionCommand command)
    {
        command ??= new SaveFlowRuleDefinitionCommand();
        GatewayConfigurationSecretPolicy.PreserveFlowRuleSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), ruleId);
        return GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(_rules.UpdateFlowRule(ruleId, GatewayConfigurationContractMapper.ToConfig(command))));
    }

    public async Task<FlowRuleDefinitionDto> UpdateFlowRuleAsync(string ruleId, SaveFlowRuleDefinitionCommand command)
    {
        command ??= new SaveFlowRuleDefinitionCommand();
        GatewayConfigurationSecretPolicy.PreserveFlowRuleSecrets(command, GatewayConfigurationContractMapper.ToDto(_projects.GetProject()), ruleId);
        FlowRuleDefinition rule = await _rules.UpdateFlowRuleAsync(ruleId, GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(rule));
    }

    public FlowRuleDefinitionDto DeleteFlowRule(string ruleId) => GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(_rules.DeleteFlowRule(ruleId)));
    public async Task<FlowRuleDefinitionDto> DeleteFlowRuleAsync(string ruleId) => GatewayConfigurationSecretPolicy.SanitizeFlowRule(GatewayConfigurationContractMapper.ToDto(await _rules.DeleteFlowRuleAsync(ruleId)));
    public MqttConfigurationDto GetMqttOptions() => GatewayConfigurationSecretPolicy.SanitizeMqtt(GatewayConfigurationContractMapper.ToDto(_mqtt.GetMqttOptions()));

    public MqttConfigurationDto UpdateMqttOptions(SaveMqttConfigurationCommand command)
    {
        command ??= new SaveMqttConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveMqttSecrets(command, GatewayConfigurationContractMapper.ToDto(_mqtt.GetMqttOptions()));
        return GatewayConfigurationSecretPolicy.SanitizeMqtt(GatewayConfigurationContractMapper.ToDto(_mqtt.UpdateMqttOptions(GatewayConfigurationContractMapper.ToConfig(command))));
    }
    public async Task<MqttConfigurationDto> UpdateMqttOptionsAsync(SaveMqttConfigurationCommand command)
    {
        command ??= new SaveMqttConfigurationCommand();
        GatewayConfigurationSecretPolicy.PreserveMqttSecrets(command, GatewayConfigurationContractMapper.ToDto(_mqtt.GetMqttOptions()));
        MqttGatewayOptions options = await _mqtt.UpdateMqttOptionsAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GatewayConfigurationSecretPolicy.SanitizeMqtt(GatewayConfigurationContractMapper.ToDto(options));
    }
    public OpcUaServerConfigurationDto GetOpcUaOptions() => GatewayConfigurationContractMapper.ToDto(_opcUa.GetOpcUaOptions());
    public OpcUaServerConfigurationDto UpdateOpcUaOptions(SaveOpcUaServerConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_opcUa.UpdateOpcUaOptions(GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<OpcUaServerConfigurationDto> UpdateOpcUaOptionsAsync(SaveOpcUaServerConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _opcUa.UpdateOpcUaOptionsAsync(GatewayConfigurationContractMapper.ToConfig(command)));
    public HistoryConfigurationDto GetHistoryOptions() => GatewayConfigurationContractMapper.ToDto(_history.GetHistoryOptions());
    public HistoryConfigurationDto UpdateHistoryOptions(SaveHistoryConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_history.UpdateHistoryOptions(GatewayConfigurationContractMapper.ToConfig(command)));
    public async Task<HistoryConfigurationDto> UpdateHistoryOptionsAsync(SaveHistoryConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(await _history.UpdateHistoryOptionsAsync(GatewayConfigurationContractMapper.ToConfig(command)));
    public StorageHealthConfigurationDto GetStorageHealthOptions() => GatewayConfigurationContractMapper.ToDto(_gateway.CurrentStorageHealthThresholds);
    public StorageHealthConfigurationDto UpdateStorageHealthOptions(SaveStorageHealthConfigurationCommand command)
    {
        _gateway.UpdateStorageHealthThresholds(GatewayConfigurationContractMapper.ToConfig(command));
        return GetStorageHealthOptions();
    }

    public async Task<StorageHealthConfigurationDto> UpdateStorageHealthOptionsAsync(SaveStorageHealthConfigurationCommand command)
    {
        await _gateway.UpdateStorageHealthThresholdsAsync(GatewayConfigurationContractMapper.ToConfig(command));
        return GetStorageHealthOptions();
    }

    public void Dispose()
    {
        _gateway.Dispose();
    }

    private static bool Matches(string value, string expected)
    {
        return string.IsNullOrWhiteSpace(expected) ||
               string.Equals((value ?? string.Empty).Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

}
