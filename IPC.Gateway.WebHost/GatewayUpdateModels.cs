/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayUpdateMaintenanceOptions
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
using System.Reflection;

namespace IPC.Gateway.WebHost;

public sealed class GatewayUpdateMaintenanceOptions
{
    public bool Enabled { get; set; } = true;
    public string ProductId { get; set; } = "IPC.Gateway";
    public string UpdateDirectory { get; set; } = "Data/Updates";
    public string InstallDirectory { get; set; } = string.Empty;
    public int MaxPackageMegabytes { get; set; } = 1024;
    public int KeepRollbackCount { get; set; } = 5;
    public IList<string> PreservePaths { get; set; } = new List<string>
    {
        "Data",
        "appsettings.json",
        "appsettings.Production.json",
        "appsettings.Development.json"
    };

    public static GatewayUpdateMaintenanceOptions FromConfiguration(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Maintenance:Updates");
        GatewayUpdateMaintenanceOptions defaults = new GatewayUpdateMaintenanceOptions();
        return new GatewayUpdateMaintenanceOptions
        {
            Enabled = GetBool(section, "Enabled", defaults.Enabled),
            ProductId = section["ProductId"] ?? defaults.ProductId,
            UpdateDirectory = section["UpdateDirectory"] ?? defaults.UpdateDirectory,
            InstallDirectory = section["InstallDirectory"] ?? string.Empty,
            MaxPackageMegabytes = GetInt(section, "MaxPackageMegabytes", defaults.MaxPackageMegabytes),
            KeepRollbackCount = GetInt(section, "KeepRollbackCount", defaults.KeepRollbackCount),
            PreservePaths = section.GetSection("PreservePaths").Get<string[]>()?.ToList() ?? defaults.PreservePaths
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

public sealed class GatewayUpdatePackageManifest
{
    public string PackageId { get; set; } = string.Empty;
    public string Product { get; set; } = "IPC.Gateway";
    public string PackageType { get; set; } = "Upgrade";
    public string Version { get; set; } = string.Empty;
    public string MinVersion { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public string EntryDirectory { get; set; } = "payload";
    public bool RequiresRestart { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}

public sealed class GatewayUpdatePackageRecord
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageType { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime UploadedTime { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Ready";
    public string ErrorMessage { get; set; } = string.Empty;
    public GatewayUpdatePackageManifest Manifest { get; set; } = new GatewayUpdatePackageManifest();
}

public sealed class GatewayRollbackPoint
{
    public string RollbackId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string SourcePackageId { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public string Directory { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
}

public sealed class GatewayPendingUpdateAction
{
    public string ActionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string RollbackId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string SourceDirectory { get; set; } = string.Empty;
    public string TargetDirectory { get; set; } = string.Empty;
    public string RollbackDirectory { get; set; } = string.Empty;
    public string ScriptPath { get; set; } = string.Empty;
    public bool RequiresServiceRestart { get; set; } = true;
    public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Pending";
}

public sealed class GatewayUpdateStatus
{
    public bool Enabled { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public string UpdateDirectory { get; set; } = string.Empty;
    public string OfflineScriptPath { get; set; } = string.Empty;
    public GatewayPendingUpdateAction? PendingAction { get; set; }
    public IList<GatewayUpdatePackageRecord> Packages { get; set; } = new List<GatewayUpdatePackageRecord>();
    public IList<GatewayRollbackPoint> RollbackPoints { get; set; } = new List<GatewayRollbackPoint>();
}

public sealed class GatewayPrepareUpdateResult
{
    public bool Prepared { get; set; }
    public string Message { get; set; } = string.Empty;
    public GatewayPendingUpdateAction PendingAction { get; set; } = new GatewayPendingUpdateAction();
}

internal static class GatewayUpdateVersion
{
    public static string Current =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty;
}
