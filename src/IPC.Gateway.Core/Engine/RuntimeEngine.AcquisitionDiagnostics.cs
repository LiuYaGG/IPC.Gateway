using System.Collections.Concurrent;
using System.Diagnostics;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine;

public sealed partial class RuntimeEngine
{
    // Only explicitly requested traces enter this dictionary. The realtime delivery
    // path checks it without allocating or acquiring the global runtime lock.
    private readonly ConcurrentDictionary<string, AcquisitionTrace> _acquisitionTraces = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAcquisitionTraceActive(string deviceId, string tagId) =>
        _acquisitionTraces.TryGetValue(deviceId, out var trace) &&
        trace.ExpiresUtc > DateTime.UtcNow && string.Equals(trace.TagId, tagId, StringComparison.OrdinalIgnoreCase);

    public AcquisitionTrace? StartAcquisitionTrace(string deviceId, string tagId, out bool busy)
    {
        lock (_syncRoot)
        {
            busy = false;
            if (!CanTraceAcquisition(deviceId, tagId)) return null;
            var now = DateTime.UtcNow;
            foreach (var pair in _acquisitionTraces)
                if (pair.Value.ExpiresUtc <= now) _acquisitionTraces.TryRemove(pair);
            if (_acquisitionTraces.TryGetValue(deviceId, out var active) && active.ExpiresUtc > now)
            {
                if (string.Equals(active.TagId, tagId, StringComparison.OrdinalIgnoreCase)) return active;
                busy = true;
                return null;
            }
            var trace = new AcquisitionTrace(tagId, now.AddSeconds(60));
            _acquisitionTraces[deviceId] = trace;
            return trace;
        }
    }

    private bool CanTraceAcquisition(string deviceId, string tagId) =>
        IsRunning && _deviceStatesById.TryGetValue(deviceId, out var state) && state.Config.Enabled &&
        _configuredChannelScheduler.IsEnabled(state.Config) && state.ReadPlan.TryGet(tagId, out var entry) &&
        entry is { IsStaticallyValid: true, Tag.Enabled: true } && entry.Group?.Enabled != false && CanDeviceRead(entry.Tag);

    public DeviceAcquisitionReport? GetAcquisitionReport(string deviceId, int? periodFilter, string? slowSort = null)
    {
        DeviceRuntimeState state;
        DeviceConfig device;
        CompiledDeviceReadPlan plan;
        bool enabled;
        int isolatedCount = 0;
        lock (_syncRoot)
        {
            if (!_deviceStatesById.TryGetValue(deviceId, out var found)) return null;
            state = found;
            device = state.Config;
            plan = state.ReadPlan;
            enabled = device.Enabled && _configuredChannelScheduler.IsEnabled(device);
        }
        var configuredPeriods = new HashSet<int>();
        foreach (var entry in plan.Entries)
        {
            if (!entry.Tag.Enabled || entry.Group?.Enabled == false || !CanDeviceRead(entry.Tag)) continue;
            int period = GetEffectiveScanRateMs(device, entry.Group, entry.Tag);
            configuredPeriods.Add(period);
            if (enabled && entry.Runtime.IsIsolated && (periodFilter == null || periodFilter == period)) isolatedCount++;
        }
        _acquisitionTraces.TryGetValue(deviceId, out var trace);
        return state.Diagnostics.Capture(deviceId, configuredPeriods.Order().ToArray(), periodFilter, isolatedCount,
            trace != null && trace.ExpiresUtc > DateTime.UtcNow ? trace : null, slowSort);
    }

    private readonly record struct DriverReadTiming(double? QueueMs, double? ReadMs)
    {
        public static DriverReadTiming Finish(DeviceRuntimeState state, long started)
        {
            double queue = (state.CurrentTaskStartedUtc - state.CurrentTaskQueuedUtc).TotalMilliseconds;
            return new(state.CurrentTaskQueuedUtc == default || state.CurrentTaskStartedUtc == default || queue < 0 ? null : queue,
                started == 0 ? null : Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private void RecordTraceSample(string deviceId, string? tagId, DateTime sampleTime, DriverReadTiming? timing)
    {
        if (!_acquisitionTraces.TryGetValue(deviceId, out var trace) || trace.ExpiresUtc <= DateTime.UtcNow ||
            !string.Equals(trace.TagId, tagId, StringComparison.OrdinalIgnoreCase)) return;
        _acquisitionTraces.TryUpdate(deviceId, trace with
        {
            SampleUtc = sampleTime.ToUniversalTime(), QueueMs = timing?.QueueMs, ReadMs = timing?.ReadMs
        }, trace);
    }
}
