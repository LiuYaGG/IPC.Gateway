/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：ProjectConfig
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
    
    
    
    
    
    
    
    
    
    public sealed class ProjectConfig
    {
        public ProjectConfig()
        {
            ProjectId = Guid.NewGuid().ToString("N");
            Name = "IPC Project";
            Devices = new List<DeviceConfig>();
            Rules = new List<EdgeRuleConfig>();
            FlowRules = new List<FlowRuleDefinition>();
        }

        public string ProjectId { get; set; }
        public string Name { get; set; }
        public List<DeviceConfig> Devices { get; set; }
        public List<EdgeRuleConfig> Rules { get; set; }
        public List<FlowRuleDefinition> FlowRules { get; set; }
    }
}
