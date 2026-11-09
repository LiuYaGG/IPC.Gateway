/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayRuntimeStatus
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
using System.Collections.Generic;
using IPC.EdgeGateway;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Gateway.Core.Gateway
{
    
    
    
    
    
    
    
    
    
    public sealed class GatewayRuntimeStatus
    {
        public GatewayRuntimeStatus()
        {
            ProjectId = string.Empty;
            ProjectName = string.Empty;
            ProjectPath = string.Empty;
            ConfigurationStore = string.Empty;
            ConfigValidation = new ProjectConfigValidationResult();
            Devices = new List<DeviceRuntimeStatus>();
            Tags = new List<TagValueSnapshot>();
            RecentErrors = new List<RuntimeErrorDetail>();
            Mqtt = new MqttGatewayStatus();
            OpcUa = new OpcUaServerStatus();
            History = new LocalHistoryStats();
            FlowRuleEngine = new EdgeRuleEngineStatus();
            Scheduler = new RuntimeSchedulerStatus();
            System = new SystemResourceStatus();
            StartedTime = DateTime.MinValue;
            LastReloadTime = DateTime.MinValue;
        }

        public bool IsRunning { get; set; }
        public string ProjectId { get; set; }
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        public string ConfigurationStore { get; set; }
        public int DeviceCount { get; set; }
        public int GroupCount { get; set; }
        public int TagCount { get; set; }
        public int EnabledDeviceCount { get; set; }
        public int OnlineDeviceCount { get; set; }
        public int GoodTagCount { get; set; }
        public int BadTagCount { get; set; }
        public int NoDataTagCount { get; set; }
        public DateTime StartedTime { get; set; }
        public DateTime LastReloadTime { get; set; }
        public ProjectConfigValidationResult ConfigValidation { get; set; }
        public IList<DeviceRuntimeStatus> Devices { get; set; }
        public IList<TagValueSnapshot> Tags { get; set; }
        public IList<RuntimeErrorDetail> RecentErrors { get; set; }
        public MqttGatewayStatus Mqtt { get; set; }
        public OpcUaServerStatus OpcUa { get; set; }
        public LocalHistoryStats History { get; set; }
        public EdgeRuleEngineStatus FlowRuleEngine { get; set; }
        public RuntimeSchedulerStatus Scheduler { get; set; }
        public SystemResourceStatus System { get; set; }
    }
}
