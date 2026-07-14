/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Application.Gateway
* 项目描述 ：
* 类 名 称 ：IGatewayDeviceConfigurationApplicationService
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
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Application.Gateway;

public interface IGatewayDeviceConfigurationApplicationService
{
    IList<ChannelConfig> GetChannels();
    ChannelConfig AddChannel(ChannelConfig channel);
    Task<ChannelConfig> AddChannelAsync(ChannelConfig channel);
    ChannelConfig UpdateChannel(string channelId, ChannelConfig input);
    Task<ChannelConfig> UpdateChannelAsync(string channelId, ChannelConfig input);
    ChannelConfig DeleteChannel(string channelId);
    Task<ChannelConfig> DeleteChannelAsync(string channelId);
    IList<DeviceConfig> GetDevices();
    DeviceConfig AddDevice(DeviceConfig device);
    Task<DeviceConfig> AddDeviceAsync(DeviceConfig device);
    DeviceConfig UpdateDevice(string deviceId, DeviceConfig input);
    Task<DeviceConfig> UpdateDeviceAsync(string deviceId, DeviceConfig input);
    DeviceConfig DeleteDevice(string deviceId);
    Task<DeviceConfig> DeleteDeviceAsync(string deviceId);
    IList<GroupConfig> GetDeviceGroups(string deviceId);
    GroupConfig AddGroup(string deviceId, GroupConfig input);
    Task<GroupConfig> AddGroupAsync(string deviceId, GroupConfig input);
    GroupConfig UpdateGroup(string groupId, GroupConfig input);
    Task<GroupConfig> UpdateGroupAsync(string groupId, GroupConfig input);
    GroupConfig DeleteGroup(string groupId);
    Task<GroupConfig> DeleteGroupAsync(string groupId);
    IList<TagConfig> GetDeviceTags(string deviceId);
    TagConfig AddDeviceTag(string deviceId, TagConfig input);
    Task<TagConfig> AddDeviceTagAsync(string deviceId, TagConfig input);
    IList<TagConfig> GetGroupTags(string groupId);
    TagConfig AddGroupTag(string groupId, TagConfig input);
    Task<TagConfig> AddGroupTagAsync(string groupId, TagConfig input);
    TagConfig UpdateTag(string tagId, TagConfig input);
    Task<TagConfig> UpdateTagAsync(string tagId, TagConfig input);
    TagConfig DeleteTag(string tagId);
    Task<TagConfig> DeleteTagAsync(string tagId);
}
