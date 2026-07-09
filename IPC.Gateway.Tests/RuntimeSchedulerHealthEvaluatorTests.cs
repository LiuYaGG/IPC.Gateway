/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeSchedulerHealthEvaluatorTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

public sealed class RuntimeSchedulerHealthEvaluatorTests
{
    [Fact]
    public void Evaluate_NoPressure_ReturnsHealthy()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 0,
                QueueLimit = 100,
                AvailableWorkers = 4
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Healthy, health.Status);
        Assert.Contains("accepting", health.Message);
    }

    [Fact]
    public void Evaluate_QueueFullAndWorkersBusy_ReturnsUnhealthy()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 10,
                QueueLimit = 10,
                AvailableWorkers = 0
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Unhealthy, health.Status);
        Assert.Contains("queue is full", health.Message);
    }

    [Fact]
    public void Evaluate_QueuePressureAndRecentTimeouts_ReturnsDegradedWithReasons()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            TotalSlow = 2,
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 8,
                QueueLimit = 10,
                AvailableWorkers = 1,
                RejectedCount = 3
            },
            Timeout = new RuntimeTimeoutStats
            {
                PollTimeoutCount = 1,
                ReadTimeoutCount = 4,
                RecentPollTimeoutCount = 1,
                RecentReadTimeoutCount = 4,
                TimeoutWindowSeconds = 300
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Degraded, health.Status);
        Assert.Contains("near capacity", health.Message);
        Assert.Contains("timeout", health.Message);
        Assert.DoesNotContain("rejected", health.Message);
        Assert.DoesNotContain("slow poll", health.Message);
    }

    [Fact]
    public void Evaluate_CumulativeTimeoutsWithoutRecentTimeouts_ReturnsHealthy()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 0,
                QueueLimit = 100,
                AvailableWorkers = 4
            },
            Timeout = new RuntimeTimeoutStats
            {
                PollTimeoutCount = 10,
                ReadTimeoutCount = 20,
                RecentPollTimeoutCount = 0,
                RecentReadTimeoutCount = 0,
                TimeoutWindowSeconds = 300
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Healthy, health.Status);
    }

    [Fact]
    public void Evaluate_CumulativeSchedulerCountersWithoutCurrentPressure_ReturnsHealthy()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            TotalSlow = 5,
            TotalFailed = 2,
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 0,
                QueueLimit = 100,
                AvailableWorkers = 4,
                RejectedCount = 1,
                BackpressureThrottledCount = 3,
                RateLimitedCount = 2
            },
            Timeout = new RuntimeTimeoutStats
            {
                PollTimeoutCount = 10,
                ReadTimeoutCount = 20,
                RecentPollTimeoutCount = 0,
                RecentReadTimeoutCount = 0,
                TimeoutWindowSeconds = 300
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Healthy, health.Status);
    }

    [Fact]
    public void Evaluate_BackpressureAndRateLimit_ReturnsDegradedWithReasons()
    {
        RuntimeSchedulerStatus status = new RuntimeSchedulerStatus
        {
            Queue = new RuntimePollingQueueStatus
            {
                PendingCount = 8,
                QueueLimit = 10,
                HighWatermark = 8,
                LowWatermark = 5,
                AvailableWorkers = 1,
                BackpressureActive = true,
                BackpressureThrottledCount = 2,
                RateLimitedCount = 3
            }
        };

        RuntimeSchedulerHealth health = RuntimeSchedulerHealthEvaluator.Evaluate(status);

        Assert.Equal(RuntimeSchedulerHealthEvaluator.Degraded, health.Status);
        Assert.Contains("backpressure", health.Message);
        Assert.Contains("near capacity", health.Message);
        Assert.DoesNotContain("rate limited", health.Message);
    }
}
