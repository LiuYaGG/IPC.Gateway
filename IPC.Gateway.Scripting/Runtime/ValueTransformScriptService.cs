using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 缓存已发布值处理脚本，并为采集清洗和规则引擎提供同步执行入口。
/// </summary>
public sealed class ValueTransformScriptService : IValueTransformScriptRuntime
{
    private readonly IScriptConfigurationStore _configurationStore;
    private readonly GatewayScriptCompiler _compiler;
    private readonly ConcurrentDictionary<string, GatewayScriptDefinition> _publishedScripts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 创建值处理脚本服务。
    /// </summary>
    public ValueTransformScriptService(
        IScriptConfigurationStore configurationStore,
        GatewayScriptCompiler compiler)
    {
        _configurationStore = configurationStore;
        _compiler = compiler;
        ReloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 从独立脚本配置重新加载已发布的值处理脚本。
    /// </summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        ScriptConfigurationDocument document =
            await _configurationStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        Reload(document);
    }

    /// <summary>
    /// 使用已经读取的配置文档刷新已发布脚本缓存。
    /// </summary>
    public void Reload(ScriptConfigurationDocument document)
    {
        _publishedScripts.Clear();
        foreach (GatewayScriptDefinition script in document?.Scripts ?? [])
        {
            if (script.ScriptType != GatewayScriptType.ValueTransform ||
                script.PublishedVersion <= 0 ||
                string.IsNullOrWhiteSpace(script.PublishedSourceCode))
            {
                continue;
            }

            GatewayScriptDefinition latest = script.Clone();
            if (!_compiler.PrepareValueTransform(latest.PublishedSourceCode).Success)
                continue;
            _publishedScripts[BuildCacheKey(script.Id, script.PublishedVersion)] = latest;
            _publishedScripts[BuildCacheKey(script.Id, 0)] = latest;
            foreach (ValueTransformPublishedVersion version in script.PublishedVersions ?? [])
            {
                if (version.Version <= 0 || string.IsNullOrWhiteSpace(version.SourceCode))
                    continue;
                GatewayScriptDefinition historical = script.Clone();
                historical.PublishedVersion = version.Version;
                historical.PublishedSourceCode = version.SourceCode;
                historical.PublishedUtc = version.PublishedUtc;
                if (!_compiler.PrepareValueTransform(historical.PublishedSourceCode).Success)
                    continue;
                _publishedScripts[BuildCacheKey(script.Id, version.Version)] = historical;
            }
        }
    }

    /// <summary>
    /// 同步执行已发布脚本，并对版本、超时和输出类型进行校验。
    /// </summary>
    public ValueTransformExecutionResult Execute(ValueTransformExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Stopwatch stopwatch = Stopwatch.StartNew();
        if (!_publishedScripts.TryGetValue(BuildCacheKey(request.ScriptId, request.ScriptVersion), out GatewayScriptDefinition? script))
            return Failed("未找到已发布的值处理脚本。", stopwatch);
        if (!IsUsageAllowed(script.ValueTransformScope, request.Usage))
            return Failed("该值处理脚本未授权用于当前执行位置。", stopwatch);

        int timeoutMilliseconds = Math.Clamp(
            request.TimeoutMilliseconds > 0 ? request.TimeoutMilliseconds : script.TransformTimeoutMilliseconds,
            10,
            5000);
        using CancellationTokenSource timeout = new(timeoutMilliseconds);
        ScriptLogCollector logs = new();
        try
        {
            ValueTransformScriptInput input = MapInput(request);
            input.Value = ConvertOutput(request.Value ?? request.ValueText, script.InputDataType);
            ValueTransformScriptGlobals globals = new(input, logs, timeout.Token);
            object? returned = _compiler
                .RunValueTransformAsync(script.PublishedSourceCode, globals, timeout.Token)
                .GetAwaiter()
                .GetResult();
            if (returned is ValueTransformScriptResult scriptResult && !scriptResult.Success)
                return Failed(scriptResult.ErrorMessage, stopwatch);

            object? rawValue = returned is ValueTransformScriptResult wrapped ? wrapped.Value : returned;
            string outputType = string.IsNullOrWhiteSpace(request.ExpectedOutputDataType)
                ? script.OutputDataType
                : request.ExpectedOutputDataType;
            object? converted = ConvertOutput(rawValue, outputType);
            stopwatch.Stop();
            return new ValueTransformExecutionResult
            {
                Success = true,
                Value = converted,
                ValueText = FormatValue(converted),
                OutputDataType = outputType,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            return Failed($"值处理脚本执行超过 {timeoutMilliseconds} ms。", stopwatch);
        }
        catch (Exception ex)
        {
            return Failed(ex.GetBaseException().Message, stopwatch);
        }
    }

    /// <summary>
    /// 执行尚未发布的草稿代码，供脚本中心测试输入输出。
    /// </summary>
    public ValueTransformExecutionResult Test(ValueTransformScriptTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ScriptValidationResult validation = _compiler.PrepareValueTransform(request.SourceCode);
        if (!validation.Success)
        {
            return new ValueTransformExecutionResult
            {
                Success = false,
                ErrorMessage = string.Join(Environment.NewLine, validation.Errors)
            };
        }
        Stopwatch stopwatch = Stopwatch.StartNew();
        int timeoutMilliseconds = Math.Clamp(request.TimeoutMilliseconds, 10, 5000);
        using CancellationTokenSource timeout = new(timeoutMilliseconds);
        try
        {
            object? inputValue = ConvertOutput(request.ValueText, request.InputDataType);
            ValueTransformScriptGlobals globals = new(new ValueTransformScriptInput
            {
                Value = inputValue,
                ValueText = request.ValueText ?? string.Empty,
                DataType = request.InputDataType ?? string.Empty,
                Quality = "Good",
                Timestamp = DateTimeOffset.Now
            }, new ScriptLogCollector(), timeout.Token);
            object? returned = _compiler
                .RunValueTransformAsync(request.SourceCode, globals, timeout.Token)
                .GetAwaiter()
                .GetResult();
            if (returned is ValueTransformScriptResult scriptResult && !scriptResult.Success)
                return Failed(scriptResult.ErrorMessage, stopwatch);
            object? rawValue = returned is ValueTransformScriptResult wrapped ? wrapped.Value : returned;
            object? converted = ConvertOutput(rawValue, request.OutputDataType);
            stopwatch.Stop();
            return new ValueTransformExecutionResult
            {
                Success = true,
                Value = converted,
                ValueText = FormatValue(converted),
                OutputDataType = request.OutputDataType,
                DurationMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return Failed(ex.GetBaseException().Message, stopwatch);
        }
    }

    /// <summary>
    /// 将核心层请求映射为脚本可读输入。
    /// </summary>
    private static ValueTransformScriptInput MapInput(ValueTransformExecutionRequest request)
    {
        return new ValueTransformScriptInput
        {
            Value = request.Value,
            ValueText = request.ValueText,
            DataType = request.DataType,
            ChannelId = request.ChannelId,
            ChannelName = request.ChannelName,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName,
            GroupId = request.GroupId,
            GroupName = request.GroupName,
            TagId = request.TagId,
            TagName = request.TagName,
            PointCode = request.PointCode,
            Quality = request.Quality,
            Timestamp = request.Timestamp,
            Parameters = request.Parameters
        };
    }

    /// <summary>
    /// 按声明的数据类型转换脚本输出。
    /// </summary>
    private static object? ConvertOutput(object? value, string? dataType)
    {
        string type = (dataType ?? string.Empty).Trim();
        if (string.Equals(type, "Object", StringComparison.OrdinalIgnoreCase) || type.Length == 0)
            return value;
        if (value is null)
            throw new InvalidOperationException("值处理脚本不能向强类型输出返回 null。");

        return type.ToUpperInvariant() switch
        {
            "BOOL" or "BOOLEAN" => ConvertBoolean(value),
            "INT8" or "SBYTE" => Convert.ToSByte(value, CultureInfo.InvariantCulture),
            "UINT8" or "BYTE" => Convert.ToByte(value, CultureInfo.InvariantCulture),
            "INT16" => Convert.ToInt16(value, CultureInfo.InvariantCulture),
            "UINT16" => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
            "INT32" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
            "UINT32" => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
            "INT64" => Convert.ToInt64(value, CultureInfo.InvariantCulture),
            "UINT64" => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
            "FLOAT" or "SINGLE" => Convert.ToSingle(value, CultureInfo.InvariantCulture),
            "DOUBLE" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            "DECIMAL" => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
            "STRING" => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            "DATETIME" => Convert.ToDateTime(value, CultureInfo.InvariantCulture),
            _ when type.EndsWith("Array", StringComparison.OrdinalIgnoreCase) && value is Array => value,
            _ => throw new InvalidOperationException($"不支持的值处理输出数据类型 {type}。")
        };
    }

    /// <summary>
    /// 将常见布尔文本和数字转换为布尔值。
    /// </summary>
    private static bool ConvertBoolean(object value)
    {
        if (value is bool boolean)
            return boolean;
        string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (bool.TryParse(text, out bool parsed))
            return parsed;
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double number))
            return Math.Abs(number) > double.Epsilon;
        throw new InvalidOperationException($"无法将“{text}”转换为 Bool。");
    }

    /// <summary>
    /// 使用不受区域设置影响的格式输出处理结果。
    /// </summary>
    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// 创建带执行耗时的失败结果。
    /// </summary>
    private static ValueTransformExecutionResult Failed(string message, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new ValueTransformExecutionResult
        {
            Success = false,
            ErrorMessage = string.IsNullOrWhiteSpace(message) ? "值处理脚本执行失败。" : message,
            DurationMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// 为脚本编号和固定发布版本构造缓存键。
    /// </summary>
    private static string BuildCacheKey(string scriptId, int version)
    {
        return $"{scriptId}\u001f{Math.Max(0, version)}";
    }

    /// <summary>
    /// 检查脚本声明的范围是否允许当前规则或标签清洗调用。
    /// </summary>
    private static bool IsUsageAllowed(ValueTransformScriptScope scope, string usage)
    {
        if (string.IsNullOrWhiteSpace(usage) || scope == ValueTransformScriptScope.Both)
            return true;
        if (string.Equals(usage, "RuleEngine", StringComparison.OrdinalIgnoreCase))
            return scope == ValueTransformScriptScope.RuleEngine;
        if (string.Equals(usage, "TagCleaning", StringComparison.OrdinalIgnoreCase))
            return scope == ValueTransformScriptScope.TagCleaning;
        return false;
    }
}
