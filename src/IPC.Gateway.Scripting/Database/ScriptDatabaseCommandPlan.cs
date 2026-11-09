namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 表示由结构化写入请求生成的参数化数据库命令计划。
/// </summary>
public sealed class ScriptDatabaseCommandPlan
{
    public string CommandText { get; set; } = string.Empty;
    public List<ScriptDatabaseParameter> Parameters { get; set; } = [];
}

/// <summary>
/// 表示参数化数据库命令中的单个参数。
/// </summary>
public sealed class ScriptDatabaseParameter
{
    public string Name { get; set; } = string.Empty;
    public object? Value { get; set; }
}
