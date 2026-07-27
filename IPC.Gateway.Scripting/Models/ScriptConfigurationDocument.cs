namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示脚本模块持久化的完整配置文档。
/// </summary>
public sealed class ScriptConfigurationDocument
{
    public int Version { get; set; } = 1;
    public List<ScriptDatabaseConnectionDefinition> Connections { get; set; } = [];
    public List<ScriptDatabaseWriteTarget> Targets { get; set; } = [];
    public List<GatewayScriptDefinition> Scripts { get; set; } = [];

    /// <summary>
    /// 创建当前配置文档的深拷贝。
    /// </summary>
    public ScriptConfigurationDocument Clone()
    {
        return new ScriptConfigurationDocument
        {
            Version = Version,
            Connections = Connections.Select(item => item.Clone()).ToList(),
            Targets = Targets.Select(item => item.Clone()).ToList(),
            Scripts = Scripts.Select(item => item.Clone()).ToList()
        };
    }
}
