/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationVersionInfo
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
using System;

namespace IPC.Gateway.Core.Gateway
{
    
    
    
    
    
    
    
    
    
    public sealed class GatewayConfigurationVersionInfo
    {
        public GatewayConfigurationVersionInfo()
        {
            Id = string.Empty;
            ConfigType = string.Empty;
            Source = string.Empty;
            Description = string.Empty;
        }

        public string Id { get; set; }
        public string ConfigType { get; set; }
        public int Version { get; set; }
        public bool Active { get; set; }
        public DateTime CreatedTime { get; set; }
        public string Source { get; set; }
        public string Description { get; set; }
    }
}
