using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace IPC.EdgeGateway;

public sealed record GatewayAlarmOperatorRecord(string Operator, DateTime TimestampUtc, string Note);

public enum AlarmOperatorAction { Acknowledge = 1, Handle = 2 }

public enum AlarmOperatorOutcome { Updated, AlreadyRecorded, NotFound, NotAnActivation, AcknowledgeRequired, Automatic }

public sealed partial class GatewayAlarmJournal
{
    private const string ActiveEventSql = "lower(coalesce(json_extract(body,'$.EventType'),'')) IN ('alarm','warning')";

    private const string ProcessingSchema = """
        CREATE TABLE IF NOT EXISTS alarm_occurrence (
            event_id TEXT PRIMARY KEY REFERENCES alarm_event(event_id) ON DELETE CASCADE,
            correlation_key TEXT NOT NULL, recovered_utc TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_alarm_occurrence_open
            ON alarm_occurrence(correlation_key) WHERE recovered_utc IS NULL;
        CREATE TABLE IF NOT EXISTS alarm_operator_action (
            event_id TEXT NOT NULL REFERENCES alarm_event(event_id) ON DELETE CASCADE,
            stage INTEGER NOT NULL CHECK(stage IN (1,2)),
            operator_id TEXT NOT NULL, operator_name TEXT NOT NULL, timestamp_ticks INTEGER NOT NULL, note TEXT NOT NULL,
            PRIMARY KEY(event_id,stage)
        );
        """;

    private static void InitializeProcessing(SqliteConnection db)
    {
        using var tx = db.BeginTransaction();
        using var sql = db.CreateCommand();
        sql.Transaction = tx;
        sql.CommandText = ProcessingSchema;
        sql.ExecuteNonQuery();
        sql.CommandText = "SELECT count(*) FROM pragma_table_info('alarm_occurrence') WHERE name='recovered_seq'";
        if ((long)sql.ExecuteScalar()! == 0)
        {
            sql.CommandText = "ALTER TABLE alarm_occurrence ADD COLUMN recovered_seq INTEGER";
            sql.ExecuteNonQuery();
        }
        sql.CommandText = "SELECT count(*) FROM pragma_table_info('alarm_event') WHERE name='history_hidden'";
        if ((long)sql.ExecuteScalar()! == 0)
        {
            // Retain original raw MQTT rows and operator audit. Only fold historical
            // recoveries that the previous journal had already linked to an activation.
            sql.CommandText = """
                DROP TRIGGER IF EXISTS track_alarm_occurrence;
                ALTER TABLE alarm_event ADD COLUMN history_hidden INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE alarm_occurrence ADD COLUMN manual INTEGER NOT NULL DEFAULT 1;
                ALTER TABLE alarm_occurrence ADD COLUMN single_event INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE alarm_occurrence ADD COLUMN recovery_value REAL;
                ALTER TABLE alarm_occurrence ADD COLUMN recovered_after_restart INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE alarm_occurrence ADD COLUMN closed_ticks INTEGER;
                CREATE TEMP TABLE previous_recoveries AS
                    SELECT seq,event_id,json_extract(body,'$.AlarmCorrelationKey') AS key,json_extract(body,'$.Timestamp') AS stamp,
                        json_extract(body,'$.Value') AS value,queued_ticks FROM alarm_event
                    WHERE lower(json_extract(body,'$.EventType'))='recovery' AND json_extract(body,'$.AlarmCorrelationKey') IS NOT NULL;
                CREATE INDEX temp.recovery_lookup ON previous_recoveries(key,stamp);
                CREATE TEMP TABLE previous_links AS
                    SELECT o.event_id AS activation,c.event_id AS recovery,c.value,c.queued_ticks,c.seq
                    FROM alarm_occurrence o JOIN alarm_event a ON a.event_id=o.event_id
                    JOIN previous_recoveries c ON c.key=o.correlation_key AND c.stamp=o.recovered_utc AND c.seq>a.seq;
                CREATE INDEX temp.activation_lookup ON previous_links(activation);
                UPDATE alarm_occurrence SET (recovery_value,closed_ticks,recovered_seq) = (
                    SELECT value,queued_ticks,seq FROM previous_links WHERE activation=alarm_occurrence.event_id ORDER BY seq LIMIT 1)
                    WHERE recovered_utc IS NOT NULL;
                UPDATE alarm_event SET history_hidden=1 WHERE event_id IN (SELECT recovery FROM previous_links);
                UPDATE alarm_occurrence SET manual=0 WHERE NOT EXISTS(
                    SELECT 1 FROM alarm_operator_action a WHERE a.event_id=alarm_occurrence.event_id);
                DROP TABLE previous_links;
                DROP TABLE previous_recoveries;
                CREATE INDEX ix_alarm_visible ON alarm_event(seq) WHERE history_hidden=0;
                """;
            sql.ExecuteNonQuery();
            // Earlier rule records used a rule/action/source compound ID. Preserve
            // their original payload and only translate keys still awaiting recovery.
            sql.CommandText = "SELECT DISTINCT correlation_key FROM alarm_occurrence WHERE recovered_utc IS NULL AND correlation_key LIKE 'rule:%'";
            var oldKeys = new List<string>();
            using (var reader = sql.ExecuteReader()) while (reader.Read()) oldKeys.Add(reader.GetString(0));
            sql.CommandText = "UPDATE alarm_occurrence SET correlation_key=$new WHERE correlation_key=$old AND recovered_utc IS NULL";
            var oldKey = sql.Parameters.Add("$old", SqliteType.Text);
            var newKey = sql.Parameters.Add("$new", SqliteType.Text);
            foreach (var old in oldKeys)
            {
                try
                {
                    var parts = JsonSerializer.Deserialize<string[]>(old[5..]);
                    if (parts is not { Length: 6 } || string.IsNullOrWhiteSpace(parts[0]) ||
                        string.IsNullOrWhiteSpace(parts[3]) || string.IsNullOrWhiteSpace(parts[5])) continue;
                    oldKey.Value = old;
                    newKey.Value = "rule2:" + JsonSerializer.Serialize(new[] { parts[0], "legacy", parts[2], parts[3], parts[4], parts[5] });
                    sql.ExecuteNonQuery();
                }
                catch (JsonException) { /* Preserve unrecognized historical identities. */ }
            }
        }
        tx.Commit();
    }

    private static void TrackOccurrence(SqliteConnection db, SqliteTransaction tx, GatewayAlarmRecord item)
    {
        using var sql = db.CreateCommand();
        sql.Transaction = tx;
        sql.Parameters.AddWithValue("$id", item.EventId);
        void Hide()
        {
            sql.CommandText = "UPDATE alarm_event SET history_hidden=1 WHERE event_id=$id";
            sql.ExecuteNonQuery();
        }
        if (item.DeliveryOnly) { Hide(); return; }
        bool active = GatewayAlarmRecord.IsActiveEvent(item.EventType);
        bool clear = string.Equals(item.EventType, "recovery", StringComparison.OrdinalIgnoreCase);
        bool correlated = !string.IsNullOrEmpty(item.AlarmCorrelationKey);
        if ((!active && !clear) || (!correlated && !item.IsSingleOccurrence)) return;
        sql.Parameters.AddWithValue("$key", item.AlarmCorrelationKey ?? item.EventId);
        if (clear || item.ClosePreviousOccurrence)
        {
            sql.CommandText = $"""
                UPDATE alarm_occurrence SET recovered_utc=$time, recovery_value=$value, recovered_after_restart=$restart,
                    closed_ticks=$ticks, recovered_seq=(SELECT seq FROM alarm_event WHERE event_id=$id)
                WHERE correlation_key=$key AND recovered_utc IS NULL AND single_event=0
                    AND (SELECT {EventTimestamp} FROM alarm_event WHERE event_id=alarm_occurrence.event_id)<=$stamp
                """;
            sql.Parameters.AddWithValue("$time", item.Timestamp.ToUniversalTime().ToString("O"));
            sql.Parameters.AddWithValue("$stamp", item.Timestamp.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
            sql.Parameters.AddWithValue("$ticks", item.Timestamp.ToUniversalTime().Ticks);
            sql.Parameters.AddWithValue("$value", (object?)item.Value ?? DBNull.Value);
            sql.Parameters.AddWithValue("$restart", item.RecoveryObservedAfterRestart);
            sql.ExecuteNonQuery();
            if (clear)
            {
                // A recovery with no open occurrence can follow a retry or a period with
                // history disabled. It remains deliverable without creating an orphan row.
                Hide();
                return;
            }
        }
        sql.CommandText = """
            INSERT INTO alarm_occurrence(event_id,correlation_key,manual,single_event)
            SELECT $id,$key,$manual,$single WHERE $single OR NOT EXISTS(
                SELECT 1 FROM alarm_occurrence WHERE correlation_key=$key AND recovered_utc IS NULL AND single_event=0)
            """;
        sql.Parameters.AddWithValue("$manual", item.RequireManualHandling);
        sql.Parameters.AddWithValue("$single", item.IsSingleOccurrence);
        if (sql.ExecuteNonQuery() == 0) Hide();
    }

    public IReadOnlyList<GatewayAlarmRecord> ReadOpenRuleAlarms()
    {
        if (Shared is { } shared) return shared.ReadOpenRules();
        using var db = Open();
        using var sql = db.CreateCommand();
        sql.CommandText = """
            SELECT e.body,o.correlation_key FROM alarm_occurrence o JOIN alarm_event e ON e.event_id=o.event_id
            WHERE o.recovered_utc IS NULL AND o.single_event=0 AND o.correlation_key LIKE 'rule2:%'
            ORDER BY e.seq
            """;
        using var reader = sql.ExecuteReader();
        var result = new List<GatewayAlarmRecord>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<GatewayAlarmRecord>(reader.GetString(0))!;
            item.AlarmCorrelationKey = reader.GetString(1);
            result.Add(item);
        }
        return result;
    }

    public AlarmOperatorOutcome ApplyOperatorAction(string eventId, AlarmOperatorAction action, string operatorId,
        string operatorName, string? note, CancellationToken cancellationToken = default)
    {
        note = note?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 256 || string.IsNullOrWhiteSpace(operatorId) ||
            action is not (AlarmOperatorAction.Acknowledge or AlarmOperatorAction.Handle) ||
            note.Length > 1000 || action == AlarmOperatorAction.Handle && note.Length == 0)
            throw new ArgumentException("Invalid alarm action.");

        cancellationToken.ThrowIfCancellationRequested();
        if (Shared is { } shared) return shared.ApplyAction(eventId,action,operatorId,operatorName,note,cancellationToken);
        using var connection = Open();
        using var interrupted = HistoryValueIndex.InterruptOnCancel(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", eventId);
        command.CommandText = $"""
            SELECT {ActiveEventSql}, COALESCE((SELECT MAX(stage) FROM alarm_operator_action WHERE event_id=$id),0),
                COALESCE((SELECT manual FROM alarm_occurrence WHERE event_id=$id),1)
            FROM alarm_event WHERE event_id=$id AND history_hidden=0
            """;
        int stage;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return AlarmOperatorOutcome.NotFound;
            if (!reader.GetBoolean(0)) return AlarmOperatorOutcome.NotAnActivation;
            if (!reader.GetBoolean(2)) return AlarmOperatorOutcome.Automatic;
            stage = reader.GetInt32(1);
        }
        if (stage >= (int)action) return AlarmOperatorOutcome.AlreadyRecorded;
        if (action == AlarmOperatorAction.Handle && stage != (int)AlarmOperatorAction.Acknowledge)
            return AlarmOperatorOutcome.AcknowledgeRequired;

        command.CommandText = """
            INSERT INTO alarm_operator_action(event_id,stage,operator_id,operator_name,timestamp_ticks,note)
            VALUES($id,$stage,$operator,$name,$time,$note)
            """;
        command.Parameters.AddWithValue("$stage", (int)action);
        command.Parameters.AddWithValue("$operator", operatorId);
        command.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(operatorName) ? operatorId : operatorName);
        command.Parameters.AddWithValue("$time", DateTime.UtcNow.Ticks);
        command.Parameters.AddWithValue("$note", note);
        command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return AlarmOperatorOutcome.Updated;
    }

    private static GatewayAlarmOperatorRecord? ReadOperatorRecord(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : new(reader.GetString(index),
            new DateTime(reader.GetInt64(index + 1), DateTimeKind.Utc), reader.GetString(index + 2));
}
