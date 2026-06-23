/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeReconnectBackoffCalculatorTests
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

namespace IPC.Gateway.Tests;

public sealed class RuntimeReconnectBackoffCalculatorTests
{
    [Fact]
    public void CalculateDelayMs_FirstFailure_UsesBaseDelay()
    {
        int delay = RuntimeReconnectBackoffCalculator.CalculateDelayMs(1, 1000, 30000);

        Assert.Equal(1000, delay);
    }

    [Fact]
    public void CalculateDelayMs_ConsecutiveFailures_DoublesUntilMax()
    {
        Assert.Equal(2000, RuntimeReconnectBackoffCalculator.CalculateDelayMs(2, 1000, 30000));
        Assert.Equal(4000, RuntimeReconnectBackoffCalculator.CalculateDelayMs(3, 1000, 30000));
        Assert.Equal(30000, RuntimeReconnectBackoffCalculator.CalculateDelayMs(10, 1000, 30000));
    }

    [Fact]
    public void CalculateDelayMs_ManyFailures_DoesNotOverflow()
    {
        int delay = RuntimeReconnectBackoffCalculator.CalculateDelayMs(128, 1000, 86400000);

        Assert.Equal(86400000, delay);
    }

    [Fact]
    public void CalculateDelayMs_InvalidInputs_AreClamped()
    {
        int delay = RuntimeReconnectBackoffCalculator.CalculateDelayMs(1, 0, 50);

        Assert.Equal(100, delay);
    }

    [Fact]
    public void CalculateScheduledDelayMs_DefaultJitter_IsDeterministicAndWithinPolicy()
    {
        int baseDelay = RuntimeReconnectBackoffCalculator.CalculateDelayMs(2, 1000, 30000);

        int first = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(2, 1000, 30000, "device-a");
        int second = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(2, 1000, 30000, "device-a");

        Assert.Equal(first, second);
        Assert.InRange(first, baseDelay, baseDelay + baseDelay * RuntimeReconnectBackoffCalculator.DefaultJitterPercent / 100);
    }

    [Fact]
    public void CalculateScheduledDelayMs_JitterDisabled_ReturnsBaseDelay()
    {
        int delay = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(3, 1000, 30000, "device-a", 0);

        Assert.Equal(4000, delay);
    }

    [Fact]
    public void CalculateScheduledDelayMs_JitterDoesNotExceedMaxDelay()
    {
        int delay = RuntimeReconnectBackoffCalculator.CalculateScheduledDelayMs(5, 1000, 12000, "device-a", 50);

        Assert.Equal(12000, delay);
    }
}
