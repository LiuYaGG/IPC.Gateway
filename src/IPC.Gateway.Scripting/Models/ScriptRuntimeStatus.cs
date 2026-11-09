namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示单个脚本最近一次运行状态。
/// </summary>
public sealed class ScriptRuntimeStatus
{
    public string ScriptId { get; set; } = string.Empty;
    public ScriptExecutionState State { get; set; } = ScriptExecutionState.Idle;
    public long ExecutionCount { get; set; }
    public long FailureCount { get; set; }
    public DateTimeOffset? LastStartedUtc { get; set; }
    public DateTimeOffset? LastFinishedUtc { get; set; }
    public long LastDurationMilliseconds { get; set; }
    public string LastError { get; set; } = string.Empty;
    public List<ScriptLogEntry> RecentLogs { get; set; } = [];
}

/// <summary>
/// 表示数据库持久化写入队列的运行状态。
/// </summary>
public sealed class ScriptDatabaseQueueStatus
{
    public int PendingCount { get; set; }
    public int FailedCount { get; set; }
    public long SucceededCount { get; set; }
    public long RetriedCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTimeOffset? LastSuccessUtc { get; set; }
}

/// <summary>
/// 表示脚本中心页面所需的完整概览数据。
/// </summary>
public sealed class ScriptCenterOverview
{
    public List<ScriptDatabaseConnectionDefinition> Connections { get; set; } = [];
    public List<ScriptDatabaseWriteTarget> Targets { get; set; } = [];
    public List<GatewayScriptDefinition> Scripts { get; set; } = [];
    public List<ScriptRuntimeStatus> RuntimeStatuses { get; set; } = [];
    public ScriptDatabaseQueueStatus QueueStatus { get; set; } = new();
}
