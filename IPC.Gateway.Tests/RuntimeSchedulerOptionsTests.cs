/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：RuntimeSchedulerOptionsTests
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

public sealed class RuntimeSchedulerOptionsTests
{
    [Fact]
    public void Normalize_DerivesAdmissionLimitAndClampsWatermarks()
    {
        RuntimeSchedulerOptions options = new RuntimeSchedulerOptions
        {
            MaxConcurrentDevicePolls = 3,
            DevicePollQueueLimit = 10,
            QueueHighWatermarkPercent = 90,
            QueueLowWatermarkPercent = 95,
            MaxDevicePollsQueuedPerSchedulerTick = 0
        };

        RuntimeSchedulerOptions normalized = options.Normalize();

        Assert.True(normalized.BackpressureEnabled);
        Assert.Equal(90, normalized.QueueHighWatermarkPercent);
        Assert.Equal(70, normalized.QueueLowWatermarkPercent);
        Assert.Equal(6, normalized.MaxDevicePollsQueuedPerSchedulerTick);
    }

    [Fact]
    public void Normalize_KeepsExplicitAdmissionLimit()
    {
        RuntimeSchedulerOptions options = new RuntimeSchedulerOptions
        {
            MaxConcurrentDevicePolls = 4,
            MaxDevicePollsQueuedPerSchedulerTick = 12,
            BackpressureDelayMs = 1
        };

        RuntimeSchedulerOptions normalized = options.Normalize();

        Assert.Equal(12, normalized.MaxDevicePollsQueuedPerSchedulerTick);
        Assert.Equal(20, normalized.BackpressureDelayMs);
    }
}
