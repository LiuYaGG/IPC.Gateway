using System.Runtime.InteropServices;

namespace IPC.Runtime.Engine;

public sealed record AcquisitionSlowTag(string TagId, int PeriodMs, long Expected, long OnTime, long Late,
    long Failed, long Missed, double? MaxGapMs, DateTime? GapStartUtc, DateTime? GapEndUtc, bool GapOngoing,
    string? GapEndReason, string? GapError);

// Each table entry is a tag and its effective period at the time of the observation.
// Keep a subtotal per segment for reports and per-second tallies for the two partial
// segments at the window boundaries. Neither storage nor query cost scales with reads/s.
internal sealed class AcquisitionTagWindow(int windowSeconds)
{
    private const int SegmentSeconds = 15;
    private readonly Segment?[] _segments = new Segment[(windowSeconds + SegmentSeconds - 1) / SegmentSeconds + 1];

    internal enum GapKind : byte { None, Sample, ReadFailure, Ongoing }

    internal struct Tally
    {
        public int OnTime, Late, Failed, Missed;
        public DateTime GapStart, GapEnd;
        public GapKind Gap;
        public string? GapError;
        public readonly long GapTicks => GapEnd.Ticks - GapStart.Ticks;
        public readonly int NotOnTime => Late + Failed + Missed;
        public readonly bool HasObservation => OnTime + NotOnTime != 0 || GapEnd != default;

        public void Add(in Tally other)
        {
            OnTime += other.OnTime;
            Late += other.Late;
            Failed += other.Failed;
            Missed += other.Missed;
            if (other.GapEnd != default && (GapEnd == default || other.GapTicks > GapTicks ||
                (other.GapTicks == GapTicks && other.GapEnd >= GapEnd)))
            {
                GapStart = other.GapStart;
                GapEnd = other.GapEnd;
                Gap = other.Gap;
                GapError = other.GapError;
            }
        }
    }

    private sealed class Segment(long number)
    {
        public readonly long Number = number;
        public readonly Dictionary<int, Dictionary<string, TagSegment>> ByPeriod = new();
    }

    // The scheduler uses whole milliseconds (at most 1000 due slots in a second).
    // Store the gap end as ticks within this second, preserving 100 ns precision.
    private struct SecondTally
    {
        private ushort _onTime, _late, _failed, _missed;
        private long _gapStart;
        private int _endOffset;
        private GapKind _gap;

        public void Add(in Tally delta, long secondStart)
        {
            _onTime += (ushort)delta.OnTime;
            _late += (ushort)delta.Late;
            _failed += (ushort)delta.Failed;
            _missed += (ushort)delta.Missed;
            if (delta.Gap == GapKind.None) return;
            long end = secondStart + _endOffset;
            if (_gap != GapKind.None && (end - _gapStart > delta.GapTicks ||
                (end - _gapStart == delta.GapTicks && end > delta.GapEnd.Ticks))) return;
            _gapStart = delta.GapStart.Ticks;
            _endOffset = (int)(delta.GapEnd.Ticks - secondStart);
            _gap = delta.Gap;
        }

        public readonly Tally Expand(long secondStart) => new()
        {
            OnTime = _onTime, Late = _late, Failed = _failed, Missed = _missed, Gap = _gap,
            GapStart = _gap == GapKind.None ? default : new DateTime(_gapStart, DateTimeKind.Utc),
            GapEnd = _gap == GapKind.None ? default : new DateTime(secondStart + _endOffset, DateTimeKind.Utc)
        };
    }

    private sealed class TagSegment(int firstOffset)
    {
        private Tally _total;
        private SecondTally[]? _seconds;
        private List<(DateTime Time, string Message)>? _readErrors;
        // A long outage often accounts for the entire segment. Its first run is kept
        // in closed form, so it needs neither per-slot replay nor an array of seconds.
        private long _firstMissedDue;
        private int _missedRunCount;

        public void Add(long segmentStartTicks, int offset, in Tally delta)
        {
            if (delta.GapError is { Length: > 0 } message)
                (_readErrors ??= []).Add((delta.GapEnd, message));
            if (_seconds == null && offset != firstOffset)
            {
                _seconds = new SecondTally[SegmentSeconds];
                Tally first = _total;
                first.Missed -= _missedRunCount;
                _seconds[firstOffset].Add(first, segmentStartTicks + firstOffset * TimeSpan.TicksPerSecond);
            }
            if (_seconds != null) _seconds[offset].Add(delta, segmentStartTicks + offset * TimeSpan.TicksPerSecond);
            _total.Add(delta);
        }

        public void AddMissed(long segmentStartTicks, long due, int count, long step)
        {
            if (_missedRunCount == 0 || _firstMissedDue + _missedRunCount * step == due)
            {
                if (_missedRunCount == 0) _firstMissedDue = due;
                _missedRunCount += count;
                _total.Missed += count;
                return;
            }
            while (count > 0)
            {
                long second = due / TimeSpan.TicksPerSecond;
                int slotsInSecond = Math.Min(count, SlotsBefore(due, (second + 1) * TimeSpan.TicksPerSecond, step));
                Add(segmentStartTicks, (int)(second % SegmentSeconds), new Tally { Missed = slotsInSecond });
                due += slotsInSecond * step;
                count -= slotsInSecond;
            }
        }

        public Tally Sum(long segmentStartTicks, int period, int first, int last)
        {
            if (first == 0 && last == SegmentSeconds - 1) return _total;
            Tally result = default;
            if (_seconds != null)
            {
                for (int i = first; i <= last; i++)
                    result.Add(_seconds[i].Expand(segmentStartTicks + i * TimeSpan.TicksPerSecond));
            }
            else if (first <= firstOffset && firstOffset <= last)
            {
                result = _total;
                result.Missed -= _missedRunCount;
            }
            if (_missedRunCount != 0)
            {
                long step = period * TimeSpan.TicksPerMillisecond;
                long from = segmentStartTicks + first * TimeSpan.TicksPerSecond;
                long until = segmentStartTicks + (last + 1) * TimeSpan.TicksPerSecond;
                int skip = Math.Min(_missedRunCount, SlotsBefore(_firstMissedDue, from, step));
                int takeThrough = Math.Min(_missedRunCount, SlotsBefore(_firstMissedDue, until, step));
                result.Missed += takeThrough - skip;
            }
            // Keep the original text out of the compact second buckets. Resolve
            // it only for the selected failure when the window cuts this segment.
            if (result.Gap == GapKind.ReadFailure && _readErrors != null)
            {
                foreach (var error in _readErrors)
                    if (error.Time == result.GapEnd) result.GapError = error.Message;
            }
            return result;
        }
    }

    public void Add(long ticks, int period, string tagId, in Tally delta)
    {
        var history = GetSegment(ticks, period, tagId, out long start, out int offset);
        history.Add(start, offset, delta);
    }

    public void AddMissed(long due, long until, int period, string tagId)
    {
        long step = period * TimeSpan.TicksPerMillisecond;
        while (due < until)
        {
            var history = GetSegment(due, period, tagId, out long start, out _);
            long end = Math.Min(until, start + SegmentSeconds * TimeSpan.TicksPerSecond);
            int count = SlotsBefore(due, end, step);
            history.AddMissed(start, due, count, step);
            due += count * step;
        }
    }

    private static int SlotsBefore(long due, long end, long step) =>
        due >= end ? 0 : (int)((end - due + step - 1) / step);

    private TagSegment GetSegment(long ticks, int period, string tagId, out long start, out int offset)
    {
        long second = ticks / TimeSpan.TicksPerSecond;
        long number = second / SegmentSeconds;
        start = number * SegmentSeconds * TimeSpan.TicksPerSecond;
        offset = (int)(second % SegmentSeconds);
        ref var segment = ref _segments[number % _segments.Length];
        if (segment == null || segment.Number != number) segment = new(number);
        if (!segment.ByPeriod.TryGetValue(period, out var tags))
            segment.ByPeriod.Add(period, tags = new(StringComparer.OrdinalIgnoreCase));
        if (!tags.TryGetValue(tagId, out var history))
            tags.Add(tagId, history = new(offset));
        return history;
    }

    public Dictionary<int, Dictionary<string, Tally>> Capture(long firstSecond, long lastSecond, int? periodFilter)
    {
        var totals = new Dictionary<int, Dictionary<string, Tally>>();
        foreach (var segment in _segments)
        {
            if (segment == null) continue;
            long start = segment.Number * SegmentSeconds;
            if (start > lastSecond || start + SegmentSeconds <= firstSecond) continue;
            int first = (int)Math.Max(0, firstSecond - start);
            int last = (int)Math.Min(SegmentSeconds - 1, lastSecond - start);
            foreach (var (period, tags) in segment.ByPeriod)
            {
                if (periodFilter.HasValue && period != periodFilter) continue;
                var rows = GetPeriod(totals, period);
                foreach (var (id, history) in tags)
                {
                    var tally = history.Sum(start * TimeSpan.TicksPerSecond, period, first, last);
                    if (tally.HasObservation) CollectionsMarshal.GetValueRefOrAddDefault(rows, id, out _).Add(tally);
                }
            }
        }
        return totals;
    }

    internal static Dictionary<string, Tally> GetPeriod(Dictionary<int, Dictionary<string, Tally>> totals, int period)
    {
        if (!totals.TryGetValue(period, out var tags)) totals.Add(period, tags = new(StringComparer.OrdinalIgnoreCase));
        return tags;
    }

    public static AcquisitionSlowTag[] Rank(Dictionary<int, Dictionary<string, Tally>>? totals, string? sort)
    {
        if (totals == null) return [];
        var rows = totals.SelectMany(period => period.Value.Select(tag => (Period: period.Key, Id: tag.Key, Value: tag.Value)));
        var ranked = sort == "misses"
            ? rows.OrderByDescending(row => row.Value.NotOnTime).ThenByDescending(row => row.Value.GapTicks)
            : rows.OrderByDescending(row => row.Value.GapTicks).ThenByDescending(row => row.Value.NotOnTime);
        return ranked.ThenBy(row => row.Id, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Period).Take(20)
            .Select(row =>
            {
                var v = row.Value;
                return new AcquisitionSlowTag(row.Id, row.Period, v.OnTime + v.NotOnTime, v.OnTime, v.Late, v.Failed, v.Missed,
                    v.GapEnd == default ? null : TimeSpan.FromTicks(v.GapTicks).TotalMilliseconds,
                    v.GapStart == default ? null : v.GapStart, v.GapEnd == default || v.Gap == GapKind.Ongoing ? null : v.GapEnd,
                    v.Gap == GapKind.Ongoing, v.Gap is GapKind.Sample or GapKind.ReadFailure ? v.Gap.ToString() : null, v.GapError);
            }).ToArray();
    }
}
