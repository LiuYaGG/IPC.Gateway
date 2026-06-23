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
    IList<DeviceConfig> GetDevices();
    DeviceConfig AddDevice(DeviceConfig device);
    DeviceConfig UpdateDevice(string deviceId, DeviceConfig input);
    DeviceConfig DeleteDevice(string deviceId);
    IList<GroupConfig> GetDeviceGroups(string deviceId);
    GroupConfig AddGroup(string deviceId, GroupConfig input);
    GroupConfig UpdateGroup(string groupId, GroupConfig input);
    GroupConfig DeleteGroup(string groupId);
    IList<TagConfig> GetDeviceTags(string deviceId);
    TagConfig AddDeviceTag(string deviceId, TagConfig input);
    IList<TagConfig> GetGroupTags(string groupId);
    TagConfig AddGroupTag(string groupId, TagConfig input);
    TagConfig UpdateTag(string tagId, TagConfig input);
    TagConfig DeleteTag(string tagId);
}
