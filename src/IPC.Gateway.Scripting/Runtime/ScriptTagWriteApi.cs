using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 向点位联动脚本公开受白名单和单次数量限制的点位写入能力。
/// </summary>
public sealed class ScriptTagWriteApi
{
    private readonly IScriptTagAccessor _accessor;
    private readonly HashSet<string> _allowedPaths;
    private readonly int _maxWrites;
    private readonly ScriptTagWriteContext _writeContext;
    private readonly ScriptLogCollector _log;
    private readonly CancellationToken _cancellationToken;
    private readonly bool _enabled;
    private int _writeCount;

    /// <summary>
    /// 创建当前脚本执行使用的点位写入 API。
    /// </summary>
    public ScriptTagWriteApi(
        IScriptTagAccessor accessor,
        IEnumerable<string>? allowedPaths,
        int maxWrites,
        ScriptTagWriteContext writeContext,
        ScriptLogCollector log,
        CancellationToken cancellationToken,
        bool enabled)
    {
        _accessor = accessor;
        _allowedPaths = (allowedPaths ?? [])
            .Select(NormalizePath)
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _maxWrites = Math.Clamp(maxWrites, 1, 100);
        _writeContext = writeContext?.Clone() ?? new ScriptTagWriteContext();
        _log = log;
        _cancellationToken = cancellationToken;
        _enabled = enabled;
    }

    /// <summary>
    /// 向白名单内的目标点位写入一个值。
    /// </summary>
    public async Task SetAsync(string path, object? value)
    {
        if (!_enabled)
            throw new InvalidOperationException("当前脚本类型不允许写入点位，请将脚本类型设置为点位联动脚本。");

        string requestedPath = NormalizePath(path);
        ScriptTagValue? target = _accessor.Read(path);
        string canonicalPath = target is null ? requestedPath : NormalizePath(target.Path);
        if (!_allowedPaths.Contains(canonicalPath))
            throw new InvalidOperationException($"点位 {path} 不在当前脚本的允许写入白名单中。");

        int currentCount = Interlocked.Increment(ref _writeCount);
        if (currentCount > _maxWrites)
            throw new InvalidOperationException($"当前脚本单次最多允许写入 {_maxWrites} 个点位。");

        await _accessor.WriteAsync(canonicalPath, value, _writeContext, _cancellationToken).ConfigureAwait(false);
        _log.Information($"点位写入成功：{canonicalPath} = {FormatValue(value)}");
    }

    /// <summary>
    /// 提供与常见异步写入命名一致的别名。
    /// </summary>
    public Task WriteAsync(string path, object? value)
    {
        return SetAsync(path, value);
    }

    /// <summary>
    /// 规范化四段式点位路径以便执行白名单比较。
    /// </summary>
    private static string NormalizePath(string? path)
    {
        string[] parts = (path ?? string.Empty).Trim().Split('/');
        if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[3]))
            throw new ArgumentException("点位路径必须为 ChannelId/DeviceId/GroupId/TagId，设备直属点位的 GroupId 留空但保留斜杠。", nameof(path));
        return string.Join("/", parts.Select(part => part.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// 将日志中的写入值转换为简短文本。
    /// </summary>
    private static string FormatValue(object? value)
    {
        string text = value?.ToString() ?? "<null>";
        return text.Length <= 200 ? text : text[..200] + "…";
    }
}
