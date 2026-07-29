namespace IPC.Gateway.Core.Gateway;

/// <summary>
/// 定义采集清洗和规则引擎调用值处理脚本的低耦合边界。
/// </summary>
public interface IValueTransformScriptRuntime
{
    /// <summary>
    /// 使用指定的已发布脚本处理一个输入值。
    /// </summary>
    ValueTransformExecutionResult Execute(ValueTransformExecutionRequest request);
}

/// <summary>
/// 表示一次值处理脚本调用所需的只读输入。
/// </summary>
public sealed class ValueTransformExecutionRequest
{
    public string ScriptId { get; set; } = string.Empty;
    public int ScriptVersion { get; set; }
    public object? Value { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string PointCode { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string ExpectedOutputDataType { get; set; } = string.Empty;
    public string Usage { get; set; } = string.Empty;
    public int TimeoutMilliseconds { get; set; } = 100;
    public IReadOnlyDictionary<string, object?> Parameters { get; set; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 表示值处理脚本的成功结果或受控失败信息。
/// </summary>
public sealed class ValueTransformExecutionResult
{
    public bool Success { get; set; }
    public object? Value { get; set; }
    public string ValueText { get; set; } = string.Empty;
    public string OutputDataType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public long DurationMilliseconds { get; set; }
}

/// <summary>
/// 在未安装脚本模块时返回明确失败结果。
/// </summary>
public sealed class NoopValueTransformScriptRuntime : IValueTransformScriptRuntime
{
    public static NoopValueTransformScriptRuntime Instance { get; } = new();

    /// <summary>
    /// 返回脚本运行时不可用的失败结果。
    /// </summary>
    public ValueTransformExecutionResult Execute(ValueTransformExecutionRequest request)
    {
        return new ValueTransformExecutionResult
        {
            Success = false,
            ErrorMessage = "值处理脚本运行时未启用。"
        };
    }
}
