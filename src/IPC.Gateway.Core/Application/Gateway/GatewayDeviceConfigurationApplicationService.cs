/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayDeviceConfigurationApplicationService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Application.Gateway
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
using IPC.Gateway.Core.Domain.Gateway;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public sealed class GatewayDeviceConfigurationApplicationService : IGatewayDeviceConfigurationApplicationService
{
    private readonly GatewayCoreService _gateway;

    public GatewayDeviceConfigurationApplicationService(GatewayCoreService gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public IList<ChannelConfig> GetChannels() => _gateway.CurrentProject.Channels;

    public ChannelConfig AddChannel(ChannelConfig channel)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.AddChannel(channel);
            return result;
        });
    }

    public Task<ChannelConfig> AddChannelAsync(ChannelConfig channel)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.AddChannel(channel);
            return result;
        });
    }

    public ChannelConfig UpdateChannel(string channelId, ChannelConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.UpdateChannel(channelId, input);
            return result;
        });
    }

    public Task<ChannelConfig> UpdateChannelAsync(string channelId, ChannelConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.UpdateChannel(channelId, input);
            return result;
        });
    }

    public ChannelConfig DeleteChannel(string channelId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.DeleteChannel(channelId);
            return result;
        });
    }

    public Task<ChannelConfig> DeleteChannelAsync(string channelId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            ChannelConfig result = aggregate.DeleteChannel(channelId);
            return result;
        });
    }

    public IList<DeviceConfig> GetDevices() => _gateway.CurrentProject.Devices;

    public DeviceConfig AddDevice(DeviceConfig device)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig result = aggregate.AddDevice(device);
            return result;
        });
    }

    public Task<DeviceConfig> AddDeviceAsync(DeviceConfig device)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig result = aggregate.AddDevice(device);
            return result;
        });
    }

    public DeviceConfig UpdateDevice(string deviceId, DeviceConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig current = aggregate.FindDevice(deviceId) ?? throw new InvalidOperationException("Device was not found.");
            if (DeviceConfigComparer.IsSameDeviceUpdate(current, input))
                return current;

            DeviceConfig result = aggregate.UpdateDevice(deviceId, input);
            return result;
        });
    }

    public Task<DeviceConfig> UpdateDeviceAsync(string deviceId, DeviceConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig current = aggregate.FindDevice(deviceId) ?? throw new InvalidOperationException("Device was not found.");
            if (DeviceConfigComparer.IsSameDeviceUpdate(current, input))
                return current;

            DeviceConfig result = aggregate.UpdateDevice(deviceId, input);
            return result;
        });
    }

    public DeviceConfig DeleteDevice(string deviceId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig result = aggregate.DeleteDevice(deviceId);
            return result;
        });
    }

    public Task<DeviceConfig> DeleteDeviceAsync(string deviceId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            DeviceConfig result = aggregate.DeleteDevice(deviceId);
            return result;
        });
    }

    public IList<GroupConfig> GetDeviceGroups(string deviceId)
    {
        return LoadAggregate().GetDeviceGroups(deviceId);
    }

    public GroupConfig AddGroup(string deviceId, GroupConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.AddGroup(deviceId, input);
            return result;
        });
    }

    public Task<GroupConfig> AddGroupAsync(string deviceId, GroupConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.AddGroup(deviceId, input);
            return result;
        });
    }

    public GroupConfig UpdateGroup(string groupId, GroupConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.UpdateGroup(groupId, input);
            return result;
        });
    }

    public Task<GroupConfig> UpdateGroupAsync(string groupId, GroupConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.UpdateGroup(groupId, input);
            return result;
        });
    }

    public GroupConfig DeleteGroup(string groupId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.DeleteGroup(groupId);
            return result;
        });
    }

    public Task<GroupConfig> DeleteGroupAsync(string groupId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            GroupConfig result = aggregate.DeleteGroup(groupId);
            return result;
        });
    }

    public IList<TagConfig> GetDeviceTags(string deviceId)
    {
        return LoadAggregate().GetDeviceTags(deviceId);
    }

    public TagConfig AddDeviceTag(string deviceId, TagConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.AddDeviceTag(deviceId, input);
            return result;
        });
    }

    public Task<TagConfig> AddDeviceTagAsync(string deviceId, TagConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.AddDeviceTag(deviceId, input);
            return result;
        });
    }

    public IList<TagConfig> GetGroupTags(string groupId)
    {
        return LoadAggregate().GetGroupTags(groupId);
    }

    public TagConfig AddGroupTag(string groupId, TagConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.AddGroupTag(groupId, input);
            return result;
        });
    }

    public Task<TagConfig> AddGroupTagAsync(string groupId, TagConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.AddGroupTag(groupId, input);
            return result;
        });
    }

    public TagConfig UpdateTag(string tagId, TagConfig input)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.UpdateTag(tagId, input);
            return result;
        });
    }

    public Task<TagConfig> UpdateTagAsync(string tagId, TagConfig input)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.UpdateTag(tagId, input);
            return result;
        });
    }

    public TagConfig DeleteTag(string tagId)
    {
        return _gateway.MutateDeviceProject(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.DeleteTag(tagId);
            return result;
        });
    }

    public Task<TagConfig> DeleteTagAsync(string tagId)
    {
        return _gateway.MutateDeviceProjectAsync(project =>
        {
            GatewayProjectAggregate aggregate = new GatewayProjectAggregate(project);
            TagConfig result = aggregate.DeleteTag(tagId);
            return result;
        });
    }

    private GatewayProjectAggregate LoadAggregate()
    {
        return new GatewayProjectAggregate(_gateway.CurrentProject);
    }

    private void Save(GatewayProjectAggregate aggregate)
    {
        _gateway.ApplyDeviceProject(aggregate.Project);
    }

    private Task SaveAsync(GatewayProjectAggregate aggregate)
    {
        return _gateway.ApplyDeviceProjectAsync(aggregate.Project);
    }
}
