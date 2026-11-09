/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogConfigurationStore
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
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IPC.Gateway.Watchdog;

namespace IPC.Gateway.WebHost;

public sealed class GatewayWatchdogConfigurationStore
{
    private readonly GatewayWatchdogOptions _current;
    private readonly IWebHostEnvironment _environment;
    private readonly object _sync = new object();

    public GatewayWatchdogConfigurationStore(GatewayWatchdogOptions current, IWebHostEnvironment environment)
    {
        _current = current ?? new GatewayWatchdogOptions();
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public GatewayWatchdogOptions Get()
    {
        lock (_sync)
            return Clone(_current);
    }

    public GatewayWatchdogOptions Save(GatewayWatchdogOptions options)
    {
        GatewayWatchdogOptions normalized = Normalize(options);
        lock (_sync)
        {
            Apply(_current, normalized);
            SaveToJson(normalized);
            return Clone(_current);
        }
    }

    private void SaveToJson(GatewayWatchdogOptions options)
    {
        string path = ResolveWritableSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root = LoadRoot(path);
        JsonObject? gateway = root["Gateway"] as JsonObject;
        if (gateway == null)
        {
            gateway = new JsonObject();
            root["Gateway"] = gateway;
        }
        gateway["Watchdog"] = ToJson(options);

        JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(path, root.ToJsonString(jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private string ResolveWritableSettingsPath()
    {
        string environmentPath = Path.Combine(_environment.ContentRootPath, $"appsettings.{_environment.EnvironmentName}.json");
        if (File.Exists(environmentPath) && !environmentPath.EndsWith(".example.json", StringComparison.OrdinalIgnoreCase))
            return environmentPath;
        return Path.Combine(_environment.ContentRootPath, "appsettings.json");
    }

    private static JsonObject LoadRoot(string path)
    {
        if (!File.Exists(path))
            return new JsonObject();
        JsonNode? node = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
        return node as JsonObject ?? new JsonObject();
    }

    private static JsonObject ToJson(GatewayWatchdogOptions options)
    {
        return new JsonObject
        {
            ["Enabled"] = options.Enabled,
            ["CheckIntervalSeconds"] = options.CheckIntervalSeconds,
            ["StartupGraceSeconds"] = options.StartupGraceSeconds,
            ["RuntimeNoProgressSeconds"] = options.RuntimeNoProgressSeconds,
            ["RecoveryCooldownSeconds"] = options.RecoveryCooldownSeconds,
            ["RecoveryTimeoutSeconds"] = options.RecoveryTimeoutSeconds,
            ["MaxRecoveriesPerWindow"] = options.MaxRecoveriesPerWindow,
            ["RecoveryWindowMinutes"] = options.RecoveryWindowMinutes,
            ["MaxHostRestartRequestsPerWindow"] = options.MaxHostRestartRequestsPerWindow,
            ["HostRestartProtectionWindowMinutes"] = options.HostRestartProtectionWindowMinutes,
            ["RequestHostStopOnUnrecoverable"] = options.RequestHostStopOnUnrecoverable,
            ["StateDirectory"] = options.StateDirectory,
            ["MonitorMqtt"] = options.MonitorMqtt,
            ["MqttDisconnectedSeconds"] = options.MqttDisconnectedSeconds,
            ["MonitorHistory"] = options.MonitorHistory,
            ["MonitorRuleEngine"] = options.MonitorRuleEngine,
            ["MonitorOpcUa"] = options.MonitorOpcUa,
            ["MonitorScheduler"] = options.MonitorScheduler
        };
    }

    private static GatewayWatchdogOptions Normalize(GatewayWatchdogOptions? options)
    {
        GatewayWatchdogOptions source = options ?? new GatewayWatchdogOptions();
        return new GatewayWatchdogOptions
        {
            Enabled = source.Enabled,
            CheckIntervalSeconds = Clamp(source.CheckIntervalSeconds, 1, 3600),
            StartupGraceSeconds = Clamp(source.StartupGraceSeconds, 0, 3600),
            RuntimeNoProgressSeconds = Clamp(source.RuntimeNoProgressSeconds, 30, 86400),
            RecoveryCooldownSeconds = Clamp(source.RecoveryCooldownSeconds, 1, 86400),
            RecoveryTimeoutSeconds = Clamp(source.RecoveryTimeoutSeconds, 5, 3600),
            MaxRecoveriesPerWindow = Clamp(source.MaxRecoveriesPerWindow, 1, 100),
            RecoveryWindowMinutes = Clamp(source.RecoveryWindowMinutes, 1, 1440),
            MaxHostRestartRequestsPerWindow = Clamp(source.MaxHostRestartRequestsPerWindow, 0, 100),
            HostRestartProtectionWindowMinutes = Clamp(source.HostRestartProtectionWindowMinutes, 1, 1440),
            RequestHostStopOnUnrecoverable = source.RequestHostStopOnUnrecoverable,
            StateDirectory = string.IsNullOrWhiteSpace(source.StateDirectory) ? "Data/Watchdog" : source.StateDirectory.Trim(),
            MonitorMqtt = source.MonitorMqtt,
            MqttDisconnectedSeconds = Clamp(source.MqttDisconnectedSeconds, 30, 86400),
            MonitorHistory = source.MonitorHistory,
            MonitorRuleEngine = source.MonitorRuleEngine,
            MonitorOpcUa = source.MonitorOpcUa,
            MonitorScheduler = source.MonitorScheduler
        };
    }

    private static GatewayWatchdogOptions Clone(GatewayWatchdogOptions source)
    {
        return Normalize(source);
    }

    private static void Apply(GatewayWatchdogOptions target, GatewayWatchdogOptions source)
    {
        target.Enabled = source.Enabled;
        target.CheckIntervalSeconds = source.CheckIntervalSeconds;
        target.StartupGraceSeconds = source.StartupGraceSeconds;
        target.RuntimeNoProgressSeconds = source.RuntimeNoProgressSeconds;
        target.RecoveryCooldownSeconds = source.RecoveryCooldownSeconds;
        target.RecoveryTimeoutSeconds = source.RecoveryTimeoutSeconds;
        target.MaxRecoveriesPerWindow = source.MaxRecoveriesPerWindow;
        target.RecoveryWindowMinutes = source.RecoveryWindowMinutes;
        target.MaxHostRestartRequestsPerWindow = source.MaxHostRestartRequestsPerWindow;
        target.HostRestartProtectionWindowMinutes = source.HostRestartProtectionWindowMinutes;
        target.RequestHostStopOnUnrecoverable = source.RequestHostStopOnUnrecoverable;
        target.StateDirectory = source.StateDirectory;
        target.MonitorMqtt = source.MonitorMqtt;
        target.MqttDisconnectedSeconds = source.MqttDisconnectedSeconds;
        target.MonitorHistory = source.MonitorHistory;
        target.MonitorRuleEngine = source.MonitorRuleEngine;
        target.MonitorOpcUa = source.MonitorOpcUa;
        target.MonitorScheduler = source.MonitorScheduler;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
