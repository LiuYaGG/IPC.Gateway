using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

internal static class GatewaySessionSchema
{
    internal static DatabaseMigration Migration => new("202611250001_session_revocation", "Persist account security versions and revoked login sessions.",
        Sql("alter table gateway_users add column security_stamp varchar(64) not null default '';"),
        Sql("alter table gateway_users add column security_stamp varchar(64) not null default '';"),
        Sql("alter table gateway_users add security_stamp varchar(64) not null default '';"),
        Sql("alter table gateway_users add column security_stamp text not null default '';"));
    private static string Sql(string alter) => alter +
        "create table gateway_revoked_sessions (session_id varchar(64) not null primary key,user_id varchar(64) not null,expires_at bigint not null);";
}

[SugarTable("gateway_revoked_sessions")]
internal sealed class GatewayRevokedSessionEntity
{
    [SugarColumn(ColumnName="session_id", IsPrimaryKey=true, Length=64)] public string SessionId { get; set; } = "";
    [SugarColumn(ColumnName="user_id", Length=64)] public string UserId { get; set; } = "";
    [SugarColumn(ColumnName="expires_at")] public long ExpiresAt { get; set; }
}
