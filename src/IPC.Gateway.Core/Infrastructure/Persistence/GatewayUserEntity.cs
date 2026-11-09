/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayUserEntity
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

[SugarTable("gateway_users")]
public sealed class GatewayUserEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "username", Length = 64, IsNullable = false)]
    public string Username { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "display_name", Length = 128, IsNullable = true)]
    public string DisplayName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "role", Length = 32, IsNullable = false)]
    public string Role { get; set; } = "Viewer";

    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    [SugarColumn(ColumnName = "password_hash", Length = 256, IsNullable = false)]
    public string PasswordHash { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "password_salt", Length = 128, IsNullable = false)]
    public string PasswordSalt { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "created_utc")]
    public DateTime CreatedUtc { get; set; }

    [SugarColumn(ColumnName = "password_changed_utc")]
    public DateTime PasswordChangedUtc { get; set; }

    [SugarColumn(ColumnName = "last_login_utc", IsNullable = true)]
    public DateTime? LastLoginUtc { get; set; }

    [SugarColumn(ColumnName = "last_failed_login_utc", IsNullable = true)]
    public DateTime? LastFailedLoginUtc { get; set; }

    [SugarColumn(ColumnName = "failed_login_count")]
    public int FailedLoginCount { get; set; }

    [SugarColumn(ColumnName = "lockout_end_utc", IsNullable = true)]
    public DateTime? LockoutEndUtc { get; set; }
}
