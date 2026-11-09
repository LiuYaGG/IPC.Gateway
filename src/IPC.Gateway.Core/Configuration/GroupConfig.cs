/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：GroupConfig
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

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public sealed class GroupConfig
    {
        public GroupConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            DeviceId = string.Empty;
            Name = "Group";
            Enabled = true;
            ScanRateMs = 1000;
            Tags = new List<TagConfig>();
        }

        public string Id { get; set; }
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public int ScanRateMs { get; set; }
        public List<TagConfig> Tags { get; set; }
    }
}
