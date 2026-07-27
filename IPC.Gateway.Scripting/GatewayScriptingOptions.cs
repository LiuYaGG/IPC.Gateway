namespace IPC.Gateway.Scripting;

/// <summary>
/// 定义脚本模块的文件目录、队列和安全执行参数。
/// </summary>
public sealed class GatewayScriptingOptions
{
    public string ConfigurationFile { get; set; } = "Data/Scripting/config.json";
    public string OutboxDirectory { get; set; } = "Data/Scripting/Outbox";
    public int MaxPendingWrites { get; set; } = 10000;
    public int MaxDatabaseRetryCount { get; set; } = 10;
    public int DatabaseRetryBaseSeconds { get; set; } = 2;
    public int SchedulerResolutionMilliseconds { get; set; } = 500;
    public int MaxRecentLogsPerScript { get; set; } = 50;

    /// <summary>
    /// 规范化脚本模块参数并返回独立副本。
    /// </summary>
    public GatewayScriptingOptions Normalize()
    {
        return new GatewayScriptingOptions
        {
            ConfigurationFile = string.IsNullOrWhiteSpace(ConfigurationFile) ? "Data/Scripting/config.json" : ConfigurationFile.Trim(),
            OutboxDirectory = string.IsNullOrWhiteSpace(OutboxDirectory) ? "Data/Scripting/Outbox" : OutboxDirectory.Trim(),
            MaxPendingWrites = Math.Clamp(MaxPendingWrites, 100, 1_000_000),
            MaxDatabaseRetryCount = Math.Clamp(MaxDatabaseRetryCount, 1, 100),
            DatabaseRetryBaseSeconds = Math.Clamp(DatabaseRetryBaseSeconds, 1, 300),
            SchedulerResolutionMilliseconds = Math.Clamp(SchedulerResolutionMilliseconds, 100, 5000),
            MaxRecentLogsPerScript = Math.Clamp(MaxRecentLogsPerScript, 10, 500)
        };
    }
}
