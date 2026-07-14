/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayProjectAggregate
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Domain.Gateway
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Domain.Gateway;

public sealed class GatewayProjectAggregate
{
    public GatewayProjectAggregate(ProjectConfig project)
    {
        Project = project ?? new ProjectConfig();
        ProjectConfigStore.Normalize(Project);
    }

    public ProjectConfig Project { get; }

    public int ChannelCount => Project.Channels.Count;

    public int DeviceCount => Project.Devices.Count;

    public int GroupCount => Project.Devices.Sum(device => device.Groups.Count);

    public int TagCount => Project.Devices.Sum(device =>
        device.Tags.Count + device.Groups.Sum(group => group.Tags.Count));

    public IReadOnlyList<TagConfig> GetAllTags()
    {
        List<TagConfig> tags = new List<TagConfig>();
        foreach (DeviceConfig device in Project.Devices)
        {
            tags.AddRange(device.Tags);
            foreach (GroupConfig group in device.Groups)
                tags.AddRange(group.Tags);
        }

        return tags;
    }

    public ChannelConfig AddChannel(ChannelConfig channel)
    {
        if (channel == null)
            throw new ArgumentNullException(nameof(channel));
        if (string.IsNullOrWhiteSpace(channel.Id))
            channel.Id = Guid.NewGuid().ToString("N");
        Project.Channels.Add(channel);
        ProjectConfigStore.Normalize(Project);
        return channel;
    }

    public ChannelConfig UpdateChannel(string channelId, ChannelConfig input)
    {
        ChannelConfig channel = Require(FindChannel(channelId), "Channel was not found.");
        bool hasDevices = Project.Devices.Any(device =>
            string.Equals(device.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase));
        if (hasDevices && (channel.Protocol != input.Protocol ||
            !string.Equals(channel.DriverId, input.DriverId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A channel with devices cannot change its protocol driver.");

        channel.Name = input.Name;
        channel.Enabled = input.Enabled;
        channel.Protocol = input.Protocol;
        channel.DriverId = input.DriverId;
        channel.MaxConcurrentDevicePolls = input.MaxConcurrentDevicePolls;
        channel.SchedulingWeight = input.SchedulingWeight;
        ProjectConfigStore.Normalize(Project);
        return channel;
    }

    public ChannelConfig DeleteChannel(string channelId)
    {
        ChannelConfig channel = Require(FindChannel(channelId), "Channel was not found.");
        if (Project.Devices.Any(device => string.Equals(device.ChannelId, channel.Id, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Delete or move the devices in the channel first.");
        Project.Channels.Remove(channel);
        ProjectConfigStore.Normalize(Project);
        return channel;
    }

    public DeviceConfig AddDevice(DeviceConfig device)
    {
        if (device == null)
            throw new ArgumentNullException(nameof(device));

        if (string.IsNullOrWhiteSpace(device.Id))
            device.Id = Guid.NewGuid().ToString("N");
        if (device.Connection != null)
            device.Connection.Protocol = device.Protocol;

        Project.Devices.Add(device);
        ProjectConfigStore.Normalize(Project);
        return device;
    }

    public DeviceConfig UpdateDevice(string deviceId, DeviceConfig input)
    {
        DeviceConfig device = Require(FindDevice(deviceId), "Device was not found.");
        device.ChannelId = input.ChannelId;
        device.Name = input.Name;
        device.Enabled = input.Enabled;
        device.Protocol = input.Protocol;
        device.Connection = input.Connection;
        device.DefaultScanRateMs = input.DefaultScanRateMs;
        device.FailureRetryDelayMs = input.FailureRetryDelayMs;
        device.MaxFailureRetryDelayMs = input.MaxFailureRetryDelayMs;
        if (device.Connection != null)
            device.Connection.Protocol = device.Protocol;
        ProjectConfigStore.Normalize(Project);
        return device;
    }

    public DeviceConfig DeleteDevice(string deviceId)
    {
        DeviceConfig device = Require(FindDevice(deviceId), "Device was not found.");
        Project.Devices.Remove(device);
        ProjectConfigStore.Normalize(Project);
        return device;
    }

    public IList<GroupConfig> GetDeviceGroups(string deviceId)
    {
        return Require(FindDevice(deviceId), "Device was not found.").Groups;
    }

    public GroupConfig AddGroup(string deviceId, GroupConfig input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        DeviceConfig device = Require(FindDevice(deviceId), "Device was not found.");
        if (string.IsNullOrWhiteSpace(input.Id))
            input.Id = Guid.NewGuid().ToString("N");
        input.DeviceId = device.Id;
        device.Groups.Add(input);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public GroupConfig UpdateGroup(string groupId, GroupConfig input)
    {
        GroupConfig group = Require(FindGroup(groupId), "Group was not found.");
        group.Name = input.Name;
        group.Enabled = input.Enabled;
        group.ScanRateMs = input.ScanRateMs;
        ProjectConfigStore.Normalize(Project);
        return group;
    }

    public GroupConfig DeleteGroup(string groupId)
    {
        DeviceConfig device = Require(FindDeviceByGroup(groupId), "Group was not found.");
        GroupConfig group = Require(FindGroup(groupId), "Group was not found.");
        device.Groups.Remove(group);
        ProjectConfigStore.Normalize(Project);
        return group;
    }

    public IList<TagConfig> GetDeviceTags(string deviceId)
    {
        return Require(FindDevice(deviceId), "Device was not found.").Tags;
    }

    public TagConfig AddDeviceTag(string deviceId, TagConfig input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        DeviceConfig device = Require(FindDevice(deviceId), "Device was not found.");
        if (string.IsNullOrWhiteSpace(input.Id))
            input.Id = Guid.NewGuid().ToString("N");
        input.DeviceId = device.Id;
        input.GroupId = string.Empty;
        device.Tags.Add(input);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public IList<TagConfig> GetGroupTags(string groupId)
    {
        return Require(FindGroup(groupId), "Group was not found.").Tags;
    }

    public TagConfig AddGroupTag(string groupId, TagConfig input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        GroupConfig group = Require(FindGroup(groupId), "Group was not found.");
        DeviceConfig device = Require(FindDeviceByGroup(groupId), "Group was not found.");
        if (string.IsNullOrWhiteSpace(input.Id))
            input.Id = Guid.NewGuid().ToString("N");
        input.DeviceId = device.Id;
        input.GroupId = group.Id;
        group.Tags.Add(input);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public TagConfig UpdateTag(string tagId, TagConfig input)
    {
        TagConfig tag = Require(FindTag(tagId), "Tag was not found.");
        ApplyTag(tag, input);
        ProjectConfigStore.Normalize(Project);
        return tag;
    }

    public TagConfig DeleteTag(string tagId)
    {
        TagConfig tag = Require(RemoveTag(tagId), "Tag was not found.");
        ProjectConfigStore.Normalize(Project);
        return tag;
    }

    public EdgeRuleConfig AddRule(EdgeRuleConfig input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        if (string.IsNullOrWhiteSpace(input.Id))
            input.Id = Guid.NewGuid().ToString("N");
        Project.Rules.Add(input);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public EdgeRuleConfig UpdateRule(string ruleId, EdgeRuleConfig input)
    {
        EdgeRuleConfig rule = Require(FindRule(ruleId), "Rule was not found.");
        int index = Project.Rules.IndexOf(rule);
        input.Id = rule.Id;
        Project.Rules[index] = input;
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public EdgeRuleConfig DeleteRule(string ruleId)
    {
        EdgeRuleConfig rule = Require(FindRule(ruleId), "Rule was not found.");
        Project.Rules.Remove(rule);
        ProjectConfigStore.Normalize(Project);
        return rule;
    }

    public FlowRuleDefinition AddFlowRule(FlowRuleDefinition input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        if (string.IsNullOrWhiteSpace(input.Id))
            input.Id = Guid.NewGuid().ToString("N");
        if (input.Version <= 0)
            input.Version = 1;
        input.LifecycleState = string.Equals(input.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase)
            ? FlowRuleLifecycleStates.Archived
            : FlowRuleLifecycleStates.Published;
        input.PublishedVersion = input.Version;
        input.PublishedTime = DateTime.Now;
        input.CreatedTime = input.CreatedTime == DateTime.MinValue ? DateTime.Now : input.CreatedTime;
        input.UpdatedTime = DateTime.Now;
        Project.FlowRules.Add(input);
        FlowRuleCompiler.SyncCompiledRule(Project, input, string.Empty);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public FlowRuleDefinition UpdateFlowRule(string ruleId, FlowRuleDefinition input)
    {
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        FlowRuleDefinition rule = Require(FindFlowRule(ruleId), "Flow rule was not found.");
        string previousCompiledRuleId = rule.CompiledRuleId;
        int index = Project.FlowRules.IndexOf(rule);
        input.Id = rule.Id;
        input.Version = Math.Max(1, rule.Version + 1);
        input.LifecycleState = string.Equals(input.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase)
            ? FlowRuleLifecycleStates.Archived
            : FlowRuleLifecycleStates.Published;
        input.PublishedVersion = input.Version;
        input.PublishedTime = DateTime.Now;
        input.CreatedTime = rule.CreatedTime == DateTime.MinValue ? DateTime.Now : rule.CreatedTime;
        input.UpdatedTime = DateTime.Now;
        Project.FlowRules[index] = input;
        FlowRuleCompiler.SyncCompiledRule(Project, input, previousCompiledRuleId);
        ProjectConfigStore.Normalize(Project);
        return input;
    }

    public FlowRuleDefinition DeleteFlowRule(string ruleId)
    {
        FlowRuleDefinition rule = Require(FindFlowRule(ruleId), "Flow rule was not found.");
        FlowRuleCompiler.RemoveCompiledRule(Project, rule);
        Project.FlowRules.Remove(rule);
        ProjectConfigStore.Normalize(Project);
        return rule;
    }

    public DeviceConfig? FindDevice(string idOrName)
    {
        return Project.Devices.FirstOrDefault(device =>
            string.Equals(device.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(device.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public ChannelConfig? FindChannel(string idOrName)
    {
        return Project.Channels.FirstOrDefault(channel =>
            string.Equals(channel.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(channel.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public DeviceConfig? FindDeviceByGroup(string groupId)
    {
        return Project.Devices.FirstOrDefault(device =>
            device.Groups.Any(group => string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(group.Name, groupId, StringComparison.OrdinalIgnoreCase)));
    }

    public GroupConfig? FindGroup(string idOrName)
    {
        foreach (DeviceConfig device in Project.Devices)
        {
            GroupConfig? group = device.Groups.FirstOrDefault(item =>
                string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
            if (group != null)
                return group;
        }

        return null;
    }

    public TagConfig? FindTag(string idOrName)
    {
        foreach (DeviceConfig device in Project.Devices)
        {
            TagConfig? tag = device.Tags.FirstOrDefault(item =>
                string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
            if (tag != null)
                return tag;

            foreach (GroupConfig group in device.Groups)
            {
                tag = group.Tags.FirstOrDefault(item =>
                    string.Equals(item.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, idOrName, StringComparison.OrdinalIgnoreCase));
                if (tag != null)
                    return tag;
            }
        }

        return null;
    }

    public EdgeRuleConfig? FindRule(string idOrName)
    {
        return Project.Rules.FirstOrDefault(rule =>
            string.Equals(rule.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    public FlowRuleDefinition? FindFlowRule(string idOrName)
    {
        return Project.FlowRules.FirstOrDefault(rule =>
            string.Equals(rule.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rule.Name, idOrName, StringComparison.OrdinalIgnoreCase));
    }

    private TagConfig? RemoveTag(string tagId)
    {
        foreach (DeviceConfig device in Project.Devices)
        {
            TagConfig? tag = device.Tags.FirstOrDefault(item =>
                string.Equals(item.Id, tagId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, tagId, StringComparison.OrdinalIgnoreCase));
            if (tag != null)
            {
                device.Tags.Remove(tag);
                return tag;
            }

            foreach (GroupConfig group in device.Groups)
            {
                tag = group.Tags.FirstOrDefault(item =>
                    string.Equals(item.Id, tagId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.Name, tagId, StringComparison.OrdinalIgnoreCase));
                if (tag != null)
                {
                    group.Tags.Remove(tag);
                    return tag;
                }
            }
        }

        return null;
    }

    private static T Require<T>(T? value, string message) where T : class
    {
        return value ?? throw new InvalidOperationException(message);
    }

    private static void ApplyTag(TagConfig target, TagConfig input)
    {
        string deviceId = target.DeviceId;
        string groupId = target.GroupId;
        target.Name = input.Name;
        target.Address = input.Address;
        target.MeterAddress = input.MeterAddress;
        target.MeterDataIdentifier = input.MeterDataIdentifier;
        target.MeterType = input.MeterType;
        target.DataType = input.DataType;
        target.ElementCount = input.ElementCount;
        target.ElementOffset = input.ElementOffset;
        target.Enabled = input.Enabled;
        target.MqttPublishEnabled = input.MqttPublishEnabled;
        target.AccessMode = input.AccessMode;
        target.ScanRateMs = input.ScanRateMs;
        target.FailureRetryDelayMs = input.FailureRetryDelayMs;
        target.Unit = input.Unit;
        target.PointCode = input.PointCode;
        target.AssetPath = input.AssetPath;
        target.BusinessType = input.BusinessType;
        target.Source = input.Source;
        target.Precision = input.Precision;
        target.Scaling = input.Scaling;
        target.Cleaning = input.Cleaning;
        target.Alarm = input.Alarm;
        target.Description = input.Description;
        target.DeviceId = deviceId;
        target.GroupId = groupId;
    }
}
