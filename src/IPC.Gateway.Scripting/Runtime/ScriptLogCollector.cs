using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 收集单次脚本执行产生的结构化日志。
/// </summary>
public sealed class ScriptLogCollector
{
    private readonly object _syncRoot = new();
    private readonly List<ScriptLogEntry> _entries = [];

    /// <summary>
    /// 记录信息日志。
    /// </summary>
    public void Information(string message)
    {
        Add("Information", message);
    }

    /// <summary>
    /// 记录警告日志。
    /// </summary>
    public void Warning(string message)
    {
        Add("Warning", message);
    }

    /// <summary>
    /// 记录错误日志。
    /// </summary>
    public void Error(string message)
    {
        Add("Error", message);
    }

    /// <summary>
    /// 获取当前执行日志的不可变副本。
    /// </summary>
    public IReadOnlyList<ScriptLogEntry> GetEntries()
    {
        lock (_syncRoot)
            return _entries.Select(CloneEntry).ToList();
    }

    /// <summary>
    /// 添加指定级别的日志记录。
    /// </summary>
    private void Add(string level, string? message)
    {
        lock (_syncRoot)
        {
            _entries.Add(new ScriptLogEntry
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Level = level,
                Message = message ?? string.Empty
            });
        }
    }

    /// <summary>
    /// 复制一条脚本日志。
    /// </summary>
    private static ScriptLogEntry CloneEntry(ScriptLogEntry entry)
    {
        return new ScriptLogEntry { TimestampUtc = entry.TimestampUtc, Level = entry.Level, Message = entry.Message };
    }
}
