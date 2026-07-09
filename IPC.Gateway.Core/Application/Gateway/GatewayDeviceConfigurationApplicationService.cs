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

    public IList<DeviceConfig> GetDevices() => _gateway.CurrentProject.Devices;

    public DeviceConfig AddDevice(DeviceConfig device)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig result = aggregate.AddDevice(device);
        Save(aggregate);
        return result;
    }

    public async Task<DeviceConfig> AddDeviceAsync(DeviceConfig device)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig result = aggregate.AddDevice(device);
        await SaveAsync(aggregate);
        return result;
    }

    public DeviceConfig UpdateDevice(string deviceId, DeviceConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig current = aggregate.FindDevice(deviceId) ?? throw new InvalidOperationException("Device was not found.");
        if (DeviceConfigComparer.IsSameDeviceUpdate(current, input))
            return current;

        DeviceConfig result = aggregate.UpdateDevice(deviceId, input);
        Save(aggregate);
        return result;
    }

    public async Task<DeviceConfig> UpdateDeviceAsync(string deviceId, DeviceConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig current = aggregate.FindDevice(deviceId) ?? throw new InvalidOperationException("Device was not found.");
        if (DeviceConfigComparer.IsSameDeviceUpdate(current, input))
            return current;

        DeviceConfig result = aggregate.UpdateDevice(deviceId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public DeviceConfig DeleteDevice(string deviceId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig result = aggregate.DeleteDevice(deviceId);
        Save(aggregate);
        return result;
    }

    public async Task<DeviceConfig> DeleteDeviceAsync(string deviceId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        DeviceConfig result = aggregate.DeleteDevice(deviceId);
        await SaveAsync(aggregate);
        return result;
    }

    public IList<GroupConfig> GetDeviceGroups(string deviceId)
    {
        return LoadAggregate().GetDeviceGroups(deviceId);
    }

    public GroupConfig AddGroup(string deviceId, GroupConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.AddGroup(deviceId, input);
        Save(aggregate);
        return result;
    }

    public async Task<GroupConfig> AddGroupAsync(string deviceId, GroupConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.AddGroup(deviceId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public GroupConfig UpdateGroup(string groupId, GroupConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.UpdateGroup(groupId, input);
        Save(aggregate);
        return result;
    }

    public async Task<GroupConfig> UpdateGroupAsync(string groupId, GroupConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.UpdateGroup(groupId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public GroupConfig DeleteGroup(string groupId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.DeleteGroup(groupId);
        Save(aggregate);
        return result;
    }

    public async Task<GroupConfig> DeleteGroupAsync(string groupId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        GroupConfig result = aggregate.DeleteGroup(groupId);
        await SaveAsync(aggregate);
        return result;
    }

    public IList<TagConfig> GetDeviceTags(string deviceId)
    {
        return LoadAggregate().GetDeviceTags(deviceId);
    }

    public TagConfig AddDeviceTag(string deviceId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.AddDeviceTag(deviceId, input);
        Save(aggregate);
        return result;
    }

    public async Task<TagConfig> AddDeviceTagAsync(string deviceId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.AddDeviceTag(deviceId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public IList<TagConfig> GetGroupTags(string groupId)
    {
        return LoadAggregate().GetGroupTags(groupId);
    }

    public TagConfig AddGroupTag(string groupId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.AddGroupTag(groupId, input);
        Save(aggregate);
        return result;
    }

    public async Task<TagConfig> AddGroupTagAsync(string groupId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.AddGroupTag(groupId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public TagConfig UpdateTag(string tagId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.UpdateTag(tagId, input);
        Save(aggregate);
        return result;
    }

    public async Task<TagConfig> UpdateTagAsync(string tagId, TagConfig input)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.UpdateTag(tagId, input);
        await SaveAsync(aggregate);
        return result;
    }

    public TagConfig DeleteTag(string tagId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.DeleteTag(tagId);
        Save(aggregate);
        return result;
    }

    public async Task<TagConfig> DeleteTagAsync(string tagId)
    {
        GatewayProjectAggregate aggregate = LoadAggregate();
        TagConfig result = aggregate.DeleteTag(tagId);
        await SaveAsync(aggregate);
        return result;
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
