/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationEntity
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Infrastructure.Persistence
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
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

[SugarTable("gateway_configurations")]
public sealed class GatewayConfigurationEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "config_type", Length = 32, IsNullable = false)]
    public string ConfigType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "version")]
    public int Version { get; set; }

    [SugarColumn(ColumnName = "payload", ColumnDataType = "text", IsNullable = false)]
    public string Payload { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "active")]
    public bool Active { get; set; }

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "source", Length = 128, IsNullable = true)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description", Length = 512, IsNullable = true)]
    public string Description { get; set; } = string.Empty;
}
