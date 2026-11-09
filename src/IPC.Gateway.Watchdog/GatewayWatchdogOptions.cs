/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Watchdog
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogOptions
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

public sealed class GatewayWatchdogOptions
{
    public bool Enabled { get; set; } = true;
    public int CheckIntervalSeconds { get; set; } = 10;
    public int StartupGraceSeconds { get; set; } = 30;
    public int RuntimeNoProgressSeconds { get; set; } = 180;
    public int RecoveryCooldownSeconds { get; set; } = 60;
    public int RecoveryTimeoutSeconds { get; set; } = 30;
    public int MaxRecoveriesPerWindow { get; set; } = 3;
    public int RecoveryWindowMinutes { get; set; } = 10;
    public int MaxHostRestartRequestsPerWindow { get; set; } = 2;
    public int HostRestartProtectionWindowMinutes { get; set; } = 30;
    public bool RequestHostStopOnUnrecoverable { get; set; } = false;
    public string StateDirectory { get; set; } = "Data/Watchdog";

    public bool MonitorMqtt { get; set; } = true;
    public int MqttDisconnectedSeconds { get; set; } = 300;
    public bool MonitorHistory { get; set; } = true;
    public bool MonitorRuleEngine { get; set; } = true;
    public bool MonitorOpcUa { get; set; } = true;
    public bool MonitorScheduler { get; set; } = true;
}
