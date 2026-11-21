using IPC.EdgeGateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Gateway;

public sealed partial class GatewayCoreService
{
    public IReadOnlyList<HistoryPointOption> GetConfiguredHistoryOptions(string level, string? channelId, string? deviceId,
        string? groupId, string? search, int limit, CancellationToken ct, bool numericOnly = false)
    {
        ProjectConfig project;
        lock (_syncRoot) project = _project; // SetCurrentProject replaces this configuration as a whole.
        var result = new List<HistoryPointOption>();
        void Add(string id, string name, string? path = null)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count == limit || (!string.IsNullOrEmpty(search) && !name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !(path?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))) return;
            result.Add(new(id, name, path));
        }

        if (level == "channel")
        {
            foreach (var channel in project.Channels)
                Add(channel.Id, channel.Name);
            return result;
        }
        var source = project.Channels.FirstOrDefault(c => string.Equals(c.Id, channelId, StringComparison.OrdinalIgnoreCase));
        if (source == null) return result;
        foreach (var device in project.Devices.Where(d => string.Equals(d.ChannelId, source.Id, StringComparison.OrdinalIgnoreCase)))
        {
            if (level == "device") { Add(device.Id, device.Name); continue; }
            if (!string.Equals(deviceId, device.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (level == "group")
            {
                if (device.Tags.Count > 0) Add("", "");
                foreach (var group in device.Groups) Add(group.Id, group.Name);
                break;
            }
            var selected = device.Groups.FirstOrDefault(g => string.Equals(groupId, g.Id, StringComparison.OrdinalIgnoreCase));
            if (groupId != "" && selected == null) break;
            foreach (var tag in selected?.Tags ?? device.Tags)
            {
                if (numericOnly && !HistoryValueIndex.IsNumericTrendType(tag.DataType.ToString())) continue;
                Add(tag.Id, tag.Name, HistoryValuePath.Build(source.Name, device.Name, selected?.Name, tag.Name));
                if (result.Count >= limit) break;
            }
            break;
        }
        return result;
    }
}
