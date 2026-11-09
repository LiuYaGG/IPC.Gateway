using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.Gateway.Core.Gateway;
using Microsoft.ML.OnnxRuntime;

namespace IPC.Gateway.Inference;

/// <summary>
/// 管理 ONNX 模型文件、不可变版本、发布状态和安全测试。
/// </summary>
public sealed class OnnxModelCatalogService
{
    private readonly object _syncRoot = new();
    private readonly string _rootDirectory;
    private readonly string _catalogPath;
    private readonly long _maxUploadBytes;
    private readonly IModelInferenceService _inference;
    private readonly JsonSerializerOptions _jsonOptions;
    private OnnxModelCatalogDocument _document;
    private readonly OnnxModelRuntimeStats _runtimeStats = new();

    /// <summary>
    /// 创建模型目录服务并加载已有目录。
    /// </summary>
    public OnnxModelCatalogService(OnnxModelCatalogOptions options, IModelInferenceService inference)
    {
        options ??= new OnnxModelCatalogOptions();
        _rootDirectory = ResolveRoot(options.Directory);
        _catalogPath = Path.Combine(_rootDirectory, "catalog.json");
        _maxUploadBytes = Math.Clamp(options.MaxUploadBytes, 1024 * 1024, 1024L * 1024 * 1024);
        _inference = inference;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        Directory.CreateDirectory(_rootDirectory);
        _document = Load();
    }

    /// <summary>
    /// 返回隐藏绝对路径后的模型目录副本。
    /// </summary>
    public IReadOnlyList<OnnxModelDefinition> GetModels()
    {
        lock (_syncRoot)
            return _document.Models.Select(CloneForClient).OrderBy(item => item.Name).ToList();
    }

    /// <summary>
    /// 新建或更新模型基础信息，已存在的版本保持不变。
    /// </summary>
    public OnnxModelDefinition SaveModel(SaveOnnxModelRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string name = RequireText(request.Name, "模型名称");
        lock (_syncRoot)
        {
            OnnxModelDefinition? model = _document.Models.FirstOrDefault(item => Same(item.Id, request.Id));
            if (model == null)
            {
                model = new OnnxModelDefinition
                {
                    Id = "model-" + Guid.NewGuid().ToString("N"),
                    CreatedUtc = DateTimeOffset.UtcNow
                };
                _document.Models.Add(model);
            }
            if (_document.Models.Any(item => !Same(item.Id, model.Id) && Same(item.Name, name)))
                throw new InvalidOperationException($"模型名称“{name}”已经存在。");
            model.Name = name;
            model.Purpose = string.IsNullOrWhiteSpace(request.Purpose) ? "DeviceAnomaly" : request.Purpose.Trim();
            model.Description = request.Description?.Trim() ?? string.Empty;
            model.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveNoLock();
            return CloneForClient(model);
        }
    }

    /// <summary>
    /// 上传并校验一个新的草稿版本，版本文件写入后不再允许覆盖。
    /// </summary>
    public async Task<OnnxModelVersion> UploadVersionAsync(
        string modelId,
        Stream source,
        string fileName,
        string notes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".onnx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只允许上传 .onnx 模型文件。");
        if (!source.CanRead)
            throw new InvalidOperationException("模型上传流不可读。");

        string tempPath = Path.Combine(_rootDirectory, ".upload-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            long length = 0;
            await using (FileStream target = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                        break;
                    length += read;
                    if (length > _maxUploadBytes)
                        throw new InvalidOperationException($"模型文件不能超过 {_maxUploadBytes / 1024 / 1024} MB。");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            if (length == 0)
                throw new InvalidOperationException("模型文件为空。");

            (List<OnnxTensorDescriptor> inputs, List<OnnxTensorDescriptor> outputs) = Inspect(tempPath);
            string sha256 = await ComputeSha256Async(tempPath, cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                OnnxModelDefinition model = FindRequiredNoLock(modelId);
                int versionNumber = model.Versions.Count == 0 ? 1 : model.Versions.Max(item => item.Version) + 1;
                string versionDirectory = Path.Combine(_rootDirectory, SafeSegment(model.Id), "v" + versionNumber);
                Directory.CreateDirectory(versionDirectory);
                string destination = Path.Combine(versionDirectory, "model.onnx");
                if (File.Exists(destination))
                    throw new InvalidOperationException("目标模型版本已经存在，不能覆盖。");
                File.Move(tempPath, destination);
                OnnxModelVersion version = new()
                {
                    Version = versionNumber,
                    FileName = Path.GetFileName(fileName),
                    RelativePath = Path.GetRelativePath(_rootDirectory, destination).Replace('\\', '/'),
                    Sha256 = sha256,
                    FileSize = length,
                    Notes = notes?.Trim() ?? string.Empty,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    Inputs = inputs,
                    Outputs = outputs
                };
                model.Versions.Add(version);
                model.UpdatedUtc = DateTimeOffset.UtcNow;
                SaveNoLock();
                return CloneVersion(version, false);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// 发布一个已校验版本，并将同一模型之前的发布版本标记为历史版本。
    /// </summary>
    public OnnxModelDefinition Publish(string modelId, int versionNumber)
    {
        lock (_syncRoot)
        {
            OnnxModelDefinition model = FindRequiredNoLock(modelId);
            OnnxModelVersion version = model.Versions.FirstOrDefault(item => item.Version == versionNumber)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            string fullPath = ResolveVersionPath(version);
            _ = Inspect(fullPath);
            foreach (OnnxModelVersion item in model.Versions)
                item.Status = item.Version == versionNumber ? "Published" : item.PublishedUtc.HasValue ? "Archived" : "Draft";
            version.PublishedUtc ??= DateTimeOffset.UtcNow;
            model.PublishedVersion = versionNumber;
            model.UpdatedUtc = DateTimeOffset.UtcNow;
            SaveNoLock();
            return CloneForClient(model);
        }
    }

    /// <summary>
    /// 使用指定版本执行测试，并返回完整数值输出和耗时。
    /// </summary>
    public ModelInferenceResult Test(string modelId, OnnxModelTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        OnnxModelVersion version = ResolveVersion(modelId, request.Version, requirePublished: false);
        ModelInferenceResult result = _inference.Predict(new ModelInferenceRequest
        {
            ModelPath = version.FullPath,
            InputName = request.InputName,
            InputNames = request.InputNames,
            OutputName = request.OutputName,
            OutputIndex = request.OutputIndex,
            Features = request.Features,
            TimeoutMilliseconds = request.TimeoutMilliseconds
        });
        lock (_syncRoot)
        {
            _runtimeStats.TotalRuns++;
            _runtimeStats.TotalDurationMilliseconds += Math.Max(0, result.DurationMilliseconds);
            _runtimeStats.LastRunUtc = DateTimeOffset.UtcNow;
            if (result.Success)
            {
                _runtimeStats.SuccessfulRuns++;
                _runtimeStats.LastError = string.Empty;
            }
            else
            {
                _runtimeStats.FailedRuns++;
                _runtimeStats.LastError = result.ErrorMessage;
            }
        }
        return result;
    }

    /// <summary>
    /// 解析供规则或虚拟标签使用的固定发布版本绝对路径。
    /// </summary>
    public OnnxModelVersion ResolvePublishedVersion(string modelId, int versionNumber)
    {
        return ResolveVersion(modelId, versionNumber, requirePublished: true);
    }

    /// <summary>
    /// 返回模型中心累计推理统计副本。
    /// </summary>
    public OnnxModelRuntimeStats GetRuntimeStats()
    {
        lock (_syncRoot)
            return new OnnxModelRuntimeStats
            {
                TotalRuns = _runtimeStats.TotalRuns,
                SuccessfulRuns = _runtimeStats.SuccessfulRuns,
                FailedRuns = _runtimeStats.FailedRuns,
                TotalDurationMilliseconds = _runtimeStats.TotalDurationMilliseconds,
                LastRunUtc = _runtimeStats.LastRunUtc,
                LastError = _runtimeStats.LastError
            };
    }

    /// <summary>
    /// 删除一个未发布且未被外部配置引用的草稿版本。
    /// </summary>
    public void DeleteVersion(string modelId, int versionNumber)
    {
        lock (_syncRoot)
        {
            OnnxModelDefinition model = FindRequiredNoLock(modelId);
            OnnxModelVersion version = model.Versions.FirstOrDefault(item => item.Version == versionNumber)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            if (version.PublishedUtc.HasValue || model.PublishedVersion == versionNumber)
                throw new InvalidOperationException("已经发布过的模型版本不能删除。");
            string directory = Path.GetDirectoryName(ResolveVersionPath(version)) ?? string.Empty;
            model.Versions.Remove(version);
            SaveNoLock();
            if (directory.StartsWith(_rootDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 解析模型版本并附加仅在服务端使用的绝对路径。
    /// </summary>
    private OnnxModelVersion ResolveVersion(string modelId, int versionNumber, bool requirePublished)
    {
        lock (_syncRoot)
        {
            OnnxModelDefinition model = FindRequiredNoLock(modelId);
            int selected = versionNumber > 0 ? versionNumber : model.PublishedVersion;
            OnnxModelVersion version = model.Versions.FirstOrDefault(item => item.Version == selected)
                ?? throw new KeyNotFoundException("未找到指定模型版本。");
            if (requirePublished && !version.PublishedUtc.HasValue)
                throw new InvalidOperationException("模型版本尚未发布。");
            OnnxModelVersion clone = CloneVersion(version, true);
            clone.FullPath = ResolveVersionPath(version);
            return clone;
        }
    }

    /// <summary>
    /// 使用 ONNX Runtime 读取模型张量结构，同时验证文件是否可加载。
    /// </summary>
    private static (List<OnnxTensorDescriptor> Inputs, List<OnnxTensorDescriptor> Outputs) Inspect(string path)
    {
        using InferenceSession session = new(path);
        return (MapTensors(session.InputMetadata), MapTensors(session.OutputMetadata));
    }

    /// <summary>
    /// 将运行时张量元数据转换为可序列化结构。
    /// </summary>
    private static List<OnnxTensorDescriptor> MapTensors(IReadOnlyDictionary<string, NodeMetadata> metadata)
    {
        return metadata.Select(item => new OnnxTensorDescriptor
        {
            Name = item.Key,
            ElementType = item.Value.ElementType?.Name ?? "Unknown",
            Dimensions = item.Value.Dimensions?.ToArray() ?? []
        }).ToList();
    }

    /// <summary>
    /// 从磁盘加载目录，损坏目录会返回空结构而不会影响网关启动。
    /// </summary>
    private OnnxModelCatalogDocument Load()
    {
        try
        {
            if (!File.Exists(_catalogPath))
                return new OnnxModelCatalogDocument();
            OnnxModelCatalogDocument? document = JsonSerializer.Deserialize<OnnxModelCatalogDocument>(File.ReadAllText(_catalogPath), _jsonOptions);
            document ??= new OnnxModelCatalogDocument();
            document.Models ??= [];
            foreach (OnnxModelDefinition model in document.Models)
                model.Versions ??= [];
            return document;
        }
        catch
        {
            return new OnnxModelCatalogDocument();
        }
    }

    /// <summary>
    /// 通过同目录临时文件原子保存模型目录。
    /// </summary>
    private void SaveNoLock()
    {
        Directory.CreateDirectory(_rootDirectory);
        string temp = _catalogPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_document, _jsonOptions));
        File.Move(temp, _catalogPath, true);
    }

    /// <summary>
    /// 获取模型，未找到时返回明确错误。
    /// </summary>
    private OnnxModelDefinition FindRequiredNoLock(string id)
    {
        return _document.Models.FirstOrDefault(item => Same(item.Id, id))
            ?? throw new KeyNotFoundException("未找到指定模型。");
    }

    /// <summary>
    /// 将相对模型路径限制在模型根目录中。
    /// </summary>
    private string ResolveVersionPath(OnnxModelVersion version)
    {
        string path = Path.GetFullPath(Path.Combine(_rootDirectory, version.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        string root = _rootDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("模型版本路径越过了模型根目录。");
        if (!File.Exists(path))
            throw new FileNotFoundException("模型版本文件不存在。", path);
        return path;
    }

    /// <summary>
    /// 创建返回给调用方的深复制，避免目录状态被外部修改。
    /// </summary>
    private static OnnxModelDefinition CloneForClient(OnnxModelDefinition source)
    {
        return new OnnxModelDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Purpose = source.Purpose,
            Description = source.Description,
            PublishedVersion = source.PublishedVersion,
            CreatedUtc = source.CreatedUtc,
            UpdatedUtc = source.UpdatedUtc,
            Versions = source.Versions.Select(item => CloneVersion(item, false)).OrderByDescending(item => item.Version).ToList()
        };
    }

    /// <summary>
    /// 创建模型版本深复制。
    /// </summary>
    private static OnnxModelVersion CloneVersion(OnnxModelVersion source, bool includePath)
    {
        return new OnnxModelVersion
        {
            Version = source.Version,
            Status = source.Status,
            FileName = source.FileName,
            FullPath = includePath ? source.FullPath : string.Empty,
            RelativePath = source.RelativePath,
            Sha256 = source.Sha256,
            FileSize = source.FileSize,
            Notes = source.Notes,
            CreatedUtc = source.CreatedUtc,
            PublishedUtc = source.PublishedUtc,
            Inputs = source.Inputs.Select(CloneTensor).ToList(),
            Outputs = source.Outputs.Select(CloneTensor).ToList()
        };
    }

    /// <summary>
    /// 创建张量描述深复制。
    /// </summary>
    private static OnnxTensorDescriptor CloneTensor(OnnxTensorDescriptor source) => new()
    {
        Name = source.Name,
        ElementType = source.ElementType,
        Dimensions = source.Dimensions?.ToArray() ?? []
    };

    /// <summary>
    /// 计算模型文件内容摘要。
    /// </summary>
    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 解析相对目录为应用目录下的绝对目录。
    /// </summary>
    private static string ResolveRoot(string configured)
    {
        string value = string.IsNullOrWhiteSpace(configured) ? "Data\\Models" : configured.Trim();
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(AppContext.BaseDirectory, value));
    }

    /// <summary>
    /// 将模型编号转换成安全目录段。
    /// </summary>
    private static string SafeSegment(string value)
    {
        string safe = new(value.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
        return string.IsNullOrWhiteSpace(safe) ? throw new InvalidOperationException("模型编号不合法。") : safe;
    }

    /// <summary>
    /// 要求必填文本并去除首尾空格。
    /// </summary>
    private static string RequireText(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(name + "不能为空。") : value.Trim();

    /// <summary>
    /// 不区分大小写比较模型标识或名称。
    /// </summary>
    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
