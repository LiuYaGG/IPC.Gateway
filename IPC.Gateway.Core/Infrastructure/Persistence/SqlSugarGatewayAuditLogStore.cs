/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：SqlSugarGatewayAuditLogStore
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

public sealed class SqlSugarGatewayAuditLogStore : IGatewayAuditLogStore
{
    private readonly SqlSugarConnectionFactory _factory;
    private readonly GatewayAuditLogOptions _options;
    private readonly object _cleanupSync = new object();
    private DateTime _lastCleanupDate = DateTime.MinValue;

    public SqlSugarGatewayAuditLogStore(GatewayDatabaseOptions options)
        : this(options, new GatewayAuditLogOptions())
    {
    }

    public SqlSugarGatewayAuditLogStore(GatewayDatabaseOptions options, GatewayAuditLogOptions auditOptions)
    {
        _options = auditOptions?.Clone() ?? new GatewayAuditLogOptions();
        _factory = new SqlSugarConnectionFactory(options ?? new GatewayDatabaseOptions());
        new GatewayDatabaseMigrator(_factory).Migrate();
        CleanupExpired(DateTime.Now);
    }

    public void Append(GatewayAuditLogEntry entry)
    {
        if (entry == null)
            return;

        CleanupExpired(DateTime.Now);
        using ISqlSugarClient db = _factory.Create();
        db.Insertable(ToEntity(entry)).ExecuteCommand();
    }

    public IReadOnlyList<GatewayAuditLogEntry> Query(GatewayAuditLogQuery query)
    {
        query ??= new GatewayAuditLogQuery();
        int limit = GatewayAuditLog.ClampLimit(query.Limit);
        int offset = GatewayAuditLog.ClampOffset(query.Offset);
        string target = (query.Target ?? string.Empty).Trim().ToLowerInvariant();
        string outcome = (query.Outcome ?? string.Empty).Trim().ToLowerInvariant();
        string username = (query.UserName ?? string.Empty).Trim().ToLowerInvariant();
        DateTime? fromUtc = query.FromTime.HasValue ? ToUtc(query.FromTime.Value) : null;
        DateTime? toUtc = query.ToTime.HasValue ? ToUtc(query.ToTime.Value) : null;

        using ISqlSugarClient db = _factory.Create();
        ISugarQueryable<GatewayAuditLogEntity> queryable = db.Queryable<GatewayAuditLogEntity>();

        if (!string.IsNullOrWhiteSpace(target))
            queryable = queryable.Where(item => item.Target.ToLower().Contains(target));

        if (!string.IsNullOrWhiteSpace(outcome))
            queryable = queryable.Where(item => item.Outcome.ToLower() == outcome);

        if (!string.IsNullOrWhiteSpace(username))
            queryable = queryable.Where(item => item.UserName.ToLower().Contains(username));

        if (fromUtc.HasValue)
            queryable = queryable.Where(item => item.TimestampUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            queryable = queryable.Where(item => item.TimestampUtc <= toUtc.Value);

        return queryable
            .OrderBy(item => item.TimestampUtc, OrderByType.Desc)
            .Skip(offset)
            .Take(limit)
            .ToList()
            .Select(ToEntry)
            .ToList();
    }

    public int DeleteOlderThan(DateTime timestamp)
    {
        DateTime thresholdUtc = ToUtc(timestamp);
        using ISqlSugarClient db = _factory.Create();
        return db.Deleteable<GatewayAuditLogEntity>()
            .Where(item => item.TimestampUtc < thresholdUtc)
            .ExecuteCommand();
    }

    internal int CleanupExpired(DateTime now)
    {
        lock (_cleanupSync)
        {
            DateTime cleanupDate = now.Date;
            if (_lastCleanupDate == cleanupDate)
                return 0;

            _lastCleanupDate = cleanupDate;
            int retentionDays = GatewayAuditLogOptions.ClampRetentionDays(_options.RetentionDays);
            return DeleteOlderThan(now.AddDays(-retentionDays));
        }
    }

    private static GatewayAuditLogEntity ToEntity(GatewayAuditLogEntry entry)
    {
        return new GatewayAuditLogEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            TimestampUtc = ToUtc(entry.Timestamp),
            Level = entry.Level ?? string.Empty,
            Action = entry.Action ?? string.Empty,
            Target = entry.Target ?? string.Empty,
            Outcome = entry.Outcome ?? string.Empty,
            UserName = entry.UserName ?? string.Empty,
            Role = entry.Role ?? string.Empty,
            RemoteIpAddress = entry.RemoteIpAddress ?? string.Empty,
            Method = entry.Method ?? string.Empty,
            Path = entry.Path ?? string.Empty,
            TraceId = entry.TraceId ?? string.Empty,
            ErrorMessage = entry.ErrorMessage ?? string.Empty,
            RawDetail = entry.RawDetail ?? string.Empty
        };
    }

    private static GatewayAuditLogEntry ToEntry(GatewayAuditLogEntity entity)
    {
        return new GatewayAuditLogEntry
        {
            Timestamp = FromUtc(entity.TimestampUtc),
            Level = entity.Level ?? string.Empty,
            Action = entity.Action ?? string.Empty,
            Target = entity.Target ?? string.Empty,
            Outcome = entity.Outcome ?? string.Empty,
            UserName = entity.UserName ?? string.Empty,
            Role = entity.Role ?? string.Empty,
            RemoteIpAddress = entity.RemoteIpAddress ?? string.Empty,
            Method = entity.Method ?? string.Empty,
            Path = entity.Path ?? string.Empty,
            TraceId = entity.TraceId ?? string.Empty,
            ErrorMessage = entity.ErrorMessage ?? string.Empty,
            RawDetail = entity.RawDetail ?? string.Empty,
            Source = "database"
        };
    }

    private static DateTime ToUtc(DateTime timestamp)
    {
        if (timestamp == default)
            return DateTime.UtcNow;

        if (timestamp.Kind == DateTimeKind.Utc)
            return timestamp;

        DateTime local = timestamp.Kind == DateTimeKind.Local
            ? timestamp
            : DateTime.SpecifyKind(timestamp, DateTimeKind.Local);
        return local.ToUniversalTime();
    }

    private static DateTime FromUtc(DateTime timestampUtc)
    {
        if (timestampUtc == default)
            return DateTime.MinValue;

        DateTime utc = timestampUtc.Kind == DateTimeKind.Utc
            ? timestampUtc
            : DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
        return utc.ToLocalTime();
    }
}
