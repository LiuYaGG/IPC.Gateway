/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：SqlSugarRuntimeStateRepository
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
using System.Security.Cryptography;
using System.Text;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Values;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class SqlSugarRuntimeStateRepository
{
    private const int MaxRecentErrors = 200;
    private readonly SqlSugarConnectionFactory _factory;

    public SqlSugarRuntimeStateRepository(GatewayDatabaseOptions options)
    {
        _factory = new SqlSugarConnectionFactory(options ?? new GatewayDatabaseOptions());
        new GatewayDatabaseMigrator(_factory).Migrate();
    }

    public GatewayRuntimeStateSnapshot Load(string projectId)
    {
        return LoadAsync(projectId).GetAwaiter().GetResult();
    }

    public async Task<GatewayRuntimeStateSnapshot> LoadAsync(string projectId)
    {
        string normalizedProjectId = NormalizeProjectId(projectId);
        using ISqlSugarClient db = _factory.Create();

        GatewayRuntimeStateSnapshot snapshot = new GatewayRuntimeStateSnapshot
        {
            Devices = (await db.Queryable<GatewayRuntimeDeviceStatusEntity>()
                .Where(item => item.ProjectId == normalizedProjectId)
                .ToListAsync())
                .Select(ToDeviceStatus)
                .ToList(),
            Tags = (await db.Queryable<GatewayRuntimeTagSnapshotEntity>()
                .Where(item => item.ProjectId == normalizedProjectId)
                .ToListAsync())
                .Select(ToTagSnapshot)
                .ToList(),
            RecentErrors = (await db.Queryable<GatewayRuntimeErrorEntity>()
                .Where(item => item.ProjectId == normalizedProjectId)
                .OrderBy(item => item.TimestampUtc, OrderByType.Desc)
                .Take(MaxRecentErrors)
                .ToListAsync())
                .Select(ToRuntimeError)
                .ToList(),
            UpdatedTime = DateTime.Now
        };

        return snapshot;
    }

    public void Save(string projectId, GatewayRuntimeStateSnapshot snapshot)
    {
        SaveAsync(projectId, snapshot).GetAwaiter().GetResult();
    }

    public async Task SaveAsync(string projectId, GatewayRuntimeStateSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        string normalizedProjectId = NormalizeProjectId(projectId);
        DateTime updatedUtc = DateTime.UtcNow;

        using ISqlSugarClient db = _factory.Create();
        await db.Ado.BeginTranAsync();
        try
        {
            HashSet<string> deviceIds = await UpsertDevicesAsync(db, normalizedProjectId, snapshot.Devices, updatedUtc);
            HashSet<string> tagIds = await UpsertTagsAsync(db, normalizedProjectId, snapshot.Tags, updatedUtc);
            HashSet<string> errorIds = await UpsertErrorsAsync(db, normalizedProjectId, snapshot.RecentErrors, updatedUtc);
            await DeleteMissingDevicesAsync(db, normalizedProjectId, deviceIds);
            await DeleteMissingTagsAsync(db, normalizedProjectId, tagIds);
            await DeleteMissingErrorsAsync(db, normalizedProjectId, errorIds);
            await TrimErrorsAsync(db, normalizedProjectId);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private static async Task<HashSet<string>> UpsertDevicesAsync(ISqlSugarClient db, string projectId, IList<DeviceRuntimeStatus> devices, DateTime updatedUtc)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<GatewayRuntimeDeviceStatusEntity> entities = new List<GatewayRuntimeDeviceStatusEntity>();
        if (devices == null)
            return seen;

        foreach (DeviceRuntimeStatus status in devices)
        {
            if (status == null)
                continue;

            GatewayRuntimeDeviceStatusEntity entity = ToDeviceEntity(projectId, status, updatedUtc);
            if (!seen.Add(entity.Id))
                continue;

            entities.Add(entity);
        }

        if (entities.Count > 0)
            await db.Storageable(entities).WhereColumns(item => item.Id).ExecuteCommandAsync();

        return seen;
    }

    private static async Task<HashSet<string>> UpsertTagsAsync(ISqlSugarClient db, string projectId, IList<TagValueSnapshot> tags, DateTime updatedUtc)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<GatewayRuntimeTagSnapshotEntity> entities = new List<GatewayRuntimeTagSnapshotEntity>();
        if (tags == null)
            return seen;

        foreach (TagValueSnapshot snapshot in tags)
        {
            if (snapshot == null)
                continue;

            GatewayRuntimeTagSnapshotEntity entity = ToTagEntity(projectId, snapshot, updatedUtc);
            if (!seen.Add(entity.Id))
                continue;

            entities.Add(entity);
        }

        if (entities.Count > 0)
            await db.Storageable(entities).WhereColumns(item => item.Id).ExecuteCommandAsync();

        return seen;
    }

    private static async Task DeleteMissingDevicesAsync(ISqlSugarClient db, string projectId, HashSet<string> activeIds)
    {
        List<string> ids = activeIds == null ? new List<string>() : activeIds.ToList();
        if (ids.Count == 0)
        {
            await db.Deleteable<GatewayRuntimeDeviceStatusEntity>()
                .Where(item => item.ProjectId == projectId)
                .ExecuteCommandAsync();
            return;
        }

        await db.Deleteable<GatewayRuntimeDeviceStatusEntity>()
            .Where(item => item.ProjectId == projectId && !ids.Contains(item.Id))
            .ExecuteCommandAsync();
    }

    private static async Task DeleteMissingTagsAsync(ISqlSugarClient db, string projectId, HashSet<string> activeIds)
    {
        List<string> ids = activeIds == null ? new List<string>() : activeIds.ToList();
        if (ids.Count == 0)
        {
            await db.Deleteable<GatewayRuntimeTagSnapshotEntity>()
                .Where(item => item.ProjectId == projectId)
                .ExecuteCommandAsync();
            return;
        }

        await db.Deleteable<GatewayRuntimeTagSnapshotEntity>()
            .Where(item => item.ProjectId == projectId && !ids.Contains(item.Id))
            .ExecuteCommandAsync();
    }

    private static async Task<HashSet<string>> UpsertErrorsAsync(ISqlSugarClient db, string projectId, IList<RuntimeErrorDetail> errors, DateTime updatedUtc)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (errors == null)
            return seen;

        List<GatewayRuntimeErrorEntity> entities = new List<GatewayRuntimeErrorEntity>();
        foreach (RuntimeErrorDetail error in errors)
        {
            if (error == null || string.IsNullOrWhiteSpace(error.Message))
                continue;

            GatewayRuntimeErrorEntity entity = ToErrorEntity(projectId, error, updatedUtc);
            if (!seen.Add(entity.Id))
                continue;

            entities.Add(entity);
        }

        if (entities.Count > 0)
            await db.Storageable(entities).WhereColumns(item => item.Id).ExecuteCommandAsync();

        return seen;
    }

    /// <summary>
    /// 删除当前快照中已经不存在的运行时错误，避免已恢复错误长期残留。
    /// </summary>
    private static async Task DeleteMissingErrorsAsync(ISqlSugarClient db, string projectId, HashSet<string> activeIds)
    {
        List<string> ids = activeIds == null ? new List<string>() : activeIds.ToList();
        if (ids.Count == 0)
        {
            await db.Deleteable<GatewayRuntimeErrorEntity>()
                .Where(item => item.ProjectId == projectId)
                .ExecuteCommandAsync();
            return;
        }

        await db.Deleteable<GatewayRuntimeErrorEntity>()
            .Where(item => item.ProjectId == projectId && !ids.Contains(item.Id))
            .ExecuteCommandAsync();
    }

    private static async Task TrimErrorsAsync(ISqlSugarClient db, string projectId)
    {
        List<string> expiredIds = (await db.Queryable<GatewayRuntimeErrorEntity>()
            .Where(item => item.ProjectId == projectId)
            .OrderBy(item => item.TimestampUtc, OrderByType.Desc)
            .Select(item => item.Id)
            .ToListAsync())
            .Skip(MaxRecentErrors)
            .ToList();

        if (expiredIds.Count == 0)
            return;

        await db.Deleteable<GatewayRuntimeErrorEntity>()
            .Where(item => expiredIds.Contains(item.Id))
            .ExecuteCommandAsync();
    }

    private static GatewayRuntimeDeviceStatusEntity ToDeviceEntity(string projectId, DeviceRuntimeStatus status, DateTime updatedUtc)
    {
        string deviceKey = BuildDeviceKey(status.ChannelId, status.DeviceId);
        return new GatewayRuntimeDeviceStatusEntity
        {
            Id = HashKey(projectId, "device", deviceKey),
            ProjectId = projectId,
            ChannelId = status.ChannelId ?? string.Empty,
            ChannelName = status.ChannelName ?? string.Empty,
            DeviceId = status.DeviceId ?? string.Empty,
            DeviceName = status.DeviceName ?? string.Empty,
            Protocol = status.Protocol ?? string.Empty,
            Enabled = status.Enabled,
            IsConnected = status.IsConnected,
            Status = status.Status ?? string.Empty,
            ConsecutiveFailures = status.ConsecutiveFailures,
            TotalReads = status.TotalReads,
            SuccessfulReads = status.SuccessfulReads,
            FailedReads = status.FailedReads,
            SuccessRate = status.SuccessRate,
            LastPollUtc = ToUtc(status.LastPollTime),
            LastSuccessUtc = ToUtc(status.LastSuccessTime),
            LastFailureUtc = ToUtc(status.LastFailureTime),
            LastReconnectDelayMs = status.LastReconnectDelayMs,
            LastError = status.LastError ?? string.Empty,
            UpdatedUtc = updatedUtc
        };
    }

    private static GatewayRuntimeTagSnapshotEntity ToTagEntity(string projectId, TagValueSnapshot snapshot, DateTime updatedUtc)
    {
        string tagKey = BuildTagKey(snapshot);
        return new GatewayRuntimeTagSnapshotEntity
        {
            Id = HashKey(projectId, "tag", tagKey),
            ProjectId = projectId,
            ChannelId = snapshot.ChannelId ?? string.Empty,
            ChannelName = snapshot.ChannelName ?? string.Empty,
            DeviceId = snapshot.DeviceId ?? string.Empty,
            DeviceName = snapshot.DeviceName ?? string.Empty,
            GroupId = snapshot.GroupId ?? string.Empty,
            GroupName = snapshot.GroupName ?? string.Empty,
            TagId = snapshot.TagId ?? string.Empty,
            TagName = snapshot.TagName ?? string.Empty,
            DataType = snapshot.DataType ?? string.Empty,
            RawValueText = snapshot.RawValueText ?? string.Empty,
            ValueText = snapshot.ValueText ?? string.Empty,
            Unit = snapshot.Unit ?? string.Empty,
            PointCode = snapshot.PointCode ?? string.Empty,
            Source = snapshot.Source ?? string.Empty,
            Quality = snapshot.Quality.ToString(),
            CleaningApplied = snapshot.CleaningApplied,
            CleaningAction = snapshot.CleaningAction ?? string.Empty,
            CleaningMessage = snapshot.CleaningMessage ?? string.Empty,
            TimestampUtc = ToUtc(snapshot.Timestamp),
            ErrorMessage = snapshot.ErrorMessage ?? string.Empty,
            UpdatedUtc = updatedUtc
        };
    }

    private static GatewayRuntimeErrorEntity ToErrorEntity(string projectId, RuntimeErrorDetail error, DateTime updatedUtc)
    {
        DateTime timestampUtc = ToUtc(error.Timestamp);
        return new GatewayRuntimeErrorEntity
        {
            Id = HashKey(projectId, "error", error.Category, error.ChannelId, error.DeviceId, error.GroupId, error.TagId, error.Message, timestampUtc.Ticks.ToString()),
            ProjectId = projectId,
            Category = error.Category ?? string.Empty,
            ChannelId = error.ChannelId ?? string.Empty,
            ChannelName = error.ChannelName ?? string.Empty,
            DeviceId = error.DeviceId ?? string.Empty,
            DeviceName = error.DeviceName ?? string.Empty,
            GroupId = error.GroupId ?? string.Empty,
            GroupName = error.GroupName ?? string.Empty,
            TagId = error.TagId ?? string.Empty,
            TagName = error.TagName ?? string.Empty,
            Message = error.Message ?? string.Empty,
            Suggestion = error.Suggestion ?? string.Empty,
            Source = error.Source ?? string.Empty,
            TimestampUtc = timestampUtc,
            UpdatedUtc = updatedUtc
        };
    }

    private static DeviceRuntimeStatus ToDeviceStatus(GatewayRuntimeDeviceStatusEntity entity)
    {
        return new DeviceRuntimeStatus
        {
            ChannelId = entity.ChannelId ?? string.Empty,
            ChannelName = entity.ChannelName ?? string.Empty,
            DeviceId = entity.DeviceId ?? string.Empty,
            DeviceName = entity.DeviceName ?? string.Empty,
            Protocol = entity.Protocol ?? string.Empty,
            Enabled = entity.Enabled,
            IsConnected = entity.IsConnected,
            Status = entity.Status ?? string.Empty,
            ConsecutiveFailures = entity.ConsecutiveFailures,
            TotalReads = entity.TotalReads,
            SuccessfulReads = entity.SuccessfulReads,
            FailedReads = entity.FailedReads,
            SuccessRate = entity.SuccessRate,
            LastPollTime = FromUtc(entity.LastPollUtc),
            LastSuccessTime = FromUtc(entity.LastSuccessUtc),
            LastFailureTime = FromUtc(entity.LastFailureUtc),
            LastReconnectDelayMs = entity.LastReconnectDelayMs,
            LastError = entity.LastError ?? string.Empty
        };
    }

    private static TagValueSnapshot ToTagSnapshot(GatewayRuntimeTagSnapshotEntity entity)
    {
        TagQuality quality;
        if (!Enum.TryParse(entity.Quality, true, out quality))
            quality = TagQuality.Unknown;

        string rawValueText = entity.RawValueText ?? string.Empty;
        string valueText = entity.ValueText ?? string.Empty;
        return new TagValueSnapshot
        {
            ChannelId = entity.ChannelId ?? string.Empty,
            ChannelName = entity.ChannelName ?? string.Empty,
            DeviceId = entity.DeviceId ?? string.Empty,
            GroupId = entity.GroupId ?? string.Empty,
            TagId = entity.TagId ?? string.Empty,
            DeviceName = entity.DeviceName ?? string.Empty,
            GroupName = entity.GroupName ?? string.Empty,
            TagName = entity.TagName ?? string.Empty,
            RawValue = rawValueText,
            RawValueText = rawValueText,
            Value = valueText,
            ValueText = valueText,
            Unit = entity.Unit ?? string.Empty,
            PointCode = entity.PointCode ?? string.Empty,
            Source = entity.Source ?? string.Empty,
            DataType = entity.DataType ?? string.Empty,
            CleaningApplied = entity.CleaningApplied,
            CleaningAction = entity.CleaningAction ?? string.Empty,
            CleaningMessage = entity.CleaningMessage ?? string.Empty,
            Quality = quality,
            Timestamp = FromUtc(entity.TimestampUtc),
            ErrorMessage = entity.ErrorMessage ?? string.Empty
        };
    }

    private static RuntimeErrorDetail ToRuntimeError(GatewayRuntimeErrorEntity entity)
    {
        return new RuntimeErrorDetail
        {
            Category = entity.Category ?? string.Empty,
            ChannelId = entity.ChannelId ?? string.Empty,
            ChannelName = entity.ChannelName ?? string.Empty,
            DeviceId = entity.DeviceId ?? string.Empty,
            DeviceName = entity.DeviceName ?? string.Empty,
            GroupId = entity.GroupId ?? string.Empty,
            GroupName = entity.GroupName ?? string.Empty,
            TagId = entity.TagId ?? string.Empty,
            TagName = entity.TagName ?? string.Empty,
            Message = entity.Message ?? string.Empty,
            Suggestion = entity.Suggestion ?? string.Empty,
            Source = entity.Source ?? string.Empty,
            Timestamp = FromUtc(entity.TimestampUtc)
        };
    }

    private static string NormalizeProjectId(string projectId)
    {
        return string.IsNullOrWhiteSpace(projectId) ? "default" : projectId.Trim();
    }

    private static string BuildDeviceKey(string channelId, string deviceId)
    {
        return string.Join("/", channelId ?? string.Empty, deviceId ?? string.Empty);
    }

    private static string BuildTagKey(TagValueSnapshot snapshot)
    {
        return string.Join("/",
            snapshot.ChannelId ?? string.Empty,
            snapshot.DeviceId ?? string.Empty,
            snapshot.GroupId ?? string.Empty,
            snapshot.TagId ?? string.Empty);
    }

    private static string HashKey(params string[] parts)
    {
        string value = string.Join("|", parts.Select(item => item ?? string.Empty));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static DateTime ToUtc(DateTime value)
    {
        if (value == DateTime.MinValue)
            return DateTime.MinValue;
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    }

    private static DateTime FromUtc(DateTime value)
    {
        if (value == DateTime.MinValue)
            return DateTime.MinValue;
        return DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime();
    }
}
