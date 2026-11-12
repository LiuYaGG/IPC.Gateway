using System.Globalization;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

/// <summary>Tracks real samples only; timer ticks never refresh sample timestamps.</summary>
internal sealed class NoChangeTimeoutTracker
{
    private object? _baseline;
    private bool _hasBaseline;
    private bool _continuous;
    private DateTime _since;
    private DateTime _latestTimestamp;
    public bool Available { get; private set; }
    public bool Changed { get; private set; }
    public double ElapsedSeconds { get; private set; }

    public void Update(TagValueSnapshot? snapshot, DateTime now, double deadband, int maxAgeSeconds)
    {
        Changed = false;
        now = now.ToUniversalTime();
        DateTime timestamp = snapshot?.Timestamp.ToUniversalTime() ?? DateTime.MinValue;
        if (snapshot != null && snapshot.Timestamp != DateTime.MinValue && timestamp < _latestTimestamp)
        {
            Available = _continuous && now - _latestTimestamp <= TimeSpan.FromSeconds(maxAgeSeconds);
            if (!Available) Invalidate();
            else ElapsedSeconds = Math.Max(0, (now - _since).TotalSeconds);
            return;
        }
        if (snapshot == null || snapshot.Quality != TagQuality.Good || snapshot.Timestamp == DateTime.MinValue ||
            timestamp > now.AddSeconds(1) || now - timestamp > TimeSpan.FromSeconds(maxAgeSeconds) ||
            !IsUsable(snapshot.Value))
        {
            Invalidate();
            return;
        }
        if (timestamp > _latestTimestamp)
        {
            bool gap = _latestTimestamp != DateTime.MinValue && timestamp - _latestTimestamp > TimeSpan.FromSeconds(maxAgeSeconds);
            Changed = _hasBaseline && !Same(_baseline, snapshot.Value, deadband);
            if (!_continuous || gap || Changed)
            {
                _since = now; // Never count time before the first observed valid sample.
                _continuous = true;
            }
            if (!_hasBaseline || Changed)
            {
                _baseline = CopyValue(snapshot.Value);
                _hasBaseline = true;
            }
            _latestTimestamp = timestamp;
        }
        Available = _continuous && now - _latestTimestamp <= TimeSpan.FromSeconds(maxAgeSeconds);
        ElapsedSeconds = Available ? Math.Max(0, (now - _since).TotalSeconds) : 0;
    }

    private void Invalidate() { Available = false; _continuous = false; ElapsedSeconds = 0; }
    public NoChangeTimeoutTracker Clone()
    {
        var copy = (NoChangeTimeoutTracker)MemberwiseClone();
        copy._baseline = CopyValue(_baseline);
        return copy;
    }
    private static object? CopyValue(object? value) => value is Array array ? array.Clone() : value;
    private static bool IsUsable(object? value)
    {
        if (value == null) return false;
        if (value is double d) return double.IsFinite(d);
        if (value is float f) return float.IsFinite(f);
        if (value is Array array) { foreach (var item in array) if (!IsUsable(item)) return false; return true; }
        return value is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal;
    }
    private static bool Same(object? left, object? right, double deadband)
    {
        if (left is Array a && right is Array b)
        {
            if (a.Rank != b.Rank || a.Length != b.Length) return false;
            for (int d = 0; d < a.Rank; d++) if (a.GetLength(d) != b.GetLength(d)) return false;
            var first = a.GetEnumerator(); var second = b.GetEnumerator();
            while (first.MoveNext() && second.MoveNext()) if (!Same(first.Current, second.Current, deadband)) return false;
            return true;
        }
        if (left is string || right is string || left is bool || right is bool) return Equals(left, right);
        if (left is double or float || right is double or float)
            return Math.Abs(Convert.ToDouble(left, CultureInfo.InvariantCulture) - Convert.ToDouble(right, CultureInfo.InvariantCulture)) <= deadband;
        // Decimal comparison preserves adjacent 64-bit heartbeat counter values.
        try
        {
            return Math.Abs(Convert.ToDecimal(left, CultureInfo.InvariantCulture) - Convert.ToDecimal(right, CultureInfo.InvariantCulture)) <= (decimal)Math.Min(deadband, (double)decimal.MaxValue / 2);
        }
        catch (OverflowException)
        {
            return Math.Abs(Convert.ToDouble(left, CultureInfo.InvariantCulture) - Convert.ToDouble(right, CultureInfo.InvariantCulture)) <= deadband;
        }
    }
}
