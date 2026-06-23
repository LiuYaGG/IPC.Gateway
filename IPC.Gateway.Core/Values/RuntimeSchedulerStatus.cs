/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Values
* 项目描述 ：
* 类 名 称 ：RuntimeSchedulerStatus
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
using System.Collections.Generic;

namespace IPC.Runtime.Values
{
    public sealed class RuntimeSchedulerStatus
    {
        public RuntimeSchedulerStatus()
        {
            IsolationStrategy = string.Empty;
            HealthStatus = "Unknown";
            HealthMessage = string.Empty;
            Queue = new RuntimePollingQueueStatus();
            Timeout = new RuntimeTimeoutStats();
            Tasks = new List<RuntimePollingTaskStatus>();
        }

        public string IsolationStrategy { get; set; }
        public string HealthStatus { get; set; }
        public string HealthMessage { get; set; }
        public int MaxConcurrentDevicePolls { get; set; }
        public int SchedulerIntervalMs { get; set; }
        public bool BackpressureEnabled { get; set; }
        public bool BackpressureActive { get; set; }
        public int QueueHighWatermark { get; set; }
        public int QueueLowWatermark { get; set; }
        public int BackpressureDelayMs { get; set; }
        public int MaxDevicePollsQueuedPerSchedulerTick { get; set; }
        public int SlowPollThresholdMs { get; set; }
        public int PollTimeoutMs { get; set; }
        public long TotalQueued { get; set; }
        public long TotalStarted { get; set; }
        public long TotalCompleted { get; set; }
        public long TotalFailed { get; set; }
        public long TotalSlow { get; set; }
        public long TotalBackpressureThrottled { get; set; }
        public long TotalRateLimited { get; set; }
        public RuntimePollingQueueStatus Queue { get; set; }
        public RuntimeTimeoutStats Timeout { get; set; }
        public IList<RuntimePollingTaskStatus> Tasks { get; set; }
    }

    public sealed class RuntimePollingQueueStatus
    {
        public int PendingCount { get; set; }
        public int RunningCount { get; set; }
        public int QueueLimit { get; set; }
        public int HighWatermark { get; set; }
        public int LowWatermark { get; set; }
        public double UtilizationPercent { get; set; }
        public bool BackpressureActive { get; set; }
        public int AvailableWorkers { get; set; }
        public long RejectedCount { get; set; }
        public long BackpressureThrottledCount { get; set; }
        public long RateLimitedCount { get; set; }
        public int MaxObservedPendingCount { get; set; }
        public DateTime LastBackpressureTime { get; set; }
        public string LastBackpressureMessage { get; set; } = string.Empty;
    }

    public sealed class RuntimeTimeoutStats
    {
        public long PollTimeoutCount { get; set; }
        public long ReadTimeoutCount { get; set; }
        public DateTime LastTimeoutTime { get; set; }
        public string LastTimeoutDeviceName { get; set; } = string.Empty;
        public string LastTimeoutMessage { get; set; } = string.Empty;
    }

    public sealed class RuntimePollingTaskStatus
    {
        public RuntimePollingTaskStatus()
        {
            DeviceId = string.Empty;
            DeviceName = string.Empty;
            Status = string.Empty;
            LastError = string.Empty;
        }

        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public long TaskId { get; set; }
        public string Status { get; set; }
        public bool IsQueued { get; set; }
        public bool IsRunning { get; set; }
        public DateTime QueuedTime { get; set; }
        public DateTime StartedTime { get; set; }
        public DateTime FinishedTime { get; set; }
        public long LastDurationMs { get; set; }
        public long SlowPollCount { get; set; }
        public long TimeoutCount { get; set; }
        public string LastError { get; set; }
    }
}
