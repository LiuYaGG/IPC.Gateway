/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeErrorEntity
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

[SugarTable("gateway_runtime_errors")]
public sealed class GatewayRuntimeErrorEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "project_id", Length = 64, IsNullable = false)]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "category", Length = 64, IsNullable = false)]
    public string Category { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_name", Length = 128, IsNullable = true)]
    public string DeviceName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "group_name", Length = 128, IsNullable = true)]
    public string GroupName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "tag_name", Length = 128, IsNullable = true)]
    public string TagName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "message", ColumnDataType = "text", IsNullable = true)]
    public string Message { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "suggestion", ColumnDataType = "text", IsNullable = true)]
    public string Suggestion { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source", Length = 128, IsNullable = true)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "timestamp_utc")]
    public DateTime TimestampUtc { get; set; }

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
