using IPC.Gateway.Scripting;
using Microsoft.Extensions.Configuration;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 从 WebHost 配置创建脚本模块参数。
/// </summary>
public static class GatewayScriptingSetup
{
    /// <summary>
    /// 读取 Gateway:Scripting 配置节并创建规范化参数。
    /// </summary>
    public static GatewayScriptingOptions FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Scripting");
        return new GatewayScriptingOptions
        {
            ConfigurationFile = section["ConfigurationFile"] ?? "Data/Scripting/config.json",
            OutboxDirectory = section["OutboxDirectory"] ?? "Data/Scripting/Outbox",
            MaxPendingWrites = GetInt(section, "MaxPendingWrites", 10000),
            MaxDatabaseRetryCount = GetInt(section, "MaxDatabaseRetryCount", 10),
            DatabaseRetryBaseSeconds = GetInt(section, "DatabaseRetryBaseSeconds", 2),
            SchedulerResolutionMilliseconds = GetInt(section, "SchedulerResolutionMilliseconds", 500),
            MaxRecentLogsPerScript = GetInt(section, "MaxRecentLogsPerScript", 50),
            MaxTagLinkageDepth = GetInt(section, "MaxTagLinkageDepth", 8)
        }.Normalize();
    }

    /// <summary>
    /// 读取整数配置并在格式无效时使用默认值。
    /// </summary>
    private static int GetInt(IConfiguration section, string key, int fallback)
    {
        return int.TryParse(section[key], out int value) ? value : fallback;
    }
}
