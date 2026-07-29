namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示一次脚本点位写入的来源，用于抑制自触发并限制跨脚本联动深度。
/// </summary>
public sealed class ScriptTagWriteContext
{
    public string ScriptId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public int LinkageDepth { get; set; }

    /// <summary>
    /// 创建当前写入来源的副本。
    /// </summary>
    public ScriptTagWriteContext Clone()
    {
        return (ScriptTagWriteContext)MemberwiseClone();
    }
}
