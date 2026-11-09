using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClickHouse.Driver.ADO.Parameters;
using IPC.Gateway.Scripting.Models;

namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 将结构化 INSERT 或 UPDATE 请求转换为各数据库方言的参数化命令。
/// </summary>
public sealed partial class ScriptDatabaseCommandBuilder
{
    /// <summary>
    /// 验证请求并生成参数化命令计划。
    /// </summary>
    public ScriptDatabaseCommandPlan Build(
        ScriptDatabaseProvider provider,
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ValidateTarget(target, request.Operation);
        return request.Operation switch
        {
            ScriptDatabaseOperation.Insert => BuildInsert(provider, target, request.Values),
            ScriptDatabaseOperation.Update => BuildUpdate(provider, target, request.Values, request.Keys),
            _ => throw new InvalidOperationException("数据库写入队列只允许 INSERT 和 UPDATE。")
        };
    }

    /// <summary>
    /// 为 ClickHouse UPDATE 生成仅按固定更新键计数的预检查命令。
    /// </summary>
    public ScriptDatabaseCommandPlan BuildClickHouseUpdatePreflight(
        ScriptDatabaseWriteTarget target,
        ScriptDatabaseWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Operation != ScriptDatabaseOperation.Update)
            throw new InvalidOperationException("ClickHouse 影响行数预检查只适用于 UPDATE。");

        ValidateTarget(target, ScriptDatabaseOperation.Update);
        Dictionary<string, object> normalizedKeys = NormalizeAndValidateKeys(target, request.Keys);
        ScriptDatabaseCommandPlan plan = new();
        StringBuilder sql = new();
        sql.Append("SELECT count() FROM ").Append(BuildQualifiedTable(ScriptDatabaseProvider.ClickHouse, target)).Append(" WHERE ");
        for (int index = 0; index < target.KeyColumns.Count; index++)
        {
            if (index > 0)
                sql.Append(" AND ");
            string key = target.KeyColumns[index];
            string parameterPlaceholder = AddParameter(plan, ScriptDatabaseProvider.ClickHouse, index, normalizedKeys[key]);
            sql.Append(QuoteIdentifier(ScriptDatabaseProvider.ClickHouse, key)).Append(" = ").Append(parameterPlaceholder);
        }

        plan.CommandText = sql.ToString();
        return plan;
    }

    /// <summary>
    /// 生成参数化 INSERT 命令。
    /// </summary>
    private static ScriptDatabaseCommandPlan BuildInsert(
        ScriptDatabaseProvider provider,
        ScriptDatabaseWriteTarget target,
        IReadOnlyDictionary<string, object?> values)
    {
        List<KeyValuePair<string, object?>> fields = NormalizeAndValidateFields(target, values, allowKeys: true);
        if (fields.Count == 0)
            throw new InvalidOperationException("INSERT 至少需要一个写入字段。");

        ScriptDatabaseCommandPlan plan = new();
        StringBuilder sql = new();
        sql.Append("INSERT INTO ").Append(BuildQualifiedTable(provider, target)).Append(" (");
        sql.AppendJoin(", ", fields.Select(item => QuoteIdentifier(provider, item.Key)));
        sql.Append(") VALUES (");
        for (int index = 0; index < fields.Count; index++)
        {
            if (index > 0)
                sql.Append(", ");
            object normalizedValue = NormalizeValue(fields[index].Value);
            sql.Append(AddParameter(plan, provider, index, normalizedValue));
        }
        sql.Append(')');
        plan.CommandText = sql.ToString();
        return plan;
    }

    /// <summary>
    /// 生成带固定更新键的参数化 UPDATE 命令。
    /// </summary>
    private static ScriptDatabaseCommandPlan BuildUpdate(
        ScriptDatabaseProvider provider,
        ScriptDatabaseWriteTarget target,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, object?> keys)
    {
        List<KeyValuePair<string, object?>> fields = NormalizeAndValidateFields(target, values, allowKeys: false);
        if (fields.Count == 0)
            throw new InvalidOperationException("UPDATE 至少需要一个更新字段。");

        Dictionary<string, object> normalizedKeys = NormalizeAndValidateKeys(target, keys);

        ScriptDatabaseCommandPlan plan = new();
        StringBuilder sql = new();
        sql.Append("UPDATE ").Append(BuildQualifiedTable(provider, target)).Append(" SET ");
        int parameterIndex = 0;
        for (int index = 0; index < fields.Count; index++)
        {
            if (index > 0)
                sql.Append(", ");
            object normalizedValue = NormalizeValue(fields[index].Value);
            string parameterPlaceholder = AddParameter(plan, provider, parameterIndex++, normalizedValue);
            sql.Append(QuoteIdentifier(provider, fields[index].Key)).Append(" = ").Append(parameterPlaceholder);
        }

        sql.Append(" WHERE ");
        for (int index = 0; index < target.KeyColumns.Count; index++)
        {
            if (index > 0)
                sql.Append(" AND ");
            string key = target.KeyColumns[index];
            string parameterPlaceholder = AddParameter(plan, provider, parameterIndex++, normalizedKeys[key]);
            sql.Append(QuoteIdentifier(provider, key)).Append(" = ").Append(parameterPlaceholder);
        }

        plan.CommandText = sql.ToString();
        return plan;
    }

    /// <summary>
    /// 验证写入目标状态和操作权限。
    /// </summary>
    private static void ValidateTarget(ScriptDatabaseWriteTarget target, ScriptDatabaseOperation operation)
    {
        if (!target.Enabled)
            throw new InvalidOperationException("数据库写入目标已停用。");
        if (operation == ScriptDatabaseOperation.Insert && !target.AllowInsert)
            throw new InvalidOperationException("数据库写入目标不允许 INSERT。");
        if (operation == ScriptDatabaseOperation.Update && !target.AllowUpdate)
            throw new InvalidOperationException("数据库写入目标不允许 UPDATE。");
        _ = ValidateIdentifier(target.Table, "数据表");
        if (!string.IsNullOrWhiteSpace(target.Schema))
            _ = ValidateIdentifier(target.Schema, "架构");
        if (target.AllowedColumns.Count == 0)
            throw new InvalidOperationException("数据库写入目标没有配置字段白名单。");
        if (operation == ScriptDatabaseOperation.Update && target.KeyColumns.Count == 0)
            throw new InvalidOperationException("UPDATE 写入目标没有配置更新键。");
    }

    /// <summary>
    /// 规范化写入字段并执行字段白名单和更新键保护检查。
    /// </summary>
    private static List<KeyValuePair<string, object?>> NormalizeAndValidateFields(
        ScriptDatabaseWriteTarget target,
        IReadOnlyDictionary<string, object?> values,
        bool allowKeys)
    {
        HashSet<string> allowed = target.AllowedColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> keys = target.KeyColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<string, object?>> result = [];
        foreach (KeyValuePair<string, object?> pair in values)
        {
            string column = ValidateIdentifier(pair.Key, "字段");
            if (!allowed.Contains(column))
                throw new InvalidOperationException($"字段 {column} 不在写入目标白名单中。");
            if (!allowKeys && keys.Contains(column))
                throw new InvalidOperationException($"UPDATE 不允许修改更新键 {column}。");
            if (result.Any(item => string.Equals(item.Key, column, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"字段 {column} 重复。");
            result.Add(new KeyValuePair<string, object?>(column, pair.Value));
        }
        return result;
    }

    /// <summary>
    /// 验证更新键集合完整且无额外字段，并将键值转换为数据库参数基础类型。
    /// </summary>
    private static Dictionary<string, object> NormalizeAndValidateKeys(
        ScriptDatabaseWriteTarget target,
        IReadOnlyDictionary<string, object?> keys)
    {
        Dictionary<string, object?> normalizedInput = new(keys, StringComparer.OrdinalIgnoreCase);
        if (normalizedInput.Count != target.KeyColumns.Count || target.KeyColumns.Any(key => !normalizedInput.ContainsKey(key)))
            throw new InvalidOperationException("UPDATE 必须为写入目标配置的每一个更新键提供值，且不能增加其他条件字段。");

        Dictionary<string, object> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string key in target.KeyColumns)
        {
            object? keyValue = normalizedInput[key];
            if (keyValue is null || keyValue is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
                throw new InvalidOperationException($"更新键 {key} 不能为空。");
            result[key] = NormalizeValue(keyValue);
        }
        return result;
    }

    /// <summary>
    /// 生成带可选架构名的安全表名。
    /// </summary>
    private static string BuildQualifiedTable(ScriptDatabaseProvider provider, ScriptDatabaseWriteTarget target)
    {
        string table = QuoteIdentifier(provider, target.Table);
        return string.IsNullOrWhiteSpace(target.Schema) ? table : $"{QuoteIdentifier(provider, target.Schema)}.{table}";
    }

    /// <summary>
    /// 按数据库方言引用经过校验的标识符。
    /// </summary>
    private static string QuoteIdentifier(ScriptDatabaseProvider provider, string identifier)
    {
        string safe = ValidateIdentifier(identifier, "数据库标识符");
        return provider switch
        {
            ScriptDatabaseProvider.SqlServer => $"[{safe}]",
            ScriptDatabaseProvider.MySql or ScriptDatabaseProvider.ClickHouse => $"`{safe}`",
            ScriptDatabaseProvider.PostgreSql or ScriptDatabaseProvider.Sqlite or ScriptDatabaseProvider.Oracle
                or ScriptDatabaseProvider.Dameng or ScriptDatabaseProvider.KingbaseEs => $"\"{safe}\"",
            _ => throw new NotSupportedException($"不支持数据库类型 {provider}。")
        };
    }

    /// <summary>
    /// 向命令计划添加参数，并按数据库方言返回对应的参数占位符。
    /// </summary>
    private static string AddParameter(
        ScriptDatabaseCommandPlan plan,
        ScriptDatabaseProvider provider,
        int index,
        object value)
    {
        string parameterName = $"p{index}";
        plan.Parameters.Add(new ScriptDatabaseParameter { Name = parameterName, Value = value });
        return provider switch
        {
            ScriptDatabaseProvider.Oracle or ScriptDatabaseProvider.Dameng => $":{parameterName}",
            ScriptDatabaseProvider.ClickHouse => BuildClickHouseParameter(parameterName, value),
            _ => $"@{parameterName}"
        };
    }

    /// <summary>
    /// 使用 ClickHouse 官方驱动推断参数类型并生成安全查询占位符。
    /// </summary>
    private static string BuildClickHouseParameter(string parameterName, object value)
    {
        ClickHouseDbParameter parameter = new()
        {
            ParameterName = parameterName,
            Value = value
        };
        return parameter.QueryForm;
    }

    /// <summary>
    /// 再次验证持久化队列中的数据库标识符，防止文件被外部篡改。
    /// </summary>
    private static string ValidateIdentifier(string? value, string displayName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (!SafeIdentifierRegex().IsMatch(normalized))
            throw new InvalidOperationException($"{displayName}“{normalized}”不是安全标识符。");
        return normalized;
    }

    /// <summary>
    /// 将 JSON 反序列化值还原为 ADO.NET 可识别的基础类型。
    /// </summary>
    private static object NormalizeValue(object? value)
    {
        if (value is not JsonElement json)
            return value ?? DBNull.Value;
        return json.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
            JsonValueKind.String when json.TryGetDateTimeOffset(out DateTimeOffset date) => date,
            JsonValueKind.String => json.GetString() ?? string.Empty,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when json.TryGetInt64(out long integer) => integer,
            JsonValueKind.Number when json.TryGetDecimal(out decimal number) => number,
            _ => json.GetRawText()
        };
    }

    /// <summary>
    /// 创建数据库安全标识符匹配规则。
    /// </summary>
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{Nd}_$]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierRegex();
}
