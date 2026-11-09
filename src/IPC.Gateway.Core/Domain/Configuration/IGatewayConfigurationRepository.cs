/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Configuration
* 项目描述 ：
* 类 名 称 ：IGatewayConfigurationRepository
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Domain.Configuration
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
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Domain.Configuration;

public interface IGatewayConfigurationRepository
{
    ProjectConfig LoadOrCreateProject(Func<ProjectConfig> defaultFactory);
    Task<ProjectConfig> LoadOrCreateProjectAsync(Func<ProjectConfig> defaultFactory);
    ProjectConfig LoadProject();
    Task<ProjectConfig> LoadProjectAsync();
    int SaveProject(ProjectConfig config, string source, string description);
    Task<int> SaveProjectAsync(ProjectConfig config, string source, string description);
    MqttGatewayOptions LoadOrCreateMqtt(MqttGatewayOptions defaultOptions);
    Task<MqttGatewayOptions> LoadOrCreateMqttAsync(MqttGatewayOptions defaultOptions);
    int SaveMqtt(MqttGatewayOptions options, string source, string description);
    Task<int> SaveMqttAsync(MqttGatewayOptions options, string source, string description);
    OpcUaServerOptions LoadOrCreateOpcUa(OpcUaServerOptions defaultOptions);
    Task<OpcUaServerOptions> LoadOrCreateOpcUaAsync(OpcUaServerOptions defaultOptions);
    int SaveOpcUa(OpcUaServerOptions options, string source, string description);
    Task<int> SaveOpcUaAsync(OpcUaServerOptions options, string source, string description);
    LocalHistoryOptions LoadOrCreateHistory(LocalHistoryOptions defaultOptions);
    Task<LocalHistoryOptions> LoadOrCreateHistoryAsync(LocalHistoryOptions defaultOptions);
    int SaveHistory(LocalHistoryOptions options, string source, string description);
    Task<int> SaveHistoryAsync(LocalHistoryOptions options, string source, string description);
    StorageHealthThresholds LoadOrCreateStorageHealth(StorageHealthThresholds defaultThresholds);
    Task<StorageHealthThresholds> LoadOrCreateStorageHealthAsync(StorageHealthThresholds defaultThresholds);
    int SaveStorageHealth(StorageHealthThresholds thresholds, string source, string description);
    Task<int> SaveStorageHealthAsync(StorageHealthThresholds thresholds, string source, string description);
    IList<GatewayConfigurationVersionInfo> GetVersions(string configType, int maxCount);
    Task<IList<GatewayConfigurationVersionInfo>> GetVersionsAsync(string configType, int maxCount);
    ProjectConfig RollbackProject(int version);
    Task<ProjectConfig> RollbackProjectAsync(int version);
    MqttGatewayOptions RollbackMqtt(int version);
    Task<MqttGatewayOptions> RollbackMqttAsync(int version);
    OpcUaServerOptions RollbackOpcUa(int version);
    Task<OpcUaServerOptions> RollbackOpcUaAsync(int version);
    LocalHistoryOptions RollbackHistory(int version);
    Task<LocalHistoryOptions> RollbackHistoryAsync(int version);
    StorageHealthThresholds RollbackStorageHealth(int version);
    Task<StorageHealthThresholds> RollbackStorageHealthAsync(int version);
}
