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
    ProjectConfig LoadProject();
    int SaveProject(ProjectConfig config, string source, string description);
    MqttGatewayOptions LoadOrCreateMqtt(MqttGatewayOptions defaultOptions);
    int SaveMqtt(MqttGatewayOptions options, string source, string description);
    OpcUaServerOptions LoadOrCreateOpcUa(OpcUaServerOptions defaultOptions);
    int SaveOpcUa(OpcUaServerOptions options, string source, string description);
    LocalHistoryOptions LoadOrCreateHistory(LocalHistoryOptions defaultOptions);
    int SaveHistory(LocalHistoryOptions options, string source, string description);
    StorageHealthThresholds LoadOrCreateStorageHealth(StorageHealthThresholds defaultThresholds);
    int SaveStorageHealth(StorageHealthThresholds thresholds, string source, string description);
    IList<GatewayConfigurationVersionInfo> GetVersions(string configType, int maxCount);
    ProjectConfig RollbackProject(int version);
    MqttGatewayOptions RollbackMqtt(int version);
    OpcUaServerOptions RollbackOpcUa(int version);
    LocalHistoryOptions RollbackHistory(int version);
    StorageHealthThresholds RollbackStorageHealth(int version);
}
