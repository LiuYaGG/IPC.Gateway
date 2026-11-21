using System.Globalization;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

/// <summary>Read-only snapshots for HTTP clients. The derived SQLite index has a single background writer.</summary>
public sealed partial class HistoryValueIndex : IDisposable
{
    private readonly string _hot;
    private readonly string _cold;
    private readonly string _connectionString;
    private readonly object _lifecycle = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _worker;
    private bool _disposed;
    private volatile bool _ready;
    private const string Joined = " FROM history_value v JOIN history_point p ON p.id=v.point_id ";

    internal HistoryValueIndex(string hot, string cold)
    {
        _hot = Path.GetFullPath(hot);
        _cold = Path.GetFullPath(cold);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_hot, ".query-index", "values-v1.sqlite"),
            DefaultTimeout = 3,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
    }

    internal void Start()
    {
        lock (_lifecycle)
        {
            if (!_disposed) _worker ??= Task.Run(() => RunBackgroundAsync(_stop.Token));
        }
    }

    public void Dispose()
    {
        Task? task;
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            _ready = false;
            _stop.Cancel();
            task = _worker;
        }
        task?.GetAwaiter().GetResult();
        _stop.Dispose();
    }

    internal static CancellationTokenRegistration InterruptOnCancel(SqliteConnection db, CancellationToken ct) =>
        ct.Register(() => SQLitePCL.raw.sqlite3_interrupt(db.Handle));

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        try { db.Open(); return db; }
        catch { db.Dispose(); throw; }
    }

    private void RequireReady()
    {
        if (!_ready) throw new HistoryIndexUnavailableException();
    }

    public Task<HistoryValueStream> OpenStreamAsync(HistoryValueFilter filter, string? snapshot, CancellationToken ct,
        int? take = null, bool countTotal = true) =>
        Task.Run(() => OpenResult(filter, snapshot, take, 0, ct, countTotal), ct);

    public async Task<HistoryValuePage> QueryAsync(HistoryValueFilter filter, int page, int pageSize,
        string? snapshot, CancellationToken ct)
    {
        if (page < 1 || pageSize < 1) throw new ArgumentOutOfRangeException(nameof(page));
        await using var result = await Task.Run(() => OpenResult(filter, snapshot, pageSize, (long)(page - 1) * pageSize, ct), ct).ConfigureAwait(false);
        var items = new List<HistoryValueRow>();
        await foreach (var item in result.Items.ConfigureAwait(false)) items.Add(item);
        return new HistoryValuePage(result.Total, page, pageSize, result.Snapshot, items);
    }

    private HistoryValueStream OpenResult(HistoryValueFilter f, string? snapshot, int? limit, long offset, CancellationToken ct, bool countTotal = true)
    {
        if (f.To <= f.From) throw new ArgumentException("Invalid history time range.");
        RequireReady();
        ct.ThrowIfCancellationRequested();
        var db = Open();
        try
        {
            var tx = db.BeginTransaction(deferred: true);
            using var cancel = InterruptOnCancel(db, ct);
            using var state = db.CreateCommand();
            state.Transaction = tx;
            state.CommandText = "SELECT epoch, COALESCE((SELECT max(seq) FROM history_value),0) FROM history_meta";
            using var r = state.ExecuteReader();
            r.Read();
            string epoch = r.GetString(0);
            long max = r.GetInt64(1);
            r.Close();
            long horizon = max;
            if (!string.IsNullOrEmpty(snapshot))
            {
                var parts = snapshot.Split(':', 2);
                if (parts.Length != 2 || parts[0] != epoch || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out horizon) || horizon > max)
                    throw new HistorySnapshotExpiredException();
            }
            string token = epoch + ":" + horizon.ToString(CultureInfo.InvariantCulture);
            const string timeRange = "v.ticks >= $from AND v.ticks < $to AND v.seq <= $max";
            var pointFilters = new List<string>();
            var args = new Dictionary<string, object> { ["$from"] = f.From.UtcTicks, ["$to"] = f.To.UtcTicks, ["$max"] = horizon };
            void Add(string column, string? value)
            {
                if (value == null) return;
                pointFilters.Add(column + "=$" + column[2..]);
                args.Add("$" + column[2..], value);
            }
            SqliteCommand Command(string sql)
            {
                var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = sql;
                foreach (var arg in args) cmd.Parameters.AddWithValue(arg.Key, arg.Value);
                return cmd;
            }
            if (!string.IsNullOrEmpty(f.Path)) Add("p.path", f.Path);
            Add("p.channel_id", f.ChannelId);
            Add("p.device_id", f.DeviceId);
            Add("p.group_id", f.GroupId);
            Add("p.tag_id", f.TagId);
            string points = string.Join(" AND ", pointFilters);
            string clause = Joined + "WHERE " + timeRange + (points.Length != 0 ? " AND " + points : "");
            if (!string.IsNullOrEmpty(f.Path))
            {
                // Paths are exact, escaped names. Probe the time index once per candidate,
                // then constrain ORDER BY to a single point index (also for size=1).
                using var candidates = Command("SELECT p.id FROM history_point p WHERE " + points +
                    " AND EXISTS (SELECT 1 FROM history_value v WHERE v.point_id=p.id AND " + timeRange + ") LIMIT 2");
                using var matched = candidates.ExecuteReader();
                long id = matched.Read() ? matched.GetInt64(0) : -1;
                if (matched.Read()) throw new HistoryPathConflictException();
                args.Add("$point", id);
                clause += " AND v.point_id=$point";
            }
            if (f.Quality != null)
            {
                args.Add("$quality", f.Quality);
                clause += " AND v.quality=$quality";
            }
            using var count = Command("SELECT count(*)" + clause);
            long total = countTotal ? (long)count.ExecuteScalar()! : -1;
            string select = "SELECT v.ticks,p.path,p.channel_id,p.device_id,p.group_id,p.tag_id,v.value,v.raw,v.quality,v.dtype,v.unit,v.error,v.processing,v.details";
            var query = Command(select + clause + " ORDER BY v.ticks DESC,v.seq DESC" + (limit.HasValue ? " LIMIT $take OFFSET $skip" : ""));
            if (limit.HasValue)
            {
                query.Parameters.AddWithValue("$take", limit.Value);
                query.Parameters.AddWithValue("$skip", offset);
            }
            ct.ThrowIfCancellationRequested();
            return new HistoryValueStream(db, tx, query, total, token, ct);
        }
        catch (SqliteException) when (ct.IsCancellationRequested) { db.Dispose(); throw new OperationCanceledException(ct); }
        catch { db.Dispose(); throw; }
    }

    public Task<IReadOnlyList<HistoryPointOption>> OptionsAsync(string level, string? channelId, string? deviceId,
        string? groupId, string? search, int limit, CancellationToken ct, bool numericOnly = false) =>
        Task.Run<IReadOnlyList<HistoryPointOption>>(() =>
        {
            RequireReady();
            ct.ThrowIfCancellationRequested();
            using var db = Open();
            using var cancel = InterruptOnCancel(db, ct);
            using var cmd = db.CreateCommand();
            (string id, string name) = level switch
            {
                "channel" => ("channel_id", "channel_name"),
                "device" => ("device_id", "device_name"),
                "group" => ("group_id", "group_name"),
                "tag" => ("tag_id", "tag_name"),
                _ => throw new ArgumentException("Unknown point level.")
            };
            var conditions = new List<string>();
            foreach (var (column, value) in new[] { ("channel_id", channelId), ("device_id", deviceId), ("group_id", groupId) })
            {
                if (value == null) continue;
                conditions.Add(column + "=$" + column);
                cmd.Parameters.AddWithValue("$" + column, value);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                conditions.Add("instr(lower(" + name + "),lower($search))>0" +
                    (numericOnly && level == "tag" ? " OR instr(lower(path),lower($search))>0" : ""));
                cmd.Parameters.AddWithValue("$search", search.Trim());
            }
            if (numericOnly)
                conditions.Add("EXISTS (SELECT 1 FROM history_value v WHERE v.point_id=history_point.id AND v.dtype IN (" + NumericTrendTypes + "))");
            cmd.Parameters.AddWithValue("$limit", limit);
            string path = level == "tag" ? "path" : "NULL";
            cmd.CommandText = "SELECT DISTINCT " + id + "," + name + "," + path + " FROM history_point " +
                (conditions.Count > 0 ? " WHERE (" + string.Join(") AND (", conditions) + ")" : "") + " ORDER BY 2,1,3 LIMIT $limit";
            var rows = new List<HistoryPointOption>();
            try
            {
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    rows.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }
            catch (SqliteException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
            return rows;
        }, ct);
}
