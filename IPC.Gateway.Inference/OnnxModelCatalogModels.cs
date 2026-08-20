using System.Text.Json.Serialization;

namespace IPC.Gateway.Inference;

/// <summary>
/// 定义模型中心的文件存储和上传限制。
/// </summary>
public sealed class OnnxModelCatalogOptions
{
    public string Directory { get; set; } = "Data\\Models";
    public long MaxUploadBytes { get; set; } = 100 * 1024 * 1024;
}

/// <summary>
/// 表示模型中心持久化文档。
/// </summary>
public sealed class OnnxModelCatalogDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<OnnxModelDefinition> Models { get; set; } = [];
}

/// <summary>
/// 表示一个可拥有多个不可变版本的 ONNX 模型。
/// </summary>
public sealed class OnnxModelDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = "DeviceAnomaly";
    public string Description { get; set; } = string.Empty;
    public int PublishedVersion { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public List<OnnxModelVersion> Versions { get; set; } = [];
}

/// <summary>
/// 表示一个已校验并以固定路径保存的模型版本。
/// </summary>
public sealed class OnnxModelVersion
{
    public int Version { get; set; }
    public string Status { get; set; } = "Draft";
    public string FileName { get; set; } = "model.onnx";
    [JsonIgnore]
    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? PublishedUtc { get; set; }
    public List<OnnxTensorDescriptor> Inputs { get; set; } = [];
    public List<OnnxTensorDescriptor> Outputs { get; set; } = [];
}

/// <summary>
/// 描述 ONNX 输入或输出张量的名称、元素类型和维度。
/// </summary>
public sealed class OnnxTensorDescriptor
{
    public string Name { get; set; } = string.Empty;
    public string ElementType { get; set; } = string.Empty;
    public int[] Dimensions { get; set; } = [];
}

/// <summary>
/// 表示模型基础信息的新建或更新请求。
/// </summary>
public sealed class SaveOnnxModelRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = "DeviceAnomaly";
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 表示一次模型测试输入。
/// </summary>
public sealed class OnnxModelTestRequest
{
    public int Version { get; set; }
    public string InputName { get; set; } = string.Empty;
    public string InputNames { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;
    public int OutputIndex { get; set; }
    public List<float> Features { get; set; } = [];
    public int TimeoutMilliseconds { get; set; } = 1000;
}

/// <summary>
/// 表示模型目录当前的推理统计。
/// </summary>
public sealed class OnnxModelRuntimeStats
{
    public long TotalRuns { get; set; }
    public long SuccessfulRuns { get; set; }
    public long FailedRuns { get; set; }
    public long TotalDurationMilliseconds { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
}
