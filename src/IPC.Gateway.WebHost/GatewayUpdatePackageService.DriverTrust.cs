using IPC.Gateway.Core.Gateway;
using IPC.Plc.Communication.Infrastructure;

namespace IPC.Gateway.WebHost;

public sealed partial class GatewayUpdatePackageService
{
    internal static int PrepareOfflineDriverTrust(string package,string installedDirectory,string updatesDirectory)
    {
        string install=Path.GetFullPath(installedDirectory);
        string updates=Path.GetFullPath(updatesDirectory);
        using var settings=new ConfigurationBuilder().SetBasePath(install).AddJsonFile("appsettings.json")
            .AddJsonFile("appsettings.Production.json",true).AddEnvironmentVariables().Build() as ConfigurationRoot
            ?? throw new InvalidOperationException("未能加载原网关配置。");
        var options=GatewayUpdateMaintenanceOptions.FromConfiguration(settings);
        options.InstallDirectory=install;
        options.UpdateDirectory=updates;
        var validationService=new GatewayUpdatePackageService(options);
        using var hold=File.OpenRead(package);
        var validation=validationService.ReadAndValidatePackage(package);
        if(!validation.Trust.ContentHashValid)throw new InvalidDataException("安装包缺少有效文件摘要。");
        var manifest=validation.Manifest;
        string prefix=NormalizeEntryDirectory(manifest.EntryDirectory).TrimEnd('/')+"/";
        foreach(var file in manifest.Files.Where(f=>f.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
        {
            var path=GetSafeExtractPath(AppContext.BaseDirectory,file.Path[prefix.Length..]);
            if(!File.Exists(path)||!ComputeSha256(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("待发布文件与原 ZIP 不一致："+file.Path);
        }
        var section=settings.GetSection("Gateway:ProtocolDrivers");
        var trust=Path.GetFullPath(section["TrustedPublicKeyPath"] ?? Path.Combine(updates,"DriverTrust","public-key.pem"),install);
        PlcDriverPluginRegistry.ConfigureTrustPolicy(section.GetValue<bool>("RequireSignature"),section["TrustedPublicKeyPem"] ?? "",trust);
        return PlcDriverPluginRegistry.PreserveUpgradeTrust(AppContext.BaseDirectory,install,manifest.PackageId,ComputeSha256(package),install);
    }
    private sealed class CompletedDriverTrustRecovery
    {
        public string ActionId { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string PackageSha256 { get; set; } = string.Empty;
        public string InstallDirectory { get; set; } = string.Empty;
        public string TrustStateSha256 { get; set; } = string.Empty;
        public DateTime CompletedUtc { get; set; }
    }

    // Receipts skip redundant recovery work; they never grant a driver permission to load.
    // DLL content and signatures are still checked by PlcDriverPluginRegistry.
    public void RestoreCompletedUpgradeTrust()
    {
        lock (_actionLock)
        {
            if (_completedTrustRestored) return;
            GatewayPendingUpdateAction? action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath());
            // The Windows helper starts the new service before changing Applying to Applied.
            if (action?.ActionType != "Upgrade" || action.Status is not ("Applying" or "Applied")) return;
            if (string.IsNullOrWhiteSpace(action.ActionId)) return;
            string install = ResolveInstallDirectory();
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(action.TargetDirectory)).Equals(install, StringComparison.OrdinalIgnoreCase)) return;
            string stateHash = PlcDriverPluginRegistry.GetUpgradeTrustStateSha256();
            string receiptPath = Path.Combine(ResolveUpdateDirectory(), "DriverTrust", "completed-recovery.json");
            CompletedDriverTrustRecovery? receipt = ReadJson<CompletedDriverTrustRecovery>(receiptPath);
            if (receipt?.ActionId == action.ActionId && receipt.PackageId == action.PackageId &&
                receipt.PackageSha256 is { Length: 64 } && receipt.TrustStateSha256 == stateHash &&
                string.Equals(receipt.InstallDirectory, install, StringComparison.OrdinalIgnoreCase))
            {
                _completedTrustRestored = true;
                GatewayStartupTiming.RecordDetail("恢复升级驱动信任", $"action={action.ActionId} result=AlreadyCompleted");
                return;
            }

            GatewayUpdatePackageRecord record = FindPackage(action.PackageId);
            // New hosts already preserve trust while validating/staging the upgrade.
            if (action.DriverTrustPrepared && action.DriverTrustPackageSha256 == record.Sha256 &&
                action.DriverTrustStateSha256 == stateHash)
            {
                CompleteRecovery("PreparedBeforeRestart");
                return;
            }

            // One-time migration from older hosts, or recovery after trust data changed.
            GatewayRollbackPoint rollback = FindRollback(action.RollbackId);
            string previous = Path.Combine(rollback.Directory, "payload");
            if (!Directory.Exists(previous)) return;
            var validation = ValidateStoredPackage(record);
            if (!validation.Trust.ContentHashValid) return;
            string payload = Path.Combine(GetStagingDirectory(), "trust-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(payload);
                ExtractPayload(record.StoredPath, validation.Manifest.EntryDirectory, payload, driversOnly: true);
                int restored = PlcDriverPluginRegistry.PreserveUpgradeTrust(payload, install, record.PackageId, record.Sha256, previous);
                if (restored > 0) PlcDriverPluginRegistry.RefreshDefaultPlugins();
                CompleteRecovery("RecoveredOnce");
            }
            finally { if (Directory.Exists(payload)) Directory.Delete(payload, true); }

            void CompleteRecovery(string result)
            {
                WriteJson(receiptPath, new CompletedDriverTrustRecovery
                {
                    ActionId = action.ActionId,
                    PackageId = record.PackageId,
                    PackageSha256 = record.Sha256,
                    InstallDirectory = install,
                    TrustStateSha256 = PlcDriverPluginRegistry.GetUpgradeTrustStateSha256(),
                    CompletedUtc = DateTime.UtcNow
                });
                _completedTrustRestored = true;
                GatewayStartupTiming.RecordDetail("恢复升级驱动信任", $"action={action.ActionId} result={result}");
            }
        }
    }
}
