using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed record GatewayAlarmOperatorRecord(string Operator, DateTime TimestampUtc, string Note);

public enum AlarmOperatorAction { Acknowledge = 1, Handle = 2 }

public enum AlarmOperatorOutcome { Updated, AlreadyRecorded, NotFound, NotAnActivation, AcknowledgeRequired }

public sealed partial class GatewayAlarmJournal
{
    private const string ActiveEventSql = "lower(coalesce(json_extract(body,'$.EventType'),'')) IN ('alarm','warning')";

    // Capture the activation/recovery link at ingestion, where the source has
    // the point identity and alarm output identity. Older entries remain
    // actionable, but their link to a recovery is unknown.
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
        CREATE TRIGGER IF NOT EXISTS track_alarm_occurrence AFTER INSERT ON alarm_event
        WHEN json_extract(NEW.body,'$.AlarmCorrelationKey') IS NOT NULL
        BEGIN
            INSERT INTO alarm_occurrence(event_id,correlation_key)
                SELECT NEW.event_id,json_extract(NEW.body,'$.AlarmCorrelationKey')
                WHERE lower(json_extract(NEW.body,'$.EventType')) IN ('alarm','warning');
            UPDATE alarm_occurrence SET recovered_utc=json_extract(NEW.body,'$.Timestamp')
                WHERE lower(json_extract(NEW.body,'$.EventType'))='recovery' AND recovered_utc IS NULL
                    AND correlation_key=json_extract(NEW.body,'$.AlarmCorrelationKey');
        END;
        """;

    public AlarmOperatorOutcome ApplyOperatorAction(string eventId, AlarmOperatorAction action, string operatorId,
        string operatorName, string? note, CancellationToken cancellationToken = default)
    {
        note = note?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 256 || string.IsNullOrWhiteSpace(operatorId) ||
            action is not (AlarmOperatorAction.Acknowledge or AlarmOperatorAction.Handle) ||
            note.Length > 1000 || action == AlarmOperatorAction.Handle && note.Length == 0)
            throw new ArgumentException("Invalid alarm action.");

        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Open();
        using var interrupted = HistoryValueIndex.InterruptOnCancel(connection, cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", eventId);
        command.CommandText = $"""
            SELECT {ActiveEventSql}, COALESCE((SELECT MAX(stage) FROM alarm_operator_action WHERE event_id=$id),0)
            FROM alarm_event WHERE event_id=$id
            """;
        int stage;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return AlarmOperatorOutcome.NotFound;
            if (!reader.GetBoolean(0)) return AlarmOperatorOutcome.NotAnActivation;
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
