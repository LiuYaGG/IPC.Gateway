/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：IGatewayApplicationService
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
using IPC.Runtime.Configuration;
using System.Threading.Tasks;

namespace IPC.Gateway.Core.Application.Gateway;

public interface IGatewayApplicationService : IDisposable
{
    void Start();
    void Stop();
    GatewayRuntimeStatusDto GetStatus();
    MqttRuntimeStatusDto GetMqttStatus();
    OpcUaServerRuntimeStatusDto GetOpcUaStatus();
    HistoryStatsDto GetHistoryStatus();
    GatewaySyncDto GetSync();
    IList<TagValueSnapshotDto> GetTagSnapshots(RuntimeTagSnapshotQuery query);
    IList<GatewayConfigurationVersionDto> GetConfigurationVersions(ConfigurationVersionsQuery query);
    Task<IList<GatewayConfigurationVersionDto>> GetConfigurationVersionsAsync(ConfigurationVersionsQuery query) => Task.FromResult(GetConfigurationVersions(query));
    GatewayRuntimeStatusDto RollbackConfiguration(RollbackConfigurationCommand command);
    Task<GatewayRuntimeStatusDto> RollbackConfigurationAsync(RollbackConfigurationCommand command) => Task.FromResult(RollbackConfiguration(command));
    GatewayRuntimeStatusDto ApplyConfigurationCommand(RawConfigurationCommand command);
    Task<GatewayRuntimeStatusDto> ApplyConfigurationCommandAsync(RawConfigurationCommand command) => Task.FromResult(ApplyConfigurationCommand(command));
    ProjectConfigurationDto GetProject();
    ProjectConfigurationDto SaveProject(SaveProjectConfigurationCommand command);
    Task<ProjectConfigurationDto> SaveProjectAsync(SaveProjectConfigurationCommand command) => Task.FromResult(SaveProject(command));
    ProjectValidationResultDto ValidateProject(ValidateProjectConfigurationCommand command);
    IList<DeviceConfigurationDto> GetDevices();
    DeviceConfigurationDto AddDevice(SaveDeviceConfigurationCommand command);
    Task<DeviceConfigurationDto> AddDeviceAsync(SaveDeviceConfigurationCommand command) => Task.FromResult(AddDevice(command));
    DeviceConfigurationDto UpdateDevice(string deviceId, SaveDeviceConfigurationCommand command);
    Task<DeviceConfigurationDto> UpdateDeviceAsync(string deviceId, SaveDeviceConfigurationCommand command) => Task.FromResult(UpdateDevice(deviceId, command));
    DeviceConfigurationDto DeleteDevice(string deviceId);
    Task<DeviceConfigurationDto> DeleteDeviceAsync(string deviceId) => Task.FromResult(DeleteDevice(deviceId));
    IList<GroupConfigurationDto> GetDeviceGroups(string deviceId);
    GroupConfigurationDto AddGroup(string deviceId, SaveGroupConfigurationCommand command);
    Task<GroupConfigurationDto> AddGroupAsync(string deviceId, SaveGroupConfigurationCommand command) => Task.FromResult(AddGroup(deviceId, command));
    GroupConfigurationDto UpdateGroup(string groupId, SaveGroupConfigurationCommand command);
    Task<GroupConfigurationDto> UpdateGroupAsync(string groupId, SaveGroupConfigurationCommand command) => Task.FromResult(UpdateGroup(groupId, command));
    GroupConfigurationDto DeleteGroup(string groupId);
    Task<GroupConfigurationDto> DeleteGroupAsync(string groupId) => Task.FromResult(DeleteGroup(groupId));
    IList<TagConfigurationDto> GetDeviceTags(string deviceId);
    TagConfigurationDto AddDeviceTag(string deviceId, SaveTagConfigurationCommand command);
    Task<TagConfigurationDto> AddDeviceTagAsync(string deviceId, SaveTagConfigurationCommand command) => Task.FromResult(AddDeviceTag(deviceId, command));
    IList<TagConfigurationDto> GetGroupTags(string groupId);
    TagConfigurationDto AddGroupTag(string groupId, SaveTagConfigurationCommand command);
    Task<TagConfigurationDto> AddGroupTagAsync(string groupId, SaveTagConfigurationCommand command) => Task.FromResult(AddGroupTag(groupId, command));
    TagConfigurationDto UpdateTag(string tagId, SaveTagConfigurationCommand command);
    Task<TagConfigurationDto> UpdateTagAsync(string tagId, SaveTagConfigurationCommand command) => Task.FromResult(UpdateTag(tagId, command));
    TagConfigurationDto DeleteTag(string tagId);
    Task<TagConfigurationDto> DeleteTagAsync(string tagId) => Task.FromResult(DeleteTag(tagId));
    Task<WriteTagResultDto> WriteTagAsync(WriteTagCommand command);
    IList<EdgeRuleConfigurationDto> GetRules();
    EdgeRuleConfigurationDto AddRule(SaveRuleConfigurationCommand command);
    Task<EdgeRuleConfigurationDto> AddRuleAsync(SaveRuleConfigurationCommand command) => Task.FromResult(AddRule(command));
    EdgeRuleConfigurationDto UpdateRule(string ruleId, SaveRuleConfigurationCommand command);
    Task<EdgeRuleConfigurationDto> UpdateRuleAsync(string ruleId, SaveRuleConfigurationCommand command) => Task.FromResult(UpdateRule(ruleId, command));
    EdgeRuleConfigurationDto DeleteRule(string ruleId);
    Task<EdgeRuleConfigurationDto> DeleteRuleAsync(string ruleId) => Task.FromResult(DeleteRule(ruleId));
    IList<FlowRuleDefinitionDto> GetFlowRules();
    FlowRuleDefinitionDto AddFlowRule(SaveFlowRuleDefinitionCommand command);
    Task<FlowRuleDefinitionDto> AddFlowRuleAsync(SaveFlowRuleDefinitionCommand command) => Task.FromResult(AddFlowRule(command));
    FlowRuleDefinitionDto UpdateFlowRule(string ruleId, SaveFlowRuleDefinitionCommand command);
    Task<FlowRuleDefinitionDto> UpdateFlowRuleAsync(string ruleId, SaveFlowRuleDefinitionCommand command) => Task.FromResult(UpdateFlowRule(ruleId, command));
    FlowRuleDefinitionDto DeleteFlowRule(string ruleId);
    Task<FlowRuleDefinitionDto> DeleteFlowRuleAsync(string ruleId) => Task.FromResult(DeleteFlowRule(ruleId));
    MqttConfigurationDto GetMqttOptions();
    MqttConfigurationDto UpdateMqttOptions(SaveMqttConfigurationCommand command);
    Task<MqttConfigurationDto> UpdateMqttOptionsAsync(SaveMqttConfigurationCommand command) => Task.FromResult(UpdateMqttOptions(command));
    OpcUaServerConfigurationDto GetOpcUaOptions();
    OpcUaServerConfigurationDto UpdateOpcUaOptions(SaveOpcUaServerConfigurationCommand command);
    Task<OpcUaServerConfigurationDto> UpdateOpcUaOptionsAsync(SaveOpcUaServerConfigurationCommand command) => Task.FromResult(UpdateOpcUaOptions(command));
    HistoryConfigurationDto GetHistoryOptions();
    HistoryConfigurationDto UpdateHistoryOptions(SaveHistoryConfigurationCommand command);
    Task<HistoryConfigurationDto> UpdateHistoryOptionsAsync(SaveHistoryConfigurationCommand command) => Task.FromResult(UpdateHistoryOptions(command));
    StorageHealthConfigurationDto GetStorageHealthOptions();
    StorageHealthConfigurationDto UpdateStorageHealthOptions(SaveStorageHealthConfigurationCommand command);
    Task<StorageHealthConfigurationDto> UpdateStorageHealthOptionsAsync(SaveStorageHealthConfigurationCommand command) => Task.FromResult(UpdateStorageHealthOptions(command));
}
