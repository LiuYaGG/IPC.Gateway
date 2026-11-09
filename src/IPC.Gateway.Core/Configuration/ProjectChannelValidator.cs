using System;
using System.Collections.Generic;

namespace IPC.Runtime.Configuration
{
    internal static class ProjectChannelValidator
    {
        public static void Validate(ProjectConfig project, ProjectConfigValidationResult result)
        {
            Dictionary<string, ChannelConfig> channels = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IList<ChannelConfig> configuredChannels = project.Channels ?? new List<ChannelConfig>();
            for (int i = 0; i < configuredChannels.Count; i++)
            {
                ChannelConfig? channel = configuredChannels[i];
                string prefix = "通道[" + (i + 1) + "]";
                if (channel == null)
                {
                    result.AddError(prefix + "不能为空。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(channel.Id) || !channels.TryAdd(channel.Id, channel))
                    result.AddError(prefix + "ID不能为空或重复。");
                if (string.IsNullOrWhiteSpace(channel.Name) || !names.Add(channel.Name.Trim()))
                    result.AddError(prefix + "名称不能为空或重复。");
                if (channel.MaxConcurrentDevicePolls < 1 || channel.MaxConcurrentDevicePolls > 256)
                    result.AddError(prefix + "最大并发设备轮询数必须在1到256之间。");
                if (channel.SchedulingWeight < 1 || channel.SchedulingWeight > 100)
                    result.AddError(prefix + "调度权重必须在1到100之间。");
            }

            foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
            {
                if (device == null)
                    continue;
                if (!channels.TryGetValue(device.ChannelId ?? string.Empty, out ChannelConfig? channel))
                {
                    result.AddError("设备“" + device.Name + "”未关联有效通道。");
                    continue;
                }

                string driverId = (device.Connection?.DriverId ?? string.Empty).Trim();
                if (channel.Protocol != device.Protocol ||
                    !string.Equals((channel.DriverId ?? string.Empty).Trim(), driverId, StringComparison.OrdinalIgnoreCase))
                    result.AddError("设备“" + device.Name + "”的协议驱动与所属通道不一致。");
            }
        }
    }
}
