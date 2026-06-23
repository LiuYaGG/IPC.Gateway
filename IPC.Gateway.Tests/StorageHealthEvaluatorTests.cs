/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：StorageHealthEvaluatorTests
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
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Tests;

public sealed class StorageHealthEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsHealthy_WhenFreeSpaceIsAboveThresholds()
    {
        StorageHealthStatus status = StorageHealthEvaluator.Evaluate(
            "Data",
            "C:\\",
            100L * 1024L * 1024L * 1024L,
            50L * 1024L * 1024L * 1024L,
            new StorageHealthThresholds());

        Assert.Equal(StorageHealthEvaluator.Healthy, status.HealthStatus);
        Assert.True(status.IsAvailable);
        Assert.Equal(50D, status.AvailablePercent);
        Assert.Equal(50D, status.UsagePercent);
    }

    [Fact]
    public void Evaluate_ReturnsDegraded_WhenFreePercentIsLow()
    {
        StorageHealthStatus status = StorageHealthEvaluator.Evaluate(
            "Data",
            "C:\\",
            100L * 1024L * 1024L * 1024L,
            5L * 1024L * 1024L * 1024L,
            new StorageHealthThresholds());

        Assert.Equal(StorageHealthEvaluator.Degraded, status.HealthStatus);
        Assert.Equal(5D, status.AvailablePercent);
        Assert.Contains("low", status.HealthMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_ReturnsUnhealthy_WhenFreeBytesAreCriticallyLow()
    {
        StorageHealthStatus status = StorageHealthEvaluator.Evaluate(
            "Data",
            "C:\\",
            100L * 1024L * 1024L * 1024L,
            128L * 1024L * 1024L,
            new StorageHealthThresholds());

        Assert.Equal(StorageHealthEvaluator.Unhealthy, status.HealthStatus);
        Assert.Equal(128L * 1024L * 1024L, status.AvailableBytes);
        Assert.Contains("critically", status.HealthMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_UsesCustomThresholds_WhenConfigured()
    {
        StorageHealthThresholds thresholds = new StorageHealthThresholds
        {
            DegradedAvailableBytes = 3L * 1024L * 1024L * 1024L,
            UnhealthyAvailableBytes = 512L * 1024L * 1024L,
            DegradedAvailablePercent = 10D,
            UnhealthyAvailablePercent = 1D
        };

        StorageHealthStatus status = StorageHealthEvaluator.Evaluate(
            "Data",
            "C:\\",
            100L * 1024L * 1024L * 1024L,
            2L * 1024L * 1024L * 1024L,
            thresholds);

        Assert.Equal(StorageHealthEvaluator.Degraded, status.HealthStatus);
        Assert.Equal(3L * 1024L * 1024L * 1024L, status.DegradedAvailableBytes);
        Assert.Equal(512L * 1024L * 1024L, status.UnhealthyAvailableBytes);
    }

    [Fact]
    public void Evaluate_DoesNotMutateThresholds_WhenNormalizing()
    {
        StorageHealthThresholds thresholds = new StorageHealthThresholds
        {
            DegradedAvailableBytes = 1,
            UnhealthyAvailableBytes = 2,
            DegradedAvailablePercent = 1D,
            UnhealthyAvailablePercent = 2D
        };

        StorageHealthStatus status = StorageHealthEvaluator.Evaluate(
            "Data",
            "C:\\",
            100L,
            50L,
            thresholds);

        Assert.Equal(2L, status.DegradedAvailableBytes);
        Assert.Equal(2D, status.DegradedAvailablePercent);
        Assert.Equal(1L, thresholds.DegradedAvailableBytes);
        Assert.Equal(1D, thresholds.DegradedAvailablePercent);
    }
}
