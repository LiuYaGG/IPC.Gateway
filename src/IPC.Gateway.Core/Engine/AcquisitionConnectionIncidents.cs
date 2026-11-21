namespace IPC.Runtime.Engine;

public sealed record AcquisitionConnectionIncident(DateTime StartedUtc, DateTime? RecoveredUtc, string Message);

// Owned by a device's diagnostics lock. Retry events extend the existing incident;
// bursts of per-tag failures cannot evict the first connection failure.
internal sealed class AcquisitionConnectionIncidents
{
    private const int Limit = 80;
    private readonly Queue<AcquisitionConnectionIncident> _completed = new();
    private AcquisitionConnectionIncident? _active;

    public void Record(AcquisitionEvent observation)
    {
        switch (observation.Kind)
        {
            case "DeviceConnectionFailure":
                _active ??= new(observation.TimestampUtc, null, observation.Message);
                break;
            case "DeviceConnectionRecovered" when _active != null:
                if (_completed.Count == Limit) _completed.Dequeue();
                _completed.Enqueue(_active with { RecoveredUtc = observation.TimestampUtc });
                _active = null;
                break;
        }
    }

    public AcquisitionConnectionIncident[] Capture(DateTime cutoffUtc)
    {
        while (_completed.TryPeek(out var oldest) && oldest.RecoveredUtc < cutoffUtc)
            _completed.Dequeue();
        return _active == null ? _completed.Reverse().ToArray() : [_active, .. _completed.Reverse()];
    }

    public void ResetActive() => _active = null;
}
