using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using Microsoft.ML.OnnxRuntime;

namespace IPC.Gateway.Inference;

/// <summary>Stores model definitions and immutable ONNX content in the shared business database.</summary>
public sealed class OnnxModelCatalogService
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly string _rootDirectory;
    private readonly string _catalogPath;
    private readonly long _maxUploadBytes;
    private readonly IModelInferenceService _inference;
    private readonly GatewaySharedArtifactRepository? _shared;
    private readonly IGatewayDatabaseOwner? _owner;
    private readonly SharedModelPathResolver? _modelPaths;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, (long Length, long Modified)> _verifiedCache = new(StringComparer.Ordinal);
    private OnnxModelCatalogDocument _document;
    private readonly OnnxModelRuntimeStats _runtimeStats = new();
    private long _revision;
    private volatile bool _sharedReady;
    public long SharedRevision => Interlocked.Read(ref _revision);

    public OnnxModelCatalogService(OnnxModelCatalogOptions options, IModelInferenceService inference,
        GatewaySharedArtifactRepository? shared = null, IGatewayDatabaseOwner? owner = null, SharedModelPathResolver? modelPaths = null)
    {
        options ??= new OnnxModelCatalogOptions();
        _rootDirectory = ResolveRoot(options.Directory);
        _catalogPath = Path.Combine(_rootDirectory, "catalog.json");
        _maxUploadBytes = Math.Clamp(options.MaxUploadBytes, 1024 * 1024, 1024L * 1024 * 1024);
        _inference = inference;
        _shared = shared;
        _owner = owner;
        _modelPaths = modelPaths;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        Directory.CreateDirectory(_rootDirectory);
        _document = shared == null ? LoadLocal() : new();
        _sharedReady = shared == null;
    }

    // Called by the independent artifact synchronizer and the standby prewarm path.
    // Inference readers retain the previous complete catalog while files are downloading.
    public void SynchronizeShared()
    {
        _mutationGate.Wait();
        try { RefreshSharedNoLock(); }
        finally { _mutationGate.Release(); }
    }

    public void ReloadFromDisk()
    {
        if (_shared != null) SynchronizeShared();
        else lock (_syncRoot) _document = LoadLocal();
    }

    public void SynchronizeRuleModels(IEnumerable<string> modelPaths)
    {
        if (_shared == null) return;
        _mutationGate.Wait();
        try
        {
            RefreshSharedNoLock();
            if (_owner?.IsCluster == true && !_owner.CanRunBusiness) return;
            var document = Snapshot();
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string reference in modelPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal))
            {
                if (document.ExternalFiles.Any(item => SharedModelPathResolver.Normalize(item.ModelPath) == SharedModelPathResolver.Normalize(reference)))
                    continue;
                // A missing optional rule model remains a rule-specific runtime fault.
                var external = ReadExternalFile(reference);
                if (external == null) continue;
                document.ExternalFiles.Add(new() { ModelPath = reference, Sha256 = external.Value.Hash, FileSize = external.Value.Size });
                files[external.Value.Hash] = external.Value.Path;
            }
            if (files.Count == 0) return;
            EnsureLocalFiles(document, files);
            SaveDocumentNoLock(document, files);
        }
        finally { _mutationGate.Release(); }
    }

    private (string Path, string Hash, long Size)? ReadExternalFile(string reference)
    {
        try
        {
            string? source = _modelPaths?.Resolve(reference);
            if (source == null)
            {
                string local = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                source = Path.GetFullPath(local, AppContext.BaseDirectory);
                if (!File.Exists(source)) source = Path.GetFullPath(local);
            }
            if (!File.Exists(source)) return null;
            long size = new FileInfo(source).Length;
            return size is > 0 and <= 1024L * 1024 * 1024 ? (source, ComputeSha256(source), size) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<OnnxModelDefinition> GetModels()
    {
        if (!_sharedReady) SynchronizeShared();
        lock (_syncRoot)
            return _document.Models.Select(CloneForClient).OrderBy(item => item.Name).ToList();
    }

    public OnnxModelDefinition SaveModel(SaveOnnxModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = RequireText(request.Name, "模型名称");
        _mutationGate.Wait();
        try
        {
            RefreshSharedNoLock();
            var document = Snapshot();
            var model = document.Models.FirstOrDefault(item => Same(item.Id, request.Id));
            if (model == null)
            {
                model = new OnnxModelDefinition { Id = "model-" + Guid.NewGuid().ToString("N"), CreatedUtc = DateTimeOffset.UtcNow };
                document.Models.Add(model);
            }
            if (document.Models.Any(item => !Same(item.Id, model.Id) && Same(item.Name, name)))
                throw new InvalidOperationException($"模型名称“{name}”已经存在。");
            model.Name = name;
            model.Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "DeviceAnomaly" : request.Purpose.Trim();
            model.Description = request.Description?.Trim() ?? string.Empty;
            model.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveDocumentNoLock(document);
            return CloneForClient(model);
        }
        finally { _mutationGate.Release(); }
    }

    public async Task<OnnxModelVersion> UploadVersionAsync(string modelId, Stream source, string fileName, string notes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".onnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只允许上传 .onnx 模型文件。");
        if (!source.CanRead) throw new InvalidOperationException("模型上传流不可读。");
        string tempPath = Path.Combine(_rootDirectory, ".upload-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            long length = 0;
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    length += read;
                    if (length > _maxUploadBytes)
                        throw new InvalidOperationException($"模型文件不能超过 {_maxUploadBytes / 1024 / 1024} MB。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (length == 0) throw new InvalidOperationException("模型文件为空。");
            var (inputs, outputs) = Inspect(tempPath);
            string hash = await ComputeSha256Async(tempPath, cancellationToken).ConfigureAwait(false);
            await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                RefreshSharedNoLock();
                var document = Snapshot();
                var model = FindRequired(document, modelId);
                int number = model.Versions.Count == 0 ? 1 : model.Versions.Max(item => item.Version) + 1;
                string relative = _shared == null ? $"{SafeSegment(model.Id)}/v{number}/model.onnx" : SharedRelativePath(hash);
                string destination = ResolveRelativePath(relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination)) File.Move(tempPath, destination);
                else if (_shared == null || ComputeSha256(destination) != hash)
                    throw new InvalidOperationException("目标模型版本已经存在，不能覆盖。");
                var version = new OnnxModelVersion
                {
                    Version = number, FileName = Path.GetFileName(fileName), RelativePath = relative,
                    Sha256 = hash, FileSize = length, Notes = notes?.Trim() ?? "", CreatedUtc = DateTimeOffset.UtcNow,
                    Inputs = inputs, Outputs = outputs
                };
                model.Versions.Add(version);
                model.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveDocumentNoLock(document, new Dictionary<string, string> { [hash] = destination });
                return CloneVersion(version, false);
            }
            finally { _mutationGate.Release(); }
        }
        finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
    }

    public OnnxModelDefinition Publish(string modelId, int versionNumber)
    {
        _mutationGate.Wait();
        try
        {
            RefreshSharedNoLock();
            var document = Snapshot();
            var model = FindRequired(document, modelId);
            var version = model.Versions.FirstOrDefault(item => item.Version == versionNumber)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            _ = Inspect(ResolveVersionPath(version));
            foreach (var item in model.Versions)
                item.Status = item.Version == versionNumber ? "Published" : item.PublishedUtc.HasValue ? "Archived" : "Draft";
            version.PublishedUtc ??= DateTimeOffset.UtcNow;
            model.PublishedVersion = versionNumber;
            model.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveDocumentNoLock(document);
            return CloneForClient(model);
        }
        finally { _mutationGate.Release(); }
    }

    public ModelInferenceResult Test(string modelId, OnnxModelTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = ResolveVersion(modelId, request.Version, requirePublished: false);
        var result = _inference.Predict(new ModelInferenceRequest
        {
            ModelPath = version.FullPath, InputName = request.InputName, InputNames = request.InputNames,
            OutputName = request.OutputName, OutputIndex = request.OutputIndex, Features = request.Features,
            TimeoutMilliseconds = request.TimeoutMilliseconds
        });
        lock (_syncRoot)
        {
            _runtimeStats.TotalRuns++;
            _runtimeStats.TotalDurationMilliseconds += Math.Max(0, result.DurationMilliseconds);
            _runtimeStats.LastRunUtc = DateTimeOffset.UtcNow;
            if (result.Success) { _runtimeStats.SuccessfulRuns++; _runtimeStats.LastError = ""; }
            else { _runtimeStats.FailedRuns++; _runtimeStats.LastError = result.ErrorMessage; }
        }
        return result;
    }

    public OnnxModelVersion ResolvePublishedVersion(string modelId, int versionNumber) =>
        ResolveVersion(modelId, versionNumber, requirePublished: true);

    public OnnxModelRuntimeStats GetRuntimeStats()
    {
        lock (_syncRoot) return new()
        {
            TotalRuns = _runtimeStats.TotalRuns, SuccessfulRuns = _runtimeStats.SuccessfulRuns,
            FailedRuns = _runtimeStats.FailedRuns, TotalDurationMilliseconds = _runtimeStats.TotalDurationMilliseconds,
            LastRunUtc = _runtimeStats.LastRunUtc, LastError = _runtimeStats.LastError
        };
    }

    public void DeleteVersion(string modelId, int versionNumber)
    {
        _mutationGate.Wait();
        try
        {
            RefreshSharedNoLock();
            var document = Snapshot();
            var model = FindRequired(document, modelId);
            var version = model.Versions.FirstOrDefault(item => item.Version == versionNumber)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            if (version.PublishedUtc.HasValue || model.PublishedVersion == versionNumber)
                throw new InvalidOperationException("已经发布过的模型版本不能删除。");
            model.Versions.Remove(version);
            SaveDocumentNoLock(document);
            // Shared content is immutable and may be referenced by another version or node.
        }
        finally { _mutationGate.Release(); }
    }

    private void RefreshSharedNoLock()
    {
        if (_shared == null) return;
        long revision = _shared.ProbeRevision("models");
        if (_sharedReady && revision == _revision)
        {
            var cached = Snapshot();
            if (!HasLocalContent(cached)) EnsureLocalFiles(cached, null);
            return;
        }
        var stored = _shared.Read("models");
        if (stored == null)
        {
            if (_owner?.IsCluster == true && !_owner.CanRunBusiness)
                throw new InvalidOperationException("HA_SHARED_MODEL_MIGRATION_REQUIRED");
            var local = LoadLocal();
            if (File.Exists(_catalogPath))
            {
                string legacyBackup = Path.Combine(_rootDirectory, "catalog.before-shared.json");
                if (!File.Exists(legacyBackup)) File.Copy(_catalogPath, legacyBackup);
            }
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var version in local.Models.SelectMany(model => model.Versions))
            {
                string path = ResolveVersionPath(version);
                string hash = ComputeSha256(path);
                if (!string.IsNullOrEmpty(version.Sha256) && !hash.Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("本地模型文件与版本摘要不一致，请恢复原始模型文件。");
                version.Sha256 = hash;
                version.FileSize = new FileInfo(path).Length;
                if (version.FileSize is <= 0 or > 1024L * 1024 * 1024) throw new InvalidDataException("本地模型文件大小无效。");
                files[hash] = path;
                version.RelativePath = SharedRelativePath(hash);
            }
            EnsureLocalFiles(local, files);
            SaveDocumentNoLock(local, files);
            return;
        }
        var incoming = Decode(stored.Payload);
        EnsureLocalFiles(incoming, null);
        lock (_syncRoot)
        {
            _document = incoming;
            _revision = stored.Revision;
            _sharedReady = true;
        }
        UpdateAliases(incoming);
        WriteSharedCache(incoming);
    }

    private bool HasLocalContent(OnnxModelCatalogDocument document)
    {
        var content = document.Models.SelectMany(model => model.Versions).Select(item => (item.Sha256, item.FileSize))
            .Concat(document.ExternalFiles.Select(item => (item.Sha256, item.FileSize)));
        foreach (var item in content)
        {
            string path = ResolveRelativePath(SharedRelativePath(item.Sha256));
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != item.FileSize || !_verifiedCache.TryGetValue(path, out var stamp)
                || stamp != (file.Length, file.LastWriteTimeUtc.Ticks)) return false;
        }
        return true;
    }

    private void EnsureLocalFiles(OnnxModelCatalogDocument document, IReadOnlyDictionary<string, string>? importedFiles)
    {
        var versions = document.Models.SelectMany(model => model.Versions).ToList();
        var content = versions.Select(v => (v.Sha256, v.FileSize))
            .Concat(document.ExternalFiles.Select(v => (v.Sha256, v.FileSize))).Distinct().ToArray();
        foreach (var version in versions) version.RelativePath = SharedRelativePath(version.Sha256);
        foreach (var item in content)
        {
            string hash = item.Sha256.ToLowerInvariant();
            if (hash.Length != 64 || !hash.All(Uri.IsHexDigit) || item.FileSize is <= 0 or > 1024L * 1024 * 1024)
                throw new InvalidDataException("共享模型版本的摘要或大小无效。");
            string relative = SharedRelativePath(hash);
            string path = ResolveRelativePath(relative);
            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                if (file.Length == item.FileSize && ComputeSha256(path) == hash)
                {
                    _verifiedCache[path] = (file.Length, file.LastWriteTimeUtc.Ticks);
                    continue;
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                if (importedFiles != null && importedFiles.TryGetValue(hash, out var source)) File.Copy(source, pending);
                else _shared!.CopyBlobTo(hash, pending);
                if (!File.Exists(pending) || new FileInfo(pending).Length != item.FileSize || ComputeSha256(pending) != hash)
                    throw new InvalidDataException("模型文件下载或摘要校验失败。");
                File.Move(pending, path, true);
                var file = new FileInfo(path);
                _verifiedCache[path] = (file.Length, file.LastWriteTimeUtc.Ticks);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }

    private void SaveDocumentNoLock(OnnxModelCatalogDocument document, IReadOnlyDictionary<string, string>? files = null)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(document, _jsonOptions);
        long revision = _shared == null ? _revision : files?.Count > 0
            ? _shared.SaveStream("models", _revision, payload, files)
            : _shared.Save("models", _revision, payload);
        if (_shared == null) WriteCache(document);
        lock (_syncRoot) { _document = document; _revision = revision; _sharedReady = true; }
        UpdateAliases(document);
        if (_shared != null) WriteSharedCache(document);
    }

    private void UpdateAliases(OnnxModelCatalogDocument document)
    {
        if (_modelPaths == null) return;
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in document.ExternalFiles)
            aliases[SharedModelPathResolver.Normalize(item.ModelPath)] = ResolveRelativePath(SharedRelativePath(item.Sha256));
        foreach (var model in document.Models)
            foreach (var version in model.Versions)
                aliases[SharedModelPathResolver.Normalize($"{model.Id}/v{version.Version}/model.onnx")] = ResolveRelativePath(version.RelativePath);
        _modelPaths.Replace(aliases);
    }

    private void WriteSharedCache(OnnxModelCatalogDocument document)
    {
        try { WriteCache(document); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_syncRoot) _runtimeStats.LastError = "共享模型已保存，本机目录缓存写入失败。";
        }
    }

    private void WriteCache(OnnxModelCatalogDocument document)
    {
        string pending = _catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(pending, JsonSerializer.Serialize(document, _jsonOptions));
            File.Move(pending, _catalogPath, true);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private OnnxModelCatalogDocument LoadLocal() => File.Exists(_catalogPath)
        ? Decode(File.ReadAllBytes(_catalogPath)) : new();

    private OnnxModelCatalogDocument Decode(byte[] bytes)
    {
        var document = JsonSerializer.Deserialize<OnnxModelCatalogDocument>(bytes, _jsonOptions)
            ?? throw new InvalidDataException("模型目录格式无效。");
        document.Models ??= [];
        document.ExternalFiles ??= [];
        foreach (var model in document.Models) model.Versions ??= [];
        return document;
    }

    private OnnxModelCatalogDocument Snapshot()
    {
        lock (_syncRoot) return new()
        {
            SchemaVersion = _document.SchemaVersion,
            Models = _document.Models.Select(CloneForClient).ToList(),
            ExternalFiles = _document.ExternalFiles.Select(item => new OnnxExternalModelFile
            {
                ModelPath = item.ModelPath, Sha256 = item.Sha256, FileSize = item.FileSize
            }).ToList()
        };
    }

    private OnnxModelVersion ResolveVersion(string modelId, int versionNumber, bool requirePublished)
    {
        lock (_syncRoot)
        {
            if (!_sharedReady) throw new InvalidOperationException("共享模型尚未加载，请等待模型同步完成。");
            var model = FindRequired(_document, modelId);
            int selected = versionNumber > 0 ? versionNumber : model.PublishedVersion;
            var version = model.Versions.FirstOrDefault(item => item.Version == selected)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            if (requirePublished && !version.PublishedUtc.HasValue) throw new InvalidOperationException("模型版本尚未发布。");
            var clone = CloneVersion(version, true);
            clone.FullPath = ResolveVersionPath(version);
            return clone;
        }
    }

    private static OnnxModelDefinition FindRequired(OnnxModelCatalogDocument document, string id) =>
        document.Models.FirstOrDefault(item => Same(item.Id, id)) ?? throw new KeyNotFoundException("未找到指定模型。");

    private string ResolveVersionPath(OnnxModelVersion version)
    {
        string path = ResolveRelativePath(version.RelativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("模型版本文件不存在。", path);
        return path;
    }

    private string ResolveRelativePath(string relative)
    {
        string path = Path.GetFullPath(Path.Combine(_rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        string root = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("模型版本路径越过了模型根目录。");
        return path;
    }

    private static string SharedRelativePath(string hash) => ".shared/" + hash.ToLowerInvariant() + ".onnx";
    private static string ComputeSha256(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static (List<OnnxTensorDescriptor> Inputs, List<OnnxTensorDescriptor> Outputs) Inspect(string path)
    {
        using InferenceSession session = new(path);
        return (MapTensors(session.InputMetadata), MapTensors(session.OutputMetadata));
    }

    private static List<OnnxTensorDescriptor> MapTensors(IReadOnlyDictionary<string, NodeMetadata> metadata) =>
        metadata.Select(item => new OnnxTensorDescriptor
        {
            Name = item.Key, ElementType = item.Value.ElementType?.Name ?? "Unknown", Dimensions = item.Value.Dimensions?.ToArray() ?? []
        }).ToList();

    private static OnnxModelDefinition CloneForClient(OnnxModelDefinition source) => new()
    {
        Id = source.Id, Name = source.Name, Purpose = source.Purpose, Description = source.Description,
        PublishedVersion = source.PublishedVersion, CreatedUtc = source.CreatedUtc, UpdatedUtc = source.UpdatedUtc,
        Versions = source.Versions.Select(item => CloneVersion(item, false)).OrderByDescending(item => item.Version).ToList()
    };

    private static OnnxModelVersion CloneVersion(OnnxModelVersion source, bool includePath) => new()
    {
        Version = source.Version, Status = source.Status, FileName = source.FileName, FullPath = includePath ? source.FullPath : "",
        RelativePath = source.RelativePath, Sha256 = source.Sha256, FileSize = source.FileSize, Notes = source.Notes,
        CreatedUtc = source.CreatedUtc, PublishedUtc = source.PublishedUtc,
        Inputs = source.Inputs.Select(CloneTensor).ToList(), Outputs = source.Outputs.Select(CloneTensor).ToList()
    };

    private static OnnxTensorDescriptor CloneTensor(OnnxTensorDescriptor source) => new()
    {
        Name = source.Name, ElementType = source.ElementType, Dimensions = source.Dimensions?.ToArray() ?? []
    };

    private static string ResolveRoot(string configured) => Path.GetFullPath(
        (string.IsNullOrWhiteSpace(configured) ? "Data/Models" : configured.Trim()).Replace('\\', Path.DirectorySeparatorChar), AppContext.BaseDirectory);

    private static string SafeSegment(string value)
    {
        string safe = new(value.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? throw new InvalidOperationException("模型编号不合法。") : safe;
    }

    private static string RequireText(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException(name + "不能为空。") : value.Trim();

    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
