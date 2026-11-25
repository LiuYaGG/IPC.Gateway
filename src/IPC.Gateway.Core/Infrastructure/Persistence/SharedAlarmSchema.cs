using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

internal static class SharedAlarmSchema
{
    public static DatabaseMigration Migration => new("202611230002_shared_alarm_journal",
        "Store cluster alarm occurrences, operator actions, threshold state and reliable deliveries in the business database.",
        Sql(DbType.PostgreSQL), Sql(DbType.MySql), Sql(DbType.SqlServer), Sql(DbType.Sqlite));

    private static string Sql(DbType provider)
    {
        bool ms = provider == DbType.SqlServer, mysql = provider == DbType.MySql;
        string text = ms ? "nvarchar(max)" : mysql ? "longtext" : "text";
        string create = mysql ? "create table if not exists" : "create table";
        string label = ms ? "nvarchar(256)" : "varchar(256)";
        string real = ms ? "float" : "double precision";
        string identity = provider switch
        {
            DbType.SqlServer => "bigint identity primary key",
            DbType.PostgreSQL => "bigserial primary key",
            DbType.MySql => "bigint auto_increment primary key",
            _ => "integer primary key autoincrement"
        };
        string[] indexes = ["ix_gw_alarm_open(corr,tracked,singleevent,recovered)","ix_gw_alarm_pending(delivery,sortkey,ready)",
            "ix_gw_alarm_time(hidden,stamp)","ix_gw_alarm_device(devid,seq)"];
        string inlineIndexes = mysql ? ", " + string.Join(", ", indexes.Select(x => "index " + x)) : "";
        string separateIndexes = mysql ? "" : string.Join(Environment.NewLine, indexes.Select(x => "create index " + x.Replace("(", " on gateway_alarm_event(") + ";"));
        return $"""
            {create} gateway_alarm_import (nodeid varchar(1) primary key, clusterid varchar(128) not null);
            {create} gateway_alarm_threshold (
                keyhash varchar(64) primary key, tagkey {text} not null, alarmstate {label} not null, thresholdvalue {real} not null
            );
            {create} gateway_alarm_event (
                seq {identity}, eventid {label} not null unique, stamp bigint not null, queued bigint not null, ready bigint not null,
                delivery integer not null, sortkey varchar(64) not null, topic {text} not null, payload {text} not null, qos integer not null,
                body {text} not null, hidden integer not null, activation integer not null, tracked integer not null,
                corr varchar(64) not null, corrname {text} not null, manual integer not null, singleevent integer not null,
                recoveryseq bigint null, recovered bigint null, recoveryvalue {real} null, restored integer not null,
                stage integer not null, ack {text} null, handled {text} null, handledat bigint null,
                chanid {label} not null, devid {label} not null, grpid {label} not null, tagid {label} not null,
                channame {text} not null, devname {text} not null, grpname {text} not null, tagname {text} not null
                {inlineIndexes}
            );
            {separateIndexes}
            """;
    }
}
