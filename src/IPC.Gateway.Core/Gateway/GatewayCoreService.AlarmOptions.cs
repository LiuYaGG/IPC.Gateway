using IPC.EdgeGateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Gateway;

public sealed partial class GatewayCoreService
{
    public IReadOnlyList<HistoryPointOption> GetConfiguredAlarmOptions(string level, GatewayAlarmQuery filter,
        string? search, int limit, CancellationToken cancellationToken)
    {
        ProjectConfig project;
        lock (_syncRoot) project = _project;
        var result = new List<HistoryPointOption>();
        static bool Match(string id, string? selected) => selected == null || string.Equals(id, selected, StringComparison.OrdinalIgnoreCase);
        void Add(string id, string name, string? path = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= limit || (!string.IsNullOrEmpty(search) && !name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))) return;
            result.Add(new(id, name, path));
        }
        foreach (var channel in project.Channels)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Match(channel.Id, filter.ChannelId)) continue;
            if (level == "channel") { Add(channel.Id, channel.Name); continue; }
            foreach (var device in project.Devices.Where(device => Match(device.ChannelId, channel.Id) && Match(device.Id, filter.DeviceId)))
            {
                if (level == "device") { Add(device.Id, device.Name); continue; }
                if (level == "group")
                {
                    if (device.Tags.Count > 0 && Match("", filter.GroupId)) Add("", "");
                    foreach (var group in device.Groups.Where(group => Match(group.Id, filter.GroupId))) Add(group.Id, group.Name);
                    continue;
                }
                if (filter.GroupId == null || filter.GroupId == "") AddTags(device.Tags, "");
                foreach (var group in device.Groups.Where(group => Match(group.Id, filter.GroupId))) AddTags(group.Tags, group.Name);
                void AddTags(IEnumerable<TagConfig> tags, string groupName)
                {
                    foreach (var tag in tags)
                        if (Match(tag.Id, filter.TagId)) Add(tag.Id, tag.Name, HistoryValuePath.Build(channel.Name, device.Name, groupName, tag.Name));
                }
            }
        }
        return result;
    }
}
