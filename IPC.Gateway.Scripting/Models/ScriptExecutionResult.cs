namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一次脚本执行的返回结果。
/// </summary>
public sealed class ScriptExecutionResult
{
    public string ScriptId { get; set; } = string.Empty;
    public ScriptExecutionState State { get; set; } = ScriptExecutionState.Idle;
    public object? ReturnValue { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public long DurationMilliseconds { get; set; }
    public List<ScriptLogEntry> Logs { get; set; } = [];
}

/// <summary>
/// 表示脚本执行期间产生的一条结构化日志。
/// </summary>
public sealed class ScriptLogEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Level { get; set; } = "Information";
    public string Message { get; set; } = string.Empty;
}
