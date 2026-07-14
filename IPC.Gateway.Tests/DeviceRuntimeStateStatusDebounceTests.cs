using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;

namespace IPC.Gateway.Tests;

public sealed class DeviceRuntimeStateStatusDebounceTests
{
    [Fact]
    public void ApplyStatusSample_DebouncesFailureAndRecoversImmediately()
    {
        DeviceRuntimeState state = new DeviceRuntimeState(
            new DeviceConfig { Name = "PLC-1", Enabled = true },
            new CircuitBreakerOptions());
        DateTime now = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal("Offline", state.StableStatus);
        Assert.Equal("Online", state.ApplyStatusSample("Online", now, 2, 2000, 1, 0));
        Assert.Equal("Online", state.ApplyStatusSample("Error", now.AddMilliseconds(100), 2, 2000, 1, 0));
        Assert.Equal("Online", state.StableStatus);
        Assert.Equal("Error", state.ApplyStatusSample("Error", now.AddMilliseconds(2500), 2, 2000, 1, 0));
        Assert.Equal("Online", state.ApplyStatusSample("Online", now.AddMilliseconds(2600), 2, 2000, 1, 0));
    }

    [Fact]
    public void ApplyStatusSample_DefaultFailureDebounceRequiresThreeFailures()
    {
        DeviceRuntimeState state = new DeviceRuntimeState(
            new DeviceConfig { Name = "PLC-UDP", Enabled = true },
            new CircuitBreakerOptions());
        RuntimeSchedulerOptions options = new RuntimeSchedulerOptions();
        DateTime now = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(3, options.DeviceStatusFailureDebounceCount);
        Assert.Equal("Online", state.ApplyStatusSample("Online", now, 1, 0, 1, 0));
        Assert.Equal("Online", state.ApplyStatusSample("Error", now.AddMilliseconds(100), options.DeviceStatusFailureDebounceCount, 0, 1, 0));
        Assert.Equal("Online", state.ApplyStatusSample("Error", now.AddMilliseconds(200), options.DeviceStatusFailureDebounceCount, 0, 1, 0));
        Assert.Equal("Error", state.ApplyStatusSample("Error", now.AddMilliseconds(300), options.DeviceStatusFailureDebounceCount, 0, 1, 0));
    }

    [Fact]
    public void ApplyStatusSample_DisabledSwitchesImmediately()
    {
        DeviceRuntimeState state = new DeviceRuntimeState(
            new DeviceConfig { Name = "PLC-2", Enabled = true },
            new CircuitBreakerOptions());
        DateTime now = new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc);

        state.ApplyStatusSample("Online", now, 2, 2000, 1, 0);

        Assert.Equal("Disabled", state.ApplyStatusSample("Disabled", now.AddMilliseconds(100), 2, 2000, 1, 0));
    }

    [Fact]
    public void ForceStatus_PreservesDegradedState()
    {
        DeviceRuntimeState state = new DeviceRuntimeState(
            new DeviceConfig { Name = "PLC-UDP", Enabled = true },
            new CircuitBreakerOptions());

        Assert.Equal("Degraded", state.ForceStatus("Degraded", DateTime.UtcNow));
        Assert.Equal("Degraded", state.StableStatus);
    }
}
