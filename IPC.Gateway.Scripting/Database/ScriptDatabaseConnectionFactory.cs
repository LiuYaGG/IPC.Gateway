using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using Dm;
using Kdbndp;
using ClickHouse.Driver.ADO;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 根据连接定义创建受支持数据库的 ADO.NET 连接。
/// </summary>
public sealed class ScriptDatabaseConnectionFactory
{
    /// <summary>
    /// 创建数据库连接并应用连接超时上限。
    /// </summary>
    public DbConnection Create(ScriptDatabaseConnectionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.Enabled)
            throw new InvalidOperationException("数据库连接已停用。");
        if (string.IsNullOrWhiteSpace(definition.ConnectionString))
            throw new InvalidOperationException("数据库连接字符串为空。");

        int timeout = Math.Clamp(definition.ConnectionTimeoutSeconds, 1, 120);
        return definition.Provider switch
        {
            ScriptDatabaseProvider.SqlServer => CreateSqlServer(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.PostgreSql => CreatePostgreSql(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.MySql => CreateMySql(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.Sqlite => CreateSqlite(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.Oracle => CreateOracle(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.Dameng => CreateDameng(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.KingbaseEs => CreateKingbase(definition.ConnectionString, timeout),
            ScriptDatabaseProvider.ClickHouse => CreateClickHouse(definition.ConnectionString),
            _ => throw new NotSupportedException($"不支持数据库类型 {definition.Provider}。")
        };
    }

    /// <summary>
    /// 创建 SQL Server 数据库连接。
    /// </summary>
    private static SqlConnection CreateSqlServer(string connectionString, int timeout)
    {
        SqlConnectionStringBuilder builder = new(connectionString) { ConnectTimeout = timeout };
        return new SqlConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建 PostgreSQL 数据库连接。
    /// </summary>
    private static NpgsqlConnection CreatePostgreSql(string connectionString, int timeout)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString) { Timeout = timeout };
        return new NpgsqlConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建 MySQL 或 MariaDB 数据库连接。
    /// </summary>
    private static MySqlConnection CreateMySql(string connectionString, int timeout)
    {
        MySqlConnectionStringBuilder builder = new(connectionString) { ConnectionTimeout = (uint)timeout };
        return new MySqlConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建 SQLite 数据库连接。
    /// </summary>
    private static SqliteConnection CreateSqlite(string connectionString, int timeout)
    {
        SqliteConnectionStringBuilder builder = new(connectionString) { DefaultTimeout = timeout };
        return new SqliteConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建 Oracle 数据库连接。
    /// </summary>
    private static OracleConnection CreateOracle(string connectionString, int timeout)
    {
        OracleConnectionStringBuilder builder = new(connectionString) { ConnectionTimeout = timeout };
        return new OracleConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建达梦数据库连接。
    /// </summary>
    private static DmConnection CreateDameng(string connectionString, int timeout)
    {
        DmConnectionStringBuilder builder = new(connectionString) { ConnectionTimeout = timeout };
        return new DmConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建人大金仓 KingbaseES 数据库连接。
    /// </summary>
    private static KdbndpConnection CreateKingbase(string connectionString, int timeout)
    {
        KdbndpConnectionStringBuilder builder = new(connectionString) { Timeout = timeout };
        return new KdbndpConnection(builder.ConnectionString);
    }

    /// <summary>
    /// 创建 ClickHouse 官方 ADO.NET 数据库连接。
    /// </summary>
    private static ClickHouseConnection CreateClickHouse(string connectionString)
    {
        return new ClickHouseConnection(connectionString);
    }
}
