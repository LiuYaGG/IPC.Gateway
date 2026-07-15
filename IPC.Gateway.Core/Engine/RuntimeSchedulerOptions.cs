/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：RuntimeSchedulerOptions
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

namespace IPC.Runtime.Engine
{
    public sealed class RuntimeSchedulerOptions
    {
        public RuntimeSchedulerOptions()
        {
            IsolationStrategy = "SemaphoreLimitedPerDeviceQueue";
            MaxConcurrentDevicePolls = Math.Max(16, Math.Min(64, Environment.ProcessorCount * 8));
            SchedulerIntervalMs = 100;
            DevicePollQueueLimit = 1024;
            BackpressureEnabled = true;
            QueueHighWatermarkPercent = 80;
            QueueLowWatermarkPercent = 50;
            BackpressureDelayMs = 500;
            MaxDevicePollsQueuedPerSchedulerTick = 0;
            ProtocolDriverCircuitBreaker = new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "SkipDevicePoll"
            };
            SlowPollThresholdMs = 5000;
            PollTimeoutMs = 10000;
            DeviceStatusFailureDebounceCount = 3;
            DeviceStatusFailureDebounceMs = 2000;
            DeviceStatusRecoveryDebounceCount = 1;
            DeviceStatusRecoveryDebounceMs = 0;
            TagValueChangedQueueLimit = 100000;
        }

        public string IsolationStrategy { get; set; }
        public int MaxConcurrentDevicePolls { get; set; }
        public int SchedulerIntervalMs { get; set; }
        public int DevicePollQueueLimit { get; set; }
        public bool BackpressureEnabled { get; set; }
        public int QueueHighWatermarkPercent { get; set; }
        public int QueueLowWatermarkPercent { get; set; }
        public int BackpressureDelayMs { get; set; }
        public int MaxDevicePollsQueuedPerSchedulerTick { get; set; }
        public CircuitBreakerOptions ProtocolDriverCircuitBreaker { get; set; }
        public int SlowPollThresholdMs { get; set; }
        public int PollTimeoutMs { get; set; }
        public int DeviceStatusFailureDebounceCount { get; set; }
        public int DeviceStatusFailureDebounceMs { get; set; }
        public int DeviceStatusRecoveryDebounceCount { get; set; }
        public int DeviceStatusRecoveryDebounceMs { get; set; }
        public int TagValueChangedQueueLimit { get; set; }

        public RuntimeSchedulerOptions Normalize()
        {
            int maxConcurrentDevicePolls = Clamp(MaxConcurrentDevicePolls, 1, 256);
            int highWatermarkPercent = Clamp(QueueHighWatermarkPercent, 1, 100);
            int lowWatermarkPercent = Clamp(QueueLowWatermarkPercent, 0, 99);
            if (lowWatermarkPercent >= highWatermarkPercent)
                lowWatermarkPercent = Math.Max(0, highWatermarkPercent - 20);

            int maxQueuedPerTick = MaxDevicePollsQueuedPerSchedulerTick <= 0
                ? Math.Max(1, maxConcurrentDevicePolls * 2)
                : Clamp(MaxDevicePollsQueuedPerSchedulerTick, 1, 100000);

            return new RuntimeSchedulerOptions
            {
                IsolationStrategy = string.IsNullOrWhiteSpace(IsolationStrategy)
                    ? "SemaphoreLimitedPerDeviceQueue"
                    : IsolationStrategy.Trim(),
                MaxConcurrentDevicePolls = maxConcurrentDevicePolls,
                SchedulerIntervalMs = Clamp(SchedulerIntervalMs, 20, 60000),
                DevicePollQueueLimit = Clamp(DevicePollQueueLimit, 1, 100000),
                BackpressureEnabled = BackpressureEnabled,
                QueueHighWatermarkPercent = highWatermarkPercent,
                QueueLowWatermarkPercent = lowWatermarkPercent,
                BackpressureDelayMs = Clamp(BackpressureDelayMs, 20, 600000),
                MaxDevicePollsQueuedPerSchedulerTick = maxQueuedPerTick,
                ProtocolDriverCircuitBreaker = (ProtocolDriverCircuitBreaker ?? new CircuitBreakerOptions()).Normalize(),
                SlowPollThresholdMs = Clamp(SlowPollThresholdMs, 100, 86400000),
                PollTimeoutMs = Clamp(PollTimeoutMs, 100, 86400000),
                DeviceStatusFailureDebounceCount = Clamp(DeviceStatusFailureDebounceCount, 1, 100),
                DeviceStatusFailureDebounceMs = Clamp(DeviceStatusFailureDebounceMs, 0, 600000),
                DeviceStatusRecoveryDebounceCount = Clamp(DeviceStatusRecoveryDebounceCount, 1, 100),
                DeviceStatusRecoveryDebounceMs = Clamp(DeviceStatusRecoveryDebounceMs, 0, 600000),
                TagValueChangedQueueLimit = Clamp(TagValueChangedQueueLimit, 1, 1000000)
            };
        }

        private static int Clamp(int value, int minValue, int maxValue)
        {
            if (value < minValue)
                return minValue;
            if (value > maxValue)
                return maxValue;
            return value;
        }
    }
}
