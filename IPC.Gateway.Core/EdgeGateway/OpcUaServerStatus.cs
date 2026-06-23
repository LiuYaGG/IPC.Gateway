/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：OpcUaServerStatus
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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

namespace IPC.EdgeGateway
{
    
    
    
    public sealed class OpcUaServerStatus
    {
        public OpcUaServerStatus()
        {
            ApplicationName = string.Empty;
            EndpointUrl = string.Empty;
            NamespaceUri = string.Empty;
            LastError = string.Empty;
            LastMessage = string.Empty;
            StartedTime = DateTime.MinValue;
            LastReloadTime = DateTime.MinValue;
            LastValueUpdateTime = DateTime.MinValue;
        }

        public bool Enabled { get; set; }
        public bool IsRunning { get; set; }
        public string ApplicationName { get; set; }
        public string EndpointUrl { get; set; }
        public string NamespaceUri { get; set; }
        public int DeviceNodeCount { get; set; }
        public int GroupNodeCount { get; set; }
        public int TagNodeCount { get; set; }
        public long ValueUpdateCount { get; set; }
        public DateTime StartedTime { get; set; }
        public DateTime LastReloadTime { get; set; }
        public DateTime LastValueUpdateTime { get; set; }
        public string LastError { get; set; }
        public string LastMessage { get; set; }

        public OpcUaServerStatus Clone()
        {
            return (OpcUaServerStatus)MemberwiseClone();
        }
    }
}
