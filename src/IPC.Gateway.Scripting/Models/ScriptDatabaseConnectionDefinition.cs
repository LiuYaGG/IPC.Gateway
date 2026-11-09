namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一个可由脚本写入的本地或远程数据库连接。
/// </summary>
public sealed class ScriptDatabaseConnectionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ScriptDatabaseProvider Provider { get; set; } = ScriptDatabaseProvider.SqlServer;
    public string ConnectionString { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int ConnectionTimeoutSeconds { get; set; } = 10;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建当前数据库连接定义的副本。
    /// </summary>
    public ScriptDatabaseConnectionDefinition Clone()
    {
        return (ScriptDatabaseConnectionDefinition)MemberwiseClone();
    }
}
