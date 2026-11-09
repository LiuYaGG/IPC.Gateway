namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示等待写入外部数据库的结构化持久化任务。
/// </summary>
public sealed class ScriptDatabaseWriteRequest
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ScriptId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public ScriptDatabaseOperation Operation { get; set; }
    public Dictionary<string, object?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, object?> Keys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string DeduplicationKey { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextAttemptUtc { get; set; } = DateTimeOffset.UtcNow;
    public string LastError { get; set; } = string.Empty;
}

/// <summary>
/// 表示脚本向持久化数据库队列提交任务后的回执。
/// </summary>
public sealed class ScriptDatabaseWriteReceipt
{
    public string RequestId { get; set; } = string.Empty;
    public bool Queued { get; set; }
    public string Message { get; set; } = string.Empty;
}
