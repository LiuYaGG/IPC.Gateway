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
    RuleEngineRuntimeStatusDto GetRuleEngineStatus();
    HistoryStatsDto GetHistoryStatus();
    GatewaySyncDto GetSync();
    IList<TagValueSnapshotDto> GetTagSnapshots(RuntimeTagSnapshotQuery query);
    IList<GatewayConfigurationVersionDto> GetConfigurationVersions(ConfigurationVersionsQuery query);
    GatewayRuntimeStatusDto RollbackConfiguration(RollbackConfigurationCommand command);
    GatewayRuntimeStatusDto ApplyConfigurationCommand(RawConfigurationCommand command);
    ProjectConfigurationDto GetProject();
    ProjectConfigurationDto SaveProject(SaveProjectConfigurationCommand command);
    ProjectValidationResultDto ValidateProject(ValidateProjectConfigurationCommand command);
    IList<DeviceConfigurationDto> GetDevices();
    DeviceConfigurationDto AddDevice(SaveDeviceConfigurationCommand command);
    DeviceConfigurationDto UpdateDevice(string deviceId, SaveDeviceConfigurationCommand command);
    DeviceConfigurationDto DeleteDevice(string deviceId);
    IList<GroupConfigurationDto> GetDeviceGroups(string deviceId);
    GroupConfigurationDto AddGroup(string deviceId, SaveGroupConfigurationCommand command);
    GroupConfigurationDto UpdateGroup(string groupId, SaveGroupConfigurationCommand command);
    GroupConfigurationDto DeleteGroup(string groupId);
    IList<TagConfigurationDto> GetDeviceTags(string deviceId);
    TagConfigurationDto AddDeviceTag(string deviceId, SaveTagConfigurationCommand command);
    IList<TagConfigurationDto> GetGroupTags(string groupId);
    TagConfigurationDto AddGroupTag(string groupId, SaveTagConfigurationCommand command);
    TagConfigurationDto UpdateTag(string tagId, SaveTagConfigurationCommand command);
    TagConfigurationDto DeleteTag(string tagId);
    Task<WriteTagResultDto> WriteTagAsync(WriteTagCommand command);
    IList<EdgeRuleConfigurationDto> GetRules();
    EdgeRuleConfigurationDto AddRule(SaveRuleConfigurationCommand command);
    EdgeRuleConfigurationDto UpdateRule(string ruleId, SaveRuleConfigurationCommand command);
    EdgeRuleConfigurationDto DeleteRule(string ruleId);
    IList<FlowRuleDefinitionDto> GetFlowRules();
    FlowRuleDefinitionDto AddFlowRule(SaveFlowRuleDefinitionCommand command);
    FlowRuleDefinitionDto UpdateFlowRule(string ruleId, SaveFlowRuleDefinitionCommand command);
    FlowRuleDefinitionDto DeleteFlowRule(string ruleId);
    MqttConfigurationDto GetMqttOptions();
    MqttConfigurationDto UpdateMqttOptions(SaveMqttConfigurationCommand command);
    OpcUaServerConfigurationDto GetOpcUaOptions();
    OpcUaServerConfigurationDto UpdateOpcUaOptions(SaveOpcUaServerConfigurationCommand command);
    HistoryConfigurationDto GetHistoryOptions();
    HistoryConfigurationDto UpdateHistoryOptions(SaveHistoryConfigurationCommand command);
    StorageHealthConfigurationDto GetStorageHealthOptions();
    StorageHealthConfigurationDto UpdateStorageHealthOptions(SaveStorageHealthConfigurationCommand command);
}
