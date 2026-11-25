namespace IPC.Gateway.Core.Infrastructure.Persistence;

internal static class SharedArtifactSchema
{
    internal static DatabaseMigration Migration => new("202611240001_shared_artifacts",
        "Share script definitions, model versions and immutable model contents.",
        Sql("bytea"), Sql("longblob"), Sql("varbinary(max)", sqlServer: true), Sql("blob"));

    private static string Sql(string binary, bool sqlServer = false)
    {
        string Create(string table) => sqlServer
            ? $"if object_id('{table}','U') is null create table {table}"
            : $"create table if not exists {table}";
        return $"""
        {Create("gateway_shared_artifacts")} (
            kind varchar(32) not null primary key, revision bigint not null, payload {binary} null
        );
        insert into gateway_shared_artifacts(kind,revision,payload)
            select 'scripts',0,null where not exists(select 1 from gateway_shared_artifacts where kind='scripts');
        insert into gateway_shared_artifacts(kind,revision,payload)
            select 'models',0,null where not exists(select 1 from gateway_shared_artifacts where kind='models');
        {Create("gateway_shared_blobs")} (digest varchar(64) not null primary key, byte_count bigint not null);
        {Create("gateway_shared_blob_chunks")} (
            digest varchar(64) not null, part integer not null, data {binary} not null,
            primary key(digest,part)
        );
        """;
    }
}
