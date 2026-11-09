using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway;

/// <summary>
/// 根据设备和点位的当前状态过滤已经恢复的运行时错误。
/// </summary>
public static class RuntimeErrorActivityFilter
{
    /// <summary>
    /// 返回仍与当前运行状态一致的错误，连接恢复事件和已经恢复的错误不会继续显示。
    /// </summary>
    public static IList<RuntimeErrorDetail> Filter(
        IList<RuntimeErrorDetail>? errors,
        IList<DeviceRuntimeStatus>? devices,
        IList<TagValueSnapshot>? tags)
    {
        if (errors == null || errors.Count == 0)
            return new List<RuntimeErrorDetail>();

        Dictionary<string, DeviceRuntimeStatus> devicesByKey = (devices ?? new List<DeviceRuntimeStatus>())
            .Where(item => item != null)
            .GroupBy(item => BuildDeviceIdentity(item.ChannelId, item.DeviceId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TagValueSnapshot> tagsByKey = (tags ?? new List<TagValueSnapshot>())
            .Where(item => item != null)
            .GroupBy(item => TagPath.BuildIdentity(item.ChannelId, item.DeviceId, item.GroupId, item.TagId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        return errors
            .Where(error => error != null && IsActive(error, devicesByKey, tagsByKey))
            .ToList();
    }

    /// <summary>
    /// 判断一条错误是否仍代表当前故障状态。
    /// </summary>
    private static bool IsActive(
        RuntimeErrorDetail error,
        IReadOnlyDictionary<string, DeviceRuntimeStatus> devices,
        IReadOnlyDictionary<string, TagValueSnapshot> tags)
    {
        string category = (error.Category ?? string.Empty).Trim();
        if (category.Equals("DeviceConnectionRecovered", StringComparison.OrdinalIgnoreCase))
            return false;

        if (category.StartsWith("DeviceConnection", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(error.DeviceId) &&
            devices.TryGetValue(BuildDeviceIdentity(error.ChannelId, error.DeviceId), out DeviceRuntimeStatus? device))
        {
            return device.Enabled && !device.IsConnected;
        }

        if (category.Equals("TagRead", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(error.TagId) &&
            tags.TryGetValue(
                TagPath.BuildIdentity(error.ChannelId, error.DeviceId, error.GroupId, error.TagId),
                out TagValueSnapshot? tag))
        {
            return tag.Quality == TagQuality.ReadError;
        }

        return true;
    }

    /// <summary>
    /// 生成不区分大小写的设备身份键。
    /// </summary>
    private static string BuildDeviceIdentity(string channelId, string deviceId)
    {
        return TagPath.Normalize(channelId) + "/" + TagPath.Normalize(deviceId);
    }
}
