/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Infrastructure
* 项目描述 ：
* 类 名 称 ：PlcClientFactory
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Infrastructure
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
using System;
using IPC.Plc.Communication.Core;

namespace IPC.Plc.Communication.Infrastructure
{
    public static class PlcClientFactory
    {
        public static IPlcClient Create(PlcConnectionOptions options)
        {
            if (options == null)
                throw new ArgumentNullException("options");

            IPlcClient? client;
            if (PlcDriverPluginRegistry.TryCreateClient(options, out client) && client != null)
                return client;

            if (!string.IsNullOrWhiteSpace(options.DriverId))
                throw new NotSupportedException("PLC protocol driver was not found: " + options.DriverId);

            throw new NotSupportedException("PLC protocol is not supported by a registered driver: " + options.Protocol);
        }
    }
}
