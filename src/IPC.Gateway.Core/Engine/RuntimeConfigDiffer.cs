using IPC.Runtime.Configuration;
using IPC.Runtime.Indexing;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeConfigDiffer
    {
        public bool CanReuseDeviceState(DeviceConfig? previous, DeviceConfig? current)
        {
            return DeviceConfigComparer.CanReuseRuntimeState(previous, current) ||
                   DeviceConfigComparer.CanReuseRuntimeStateForEnabledChange(previous, current);
        }

        // A project reload restarts the engine. Only carry observations whose
        // source can still be identified in both configurations.
        internal static HashSet<string> GetReusableSnapshotKeys(ProjectConfig? previous, ProjectConfig current)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (previous == null || !string.Equals(previous.ProjectId, current.ProjectId, StringComparison.OrdinalIgnoreCase))
                return keys;

            var oldDevices = previous.Devices.ToDictionary(device => device.Id, StringComparer.OrdinalIgnoreCase);
            var oldDisabledChannels = previous.Channels.Where(c => !c.Enabled).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disabledChannels = current.Channels.Where(c => !c.Enabled).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var device in current.Devices)
            {
                if (!device.Enabled || disabledChannels.Contains(device.ChannelId) || oldDisabledChannels.Contains(device.ChannelId) ||
                    !oldDevices.TryGetValue(device.Id, out var oldDevice) || !DeviceConfigComparer.CanReuseRuntimeState(oldDevice, device))
                    continue;

                var oldTags = ReadableTags(oldDevice).ToDictionary(point => point.Tag.Id, StringComparer.OrdinalIgnoreCase);
                foreach (var (groupId, tag) in ReadableTags(device))
                {
                    if (oldTags.TryGetValue(tag.Id, out var old) &&
                        string.Equals(old.GroupId, groupId, StringComparison.OrdinalIgnoreCase) &&
                        CompiledTagRead.HasSameSource(tag, CompiledDeviceReadPlan.ResolveAddress(device, tag),
                            old.Tag, CompiledDeviceReadPlan.ResolveAddress(oldDevice, old.Tag)))
                        keys.Add(TagPath.BuildIdentity(device.ChannelId, device.Id, groupId, tag.Id));
                }
            }
            return keys;
        }

        private static IEnumerable<(string GroupId, TagConfig Tag)> ReadableTags(DeviceConfig device)
        {
            foreach (var tag in device.Tags)
                if (tag.Enabled && tag.AccessMode != TagAccessMode.WriteOnly) yield return (string.Empty, tag);
            foreach (var group in device.Groups.Where(group => group.Enabled))
                foreach (var tag in group.Tags)
                    if (tag.Enabled && tag.AccessMode != TagAccessMode.WriteOnly) yield return (group.Id, tag);
        }
    }
}
