/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeStateSnapshot
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway;

public sealed class GatewayRuntimeStateSnapshot
{
    public GatewayRuntimeStateSnapshot()
    {
        Devices = new List<DeviceRuntimeStatus>();
        Tags = new List<TagValueSnapshot>();
        RecentErrors = new List<RuntimeErrorDetail>();
        UpdatedTime = DateTime.MinValue;
    }

    public IList<DeviceRuntimeStatus> Devices { get; set; }
    public IList<TagValueSnapshot> Tags { get; set; }
    public IList<RuntimeErrorDetail> RecentErrors { get; set; }
    public DateTime UpdatedTime { get; set; }
}
