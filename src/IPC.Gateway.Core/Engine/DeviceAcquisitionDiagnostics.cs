using System.Runtime.InteropServices;
using Tally = IPC.Runtime.Engine.AcquisitionTagWindow.Tally;
using GapKind = IPC.Runtime.Engine.AcquisitionTagWindow.GapKind;

namespace IPC.Runtime.Engine;

public sealed record AcquisitionBatch(DateTime TimestampUtc, string FirstAddress, string LastAddress,
    int TagCount, int RequestCount, int SuccessCount, int FailedCount, int DeferredCount, double? ElapsedMs, string Mode);

public sealed record AcquisitionEvent(DateTime TimestampUtc, string Kind, string? TagId, string? Address, string Message);

public sealed record AcquisitionTrace(string TagId, DateTime ExpiresUtc, DateTime? SampleUtc = null,
    double? QueueMs = null, double? ReadMs = null);

public sealed record DeviceAcquisitionReport(string DeviceId, int WindowSeconds, int ToleranceMs, DateTime ObservedSinceUtc,
    int[] Periods, long Expected, long OnTime, long Late, long Failed, long Missed, double? OnTimePercent,
    double? MaxGapMs, double? P95GapMs, int IsolatedCount, AcquisitionBatch[] Batches, AcquisitionEvent[] Events,
    AcquisitionTrace? Trace, AcquisitionSlowTag[] SlowTags, DateTime ObservedAtUtc,
    AcquisitionConnectionIncident[] ConnectionIncidents);

// Slot counters are bounded by the window, regardless of sample volume. Only tags actively on
// periodic polling have a deadline; first reads, quarantined tags and change subscriptions do not.
internal sealed class DeviceAcquisitionDiagnostics
{
    private const int WindowSeconds = 300;
    private const int ToleranceMs = 100;
    private const int HistoryLimit = 40;
    private const long Second = TimeSpan.TicksPerSecond;
    private const long Millisecond = TimeSpan.TicksPerMillisecond;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly object _gate = new();
    private readonly Dictionary<string, Deadline> _deadlines = new(StringComparer.OrdinalIgnoreCase);
    private readonly Bucket?[] _window = new Bucket[WindowSeconds];
    private readonly AcquisitionTagWindow _tagWindow = new(WindowSeconds);
    private readonly Queue<AcquisitionBatch> _batches = new();
    private readonly Queue<AcquisitionEvent> _events = new();
    private readonly AcquisitionConnectionIncidents _connectionIncidents = new();

    private readonly record struct Deadline(int PeriodMs, DateTime DueUtc, DateTime LastGoodUtc);

    private sealed class Counters
    {
        public long OnTime, Late, Failed, Missed;
        public double? MaxGap;
        public Dictionary<int, int>? GapHistogram;
    }

    private sealed class Bucket(long second)
    {
        public long Second = second;
        public readonly Dictionary<int, Counters> ByPeriod = new();
    }

    public void Reconcile(Func<string, int, bool> keep, bool connectionDisabled = false)
    {
        lock (_gate)
        {
            foreach (var pair in _deadlines)
                if (!keep(pair.Key, pair.Value.PeriodMs)) _deadlines.Remove(pair.Key);
            if (connectionDisabled) _connectionIncidents.ResetActive();
        }
    }

    public void Suspend(string tagId)
    {
        lock (_gate) _deadlines.Remove(tagId);
    }

    public void RecordPeriodic(string tagId, int periodMs, DateTime completedUtc, DateTime nextDueUtc, bool valid)
    {
        lock (_gate)
        {
            DateTime lastGood = default;
            if (_deadlines.TryGetValue(tagId, out var prior) && prior.PeriodMs == periodMs)
            {
                CompleteSlot(tagId, prior, completedUtc, valid);
                lastGood = prior.LastGoodUtc;
                if (valid && lastGood != default)
                {
                    double gap = (completedUtc - lastGood).TotalMilliseconds;
                    if (gap >= 0)
                    {
                        Counters counters = Record(completedUtc.Ticks, periodMs, tagId, new Tally
                        {
                            GapStart = lastGood, GapEnd = completedUtc, Gap = GapKind.Sample
                        });
                        // The histogram retains every gap. Bins are 1 ms below 1 s, 10 ms
                        // below 10 s, and 100 ms thereafter. Return the upper bin edge.
                        int step = gap < 1000 ? 1 : gap < 10000 ? 10 : 100;
                        int bin = (int)Math.Min(int.MaxValue, Math.Ceiling(gap / step) * step);
                        var histogram = counters.GapHistogram ??= new();
                        histogram[bin] = histogram.GetValueOrDefault(bin) + 1;
                    }
                }
            }
            else if (!valid) return;
            if (valid) lastGood = completedUtc;
            _deadlines[tagId] = new(periodMs, nextDueUtc, lastGood);
        }
    }

    public void RecordFailure(string tagId, DateTime completedUtc, string? message = null)
    {
        lock (_gate)
        {
            if (!_deadlines.Remove(tagId, out var prior)) return;
            CompleteSlot(tagId, prior, completedUtc, false);
            if (prior.LastGoodUtc != default && completedUtc >= prior.LastGoodUtc)
            {
                // Periodic accounting stops here, even if the tag remains isolated for longer.
                Record(completedUtc.Ticks, prior.PeriodMs, tagId, new Tally
                {
                    GapStart = prior.LastGoodUtc,
                    GapEnd = completedUtc,
                    Gap = GapKind.ReadFailure,
                    GapError = string.IsNullOrWhiteSpace(message) ? null : message.Length > 512 ? message[..512] : message
                });
            }
        }
    }

    private void CompleteSlot(string tagId, Deadline prior, DateTime completedUtc, bool valid)
    {
        if (completedUtc < prior.DueUtc) return;
        long step = prior.PeriodMs * Millisecond;
        long skipped = (completedUtc.Ticks - prior.DueUtc.Ticks) / step;
        long lastDue = prior.DueUtc.Ticks + skipped * step;
        long cutoff = (completedUtc.Ticks / Second - WindowSeconds + 1) * Second;
        // An outage can span days. Jump to the visible window, then credit skipped
        // slots in bulk per second, rather than replaying a tag's missed reads.
        long due = FirstDueAtOrAfter(prior.DueUtc.Ticks, cutoff, step);
        _tagWindow.AddMissed(due, lastDue, prior.PeriodMs, tagId);
        while (due < lastDue)
        {
            long end = Math.Min(lastDue, (due / Second + 1) * Second);
            long count = (end - due + step - 1) / step;
            GetCounters(due, prior.PeriodMs).Missed += count;
            due += count * step;
        }
        if (lastDue < cutoff) return;
        Tally slot = default;
        if (!valid) slot.Failed = 1;
        else if (completedUtc.Ticks - lastDue <= ToleranceMs * Millisecond) slot.OnTime = 1;
        else slot.Late = 1;
        Record(lastDue, prior.PeriodMs, tagId, slot);
    }

    private static long FirstDueAtOrAfter(long due, long cutoff, long step) =>
        due >= cutoff ? due : due + (cutoff - due + step - 1) / step * step;

    private Counters Record(long ticks, int periodMs, string tagId, in Tally delta)
    {
        var counters = GetCounters(ticks, periodMs);
        counters.OnTime += delta.OnTime;
        counters.Late += delta.Late;
        counters.Failed += delta.Failed;
        counters.Missed += delta.Missed;
        if (delta.GapEnd != default)
            counters.MaxGap = Math.Max(counters.MaxGap ?? 0, TimeSpan.FromTicks(delta.GapTicks).TotalMilliseconds);
        _tagWindow.Add(ticks, periodMs, tagId, delta);
        return counters;
    }

    private Counters GetCounters(long ticks, int periodMs)
    {
        long second = ticks / Second;
        ref Bucket? bucket = ref _window[second % WindowSeconds];
        if (bucket == null || bucket.Second != second) bucket = new Bucket(second);
        if (!bucket.ByPeriod.TryGetValue(periodMs, out var counters))
            bucket.ByPeriod.Add(periodMs, counters = new Counters());
        return counters;
    }

    public void RecordBatch(AcquisitionBatch batch)
    {
        lock (_gate) Append(_batches, batch);
    }

    public void RecordEvent(string kind, string? tagId, string? address, string? message)
    {
        string detail = message ?? string.Empty;
        if (detail.Length > 512) detail = detail[..512];
        lock (_gate)
        {
            var observation = new AcquisitionEvent(DateTime.UtcNow, kind, tagId, address, detail);
            Append(_events, observation);
            _connectionIncidents.Record(observation);
        }
    }

    private static void Append<T>(Queue<T> queue, T item)
    {
        if (queue.Count >= HistoryLimit) queue.Dequeue();
        queue.Enqueue(item);
    }

    public DeviceAcquisitionReport Capture(string deviceId, int[] periods, int? periodFilter, int isolatedCount, AcquisitionTrace? trace,
        string? slowSort = null)
    {
        var histogram = new Dictionary<int, long>();
        long onTime = 0, late = 0, failed = 0, missed = 0, gapCount = 0;
        double? maxGap = null;
        DateTime now;
        AcquisitionConnectionIncident[] incidents;
        AcquisitionBatch[] batches;
        AcquisitionEvent[] events;
        Dictionary<int, Dictionary<string, Tally>>? tagTotals;
        DateTime since;
        lock (_gate)
        {
            now = DateTime.UtcNow;
            long cutoff = Math.Max(_startedUtc.Ticks, (now.Ticks / Second - WindowSeconds + 1) * Second);
            since = new DateTime(cutoff, DateTimeKind.Utc);
            tagTotals = slowSort is "gap" or "misses" ? _tagWindow.Capture(cutoff / Second, now.Ticks / Second, periodFilter) : null;
            incidents = tagTotals == null ? [] : _connectionIncidents.Capture(since);
            foreach (var bucket in _window)
            {
                if (bucket == null || bucket.Second < cutoff / Second || bucket.Second > now.Ticks / Second) continue;
                foreach (var (period, counters) in bucket.ByPeriod)
                {
                    if (periodFilter.HasValue && period != periodFilter) continue;
                    onTime += counters.OnTime;
                    late += counters.Late;
                    failed += counters.Failed;
                    missed += counters.Missed;
                    if (counters.MaxGap.HasValue) maxGap = Math.Max(maxGap ?? 0, counters.MaxGap.Value);
                    if (counters.GapHistogram == null) continue;
                    foreach (var (gap, count) in counters.GapHistogram)
                    {
                        histogram[gap] = histogram.GetValueOrDefault(gap) + count;
                        gapCount += count;
                    }
                }
            }
            long expiredThrough = now.Ticks - ToleranceMs * Millisecond;
            foreach (var (tagId, deadline) in _deadlines)
            {
                if (periodFilter.HasValue && deadline.PeriodMs != periodFilter) continue;
                long step = deadline.PeriodMs * Millisecond;
                long due = FirstDueAtOrAfter(deadline.DueUtc.Ticks, cutoff, step);
                Tally pending = default;
                if (due <= expiredThrough)
                {
                    pending.Missed = (int)((expiredThrough - due) / step + 1);
                    missed += pending.Missed;
                }
                if (deadline.LastGoodUtc != default && now.Ticks - deadline.LastGoodUtc.Ticks > step + ToleranceMs * Millisecond)
                {
                    maxGap = Math.Max(maxGap ?? 0, (now - deadline.LastGoodUtc).TotalMilliseconds);
                    pending.GapStart = deadline.LastGoodUtc;
                    pending.GapEnd = now;
                    pending.Gap = GapKind.Ongoing;
                }
                if (tagTotals != null && pending.HasObservation)
                {
                    var rows = AcquisitionTagWindow.GetPeriod(tagTotals, deadline.PeriodMs);
                    CollectionsMarshal.GetValueRefOrAddDefault(rows, tagId, out _).Add(pending);
                }
            }
            batches = _batches.Reverse().ToArray();
            events = _events.Reverse().ToArray();
        }
        double? p95 = null;
        long rank = (long)Math.Ceiling(gapCount * 0.95);
        foreach (var (gap, count) in histogram.OrderBy(pair => pair.Key))
        {
            rank -= count;
            if (rank <= 0) { p95 = gap; break; }
        }
        long expected = onTime + late + failed + missed;
        var slowTags = AcquisitionTagWindow.Rank(tagTotals, slowSort);
        var related = incidents.Where(incident => slowTags.Any(tag => tag.GapStartUtc is DateTime start &&
            incident.StartedUtc <= (tag.GapEndUtc ?? now) && (incident.RecoveredUtc ?? now) >= start)).ToArray();
        return new(deviceId, WindowSeconds, ToleranceMs, since, periods, expected, onTime, late, failed, missed,
            expected > 0 ? Math.Round(onTime * 100d / expected, 2) : null, maxGap, p95, isolatedCount, batches, events, trace,
            slowTags, now, related);
    }
}
