namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 定义脚本的触发方式。
/// </summary>
public enum ScriptTriggerType
{
    Manual,
    Interval,
    TagChanged
}

/// <summary>
/// 定义点位变化脚本的触发模式。
/// </summary>
public enum ScriptTagChangeMode
{
    Any,
    RisingEdge,
    FallingEdge
}

/// <summary>
/// 定义外部数据库提供程序。
/// </summary>
public enum ScriptDatabaseProvider
{
    SqlServer,
    PostgreSql,
    MySql,
    Sqlite,
    Oracle,
    Dameng,
    KingbaseEs,
    ClickHouse
}

/// <summary>
/// 定义脚本允许提交的数据库写入操作。
/// </summary>
public enum ScriptDatabaseOperation
{
    Insert,
    Update
}

/// <summary>
/// 定义脚本执行的最终状态。
/// </summary>
public enum ScriptExecutionState
{
    Idle,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Skipped
}
