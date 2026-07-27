using IPC.Gateway.Scripting.Database;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证脚本数据库命令只能在目标白名单内生成参数化 INSERT 和 UPDATE。
/// </summary>
public sealed class ScriptDatabaseCommandBuilderTests
{
    /// <summary>
    /// 验证 SQL Server INSERT 使用引用标识符和独立参数承载外部值。
    /// </summary>
    [Fact]
    public void Build_Insert_ShouldGenerateParameterizedCommand()
    {
        ScriptDatabaseCommandPlan plan = new ScriptDatabaseCommandBuilder().Build(
            ScriptDatabaseProvider.SqlServer,
            CreateTarget(),
            new ScriptDatabaseWriteRequest
            {
                Operation = ScriptDatabaseOperation.Insert,
                Values = new Dictionary<string, object?> { ["TagName"] = "Temperature'; DROP TABLE Records;--", ["Value"] = 12.5 }
            });

        Assert.Equal("INSERT INTO [dbo].[Records] ([TagName], [Value]) VALUES (@p0, @p1)", plan.CommandText);
        Assert.DoesNotContain("DROP TABLE", plan.CommandText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, plan.Parameters.Count);
    }

    /// <summary>
    /// 验证 UPDATE 必须使用目标中预先配置的完整更新键。
    /// </summary>
    [Fact]
    public void Build_Update_ShouldRequireConfiguredKeys()
    {
        ScriptDatabaseWriteRequest request = new()
        {
            Operation = ScriptDatabaseOperation.Update,
            Values = new Dictionary<string, object?> { ["Value"] = 20 },
            Keys = new Dictionary<string, object?>()
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            new ScriptDatabaseCommandBuilder().Build(ScriptDatabaseProvider.SqlServer, CreateTarget(), request));

        Assert.Contains("UPDATE", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证未列入字段白名单的内容不会进入数据库命令。
    /// </summary>
    [Fact]
    public void Build_UnknownColumn_ShouldBeRejected()
    {
        ScriptDatabaseWriteRequest request = new()
        {
            Operation = ScriptDatabaseOperation.Insert,
            Values = new Dictionary<string, object?> { ["UnauthorizedColumn"] = 1 }
        };

        Assert.Throws<InvalidOperationException>(() =>
            new ScriptDatabaseCommandBuilder().Build(ScriptDatabaseProvider.PostgreSql, CreateTarget(), request));
    }

    /// <summary>
    /// 验证含 SQL 片段的数据表名会在生成命令前被拒绝。
    /// </summary>
    [Fact]
    public void Build_UnsafeTableIdentifier_ShouldBeRejected()
    {
        ScriptDatabaseWriteTarget target = CreateTarget();
        target.Table = "Records;DELETE FROM Users";

        Assert.Throws<InvalidOperationException>(() => new ScriptDatabaseCommandBuilder().Build(
            ScriptDatabaseProvider.MySql,
            target,
            new ScriptDatabaseWriteRequest
            {
                Operation = ScriptDatabaseOperation.Insert,
                Values = new Dictionary<string, object?> { ["Value"] = 1 }
            }));
    }

    /// <summary>
    /// 验证升级后的安全版 SQLite 原生组件可以建立内存数据库连接。
    /// </summary>
    [Fact]
    public async Task ConnectionFactory_Sqlite_ShouldOpenInMemoryDatabase()
    {
        ScriptDatabaseConnectionDefinition definition = new()
        {
            Provider = ScriptDatabaseProvider.Sqlite,
            ConnectionString = "Data Source=:memory:",
            Enabled = true,
            ConnectionTimeoutSeconds = 5
        };

        await using var connection = new ScriptDatabaseConnectionFactory().Create(definition);
        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    /// <summary>
    /// 验证 Oracle 和达梦使用冒号参数，人大金仓使用 PostgreSQL 风格参数。
    /// </summary>
    [Theory]
    [InlineData(ScriptDatabaseProvider.Oracle, "INSERT INTO \"dbo\".\"Records\" (\"Value\") VALUES (:p0)")]
    [InlineData(ScriptDatabaseProvider.Dameng, "INSERT INTO \"dbo\".\"Records\" (\"Value\") VALUES (:p0)")]
    [InlineData(ScriptDatabaseProvider.KingbaseEs, "INSERT INTO \"dbo\".\"Records\" (\"Value\") VALUES (@p0)")]
    public void Build_NewRelationalProvider_ShouldGenerateExpectedDialect(
        ScriptDatabaseProvider provider,
        string expectedSql)
    {
        ScriptDatabaseCommandPlan plan = new ScriptDatabaseCommandBuilder().Build(
            provider,
            CreateTarget(),
            new ScriptDatabaseWriteRequest
            {
                Operation = ScriptDatabaseOperation.Insert,
                Values = new Dictionary<string, object?> { ["Value"] = 12 }
            });

        Assert.Equal(expectedSql, plan.CommandText);
        Assert.Equal("p0", plan.Parameters.Single().Name);
    }

    /// <summary>
    /// 验证 ClickHouse 使用官方驱动推断的强类型查询参数，避免拼接外部值。
    /// </summary>
    [Fact]
    public void Build_ClickHouseInsert_ShouldGenerateTypedParameter()
    {
        ScriptDatabaseCommandPlan plan = new ScriptDatabaseCommandBuilder().Build(
            ScriptDatabaseProvider.ClickHouse,
            CreateTarget(),
            new ScriptDatabaseWriteRequest
            {
                Operation = ScriptDatabaseOperation.Insert,
                Values = new Dictionary<string, object?> { ["TagName"] = "温度'; DROP TABLE x;--", ["Value"] = 12 }
            });

        Assert.Equal("INSERT INTO `dbo`.`Records` (`TagName`, `Value`) VALUES ({p0:String}, {p1:Int32})", plan.CommandText);
        Assert.DoesNotContain("DROP TABLE", plan.CommandText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 验证 ClickHouse 更新预检查只能按目标中配置的更新键生成计数查询。
    /// </summary>
    [Fact]
    public void Build_ClickHouseUpdatePreflight_ShouldUseConfiguredKeys()
    {
        ScriptDatabaseCommandPlan plan = new ScriptDatabaseCommandBuilder().BuildClickHouseUpdatePreflight(
            CreateTarget(),
            new ScriptDatabaseWriteRequest
            {
                Operation = ScriptDatabaseOperation.Update,
                Values = new Dictionary<string, object?> { ["Value"] = 20 },
                Keys = new Dictionary<string, object?> { ["TagName"] = "Temperature" }
            });

        Assert.Equal("SELECT count() FROM `dbo`.`Records` WHERE `TagName` = {p0:String}", plan.CommandText);
        Assert.Single(plan.Parameters);
    }

    /// <summary>
    /// 验证新增数据库类型能够创建对应厂商的 ADO.NET 连接对象。
    /// </summary>
    [Theory]
    [InlineData(ScriptDatabaseProvider.Oracle, "User Id=system;Password=test;Data Source=127.0.0.1:1521/ORCL", "Oracle.ManagedDataAccess.Client.OracleConnection")]
    [InlineData(ScriptDatabaseProvider.Dameng, "Server=127.0.0.1;Port=5236;User Id=SYSDBA;Password=test", "Dm.DmConnection")]
    [InlineData(ScriptDatabaseProvider.KingbaseEs, "Server=127.0.0.1;Port=54321;Database=TEST;User Id=SYSTEM;Password=test", "Kdbndp.KdbndpConnection")]
    [InlineData(ScriptDatabaseProvider.ClickHouse, "Host=127.0.0.1;Port=8123;Database=default;Username=default", "ClickHouse.Driver.ADO.ClickHouseConnection")]
    public void ConnectionFactory_NewProvider_ShouldCreateVendorConnection(
        ScriptDatabaseProvider provider,
        string connectionString,
        string expectedType)
    {
        ScriptDatabaseConnectionDefinition definition = new()
        {
            Provider = provider,
            ConnectionString = connectionString,
            Enabled = true,
            ConnectionTimeoutSeconds = 5
        };

        using var connection = new ScriptDatabaseConnectionFactory().Create(definition);

        Assert.Equal(expectedType, connection.GetType().FullName);
    }

    /// <summary>
    /// 创建供测试复用的严格数据库写入目标。
    /// </summary>
    private static ScriptDatabaseWriteTarget CreateTarget()
    {
        return new ScriptDatabaseWriteTarget
        {
            Id = "target-1",
            Name = "记录表",
            ConnectionId = "connection-1",
            Schema = "dbo",
            Table = "Records",
            Enabled = true,
            AllowInsert = true,
            AllowUpdate = true,
            AllowedColumns = ["TagName", "Value", "CollectedAt"],
            KeyColumns = ["TagName"],
            MaxAffectedRows = 1
        };
    }
}
