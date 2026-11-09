using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration
{
    internal static class ProjectChannelNormalizer
    {
        public static void Normalize(ProjectConfig project)
        {
            project.Channels ??= new List<ChannelConfig>();

            Dictionary<string, ChannelConfig> channelsById = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ChannelConfig> channelsByDriver = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < project.Channels.Count; i++)
            {
                ChannelConfig? channel = project.Channels[i];
                if (channel == null)
                    continue;

                if (string.IsNullOrWhiteSpace(channel.Id))
                    channel.Id = Guid.NewGuid().ToString("N");
                channel.DriverId = NormalizeDriverId(channel.DriverId);
                channel.Name = string.IsNullOrWhiteSpace(channel.Name)
                    ? CreateUniqueName(channel.Protocol, names)
                    : channel.Name.Trim();
                names.Add(channel.Name);
                channel.MaxConcurrentDevicePolls = Math.Clamp(channel.MaxConcurrentDevicePolls, 1, 256);
                if (channel.Id.StartsWith("auto-", StringComparison.OrdinalIgnoreCase) &&
                    channel.MaxConcurrentDevicePolls == 4)
                    channel.MaxConcurrentDevicePolls = 64;
                channel.SchedulingWeight = Math.Clamp(channel.SchedulingWeight, 1, 100);

                channelsById[channel.Id] = channel;
                channelsByDriver.TryAdd(BuildDriverKey(channel.Protocol, channel.DriverId), channel);
            }

            foreach (DeviceConfig device in project.Devices)
            {
                if (device == null)
                    continue;

                string driverId = NormalizeDriverId(device.Connection?.DriverId);
                ChannelConfig? assigned = null;
                if (!string.IsNullOrWhiteSpace(device.ChannelId))
                    channelsById.TryGetValue(device.ChannelId, out assigned);

                if (assigned == null || !Matches(assigned, device.Protocol, driverId))
                {
                    string key = BuildDriverKey(device.Protocol, driverId);
                    if (!channelsByDriver.TryGetValue(key, out assigned))
                    {
                        assigned = new ChannelConfig
                        {
                            Id = CreateStableChannelId(key),
                            Name = CreateUniqueName(device.Protocol, names),
                            Protocol = device.Protocol,
                            DriverId = driverId
                        };
                        project.Channels.Add(assigned);
                        channelsById[assigned.Id] = assigned;
                        channelsByDriver[key] = assigned;
                        names.Add(assigned.Name);
                    }

                    device.ChannelId = assigned.Id;
                }
            }
        }

        private static bool Matches(ChannelConfig channel, PlcProtocol protocol, string driverId)
        {
            return channel.Protocol == protocol &&
                   string.Equals(NormalizeDriverId(channel.DriverId), driverId, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildDriverKey(PlcProtocol protocol, string driverId)
        {
            return protocol + "|" + NormalizeDriverId(driverId);
        }

        private static string NormalizeDriverId(string? driverId)
        {
            return (driverId ?? string.Empty).Trim();
        }

        private static string CreateStableChannelId(string driverKey)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("configured-channel|" + driverKey.ToUpperInvariant()));
            return "auto-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
        }

        private static string CreateUniqueName(PlcProtocol protocol, HashSet<string> names)
        {
            string baseName = protocol + " 通道";
            string name = baseName;
            int suffix = 2;
            while (names.Contains(name))
                name = baseName + " " + suffix++;
            return name;
        }
    }
}
