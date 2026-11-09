/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：SqlSugarConnectionFactory
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
using IPC.Gateway.Core.Gateway;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class SqlSugarConnectionFactory
{
    private readonly GatewayDatabaseOptions _options;

    public SqlSugarConnectionFactory(GatewayDatabaseOptions options)
    {
        _options = options ?? new GatewayDatabaseOptions();
    }

    public ISqlSugarClient Create()
    {
        ConnectionConfig config = new ConnectionConfig
        {
            DbType = ResolveDbType(_options.Provider),
            ConnectionString = BuildConnectionString(),
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        };

        return new SqlSugarClient(config);
    }

    public void EnsureDatabase()
    {
        DbType dbType = ResolveDbType(_options.Provider);
        if (!_options.AutoCreateDatabase || !string.IsNullOrWhiteSpace(_options.ConnectionString))
            return;

        if (dbType == DbType.Sqlite)
        {
            BuildSqliteConnectionString(_options.Database);
            return;
        }

        using ISqlSugarClient db = new SqlSugarClient(new ConnectionConfig
        {
            DbType = dbType,
            ConnectionString = BuildAdminConnectionString(dbType),
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute
        });

        string databaseName = NormalizeDatabaseName(_options.Database);
        switch (dbType)
        {
            case DbType.PostgreSQL:
                int count = db.Ado.GetInt($"select count(1) from pg_database where datname = '{EscapeSqlLiteral(databaseName)}';");
                if (count == 0)
                    db.Ado.ExecuteCommand($"create database \"{EscapeIdentifier(databaseName, '"')}\" encoding 'UTF8';");
                break;

            case DbType.MySql:
                db.Ado.ExecuteCommand(
                    $"create database if not exists `{EscapeIdentifier(databaseName, '`')}` " +
                    "character set utf8mb4 collate utf8mb4_unicode_ci;");
                break;

            case DbType.SqlServer:
                db.Ado.ExecuteCommand(
                    $"if db_id(N'{EscapeSqlLiteral(databaseName)}') is null create database [{EscapeIdentifier(databaseName, ']')}];");
                break;
        }
    }

    private string BuildConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(_options.ConnectionString))
            return _options.ConnectionString;

        DbType dbType = ResolveDbType(_options.Provider);
        if (dbType == DbType.Sqlite)
            return BuildSqliteConnectionString(_options.Database);

        string databaseName = NormalizeDatabaseName(_options.Database);
        return dbType switch
        {
            DbType.PostgreSQL => BuildPostgreSqlConnectionString(databaseName),
            DbType.MySql => BuildMySqlConnectionString(databaseName),
            DbType.SqlServer => BuildSqlServerConnectionString(databaseName),
            _ => BuildPostgreSqlConnectionString(databaseName)
        };
    }

    private string BuildAdminConnectionString(DbType dbType)
    {
        return dbType switch
        {
            DbType.PostgreSQL => BuildPostgreSqlConnectionString("postgres"),
            DbType.MySql => BuildMySqlConnectionString(string.Empty),
            DbType.SqlServer => BuildSqlServerConnectionString("master"),
            _ => BuildConnectionString()
        };
    }

    private string BuildPostgreSqlConnectionString(string databaseName)
    {
        return $"Host={NormalizeHost()};Port={NormalizePort(5432)};Database={databaseName};Username={NormalizeUsername("postgres")};Password={_options.Password ?? string.Empty};Pooling=true";
    }

    private string BuildMySqlConnectionString(string databaseName)
    {
        string databasePart = string.IsNullOrWhiteSpace(databaseName) ? string.Empty : $"Database={databaseName};";
        return $"Server={NormalizeHost()};Port={NormalizePort(3306)};{databasePart}Uid={NormalizeUsername("root")};Pwd={_options.Password ?? string.Empty};CharSet=utf8mb4;Allow User Variables=true;";
    }

    private string BuildSqlServerConnectionString(string databaseName)
    {
        return $"Server={NormalizeHost()},{NormalizePort(1433)};Database={databaseName};User Id={NormalizeUsername("sa")};Password={_options.Password ?? string.Empty};TrustServerCertificate=True;Encrypt=False;";
    }

    private static string BuildSqliteConnectionString(string database)
    {
        string databasePath = string.IsNullOrWhiteSpace(database) ? "Data/ipc_gateway.db" : database;
        string fullPath = Path.IsPathRooted(databasePath)
            ? databasePath
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        return "Data Source=" + fullPath;
    }

    private string NormalizeHost()
    {
        return string.IsNullOrWhiteSpace(_options.Host) ? "localhost" : _options.Host.Trim();
    }

    private int NormalizePort(int defaultPort)
    {
        return _options.Port <= 0 ? defaultPort : _options.Port;
    }

    private string NormalizeUsername(string defaultUsername)
    {
        return string.IsNullOrWhiteSpace(_options.Username) ? defaultUsername : _options.Username.Trim();
    }

    private static string NormalizeDatabaseName(string database)
    {
        return string.IsNullOrWhiteSpace(database) ? "ipc_gateway" : database.Trim();
    }

    private static DbType ResolveDbType(string? provider)
    {
        string value = (provider ?? string.Empty).Trim();
        if (value.Length == 0)
            return DbType.PostgreSQL;

        if (value.Equals("postgres", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("postgresql", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("pgsql", StringComparison.OrdinalIgnoreCase))
            return DbType.PostgreSQL;

        if (value.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("mariadb", StringComparison.OrdinalIgnoreCase))
            return DbType.MySql;

        if (value.Equals("sqlserver", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("mssql", StringComparison.OrdinalIgnoreCase))
            return DbType.SqlServer;

        if (value.Equals("sqlite", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("sqlite3", StringComparison.OrdinalIgnoreCase))
            return DbType.Sqlite;

        return DbType.PostgreSQL;
    }

    private static string EscapeSqlLiteral(string value)
    {
        return (value ?? string.Empty).Replace("'", "''");
    }

    private static string EscapeIdentifier(string value, char quoteEnd)
    {
        string text = value ?? string.Empty;
        if (quoteEnd == ']')
            return text.Replace("]", "]]");
        return text.Replace(quoteEnd.ToString(), new string(quoteEnd, 2));
    }
}
