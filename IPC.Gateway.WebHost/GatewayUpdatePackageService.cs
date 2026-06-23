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
    private readonly JsonSerializerOptions _jsonOptions;

    public GatewayUpdatePackageService(GatewayUpdateMaintenanceOptions options)
    {
        _options = options ?? new GatewayUpdateMaintenanceOptions();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    public GatewayUpdateStatus GetStatus()
    {
        EnsureDirectories();
        return new GatewayUpdateStatus
        {
            Enabled = _options.Enabled,
            ProductId = _options.ProductId,
            CurrentVersion = GatewayUpdateVersion.Current,
            InstallDirectory = ResolveInstallDirectory(),
            UpdateDirectory = ResolveUpdateDirectory(),
            OfflineScriptPath = GetOfflineScriptPath(),
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
        await using (FileStream output = File.Create(tempPath))
        await using (Stream input = file.OpenReadStream())
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        try
        {
            string sha256 = ComputeSha256(tempPath);
            GatewayUpdatePackageManifest manifest = ReadAndValidateManifest(tempPath);
            string packageId = NormalizeId(string.IsNullOrWhiteSpace(manifest.PackageId)
                ? $"{manifest.PackageType}-{manifest.Version}-{DateTime.UtcNow:yyyyMMddHHmmss}"
                : manifest.PackageId);
            manifest.PackageId = packageId;

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
            WriteJson(storedJson, record);
            return record;
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    public GatewayPrepareUpdateResult PrepareUpgrade(string packageId)
    {
        if (!_options.Enabled)
            throw new InvalidOperationException("升级维护功能未启用。");

        GatewayUpdatePackageRecord record = FindPackage(packageId);
        string actionId = "upgrade-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        string stagingRoot = Path.Combine(GetStagingDirectory(), actionId);
        string sourceDirectory = Path.Combine(stagingRoot, "payload");
        Directory.CreateDirectory(sourceDirectory);

        ExtractPayload(record.StoredPath, record.Manifest.EntryDirectory, sourceDirectory);
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
        manifest.PackageType = NormalizePackageType(manifest.PackageType);
        manifest.Version = (manifest.Version ?? string.Empty).Trim();
        manifest.EntryDirectory = NormalizeEntryDirectory(manifest.EntryDirectory);

        if (!string.Equals(manifest.Product, _options.ProductId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("升级包产品标识不匹配。");
        if (string.IsNullOrWhiteSpace(manifest.Version))
            throw new ArgumentException("升级包清单缺少版本号。");
        if (archive.Entries.All(entry => !IsPayloadEntry(entry, manifest.EntryDirectory)))
            throw new ArgumentException("升级包缺少 payload 目录。");

        return manifest;
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
        string content = $$"""
            [CmdletBinding(SupportsShouldProcess = $true)]
            param(
                [string]$ServiceName = "{{serviceName}}",
                [string]$PendingActionPath = '{{EscapePowerShellPath(pendingPath)}}',
                [switch]$SkipServiceControl
            )

            $ErrorActionPreference = "Stop"

            if (-not (Test-Path -LiteralPath $PendingActionPath -PathType Leaf)) {
                throw "未找到待执行升级动作：$PendingActionPath"
            }

            $action = Get-Content -LiteralPath $PendingActionPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if (-not (Test-Path -LiteralPath $action.SourceDirectory -PathType Container)) {
                throw "升级源目录不存在：$($action.SourceDirectory)"
            }
            if (-not (Test-Path -LiteralPath $action.TargetDirectory -PathType Container)) {
                throw "安装目录不存在：$($action.TargetDirectory)"
            }

            if (-not $SkipServiceControl) {
                $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($service -and $service.Status -ne "Stopped") {
                    if ($PSCmdlet.ShouldProcess($ServiceName, "Stop service")) {
                        Stop-Service -Name $ServiceName -Force
                        $service.WaitForStatus("Stopped", "00:00:30")
                    }
                }
            }

            $robocopyArgs = @(
                "`"$($action.SourceDirectory)`"",
                "`"$($action.TargetDirectory)`"",
                "/MIR",
                "/R:2",
                "/W:1",
                "/XD",
                "`"Data`"",
                "/XF",
                "`"appsettings.json`"",
                "`"appsettings.Production.json`"",
                "`"appsettings.Development.json`""
            )
            $process = Start-Process -FilePath "robocopy.exe" -ArgumentList $robocopyArgs -NoNewWindow -Wait -PassThru
            if ($process.ExitCode -ge 8) {
                throw "文件替换失败，robocopy exit code: $($process.ExitCode)"
            }

            $action.Status = "Applied"
            $action.AppliedTime = (Get-Date).ToUniversalTime().ToString("O")
            $action | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $PendingActionPath -Encoding UTF8

            if (-not $SkipServiceControl) {
                $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
                if ($service) {
                    if ($PSCmdlet.ShouldProcess($ServiceName, "Start service")) {
                        Start-Service -Name $ServiceName
                    }
                }
            }

            Write-Host "IPC Gateway 离线$($action.ActionType)已完成。"
            """;
        File.WriteAllText(scriptPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
        int keep = Math.Max(1, _options.KeepRollbackCount);
        foreach (GatewayRollbackPoint point in ReadRollbackPoints().Skip(keep))
        {
            try
            {
                if (Directory.Exists(point.Directory))
                    Directory.Delete(point.Directory, recursive: true);
            }
            catch
            {
            }
        }
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
        return Path.GetFullPath(path);
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
}
