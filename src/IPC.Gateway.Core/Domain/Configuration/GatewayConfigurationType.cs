/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Configuration
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationType
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
namespace IPC.Gateway.Core.Domain.Configuration;

public static class GatewayConfigurationType
{
    public const string Project = "project";
    public const string Mqtt = "mqtt";
    public const string OpcUa = "opcUa";
    public const string History = "history";
    public const string StorageHealth = "storageHealth";
}
