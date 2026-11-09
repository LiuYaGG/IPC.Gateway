/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Watchdog
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogStates
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Watchdog
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
namespace IPC.Gateway.Watchdog;

public static class GatewayWatchdogStates
{
    public const string Disabled = "Disabled";
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unhealthy = "Unhealthy";
    public const string Recovering = "Recovering";
    public const string Protected = "Protected";
}

public sealed class GatewayWatchdogCheckResult
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = GatewayWatchdogStates.Healthy;
    public string Message { get; set; } = string.Empty;
    public DateTime ObservedTime { get; set; } = DateTime.Now;
    public bool RecoveryRecommended { get; set; }
}

public sealed class GatewayWatchdogRecoveryEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class GatewayRestartProtectionStatus
{
    public int RecentRecoveryCount { get; set; }
    public int RecentHostRestartRequestCount { get; set; }
    public bool RecoveryBlocked { get; set; }
    public bool HostRestartBlocked { get; set; }
    public DateTime WindowStartTime { get; set; }
    public DateTime NextAllowedRecoveryTime { get; set; }
}

public sealed class GatewayWatchdogSnapshot
{
    public bool Enabled { get; set; }
    public string State { get; set; } = GatewayWatchdogStates.Disabled;
    public DateTime StartedTime { get; set; } = DateTime.Now;
    public DateTime LastCheckTime { get; set; }
    public DateTime LastHealthyTime { get; set; }
    public DateTime LastRecoveryTime { get; set; }
    public string LastIssue { get; set; } = string.Empty;
    public long CheckCount { get; set; }
    public long RecoveryAttemptCount { get; set; }
    public long RecoverySuccessCount { get; set; }
    public long RecoveryFailureCount { get; set; }
    public long BlockedRecoveryCount { get; set; }
    public long HostRestartRequestCount { get; set; }
    public IList<GatewayWatchdogCheckResult> Checks { get; set; } = new List<GatewayWatchdogCheckResult>();
    public IList<GatewayWatchdogRecoveryEvent> RecentEvents { get; set; } = new List<GatewayWatchdogRecoveryEvent>();
    public GatewayRestartProtectionStatus RestartProtection { get; set; } = new GatewayRestartProtectionStatus();
}
