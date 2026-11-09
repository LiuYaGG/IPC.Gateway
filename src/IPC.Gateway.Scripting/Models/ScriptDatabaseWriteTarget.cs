namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一个受字段白名单和影响行数约束的数据库写入目标。
/// </summary>
public sealed class ScriptDatabaseWriteTarget
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ConnectionId { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool AllowInsert { get; set; } = true;
    public bool AllowUpdate { get; set; } = true;
    public List<string> AllowedColumns { get; set; } = [];
    public List<string> KeyColumns { get; set; } = [];
    public int MaxAffectedRows { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建当前数据库写入目标的深拷贝。
    /// </summary>
    public ScriptDatabaseWriteTarget Clone()
    {
        ScriptDatabaseWriteTarget clone = (ScriptDatabaseWriteTarget)MemberwiseClone();
        clone.AllowedColumns = [.. AllowedColumns];
        clone.KeyColumns = [.. KeyColumns];
        return clone;
    }
}
