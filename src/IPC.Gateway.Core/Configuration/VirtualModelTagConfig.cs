namespace IPC.Runtime.Configuration;

/// <summary>
/// 定义一个由固定 ONNX 模型版本计算得到的虚拟标签。
/// </summary>
public sealed class VirtualModelTagConfig
{
    public string ModelId { get; set; } = string.Empty;
    public int ModelVersion { get; set; }
    public string InputName { get; set; } = string.Empty;
    public string InputNames { get; set; } = string.Empty;
    public string OutputName { get; set; } = string.Empty;
    public int OutputIndex { get; set; }
    public string TriggerMode { get; set; } = "OnInputChanged";
    public int IntervalMilliseconds { get; set; } = 1000;
    public int DebounceMilliseconds { get; set; } = 100;
    public int MaxInputAgeMilliseconds { get; set; } = 10000;
    public int TimeoutMilliseconds { get; set; } = 1000;
    public string FailurePolicy { get; set; } = "KeepLastGood";
    public string FallbackValue { get; set; } = string.Empty;
    public double BoolThreshold { get; set; } = 0.5D;
    public List<VirtualModelInputBindingConfig> Inputs { get; set; } = [];
}

/// <summary>
/// 将一个模型特征绑定到四段式标签路径。
/// </summary>
public sealed class VirtualModelInputBindingConfig
{
    public string FeatureName { get; set; } = string.Empty;
    public string TagPath { get; set; } = string.Empty;
    public double Multiplier { get; set; } = 1D;
    public double Offset { get; set; }
}
