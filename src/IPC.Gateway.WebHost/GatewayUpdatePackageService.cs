/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayUpdatePackageService
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
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IPC.Gateway.WebHost;

public sealed class GatewayUpdatePackageService
{
    private const string ManifestEntryName = "ipc-gateway-package.json";
    private const string PendingActionFileName = "pending-action.json";
    private const string OfflineScriptFileName = "apply-pending-update.ps1";
    private readonly GatewayUpdateMaintenanceOptions _options;
    private readonly IGatewayUpdateProcessLauncher _processLauncher;
    private readonly object _actionLock = new();
    private readonly HashSet<string> _incomingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions;

    public GatewayUpdatePackageService(GatewayUpdateMaintenanceOptions options, IGatewayUpdateProcessLauncher? processLauncher = null)
    {
        _options = options ?? new GatewayUpdateMaintenanceOptions();
        _processLauncher = processLauncher ?? new GatewayUpdateProcessLauncher();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public GatewayUpdateStatus GetStatus()
    {
        lock (_actionLock)
        {
            EnsureDirectories();
            if (_options.Enabled) ApplyRetentionPolicy();
            return GetStatusCore();
        }
    }

    private GatewayUpdateStatus GetStatusCore()
    {
        EnsureDirectories();
        return new GatewayUpdateStatus
        {
            Enabled = _options.Enabled,
            SupportsApply = _processLauncher.IsSupported,
            MaxPackageMegabytes = Math.Max(1, _options.MaxPackageMegabytes),
            ProductId = _options.ProductId,
            CurrentVersion = GatewayUpdateVersion.Current,
            InstallDirectory = ResolveInstallDirectory(),
            UpdateDirectory = ResolveUpdateDirectory(),
            OfflineScriptPath = GetOfflineScriptPath(),
            RequirePackageFileDigests = _options.RequirePackageFileDigests,
            RequirePackageSignature = _options.RequirePackageSignature,
            TrustedPackagePublicKeyConfigured = HasTrustedPackagePublicKey(),
            PendingAction = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath()),
            Packages = ReadPackageRecords(),
            RollbackPoints = ReadRollbackPoints()
        };
    }

    public async Task<GatewayUpdatePackageRecord> StorePackageAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("升级维护功能未启用。");
        if (file == null || file.Length == 0)
            throw new ArgumentException("请选择升级包文件。");
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("升级包必须是 zip 文件。");

        long maxBytes = Math.Max(1, _options.MaxPackageMegabytes) * 1024L * 1024L;
        if (file.Length > maxBytes)
            throw new ArgumentException($"升级包不能超过 {_options.MaxPackageMegabytes} MB。");

        EnsureDirectories();
        string tempPath = Path.Combine(GetIncomingDirectory(), Guid.NewGuid().ToString("N") + ".zip");
        lock (_actionLock) { EnsureNoRunningAction(); _incomingFiles.Add(tempPath); }
        try
        {
            await using (FileStream output = File.Create(tempPath))
            await using (Stream input = file.OpenReadStream())
            {
                await input.CopyToAsync(output, cancellationToken);
            }
            string sha256 = ComputeSha256(tempPath);
            GatewayUpdatePackageValidationResult validation = ReadAndValidatePackage(tempPath);
            GatewayUpdatePackageManifest manifest = validation.Manifest;
            string packageId = NormalizeId(string.IsNullOrWhiteSpace(manifest.PackageId)
                ? $"{manifest.PackageType}-{manifest.Version}-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : manifest.PackageId);
            manifest.PackageId = packageId;

            lock (_actionLock)
            {
            EnsureNoRunningAction();
            string storedZip = Path.Combine(GetPackagesDirectory(), packageId + ".zip");
            string storedJson = Path.Combine(GetPackagesDirectory(), packageId + ".json");
            if (File.Exists(storedZip) || File.Exists(storedJson))
                throw new InvalidOperationException("升级包已存在：" + packageId);

            File.Move(tempPath, storedZip);
            GatewayUpdatePackageRecord record = new GatewayUpdatePackageRecord
            {
                PackageId = packageId,
                PackageType = manifest.PackageType,
                Version = manifest.Version,
                FileName = Path.GetFileName(file.FileName),
                StoredPath = storedZip,
                Sha256 = sha256,
                SizeBytes = new FileInfo(storedZip).Length,
                UploadedTime = DateTime.UtcNow,
                Manifest = manifest
            };
            ApplyTrustResult(record, validation);
            WriteJson(storedJson, record);
            ApplyRetentionPolicy();
            return record;
            }
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
        finally
        {
            lock (_actionLock) { _incomingFiles.Remove(tempPath); }
        }
    }

    public GatewayPrepareUpdateResult PrepareUpgrade(string packageId)
    {
        lock (_actionLock)
        {
            EnsureNoRunningAction();
            return PrepareUpgradeCore(packageId);
        }
    }

    private GatewayPrepareUpdateResult PrepareUpgradeCore(string packageId)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("升级维护功能未启用。");

        GatewayUpdatePackageRecord record = FindPackage(packageId);
        GatewayUpdatePackageValidationResult validation = ValidateStoredPackage(record);
        GatewayUpdatePackageManifest manifest = validation.Manifest;
        string actionId = "upgrade-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string stagingRoot = Path.Combine(GetStagingDirectory(), actionId);
        string sourceDirectory = Path.Combine(stagingRoot, "payload");
        Directory.CreateDirectory(sourceDirectory);

        ExtractPayload(record.StoredPath, manifest.EntryDirectory, sourceDirectory);
        GatewayRollbackPoint rollbackPoint = CreateRollbackPoint(record);
        GatewayPendingUpdateAction action = new GatewayPendingUpdateAction
        {
            ActionId = actionId,
            ActionType = "Upgrade",
            PackageId = record.PackageId,
            Version = record.Version,
            PackagePath = record.StoredPath,
            SourceDirectory = sourceDirectory,
            TargetDirectory = ResolveInstallDirectory(),
            RollbackId = rollbackPoint.RollbackId,
            RollbackDirectory = rollbackPoint.Directory,
            RequiresServiceRestart = true,
            CreatedTime = DateTime.UtcNow,
            Status = "Pending"
        };
        action.ScriptPath = WriteOfflineScript(action);
        WriteJson(GetPendingActionPath(), action);
        return new GatewayPrepareUpdateResult
        {
            Prepared = true,
            Message = "升级包已暂存，请在维护窗口停止服务后执行离线升级脚本。",
            PendingAction = action
        };
    }

    public GatewayPrepareUpdateResult PrepareRollback(string rollbackId)
    {
        lock (_actionLock)
        {
            EnsureNoRunningAction();
            return PrepareRollbackCore(rollbackId);
        }
    }

    private GatewayPrepareUpdateResult PrepareRollbackCore(string rollbackId)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("升级维护功能未启用。");

        GatewayRollbackPoint rollback = FindRollback(rollbackId);
        string actionId = "rollback-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        GatewayPendingUpdateAction action = new GatewayPendingUpdateAction
        {
            ActionId = actionId,
            ActionType = "Rollback",
            RollbackId = rollback.RollbackId,
            Version = rollback.Version,
            SourceDirectory = Path.Combine(rollback.Directory, "payload"),
            TargetDirectory = ResolveInstallDirectory(),
            RollbackDirectory = rollback.Directory,
            RequiresServiceRestart = true,
            CreatedTime = DateTime.UtcNow,
            Status = "Pending"
        };
        action.ScriptPath = WriteOfflineScript(action);
        WriteJson(GetPendingActionPath(), action);
        return new GatewayPrepareUpdateResult
        {
            Prepared = true,
            Message = "回滚动作已准备好，请在维护窗口停止服务后执行离线升级脚本。",
            PendingAction = action
        };
    }

    public GatewayPendingUpdateAction ApplyPendingAction(string actionId, bool allowUpgrade = true, bool allowRollback = true)
    {
        lock (_actionLock)
        {
            if (!_options.Enabled) throw new InvalidOperationException("升级维护功能未启用。");
            if (!_processLauncher.IsSupported) throw new InvalidOperationException("界面自动升级仅支持以 Windows 服务方式运行的网关。");
            GatewayPendingUpdateAction action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath())
                ?? throw new InvalidOperationException("请先准备升级或回滚。");
            if (string.IsNullOrWhiteSpace(actionId) || !string.Equals(action.ActionId, actionId, StringComparison.Ordinal))
                throw new InvalidOperationException("待执行动作已变化，请刷新后重新确认。");
            if (action.Status != "Pending") throw new InvalidOperationException("该动作已执行或正在执行，请勿重复提交。");
            if ((action.ActionType == "Upgrade" && !allowUpgrade) || (action.ActionType == "Rollback" && !allowRollback))
                throw new UnauthorizedAccessException("当前用户没有执行该升级或回滚动作的权限。");
            if (!Path.TrimEndingDirectorySeparator(Path.GetFullPath(action.TargetDirectory)).Equals(ResolveInstallDirectory(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("待执行动作的安装目录不匹配。");
            action.TargetDirectory = ResolveInstallDirectory();
            if (action.ActionType == "Upgrade")
            {
                // Revalidate the original ZIP and re-extract a fresh payload before stopping the host.
                GatewayUpdatePackageRecord record = FindPackage(action.PackageId);
                GatewayUpdatePackageManifest manifest = ValidateStoredPackage(record).Manifest;
                action.SourceDirectory = Path.Combine(GetStagingDirectory(), "execute-" + Guid.NewGuid().ToString("N"), "payload");
                Directory.CreateDirectory(action.SourceDirectory);
                ExtractPayload(record.StoredPath, manifest.EntryDirectory, action.SourceDirectory);
            }
            else if (action.ActionType == "Rollback")
            {
                GatewayRollbackPoint rollback = FindRollback(action.RollbackId);
                action.SourceDirectory = Path.Combine(rollback.Directory, "payload");
                if (!Directory.Exists(action.SourceDirectory)) throw new InvalidOperationException("回滚文件不存在。");
            }
            else throw new InvalidOperationException("不支持的升级动作。");
            if (!File.Exists(Path.Combine(action.SourceDirectory, "IPC.Gateway.WebHost.dll")))
                throw new InvalidOperationException("升级或回滚文件缺少 IPC.Gateway.WebHost.dll。");
            action.ExecutionLogPath = Path.Combine(ResolveUpdateDirectory(), "execution-" + action.ActionId + ".log");
            action.ErrorMessage = string.Empty;
            action.Status = "Scheduled";
            action.ScriptPath = WriteOfflineScript(action);
            WriteJson(GetPendingActionPath(), action);
            try { _processLauncher.Launch(action.ScriptPath, action.ActionId); }
            catch (Exception ex)
            {
                action.Status = "Pending";
                action.ErrorMessage = ex.Message;
                WriteJson(GetPendingActionPath(), action);
                throw new InvalidOperationException("无法启动升级进程，请检查服务账号权限和执行日志。", ex);
            }
            return action;
        }
    }

    private void EnsureNoRunningAction()
    {
        GatewayPendingUpdateAction? action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath());
        if (action == null && File.Exists(GetPendingActionPath()))
            throw new InvalidOperationException("升级状态正在更新，请稍后重试。");
        string? state = action?.Status;
        if (state is "Scheduled" or "Applying") throw new InvalidOperationException("升级正在执行，请等待服务恢复。");
    }

    internal GatewayUpdatePackageManifest ReadAndValidateManifest(string zipPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry? manifestEntry = archive.GetEntry(ManifestEntryName);
        if (manifestEntry == null)
            throw new ArgumentException("升级包缺少 ipc-gateway-package.json 清单。");

        using Stream stream = manifestEntry.Open();
        GatewayUpdatePackageManifest? manifest = JsonSerializer.Deserialize<GatewayUpdatePackageManifest>(stream, _jsonOptions);
        if (manifest == null)
            throw new ArgumentException("升级包清单格式不正确。");

        manifest.Product = (manifest.Product ?? string.Empty).Trim();
        manifest.PackageId = (manifest.PackageId ?? string.Empty).Trim();
        manifest.PackageType = NormalizePackageType(manifest.PackageType);
        manifest.Version = (manifest.Version ?? string.Empty).Trim();
        manifest.MinVersion = (manifest.MinVersion ?? string.Empty).Trim();
        manifest.BuildId = (manifest.BuildId ?? string.Empty).Trim();
        manifest.EntryDirectory = NormalizeEntryDirectory(manifest.EntryDirectory);
        manifest.Description = (manifest.Description ?? string.Empty).Trim();
        manifest.HashAlgorithm = NormalizeHashAlgorithm(manifest.HashAlgorithm);
        manifest.SignatureAlgorithm = (manifest.SignatureAlgorithm ?? string.Empty).Trim();
        manifest.Signature = (manifest.Signature ?? string.Empty).Trim();
        manifest.Signer = (manifest.Signer ?? string.Empty).Trim();
        manifest.ManifestVersion = manifest.ManifestVersion <= 0 ? 1 : manifest.ManifestVersion;
        manifest.Files ??= new List<GatewayUpdatePackageFileDigest>();

        if (!string.Equals(manifest.Product, _options.ProductId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("升级包产品标识不匹配。");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new ArgumentException("升级包清单缺少版本号。");
        if (archive.Entries.All(entry => !IsPayloadEntry(entry, manifest.EntryDirectory)))
            throw new ArgumentException("升级包缺少 payload 目录。");

        _ = ValidatePackageTrust(archive, manifest);
        return manifest;
    }

    private GatewayUpdatePackageValidationResult ReadAndValidatePackage(string zipPath)
    {
        GatewayUpdatePackageManifest manifest = ReadAndValidateManifest(zipPath);
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        GatewayUpdatePackageTrustResult trust = ValidatePackageTrust(archive, manifest);
        return new GatewayUpdatePackageValidationResult(manifest, trust);
    }

    private GatewayUpdatePackageValidationResult ValidateStoredPackage(GatewayUpdatePackageRecord record)
    {
        if (!File.Exists(record.StoredPath))
            throw new InvalidOperationException("升级包文件不存在：" + record.StoredPath);

        string sha256 = ComputeSha256(record.StoredPath);
        if (!string.IsNullOrWhiteSpace(record.Sha256) &&
            !sha256.Equals(record.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("升级包归档摘要与入库记录不一致，可能已被篡改。");
        }

        GatewayUpdatePackageValidationResult validation = ReadAndValidatePackage(record.StoredPath);
        record.Sha256 = sha256;
        record.SizeBytes = new FileInfo(record.StoredPath).Length;
        record.Manifest = validation.Manifest;
        ApplyTrustResult(record, validation);
        WriteJson(Path.Combine(GetPackagesDirectory(), record.PackageId + ".json"), record);
        return validation;
    }

    private GatewayUpdatePackageTrustResult ValidatePackageTrust(ZipArchive archive, GatewayUpdatePackageManifest manifest)
    {
        ValidateArchiveEntryPaths(archive);
        IList<GatewayUpdatePackageFileDigest> files = NormalizeManifestFiles(manifest.Files);
        manifest.Files = files;

        if (files.Count == 0)
        {
            if (_options.RequirePackageFileDigests)
                throw new ArgumentException("升级包缺少文件摘要清单。");

            return new GatewayUpdatePackageTrustResult(
                ContentHashValid: false,
                SignatureValid: false,
                Status: "Unverified",
                Message: "升级包未提供文件摘要清单，仅允许兼容旧包流程。");
        }

        Dictionary<string, ZipArchiveEntry> archiveFiles = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (IsDirectoryEntry(entry))
                continue;

            string normalizedPath = NormalizePackageFilePath(entry.FullName);
            if (normalizedPath.Equals(ManifestEntryName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!archiveFiles.TryAdd(normalizedPath, entry))
                throw new ArgumentException("升级包中存在重复文件：" + normalizedPath);
        }

        HashSet<string> manifestPaths = files.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (GatewayUpdatePackageFileDigest file in files)
        {
            if (!archiveFiles.TryGetValue(file.Path, out ZipArchiveEntry? entry))
                throw new ArgumentException("升级包摘要清单声明的文件不存在：" + file.Path);
            if (entry.Length != file.SizeBytes)
                throw new ArgumentException("升级包文件大小与摘要清单不一致：" + file.Path);

            string actualSha256 = ComputeEntrySha256(entry);
            if (!actualSha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("升级包文件摘要校验失败：" + file.Path);
        }

        foreach (string extraPath in archiveFiles.Keys.Where(path => !manifestPaths.Contains(path)))
        {
            throw new ArgumentException("升级包包含未在摘要清单中声明的文件：" + extraPath);
        }

        bool signatureValid = ValidatePackageSignature(manifest, out string signatureMessage);
        if (signatureValid)
        {
            return new GatewayUpdatePackageTrustResult(
                ContentHashValid: true,
                SignatureValid: true,
                Status: "Trusted",
                Message: "文件摘要和发布签名校验通过。");
        }

        return new GatewayUpdatePackageTrustResult(
            ContentHashValid: true,
            SignatureValid: false,
            Status: "ContentVerified",
            Message: "文件摘要校验通过。" + signatureMessage);
    }

    private bool ValidatePackageSignature(GatewayUpdatePackageManifest manifest, out string message)
    {
        bool hasSignature = !string.IsNullOrWhiteSpace(manifest.Signature);
        if (!hasSignature)
        {
            if (_options.RequirePackageSignature)
                throw new ArgumentException("升级包缺少发布签名。");

            message = "未提供发布签名。";
            return false;
        }

        if (!manifest.SignatureAlgorithm.Equals("RS256", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("升级包签名算法不受支持：" + manifest.SignatureAlgorithm);

        string publicKeyPem = ReadTrustedPackagePublicKeyPem();
        if (string.IsNullOrWhiteSpace(publicKeyPem))
        {
            if (_options.RequirePackageSignature)
                throw new InvalidOperationException("已要求升级包签名校验，但未配置可信发布公钥。");

            message = "未配置可信发布公钥，签名未校验。";
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("升级包签名格式不正确。", exception);
        }

        try
        {
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            byte[] data = Encoding.UTF8.GetBytes(BuildManifestSigningPayload(manifest));
            bool valid = rsa.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (!valid)
                throw new ArgumentException("升级包签名校验失败。");
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("可信发布公钥配置无效或签名无法校验。", exception);
        }

        message = "发布签名校验通过。";
        return true;
    }

    private static IList<GatewayUpdatePackageFileDigest> NormalizeManifestFiles(IList<GatewayUpdatePackageFileDigest> files)
    {
        List<GatewayUpdatePackageFileDigest> normalized = new List<GatewayUpdatePackageFileDigest>();
        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (GatewayUpdatePackageFileDigest file in files ?? Array.Empty<GatewayUpdatePackageFileDigest>())
        {
            string path = NormalizePackageFilePath(file.Path);
            if (!paths.Add(path))
                throw new ArgumentException("升级包摘要清单存在重复文件：" + path);
            if (file.SizeBytes < 0)
                throw new ArgumentException("升级包摘要清单文件大小无效：" + path);

            string sha256 = (file.Sha256 ?? string.Empty).Trim().ToLowerInvariant();
            if (!IsSha256Hex(sha256))
                throw new ArgumentException("升级包摘要清单 SHA256 无效：" + path);

            normalized.Add(new GatewayUpdatePackageFileDigest
            {
                Path = path,
                Sha256 = sha256,
                SizeBytes = file.SizeBytes
            });
        }

        return normalized
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string BuildManifestSigningPayload(GatewayUpdatePackageManifest manifest)
    {
        IList<GatewayUpdatePackageFileDigest> files = NormalizeManifestFiles(manifest.Files);
        StringBuilder builder = new StringBuilder();
        AppendSigningLine(builder, "manifestVersion", (manifest.ManifestVersion <= 0 ? 1 : manifest.ManifestVersion).ToString(CultureInfo.InvariantCulture));
        AppendSigningLine(builder, "packageId", (manifest.PackageId ?? string.Empty).Trim());
        AppendSigningLine(builder, "product", (manifest.Product ?? string.Empty).Trim());
        AppendSigningLine(builder, "packageType", NormalizePackageType(manifest.PackageType));
        AppendSigningLine(builder, "version", (manifest.Version ?? string.Empty).Trim());
        AppendSigningLine(builder, "minVersion", (manifest.MinVersion ?? string.Empty).Trim());
        AppendSigningLine(builder, "buildId", (manifest.BuildId ?? string.Empty).Trim());
        AppendSigningLine(builder, "entryDirectory", NormalizeEntryDirectory(manifest.EntryDirectory));
        AppendSigningLine(builder, "requiresRestart", manifest.RequiresRestart ? "true" : "false");
        AppendSigningLine(builder, "hashAlgorithm", NormalizeHashAlgorithm(manifest.HashAlgorithm));
        AppendSigningLine(builder, "signatureAlgorithm", (manifest.SignatureAlgorithm ?? string.Empty).Trim());
        AppendSigningLine(builder, "signer", (manifest.Signer ?? string.Empty).Trim());
        AppendSigningLine(builder, "signedTime", manifest.SignedTime.HasValue
            ? manifest.SignedTime.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : string.Empty);
        foreach (GatewayUpdatePackageFileDigest file in files)
        {
            AppendSigningLine(
                builder,
                "file",
                file.Path + "|" + file.SizeBytes.ToString(CultureInfo.InvariantCulture) + "|" + file.Sha256);
        }

        return builder.ToString();
    }

    private static void AppendSigningLine(StringBuilder builder, string key, string value)
    {
        builder
            .Append(key)
            .Append('=')
            .Append((value ?? string.Empty).Replace("\r", " ").Replace("\n", " "))
            .Append('\n');
    }

    private static void ValidateArchiveEntryPaths(ZipArchive archive)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            _ = NormalizePackageFilePath(entry.FullName);
        }
    }

    private static string NormalizePackageFilePath(string value)
    {
        string raw = (value ?? string.Empty).Replace('\\', '/').Trim();
        if (raw.StartsWith("/", StringComparison.Ordinal) || raw.Contains(':'))
            throw new ArgumentException("升级包中存在非法路径：" + value);

        string[] segments = raw
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
            throw new ArgumentException("升级包中存在非法路径：" + value);

        return string.Join("/", segments);
    }

    private static bool IsSha256Hex(string value)
    {
        return value.Length == 64 && value.All(ch =>
            ch is >= '0' and <= '9' ||
            ch is >= 'a' and <= 'f' ||
            ch is >= 'A' and <= 'F');
    }

    private static string NormalizeHashAlgorithm(string value)
    {
        string algorithm = string.IsNullOrWhiteSpace(value) ? "SHA256" : value.Trim();
        if (algorithm.Equals("SHA256", StringComparison.OrdinalIgnoreCase) ||
            algorithm.Equals("SHA-256", StringComparison.OrdinalIgnoreCase))
        {
            return "SHA256";
        }

        throw new ArgumentException("升级包摘要算法不受支持：" + value);
    }

    private static string ComputeEntrySha256(ZipArchiveEntry entry)
    {
        using SHA256 sha256 = SHA256.Create();
        using Stream stream = entry.Open();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
               entry.FullName.EndsWith("\\", StringComparison.Ordinal);
    }

    private void ApplyTrustResult(GatewayUpdatePackageRecord record, GatewayUpdatePackageValidationResult validation)
    {
        record.ManifestVersion = validation.Manifest.ManifestVersion;
        record.FileCount = validation.Manifest.Files.Count;
        record.ContentHashValid = validation.Trust.ContentHashValid;
        record.SignatureValid = validation.Trust.SignatureValid;
        record.TrustStatus = validation.Trust.Status;
        record.TrustMessage = validation.Trust.Message;
        record.Signer = validation.Manifest.Signer;
        record.SignedTime = validation.Manifest.SignedTime;
    }

    private bool HasTrustedPackagePublicKey()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(ReadTrustedPackagePublicKeyPem());
        }
        catch
        {
            return false;
        }
    }

    private string ReadTrustedPackagePublicKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.TrustedPackagePublicKeyPem))
            return _options.TrustedPackagePublicKeyPem.Trim();

        if (string.IsNullOrWhiteSpace(_options.TrustedPackagePublicKeyPath))
            return string.Empty;

        string path = Environment.ExpandEnvironmentVariables(_options.TrustedPackagePublicKeyPath.Trim());
        if (!Path.IsPathRooted(path))
            path = Path.Combine(ResolveInstallDirectory(), path);

        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : string.Empty;
    }

    private GatewayRollbackPoint CreateRollbackPoint(GatewayUpdatePackageRecord sourcePackage)
    {
        string rollbackId = "rollback-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string rollbackRoot = Path.Combine(GetRollbacksDirectory(), rollbackId);
        string payloadRoot = Path.Combine(rollbackRoot, "payload");
        Directory.CreateDirectory(payloadRoot);
        CopyDirectory(ResolveInstallDirectory(), payloadRoot, CreatePreserveSet());

        (long sizeBytes, int fileCount) = CalculateDirectoryStats(payloadRoot);
        GatewayRollbackPoint point = new GatewayRollbackPoint
        {
            RollbackId = rollbackId,
            Version = GatewayUpdateVersion.Current,
            SourcePackageId = sourcePackage.PackageId,
            CreatedTime = DateTime.UtcNow,
            Directory = rollbackRoot,
            SizeBytes = sizeBytes,
            FileCount = fileCount
        };
        WriteJson(Path.Combine(rollbackRoot, "rollback.json"), point);
        PruneOldRollbackPoints();
        return point;
    }

    private void ExtractPayload(string zipPath, string entryDirectory, string targetDirectory)
    {
        string normalizedEntryDirectory = NormalizeEntryDirectory(entryDirectory);
        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!IsPayloadEntry(entry, normalizedEntryDirectory))
                continue;

            string relative = entry.FullName.Substring(normalizedEntryDirectory.Length).TrimStart('/', '\\');
            if (string.IsNullOrWhiteSpace(relative))
                continue;

            string targetPath = GetSafeExtractPath(targetDirectory, relative);
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private string WriteOfflineScript(GatewayPendingUpdateAction action)
    {
        string scriptPath = GetOfflineScriptPath();
        string pendingPath = GetPendingActionPath();
        string serviceName = "IPCGateway";
        string preserveNames = string.Join(", ", CreatePreserveSet().Select(path => "'" + EscapePowerShellPath(path) + "'"));
        string content = $$"""
            [CmdletBinding(SupportsShouldProcess = $true)]
            param(
                [string]$ServiceName = "{{serviceName}}",
                [string]$PendingActionPath = '{{EscapePowerShellPath(pendingPath)}}',
                [switch]$SkipServiceControl,
                [switch]$Automatic,
                [string]$ExpectedActionId = ''
            )

            $ErrorActionPreference = "Stop"
            # Give the HTTP response time to reach the browser before stopping its host.
            if ($Automatic) { Start-Sleep -Seconds 5 }
            $action = $null
            $transcribing = $false
            $executing = $false
            $lock = $null
            try {
            $lock = [IO.File]::Open($PendingActionPath + '.lock', 'OpenOrCreate', 'ReadWrite', 'None')
            if (-not (Test-Path -LiteralPath $PendingActionPath -PathType Leaf)) {
                throw "未找到待执行升级动作：$PendingActionPath"
            }

            $action = Get-Content -LiteralPath $PendingActionPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($ExpectedActionId -and $action.ActionId -ne $ExpectedActionId) { throw 'Pending action changed.' }
            if ($action.Status -notin @('Pending', 'Scheduled')) { throw 'Action is already running or completed.' }
            $executing = $true
            $sourceRoot = [IO.Path]::GetFullPath($action.SourceDirectory).TrimEnd('\')
            $targetRoot = [IO.Path]::GetFullPath($action.TargetDirectory).TrimEnd('\')
            if ($sourceRoot -eq $targetRoot -or $targetRoot -eq [IO.Path]::GetPathRoot($targetRoot).TrimEnd('\') -or $targetRoot.StartsWith($sourceRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe source or target directory.' }
            if (-not (Test-Path -LiteralPath $action.SourceDirectory -PathType Container)) {
                throw "升级源目录不存在：$($action.SourceDirectory)"
            }
            if (-not (Test-Path -LiteralPath $action.TargetDirectory -PathType Container)) {
                throw "安装目录不存在：$($action.TargetDirectory)"
            }
            $action.Status = 'Applying'
            $action | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PendingActionPath -Encoding UTF8
            $logPath = $action.ExecutionLogPath
            if (-not $logPath) { $logPath = Join-Path (Split-Path -Parent $PendingActionPath) 'offline-update.log' }
            Start-Transcript -LiteralPath $logPath -Append | Out-Null
            $transcribing = $true

            if (-not $SkipServiceControl) {
                $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($service -and $service.Status -ne "Stopped") {
                    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop service")) {
                        $oldProcess = $null
                        $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
                        if ($serviceInfo.ProcessId -gt 0) { try { $oldProcess = [Diagnostics.Process]::GetProcessById([int]$serviceInfo.ProcessId) } catch { } }
                        Stop-Service -Name $ServiceName -Force
                        $service.WaitForStatus("Stopped", "00:00:30")
                        if ($oldProcess) {
                            try { if (-not $oldProcess.WaitForExit(30000)) { throw 'The old host process has not exited.' } }
                            finally { $oldProcess.Dispose() }
                        }
                    }
                }
            }

            $preserveNames = @({{preserveNames}})
            $robocopyArgs = @(
                "`"$sourceRoot`"",
                "`"$targetRoot`"",
                "/MIR",
                "/R:2",
                "/W:1",
                "/XD",
                "`"{{EscapePowerShellPath(ResolveUpdateDirectory())}}`""
            )
            $robocopyArgs += @($preserveNames | ForEach-Object { '"' + $_ + '"' })
            $robocopyArgs += '/XF'
            $robocopyArgs += @($preserveNames | ForEach-Object { '"' + $_ + '"' })
            $copyLogPath = $logPath + '.copy.log'
            $robocopyArgs += "/UNILOG:`"$copyLogPath`""
            $process = Start-Process -FilePath "robocopy.exe" -ArgumentList $robocopyArgs -NoNewWindow -Wait -PassThru
            if ($process.ExitCode -ge 8) {
                throw "文件替换失败，robocopy exit code: $($process.ExitCode)。复制日志：$copyLogPath"
            }

            if (-not $SkipServiceControl) {
                $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($service) {
                    if ($PSCmdlet.ShouldProcess($ServiceName, "Start service")) {
                        Start-Service -Name $ServiceName
                        $service.WaitForStatus('Running', '00:00:30')
                    }
                }
            }
            $action.Status = 'Applied'
            $action | Add-Member -MemberType NoteProperty -Name AppliedTime -Value ((Get-Date).ToUniversalTime().ToString('O')) -Force
            $action | Add-Member -MemberType NoteProperty -Name ErrorMessage -Value '' -Force
            $action | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PendingActionPath -Encoding UTF8
            Write-Host "IPC Gateway 离线$($action.ActionType)已完成。"
            }
            catch {
                if ($executing -and $action) {
                    $action.Status = 'Failed'
                    $action | Add-Member -MemberType NoteProperty -Name ErrorMessage -Value $_.Exception.Message -Force
                    $action | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PendingActionPath -Encoding UTF8
                }
                # Recover availability where possible; retain the failure and transcript for diagnosis.
                if ($executing -and -not $SkipServiceControl) {
                    try { Start-Service -Name $ServiceName -ErrorAction Stop } catch { Write-Warning $_.Exception.Message }
                }
                throw
            }
            finally {
                if ($transcribing) { Stop-Transcript | Out-Null }
                if ($lock) { $lock.Dispose() }
            }
            """;
        File.WriteAllText(scriptPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return scriptPath;
    }

    private GatewayUpdatePackageRecord FindPackage(string packageId)
    {
        string normalized = NormalizeId(packageId);
        GatewayUpdatePackageRecord? record = ReadPackageRecords()
            .FirstOrDefault(item => item.PackageId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (record == null)
            throw new InvalidOperationException("升级包不存在：" + packageId);
        return record;
    }

    private GatewayRollbackPoint FindRollback(string rollbackId)
    {
        string normalized = NormalizeId(rollbackId);
        GatewayRollbackPoint? rollback = ReadRollbackPoints()
            .FirstOrDefault(item => item.RollbackId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (rollback == null)
            throw new InvalidOperationException("回滚点不存在：" + rollbackId);
        return rollback;
    }

    private IList<GatewayUpdatePackageRecord> ReadPackageRecords()
    {
        EnsureDirectories();
        return Directory.EnumerateFiles(GetPackagesDirectory(), "*.json", SearchOption.TopDirectoryOnly)
            .Select(ReadJson<GatewayUpdatePackageRecord>)
            .Where(item => item != null)
            .Cast<GatewayUpdatePackageRecord>()
            .OrderByDescending(item => item.UploadedTime)
            .ToList();
    }

    private IList<GatewayRollbackPoint> ReadRollbackPoints()
    {
        EnsureDirectories();
        return Directory.EnumerateFiles(GetRollbacksDirectory(), "rollback.json", SearchOption.AllDirectories)
            .Select(ReadJson<GatewayRollbackPoint>)
            .Where(item => item != null)
            .Cast<GatewayRollbackPoint>()
            .OrderByDescending(item => item.CreatedTime)
            .ToList();
    }

    private void PruneOldRollbackPoints()
    {
        // Enforce the retention limit even when an older installation keeps a larger configured count.
        foreach (GatewayRollbackPoint point in ReadRollbackPoints().Skip(1))
        {
            GatewayPendingUpdateAction? action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath());
            if (action?.Status == "Pending" && action.ActionType == "Rollback" && action.RollbackId == point.RollbackId)
                File.Delete(GetPendingActionPath());
            DeleteOwnedDirectory(GetRollbacksDirectory(), Path.Combine(GetRollbacksDirectory(), NormalizeId(point.RollbackId)));
        }
    }

    private void ApplyRetentionPolicy()
    {
        GatewayPendingUpdateAction? action = ReadJson<GatewayPendingUpdateAction>(GetPendingActionPath());
        // The external worker may still be reading these files after this host restarts.
        if (action == null && File.Exists(GetPendingActionPath())) return;
        if (action?.Status is "Scheduled" or "Applying") return;
        GatewayUpdatePackageRecord? latest = ReadPackageRecords().FirstOrDefault();
        string? keepId = latest == null ? null : NormalizeId(latest.PackageId);
        if (action?.Status == "Pending" && action.ActionType == "Upgrade" && action.PackageId != latest?.PackageId)
        {
            File.Delete(GetPendingActionPath());
            action = null;
        }
        foreach (string file in Directory.EnumerateFiles(GetPackagesDirectory(), "*", SearchOption.TopDirectoryOnly))
        {
            string extension = Path.GetExtension(file);
            if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (keepId == null || !Path.GetFileNameWithoutExtension(file).Equals(keepId, StringComparison.OrdinalIgnoreCase))
                File.Delete(file);
        }
        PruneOldRollbackPoints();
        string? keepStagingDirectory = null;
        foreach (string file in Directory.EnumerateFiles(GetIncomingDirectory(), "*.zip", SearchOption.TopDirectoryOnly))
            if (!_incomingFiles.Contains(file)) File.Delete(file);
        if (action?.Status == "Pending")
        {
            string source = Path.GetFullPath(action.SourceDirectory);
            string stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetStagingDirectory())) + Path.DirectorySeparatorChar;
            if (source.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                keepStagingDirectory = source[stagingRoot.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        }
        foreach (string directory in Directory.EnumerateDirectories(GetStagingDirectory()))
            if (!Path.GetFileName(directory).Equals(keepStagingDirectory, StringComparison.OrdinalIgnoreCase))
                DeleteOwnedDirectory(GetStagingDirectory(), directory);
    }

    private static void DeleteOwnedDirectory(string root, string directory)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!string.Equals(Path.GetDirectoryName(fullDirectory), fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("升级清理目录超出允许范围。");
        if (Directory.Exists(fullDirectory)) Directory.Delete(fullDirectory, recursive: true);
    }

    private void CopyDirectory(string sourceDirectory, string targetDirectory, ISet<string> excludedRootNames)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(directory);
            if (excludedRootNames.Contains(name))
                continue;
            CopyDirectory(directory, Path.Combine(targetDirectory, name), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (excludedRootNames.Contains(name))
                continue;
            File.Copy(file, Path.Combine(targetDirectory, name), overwrite: true);
        }
    }

    private ISet<string> CreatePreserveSet()
    {
        return (_options.PreservePaths ?? Array.Empty<string>())
            .Select(item => item.Replace('\\', '/').Trim('/'))
            .Where(item => !string.IsNullOrWhiteSpace(item) && !item.Contains('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(ResolveUpdateDirectory());
        Directory.CreateDirectory(GetIncomingDirectory());
        Directory.CreateDirectory(GetPackagesDirectory());
        Directory.CreateDirectory(GetStagingDirectory());
        Directory.CreateDirectory(GetRollbacksDirectory());
    }

    private string ResolveInstallDirectory()
    {
        string path = string.IsNullOrWhiteSpace(_options.InstallDirectory)
            ? AppContext.BaseDirectory
            : _options.InstallDirectory.Trim();
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private string ResolveUpdateDirectory()
    {
        string path = string.IsNullOrWhiteSpace(_options.UpdateDirectory)
            ? "Data/Updates"
            : _options.UpdateDirectory.Trim();
        if (!Path.IsPathRooted(path))
            path = Path.Combine(ResolveInstallDirectory(), path);
        return Path.GetFullPath(path);
    }

    private string GetIncomingDirectory() => Path.Combine(ResolveUpdateDirectory(), "incoming");
    private string GetPackagesDirectory() => Path.Combine(ResolveUpdateDirectory(), "packages");
    private string GetStagingDirectory() => Path.Combine(ResolveUpdateDirectory(), "staging");
    private string GetRollbacksDirectory() => Path.Combine(ResolveUpdateDirectory(), "rollbacks");
    private string GetPendingActionPath() => Path.Combine(ResolveUpdateDirectory(), PendingActionFileName);
    private string GetOfflineScriptPath() => Path.Combine(ResolveUpdateDirectory(), OfflineScriptFileName);

    private static (long SizeBytes, int FileCount) CalculateDirectoryStats(string directory)
    {
        long size = 0;
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            FileInfo info = new FileInfo(file);
            size += info.Length;
            count++;
        }
        return (size, count);
    }

    private T? ReadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
                return default;
            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch
        {
            return default;
        }
    }

    private void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = JsonSerializer.Serialize(value, _jsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ComputeSha256(string path)
    {
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }

    private static bool IsPayloadEntry(ZipArchiveEntry entry, string entryDirectory)
    {
        string name = entry.FullName.Replace('\\', '/');
        return name.StartsWith(entryDirectory.Trim('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEntryDirectory(string value)
    {
        string normalized = (string.IsNullOrWhiteSpace(value) ? "payload" : value)
            .Replace('\\', '/')
            .Trim('/');
        return string.IsNullOrWhiteSpace(normalized) ? "payload" : normalized;
    }

    private static string NormalizePackageType(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "Upgrade" : value.Trim();
        if (normalized.Equals("Install", StringComparison.OrdinalIgnoreCase))
            return "Install";
        if (normalized.Equals("Upgrade", StringComparison.OrdinalIgnoreCase))
            return "Upgrade";
        throw new ArgumentException("升级包类型只能是 Install 或 Upgrade。");
    }

    private static string NormalizeId(string value)
    {
        string id = new string((value ?? string.Empty)
            .Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.' ? ch : '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    private static string GetSafeExtractPath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string target = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!target.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("升级包中存在非法路径：" + relativePath);
        return target;
    }

    private static string EscapePowerShellPath(string path)
    {
        return path.Replace("'", "''");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record GatewayUpdatePackageValidationResult(
        GatewayUpdatePackageManifest Manifest,
        GatewayUpdatePackageTrustResult Trust);

    private sealed record GatewayUpdatePackageTrustResult(
        bool ContentHashValid,
        bool SignatureValid,
        string Status,
        string Message);
}
