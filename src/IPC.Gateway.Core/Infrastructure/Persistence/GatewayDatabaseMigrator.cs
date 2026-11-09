/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayDatabaseMigrator
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Infrastructure.Persistence
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class GatewayDatabaseMigrator
{
    private static readonly object SyncRoot = new object();
    private static readonly HashSet<string> MigratedConnectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly SqlSugarConnectionFactory _factory;

    public GatewayDatabaseMigrator(SqlSugarConnectionFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Migrate()
    {
        _factory.EnsureDatabase();
        using ISqlSugarClient db = _factory.Create();
        string key = db.CurrentConnectionConfig.DbType + "|" + db.CurrentConnectionConfig.ConnectionString;

        lock (SyncRoot)
        {
            if (MigratedConnectionKeys.Contains(key))
                return;

            EnsureMigrationTable(db);
            foreach (DatabaseMigration migration in GatewayMigrations.All)
            {
                if (HasMigration(db, migration.Id))
                    continue;

                db.Ado.BeginTran();
                try
                {
                    ExecuteMigration(db, migration);
                    InsertMigration(db, migration);
                    db.Ado.CommitTran();
                }
                catch
                {
                    db.Ado.RollbackTran();
                    throw;
                }
            }

            MigratedConnectionKeys.Add(key);
        }
    }

    private static void ExecuteMigration(ISqlSugarClient db, DatabaseMigration migration)
    {
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite &&
            string.Equals(migration.Id, "202606210001_runtime_tag_cleaning_state", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteSqliteRuntimeTagCleaningMigration(db);
            return;
        }

        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite &&
            string.Equals(migration.Id, "202606220002_account_security_state", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteSqliteAccountSecurityStateMigration(db);
            return;
        }

        db.Ado.ExecuteCommand(migration.GetSql(db.CurrentConnectionConfig.DbType));
    }

    private static void ExecuteSqliteRuntimeTagCleaningMigration(ISqlSugarClient db)
    {
        AddSqliteColumnIfMissing(db, "gateway_runtime_tag_snapshots", "cleaning_applied", "cleaning_applied integer not null default 0");
        AddSqliteColumnIfMissing(db, "gateway_runtime_tag_snapshots", "cleaning_action", "cleaning_action text null");
        AddSqliteColumnIfMissing(db, "gateway_runtime_tag_snapshots", "cleaning_message", "cleaning_message text null");
    }

    private static void ExecuteSqliteAccountSecurityStateMigration(ISqlSugarClient db)
    {
        AddSqliteColumnIfMissing(db, "gateway_users", "password_changed_utc", "password_changed_utc text not null default '1970-01-01 00:00:00'");
        AddSqliteColumnIfMissing(db, "gateway_users", "last_login_utc", "last_login_utc text null");
        AddSqliteColumnIfMissing(db, "gateway_users", "last_failed_login_utc", "last_failed_login_utc text null");
        AddSqliteColumnIfMissing(db, "gateway_users", "failed_login_count", "failed_login_count integer not null default 0");
        AddSqliteColumnIfMissing(db, "gateway_users", "lockout_end_utc", "lockout_end_utc text null");
    }

    private static void AddSqliteColumnIfMissing(ISqlSugarClient db, string tableName, string columnName, string columnDefinition)
    {
        if (SqliteColumnExists(db, tableName, columnName))
            return;

        db.Ado.ExecuteCommand($"alter table {tableName} add column {columnDefinition};");
    }

    private static bool SqliteColumnExists(ISqlSugarClient db, string tableName, string columnName)
    {
        string sql = "select count(1) from pragma_table_info('" +
            EscapeSqlLiteral(tableName) +
            "') where lower(name) = lower('" +
            EscapeSqlLiteral(columnName) +
            "')";
        return db.Ado.GetInt(sql) > 0;
    }

    private static void EnsureMigrationTable(ISqlSugarClient db)
    {
        db.Ado.ExecuteCommand(GetMigrationTableSql(db.CurrentConnectionConfig.DbType));
    }

    private static bool HasMigration(ISqlSugarClient db, string migrationId)
    {
        string sql = db.CurrentConnectionConfig.DbType switch
        {
            DbType.MySql => $"select count(1) from gateway_schema_migrations where migration_id = '{EscapeSqlLiteral(migrationId)}'",
            DbType.SqlServer => $"select count(1) from gateway_schema_migrations where migration_id = N'{EscapeSqlLiteral(migrationId)}'",
            _ => $"select count(1) from gateway_schema_migrations where migration_id = '{EscapeSqlLiteral(migrationId)}'"
        };

        return db.Ado.GetInt(sql) > 0;
    }

    private static void InsertMigration(ISqlSugarClient db, DatabaseMigration migration)
    {
        string id = EscapeSqlLiteral(migration.Id);
        string description = EscapeSqlLiteral(migration.Description);
        string provider = EscapeSqlLiteral(db.CurrentConnectionConfig.DbType.ToString());

        string sql = db.CurrentConnectionConfig.DbType switch
        {
            DbType.MySql =>
                $"insert into gateway_schema_migrations(migration_id, description, provider, applied_utc) values('{id}', '{description}', '{provider}', utc_timestamp())",
            DbType.SqlServer =>
                $"insert into gateway_schema_migrations(migration_id, description, provider, applied_utc) values(N'{id}', N'{description}', N'{provider}', sysutcdatetime())",
            _ =>
                $"insert into gateway_schema_migrations(migration_id, description, provider, applied_utc) values('{id}', '{description}', '{provider}', timezone('utc', now()))"
        };

        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            sql = $"insert into gateway_schema_migrations(migration_id, description, provider, applied_utc) values('{id}', '{description}', '{provider}', datetime('now'))";

        db.Ado.ExecuteCommand(sql);
    }

    private static string GetMigrationTableSql(DbType dbType)
    {
        return dbType switch
        {
            DbType.MySql => @"
create table if not exists gateway_schema_migrations (
    migration_id varchar(128) not null primary key,
    description varchar(512) not null,
    provider varchar(64) not null,
    applied_utc datetime not null
);",
            DbType.SqlServer => @"
if object_id(N'gateway_schema_migrations', N'U') is null
begin
    create table gateway_schema_migrations (
        migration_id nvarchar(128) not null primary key,
        description nvarchar(512) not null,
        provider nvarchar(64) not null,
        applied_utc datetime2 not null
    );
end",
            DbType.Sqlite => @"
create table if not exists gateway_schema_migrations (
    migration_id text not null primary key,
    description text not null,
    provider text not null,
    applied_utc text not null
);",
            _ => @"
create table if not exists gateway_schema_migrations (
    migration_id text not null primary key,
    description text not null,
    provider text not null,
    applied_utc timestamp without time zone not null
);"
        };
    }

    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }
}
