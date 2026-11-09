/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：PostgresGatewayConfigurationStore
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using IPC.Gateway.Core.Infrastructure.Persistence;

namespace IPC.Gateway.Core.Gateway;

[Obsolete("Use SqlSugarGatewayConfigurationRepository through IGatewayConfigurationRepository instead.")]
public sealed class PostgresGatewayConfigurationStore : SqlSugarGatewayConfigurationRepository
{
    public const string ProjectConfigType = "project";
    public const string MqttConfigType = "mqtt";

    public PostgresGatewayConfigurationStore(GatewayDatabaseOptions options)
        : base(options)
    {
    }
}
