using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 从当前项目配置中解析脚本目标点位的权威元数据。
/// </summary>
internal static class GatewayScriptTagMetadataResolver
{
    /// <summary>
    /// 按运行快照中的四段标识查找标签，并返回配置的数据类型名称。
    /// </summary>
    internal static string ResolveDataType(ProjectConfig project, TagValueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(snapshot);

        DeviceConfig? device = (project.Devices ?? [])
            .FirstOrDefault(item =>
                string.Equals(item.Id, snapshot.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.ChannelId, snapshot.ChannelId, StringComparison.OrdinalIgnoreCase));
        if (device is null)
            throw new KeyNotFoundException($"当前项目配置中未找到脚本目标设备 {snapshot.ChannelId}/{snapshot.DeviceId}。");

        TagConfig? tag;
        if (string.IsNullOrWhiteSpace(snapshot.GroupId))
        {
            tag = (device.Tags ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, snapshot.TagId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            GroupConfig? group = (device.Groups ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, snapshot.GroupId, StringComparison.OrdinalIgnoreCase));
            if (group is null)
                throw new KeyNotFoundException($"当前项目配置中未找到脚本目标分组 {snapshot.GroupId}。");

            tag = (group.Tags ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, snapshot.TagId, StringComparison.OrdinalIgnoreCase));
        }

        if (tag is null)
            throw new KeyNotFoundException($"当前项目配置中未找到脚本目标标签 {snapshot.TagId}。");

        return tag.DataType.ToString();
    }
}
