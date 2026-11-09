using System.Data;
using System.Data.Common;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 打开目标数据库并执行已生成的参数化 INSERT 或 UPDATE。
/// </summary>
public sealed class ScriptDatabaseWriteExecutor
{
    private readonly ScriptDatabaseConnectionFactory _connectionFactory;
    private readonly ScriptDatabaseCommandBuilder _commandBuilder;

    /// <summary>
    /// 创建数据库写入执行器。
    /// </summary>
    public ScriptDatabaseWriteExecutor(
        ScriptDatabaseConnectionFactory connectionFactory,
        ScriptDatabaseCommandBuilder commandBuilder)
    {
        _connectionFactory = connectionFactory;
        _commandBuilder = commandBuilder;
    }

    /// <summary>
    /// 执行结构化写入；支持事务的数据库会在影响行数超限时回滚。
    /// </summary>
    public async Task<int> ExecuteAsync(
        ScriptDatabaseConnectionDefinition connectionDefinition,
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ScriptDatabaseCommandPlan plan = _commandBuilder.Build(connectionDefinition.Provider, target, request);
        await using DbConnection connection = _connectionFactory.Create(connectionDefinition);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (connectionDefinition.Provider == ScriptDatabaseProvider.ClickHouse)
        {
            ScriptDatabaseCommandPlan? preflightPlan = request.Operation == ScriptDatabaseOperation.Update
                ? _commandBuilder.BuildClickHouseUpdatePreflight(target, request)
                : null;
            return await ExecuteClickHouseAsync(connection, connectionDefinition, target, plan, preflightPlan, cancellationToken).ConfigureAwait(false);
        }

        return await ExecuteTransactionalAsync(connection, connectionDefinition, target, request, plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在数据库事务中执行单条结构化写入命令。
    /// </summary>
    private static async Task<int> ExecuteTransactionalAsync(
        DbConnection connection,
        ScriptDatabaseConnectionDefinition connectionDefinition,
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseWriteRequest request,
        ScriptDatabaseCommandPlan plan,
        CancellationToken cancellationToken)
    {
        await using DbTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandType = CommandType.Text;
            command.CommandText = plan.CommandText;
            command.CommandTimeout = Math.Clamp(connectionDefinition.ConnectionTimeoutSeconds, 1, 120);
            AddParameters(command, plan.Parameters);
            int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            ValidateAffectedRows(target, request, affectedRows);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return affectedRows;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 在不支持 ADO.NET 事务的 ClickHouse 上执行单条结构化写入命令。
    /// </summary>
    private static async Task<int> ExecuteClickHouseAsync(
        DbConnection connection,
        ScriptDatabaseConnectionDefinition connectionDefinition,
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseCommandPlan plan,
        ScriptDatabaseCommandPlan? preflightPlan,
        CancellationToken cancellationToken)
    {
        if (preflightPlan is not null)
            await ValidateClickHouseUpdateCountAsync(connection, connectionDefinition, target, preflightPlan, cancellationToken).ConfigureAwait(false);

        await using DbCommand command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = plan.CommandText;
        command.CommandTimeout = Math.Clamp(connectionDefinition.ConnectionTimeoutSeconds, 1, 120);
        AddParameters(command, plan.Parameters);
        int affectedRows = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return affectedRows;
    }

    /// <summary>
    /// 在 ClickHouse UPDATE 前按固定更新键检查预计影响行数。
    /// </summary>
    private static async Task ValidateClickHouseUpdateCountAsync(
        DbConnection connection,
        ScriptDatabaseConnectionDefinition connectionDefinition,
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseCommandPlan preflightPlan,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = preflightPlan.CommandText;
        command.CommandTimeout = Math.Clamp(connectionDefinition.ConnectionTimeoutSeconds, 1, 120);
        AddParameters(command, preflightPlan.Parameters);
        object? result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        decimal affectedRows = result is null or DBNull ? 0 : Convert.ToDecimal(result);
        int maximum = Math.Max(1, target.MaxAffectedRows);
        if (affectedRows > maximum)
            throw new InvalidOperationException($"ClickHouse UPDATE 预计影响 {affectedRows} 行，超过目标允许的 {maximum} 行，已在写入前拦截。");
    }

    /// <summary>
    /// 校验驱动返回的影响行数；驱动返回负数时表示未提供可靠行数。
    /// </summary>
    private static void ValidateAffectedRows(
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseWriteRequest request,
        int affectedRows)
    {
        if (request.Operation != ScriptDatabaseOperation.Update || affectedRows < 0 || affectedRows <= Math.Max(1, target.MaxAffectedRows))
            return;

        throw new InvalidOperationException($"UPDATE 实际影响 {affectedRows} 行，超过目标允许的 {target.MaxAffectedRows} 行，事务已回滚。");
    }

    /// <summary>
    /// 仅打开并关闭数据库连接，用于验证网络、TLS 和登录凭据。
    /// </summary>
    public async Task TestConnectionAsync(
        ScriptDatabaseConnectionDefinition connectionDefinition,
        CancellationToken cancellationToken = default)
    {
        await using DbConnection connection = _connectionFactory.Create(connectionDefinition);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        if (connectionDefinition.Provider == ScriptDatabaseProvider.ClickHouse)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = Math.Clamp(connectionDefinition.ConnectionTimeoutSeconds, 1, 120);
            _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
        await connection.CloseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 将命令计划中的值添加为数据库参数。
    /// </summary>
    private static void AddParameters(DbCommand command, IEnumerable<ScriptDatabaseParameter> parameters)
    {
        foreach (ScriptDatabaseParameter definition in parameters)
        {
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = definition.Name;
            parameter.Value = definition.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
