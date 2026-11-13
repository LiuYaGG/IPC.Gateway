using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed class GatewayAlarmRecord
{
    public string EventId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = "threshold";
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string TagId { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string PreviousState { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public string Message { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "Local";
    [JsonIgnore] public DateTime? NotBeforeUtc { get; set; }
    [JsonIgnore] public string Topic { get; set; } = string.Empty;
    [JsonIgnore] public string Payload { get; set; } = string.Empty;
    [JsonIgnore] public int Qos { get; set; }
}

public sealed record GatewayAlarmPage(long Total, IReadOnlyList<GatewayAlarmRecord> Items);
internal readonly record struct AlarmTagState(string State, double Threshold);

/// <summary>Pending alarms remain durable independently of ordinary history and MQTT outbox limits.</summary>
public sealed class GatewayAlarmJournal
{
    private readonly string _path;
    private readonly string _connectionString;
    private readonly object _schemaLock = new();
    private readonly Func<LocalHistoryOptions>? _retention;
    private DateTime _nextCleanup;
    private volatile bool _initialized;

    public GatewayAlarmJournal(string databasePath, Func<LocalHistoryOptions>? retention = null)
    {
        _path = Path.GetFullPath(databasePath);
        _retention = retention;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _path, DefaultTimeout = 5 }.ToString();
    }

    private SqliteConnection Open()
    {
        if (!_initialized)
        {
            lock (_schemaLock)
            {
                if (!_initialized)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    using var schema = new SqliteConnection(_connectionString);
                    schema.Open();
                    using var create = schema.CreateCommand();
                    create.CommandText = """
                        PRAGMA journal_mode=WAL;
                        CREATE TABLE IF NOT EXISTS alarm_state (
                            tag_key TEXT PRIMARY KEY, state TEXT NOT NULL, threshold REAL NOT NULL
                        );
                        CREATE TABLE IF NOT EXISTS alarm_event (
                            seq INTEGER PRIMARY KEY AUTOINCREMENT, event_id TEXT NOT NULL UNIQUE,
                            device_id TEXT COLLATE NOCASE NOT NULL, body TEXT NOT NULL, delivery INTEGER NOT NULL,
                            queued_ticks INTEGER NOT NULL, ready_ticks INTEGER NOT NULL, topic TEXT NOT NULL,
                            payload TEXT NOT NULL, qos INTEGER NOT NULL, order_key TEXT NOT NULL
                        );
                        CREATE INDEX IF NOT EXISTS ix_alarm_pending ON alarm_event(seq) WHERE delivery=1;
                        CREATE INDEX IF NOT EXISTS ix_alarm_device ON alarm_event(device_id,seq);
                        CREATE INDEX IF NOT EXISTS ix_alarm_ready ON alarm_event(order_key,ready_ticks) WHERE delivery=1;
                        CREATE INDEX IF NOT EXISTS ix_alarm_retention ON alarm_event(queued_ticks) WHERE delivery<>1;
                        """;
                    create.ExecuteNonQuery();
                    _initialized = true;
                }
            }
        }
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    internal Dictionary<string, AlarmTagState> ReadStates()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT tag_key,state,threshold FROM alarm_state";
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, AlarmTagState>(StringComparer.Ordinal);
        while (reader.Read())
            result.Add(reader.GetString(0), new(reader.GetString(1), reader.GetDouble(2)));
        return result;
    }

    internal void SaveTransitions(List<GatewayAlarmRecord> events, Dictionary<string, AlarmTagState?> states)
    {
        if (states.Count == 0) return;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var insert = CreateInsert(connection, transaction);
        foreach (var item in events)
            Insert(insert, item, item.DeliveryStatus == "Pending");
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            INSERT INTO alarm_state(tag_key,state,threshold) VALUES($key,$state,$threshold)
            ON CONFLICT(tag_key) DO UPDATE SET state=excluded.state,threshold=excluded.threshold
            """;
        var key = update.Parameters.Add("$key", SqliteType.Text);
        var state = update.Parameters.Add("$state", SqliteType.Text);
        var threshold = update.Parameters.Add("$threshold", SqliteType.Real);
        using var remove = connection.CreateCommand();
        remove.Transaction = transaction;
        remove.CommandText = "DELETE FROM alarm_state WHERE tag_key=$key";
        var removedKey = remove.Parameters.Add("$key", SqliteType.Text);
        foreach (var entry in states)
        {
            if (entry.Value is not { } current)
            {
                removedKey.Value = entry.Key;
                remove.ExecuteNonQuery();
                continue;
            }
            key.Value = entry.Key;
            state.Value = current.State;
            threshold.Value = current.Threshold;
            update.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public string EnqueueRule(GatewayAlarmRecord record, bool publish)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.EventId)) record.EventId = Guid.NewGuid().ToString("N");
        record.Source = "rule";
        record.Timestamp = record.Timestamp == default ? DateTime.UtcNow : record.Timestamp.ToUniversalTime();
        using var connection = Open();
        using var insert = CreateInsert(connection, null);
        Insert(insert, record, publish);
        return record.EventId;
    }

    private static SqliteCommand CreateInsert(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO alarm_event(event_id,device_id,body,delivery,queued_ticks,ready_ticks,topic,payload,qos,order_key)
            VALUES($id,$device,$body,$delivery,$queued,
                CASE WHEN $key='' THEN $ready ELSE MAX($ready,COALESCE((SELECT MAX(ready_ticks)
                    FROM alarm_event WHERE delivery=1 AND order_key=$key),0)) END,$topic,$payload,$qos,$key)
            ON CONFLICT(event_id) DO NOTHING
            """;
        foreach (string name in new[] { "$id", "$device", "$body", "$topic", "$payload", "$key" })
            command.Parameters.Add(name, SqliteType.Text);
        foreach (string name in new[] { "$delivery", "$queued", "$ready", "$qos" })
            command.Parameters.Add(name, SqliteType.Integer);
        return command;
    }

    private static void Insert(SqliteCommand command, GatewayAlarmRecord record, bool publish)
    {
        var p = command.Parameters;
        p["$id"].Value = record.EventId;
        p["$device"].Value = record.DeviceId ?? string.Empty;
        p["$body"].Value = JsonSerializer.Serialize(record);
        p["$delivery"].Value = publish ? 1 : 0;
        p["$queued"].Value = DateTime.UtcNow.Ticks;
        p["$ready"].Value = record.NotBeforeUtc?.ToUniversalTime().Ticks ?? 0L;
        p["$topic"].Value = publish ? record.Topic : string.Empty;
        p["$payload"].Value = publish ? record.Payload : string.Empty;
        p["$qos"].Value = Math.Clamp(record.Qos, 1, 2);
        // A changed recovery delay cannot overtake the same rule/tag's pending activation.
        p["$key"].Value = publish && record.Source == "rule" ? JsonSerializer.Serialize(new[] { record.RuleId, record.DeviceId, record.TagId }) : string.Empty;
        command.ExecuteNonQuery();
    }

    internal void CleanupIfDue()
    {
        if (_retention == null || DateTime.UtcNow < _nextCleanup) return;
        var options = _retention();
        var storage = LocalHistoryStorageOptions.Normalize(options.Storage, options.RetentionDays);
        if (storage.AutoCleanupEnabled)
        {
            int days = storage.TieringEnabled && storage.RetentionPolicy == "MoveToColdThenDelete"
                ? storage.ColdRetentionDays : storage.HotRetentionDays;
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM alarm_event WHERE seq IN
                (SELECT seq FROM alarm_event WHERE delivery<>1 AND queued_ticks < $cutoff ORDER BY queued_ticks LIMIT 2000)
                """;
            command.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-days).Ticks);
            if (command.ExecuteNonQuery() == 2000)
            {
                _nextCleanup = DateTime.UtcNow.AddSeconds(10);
                return;
            }
        }
        _nextCleanup = DateTime.UtcNow.AddHours(storage.CleanupIntervalHours);
    }

    public GatewayAlarmPage ReadPage(int page, int pageSize, IReadOnlyCollection<string>? allowedDeviceIds = null)
    {
        if (allowedDeviceIds is { Count: 0 }) return new(0, Array.Empty<GatewayAlarmRecord>());
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        string filter = string.Empty;
        if (allowedDeviceIds != null)
        {
            filter = " WHERE device_id IN (SELECT value FROM json_each($devices))";
            command.Parameters.AddWithValue("$devices", JsonSerializer.Serialize(allowedDeviceIds));
        }
        command.CommandText = "SELECT COUNT(*) FROM alarm_event" + filter;
        long total = (long)command.ExecuteScalar()!;
        int size = Math.Clamp(pageSize, 1, 500);
        command.CommandText = "SELECT body,delivery FROM alarm_event" + filter + " ORDER BY seq DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$offset", (long)(Math.Max(1, page) - 1) * size);
        using var reader = command.ExecuteReader();
        return new(total, ReadRows(reader, false));
    }

    public IReadOnlyList<GatewayAlarmRecord> ReadPending(int count)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT body,delivery,topic,payload,qos FROM alarm_event
            WHERE delivery=1 AND ready_ticks <= $now ORDER BY seq LIMIT $count
            """;
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue("$count", Math.Clamp(count, 1, 1000));
        using var reader = command.ExecuteReader();
        return ReadRows(reader, true);
    }

    private static List<GatewayAlarmRecord> ReadRows(SqliteDataReader reader, bool withPayload)
    {
        var rows = new List<GatewayAlarmRecord>();
        while (reader.Read())
        {
            var row = JsonSerializer.Deserialize<GatewayAlarmRecord>(reader.GetString(0))!;
            row.DeliveryStatus = reader.GetInt32(1) switch { 1 => "Pending", 2 => "Sent", _ => "Local" };
            if (withPayload)
            {
                row.Topic = reader.GetString(2);
                row.Payload = reader.GetString(3);
                row.Qos = reader.GetInt32(4);
            }
            rows.Add(row);
        }
        return rows;
    }

    public void ConfirmSent(string eventId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE alarm_event SET delivery=2,topic='',payload='' WHERE event_id=$id AND delivery=1";
        command.Parameters.AddWithValue("$id", eventId);
        command.ExecuteNonQuery();
    }

    public (long Count, DateTime? OldestUtc) GetPendingStats()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(queued_ticks) FROM alarm_event WHERE delivery=1";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : new DateTime(reader.GetInt64(1), DateTimeKind.Utc));
    }
}
