using System.Globalization;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed record HistoryTrendPoint(string ChannelId, string DeviceId, string GroupId, string TagId);
public sealed record HistoryTrendSample(DateTimeOffset Timestamp, double Value, string ValueText, string Quality,
    string Unit, int Segment, string Path, string? Processing);
public sealed record HistoryTrendSeries(HistoryTrendPoint Point, IReadOnlyList<HistoryTrendSample> Samples);

public sealed partial class HistoryValueIndex
{
    public const int MaxTrendPoints = 6;
    private const int TrendBuckets = 280;
    private const int MaxTrendUnits = 8;
    internal const string NumericTrendTypes = "'Byte','Int8','UInt8','Int16','UInt16','Int32','UInt32','Int64','UInt64','Float','Double'";

    public Task<IReadOnlyList<HistoryTrendSeries>> QueryTrendAsync(IReadOnlyList<HistoryTrendPoint> points,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (points.Count is < 1 or > MaxTrendPoints || to <= from) throw new ArgumentException("Invalid trend query.");
        return Task.Run<IReadOnlyList<HistoryTrendSeries>>(() =>
        {
            RequireReady();
            ct.ThrowIfCancellationRequested();
            using var db = Open();
            using var cancel = InterruptOnCancel(db, ct);
            using var tx = db.BeginTransaction(deferred: true);
            using var query = db.CreateCommand();
            query.Transaction = tx;
            query.CommandText = """
                SELECT v.ticks,v.value,v.quality,v.dtype,v.unit,p.path,v.processing
                FROM history_value v INDEXED BY history_point_time
                JOIN history_point p ON p.id=v.point_id
                WHERE v.point_id IN (
                    SELECT id FROM history_point WHERE channel_id=$c AND device_id=$d AND group_id=$g AND tag_id=$t
                ) AND v.ticks >= $from AND v.ticks < $to
                ORDER BY v.ticks,v.seq
                """;
            query.Parameters.AddWithValue("$from", from.UtcTicks);
            query.Parameters.AddWithValue("$to", to.UtcTicks);
            var channel = query.Parameters.AddWithValue("$c", "");
            var device = query.Parameters.AddWithValue("$d", "");
            var group = query.Parameters.AddWithValue("$g", "");
            var tag = query.Parameters.AddWithValue("$t", "");
            var results = new List<HistoryTrendSeries>(points.Count);
            try
            {
                foreach (var point in points)
                {
                    channel.Value = point.ChannelId;
                    device.Value = point.DeviceId;
                    group.Value = point.GroupId;
                    tag.Value = point.TagId;
                    using var reader = query.ExecuteReader();
                    results.Add(new(point, DownsampleTrend(reader, from.UtcTicks, to.UtcTicks, ct)));
                }
            }
            catch (SqliteException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            return results;
        }, ct);
    }

    private static IReadOnlyList<HistoryTrendSample> DownsampleTrend(SqliteDataReader reader, long from, long to,
        CancellationToken ct)
    {
        long width = (to - from + TrendBuckets - 1) / TrendBuckets;
        var units = new Dictionary<string, TrendBucket[]>(StringComparer.Ordinal);
        string? lastUnit = null;
        int segment = 0;
        long rowIndex = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            rowIndex++;
            string? valueText = GetString(reader, 1);
            string quality = reader.GetString(2);
            string? processing = GetString(reader, 6);
            if ((!quality.Equals("Good", StringComparison.OrdinalIgnoreCase) &&
                    !quality.Equals("Filtered", StringComparison.OrdinalIgnoreCase)) ||
                !IsNumericTrendType(GetString(reader, 3)) || processing?.Equals("fill", StringComparison.OrdinalIgnoreCase) == true ||
                !double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                lastUnit = null;
                continue;
            }

            string unit = GetString(reader, 4)?.Trim() ?? "";
            if (lastUnit != unit) segment++;
            lastUnit = unit;
            if (!units.TryGetValue(unit, out var buckets))
            {
                if (units.Count == MaxTrendUnits) throw new ArgumentException("Too many historical units in the trend time range.");
                units.Add(unit, buckets = new TrendBucket[TrendBuckets]);
            }
            long ticks = reader.GetInt64(0);
            ref var bucket = ref buckets[(ticks - from) / width];
            // Allocate a sample only when it is kept by a time bucket. Compare the
            // original numeric text where possible so large integers do not tie as doubles.
            decimal? exact = decimal.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;
            var entry = new TrendReading(rowIndex, ticks, value, exact, valueText!, quality, unit, segment, reader.GetString(5), processing);
            bucket.Include(entry);
        }

        return units.Values.SelectMany(buckets => buckets).SelectMany(bucket => bucket.Selected())
            .DistinctBy(item => item.Index).OrderBy(item => item.Index)
            .Select(item => new HistoryTrendSample(new DateTimeOffset(item.Ticks, TimeSpan.Zero), item.Value,
                item.ValueText, item.Quality, item.Unit, item.Segment, item.Path, item.Processing)).ToArray();
    }

    private static string? GetString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    public static bool IsNumericTrendType(string? type) => type is
        "Byte" or "Int8" or "UInt8" or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64" or "Float" or "Double";

    private readonly record struct TrendReading(long Index, long Ticks, double Value, decimal? ExactValue, string ValueText,
        string Quality, string Unit, int Segment, string Path, string? Processing)
    {
        public int CompareValue(in TrendReading other) => ExactValue is decimal a && other.ExactValue is decimal b
            ? a.CompareTo(b)
            : Value.CompareTo(other.Value);
    }

    private struct TrendBucket
    {
        private TrendReading _first;
        private TrendReading _last;
        private TrendReading _minimum;
        private TrendReading _maximum;

        public void Include(in TrendReading incoming)
        {
            if (_first.Index == 0) _first = _minimum = _maximum = incoming;
            if (incoming.CompareValue(_minimum) < 0) _minimum = incoming;
            if (incoming.CompareValue(_maximum) > 0) _maximum = incoming;
            _last = incoming;
        }

        public readonly IEnumerable<TrendReading> Selected() => _first.Index == 0 ? [] : [_first, _minimum, _maximum, _last];
    }
}
