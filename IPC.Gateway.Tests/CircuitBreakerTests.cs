/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：CircuitBreakerTests
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
using IPC.Gateway.Core.Resilience;

namespace IPC.Gateway.Tests;

public sealed class CircuitBreakerTests
{
    [Fact]
    public void RecordFailure_OpensCircuitAfterThreshold()
    {
        CircuitBreaker breaker = new CircuitBreaker(
            "test",
            new CircuitBreakerOptions
            {
                FailureThreshold = 2,
                SuccessThreshold = 1,
                BreakDurationSeconds = 30,
                DegradedMode = "Skip"
            });

        breaker.RecordFailure("first");
        Assert.True(breaker.CanExecute());

        breaker.RecordFailure("second");
        Assert.False(breaker.CanExecute());

        CircuitBreakerStatus status = breaker.Snapshot();
        Assert.True(status.IsOpen);
        Assert.Equal("Open", status.State);
        Assert.Equal(2, status.ConsecutiveFailures);
        Assert.Equal(2, status.TotalFailures);
        Assert.Equal(1, status.TotalRejected);
        Assert.Equal("second", status.LastFailureMessage);
    }

    [Fact]
    public void CanExecute_MovesToHalfOpenAndSuccessClosesCircuit()
    {
        CircuitBreaker breaker = new CircuitBreaker(
            "test",
            new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                SuccessThreshold = 1,
                BreakDurationSeconds = 1
            });

        breaker.RecordFailure("down");
        Assert.False(breaker.CanExecute(DateTime.UtcNow));

        Assert.True(breaker.CanExecute(DateTime.UtcNow.AddSeconds(2)));
        Assert.True(breaker.Snapshot().IsHalfOpen);

        breaker.RecordSuccess();

        CircuitBreakerStatus status = breaker.Snapshot();
        Assert.False(status.IsOpen);
        Assert.False(status.IsHalfOpen);
        Assert.Equal("Closed", status.State);
        Assert.Equal(0, status.ConsecutiveFailures);
    }
}
