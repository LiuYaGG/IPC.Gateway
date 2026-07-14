/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：DeviceConfig
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Configuration
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
using System.Collections.Generic;
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public sealed class DeviceConfig
    {
        public DeviceConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            ChannelId = string.Empty;
            Name = "Device";
            Enabled = true;
            Protocol = PlcProtocol.ModbusTcp;
            Connection = new PlcConnectionOptions();
            DefaultScanRateMs = 1000;
            FailureRetryDelayMs = 1000;
            MaxFailureRetryDelayMs = 30000;
            Tags = new List<TagConfig>();
            Groups = new List<GroupConfig>();
        }

        public string Id { get; set; }
        public string ChannelId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public PlcProtocol Protocol { get; set; }
        public PlcConnectionOptions Connection { get; set; }
        public int DefaultScanRateMs { get; set; }
        public int FailureRetryDelayMs { get; set; }
        public int MaxFailureRetryDelayMs { get; set; }
        public List<TagConfig> Tags { get; set; }
        public List<GroupConfig> Groups { get; set; }
    }
}
