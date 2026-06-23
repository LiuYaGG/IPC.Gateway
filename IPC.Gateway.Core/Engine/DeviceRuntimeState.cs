/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：DeviceRuntimeState
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Engine
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
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine
{
    
    
    
    
    
    
    
    
    
    internal sealed class DeviceRuntimeState
    {
        public DeviceRuntimeState(DeviceConfig config, CircuitBreakerOptions protocolCircuitBreakerOptions)
        {
            Config = config;
            SyncRoot = new object();
            ProtocolCircuitBreaker = new CircuitBreaker(
                "ProtocolDriver:" + (config == null ? string.Empty : config.Name ?? string.Empty),
                protocolCircuitBreakerOptions);
            NextPollUtc = DateTime.MinValue;
            NextReconnectUtc = DateTime.MinValue;
            LastReconnectDelayMs = 0;
            LastError = string.Empty;
            LastConnectionError = string.Empty;
            LastConnectionErrorTime = DateTime.MinValue;
            LastTaskStatus = "Idle";
            LastTaskError = string.Empty;
        }

        public DeviceConfig Config { get; private set; }
        public object SyncRoot { get; private set; }
        public CircuitBreaker ProtocolCircuitBreaker { get; private set; }
        public IPlcClient? Client { get; set; }
        public bool IsPolling { get; set; }
        public bool IsQueued { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime NextPollUtc { get; set; }
        public DateTime NextReconnectUtc { get; set; }
        public int LastReconnectDelayMs { get; set; }
        public string LastError { get; set; }
        public string LastConnectionError { get; set; }
        public DateTime LastConnectionErrorTime { get; set; }
        public long TotalReads { get; set; }
        public long SuccessfulReads { get; set; }
        public long FailedReads { get; set; }
        public DateTime LastPollTime { get; set; }
        public DateTime LastSuccessTime { get; set; }
        public DateTime LastFailureTime { get; set; }
        public long CurrentTaskId { get; set; }
        public DateTime CurrentTaskQueuedUtc { get; set; }
        public DateTime CurrentTaskStartedUtc { get; set; }
        public DateTime CurrentTaskFinishedUtc { get; set; }
        public long LastTaskDurationMs { get; set; }
        public string LastTaskStatus { get; set; }
        public string LastTaskError { get; set; }
        public long SlowPollCount { get; set; }
        public long TimeoutCount { get; set; }
    }
}
