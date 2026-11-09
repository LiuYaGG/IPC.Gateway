using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 定义值处理脚本能够访问的纯计算全局对象。
/// </summary>
public sealed class ValueTransformScriptGlobals
{
    /// <summary>
    /// 创建一次值处理脚本执行上下文。
    /// </summary>
    public ValueTransformScriptGlobals(
        ValueTransformScriptInput input,
        ScriptLogCollector log,
        CancellationToken cancellationToken)
    {
        Input = input;
        Log = log;
        CancellationToken = cancellationToken;
    }

    public ValueTransformScriptInput Input { get; }
    public ScriptLogCollector Log { get; }
    public CancellationToken CancellationToken { get; }
    public DateTimeOffset Now => DateTimeOffset.Now;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建脚本可直接返回的成功结果。
    /// </summary>
    public ValueTransformScriptResult Success(object? value)
    {
        return ValueTransformScriptResult.Ok(value);
    }

    /// <summary>
    /// 创建脚本可直接返回的失败结果。
    /// </summary>
    public ValueTransformScriptResult Failure(string message)
    {
        return ValueTransformScriptResult.Failure(message);
    }
}
