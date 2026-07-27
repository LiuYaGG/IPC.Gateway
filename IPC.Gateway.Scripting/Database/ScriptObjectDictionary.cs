using System.Collections;
using System.Reflection;

namespace IPC.Gateway.Scripting.Database;

/// <summary>
/// 将脚本匿名对象或字典安全转换成结构化字段集合。
/// </summary>
public static class ScriptObjectDictionary
{
    /// <summary>
    /// 将脚本传入的对象转换为忽略大小写的字段字典。
    /// </summary>
    public static Dictionary<string, object?> FromObject(object? value)
    {
        if (value is null)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (value is IReadOnlyDictionary<string, object?> readOnly)
            return new Dictionary<string, object?>(readOnly, StringComparer.OrdinalIgnoreCase);
        if (value is IDictionary<string, object?> generic)
            return new Dictionary<string, object?>(generic, StringComparer.OrdinalIgnoreCase);
        if (value is IDictionary dictionary)
        {
            Dictionary<string, object?> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in dictionary)
                result[entry.Key?.ToString() ?? string.Empty] = entry.Value;
            return result;
        }

        return value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => property.GetValue(value), StringComparer.OrdinalIgnoreCase);
    }
}
