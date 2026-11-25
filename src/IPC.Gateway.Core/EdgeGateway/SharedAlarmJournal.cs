using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IPC.Gateway.Core.Infrastructure.Persistence;
using SqlSugar;

namespace IPC.EdgeGateway;

internal sealed partial class SharedAlarmJournal(SqlSugarConnectionFactory factory, IGatewayDatabaseOwner owner)
{
    private const string Events = "gateway_alarm_event";
    private static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    private static string Slice(ISqlSugarClient db, long offset, int count) =>
        db.CurrentConnectionConfig.DbType == SqlSugar.DbType.SqlServer
            ? $" OFFSET {offset} ROWS FETCH NEXT {count} ROWS ONLY" : $" LIMIT {count} OFFSET {offset}";

    private static SugarParameter[] Args(params (string Name, object? Value)[] pairs) =>
        pairs.Select(p => new SugarParameter("@" + p.Name, p.Value ?? DBNull.Value)).ToArray();
    private static long Long(object? value) => value is null or DBNull ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    private static DateTime Utc(object ticks) => new(Long(ticks), DateTimeKind.Utc);
    private static string Str(DataRow row, string name) => Convert.ToString(row[name], CultureInfo.InvariantCulture) ?? "";

    private T Write<T>(Func<ISqlSugarClient, T> work, CancellationToken cancellationToken = default, long? expectedEpoch = null)
    {
        long epoch = expectedEpoch ?? owner.Epoch;
        using var db = factory.Create();
        cancellationToken.ThrowIfCancellationRequested();
        db.Ado.BeginTran();
        try
        {
            // Hold the same owner-row lock used when installing B's epoch until this mutation commits.
            GatewayDatabaseOwnership.GuardTransaction(db, owner);
            if (!owner.CanRunBusiness || epoch != owner.Epoch)
                throw new IPC.Plc.Communication.Core.PlcWriteDeniedException("HA_STALE_EPOCH");
            T result = work(db);
            cancellationToken.ThrowIfCancellationRequested();
            db.Ado.CommitTran();
            return result;
        }
        catch { db.Ado.RollbackTran(); throw; }
    }

    internal Dictionary<string, AlarmTagState> ReadStates()
    {
        using var db = factory.Create();
        return db.Ado.GetDataTable("select tagkey,alarmstate,thresholdvalue from gateway_alarm_threshold").Rows.Cast<DataRow>()
            .ToDictionary(r => Str(r, "tagkey"), r => new AlarmTagState(Str(r, "alarmstate"), Convert.ToDouble(r["thresholdvalue"], CultureInfo.InvariantCulture)), StringComparer.Ordinal);
    }

    internal void SaveTransitions(List<GatewayAlarmRecord> events, Dictionary<string, AlarmTagState?> states, long epoch)
    {
        Write(db =>
        {
            foreach (var item in events) Insert(db, item, item.DeliveryStatus == "Pending");
            foreach (var (key, value) in states)
            {
                var hash = Hash(key);
                db.Ado.ExecuteCommand("delete from gateway_alarm_threshold where keyhash=@hash", Args(("hash", hash)));
                if (value is { } s)
                    db.Ado.ExecuteCommand("insert into gateway_alarm_threshold(keyhash,tagkey,alarmstate,thresholdvalue) values(@hash,@key,@state,@value)",
                        Args(("hash", hash), ("key", key), ("state", s.State), ("value", s.Threshold)));
            }
            return 0;
        }, expectedEpoch: epoch);
    }

    internal void Enqueue(GatewayAlarmRecord item, bool publish) => Write(db => { Insert(db, item, publish); return 0; });

    private static void Insert(ISqlSugarClient db, GatewayAlarmRecord item, bool publish)
    {
        var id = Args(("id", item.EventId));
        if (db.Ado.GetInt($"select count(*) from {Events} where eventid=@id", id) != 0) return;
        bool active = GatewayAlarmRecord.IsActiveEvent(item.EventType);
        bool recovery = string.Equals(item.EventType, "recovery", StringComparison.OrdinalIgnoreCase);
        bool correlated = !string.IsNullOrEmpty(item.AlarmCorrelationKey);
        string corr = Hash(item.AlarmCorrelationKey ?? item.EventId);
        long stamp = item.Timestamp.ToUniversalTime().Ticks;
        bool hidden = item.DeliveryOnly || recovery && correlated;
        bool track = !item.DeliveryOnly && active && (correlated || item.IsSingleOccurrence);
        if (track && !item.ClosePreviousOccurrence && !item.IsSingleOccurrence &&
            db.Ado.GetInt($"select count(*) from {Events} where corr=@corr and tracked=1 and singleevent=0 and recovered is null", Args(("corr", corr))) > 0)
        { track = false; hidden = true; }
        string sortKey = publish && item.Source == "rule" ? Hash(JsonSerializer.Serialize(new[] { item.RuleId, item.DeviceId, item.TagId })) : "";
        long ready = item.NotBeforeUtc?.ToUniversalTime().Ticks ?? 0;
        if (sortKey.Length > 0)
            ready = Math.Max(ready, Long(db.Ado.GetScalar($"select max(ready) from {Events} where sortkey=@k and delivery=1", Args(("k", sortKey)))));
        db.Ado.ExecuteCommand($"""
            insert into {Events}(eventid,stamp,queued,ready,delivery,sortkey,topic,payload,qos,body,hidden,activation,tracked,corr,
                corrname,manual,singleevent,restored,stage,chanid,devid,grpid,tagid,channame,devname,grpname,tagname)
            values(@id,@stamp,@queued,@ready,@delivery,@sortkey,@topic,@payload,@qos,@body,@hidden,@active,@tracked,@corr,
                @corrname,@manual,@single,0,0,@cid,@did,@gid,@tid,@cn,@dn,@gn,@tn)
            """, Args(("id", item.EventId), ("stamp", stamp), ("queued", DateTime.UtcNow.Ticks), ("ready", ready), ("delivery", publish ? 1 : 0),
                ("sortkey", sortKey), ("topic", publish ? item.Topic : ""), ("payload", publish ? item.Payload : ""), ("qos", Math.Clamp(item.Qos, 1, 2)),
                ("body", JsonSerializer.Serialize(item)), ("hidden", hidden ? 1 : 0), ("active", active ? 1 : 0), ("tracked", track ? 1 : 0), ("corr", corr),
                ("corrname", item.AlarmCorrelationKey ?? ""), ("manual", item.RequireManualHandling ? 1 : 0), ("single", item.IsSingleOccurrence ? 1 : 0),
                ("cid", item.ChannelId ?? ""), ("did", item.DeviceId ?? ""), ("gid", item.GroupId ?? ""), ("tid", item.TagId ?? ""),
                ("cn", item.ChannelName ?? ""), ("dn", item.DeviceName ?? ""), ("gn", item.GroupName ?? ""), ("tn", item.TagName ?? "")));
        if (item.DeliveryOnly || (!correlated && !item.IsSingleOccurrence) || !(recovery || item.ClosePreviousOccurrence)) return;
        long seq = Long(db.Ado.GetScalar($"select seq from {Events} where eventid=@id", id));
        db.Ado.ExecuteCommand($"""
            update {Events} set recovered=@stamp,recoveryvalue=@value,restored=@restart,recoveryseq=@seq
            where corr=@corr and tracked=1 and singleevent=0 and recovered is null and stamp<=@stamp and seq<@seq
            """, Args(("stamp", stamp), ("value", item.Value), ("restart", item.RecoveryObservedAfterRestart ? 1 : 0), ("seq", seq), ("corr", corr)));
        // Out-of-order replacement cannot create a second open occurrence for the same rule/tag.
        if (track && item.ClosePreviousOccurrence && !item.IsSingleOccurrence &&
            db.Ado.GetInt($"select count(*) from {Events} where corr=@corr and seq<@seq and tracked=1 and recovered is null and singleevent=0", Args(("corr", corr), ("seq", seq))) > 0)
            db.Ado.ExecuteCommand($"update {Events} set tracked=0,hidden=1 where seq=@seq", Args(("seq", seq)));
    }

    internal IReadOnlyList<GatewayAlarmRecord> ReadOpenRules()
    {
        using var db = factory.Create();
        return db.Ado.GetDataTable($"select body,corrname from {Events} where tracked=1 and singleevent=0 and recovered is null and corrname like 'rule2:%' order by seq").Rows.Cast<DataRow>()
            .Select(r => { var item = JsonSerializer.Deserialize<GatewayAlarmRecord>(Str(r, "body"))!; item.AlarmCorrelationKey = Str(r, "corrname"); return item; }).ToArray();
    }

    internal IReadOnlyList<GatewayAlarmRecord> Pending(int count)
    {
        using var db = factory.Create();
        var table = db.Ado.GetDataTable($"select * from {Events} where delivery=1 and ready<=@now order by seq" + Slice(db, 0, Math.Clamp(count, 1, 1000)),
            Args(("now", DateTime.UtcNow.Ticks)));
        return table.Rows.Cast<DataRow>().Select(r => Decode(r, long.MaxValue, true)).ToArray();
    }

    internal void ConfirmSent(string id,long? expectedEpoch) =>
        Write(db => db.Ado.ExecuteCommand($"update {Events} set delivery=2,topic='',payload='' where eventid=@id and delivery=1", Args(("id", id))), expectedEpoch:expectedEpoch);

    internal (long Count, DateTime? OldestUtc) PendingStats()
    {
        using var db = factory.Create();
        var row = db.Ado.GetDataTable($"select count(*) as amount,min(queued) as oldest from {Events} where delivery=1").Rows[0];
        return (Long(row["amount"]), row.IsNull("oldest") ? null : Utc(row["oldest"]));
    }

    internal AlarmOperatorOutcome ApplyAction(string id, AlarmOperatorAction action, string operatorId, string name, string note, CancellationToken ct)
    {
        return Write(db =>
        {
            var table = db.Ado.GetDataTable($"select activation,manual,stage from {Events} where eventid=@id and hidden=0", Args(("id", id)));
            if (table.Rows.Count == 0) return AlarmOperatorOutcome.NotFound;
            var r = table.Rows[0];
            if (Long(r["activation"]) == 0) return AlarmOperatorOutcome.NotAnActivation;
            if (Long(r["manual"]) == 0) return AlarmOperatorOutcome.Automatic;
            long stage = Long(r["stage"]);
            if (stage >= (int)action) return AlarmOperatorOutcome.AlreadyRecorded;
            if (action == AlarmOperatorAction.Handle && stage != 1) return AlarmOperatorOutcome.AcknowledgeRequired;
            var stamp = DateTime.UtcNow;
            string audit = JsonSerializer.Serialize(new { OperatorId = operatorId, Operator = string.IsNullOrWhiteSpace(name) ? operatorId : name, TimestampUtc = stamp, Note = note });
            string assignments = action == AlarmOperatorAction.Handle ? "handled=@body,handledat=@stamp" : "ack=@body";
            var args = Args(("stage", (int)action), ("id", id), ("body", audit)).ToList();
            if (action == AlarmOperatorAction.Handle) args.Add(new("@stamp", stamp.Ticks));
            db.Ado.ExecuteCommand($"update {Events} set stage=@stage,{assignments} where eventid=@id", args.ToArray());
            return AlarmOperatorOutcome.Updated;
        }, ct);
    }

    internal int Cleanup(long cutoff) => Write(db =>
    {
        var rows = db.Ado.GetDataTable($"""
            select seq from {Events} where delivery<>1 and queued<@cutoff and
            (hidden=1 or activation=0 or ((tracked=0 or singleevent=1 or recovered<@cutoff) and
            ((tracked=1 and manual=0) or (stage=2 and handledat<@cutoff)))) order by seq
            """ + Slice(db, 0, 2000), Args(("cutoff", cutoff)));
        if (rows.Rows.Count != 0)
        {
            var sequences = rows.Rows.Cast<DataRow>().Select(r => Long(r["seq"]).ToString(CultureInfo.InvariantCulture));
            db.Ado.ExecuteCommand($"delete from {Events} where seq in ({string.Join(",", sequences)})");
        }
        return rows.Rows.Count;
    });
}
