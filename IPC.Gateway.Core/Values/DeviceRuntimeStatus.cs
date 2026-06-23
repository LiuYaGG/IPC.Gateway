/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Values
* 项目描述 ：
* 类 名 称 ：DeviceRuntimeStatus
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Values
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
using IPC.Gateway.Core.Resilience;

namespace IPC.Runtime.Values
{
    
    
    
    
    
    
    
    
    
    public sealed class DeviceRuntimeStatus
    {
        public DeviceRuntimeStatus()
        {
            DeviceId = string.Empty;
            DeviceName = string.Empty;
            Protocol = string.Empty;
            Status = "Unknown";
            LastTaskStatus = string.Empty;
            LastError = string.Empty;
            ProtocolCircuitBreaker = new CircuitBreakerStatus { Name = "ProtocolDriver", Enabled = true };
        }

        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string Protocol { get; set; }
        public bool Enabled { get; set; }
        public bool IsConnected { get; set; }
        public bool IsPolling { get; set; }
        public bool IsQueued { get; set; }
        public string Status { get; set; }
        public int ConsecutiveFailures { get; set; }
        public long TotalReads { get; set; }
        public long SuccessfulReads { get; set; }
        public long FailedReads { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastPollTime { get; set; }
        public DateTime LastSuccessTime { get; set; }
        public DateTime LastFailureTime { get; set; }
        public DateTime NextReconnectTime { get; set; }
        public int LastReconnectDelayMs { get; set; }
        public DateTime NextPollTime { get; set; }
        public long CurrentTaskId { get; set; }
        public string LastTaskStatus { get; set; }
        public long LastTaskDurationMs { get; set; }
        public long SlowPollCount { get; set; }
        public long TimeoutCount { get; set; }
        public string LastError { get; set; }
        public CircuitBreakerStatus ProtocolCircuitBreaker { get; set; }
    }
}
