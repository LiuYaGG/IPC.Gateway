using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Abstractions;

/// <summary>
/// 定义脚本模块与网关点位运行时之间的低耦合访问边界。
/// </summary>
public interface IScriptTagAccessor
{
    event EventHandler<ScriptTagChangedEventArgs>? TagChanged;

    /// <summary>
    /// 按统一点位路径读取一个缓存快照。
    /// </summary>
    ScriptTagValue? Read(string path);

    /// <summary>
    /// 按统一点位路径批量读取缓存快照。
    /// </summary>
    IReadOnlyList<ScriptTagValue> ReadMany(IEnumerable<string> paths);

    /// <summary>
    /// 向指定点位写入一个值。
    /// </summary>
    Task WriteAsync(string path, object? value, CancellationToken cancellationToken = default);
}

/// <summary>
/// 表示网关点位值发生变化时传递给脚本运行时的数据。
/// </summary>
public sealed class ScriptTagChangedEventArgs : EventArgs
{
    /// <summary>
    /// 使用变化前后的点位快照创建事件参数。
    /// </summary>
    public ScriptTagChangedEventArgs(ScriptTagValue? previousValue, ScriptTagValue currentValue)
    {
        PreviousValue = previousValue;
        CurrentValue = currentValue;
    }

    public ScriptTagValue? PreviousValue { get; }
    public ScriptTagValue CurrentValue { get; }
}
