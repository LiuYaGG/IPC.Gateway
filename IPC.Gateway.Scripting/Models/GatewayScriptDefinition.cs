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
    public ScriptTriggerType TriggerType { get; set; } = ScriptTriggerType.Manual;
    public int IntervalSeconds { get; set; } = 60;
    public string TriggerTagPath { get; set; } = string.Empty;
    public ScriptTagChangeMode TagChangeMode { get; set; } = ScriptTagChangeMode.Any;
    public int DebounceMilliseconds { get; set; } = 500;
    public int TimeoutSeconds { get; set; } = 5;
    public string SourceCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 创建当前脚本定义的副本。
    /// </summary>
    public GatewayScriptDefinition Clone()
    {
        return (GatewayScriptDefinition)MemberwiseClone();
    }
}
