/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Watchdog
* 项目描述 ：
* 类 名 称 ：GatewayRestartProtectionStore
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
using System.Text;
using System.Text.Json;

namespace IPC.Gateway.Watchdog;

public sealed class GatewayRestartProtectionStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public GatewayRestartProtectionStore(GatewayWatchdogOptions options)
    {
        string directory = ResolveStateDirectory(options);
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "restart-protection.json");
    }

    public GatewayRestartProtectionState Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new GatewayRestartProtectionState();

            string json = File.ReadAllText(_path, Encoding.UTF8);
            return JsonSerializer.Deserialize<GatewayRestartProtectionState>(json, _jsonOptions) ?? new GatewayRestartProtectionState();
        }
        catch
        {
            return new GatewayRestartProtectionState();
        }
    }

    public void Save(GatewayRestartProtectionState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string json = JsonSerializer.Serialize(state ?? new GatewayRestartProtectionState(), _jsonOptions);
        File.WriteAllText(_path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string ResolveStateDirectory(GatewayWatchdogOptions options)
    {
        string directory = string.IsNullOrWhiteSpace(options.StateDirectory) ? "Data/Watchdog" : options.StateDirectory.Trim();
        if (!Path.IsPathRooted(directory))
            directory = Path.Combine(AppContext.BaseDirectory, directory);
        return Path.GetFullPath(directory);
    }
}

public sealed class GatewayRestartProtectionState
{
    public IList<DateTime> RecoveryAttemptsUtc { get; set; } = new List<DateTime>();
    public IList<DateTime> HostRestartRequestsUtc { get; set; } = new List<DateTime>();
    public DateTime LastRecoveryUtc { get; set; }

    public void Prune(DateTime nowUtc, GatewayWatchdogOptions options)
    {
        DateTime recoveryCutoff = nowUtc.AddMinutes(-Math.Max(1, options.RecoveryWindowMinutes));
        DateTime restartCutoff = nowUtc.AddMinutes(-Math.Max(1, options.HostRestartProtectionWindowMinutes));
        RecoveryAttemptsUtc = RecoveryAttemptsUtc.Where(item => item >= recoveryCutoff).ToList();
        HostRestartRequestsUtc = HostRestartRequestsUtc.Where(item => item >= restartCutoff).ToList();
    }

    public GatewayRestartProtectionStatus ToStatus(DateTime nowUtc, GatewayWatchdogOptions options)
    {
        Prune(nowUtc, options);
        DateTime nextRecovery = LastRecoveryUtc == default
            ? DateTime.MinValue
            : LastRecoveryUtc.AddSeconds(Math.Max(1, options.RecoveryCooldownSeconds));
        return new GatewayRestartProtectionStatus
        {
            RecentRecoveryCount = RecoveryAttemptsUtc.Count,
            RecentHostRestartRequestCount = HostRestartRequestsUtc.Count,
            RecoveryBlocked = RecoveryAttemptsUtc.Count >= Math.Max(1, options.MaxRecoveriesPerWindow) || nowUtc < nextRecovery,
            HostRestartBlocked = HostRestartRequestsUtc.Count >= Math.Max(1, options.MaxHostRestartRequestsPerWindow),
            WindowStartTime = nowUtc.AddMinutes(-Math.Max(1, options.RecoveryWindowMinutes)).ToLocalTime(),
            NextAllowedRecoveryTime = nextRecovery == DateTime.MinValue ? DateTime.MinValue : nextRecovery.ToLocalTime()
        };
    }
}
