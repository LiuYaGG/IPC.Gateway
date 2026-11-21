using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed record GatewayAlarmQuery
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? ChannelId { get; init; }
    public string? DeviceId { get; init; }
    /// <summary>Null means any group; an explicit empty string means device-direct tags.</summary>
    public string? GroupId { get; init; }
    public string? TagId { get; init; }
    public long? Snapshot { get; init; }
    public string? ProcessingStatus { get; init; }
}

public sealed partial class GatewayAlarmJournal
{
    // Records have always been serialized as UTC. Pad their optional fractional seconds to
    // seven digits for exact comparisons without SQLite's millisecond date-function rounding.
    private const string EventTimestamp = "substr(replace(json_extract(body,'$.Timestamp'),'Z','') || CASE WHEN instr(json_extract(body,'$.Timestamp'),'.')=0 THEN '.' ELSE '' END || '0000000',1,27)";

    public GatewayAlarmPage ReadPage(int page, int pageSize, IReadOnlyCollection<string>? allowedDeviceIds = null,
        GatewayAlarmQuery? query = null, CancellationToken cancellationToken = default) =>
        ReadResult(query ?? new(), allowedDeviceIds, Math.Clamp(pageSize, 1, 500),
            (long)(Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 500), cancellationToken);

    private GatewayAlarmPage ReadResult(GatewayAlarmQuery query, IReadOnlyCollection<string>? allowedDeviceIds,
        int size, long offset, CancellationToken cancellationToken)
    {
        ValidateQuery(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (allowedDeviceIds is { Count: 0 }) return new(0, [], query.Snapshot ?? 0);
        using var connection = Open();
        using var interrupted = HistoryValueIndex.InterruptOnCancel(connection, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(seq),0) FROM alarm_event";
        long latest = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        long snapshot = Math.Min(query.Snapshot ?? latest, latest);
        string filter = AddFilters(command, query with { Snapshot = snapshot }, allowedDeviceIds);
        command.CommandText = "SELECT COUNT(*) FROM alarm_event" + filter;
        long total = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        cancellationToken.ThrowIfCancellationRequested();
        command.CommandText = """
            SELECT body,delivery,occ.event_id IS NOT NULL,occ.recovered_utc,
                ack.operator_name,ack.timestamp_ticks,ack.note,handled.operator_name,handled.timestamp_ticks,handled.note
            FROM alarm_event
            LEFT JOIN alarm_occurrence occ ON occ.event_id=alarm_event.event_id
            LEFT JOIN alarm_operator_action ack ON ack.event_id=alarm_event.event_id AND ack.stage=1
            LEFT JOIN alarm_operator_action handled ON handled.event_id=alarm_event.event_id AND handled.stage=2
            """ + filter + " ORDER BY seq DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$offset", offset);
        using var reader = command.ExecuteReader();
        var rows = ReadRows(reader, false, cancellationToken, withProcessing: true);
        cancellationToken.ThrowIfCancellationRequested();
        return new(total, rows, snapshot);
    }

    public IReadOnlyList<HistoryPointOption> ReadOptions(string level, GatewayAlarmQuery query, string? search = null,
        int limit = 200, IReadOnlyCollection<string>? allowedDeviceIds = null, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        if (level is not ("channel" or "device" or "group" or "tag")) throw new ArgumentException("报警筛选层级无效。");
        if (search?.Length > 256 || limit is < 1 or > 1000) throw new ArgumentException("报警筛选参数无效。");
        cancellationToken.ThrowIfCancellationRequested();
        if (allowedDeviceIds is { Count: 0 }) return [];
        using var connection = Open();
        using var interrupted = HistoryValueIndex.InterruptOnCancel(connection, cancellationToken);
        using var command = connection.CreateCommand();
        string field = level switch { "channel" => "Channel", "device" => "Device", "group" => "Group", _ => "Tag" };
        string id = $"COALESCE(json_extract(body,'$.{field}Id'),'')";
        string name = $"COALESCE(json_extract(body,'$.{field}Name'),'')";
        string path = "COALESCE(json_extract(body,'$.ChannelName'),'') || '.' || COALESCE(json_extract(body,'$.DeviceName'),'') || " +
            "CASE WHEN COALESCE(json_extract(body,'$.GroupName'),'')='' THEN '' ELSE '.' || json_extract(body,'$.GroupName') END || " +
            "CASE WHEN COALESCE(json_extract(body,'$.TagName'),'')='' THEN '' ELSE '.' || json_extract(body,'$.TagName') END";
        string filter = AddFilters(command, query, allowedDeviceIds);
        if (level != "group") filter += " AND " + id + "<>''";
        // MAX(seq) makes SQLite select the name/path from the latest record for a renamed ID.
        command.CommandText = $"SELECT id,name,path FROM (SELECT {id} AS id,{name} AS name,{path} AS path,MAX(seq) AS latest FROM alarm_event{filter} GROUP BY {id} COLLATE NOCASE)";
        if (!string.IsNullOrWhiteSpace(search))
        {
            command.CommandText += " WHERE instr(lower(name),lower($search))>0 OR instr(lower(path),lower($search))>0";
            command.Parameters.AddWithValue("$search", search.Trim());
        }
        command.CommandText += " ORDER BY name COLLATE NOCASE,id COLLATE NOCASE LIMIT $limit";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = command.ExecuteReader();
        var options = new List<HistoryPointOption>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            options.Add(new(reader.GetString(0), reader.GetString(1), level == "tag" ? reader.GetString(2) : null));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return options;
    }

    private static void ValidateQuery(GatewayAlarmQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.From > query.To || query.Snapshot < 0 || query.ProcessingStatus is not (null or "Pending" or "Acknowledged" or "Handled") ||
            new[] { query.ChannelId, query.DeviceId, query.GroupId, query.TagId }.Any(id => id?.Length > 256))
            throw new ArgumentException("报警查询参数无效。");
    }

    private static string AddFilters(SqliteCommand command, GatewayAlarmQuery query, IReadOnlyCollection<string>? allowedDeviceIds)
    {
        var parts = new List<string> { "1=1" };
        void Add(string sql, string parameter, object value) { parts.Add(sql); command.Parameters.AddWithValue(parameter, value); }
        if (allowedDeviceIds != null)
            Add("device_id IN (SELECT value FROM json_each($devices))", "$devices", JsonSerializer.Serialize(allowedDeviceIds));
        if (query.Snapshot.HasValue) Add("seq<=$snapshot", "$snapshot", query.Snapshot.Value);
        if (query.From.HasValue) Add(EventTimestamp + ">=$from", "$from", query.From.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        if (query.To.HasValue) Add(EventTimestamp + "<=$to", "$to", query.To.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
        if (query.ChannelId != null) Add("COALESCE(json_extract(body,'$.ChannelId'),'')=$channel COLLATE NOCASE", "$channel", query.ChannelId);
        if (query.DeviceId != null) Add("device_id=$device COLLATE NOCASE", "$device", query.DeviceId);
        if (query.GroupId != null) Add("COALESCE(json_extract(body,'$.GroupId'),'')=$group COLLATE NOCASE", "$group", query.GroupId);
        if (query.TagId != null) Add("COALESCE(json_extract(body,'$.TagId'),'')=$tag COLLATE NOCASE", "$tag", query.TagId);
        if (query.ProcessingStatus != null)
        {
            parts.Add("(" + ActiveEventSql + ")");
            int stage = query.ProcessingStatus switch { "Pending" => 0, "Acknowledged" => 1, _ => 2 };
            Add("COALESCE((SELECT MAX(stage) FROM alarm_operator_action oa WHERE oa.event_id=alarm_event.event_id),0)=$stage", "$stage", stage);
        }
        return " WHERE " + string.Join(" AND ", parts);
    }
}
