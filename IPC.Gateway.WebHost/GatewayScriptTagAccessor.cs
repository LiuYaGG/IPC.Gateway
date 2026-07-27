using System.Collections.Concurrent;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using IPC.Runtime.Api;
using IPC.Runtime.Engine;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 将脚本点位访问接口适配到现有网关运行时缓存和写入通道。
/// </summary>
public sealed class GatewayScriptTagAccessor : IScriptTagAccessor, IDisposable
{
    private readonly GatewayCoreService _gateway;
    private readonly ConcurrentDictionary<string, ScriptTagValue> _lastValues = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// 创建点位访问适配器并订阅现有点位变化事件。
    /// </summary>
    public GatewayScriptTagAccessor(GatewayCoreService gateway)
    {
        _gateway = gateway;
        foreach (TagValueSnapshot snapshot in _gateway.Runtime.GetSnapshots())
        {
            ScriptTagValue mapped = Map(snapshot);
            _lastValues[mapped.Path] = mapped;
        }
        _gateway.Runtime.TagValueChanged += HandleTagValueChanged;
    }

    public event EventHandler<ScriptTagChangedEventArgs>? TagChanged;

    /// <summary>
    /// 按统一点位路径读取一个运行时缓存快照。
    /// </summary>
    public ScriptTagValue? Read(string path)
    {
        TagValueSnapshot? snapshot = ResolveSnapshot(path);
        return snapshot is null ? null : Map(snapshot);
    }

    /// <summary>
    /// 批量读取多个运行时点位缓存快照。
    /// </summary>
    public IReadOnlyList<ScriptTagValue> ReadMany(IEnumerable<string> paths)
    {
        List<ScriptTagValue> values = [];
        foreach (string path in paths ?? [])
        {
            ScriptTagValue? value = Read(path);
            if (value is not null)
                values.Add(value);
        }
        return values;
    }

    /// <summary>
    /// 使用点位当前数据类型向现有运行时写入值。
    /// </summary>
    public async Task WriteAsync(string path, object? value, CancellationToken cancellationToken = default)
    {
        TagValueSnapshot runtimeSnapshot = ResolveSnapshot(path) ?? throw new KeyNotFoundException($"未找到点位 {path}。");
        WriteTagResponse response = await Task.Run(() => _gateway.Runtime.WriteTag(new WriteTagRequest
        {
            ChannelId = runtimeSnapshot.ChannelId,
            DeviceId = runtimeSnapshot.DeviceId,
            GroupId = runtimeSnapshot.GroupId,
            TagId = runtimeSnapshot.TagId,
            DataType = runtimeSnapshot.DataType,
            Value = value ?? string.Empty,
            ValueText = value?.ToString() ?? string.Empty
        }), cancellationToken).ConfigureAwait(false);
        if (!response.Success)
            throw new InvalidOperationException(response.ErrorMessage);
    }

    /// <summary>
    /// 取消点位变化事件订阅。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gateway.Runtime.TagValueChanged -= HandleTagValueChanged;
    }

    /// <summary>
    /// 将现有运行时点位变化事件转换为脚本模块事件。
    /// </summary>
    private void HandleTagValueChanged(object? sender, TagValueChangedEventArgs eventArgs)
    {
        ScriptTagValue current = Map(eventArgs.Snapshot);
        _lastValues.TryGetValue(current.Path, out ScriptTagValue? previous);
        _lastValues[current.Path] = current;
        TagChanged?.Invoke(this, new ScriptTagChangedEventArgs(previous, current));
    }

    /// <summary>
    /// 优先按四段标识解析点位，未命中时再按通道、设备、分组和标签名称解析。
    /// </summary>
    private TagValueSnapshot? ResolveSnapshot(string path)
    {
        string[] parts = ParsePath(path);
        if (_gateway.Runtime.TryGetSnapshotById(parts[0], parts[1], parts[2], parts[3], out TagValueSnapshot? snapshot) && snapshot is not null)
            return snapshot;

        List<TagValueSnapshot> matches = _gateway.Runtime.GetSnapshots()
            .Where(item => MatchesPath(item, parts))
            .Take(2)
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException($"点位名称路径 {path} 匹配到多个点位，请改用唯一的 ID 路径。");
        return matches.FirstOrDefault();
    }

    /// <summary>
    /// 判断一个运行时点位是否同时匹配路径各段的标识或显示名称。
    /// </summary>
    private static bool MatchesPath(TagValueSnapshot snapshot, IReadOnlyList<string> parts)
    {
        return MatchesSegment(parts[0], snapshot.ChannelId, snapshot.ChannelName) &&
               MatchesSegment(parts[1], snapshot.DeviceId, snapshot.DeviceName) &&
               MatchesGroup(parts[2], snapshot.GroupId, snapshot.GroupName) &&
               MatchesSegment(parts[3], snapshot.TagId, snapshot.TagName);
    }

    /// <summary>
    /// 忽略大小写匹配路径段与点位标识或显示名称。
    /// </summary>
    private static bool MatchesSegment(string requested, string id, string name)
    {
        return string.Equals(requested, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(requested, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 匹配分组路径段，并正确处理设备直属点位的空分组。
    /// </summary>
    private static bool MatchesGroup(string requested, string id, string name)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return string.IsNullOrWhiteSpace(id);
        return MatchesSegment(requested, id, name);
    }

    /// <summary>
    /// 将网关点位快照映射成脚本类库模型。
    /// </summary>
    private static ScriptTagValue Map(TagValueSnapshot snapshot)
    {
        return new ScriptTagValue
        {
            Path = TagPath.BuildIdentity(snapshot.ChannelId, snapshot.DeviceId, snapshot.GroupId, snapshot.TagId),
            ChannelId = snapshot.ChannelId,
            DeviceId = snapshot.DeviceId,
            GroupId = snapshot.GroupId,
            TagId = snapshot.TagId,
            Name = string.IsNullOrWhiteSpace(snapshot.TagName) ? snapshot.TagId : snapshot.TagName,
            Value = snapshot.Value,
            ValueText = snapshot.ValueText,
            DataType = snapshot.DataType,
            Quality = snapshot.Quality.ToString(),
            Timestamp = ToDateTimeOffset(snapshot.Timestamp),
            ErrorMessage = snapshot.ErrorMessage
        };
    }

    /// <summary>
    /// 解析 ChannelId/DeviceId/GroupId/TagId 四段点位路径。
    /// </summary>
    private static string[] ParsePath(string path)
    {
        string[] parts = (path ?? string.Empty).Trim().Split('/');
        if (parts.Length != 4 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]) || string.IsNullOrWhiteSpace(parts[3]))
            throw new ArgumentException("点位路径必须为 ChannelId/DeviceId/GroupId/TagId，设备直属点位的 GroupId 留空但保留斜杠。", nameof(path));
        return parts.Select(item => item.Trim()).ToArray();
    }

    /// <summary>
    /// 将运行时日期转换为带时区偏移的日期。
    /// </summary>
    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        if (value == default)
            return default;
        return value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(value, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(value),
            _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local))
        };
    }
}
