/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayAuditLogOptions
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
namespace IPC.Gateway.Core.Gateway;

public sealed class GatewayAuditLogOptions
{
    public GatewayAuditLogOptions()
    {
        RetentionDays = 180;
    }

    public int RetentionDays { get; set; }

    public GatewayAuditLogOptions Clone()
    {
        return new GatewayAuditLogOptions
        {
            RetentionDays = RetentionDays
        };
    }

    public static int ClampRetentionDays(int value)
    {
        if (value < 1)
            return 180;
        if (value > 3650)
            return 3650;
        return value;
    }
}
