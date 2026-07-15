/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayWatchdogTests
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
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Watchdog;

namespace IPC.Gateway.Tests;

public sealed class GatewayWatchdogTests
{
    [Fact]
    public void RestartProtection_BlocksRecovery_WhenWindowLimitIsReached()
    {
        GatewayWatchdogOptions options = new GatewayWatchdogOptions
        {
            MaxRecoveriesPerWindow = 2,
            RecoveryWindowMinutes = 10,
            RecoveryCooldownSeconds = 1
        };
        DateTime now = DateTime.UtcNow;
        GatewayRestartProtectionState state = new GatewayRestartProtectionState
        {
            RecoveryAttemptsUtc = new List<DateTime>
            {
                now.AddMinutes(-2),
                now.AddMinutes(-1)
            },
            LastRecoveryUtc = now.AddMinutes(-1)
        };

        GatewayRestartProtectionStatus status = state.ToStatus(now, options);

        Assert.Equal(2, status.RecentRecoveryCount);
        Assert.True(status.RecoveryBlocked);
    }

    [Fact]
    public void Evaluator_RecommendsRecovery_WhenRuntimeIsStopped()
    {
        GatewayWatchdogEvaluator evaluator = new GatewayWatchdogEvaluator(new GatewayWatchdogOptions());
        GatewayRuntimeStatusDto status = new GatewayRuntimeStatusDto
        {
            IsRunning = false
        };

        IList<GatewayWatchdogCheckResult> checks = evaluator.Evaluate(status, DateTime.Now);

        GatewayWatchdogCheckResult runtime = Assert.Single(checks, item => item.Name == "runtime");
        Assert.Equal(GatewayWatchdogStates.Unhealthy, runtime.State);
        Assert.True(runtime.RecoveryRecommended);
    }

    [Fact]
    public void Evaluator_RecommendsRecovery_WhenSchedulerStopsCompletingWork()
    {
        GatewayWatchdogEvaluator evaluator = new GatewayWatchdogEvaluator(new GatewayWatchdogOptions
        {
            RuntimeNoProgressSeconds = 30
        });
        DateTime baseline = DateTime.Now;
        GatewayRuntimeStatusDto status = CreateRunningStatus(totalCompleted: 10, runningCount: 1);

        evaluator.Evaluate(status, baseline);
        IList<GatewayWatchdogCheckResult> checks = evaluator.Evaluate(
            CreateRunningStatus(totalCompleted: 10, runningCount: 1),
            baseline.AddSeconds(45));

        GatewayWatchdogCheckResult scheduler = Assert.Single(checks, item => item.Name == "scheduler");
        Assert.Equal(GatewayWatchdogStates.Unhealthy, scheduler.State);
        Assert.True(scheduler.RecoveryRecommended);
    }

    [Fact]
    public void Evaluator_ResetsProgressBaseline_WhenSchedulerCounterIsReset()
    {
        GatewayWatchdogEvaluator evaluator = new GatewayWatchdogEvaluator(new GatewayWatchdogOptions
        {
            RuntimeNoProgressSeconds = 30
        });
        DateTime baseline = DateTime.Now;

        evaluator.Evaluate(CreateRunningStatus(totalCompleted: 1000, runningCount: 1), baseline);
        IList<GatewayWatchdogCheckResult> resetChecks = evaluator.Evaluate(
            CreateRunningStatus(totalCompleted: 2, runningCount: 1),
            baseline.AddSeconds(45));

        GatewayWatchdogCheckResult resetScheduler = Assert.Single(resetChecks, item => item.Name == "scheduler");
        Assert.Equal(GatewayWatchdogStates.Healthy, resetScheduler.State);
        Assert.False(resetScheduler.RecoveryRecommended);

        IList<GatewayWatchdogCheckResult> stalledChecks = evaluator.Evaluate(
            CreateRunningStatus(totalCompleted: 2, runningCount: 1),
            baseline.AddSeconds(80));
        GatewayWatchdogCheckResult stalledScheduler = Assert.Single(stalledChecks, item => item.Name == "scheduler");
        Assert.Equal(GatewayWatchdogStates.Unhealthy, stalledScheduler.State);
        Assert.True(stalledScheduler.RecoveryRecommended);
    }

    [Fact]
    public async Task RecoveryGate_BlocksOverlap_UntilActiveRecoveryActuallyCompletes()
    {
        GatewayWatchdogRecoveryGate gate = new GatewayWatchdogRecoveryGate();
        TaskCompletionSource recovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(gate.TryEnter());
        Task gateRelease = gate.ReleaseWhenCompleted(recovery.Task);
        Assert.False(gate.TryEnter());

        recovery.SetResult();
        await gateRelease;

        Assert.True(gate.TryEnter());
        gate.Release();
    }

    private static GatewayRuntimeStatusDto CreateRunningStatus(long totalCompleted, int runningCount)
    {
        return new GatewayRuntimeStatusDto
        {
            IsRunning = true,
            EnabledDeviceCount = 1,
            TagCount = 1,
            Scheduler = new RuntimeSchedulerStatusDto
            {
                HealthStatus = GatewayWatchdogStates.Healthy,
                HealthMessage = "Scheduler is healthy.",
                TotalCompleted = totalCompleted,
                Queue = new RuntimePollingQueueStatusDto
                {
                    RunningCount = runningCount
                }
            }
        };
    }
}
