using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;

namespace IPC.Gateway.Tests;

public sealed class GatewayDatabaseMigrationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "ipc-gateway-migration-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Migrate_Sqlite_AddsRuntimeChannelIdentityColumns()
    {
        Directory.CreateDirectory(_testDirectory);
        string databasePath = Path.Combine(_testDirectory, "gateway.db");
        GatewayDatabaseOptions options = new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = databasePath,
            ConnectionString = "Data Source=" + databasePath + ";Pooling=False",
            AutoCreateDatabase = true
        };
        SqlSugarConnectionFactory factory = new SqlSugarConnectionFactory(options);

        new GatewayDatabaseMigrator(factory).Migrate();

        using SqlSugar.ISqlSugarClient db = factory.Create();
        Assert.Equal(
            1,
            db.Ado.GetInt(
                "select count(1) from gateway_schema_migrations " +
                "where migration_id = '202607150001_runtime_channel_identity'"));

        AssertColumns(db, "gateway_runtime_device_statuses", "channel_id", "channel_name");
        AssertColumns(db, "gateway_runtime_tag_snapshots", "channel_id", "channel_name");
        AssertColumns(
            db,
            "gateway_runtime_errors",
            "channel_id",
            "channel_name",
            "device_id",
            "group_id",
            "tag_id");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }

    private static void AssertColumns(
        SqlSugar.ISqlSugarClient db,
        string tableName,
        params string[] columnNames)
    {
        foreach (string columnName in columnNames)
        {
            int count = db.Ado.GetInt(
                $"select count(1) from pragma_table_info('{tableName}') " +
                $"where lower(name) = lower('{columnName}')");
            Assert.Equal(1, count);
        }
    }
}
