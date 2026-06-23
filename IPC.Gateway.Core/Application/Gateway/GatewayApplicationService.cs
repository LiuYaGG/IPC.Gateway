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

    public RuleEngineRuntimeStatusDto GetRuleEngineStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetRuleEngineStatus());

    public HistoryStatsDto GetHistoryStatus() => GatewayConfigurationContractMapper.ToDto(_gateway.GetHistoryStats());

    public GatewaySyncDto GetSync()
    {
        return GatewayConfigurationContractMapper.ToDto(new GatewaySyncResult
        {
            Status = _gateway.GetStatus(),
            Project = _projects.GetProject(),
            Mqtt = _mqtt.GetMqttOptions(),
            OpcUa = _opcUa.GetOpcUaOptions(),
            History = _history.GetHistoryOptions(),
            StorageHealth = _gateway.CurrentStorageHealthThresholds
        });
    }

    public IList<TagValueSnapshotDto> GetTagSnapshots(RuntimeTagSnapshotQuery query)
    {
        query ??= new RuntimeTagSnapshotQuery();
        IEnumerable<TagValueSnapshotDto> tags = GetStatus().Tags;

        tags = tags.Where(tag =>
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

    public GatewayRuntimeStatusDto RollbackConfiguration(RollbackConfigurationCommand command)
    {
        command ??= new RollbackConfigurationCommand();
        _gateway.RollbackConfiguration(command.ConfigType, command.Version);
        return GetStatus();
    }

    public GatewayRuntimeStatusDto ApplyConfigurationCommand(RawConfigurationCommand command)
    {
        command ??= new RawConfigurationCommand();
        _gateway.ApplyConfigurationCommand(command.Source, command.Payload);
        return GetStatus();
    }

    public ProjectConfigurationDto GetProject() => GatewayConfigurationContractMapper.ToDto(_projects.GetProject());
    public ProjectConfigurationDto SaveProject(SaveProjectConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_projects.SaveProject(GatewayConfigurationContractMapper.ToConfig(command)));
    public ProjectValidationResultDto ValidateProject(ValidateProjectConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_projects.ValidateProject(GatewayConfigurationContractMapper.ToConfig(command)));
    public IList<DeviceConfigurationDto> GetDevices() => _devices.GetDevices().Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public DeviceConfigurationDto AddDevice(SaveDeviceConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddDevice(GatewayConfigurationContractMapper.ToConfig(command)));
    public DeviceConfigurationDto UpdateDevice(string deviceId, SaveDeviceConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.UpdateDevice(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public DeviceConfigurationDto DeleteDevice(string deviceId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteDevice(deviceId));
    public IList<GroupConfigurationDto> GetDeviceGroups(string deviceId) => _devices.GetDeviceGroups(deviceId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public GroupConfigurationDto AddGroup(string deviceId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddGroup(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public GroupConfigurationDto UpdateGroup(string groupId, SaveGroupConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.UpdateGroup(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public GroupConfigurationDto DeleteGroup(string groupId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteGroup(groupId));
    public IList<TagConfigurationDto> GetDeviceTags(string deviceId) => _devices.GetDeviceTags(deviceId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public TagConfigurationDto AddDeviceTag(string deviceId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddDeviceTag(deviceId, GatewayConfigurationContractMapper.ToConfig(command)));
    public IList<TagConfigurationDto> GetGroupTags(string groupId) => _devices.GetGroupTags(groupId).Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public TagConfigurationDto AddGroupTag(string groupId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.AddGroupTag(groupId, GatewayConfigurationContractMapper.ToConfig(command)));
    public TagConfigurationDto UpdateTag(string tagId, SaveTagConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_devices.UpdateTag(tagId, GatewayConfigurationContractMapper.ToConfig(command)));
    public TagConfigurationDto DeleteTag(string tagId) => GatewayConfigurationContractMapper.ToDto(_devices.DeleteTag(tagId));
    public async Task<WriteTagResultDto> WriteTagAsync(WriteTagCommand command)
    {
        command ??= new WriteTagCommand();
        int timeout = command.TimeoutMilliseconds <= 0 ? 10000 : Math.Min(command.TimeoutMilliseconds, 30000);
        WriteTagRequest request = new WriteTagRequest
        {
            DeviceName = command.DeviceName ?? string.Empty,
            GroupName = command.GroupName ?? string.Empty,
            TagName = command.TagName ?? string.Empty,
            DataType = command.DataType ?? string.Empty,
            ValueText = command.ValueText ?? string.Empty
        };

        Task<WriteTagResultDto> writeTask = Task.Run(() => GatewayConfigurationContractMapper.ToDto(_gateway.Runtime.WriteTag(request)));
        Task completed = await Task.WhenAny(writeTask, Task.Delay(timeout));
        if (completed != writeTask)
        {
            return new WriteTagResultDto
            {
                Success = false,
                DeviceName = request.DeviceName,
                GroupName = request.GroupName,
                TagName = request.TagName,
                DataType = request.DataType,
                Timestamp = DateTime.Now,
                ErrorMessage = "标签写入超时。"
            };
        }

        return await writeTask;
    }
    public IList<EdgeRuleConfigurationDto> GetRules() => _rules.GetRules().Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public EdgeRuleConfigurationDto AddRule(SaveRuleConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_rules.AddRule(GatewayConfigurationContractMapper.ToConfig(command)));
    public EdgeRuleConfigurationDto UpdateRule(string ruleId, SaveRuleConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_rules.UpdateRule(ruleId, GatewayConfigurationContractMapper.ToConfig(command)));
    public EdgeRuleConfigurationDto DeleteRule(string ruleId) => GatewayConfigurationContractMapper.ToDto(_rules.DeleteRule(ruleId));
    public IList<FlowRuleDefinitionDto> GetFlowRules() => _rules.GetFlowRules().Select(GatewayConfigurationContractMapper.ToDto).ToList();
    public FlowRuleDefinitionDto AddFlowRule(SaveFlowRuleDefinitionCommand command) => GatewayConfigurationContractMapper.ToDto(_rules.AddFlowRule(GatewayConfigurationContractMapper.ToConfig(command)));
    public FlowRuleDefinitionDto UpdateFlowRule(string ruleId, SaveFlowRuleDefinitionCommand command) => GatewayConfigurationContractMapper.ToDto(_rules.UpdateFlowRule(ruleId, GatewayConfigurationContractMapper.ToConfig(command)));
    public FlowRuleDefinitionDto DeleteFlowRule(string ruleId) => GatewayConfigurationContractMapper.ToDto(_rules.DeleteFlowRule(ruleId));
    public MqttConfigurationDto GetMqttOptions() => GatewayConfigurationContractMapper.ToDto(_mqtt.GetMqttOptions());
    public MqttConfigurationDto UpdateMqttOptions(SaveMqttConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_mqtt.UpdateMqttOptions(GatewayConfigurationContractMapper.ToConfig(command)));
    public OpcUaServerConfigurationDto GetOpcUaOptions() => GatewayConfigurationContractMapper.ToDto(_opcUa.GetOpcUaOptions());
    public OpcUaServerConfigurationDto UpdateOpcUaOptions(SaveOpcUaServerConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_opcUa.UpdateOpcUaOptions(GatewayConfigurationContractMapper.ToConfig(command)));
    public HistoryConfigurationDto GetHistoryOptions() => GatewayConfigurationContractMapper.ToDto(_history.GetHistoryOptions());
    public HistoryConfigurationDto UpdateHistoryOptions(SaveHistoryConfigurationCommand command) => GatewayConfigurationContractMapper.ToDto(_history.UpdateHistoryOptions(GatewayConfigurationContractMapper.ToConfig(command)));
    public StorageHealthConfigurationDto GetStorageHealthOptions() => GatewayConfigurationContractMapper.ToDto(_gateway.CurrentStorageHealthThresholds);
    public StorageHealthConfigurationDto UpdateStorageHealthOptions(SaveStorageHealthConfigurationCommand command)
    {
        _gateway.UpdateStorageHealthThresholds(GatewayConfigurationContractMapper.ToConfig(command));
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
