using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 向受信任管理员脚本公开受控的点位缓存读取和点位写入能力。
/// </summary>
public sealed class ScriptTagApi
{
    private readonly IScriptTagAccessor _accessor;
    private readonly ScriptTagWriteApi _writes;

    /// <summary>
    /// 创建当前脚本执行使用的点位 API。
    /// </summary>
    public ScriptTagApi(IScriptTagAccessor accessor, ScriptTagWriteApi writes)
    {
        _accessor = accessor;
        _writes = writes;
    }

    /// <summary>
    /// 读取指定统一路径的点位缓存值。
    /// </summary>
    public ScriptTagValue Read(string path)
    {
        return _accessor.Read(path) ?? throw new KeyNotFoundException($"未找到点位 {path}。");
    }

    /// <summary>
    /// 批量读取指定统一路径的点位缓存快照。
    /// </summary>
    public IReadOnlyList<ScriptTagValue> Snapshot(params string[] paths)
    {
        return _accessor.ReadMany(paths ?? []);
    }

    /// <summary>
    /// 读取点位并转换为双精度数字。
    /// </summary>
    public double ReadDouble(string path, double fallback = 0D)
    {
        return Read(path).AsDouble(fallback);
    }

    /// <summary>
    /// 读取点位并转换为三十二位整数。
    /// </summary>
    public int ReadInt32(string path, int fallback = 0)
    {
        return Read(path).AsInt32(fallback);
    }

    /// <summary>
    /// 读取点位并转换为布尔值。
    /// </summary>
    public bool ReadBoolean(string path, bool fallback = false)
    {
        return Read(path).AsBoolean(fallback);
    }

    /// <summary>
    /// 读取点位并转换为字符串。
    /// </summary>
    public string ReadString(string path, string fallback = "")
    {
        return Read(path).AsString(fallback);
    }

    /// <summary>
    /// 使用点位自身的数据类型向指定点位写入一个值。
    /// </summary>
    public Task WriteAsync(string path, object? value)
    {
        return _writes.WriteAsync(path, value);
    }
}
