namespace IPC.Gateway.Scripting.Models;

/// <summary>
/// 表示脚本安全检查和编译检查的结果。
/// </summary>
public sealed class ScriptValidationResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}
