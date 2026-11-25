using System.Data;
using System.Globalization;
using System.Text.Json;
using SqlSugar;

namespace IPC.EdgeGateway;

internal sealed partial class SharedAlarmJournal
{
    private static GatewayAlarmRecord Decode(DataRow row, long snapshot, bool includePayload)
    {
        var item = JsonSerializer.Deserialize<GatewayAlarmRecord>(Str(row, "body"))!;
        item.DeliveryStatus = Long(row["delivery"]) switch { 1 => "Pending", 2 => "Sent", _ => "Local" };
        item.OccurrenceTracked = Long(row["tracked"]) == 1;
        item.RequireManualHandling = Long(row["manual"]) == 1;
        item.IsSingleOccurrence = Long(row["singleevent"]) == 1;
        if (!row.IsNull("recovered") && Long(row["recoveryseq"]) <= snapshot)
        {
            item.RecoveredAtUtc = Utc(row["recovered"]);
            item.RecoveryValue = row.IsNull("recoveryvalue") ? null : Convert.ToDouble(row["recoveryvalue"], CultureInfo.InvariantCulture);
            item.RecoveryObservedAfterRestart = Long(row["restored"]) == 1;
        }
        if (!row.IsNull("ack")) item.Acknowledged = JsonSerializer.Deserialize<GatewayAlarmOperatorRecord>(Str(row, "ack"));
        if (!row.IsNull("handled")) item.Handled = JsonSerializer.Deserialize<GatewayAlarmOperatorRecord>(Str(row, "handled"));
        if (includePayload)
        {
            item.Topic = Str(row, "topic"); item.Payload = Str(row, "payload"); item.Qos = (int)Long(row["qos"]);
        }
        return item;
    }

    private static (string Where, List<SugarParameter> Params) Filter(GatewayAlarmQuery q, IReadOnlyCollection<string>? devices, long snapshot)
    {
        var clauses = new List<string> { "hidden=0", "seq<=@snapshot" };
        var args = Args(("snapshot", snapshot)).ToList();
        void Add(string clause, string param, object value) { clauses.Add(clause); args.Add(new(param, value)); }
        if (q.From.HasValue) Add("stamp>=@fromTicks", "@fromTicks", q.From.Value.UtcTicks);
        if (q.To.HasValue) Add("stamp<=@toTicks", "@toTicks", q.To.Value.UtcTicks);
        foreach (var (column, value) in new[] { ("chanid", q.ChannelId), ("devid", q.DeviceId), ("grpid", q.GroupId), ("tagid", q.TagId) })
            if (value != null) Add($"lower({column})=@{column}", "@" + column, value.ToLowerInvariant());
        if (devices != null)
        {
            var keys = devices.Select(d => d.ToLowerInvariant()).Distinct().ToArray();
            if (keys.Length == 0) clauses.Add("1=0");
            else
            {
                // Web reads currently authorize the entire gateway. Accept scoped callers without ever
                // widening their result; SqlSugar expands a collection parameter for this predicate.
                Add("lower(devid) in (@allowDevices)", "@allowDevices", keys);
            }
        }
        if (q.ProcessingStatus != null)
        {
            clauses.Add("activation=1");
            clauses.Add(q.ProcessingStatus switch
            {
                "AutoMonitoring" => "tracked=1 and manual=0 and singleevent=0 and (recovered is null or recoveryseq>@snapshot)",
                "AutoClosed" => "tracked=1 and manual=0 and singleevent=0 and recovered is not null and recoveryseq<=@snapshot",
                "Recorded" => "tracked=1 and manual=0 and singleevent=1",
                "Pending" => "manual=1 and stage=0",
                "Acknowledged" => "manual=1 and stage=1",
                "Handled" => "manual=1 and stage=2",
                _ => "1=0"
            });
        }
        return (" where " + string.Join(" and ", clauses.Select(s => "(" + s + ")")), args);
    }

    internal GatewayAlarmPage Page(GatewayAlarmQuery query, IReadOnlyCollection<string>? allowed, int size, long offset, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var db = factory.Create();
        db.Ado.CommandTimeOut = 10;
        long latest = Long(db.Ado.GetScalar($"select max(seq) from {Events}"));
        long snapshot = Math.Min(latest, query.Snapshot ?? latest);
        var filter = Filter(query, allowed, snapshot);
        long count = Long(db.Ado.GetScalar($"select count(*) from {Events}" + filter.Where, filter.Params.ToArray()));
        ct.ThrowIfCancellationRequested();
        var table = db.Ado.GetDataTable($"select * from {Events}" + filter.Where + " order by seq desc" + Slice(db, offset, size), filter.Params.ToArray());
        ct.ThrowIfCancellationRequested();
        return new(count, table.Rows.Cast<DataRow>().Select(r => Decode(r, snapshot, false)).ToArray(), snapshot);
    }

    internal IReadOnlyList<HistoryPointOption> Options(string level, GatewayAlarmQuery query, string? search, int limit,
        IReadOnlyCollection<string>? allowed, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var db = factory.Create();
        db.Ado.CommandTimeOut = 10;
        string prefix = level switch { "channel" => "chan", "device" => "dev", "group" => "grp", "tag" => "tag", _ => throw new ArgumentException("Invalid alarm option.") };
        var filter = Filter(query, allowed, query.Snapshot ?? long.MaxValue);
        string where = filter.Where + (level == "group" ? "" : $" and {prefix}id<>''");
        string path = db.CurrentConnectionConfig.DbType == SqlSugar.DbType.Sqlite
            ? "channame || '.' || devname || case when grpname='' then '' else '.'||grpname end || case when tagname='' then '' else '.'||tagname end"
            : "concat(channame,'.',devname,case when grpname='' then '' else concat('.',grpname) end,case when tagname='' then '' else concat('.',tagname) end)";
        string sql = $"""
            select * from (
                select {prefix}id as optionid,{prefix}name as optionname,{path} as optionpath from {Events} e
                join (select max(seq) as latest from {Events}{where} group by lower({prefix}id)) l on e.seq=l.latest
            ) optionset
            """;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant().Replace("!", "!!").Replace("%", "!%").Replace("_", "!_").Replace("[", "![");
            sql += " where lower(optionname) like @term escape '!' or lower(optionpath) like @term escape '!'";
            filter.Params.Add(new("@term", "%" + term + "%"));
        }
        var rows = db.Ado.GetDataTable(sql + " order by optionname,optionid" + Slice(db, 0, limit), filter.Params.ToArray());
        ct.ThrowIfCancellationRequested();
        return rows.Rows.Cast<DataRow>().Select(r => new HistoryPointOption(Str(r, "optionid"), Str(r, "optionname"), level == "tag" ? Str(r, "optionpath") : null)).ToArray();
    }
}
