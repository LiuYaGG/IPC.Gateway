/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：RuntimeSchedulerHealth
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
using System.Collections.Generic;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    public sealed class RuntimeSchedulerHealth
    {
        public RuntimeSchedulerHealth()
        {
            Status = RuntimeSchedulerHealthEvaluator.Healthy;
            Message = string.Empty;
        }

        public RuntimeSchedulerHealth(string status, string message)
        {
            Status = string.IsNullOrWhiteSpace(status) ? RuntimeSchedulerHealthEvaluator.Healthy : status;
            Message = message ?? string.Empty;
        }

        public string Status { get; set; }
        public string Message { get; set; }
    }

    public static class RuntimeSchedulerHealthEvaluator
    {
        public const string Healthy = "Healthy";
        public const string Degraded = "Degraded";
        public const string Unhealthy = "Unhealthy";

        public static RuntimeSchedulerHealth Evaluate(RuntimeSchedulerStatus status)
        {
            if (status == null)
                return new RuntimeSchedulerHealth(Unhealthy, "Scheduler status is unavailable.");

            RuntimePollingQueueStatus queue = status.Queue ?? new RuntimePollingQueueStatus();
            RuntimeTimeoutStats timeout = status.Timeout ?? new RuntimeTimeoutStats();

            if (IsQueueFull(queue) && queue.AvailableWorkers <= 0)
                return new RuntimeSchedulerHealth(Unhealthy, "Scheduler queue is full and no polling workers are available.");

            List<string> reasons = new List<string>();
            if (queue.BackpressureActive)
                reasons.Add("polling queue backpressure is active (" + queue.PendingCount + "/" + queue.QueueLimit + ")");
            if (IsQueueNearLimit(queue))
                reasons.Add("polling queue is near capacity (" + queue.PendingCount + "/" + queue.QueueLimit + ")");
            if (queue.PendingCount > 0 && queue.AvailableWorkers <= 0)
                reasons.Add("all polling workers are busy with " + queue.PendingCount + " task(s) pending");
            int timeoutWindowSeconds = timeout.TimeoutWindowSeconds <= 0 ? 300 : timeout.TimeoutWindowSeconds;
            string timeoutWindowText = FormatWindow(timeoutWindowSeconds);
            if (timeout.RecentPollTimeoutCount > 0)
                reasons.Add(timeout.RecentPollTimeoutCount + " poll timeout(s) occurred in the last " + timeoutWindowText);
            if (timeout.RecentReadTimeoutCount > 0)
                reasons.Add(timeout.RecentReadTimeoutCount + " tag read timeout(s) occurred in the last " + timeoutWindowText);

            if (reasons.Count > 0)
                return new RuntimeSchedulerHealth(Degraded, string.Join("; ", reasons.ToArray()) + ".");

            return new RuntimeSchedulerHealth(Healthy, "Scheduler is accepting polling work.");
        }

        private static bool IsQueueFull(RuntimePollingQueueStatus queue)
        {
            return queue != null &&
                   queue.QueueLimit > 0 &&
                   queue.PendingCount >= queue.QueueLimit;
        }

        private static bool IsQueueNearLimit(RuntimePollingQueueStatus queue)
        {
            if (queue == null || queue.QueueLimit <= 0 || queue.PendingCount <= 0)
                return false;

            if (queue.HighWatermark > 0)
                return queue.PendingCount >= queue.HighWatermark;

            double utilization = queue.PendingCount * 100D / queue.QueueLimit;
            return utilization >= 80D;
        }

        private static string FormatWindow(int seconds)
        {
            if (seconds > 0 && seconds % 60 == 0)
                return (seconds / 60) + " minute(s)";
            return seconds + " second(s)";
        }
    }
}
