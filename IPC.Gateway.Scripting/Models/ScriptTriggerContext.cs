namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一次脚本执行的触发上下文。
/// </summary>
public sealed class ScriptTriggerContext
{
    public ScriptTriggerType Type { get; set; } = ScriptTriggerType.Manual;
    public string TagPath { get; set; } = string.Empty;
    public ScriptTagValue? PreviousValue { get; set; }
    public ScriptTagValue? CurrentValue { get; set; }
    public DateTimeOffset TriggeredUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 判断当前触发是否为布尔上升沿。
    /// </summary>
    public bool IsRisingEdge()
    {
        return PreviousValue is not null && CurrentValue is not null &&
               !PreviousValue.AsBoolean() && CurrentValue.AsBoolean();
    }

    /// <summary>
    /// 判断当前触发是否为布尔下降沿。
    /// </summary>
    public bool IsFallingEdge()
    {
        return PreviousValue is not null && CurrentValue is not null &&
               PreviousValue.AsBoolean() && !CurrentValue.AsBoolean();
    }
}
