/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure
* 项目描述 ：
* 类 名 称 ：GatewayCoreServiceCollectionExtensions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Infrastructure
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
using IPC;
using IPC.EdgeGateway;
using IPC.Gateway.DataProcessing;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Users;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Engine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace IPC.Gateway.Core.Infrastructure;

public static class GatewayCoreServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayCore(this IServiceCollection services, IConfiguration configuration)
    {
        GatewayAuditLogOptions auditOptions = CreateAuditOptions(configuration);
        GatewayAccountSecurityOptions accountSecurityOptions = CreateAccountSecurityOptions(configuration);
        GatewaySecretProtectionOptions secretProtectionOptions = CreateSecretProtectionOptions(configuration);
        IpcLogService.ConfigureRetentionDays(auditOptions.RetentionDays);

        services.AddSingleton(sp => new GatewayCoreService(
            CreateRuntimeOptions(configuration, secretProtectionOptions),
            CreateMqttOptions(configuration),
            CreateOpcUaOptions(configuration),
            CreateHistoryOptions(configuration),
            CreateStorageHealthThresholds(configuration),
            sp.GetService<IFlowRuleEngineFactory>(),
            sp.GetService<IModelInferenceService>()));

        services.AddSingleton<IGatewayProjectApplicationService, GatewayProjectApplicationService>();
        services.AddSingleton<IGatewayDeviceConfigurationApplicationService, GatewayDeviceConfigurationApplicationService>();
        services.AddSingleton<IGatewayRuleConfigurationApplicationService, GatewayRuleConfigurationApplicationService>();
        services.AddSingleton<IGatewayMqttConfigurationApplicationService, GatewayMqttConfigurationApplicationService>();
        services.AddSingleton<IGatewayOpcUaConfigurationApplicationService, GatewayOpcUaConfigurationApplicationService>();
        services.AddSingleton<IGatewayHistoryConfigurationApplicationService, GatewayHistoryConfigurationApplicationService>();
        services.AddSingleton<IGatewayApplicationService, GatewayApplicationService>();
        services.AddSingleton<IGatewayUserRepository>(_ => new GatewayUserStore(
            CreateDatabaseOptions(configuration),
            CreateBootstrapUserOptions(configuration),
            accountSecurityOptions));
        services.AddSingleton<IGatewayRoleRepository>(_ => new GatewayRoleStore(
            CreateDatabaseOptions(configuration)));
        services.AddSingleton<IGatewayAuditLogStore>(_ => new SqlSugarGatewayAuditLogStore(
            CreateDatabaseOptions(configuration),
                auditOptions));
        services.AddSingleton(accountSecurityOptions);
        services.AddSingleton(secretProtectionOptions);
        services.AddSingleton<IGatewayUserApplicationService, GatewayUserApplicationService>();
        services.AddSingleton<IGatewayRoleApplicationService, GatewayRoleApplicationService>();
        return services;
    }

    private static GatewayRuntimeOptions CreateRuntimeOptions(IConfiguration configuration, GatewaySecretProtectionOptions secretProtectionOptions)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Runtime");
        return new GatewayRuntimeOptions
        {
            AutoCreateDefaultProject = GetBool(section, "AutoCreateDefaultProject", true),
            Database = CreateDatabaseOptions(configuration),
            SecretProtection = secretProtectionOptions,
            Resilience = CreateResilienceOptions(configuration),
            Scheduler = new RuntimeSchedulerOptions
            {
                IsolationStrategy = section["IsolationStrategy"] ?? "SemaphoreLimitedPerDeviceQueue",
                MaxConcurrentDevicePolls = GetInt(section, "MaxConcurrentDevicePolls", Math.Max(2, Math.Min(8, Environment.ProcessorCount))),
                SchedulerIntervalMs = GetInt(section, "SchedulerIntervalMs", 100),
                DevicePollQueueLimit = GetInt(section, "DevicePollQueueLimit", 1024),
                BackpressureEnabled = GetBool(section, "BackpressureEnabled", true),
                QueueHighWatermarkPercent = GetInt(section, "QueueHighWatermarkPercent", 80),
                QueueLowWatermarkPercent = GetInt(section, "QueueLowWatermarkPercent", 50),
                BackpressureDelayMs = GetInt(section, "BackpressureDelayMs", 500),
                MaxDevicePollsQueuedPerSchedulerTick = GetInt(section, "MaxDevicePollsQueuedPerSchedulerTick", 0),
                ProtocolDriverCircuitBreaker = CreateCircuitBreakerOptions(
                    configuration.GetSection("Gateway:Resilience:ProtocolDriver"),
                    new GatewayResilienceOptions().ProtocolDriver),
                SlowPollThresholdMs = GetInt(section, "SlowPollThresholdMs", 5000),
                PollTimeoutMs = GetInt(section, "PollTimeoutMs", 10000)
            }
        };
    }

    private static GatewayResilienceOptions CreateResilienceOptions(IConfiguration configuration)
    {
        GatewayResilienceOptions defaults = new GatewayResilienceOptions();
        return new GatewayResilienceOptions
        {
            RuleEngine = CreateCircuitBreakerOptions(configuration.GetSection("Gateway:Resilience:RuleEngine"), defaults.RuleEngine),
            Mqtt = CreateCircuitBreakerOptions(configuration.GetSection("Gateway:Resilience:Mqtt"), defaults.Mqtt),
            History = CreateCircuitBreakerOptions(configuration.GetSection("Gateway:Resilience:History"), defaults.History),
            ProtocolDriver = CreateCircuitBreakerOptions(configuration.GetSection("Gateway:Resilience:ProtocolDriver"), defaults.ProtocolDriver)
        };
    }

    private static CircuitBreakerOptions CreateCircuitBreakerOptions(IConfiguration section, CircuitBreakerOptions defaults)
    {
        CircuitBreakerOptions fallback = defaults ?? new CircuitBreakerOptions();
        return new CircuitBreakerOptions
        {
            Enabled = GetBool(section, "Enabled", fallback.Enabled),
            FailureThreshold = GetInt(section, "FailureThreshold", fallback.FailureThreshold),
            SuccessThreshold = GetInt(section, "SuccessThreshold", fallback.SuccessThreshold),
            BreakDurationSeconds = GetInt(section, "BreakDurationSeconds", fallback.BreakDurationSeconds),
            DegradedMode = section["DegradedMode"] ?? fallback.DegradedMode
        }.Normalize();
    }

    private static GatewayDatabaseOptions CreateDatabaseOptions(IConfiguration configuration)
    {
        IConfigurationSection database = configuration.GetSection("Gateway:Database");
        return new GatewayDatabaseOptions
        {
            Provider = database["Provider"] ?? "PostgreSQL",
            ConnectionString = database["ConnectionString"] ?? string.Empty,
            Host = database["Host"] ?? "localhost",
            Port = GetInt(database, "Port", 5432),
            Database = database["Database"] ?? "ipc_gateway",
            Username = database["Username"] ?? "postgres",
            Password = database["Password"] ?? string.Empty,
            AutoCreateDatabase = GetBool(database, "AutoCreateDatabase", true)
        };
    }

    private static MqttGatewayOptions CreateMqttOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Mqtt");
        return new MqttGatewayOptions
        {
            Enabled = GetBool(section, "Enabled", false),
            GatewayId = section["GatewayId"] ?? "IPC-Gateway-Web",
            GatewayName = section["GatewayName"] ?? "IPC Gateway Web",
            SiteName = section["SiteName"] ?? string.Empty,
            Host = section["Host"] ?? "localhost",
            Port = GetInt(section, "Port", 1883),
            ClientId = section["ClientId"] ?? "IPC-Gateway-Web",
            Username = section["Username"] ?? string.Empty,
            Password = section["Password"] ?? string.Empty,
            UseTls = GetBool(section, "UseTls", false),
            AllowUntrustedCertificates = GetBool(section, "AllowUntrustedCertificates", false),
            ClientCertificatePath = section["ClientCertificatePath"] ?? string.Empty,
            ClientCertificatePassword = section["ClientCertificatePassword"] ?? string.Empty,
            ClientCertificateThumbprint = section["ClientCertificateThumbprint"] ?? string.Empty,
            ServerCertificateThumbprint = section["ServerCertificateThumbprint"] ?? string.Empty,
            CaCertificatePath = section["CaCertificatePath"] ?? string.Empty,
            SubscribeTopic = section["SubscribeTopic"] ?? "gateway/IPC-Gateway-Web/config",
            PublishEnabled = GetBool(section, "PublishEnabled", true),
            PublishTopicTemplate = section["PublishTopicTemplate"] ?? "ipc/data/{device}/{group}/{tag}",
            PublishQos = GetInt(section, "PublishQos", 0),
            HeartbeatEnabled = GetBool(section, "HeartbeatEnabled", true),
            HeartbeatIntervalSeconds = GetInt(section, "HeartbeatIntervalSeconds", 60),
            StatusTopic = section["StatusTopic"] ?? "gateway/{gatewayId}/status",
            HeartbeatTopic = section["HeartbeatTopic"] ?? "gateway/{gatewayId}/heartbeat",
            OutboxDirectory = section["OutboxDirectory"] ?? "Data/MqttOutbox",
            OutboxMaxMessages = GetInt(section, "OutboxMaxMessages", 10000),
            OutboxMaxMegabytes = GetInt(section, "OutboxMaxMegabytes", 100),
            OutboxRetentionHours = GetInt(section, "OutboxRetentionHours", 168),
            OutboxQuarantineRetentionHours = GetInt(section, "OutboxQuarantineRetentionHours", 720)
        };
    }

    private static OpcUaServerOptions CreateOpcUaOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:OpcUa");
        return OpcUaServerOptions.Normalize(new OpcUaServerOptions
        {
            Enabled = GetBool(section, "Enabled", false),
            ApplicationName = section["ApplicationName"] ?? "IPC Gateway OPC UA Server",
            ApplicationUri = section["ApplicationUri"] ?? "urn:ipc-gateway:opcua",
            ProductUri = section["ProductUri"] ?? "urn:ipc-gateway",
            Host = section["Host"] ?? "0.0.0.0",
            Port = GetInt(section, "Port", 4840),
            EndpointPath = section["EndpointPath"] ?? "IPC.Gateway",
            NamespaceUri = section["NamespaceUri"] ?? "urn:ipc-gateway:tags",
            CertificateStorePath = section["CertificateStorePath"] ?? "Data/OpcUa/pki",
            AutoAcceptUntrustedCertificates = GetBool(section, "AutoAcceptUntrustedCertificates", true),
            MinimumSamplingIntervalMs = GetInt(section, "MinimumSamplingIntervalMs", 250),
            PublishDiagnostics = GetBool(section, "PublishDiagnostics", true)
        });
    }

    private static LocalHistoryOptions CreateHistoryOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:History");
        return new LocalHistoryOptions
        {
            Enabled = GetBool(section, "Enabled", true),
            Directory = section["Directory"] ?? "Data/History",
            RetentionDays = GetInt(section, "RetentionDays", 7),
            MaxViewRecords = GetInt(section, "MaxViewRecords", 500),
            DataProcessing = CreateDataProcessingOptions(section.GetSection("DataProcessing"))
        };
    }

    private static EdgeDataProcessingOptions CreateDataProcessingOptions(IConfiguration section)
    {
        return EdgeDataProcessingOptions.Normalize(new EdgeDataProcessingOptions
        {
            Enabled = GetBool(section, "Enabled", false),
            CompressionEnabled = GetBool(section, "CompressionEnabled", false),
            CompressionTolerance = GetDouble(section, "CompressionTolerance", 0D),
            CompressDuplicateText = GetBool(section, "CompressDuplicateText", true),
            DownsamplingEnabled = GetBool(section, "DownsamplingEnabled", false),
            DownsamplingIntervalMs = GetInt(section, "DownsamplingIntervalMs", 0),
            AlignmentEnabled = GetBool(section, "AlignmentEnabled", false),
            AlignmentIntervalMs = GetInt(section, "AlignmentIntervalMs", 0),
            FillEnabled = GetBool(section, "FillEnabled", false),
            FillIntervalMs = GetInt(section, "FillIntervalMs", 0),
            FillMaxGapSeconds = GetInt(section, "FillMaxGapSeconds", 0),
            FillMode = section["FillMode"] ?? "Previous",
            AggregationEnabled = GetBool(section, "AggregationEnabled", false),
            AggregationIntervalSeconds = GetInt(section, "AggregationIntervalSeconds", 0),
            AggregationMethods = section["AggregationMethods"] ?? "Average,Min,Max,Count",
            MaxSyntheticPointsPerInput = GetInt(section, "MaxSyntheticPointsPerInput", 1000)
        });
    }

    private static GatewayAuditLogOptions CreateAuditOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Audit");
        GatewayAuditLogOptions defaults = new GatewayAuditLogOptions();
        return new GatewayAuditLogOptions
        {
            RetentionDays = GatewayAuditLogOptions.ClampRetentionDays(GetInt(section, "RetentionDays", defaults.RetentionDays))
        };
    }

    private static StorageHealthThresholds CreateStorageHealthThresholds(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:StorageHealth");
        StorageHealthThresholds defaults = new StorageHealthThresholds();
        return new StorageHealthThresholds
        {
            DegradedAvailableBytes = GetLong(
                section,
                "DegradedAvailableBytes",
                GetLong(section, "DegradedAvailableMegabytes", defaults.DegradedAvailableBytes / 1024L / 1024L) * 1024L * 1024L),
            UnhealthyAvailableBytes = GetLong(
                section,
                "UnhealthyAvailableBytes",
                GetLong(section, "UnhealthyAvailableMegabytes", defaults.UnhealthyAvailableBytes / 1024L / 1024L) * 1024L * 1024L),
            DegradedAvailablePercent = GetDouble(section, "DegradedAvailablePercent", defaults.DegradedAvailablePercent),
            UnhealthyAvailablePercent = GetDouble(section, "UnhealthyAvailablePercent", defaults.UnhealthyAvailablePercent)
        };
    }

    private static GatewayBootstrapUserOptions CreateBootstrapUserOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Auth");
        return new GatewayBootstrapUserOptions
        {
            AutoCreateAdmin = GetBool(section, "AutoCreateAdmin", true),
            AdminUsername = section["BootstrapAdminUsername"] ?? "admin",
            AdminDisplayName = section["BootstrapAdminDisplayName"] ?? "System Administrator",
            AdminPassword = section["BootstrapAdminPassword"] ?? string.Empty
        };
    }

    private static GatewayAccountSecurityOptions CreateAccountSecurityOptions(IConfiguration configuration)
    {
        IConfigurationSection password = configuration.GetSection("Gateway:Security:PasswordPolicy");
        IConfigurationSection lockout = configuration.GetSection("Gateway:Security:AccountLockout");
        return new GatewayAccountSecurityOptions
        {
            Password = new GatewayPasswordPolicyOptions
            {
                Enabled = GetBool(password, "Enabled", true),
                MinLength = GetInt(password, "MinLength", 8),
                MaxLength = GetInt(password, "MaxLength", 128),
                RequireUppercase = GetBool(password, "RequireUppercase", false),
                RequireLowercase = GetBool(password, "RequireLowercase", true),
                RequireDigit = GetBool(password, "RequireDigit", true),
                RequireSymbol = GetBool(password, "RequireSymbol", true),
                RejectUsernameInPassword = GetBool(password, "RejectUsernameInPassword", true)
            },
            Lockout = new GatewayAccountLockoutOptions
            {
                Enabled = GetBool(lockout, "Enabled", true),
                MaxFailedAttempts = GetInt(lockout, "MaxFailedAttempts", 5),
                LockoutMinutes = GetInt(lockout, "LockoutMinutes", 15),
                ResetFailedCountOnSuccess = GetBool(lockout, "ResetFailedCountOnSuccess", true)
            }
        };
    }

    private static GatewaySecretProtectionOptions CreateSecretProtectionOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("Gateway:Security:SecretStorage");
        return new GatewaySecretProtectionOptions
        {
            Enabled = GetBool(section, "Enabled", true),
            MasterKey = section["MasterKey"] ?? string.Empty,
            EnvironmentVariableName = section["EnvironmentVariableName"] ?? "IPC_GATEWAY_SECRET_KEY"
        };
    }

    private static bool GetBool(IConfiguration configuration, string key, bool defaultValue)
    {
        string? value = configuration[key];
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue)
    {
        string? value = configuration[key];
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
    }

    private static long GetLong(IConfiguration configuration, string key, long defaultValue)
    {
        string? value = configuration[key];
        return long.TryParse(value, out long parsed) ? parsed : defaultValue;
    }

    private static double GetDouble(IConfiguration configuration, string key, double defaultValue)
    {
        string? value = configuration[key];
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : defaultValue;
    }
}
