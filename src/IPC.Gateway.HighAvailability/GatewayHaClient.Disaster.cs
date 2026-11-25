using System.Diagnostics;

namespace IPC.Gateway.HighAvailability;

public sealed record HaDisasterTakeover(string OperationId, string NodeId, string InstanceId, string Actor,
    string Reason, DateTimeOffset StartedAtUtc, DateTimeOffset ExpiresAtUtc, bool Active);

public sealed partial class GatewayHaClient
{
    private sealed record DisasterLease(HaDisasterTakeover State, long Deadline);
    private DisasterLease? _disasterLease;
    private volatile bool _disasterDraining;

    // This authorization is deliberately process-local: a restart always needs a new human decision.
    public bool DisasterTakeoverActive => Volatile.Read(ref _disasterLease) is { } grant &&
        Stopwatch.GetTimestamp() < grant.Deadline;
    public bool DisasterTakeoverConfigured => Endpoints.Count == 3 && !AwaitingProvisioning &&
        NodeId is "A" or "B" && Volatile.Read(ref _latched) != 0;
    public bool RequiresWriteAuthorization => IsCluster || DisasterTakeoverActive || _disasterDraining;
    public HaDisasterTakeover? DisasterTakeover
    {
        get
        {
            var state = Volatile.Read(ref _disasterLease)?.State;
            return state == null ? null : state with { Active = DisasterTakeoverActive };
        }
    }

    public HaDisasterTakeover StartDisasterTakeover(string operationId, string actor, string reason, int durationMinutes)
    {
        if (!Guid.TryParse(operationId, out _) || string.IsNullOrWhiteSpace(reason) || reason.Length > 512 ||
            string.IsNullOrWhiteSpace(actor) || actor.Length > 160 || durationMinutes is < 15 or > 1440)
            throw new HaRequestException(400, "HA_DISASTER_INVALID_REQUEST");
        lock (_inflightLock)
        {
            if (!DisasterTakeoverConfigured)
                throw new HaRequestException(409, "HA_NOT_PROVISIONED");
            var previous = _disasterLease;
            if (previous?.State.OperationId == operationId && DisasterTakeoverActive) return previous.State;
            if (!DisasterTakeoverActive && (CanRunBusiness || _businessPeriod || _inflight != 0 || _disasterDraining))
                throw new HaRequestException(409, "HA_DISASTER_NOT_DRAINED");
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var duration = TimeSpan.FromMinutes(durationMinutes);
            var state = new HaDisasterTakeover(operationId, NodeId, InstanceId, actor, reason.Trim(),
                previous != null && DisasterTakeoverActive ? previous.State.StartedAtUtc : now, now.Add(duration), true);
            _draining = false;
            Volatile.Write(ref _disasterLease, new(state, Stopwatch.GetTimestamp() + (long)(duration.TotalSeconds * Stopwatch.Frequency)));
            return state;
        }
    }

    public HaDisasterTakeover? StopDisasterTakeover()
    {
        lock (_inflightLock)
        {
            var previous = Interlocked.Exchange(ref _disasterLease, null);
            if (previous == null) return null;
            _disasterDraining = _businessPeriod || _inflight != 0;
            RevokeLocal();
            return previous.State with { Active = false };
        }
    }
}
