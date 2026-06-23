/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeDeviceStatusEntity
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

[SugarTable("gateway_runtime_device_statuses")]
public sealed class GatewayRuntimeDeviceStatusEntity
{
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, Length = 64)]
    public string Id { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "project_id", Length = 64, IsNullable = false)]
    public string ProjectId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_id", Length = 64, IsNullable = false)]
    public string DeviceId { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "device_name", Length = 128, IsNullable = false)]
    public string DeviceName { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "protocol", Length = 64, IsNullable = false)]
    public string Protocol { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    [SugarColumn(ColumnName = "is_connected")]
    public bool IsConnected { get; set; }

    [SugarColumn(ColumnName = "status", Length = 32, IsNullable = false)]
    public string Status { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "consecutive_failures")]
    public int ConsecutiveFailures { get; set; }

    [SugarColumn(ColumnName = "total_reads")]
    public long TotalReads { get; set; }

    [SugarColumn(ColumnName = "successful_reads")]
    public long SuccessfulReads { get; set; }

    [SugarColumn(ColumnName = "failed_reads")]
    public long FailedReads { get; set; }

    [SugarColumn(ColumnName = "success_rate")]
    public double SuccessRate { get; set; }

    [SugarColumn(ColumnName = "last_poll_utc")]
    public DateTime LastPollUtc { get; set; }

    [SugarColumn(ColumnName = "last_success_utc")]
    public DateTime LastSuccessUtc { get; set; }

    [SugarColumn(ColumnName = "last_failure_utc")]
    public DateTime LastFailureUtc { get; set; }

    [SugarColumn(ColumnName = "last_reconnect_delay_ms")]
    public int LastReconnectDelayMs { get; set; }

    [SugarColumn(ColumnName = "last_error", ColumnDataType = "text", IsNullable = true)]
    public string LastError { get; set; } = string.Empty;

    [SugarColumn(ColumnName = "updated_utc")]
    public DateTime UpdatedUtc { get; set; }
}
