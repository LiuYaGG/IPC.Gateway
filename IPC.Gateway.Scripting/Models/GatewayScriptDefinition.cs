namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一段可按手动、周期或点位变化方式触发的 C# 脚本。
/// </summary>
public sealed class GatewayScriptDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public GatewayScriptType ScriptType { get; set; } = GatewayScriptType.DatabaseWrite;
    public ScriptTriggerType TriggerType { get; set; } = ScriptTriggerType.Manual;
    public int IntervalSeconds { get; set; } = 60;
    public string TriggerTagPath { get; set; } = string.Empty;
    public ScriptTagChangeMode TagChangeMode { get; set; } = ScriptTagChangeMode.Any;
    public int DebounceMilliseconds { get; set; } = 500;
    public int TimeoutSeconds { get; set; } = 5;
    public List<string> AllowedWriteTagPaths { get; set; } = [];
    public int MaxWritesPerExecution { get; set; } = 20;
    public ValueTransformScriptScope ValueTransformScope { get; set; } = ValueTransformScriptScope.Both;
    public string NodeCategory { get; set; } = "处理";
    public string InputDataType { get; set; } = "Double";
    public string OutputDataType { get; set; } = "Double";
    public int TransformTimeoutMilliseconds { get; set; } = 100;
    public string SourceCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public int PublishedVersion { get; set; }
    public string PublishedSourceCode { get; set; } = string.Empty;
    public DateTimeOffset? PublishedUtc { get; set; }
    public List<ValueTransformPublishedVersion> PublishedVersions { get; set; } = [];
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建当前脚本定义的副本。
    /// </summary>
    public GatewayScriptDefinition Clone()
    {
        GatewayScriptDefinition clone = (GatewayScriptDefinition)MemberwiseClone();
        clone.AllowedWriteTagPaths = (AllowedWriteTagPaths ?? []).ToList();
        clone.PublishedVersions = (PublishedVersions ?? []).Select(item => item.Clone()).ToList();
        return clone;
    }
}

/// <summary>
/// 保存一个可长期被规则或标签配置固定引用的值处理脚本发布版本。
/// </summary>
public sealed class ValueTransformPublishedVersion
{
    public int Version { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public DateTimeOffset PublishedUtc { get; set; }

    /// <summary>
    /// 创建发布版本的独立副本。
    /// </summary>
    public ValueTransformPublishedVersion Clone()
    {
        return (ValueTransformPublishedVersion)MemberwiseClone();
    }
}
