/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：SqlSugarGatewayConfigurationRepository
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
using System.Text.Json;
using System.Text.Json.Serialization;
using IPC.EdgeGateway;
using IPC.Gateway.DataProcessing;
using IPC.Gateway.Core.Domain.Configuration;
using IPC.Gateway.Core.Gateway;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public class SqlSugarGatewayConfigurationRepository : IGatewayConfigurationRepository
{
    private readonly SqlSugarConnectionFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GatewaySecretProtector _secretProtector;

    public SqlSugarGatewayConfigurationRepository(GatewayDatabaseOptions options)
        : this(options, new GatewaySecretProtectionOptions())
    {
    }

    public SqlSugarGatewayConfigurationRepository(GatewayDatabaseOptions options, GatewaySecretProtectionOptions? secretProtectionOptions)
    {
        _factory = new SqlSugarConnectionFactory(options);
        _secretProtector = new GatewaySecretProtector(secretProtectionOptions);
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());
        EnsureSchema();
    }

    public ProjectConfig LoadOrCreateProject(Func<ProjectConfig> defaultFactory)
    {
        string json;
        if (TryLoadActive(GatewayConfigurationType.Project, out json))
        {
            ProjectConfig? loaded = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
            if (loaded != null)
            {
                UnprotectProjectSecrets(loaded);
                ProjectConfigStore.Normalize(loaded);
                return loaded;
            }
        }

        ProjectConfig config = defaultFactory == null ? new ProjectConfig() : defaultFactory();
        SaveProject(config, "System", "初始化默认项目配置");
        return config;
    }

    public async Task<ProjectConfig> LoadOrCreateProjectAsync(Func<ProjectConfig> defaultFactory)
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.Project);
        if (!string.IsNullOrWhiteSpace(json))
        {
            ProjectConfig? loaded = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
            if (loaded != null)
            {
                UnprotectProjectSecrets(loaded);
                ProjectConfigStore.Normalize(loaded);
                return loaded;
            }
        }

        ProjectConfig config = defaultFactory == null ? new ProjectConfig() : defaultFactory();
        await SaveProjectAsync(config, "System", "Initialize default project configuration");
        return config;
    }

    public ProjectConfig LoadProject()
    {
        string json;
        if (!TryLoadActive(GatewayConfigurationType.Project, out json))
            throw new InvalidOperationException("数据库中没有启用的项目配置。");

        ProjectConfig? config = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
        if (config == null)
            throw new InvalidOperationException("项目配置内容为空。");

        UnprotectProjectSecrets(config);
        ProjectConfigStore.Normalize(config);
        return config;
    }

    public async Task<ProjectConfig> LoadProjectAsync()
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.Project);
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Project configuration was not found in database.");

        ProjectConfig? config = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
        if (config == null)
            throw new InvalidOperationException("Project configuration payload is empty.");

        UnprotectProjectSecrets(config);
        ProjectConfigStore.Normalize(config);
        return config;
    }

    public int SaveProject(ProjectConfig config, string source, string description)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        ProjectConfigStore.Normalize(config);
        ProjectConfigValidationResult validation = ProjectConfigValidator.Validate(config);
        if (!validation.IsValid)
            throw new InvalidOperationException("项目配置校验失败：" + string.Join("；", validation.Errors));

        ProjectConfig stored = ProjectConfigCloner.Clone(config);
        ProtectProjectSecrets(stored);
        return Save(GatewayConfigurationType.Project, JsonSerializer.Serialize(stored, _jsonOptions), source, description);
    }

    public async Task<int> SaveProjectAsync(ProjectConfig config, string source, string description)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        ProjectConfigStore.Normalize(config);
        ProjectConfigValidationResult validation = ProjectConfigValidator.Validate(config);
        if (!validation.IsValid)
            throw new InvalidOperationException("Project configuration validation failed: " + string.Join("; ", validation.Errors));

        ProjectConfig stored = ProjectConfigCloner.Clone(config);
        ProtectProjectSecrets(stored);
        return await SaveAsync(GatewayConfigurationType.Project, JsonSerializer.Serialize(stored, _jsonOptions), source, description);
    }

    public MqttGatewayOptions LoadOrCreateMqtt(MqttGatewayOptions defaultOptions)
    {
        string json;
        if (TryLoadActive(GatewayConfigurationType.Mqtt, out json))
        {
            MqttGatewayOptions? loaded = JsonSerializer.Deserialize<MqttGatewayOptions>(json, _jsonOptions);
            if (loaded != null)
            {
                UnprotectMqttSecrets(loaded);
                return NormalizeMqtt(loaded);
            }
        }

        MqttGatewayOptions options = defaultOptions == null ? new MqttGatewayOptions() : defaultOptions.Clone();
        SaveMqtt(options, "System", "初始化默认 MQTT 参数");
        return options;
    }

    public async Task<MqttGatewayOptions> LoadOrCreateMqttAsync(MqttGatewayOptions defaultOptions)
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.Mqtt);
        if (!string.IsNullOrWhiteSpace(json))
        {
            MqttGatewayOptions? loaded = JsonSerializer.Deserialize<MqttGatewayOptions>(json, _jsonOptions);
            if (loaded != null)
            {
                UnprotectMqttSecrets(loaded);
                return NormalizeMqtt(loaded);
            }
        }

        MqttGatewayOptions options = defaultOptions == null ? new MqttGatewayOptions() : defaultOptions.Clone();
        await SaveMqttAsync(options, "System", "Initialize default MQTT options");
        return options;
    }

    public int SaveMqtt(MqttGatewayOptions options, string source, string description)
    {
        MqttGatewayOptions normalized = NormalizeMqtt(options == null ? new MqttGatewayOptions() : options.Clone());
        MqttGatewayOptions stored = normalized.Clone();
        ProtectMqttSecrets(stored);
        return Save(GatewayConfigurationType.Mqtt, JsonSerializer.Serialize(stored, _jsonOptions), source, description);
    }

    public async Task<int> SaveMqttAsync(MqttGatewayOptions options, string source, string description)
    {
        MqttGatewayOptions normalized = NormalizeMqtt(options == null ? new MqttGatewayOptions() : options.Clone());
        MqttGatewayOptions stored = normalized.Clone();
        ProtectMqttSecrets(stored);
        return await SaveAsync(GatewayConfigurationType.Mqtt, JsonSerializer.Serialize(stored, _jsonOptions), source, description);
    }

    public OpcUaServerOptions LoadOrCreateOpcUa(OpcUaServerOptions defaultOptions)
    {
        string json;
        if (TryLoadActive(GatewayConfigurationType.OpcUa, out json))
        {
            OpcUaServerOptions? loaded = JsonSerializer.Deserialize<OpcUaServerOptions>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeOpcUa(loaded);
        }

        OpcUaServerOptions options = NormalizeOpcUa(defaultOptions == null ? new OpcUaServerOptions() : defaultOptions.Clone());
        SaveOpcUa(options, "System", "Initialize default OPC UA Server options");
        return options;
    }

    public async Task<OpcUaServerOptions> LoadOrCreateOpcUaAsync(OpcUaServerOptions defaultOptions)
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.OpcUa);
        if (!string.IsNullOrWhiteSpace(json))
        {
            OpcUaServerOptions? loaded = JsonSerializer.Deserialize<OpcUaServerOptions>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeOpcUa(loaded);
        }

        OpcUaServerOptions options = NormalizeOpcUa(defaultOptions == null ? new OpcUaServerOptions() : defaultOptions.Clone());
        await SaveOpcUaAsync(options, "System", "Initialize default OPC UA Server options");
        return options;
    }

    public int SaveOpcUa(OpcUaServerOptions options, string source, string description)
    {
        OpcUaServerOptions normalized = NormalizeOpcUa(options == null ? new OpcUaServerOptions() : options.Clone());
        return Save(GatewayConfigurationType.OpcUa, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public async Task<int> SaveOpcUaAsync(OpcUaServerOptions options, string source, string description)
    {
        OpcUaServerOptions normalized = NormalizeOpcUa(options == null ? new OpcUaServerOptions() : options.Clone());
        return await SaveAsync(GatewayConfigurationType.OpcUa, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public LocalHistoryOptions LoadOrCreateHistory(LocalHistoryOptions defaultOptions)
    {
        string json;
        if (TryLoadActive(GatewayConfigurationType.History, out json))
        {
            LocalHistoryOptions? loaded = JsonSerializer.Deserialize<LocalHistoryOptions>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeHistory(loaded);
        }

        LocalHistoryOptions options = defaultOptions == null ? new LocalHistoryOptions() : defaultOptions.Clone();
        SaveHistory(options, "System", "Initialize default local history options");
        return NormalizeHistory(options);
    }

    public async Task<LocalHistoryOptions> LoadOrCreateHistoryAsync(LocalHistoryOptions defaultOptions)
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.History);
        if (!string.IsNullOrWhiteSpace(json))
        {
            LocalHistoryOptions? loaded = JsonSerializer.Deserialize<LocalHistoryOptions>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeHistory(loaded);
        }

        LocalHistoryOptions options = defaultOptions == null ? new LocalHistoryOptions() : defaultOptions.Clone();
        await SaveHistoryAsync(options, "System", "Initialize default local history options");
        return NormalizeHistory(options);
    }

    public int SaveHistory(LocalHistoryOptions options, string source, string description)
    {
        LocalHistoryOptions normalized = NormalizeHistory(options == null ? new LocalHistoryOptions() : options.Clone());
        return Save(GatewayConfigurationType.History, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public async Task<int> SaveHistoryAsync(LocalHistoryOptions options, string source, string description)
    {
        LocalHistoryOptions normalized = NormalizeHistory(options == null ? new LocalHistoryOptions() : options.Clone());
        return await SaveAsync(GatewayConfigurationType.History, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public StorageHealthThresholds LoadOrCreateStorageHealth(StorageHealthThresholds defaultThresholds)
    {
        string json;
        if (TryLoadActive(GatewayConfigurationType.StorageHealth, out json))
        {
            StorageHealthThresholds? loaded = JsonSerializer.Deserialize<StorageHealthThresholds>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeStorageHealth(loaded);
        }

        StorageHealthThresholds thresholds = NormalizeStorageHealth(defaultThresholds == null ? new StorageHealthThresholds() : defaultThresholds.Clone());
        SaveStorageHealth(thresholds, "System", "Initialize default storage health thresholds");
        return thresholds;
    }

    public async Task<StorageHealthThresholds> LoadOrCreateStorageHealthAsync(StorageHealthThresholds defaultThresholds)
    {
        string json = await TryLoadActiveAsync(GatewayConfigurationType.StorageHealth);
        if (!string.IsNullOrWhiteSpace(json))
        {
            StorageHealthThresholds? loaded = JsonSerializer.Deserialize<StorageHealthThresholds>(json, _jsonOptions);
            if (loaded != null)
                return NormalizeStorageHealth(loaded);
        }

        StorageHealthThresholds thresholds = NormalizeStorageHealth(defaultThresholds == null ? new StorageHealthThresholds() : defaultThresholds.Clone());
        await SaveStorageHealthAsync(thresholds, "System", "Initialize default storage health thresholds");
        return thresholds;
    }

    public int SaveStorageHealth(StorageHealthThresholds thresholds, string source, string description)
    {
        StorageHealthThresholds normalized = NormalizeStorageHealth(thresholds == null ? new StorageHealthThresholds() : thresholds.Clone());
        return Save(GatewayConfigurationType.StorageHealth, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public async Task<int> SaveStorageHealthAsync(StorageHealthThresholds thresholds, string source, string description)
    {
        StorageHealthThresholds normalized = NormalizeStorageHealth(thresholds == null ? new StorageHealthThresholds() : thresholds.Clone());
        return await SaveAsync(GatewayConfigurationType.StorageHealth, JsonSerializer.Serialize(normalized, _jsonOptions), source, description);
    }

    public IList<GatewayConfigurationVersionInfo> GetVersions(string configType, int maxCount)
    {
        int limit = maxCount <= 0 ? 50 : Math.Min(maxCount, 500);
        string normalizedType = NormalizeConfigTypeFilter(configType);

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        List<GatewayConfigurationEntity> rows = db.Queryable<GatewayConfigurationEntity>()
            .WhereIF(!string.IsNullOrWhiteSpace(normalizedType), item => item.ConfigType == normalizedType)
            .OrderBy(item => item.CreatedUtc, SqlSugar.OrderByType.Desc)
            .Take(limit)
            .ToList();

        return rows.Select(item => new GatewayConfigurationVersionInfo
        {
            Id = item.Id,
            ConfigType = item.ConfigType,
            Version = item.Version,
            Active = item.Active,
            CreatedTime = DateTime.SpecifyKind(item.CreatedUtc, DateTimeKind.Utc).ToLocalTime(),
            Source = item.Source ?? string.Empty,
            Description = item.Description ?? string.Empty
        }).ToList();
    }

    public async Task<IList<GatewayConfigurationVersionInfo>> GetVersionsAsync(string configType, int maxCount)
    {
        int limit = maxCount <= 0 ? 50 : Math.Min(maxCount, 500);
        string normalizedType = NormalizeConfigTypeFilter(configType);

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        List<GatewayConfigurationEntity> rows = await db.Queryable<GatewayConfigurationEntity>()
            .WhereIF(!string.IsNullOrWhiteSpace(normalizedType), item => item.ConfigType == normalizedType)
            .OrderBy(item => item.CreatedUtc, SqlSugar.OrderByType.Desc)
            .Take(limit)
            .ToListAsync();

        return rows.Select(item => new GatewayConfigurationVersionInfo
        {
            Id = item.Id,
            ConfigType = item.ConfigType,
            Version = item.Version,
            Active = item.Active,
            CreatedTime = DateTime.SpecifyKind(item.CreatedUtc, DateTimeKind.Utc).ToLocalTime(),
            Source = item.Source ?? string.Empty,
            Description = item.Description ?? string.Empty
        }).ToList();
    }

    public ProjectConfig RollbackProject(int version)
    {
        string json = ActivateVersion(GatewayConfigurationType.Project, version);
        ProjectConfig? config = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
        if (config == null)
            throw new InvalidOperationException("回滚后的项目配置为空。");

        UnprotectProjectSecrets(config);
        ProjectConfigStore.Normalize(config);
        return config;
    }

    public async Task<ProjectConfig> RollbackProjectAsync(int version)
    {
        string json = await ActivateVersionAsync(GatewayConfigurationType.Project, version);
        ProjectConfig? config = JsonSerializer.Deserialize<ProjectConfig>(json, _jsonOptions);
        if (config == null)
            throw new InvalidOperationException("Rolled back project configuration is empty.");

        UnprotectProjectSecrets(config);
        ProjectConfigStore.Normalize(config);
        return config;
    }

    public MqttGatewayOptions RollbackMqtt(int version)
    {
        string json = ActivateVersion(GatewayConfigurationType.Mqtt, version);
        MqttGatewayOptions? options = JsonSerializer.Deserialize<MqttGatewayOptions>(json, _jsonOptions);
        options ??= new MqttGatewayOptions();
        UnprotectMqttSecrets(options);
        return NormalizeMqtt(options);
    }

    public async Task<MqttGatewayOptions> RollbackMqttAsync(int version)
    {
        string json = await ActivateVersionAsync(GatewayConfigurationType.Mqtt, version);
        MqttGatewayOptions? options = JsonSerializer.Deserialize<MqttGatewayOptions>(json, _jsonOptions);
        options ??= new MqttGatewayOptions();
        UnprotectMqttSecrets(options);
        return NormalizeMqtt(options);
    }

    public OpcUaServerOptions RollbackOpcUa(int version)
    {
        string json = ActivateVersion(GatewayConfigurationType.OpcUa, version);
        OpcUaServerOptions? options = JsonSerializer.Deserialize<OpcUaServerOptions>(json, _jsonOptions);
        return NormalizeOpcUa(options == null ? new OpcUaServerOptions() : options);
    }

    public async Task<OpcUaServerOptions> RollbackOpcUaAsync(int version)
    {
        string json = await ActivateVersionAsync(GatewayConfigurationType.OpcUa, version);
        OpcUaServerOptions? options = JsonSerializer.Deserialize<OpcUaServerOptions>(json, _jsonOptions);
        return NormalizeOpcUa(options == null ? new OpcUaServerOptions() : options);
    }

    public LocalHistoryOptions RollbackHistory(int version)
    {
        string json = ActivateVersion(GatewayConfigurationType.History, version);
        LocalHistoryOptions? options = JsonSerializer.Deserialize<LocalHistoryOptions>(json, _jsonOptions);
        return NormalizeHistory(options == null ? new LocalHistoryOptions() : options);
    }

    public async Task<LocalHistoryOptions> RollbackHistoryAsync(int version)
    {
        string json = await ActivateVersionAsync(GatewayConfigurationType.History, version);
        LocalHistoryOptions? options = JsonSerializer.Deserialize<LocalHistoryOptions>(json, _jsonOptions);
        return NormalizeHistory(options == null ? new LocalHistoryOptions() : options);
    }

    public StorageHealthThresholds RollbackStorageHealth(int version)
    {
        string json = ActivateVersion(GatewayConfigurationType.StorageHealth, version);
        StorageHealthThresholds? thresholds = JsonSerializer.Deserialize<StorageHealthThresholds>(json, _jsonOptions);
        return NormalizeStorageHealth(thresholds == null ? new StorageHealthThresholds() : thresholds);
    }

    public async Task<StorageHealthThresholds> RollbackStorageHealthAsync(int version)
    {
        string json = await ActivateVersionAsync(GatewayConfigurationType.StorageHealth, version);
        StorageHealthThresholds? thresholds = JsonSerializer.Deserialize<StorageHealthThresholds>(json, _jsonOptions);
        return NormalizeStorageHealth(thresholds == null ? new StorageHealthThresholds() : thresholds);
    }

    private bool TryLoadActive(string configType, out string json)
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        GatewayConfigurationEntity row = db.Queryable<GatewayConfigurationEntity>()
            .Where(item => item.ConfigType == configType && item.Active)
            .OrderBy(item => item.Version, SqlSugar.OrderByType.Desc)
            .First();

        json = row == null ? string.Empty : row.Payload ?? string.Empty;
        return !string.IsNullOrWhiteSpace(json);
    }

    private async Task<string> TryLoadActiveAsync(string configType)
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        GatewayConfigurationEntity row = await db.Queryable<GatewayConfigurationEntity>()
            .Where(item => item.ConfigType == configType && item.Active)
            .OrderBy(item => item.Version, SqlSugar.OrderByType.Desc)
            .FirstAsync();

        return row == null ? string.Empty : row.Payload ?? string.Empty;
    }

    private int Save(string configType, string payload, string source, string description)
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        db.Ado.BeginTran();
        try
        {
            int nextVersion = db.Queryable<GatewayConfigurationEntity>()
                .Where(item => item.ConfigType == configType)
                .Max(item => item.Version) + 1;
            if (nextVersion <= 0)
                nextVersion = 1;

            db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == false)
                .Where(item => item.ConfigType == configType)
                .ExecuteCommand();

            db.Insertable(new GatewayConfigurationEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                ConfigType = configType,
                Version = nextVersion,
                Payload = payload,
                CreatedUtc = DateTime.UtcNow,
                Active = true,
                Source = source ?? string.Empty,
                Description = description ?? string.Empty
            }).ExecuteCommand();

            db.Ado.CommitTran();
            return nextVersion;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    private async Task<int> SaveAsync(string configType, string payload, string source, string description)
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        await db.Ado.BeginTranAsync();
        try
        {
            int nextVersion = await db.Queryable<GatewayConfigurationEntity>()
                .Where(item => item.ConfigType == configType)
                .MaxAsync(item => item.Version) + 1;
            if (nextVersion <= 0)
                nextVersion = 1;

            await db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == false)
                .Where(item => item.ConfigType == configType)
                .ExecuteCommandAsync();

            await db.Insertable(new GatewayConfigurationEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                ConfigType = configType,
                Version = nextVersion,
                Payload = payload,
                CreatedUtc = DateTime.UtcNow,
                Active = true,
                Source = source ?? string.Empty,
                Description = description ?? string.Empty
            }).ExecuteCommandAsync();

            await db.Ado.CommitTranAsync();
            return nextVersion;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private string ActivateVersion(string configType, int version)
    {
        if (version <= 0)
            throw new ArgumentException("配置版本号必须大于 0。", nameof(version));

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        db.Ado.BeginTran();
        try
        {
            GatewayConfigurationEntity row = db.Queryable<GatewayConfigurationEntity>()
                .Where(item => item.ConfigType == configType && item.Version == version)
                .First();

            if (row == null || string.IsNullOrWhiteSpace(row.Payload))
                throw new InvalidOperationException("未找到指定配置版本。");

            db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == false)
                .Where(item => item.ConfigType == configType)
                .ExecuteCommand();

            db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == true)
                .Where(item => item.ConfigType == configType && item.Version == version)
                .ExecuteCommand();

            db.Ado.CommitTran();
            return row.Payload;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }

    private async Task<string> ActivateVersionAsync(string configType, int version)
    {
        if (version <= 0)
            throw new ArgumentException("Configuration version must be greater than 0.", nameof(version));

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        await db.Ado.BeginTranAsync();
        try
        {
            GatewayConfigurationEntity row = await db.Queryable<GatewayConfigurationEntity>()
                .Where(item => item.ConfigType == configType && item.Version == version)
                .FirstAsync();

            if (row == null || string.IsNullOrWhiteSpace(row.Payload))
                throw new InvalidOperationException("The specified configuration version was not found.");

            await db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == false)
                .Where(item => item.ConfigType == configType)
                .ExecuteCommandAsync();

            await db.Updateable<GatewayConfigurationEntity>()
                .SetColumns(item => item.Active == true)
                .Where(item => item.ConfigType == configType && item.Version == version)
                .ExecuteCommandAsync();

            await db.Ado.CommitTranAsync();
            return row.Payload;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }

    private void EnsureSchema()
    {
        new GatewayDatabaseMigrator(_factory).Migrate();
    }

    private static MqttGatewayOptions NormalizeMqtt(MqttGatewayOptions options)
    {
        options.Port = MqttGatewayOptions.ClampPort(options.Port);
        options.PublishQos = MqttGatewayOptions.ClampQos(options.PublishQos);
        options.HeartbeatQos = MqttGatewayOptions.ClampQos(options.HeartbeatQos);
        options.HeartbeatIntervalSeconds = MqttGatewayOptions.ClampHeartbeatIntervalSeconds(options.HeartbeatIntervalSeconds);
        options.PublishAckTimeoutMilliseconds = MqttGatewayOptions.ClampAckTimeoutMilliseconds(options.PublishAckTimeoutMilliseconds);
        options.OutboxMaxMessages = MqttGatewayOptions.ClampOutboxMaxMessages(options.OutboxMaxMessages);
        options.OutboxMaxMegabytes = MqttGatewayOptions.ClampOutboxMaxMegabytes(options.OutboxMaxMegabytes);
        options.OutboxRetentionHours = MqttGatewayOptions.ClampOutboxRetentionHours(options.OutboxRetentionHours);
        options.PublishFlushBatchSize = MqttGatewayOptions.ClampPublishFlushBatchSize(options.PublishFlushBatchSize);
        options.PublishRetryMinSeconds = MqttGatewayOptions.ClampRetrySeconds(options.PublishRetryMinSeconds);
        options.PublishRetryMaxSeconds = MqttGatewayOptions.ClampRetrySeconds(options.PublishRetryMaxSeconds);
        options.ReconnectSeconds = MqttGatewayOptions.ClampReconnectSeconds(options.ReconnectSeconds);
        options.KeepAliveSeconds = MqttGatewayOptions.ClampKeepAliveSeconds(options.KeepAliveSeconds);
        options.ConfigVersion = MqttGatewayOptions.ClampConfigVersion(options.ConfigVersion);
        options.PublishMode = MqttGatewayOptions.NormalizePublishMode(options.PublishMode);
        options.SparkplugNamespace = MqttGatewayOptions.NormalizeText(options.SparkplugNamespace, "spBv1.0");
        options.SparkplugGroupId = MqttGatewayOptions.NormalizeText(options.SparkplugGroupId, string.IsNullOrWhiteSpace(options.GatewayId) ? "IPC-Gateway" : options.GatewayId);
        options.SparkplugEdgeNodeId = MqttGatewayOptions.NormalizeText(options.SparkplugEdgeNodeId, string.IsNullOrWhiteSpace(options.ClientId) ? "EdgeNode" : options.ClientId);
        options.SparkplugDeviceIdSource = MqttGatewayOptions.NormalizeText(options.SparkplugDeviceIdSource, "DeviceId");
        options.SparkplugMetricNameTemplate = MqttGatewayOptions.NormalizeText(options.SparkplugMetricNameTemplate, "{channel}/{group}/{tag}");
        options.SparkplugDeathQos = MqttGatewayOptions.ClampQos(options.SparkplugDeathQos);
        options.SparkplugBirthQos = MqttGatewayOptions.ClampQos(options.SparkplugBirthQos);
        options.Password = options.Password ?? string.Empty;
        options.ClientCertificatePath = options.ClientCertificatePath ?? string.Empty;
        options.ClientCertificatePassword = options.ClientCertificatePassword ?? string.Empty;
        options.ClientCertificateThumbprint = options.ClientCertificateThumbprint ?? string.Empty;
        options.ServerCertificateThumbprint = options.ServerCertificateThumbprint ?? string.Empty;
        options.CaCertificatePath = options.CaCertificatePath ?? string.Empty;
        return options;
    }

    private void ProtectProjectSecrets(ProjectConfig project)
    {
        foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
        {
            if (device?.Connection == null)
                continue;

            device.Connection.Password = _secretProtector.Protect(device.Connection.Password);
        }

        foreach (EdgeRuleConfig rule in project.Rules ?? new List<EdgeRuleConfig>())
        {
            foreach (EdgeRuleActionConfig action in rule.Actions ?? new List<EdgeRuleActionConfig>())
                action.EmailPassword = _secretProtector.Protect(action.EmailPassword);
        }

        foreach (FlowRuleDefinition flowRule in project.FlowRules ?? new List<FlowRuleDefinition>())
        {
            foreach (FlowRuleNode node in flowRule.Nodes ?? new List<FlowRuleNode>())
                node.EmailPassword = _secretProtector.Protect(node.EmailPassword);
        }
    }

    private void UnprotectProjectSecrets(ProjectConfig project)
    {
        foreach (DeviceConfig device in project.Devices ?? new List<DeviceConfig>())
        {
            if (device?.Connection == null)
                continue;

            device.Connection.Password = _secretProtector.Unprotect(device.Connection.Password);
        }

        foreach (EdgeRuleConfig rule in project.Rules ?? new List<EdgeRuleConfig>())
        {
            foreach (EdgeRuleActionConfig action in rule.Actions ?? new List<EdgeRuleActionConfig>())
                action.EmailPassword = _secretProtector.Unprotect(action.EmailPassword);
        }

        foreach (FlowRuleDefinition flowRule in project.FlowRules ?? new List<FlowRuleDefinition>())
        {
            foreach (FlowRuleNode node in flowRule.Nodes ?? new List<FlowRuleNode>())
                node.EmailPassword = _secretProtector.Unprotect(node.EmailPassword);
        }
    }

    private void ProtectMqttSecrets(MqttGatewayOptions options)
    {
        options.Password = _secretProtector.Protect(options.Password);
        options.ClientCertificatePassword = _secretProtector.Protect(options.ClientCertificatePassword);
    }

    private void UnprotectMqttSecrets(MqttGatewayOptions options)
    {
        options.Password = _secretProtector.Unprotect(options.Password);
        options.ClientCertificatePassword = _secretProtector.Unprotect(options.ClientCertificatePassword);
    }

    private static OpcUaServerOptions NormalizeOpcUa(OpcUaServerOptions options)
    {
        return OpcUaServerOptions.Normalize(options);
    }

    private static LocalHistoryOptions NormalizeHistory(LocalHistoryOptions options)
    {
        options.Directory = string.IsNullOrWhiteSpace(options.Directory) ? "Data/History" : options.Directory.Trim();
        options.RetentionDays = LocalHistoryOptions.ClampRetentionDays(options.RetentionDays);
        options.MaxViewRecords = LocalHistoryOptions.ClampMaxViewRecords(options.MaxViewRecords);
        LocalHistoryOptions normalized = options.Clone();
        options.BatchWriteEnabled = normalized.BatchWriteEnabled;
        options.BatchFlushIntervalMs = normalized.BatchFlushIntervalMs;
        options.BatchMaxLines = normalized.BatchMaxLines;
        options.BatchQueueLimit = normalized.BatchQueueLimit;
        options.DataProcessing = EdgeDataProcessingOptions.Normalize(options.DataProcessing);
        return options;
    }

    private static StorageHealthThresholds NormalizeStorageHealth(StorageHealthThresholds thresholds)
    {
        return StorageHealthEvaluator.NormalizeThresholds(thresholds);
    }

    private static string NormalizeConfigTypeFilter(string configType)
    {
        string value = string.IsNullOrWhiteSpace(configType) ? string.Empty : configType.Trim();
        if (value.Length == 0)
            return string.Empty;

        if (value.Equals(GatewayConfigurationType.Project, StringComparison.OrdinalIgnoreCase))
            return GatewayConfigurationType.Project;
        if (value.Equals(GatewayConfigurationType.Mqtt, StringComparison.OrdinalIgnoreCase))
            return GatewayConfigurationType.Mqtt;
        if (value.Equals(GatewayConfigurationType.OpcUa, StringComparison.OrdinalIgnoreCase))
            return GatewayConfigurationType.OpcUa;
        if (value.Equals(GatewayConfigurationType.History, StringComparison.OrdinalIgnoreCase))
            return GatewayConfigurationType.History;
        if (value.Equals(GatewayConfigurationType.StorageHealth, StringComparison.OrdinalIgnoreCase))
            return GatewayConfigurationType.StorageHealth;

        return value;
    }
}
