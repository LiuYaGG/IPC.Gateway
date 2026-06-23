/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Infrastructure
* 项目描述 ：
* 类 名 称 ：LegacyPlcDriverPluginAdapter
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
    internal sealed class LegacyPlcDriverPluginAdapter : IProtocolDriver
    {
        private readonly IPlcDriverPlugin _plugin;

        public LegacyPlcDriverPluginAdapter(IPlcDriverPlugin plugin)
        {
            _plugin = plugin ?? throw new ArgumentNullException("plugin");
        }

        public string DriverId
        {
            get { return _plugin.DriverId; }
        }

        public string DisplayName
        {
            get { return _plugin.DisplayName; }
        }

        public PlcProtocol Protocol
        {
            get { return PlcProtocol.Plugin; }
        }

        public bool Supports(PlcConnectionOptions options)
        {
            if (options == null)
                return false;
            return !string.IsNullOrWhiteSpace(options.DriverId) &&
                   string.Equals(options.DriverId.Trim(), DriverId, StringComparison.OrdinalIgnoreCase);
        }

        public IPlcClient CreateClient(PlcConnectionOptions options)
        {
            return _plugin.CreateClient(options);
        }
    }
}
