using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SqlSugar;

namespace IPC.EdgeGateway;

internal sealed partial class SharedAlarmJournal
{
    // Both gateways call this after stopping their standalone business workers, before acknowledging
    // enrollment. The live business epoch cannot coexist with an unsealed bootstrap import.
    internal void ImportStandalone(string cluster, string node, Func<SqliteConnection?> openLocal)
    {
        using var db = factory.Create();
        db.Ado.BeginTran();
        try
        {
            bool Done() => db.Ado.GetInt("select count(*) from gateway_alarm_import where nodeid=@node and clusterid=@cluster",
                Args(("node", node), ("cluster", cluster))) != 0;
            if (!Done())
            {
                if (db.Ado.ExecuteCommand("update gateway_ha_owner set revision=revision+1,cluster_id=@cluster where slot=1 and epoch=0 and (cluster_id='' or cluster_id=@cluster)",
                    Args(("cluster", cluster))) != 1) throw new InvalidOperationException("HA_ALARM_IMPORT_AFTER_ACTIVATION");
                if (!Done())
                {
                    using var source = openLocal();
                    if (source != null) CopyLocal(db, source);
                    db.Ado.ExecuteCommand("insert into gateway_alarm_import(nodeid,clusterid) values(@node,@cluster)", Args(("node", node), ("cluster", cluster)));
                }
            }
            db.Ado.CommitTran();
        }
        catch { db.Ado.RollbackTran(); throw; }
    }

    private static void CopyLocal(ISqlSugarClient db, SqliteConnection source)
    {
        using var snapshot = source.BeginTransaction(deferred: true);
        using var q = source.CreateCommand(); q.Transaction = snapshot;
        q.CommandText = "select tag_key,state,threshold from alarm_state";
        using (var reader = q.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = reader.GetString(0); var state = reader.GetString(1); double threshold = reader.GetDouble(2);
                var args = Args(("hash", Hash(key)));
                var previous = db.Ado.GetDataTable("select alarmstate,thresholdvalue from gateway_alarm_threshold where keyhash=@hash", args);
                if (previous.Rows.Count == 0)
                    db.Ado.ExecuteCommand("insert into gateway_alarm_threshold(keyhash,tagkey,alarmstate,thresholdvalue) values(@hash,@key,@state,@value)",
                        Args(("hash", Hash(key)), ("key", key), ("state", state), ("value", threshold)));
                else if (Str(previous.Rows[0], "alarmstate") != state || Convert.ToDouble(previous.Rows[0]["thresholdvalue"], CultureInfo.InvariantCulture) != threshold)
                    throw new InvalidDataException("HA_ALARM_THRESHOLD_CONFLICT: " + key);
            }
        }
        q.CommandText = """
            select e.event_id,e.body,e.delivery,e.queued_ticks,e.ready_ticks,e.topic,e.payload,e.qos,e.history_hidden,
                o.correlation_key,o.manual,o.single_event,o.recovered_utc,o.recovery_value,o.recovered_after_restart,
                a.operator_id,a.operator_name,a.timestamp_ticks,a.note,h.operator_id,h.operator_name,h.timestamp_ticks,h.note
            from alarm_event e left join alarm_occurrence o on o.event_id=e.event_id
            left join alarm_operator_action a on a.event_id=e.event_id and a.stage=1
            left join alarm_operator_action h on h.event_id=e.event_id and h.stage=2
            where e.delivery=1 or (o.single_event=0 and o.recovered_utc is null)
               or (e.history_hidden=0 and lower(json_extract(e.body,'$.EventType')) in ('alarm','warning') and coalesce(o.manual,1)=1 and h.event_id is null)
            order by e.seq
            """;
        using (var reader = q.ExecuteReader()) while (reader.Read()) CopyEvent(db, reader);
        snapshot.Commit();
    }

    private static void CopyEvent(ISqlSugarClient db, SqliteDataReader r)
    {
        string id = r.GetString(0), body = r.GetString(1);
        var item = JsonSerializer.Deserialize<GatewayAlarmRecord>(body) ?? throw new InvalidDataException("Invalid standalone alarm " + id);
        var correlation = r.IsDBNull(9) ? null : r.GetString(9);
        string? Operator(int index) => r.IsDBNull(index) ? null : JsonSerializer.Serialize(new
        {
            OperatorId = r.GetString(index),
            Operator = r.GetString(index + 1),
            TimestampUtc = new DateTime(r.GetInt64(index + 2), DateTimeKind.Utc),
            Note = r.GetString(index + 3)
        });
        string? ack = Operator(15), handled = Operator(19);
        long? recovered = r.IsDBNull(12) ? null : DateTimeOffset.Parse(r.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcTicks;
        int delivery = r.GetInt32(2);
        var old = db.Ado.GetDataTable($"select body,ack,handled,recovered,delivery from {Events} where eventid=@id", Args(("id", id)));
        if (old.Rows.Count != 0)
        {
            var row = old.Rows[0];
            if (Str(row, "body") != body || ack != null && !row.IsNull("ack") && Str(row, "ack") != ack ||
                handled != null && !row.IsNull("handled") && Str(row, "handled") != handled ||
                recovered.HasValue && !row.IsNull("recovered") && Long(row["recovered"]) != recovered)
                throw new InvalidDataException("HA_ALARM_EVENT_CONFLICT: " + id);
            if (delivery > Long(row["delivery"]))
                db.Ado.ExecuteCommand($"update {Events} set delivery=@d,topic=@t,payload=@p,ready=@r,qos=@q where eventid=@id",
                    Args(("id", id), ("d", delivery), ("t", r.GetString(5)), ("p", r.GetString(6)), ("r", r.GetInt64(4)), ("q", r.GetInt32(7))));
        }
        else
        {
            var args = Args(("id", id), ("body", body), ("d", delivery), ("queued", r.GetInt64(3)), ("ready", r.GetInt64(4)), ("topic", r.GetString(5)), ("payload", r.GetString(6)),
                ("qos", r.GetInt32(7)), ("hidden", r.GetInt32(8)), ("tracked", correlation == null ? 0 : 1), ("corr", Hash(correlation ?? item.EventId)),
                ("corrname", correlation ?? ""), ("manual", r.IsDBNull(10) ? 1 : r.GetInt32(10)), ("single", r.IsDBNull(11) ? 0 : r.GetInt32(11)),
                ("stamp", item.Timestamp.ToUniversalTime().Ticks), ("active", GatewayAlarmRecord.IsActiveEvent(item.EventType) ? 1 : 0),
                ("sortkey", delivery == 1 && item.Source == "rule" ? Hash(JsonSerializer.Serialize(new[] { item.RuleId, item.DeviceId, item.TagId })) : ""),
                ("cn", item.ChannelName ?? ""), ("dn", item.DeviceName ?? ""), ("gn", item.GroupName ?? ""), ("tn", item.TagName ?? ""),
                ("cid", item.ChannelId ?? ""), ("did", item.DeviceId ?? ""), ("gid", item.GroupId ?? ""), ("tid", item.TagId ?? ""));
            db.Ado.ExecuteCommand($"""
                insert into {Events}(eventid,body,delivery,queued,ready,topic,payload,qos,hidden,tracked,corr,corrname,manual,singleevent,
                    stamp,activation,sortkey,channame,devname,grpname,tagname,chanid,devid,grpid,tagid,stage,restored)
                values(@id,@body,@d,@queued,@ready,@topic,@payload,@qos,@hidden,@tracked,@corr,@corrname,@manual,@single,
                    @stamp,@active,@sortkey,@cn,@dn,@gn,@tn,@cid,@did,@gid,@tid,0,0)
                """, args);
        }
        if (ack != null)
            db.Ado.ExecuteCommand($"update {Events} set ack=@a,stage=case when stage=0 then 1 else stage end where eventid=@id", Args(("a", ack), ("id", id)));
        if (handled != null)
            db.Ado.ExecuteCommand($"update {Events} set handled=@h,handledat=@t,stage=2 where eventid=@id", Args(("h", handled), ("t", r.GetInt64(21)), ("id", id)));
        if (recovered.HasValue)
            db.Ado.ExecuteCommand($"update {Events} set recovered=@t,recoveryseq=seq,recoveryvalue=@v,restored=@restart where eventid=@id",
                Args(("id", id), ("t", recovered), ("v", r.IsDBNull(13) ? null : r.GetDouble(13)), ("restart", r.IsDBNull(14) ? 0 : r.GetInt32(14))));
    }
}
