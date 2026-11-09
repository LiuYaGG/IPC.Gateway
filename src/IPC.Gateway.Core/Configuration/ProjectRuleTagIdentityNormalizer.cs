using System;
using System.Collections.Generic;
using System.Linq;

namespace IPC.Runtime.Configuration
{
    internal static class ProjectRuleTagIdentityNormalizer
    {
        public static void Normalize(ProjectConfig project)
        {
            if (project == null)
                return;

            List<TagIdentity> identities = BuildIdentities(project);
            NormalizeRules(project.Rules, identities);
            NormalizeFlowRules(project.FlowRules, identities);
        }

        private static List<TagIdentity> BuildIdentities(ProjectConfig project)
        {
            Dictionary<string, string> channelNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ChannelConfig channel in project.Channels ?? new List<ChannelConfig>())
            {
                if (channel != null && !string.IsNullOrWhiteSpace(channel.Id) && !channelNames.ContainsKey(channel.Id))
                    channelNames[channel.Id] = channel.Name ?? string.Empty;
            }
            List<TagIdentity> result = new List<TagIdentity>();

            foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
            {
                if (device == null)
                    continue;

                string channelName = channelNames.TryGetValue(device.ChannelId ?? string.Empty, out string? value)
                    ? value ?? string.Empty
                    : string.Empty;
                AddTags(result, device, null, device.Tags, channelName);
                foreach (GroupConfig group in device.Groups ?? new List<GroupConfig>())
                    AddTags(result, device, group, group?.Tags, channelName);
            }

            return result;
        }

        private static void AddTags(List<TagIdentity> target, DeviceConfig device, GroupConfig? group, IList<TagConfig>? tags, string channelName)
        {
            if (tags == null)
                return;

            foreach (TagConfig tag in tags)
            {
                if (tag == null)
                    continue;

                target.Add(new TagIdentity
                {
                    ChannelId = device.ChannelId ?? string.Empty,
                    ChannelName = channelName,
                    DeviceId = device.Id ?? string.Empty,
                    DeviceName = device.Name ?? string.Empty,
                    GroupId = group?.Id ?? string.Empty,
                    GroupName = group?.Name ?? string.Empty,
                    TagId = tag.Id ?? string.Empty,
                    TagName = tag.Name ?? string.Empty,
                    PointCode = tag.PointCode ?? string.Empty,
                    DataType = tag.DataType.ToString()
                });
            }
        }

        private static void NormalizeRules(IList<EdgeRuleConfig>? rules, IList<TagIdentity> identities)
        {
            if (rules == null)
                return;

            foreach (EdgeRuleConfig rule in rules)
            {
                if (rule == null)
                    continue;

                ApplySource(rule, Find(identities, rule.SourceTagId, rule.SourcePointCode, rule.SourceDeviceName, rule.SourceGroupName, rule.SourceTagName));
                ApplyRelated(rule, Find(identities, rule.RelatedTagId, rule.RelatedPointCode, rule.RelatedDeviceName, rule.RelatedGroupName, rule.RelatedTagName));
                ApplyContext(rule, Find(identities, rule.ContextTagId, rule.ContextPointCode, rule.ContextDeviceName, rule.ContextGroupName, rule.ContextTagName));
                foreach (EdgeRuleConditionConfig condition in rule.Conditions ?? new List<EdgeRuleConditionConfig>())
                    ApplyCondition(condition, Find(identities, condition.SourceTagId, condition.SourcePointCode, condition.SourceDeviceName, condition.SourceGroupName, condition.SourceTagName));
            }
        }

        private static void NormalizeFlowRules(IList<FlowRuleDefinition>? rules, IList<TagIdentity> identities)
        {
            if (rules == null)
                return;

            foreach (FlowRuleDefinition rule in rules)
            {
                foreach (FlowRuleNode node in rule?.Nodes ?? new List<FlowRuleNode>())
                {
                    if (node == null)
                        continue;

                    ApplyNode(node, Find(identities, node.TagId, node.PointCode, node.DeviceName, node.GroupName, node.TagName));
                    ApplyNodeRelated(node, Find(identities, node.RelatedTagId, node.RelatedPointCode, node.RelatedDeviceName, node.RelatedGroupName, node.RelatedTagName));
                    ApplyNodeContext(node, Find(identities, node.ContextTagId, node.ContextPointCode, node.ContextDeviceName, node.ContextGroupName, node.ContextTagName));
                }
            }
        }

        private static TagIdentity? Find(IList<TagIdentity> identities, string tagId, string pointCode, string deviceName, string groupName, string tagName)
        {
            IEnumerable<TagIdentity> matches;
            if (!string.IsNullOrWhiteSpace(tagId))
                matches = identities.Where(item => EqualsText(item.TagId, tagId));
            else if (!string.IsNullOrWhiteSpace(pointCode))
                matches = identities.Where(item => EqualsText(item.PointCode, pointCode));
            else
                matches = identities.Where(item => EqualsText(item.DeviceName, deviceName) && EqualsText(item.GroupName, groupName) && EqualsText(item.TagName, tagName));

            TagIdentity[] result = matches.Take(2).ToArray();
            return result.Length == 1 ? result[0] : null;
        }

        private static bool EqualsText(string left, string right) =>
            !string.IsNullOrWhiteSpace(right) && string.Equals(left?.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

        private static void ApplySource(EdgeRuleConfig target, TagIdentity? source)
        {
            if (source == null) return;
            target.SourceChannelId = source.ChannelId; target.SourceChannelName = source.ChannelName;
            target.SourceDeviceId = source.DeviceId; target.SourceGroupId = source.GroupId; target.SourceTagId = source.TagId;
            target.SourceDeviceName = source.DeviceName; target.SourceGroupName = source.GroupName; target.SourceTagName = source.TagName;
            target.SourcePointCode = source.PointCode; target.SourceDataType = source.DataType;
        }

        private static void ApplyRelated(EdgeRuleConfig target, TagIdentity? source)
        {
            if (source == null) return;
            target.RelatedChannelId = source.ChannelId; target.RelatedChannelName = source.ChannelName;
            target.RelatedDeviceId = source.DeviceId; target.RelatedGroupId = source.GroupId; target.RelatedTagId = source.TagId;
            target.RelatedDeviceName = source.DeviceName; target.RelatedGroupName = source.GroupName; target.RelatedTagName = source.TagName;
            target.RelatedPointCode = source.PointCode; target.RelatedDataType = source.DataType;
        }

        private static void ApplyContext(EdgeRuleConfig target, TagIdentity? source)
        {
            if (source == null) return;
            target.ContextChannelId = source.ChannelId; target.ContextChannelName = source.ChannelName;
            target.ContextDeviceId = source.DeviceId; target.ContextGroupId = source.GroupId; target.ContextTagId = source.TagId;
            target.ContextDeviceName = source.DeviceName; target.ContextGroupName = source.GroupName; target.ContextTagName = source.TagName;
            target.ContextPointCode = source.PointCode; target.ContextDataType = source.DataType;
        }

        private static void ApplyCondition(EdgeRuleConditionConfig target, TagIdentity? source)
        {
            if (source == null) return;
            target.SourceChannelId = source.ChannelId; target.SourceChannelName = source.ChannelName;
            target.SourceDeviceId = source.DeviceId; target.SourceGroupId = source.GroupId; target.SourceTagId = source.TagId;
            target.SourceDeviceName = source.DeviceName; target.SourceGroupName = source.GroupName; target.SourceTagName = source.TagName;
            target.SourcePointCode = source.PointCode; target.SourceDataType = source.DataType;
        }

        private static void ApplyNode(FlowRuleNode target, TagIdentity? source)
        {
            if (source == null) return;
            target.ChannelId = source.ChannelId; target.ChannelName = source.ChannelName; target.DeviceId = source.DeviceId;
            target.GroupId = source.GroupId; target.TagId = source.TagId; target.DeviceName = source.DeviceName;
            target.GroupName = source.GroupName; target.TagName = source.TagName; target.PointCode = source.PointCode; target.DataType = source.DataType;
        }

        private static void ApplyNodeRelated(FlowRuleNode target, TagIdentity? source)
        {
            if (source == null) return;
            target.RelatedChannelId = source.ChannelId; target.RelatedChannelName = source.ChannelName; target.RelatedDeviceId = source.DeviceId;
            target.RelatedGroupId = source.GroupId; target.RelatedTagId = source.TagId; target.RelatedDeviceName = source.DeviceName;
            target.RelatedGroupName = source.GroupName; target.RelatedTagName = source.TagName; target.RelatedPointCode = source.PointCode; target.RelatedDataType = source.DataType;
        }

        private static void ApplyNodeContext(FlowRuleNode target, TagIdentity? source)
        {
            if (source == null) return;
            target.ContextChannelId = source.ChannelId; target.ContextChannelName = source.ChannelName; target.ContextDeviceId = source.DeviceId;
            target.ContextGroupId = source.GroupId; target.ContextTagId = source.TagId; target.ContextDeviceName = source.DeviceName;
            target.ContextGroupName = source.GroupName; target.ContextTagName = source.TagName; target.ContextPointCode = source.PointCode; target.ContextDataType = source.DataType;
        }

        private sealed class TagIdentity
        {
            public string ChannelId { get; set; } = string.Empty; public string ChannelName { get; set; } = string.Empty;
            public string DeviceId { get; set; } = string.Empty; public string DeviceName { get; set; } = string.Empty;
            public string GroupId { get; set; } = string.Empty; public string GroupName { get; set; } = string.Empty;
            public string TagId { get; set; } = string.Empty; public string TagName { get; set; } = string.Empty;
            public string PointCode { get; set; } = string.Empty; public string DataType { get; set; } = string.Empty;
        }
    }
}
