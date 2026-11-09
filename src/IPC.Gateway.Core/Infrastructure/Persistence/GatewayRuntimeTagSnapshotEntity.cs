/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeTagSnapshotEntity
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

[SugarTable("gateway_runtime_tag_snapshots")]
public sealed class GatewayRuntimeTagSnapshotEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "project_id", Length = 64, IsNullable = false)]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "channel_id", Length = 64, IsNullable = false)]
    public string ChannelId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "channel_name", Length = 128, IsNullable = false)]
    public string ChannelName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_id", Length = 64, IsNullable = false)]
    public string DeviceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_name", Length = 128, IsNullable = false)]
    public string DeviceName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "group_id", Length = 64, IsNullable = true)]
    public string GroupId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "group_name", Length = 128, IsNullable = true)]
    public string GroupName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "tag_id", Length = 64, IsNullable = false)]
    public string TagId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "tag_name", Length = 128, IsNullable = false)]
    public string TagName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "data_type", Length = 64, IsNullable = false)]
    public string DataType { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "raw_value_text", ColumnDataType = "text", IsNullable = true)]
    public string RawValueText { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "value_text", ColumnDataType = "text", IsNullable = true)]
    public string ValueText { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "unit", Length = 64, IsNullable = true)]
    public string Unit { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "point_code", Length = 256, IsNullable = true)]
    public string PointCode { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "source", Length = 128, IsNullable = true)]
    public string Source { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "quality", Length = 32, IsNullable = false)]
    public string Quality { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cleaning_applied")]
    public bool CleaningApplied { get; set; }

    [SugarColumn(ColumnName = "cleaning_action", Length = 64, IsNullable = true)]
    public string CleaningAction { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "cleaning_message", ColumnDataType = "text", IsNullable = true)]
    public string CleaningMessage { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "timestamp_utc")]
    public DateTime TimestampUtc { get; set; }

    [SugarColumn(ColumnName = "error_message", ColumnDataType = "text", IsNullable = true)]
    public string ErrorMessage { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
