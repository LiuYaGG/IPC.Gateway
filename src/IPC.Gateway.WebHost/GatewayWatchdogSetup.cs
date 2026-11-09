/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogSetup
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
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
using IPC.Gateway.Watchdog;
using Microsoft.Extensions.Hosting;

namespace IPC.Gateway.WebHost;

public static class GatewayWatchdogSetup
{
    public static IServiceCollection AddGatewayWatchdog(this IServiceCollection services, IConfiguration configuration)
    {
        GatewayWatchdogOptions options = CreateOptions(configuration);
        services.AddSingleton(options);
        services.AddSingleton<GatewayWatchdogHostedService>();
        services.AddSingleton<IGatewayWatchdogService>(provider => provider.GetRequiredService<GatewayWatchdogHostedService>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GatewayWatchdogHostedService>());
        return services;
    }

    private static GatewayWatchdogOptions CreateOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Watchdog");
        GatewayWatchdogOptions defaults = new GatewayWatchdogOptions();
        return new GatewayWatchdogOptions
        {
            Enabled = GetBool(section, "Enabled", defaults.Enabled),
            CheckIntervalSeconds = GetInt(section, "CheckIntervalSeconds", defaults.CheckIntervalSeconds),
            StartupGraceSeconds = GetInt(section, "StartupGraceSeconds", defaults.StartupGraceSeconds),
            RuntimeNoProgressSeconds = GetInt(section, "RuntimeNoProgressSeconds", defaults.RuntimeNoProgressSeconds),
            RecoveryCooldownSeconds = GetInt(section, "RecoveryCooldownSeconds", defaults.RecoveryCooldownSeconds),
            RecoveryTimeoutSeconds = GetInt(section, "RecoveryTimeoutSeconds", defaults.RecoveryTimeoutSeconds),
            MaxRecoveriesPerWindow = GetInt(section, "MaxRecoveriesPerWindow", defaults.MaxRecoveriesPerWindow),
            RecoveryWindowMinutes = GetInt(section, "RecoveryWindowMinutes", defaults.RecoveryWindowMinutes),
            MaxHostRestartRequestsPerWindow = GetInt(section, "MaxHostRestartRequestsPerWindow", defaults.MaxHostRestartRequestsPerWindow),
            HostRestartProtectionWindowMinutes = GetInt(section, "HostRestartProtectionWindowMinutes", defaults.HostRestartProtectionWindowMinutes),
            RequestHostStopOnUnrecoverable = GetBool(section, "RequestHostStopOnUnrecoverable", defaults.RequestHostStopOnUnrecoverable),
            StateDirectory = section["StateDirectory"] ?? defaults.StateDirectory,
            MonitorMqtt = GetBool(section, "MonitorMqtt", defaults.MonitorMqtt),
            MqttDisconnectedSeconds = GetInt(section, "MqttDisconnectedSeconds", defaults.MqttDisconnectedSeconds),
            MonitorHistory = GetBool(section, "MonitorHistory", defaults.MonitorHistory),
            MonitorRuleEngine = GetBool(section, "MonitorRuleEngine", defaults.MonitorRuleEngine),
            MonitorOpcUa = GetBool(section, "MonitorOpcUa", defaults.MonitorOpcUa),
            MonitorScheduler = GetBool(section, "MonitorScheduler", defaults.MonitorScheduler)
        };
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        string? value = configuration[key];
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        string? value = configuration[key];
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
    }
}
