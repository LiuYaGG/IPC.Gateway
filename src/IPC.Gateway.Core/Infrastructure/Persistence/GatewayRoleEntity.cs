/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayRoleEntity
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

[SugarTable("gateway_roles")]
public sealed class GatewayRoleEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "name", Length = 64, IsNullable = false)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "display_name", Length = 128, IsNullable = true)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "description", Length = 512, IsNullable = true)]
    public string Description { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    [SugarColumn(ColumnName = "is_system")]
    public bool IsSystem { get; set; }

    [SugarColumn(ColumnName = "permissions_json", IsNullable = false)]
    public string PermissionsJson { get; set; } = "[]";

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
