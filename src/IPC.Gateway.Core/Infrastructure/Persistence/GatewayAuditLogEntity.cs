/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayAuditLogEntity
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

[SugarTable("gateway_audit_logs")]
public sealed class GatewayAuditLogEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "timestamp_utc")]
    public DateTime TimestampUtc { get; set; }

    [SugarColumn(ColumnName = "level", Length = 32, IsNullable = false)]
    public string Level { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "action", Length = 128, IsNullable = false)]
    public string Action { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "target", Length = 256, IsNullable = true)]
    public string Target { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "outcome", Length = 64, IsNullable = true)]
    public string Outcome { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "username", Length = 128, IsNullable = true)]
    public string UserName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "role", Length = 64, IsNullable = true)]
    public string Role { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "remote_ip_address", Length = 128, IsNullable = true)]
    public string RemoteIpAddress { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "method", Length = 16, IsNullable = true)]
    public string Method { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "path", Length = 512, IsNullable = true)]
    public string Path { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "trace_id", Length = 128, IsNullable = true)]
    public string TraceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "error_message", ColumnDataType = "text", IsNullable = true)]
    public string ErrorMessage { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "raw_detail", ColumnDataType = "text", IsNullable = true)]
    public string RawDetail { get; set; } = string.Empty;
}
