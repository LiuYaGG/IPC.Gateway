/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewayConfigurationAuditEvent
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.Globalization;
using System.IO;
using System.Text.Json;
using IPC;

namespace IPC.Gateway.Core.Gateway;

public sealed class GatewayConfigurationAuditEvent
{
    public string Outcome { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RemoteIpAddress { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string RequestBodySha256 { get; set; } = string.Empty;
    public long RequestContentLength { get; set; }
}

public sealed class GatewaySecurityAuditEvent
{
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RemoteIpAddress { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class GatewayAuditLogQuery
{
    public int Limit { get; set; } = 100;
    public int Offset { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime? FromTime { get; set; }
    public DateTime? ToTime { get; set; }
}

public sealed class GatewayAuditLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RemoteIpAddress { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string RawDetail { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class GatewayAuditLogQueryResult
{
    public int Limit { get; set; }
    public int Offset { get; set; }
    public int Returned { get; set; }
    public bool HasMore { get; set; }
    public IReadOnlyList<GatewayAuditLogEntry> Items { get; set; } = Array.Empty<GatewayAuditLogEntry>();
}

public interface IGatewayAuditLogStore
{
    void Append(GatewayAuditLogEntry entry);
    IReadOnlyList<GatewayAuditLogEntry> Query(GatewayAuditLogQuery query);
    int DeleteOlderThan(DateTime timestamp);
}

public static class GatewayAuditLog
{
    private const string ConfigurationWriteAction = "configuration.write";
    private const string SecurityActionPrefix = "security.";
    private const int DefaultReadLimit = 100;
    private const int MaxReadLimit = 500;
    private const int MaxReadOffset = 10000;

    public static void WriteConfigurationChange(GatewayConfigurationAuditEvent auditEvent, IGatewayAuditLogStore? store = null)
    {
        auditEvent ??= new GatewayConfigurationAuditEvent();
        GatewayAuditLogEntry entry = CreateConfigurationEntry(auditEvent);
        IpcLogService.WriteAudit(ConfigurationWriteAction, Safe(auditEvent.Target), entry.RawDetail);
        store?.Append(entry);
    }

    public static void WriteSecurityEvent(GatewaySecurityAuditEvent auditEvent, IGatewayAuditLogStore? store = null)
    {
        auditEvent ??= new GatewaySecurityAuditEvent();
        GatewayAuditLogEntry entry = CreateSecurityEntry(auditEvent);
        IpcLogService.WriteAudit(entry.Action, Safe(entry.Target), entry.RawDetail);
        store?.Append(entry);
    }

    public static string BuildConfigurationDetail(GatewayConfigurationAuditEvent auditEvent)
    {
        auditEvent ??= new GatewayConfigurationAuditEvent();
        Dictionary<string, string> detail = new Dictionary<string, string>
        {
            ["outcome"] = Safe(auditEvent.Outcome),
            ["user"] = Safe(auditEvent.UserName),
            ["role"] = Safe(auditEvent.Role),
            ["ip"] = Safe(auditEvent.RemoteIpAddress),
            ["method"] = Safe(auditEvent.Method),
            ["path"] = Safe(auditEvent.Path),
            ["traceId"] = Safe(auditEvent.TraceId)
        };

        if (!string.IsNullOrWhiteSpace(auditEvent.ErrorMessage))
            detail["error"] = Safe(auditEvent.ErrorMessage);
        if (!string.IsNullOrWhiteSpace(auditEvent.RequestBodySha256))
            detail["requestBodySha256"] = Safe(auditEvent.RequestBodySha256);
        if (auditEvent.RequestContentLength > 0)
            detail["requestContentLength"] = auditEvent.RequestContentLength.ToString(CultureInfo.InvariantCulture);

        return JsonSerializer.Serialize(detail);
    }

    public static IReadOnlyList<GatewayAuditLogEntry> ReadRecent(GatewayAuditLogQuery? query, IGatewayAuditLogStore? store = null)
    {
        query ??= new GatewayAuditLogQuery();
        int limit = ClampLimit(query.Limit);
        List<GatewayAuditLogEntry> entries = new List<GatewayAuditLogEntry>(limit);
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (store != null)
        {
            try
            {
                foreach (GatewayAuditLogEntry entry in store.Query(query))
                    AddEntry(entries, seen, entry, limit);
            }
            catch
            {
            }
        }

        if (entries.Count < limit)
        {
            foreach (GatewayAuditLogEntry entry in ReadRecentFromFiles(query))
                AddEntry(entries, seen, entry, limit);
        }

        return entries
            .OrderByDescending(item => item.Timestamp)
            .Take(limit)
            .ToList();
    }

    public static GatewayAuditLogQueryResult ReadPage(GatewayAuditLogQuery? query, IGatewayAuditLogStore? store = null)
    {
        query ??= new GatewayAuditLogQuery();
        int limit = ClampLimit(query.Limit);
        int offset = ClampOffset(query.Offset);
        GatewayAuditLogQuery pageQuery = CopyQuery(query, offset, Math.Min(limit + 1, MaxReadLimit));
        List<GatewayAuditLogEntry> rows = ReadRecent(pageQuery, store).ToList();
        bool hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        return new GatewayAuditLogQueryResult
        {
            Limit = limit,
            Offset = offset,
            Returned = rows.Count,
            HasMore = hasMore,
            Items = rows
        };
    }

    internal static GatewayAuditLogEntry CreateConfigurationEntry(GatewayConfigurationAuditEvent auditEvent, DateTime? timestamp = null)
    {
        auditEvent ??= new GatewayConfigurationAuditEvent();
        return new GatewayAuditLogEntry
        {
            Timestamp = timestamp ?? DateTime.Now,
            Level = "AUDIT",
            Action = ConfigurationWriteAction,
            Target = Safe(auditEvent.Target),
            Outcome = Safe(auditEvent.Outcome),
            UserName = Safe(auditEvent.UserName),
            Role = Safe(auditEvent.Role),
            RemoteIpAddress = Safe(auditEvent.RemoteIpAddress),
            Method = Safe(auditEvent.Method),
            Path = Safe(auditEvent.Path),
            TraceId = Safe(auditEvent.TraceId),
            ErrorMessage = Safe(auditEvent.ErrorMessage),
            RawDetail = BuildConfigurationDetail(auditEvent),
            Source = "database"
        };
    }

    internal static GatewayAuditLogEntry CreateSecurityEntry(GatewaySecurityAuditEvent auditEvent, DateTime? timestamp = null)
    {
        auditEvent ??= new GatewaySecurityAuditEvent();
        string action = Safe(auditEvent.Action);
        if (string.IsNullOrWhiteSpace(action))
            action = "event";
        if (!action.StartsWith(SecurityActionPrefix, StringComparison.OrdinalIgnoreCase))
            action = SecurityActionPrefix + action;

        return new GatewayAuditLogEntry
        {
            Timestamp = timestamp ?? DateTime.Now,
            Level = "AUDIT",
            Action = action,
            Target = Safe(auditEvent.Target),
            Outcome = Safe(auditEvent.Outcome),
            UserName = Safe(auditEvent.UserName),
            Role = Safe(auditEvent.Role),
            RemoteIpAddress = Safe(auditEvent.RemoteIpAddress),
            Method = Safe(auditEvent.Method),
            Path = Safe(auditEvent.Path),
            TraceId = Safe(auditEvent.TraceId),
            ErrorMessage = Safe(auditEvent.ErrorMessage),
            RawDetail = BuildSecurityDetail(auditEvent),
            Source = "database"
        };
    }

    private static string BuildSecurityDetail(GatewaySecurityAuditEvent auditEvent)
    {
        Dictionary<string, string> detail = new Dictionary<string, string>
        {
            ["outcome"] = Safe(auditEvent.Outcome),
            ["user"] = Safe(auditEvent.UserName),
            ["role"] = Safe(auditEvent.Role),
            ["ip"] = Safe(auditEvent.RemoteIpAddress),
            ["method"] = Safe(auditEvent.Method),
            ["path"] = Safe(auditEvent.Path),
            ["traceId"] = Safe(auditEvent.TraceId)
        };

        if (!string.IsNullOrWhiteSpace(auditEvent.Target))
            detail["target"] = Safe(auditEvent.Target);
        if (!string.IsNullOrWhiteSpace(auditEvent.ErrorMessage))
            detail["error"] = Safe(auditEvent.ErrorMessage);
        if (!string.IsNullOrWhiteSpace(auditEvent.Detail))
            detail["detail"] = Safe(auditEvent.Detail);

        return JsonSerializer.Serialize(detail);
    }

    private static IReadOnlyList<GatewayAuditLogEntry> ReadRecentFromFiles(GatewayAuditLogQuery query)
    {
        int limit = ClampLimit(query.Limit);
        int offset = ClampOffset(query.Offset);
        int readLimit = Math.Min(MaxReadOffset + limit, offset + limit);
        List<GatewayAuditLogEntry> entries = new List<GatewayAuditLogEntry>(readLimit);
        string directory = GetLogDirectoryPath();
        if (!Directory.Exists(directory))
            return entries;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "audit-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(GetLastWriteTimeUtcSafe)
                .ToList();
        }
        catch
        {
            return entries;
        }

        foreach (string file in files)
        {
            foreach (string line in ReadLinesNewestFirst(file))
            {
                if (!TryParseAuditLine(line, out GatewayAuditLogEntry entry))
                    continue;

                if (!MatchesQuery(entry, query))
                    continue;

                entries.Add(entry);
                if (entries.Count >= readLimit)
                    return entries.Skip(offset).Take(limit).ToList();
            }
        }

        return entries.Skip(offset).Take(limit).ToList();
    }

    private static void AddEntry(ICollection<GatewayAuditLogEntry> entries, ISet<string> seen, GatewayAuditLogEntry entry, int limit)
    {
        if (entry == null || entries.Count >= limit)
            return;

        string key = BuildAuditKey(entry);
        if (!seen.Add(key))
            return;

        entries.Add(entry);
    }

    private static string BuildAuditKey(GatewayAuditLogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.TraceId))
            return "trace:" + entry.TraceId.Trim() + "|" + entry.Action + "|" + entry.Target + "|" + entry.Outcome;

        return string.Join("|", new[]
        {
            entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            entry.Action ?? string.Empty,
            entry.Target ?? string.Empty,
            entry.Outcome ?? string.Empty,
            entry.UserName ?? string.Empty,
            entry.RawDetail ?? string.Empty
        });
    }

    internal static bool TryParseAuditLine(string line, out GatewayAuditLogEntry entry)
    {
        entry = new GatewayAuditLogEntry();
        if (string.IsNullOrWhiteSpace(line) || line.Length < 31)
            return false;

        if (!DateTime.TryParseExact(
            line.Substring(0, 23),
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out DateTime timestamp))
            return false;

        int levelStart = line.IndexOf('[', 23);
        int levelEnd = line.IndexOf(']', levelStart + 1);
        if (levelStart < 0 || levelEnd <= levelStart)
            return false;

        string remainder = line.Substring(levelEnd + 1).TrimStart();
        int actionStart = remainder.IndexOf("action=", StringComparison.OrdinalIgnoreCase);
        int targetStart = remainder.IndexOf(" target=", StringComparison.OrdinalIgnoreCase);
        int detailStart = remainder.IndexOf(" detail=", StringComparison.OrdinalIgnoreCase);
        if (actionStart < 0 || targetStart <= actionStart || detailStart <= targetStart)
            return false;

        string action = remainder.Substring(actionStart + "action=".Length, targetStart - actionStart - "action=".Length).Trim();
        string target = remainder.Substring(targetStart + " target=".Length, detailStart - targetStart - " target=".Length).Trim();
        string detail = remainder.Substring(detailStart + " detail=".Length).Trim();
        Dictionary<string, string> fields = ParseDetail(detail);

        entry = new GatewayAuditLogEntry
        {
            Timestamp = timestamp,
            Level = line.Substring(levelStart + 1, levelEnd - levelStart - 1).Trim(),
            Action = action,
            Target = target,
            Outcome = GetField(fields, "outcome"),
            UserName = GetField(fields, "user"),
            Role = GetField(fields, "role"),
            RemoteIpAddress = GetField(fields, "ip"),
            Method = GetField(fields, "method"),
            Path = GetField(fields, "path"),
            TraceId = GetField(fields, "traceId"),
            ErrorMessage = GetField(fields, "error"),
            RawDetail = detail,
            Source = "file"
        };
        return true;
    }

    internal static bool MatchesQuery(GatewayAuditLogEntry entry, GatewayAuditLogQuery query)
    {
        if (!ContainsIgnoreCase(entry.Target, query.Target))
            return false;

        if (!string.IsNullOrWhiteSpace(query.Outcome) &&
            !string.Equals(entry.Outcome, query.Outcome.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!ContainsIgnoreCase(entry.UserName, query.UserName))
            return false;

        if (query.FromTime.HasValue && entry.Timestamp < query.FromTime.Value)
            return false;

        if (query.ToTime.HasValue && entry.Timestamp > query.ToTime.Value)
            return false;

        return true;
    }

    internal static int ClampLimit(int limit)
    {
        if (limit <= 0)
            return DefaultReadLimit;
        if (limit > MaxReadLimit)
            return MaxReadLimit;
        return limit;
    }

    internal static int ClampOffset(int offset)
    {
        if (offset < 0)
            return 0;
        if (offset > MaxReadOffset)
            return MaxReadOffset;
        return offset;
    }

    private static GatewayAuditLogQuery CopyQuery(GatewayAuditLogQuery query, int offset, int limit)
    {
        return new GatewayAuditLogQuery
        {
            Limit = limit,
            Offset = offset,
            Target = query.Target ?? string.Empty,
            Outcome = query.Outcome ?? string.Empty,
            UserName = query.UserName ?? string.Empty,
            FromTime = query.FromTime,
            ToTime = query.ToTime
        };
    }

    private static IEnumerable<string> ReadLinesNewestFirst(string file)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file);
        }
        catch
        {
            yield break;
        }

        for (int i = lines.Length - 1; i >= 0; i--)
            yield return lines[i];
    }

    private static Dictionary<string, string> ParseDetail(string detail)
    {
        Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(detail))
            return fields;

        try
        {
            using JsonDocument document = JsonDocument.Parse(detail);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return fields;

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                fields[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }
        }
        catch
        {
        }

        return fields;
    }

    private static string GetField(IReadOnlyDictionary<string, string> fields, string name)
    {
        return fields.TryGetValue(name, out string? value) ? value ?? string.Empty : string.Empty;
    }

    private static bool ContainsIgnoreCase(string value, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return (value ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static DateTime GetLastWriteTimeUtcSafe(string file)
    {
        try
        {
            return File.GetLastWriteTimeUtc(file);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string GetLogDirectoryPath()
    {
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Logs");
    }

    private static string Safe(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
