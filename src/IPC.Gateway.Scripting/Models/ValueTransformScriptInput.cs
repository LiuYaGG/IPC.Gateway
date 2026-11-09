using System.Globalization;

namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 向值处理脚本公开当前值、标签元数据和节点参数。
/// </summary>
public sealed class ValueTransformScriptInput
{
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
    public IReadOnlyDictionary<string, object?> Parameters { get; set; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 将输入值转换为双精度数值。
    /// </summary>
    public double AsDouble(double fallback = 0D)
    {
        return TryConvert(value => Convert.ToDouble(value, CultureInfo.InvariantCulture), fallback);
    }

    /// <summary>
    /// 将输入值转换为三十二位整数。
    /// </summary>
    public int AsInt32(int fallback = 0)
    {
        return TryConvert(value => Convert.ToInt32(value, CultureInfo.InvariantCulture), fallback);
    }

    /// <summary>
    /// 将输入值转换为六十四位整数。
    /// </summary>
    public long AsInt64(long fallback = 0L)
    {
        return TryConvert(value => Convert.ToInt64(value, CultureInfo.InvariantCulture), fallback);
    }

    /// <summary>
    /// 将输入值转换为布尔值。
    /// </summary>
    public bool AsBoolean(bool fallback = false)
    {
        if (Value is bool boolean)
            return boolean;
        if (bool.TryParse(ValueText, out bool parsedBoolean))
            return parsedBoolean;
        if (double.TryParse(ValueText, NumberStyles.Any, CultureInfo.InvariantCulture, out double parsedNumber))
            return Math.Abs(parsedNumber) > double.Epsilon;
        return TryConvert(value => Convert.ToBoolean(value, CultureInfo.InvariantCulture), fallback);
    }

    /// <summary>
    /// 将输入值转换为字符串。
    /// </summary>
    public string AsString(string fallback = "")
    {
        return Convert.ToString(Value, CultureInfo.InvariantCulture) ??
               (string.IsNullOrEmpty(ValueText) ? fallback : ValueText);
    }

    /// <summary>
    /// 执行安全类型转换并在失败时返回指定默认值。
    /// </summary>
    private T TryConvert<T>(Func<object, T> converter, T fallback)
    {
        try
        {
            object candidate = Value ?? ValueText;
            return converter(candidate);
        }
        catch
        {
            return fallback;
        }
    }
}

/// <summary>
/// 表示脚本主动返回的成功或失败结果。
/// </summary>
public sealed class ValueTransformScriptResult
{
    public bool Success { get; init; }
    public object? Value { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// 创建带转换值的成功结果。
    /// </summary>
    public static ValueTransformScriptResult Ok(object? value)
    {
        return new ValueTransformScriptResult { Success = true, Value = value };
    }

    /// <summary>
    /// 创建带错误说明的失败结果。
    /// </summary>
    public static ValueTransformScriptResult Failure(string message)
    {
        return new ValueTransformScriptResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "值处理脚本返回失败。" : message.Trim()
        };
    }
}

/// <summary>
/// 表示前端测试草稿值处理脚本的输入。
/// </summary>
public sealed class ValueTransformScriptTestRequest
{
    public string SourceCode { get; set; } = string.Empty;
    public string InputDataType { get; set; } = "Double";
    public string OutputDataType { get; set; } = "Double";
    public string ValueText { get; set; } = string.Empty;
    public int TimeoutMilliseconds { get; set; } = 100;
}
