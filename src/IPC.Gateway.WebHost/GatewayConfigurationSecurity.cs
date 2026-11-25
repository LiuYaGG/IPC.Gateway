using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Application.Gateway;
using System.Security.Claims;

namespace IPC.Gateway.WebHost;

internal static class GatewayConfigurationSecurity
{
    internal const string RedactedSecret = "********";

    public static bool CanUseConfigurationSync(ClaimsPrincipal user)
    {
        return user?.Identity?.IsAuthenticated == true;
    }

    public static GatewaySyncDto SanitizeSync(GatewaySyncDto? sync, ClaimsPrincipal user)
    {
        sync ??= new GatewaySyncDto();
        sync.Status = FilterStatus(sync.Status, user);
        sync.Project = FilterProject(sync.Project, user);
        sync.Mqtt = GatewayAuthEndpoints.CanViewMqtt(user) ? SanitizeMqtt(sync.Mqtt) : new MqttConfigurationDto();
        sync.OpcUa = GatewayAuthEndpoints.CanViewOpcUa(user) ? sync.OpcUa ?? new OpcUaServerConfigurationDto() : new OpcUaServerConfigurationDto();
        sync.History = GatewayAuthEndpoints.CanViewHistory(user) ? sync.History ?? new HistoryConfigurationDto() : new HistoryConfigurationDto();
        sync.StorageHealth = CanViewStorageHealth(user) ? sync.StorageHealth ?? new StorageHealthConfigurationDto() : new StorageHealthConfigurationDto();
        return sync;
    }

    public static ProjectConfigurationDto SanitizeProject(ProjectConfigurationDto? project)
    {
        project ??= new ProjectConfigurationDto();
        project.Devices = SanitizeDevices(project.Devices);
        project.Rules = SanitizeRules(project.Rules);
        project.FlowRules = SanitizeFlowRules(project.FlowRules);
        return project;
    }

    public static IList<DeviceConfigurationDto> SanitizeDevices(IEnumerable<DeviceConfigurationDto>? devices)
    {
        return (devices ?? Array.Empty<DeviceConfigurationDto>())
            .Select(SanitizeDevice)
            .ToList();
    }

    public static DeviceConfigurationDto SanitizeDevice(DeviceConfigurationDto? device)
    {
        device ??= new DeviceConfigurationDto();
        device.Connection ??= new PlcConnectionDto();
        RedactPlcConnection(device.Connection);
        return device;
    }

    public static IList<EdgeRuleConfigurationDto> SanitizeRules(IEnumerable<EdgeRuleConfigurationDto>? rules)
    {
        return (rules ?? Array.Empty<EdgeRuleConfigurationDto>())
            .Select(SanitizeRule)
            .ToList();
    }

    public static EdgeRuleConfigurationDto SanitizeRule(EdgeRuleConfigurationDto? rule)
    {
        rule ??= new EdgeRuleConfigurationDto();
        rule.Actions ??= new List<EdgeRuleActionDto>();
        foreach (EdgeRuleActionDto action in rule.Actions)
        {
            if (!string.IsNullOrEmpty(action.EmailPassword))
                action.EmailPassword = RedactedSecret;
            action.WebhookUrl = WebhookSecretProtector.SanitizeUrl(action.WebhookUrl);
            action.WebhookHeaders = WebhookSecretProtector.SanitizeHeaders(action.WebhookHeaders);
        }

        return rule;
    }

    public static IList<FlowRuleDefinitionDto> SanitizeFlowRules(IEnumerable<FlowRuleDefinitionDto>? rules)
    {
        return (rules ?? Array.Empty<FlowRuleDefinitionDto>())
            .Select(SanitizeFlowRule)
            .ToList();
    }

    public static FlowRuleDefinitionDto SanitizeFlowRule(FlowRuleDefinitionDto? rule)
    {
        rule ??= new FlowRuleDefinitionDto();
        rule.Nodes ??= new List<FlowRuleNodeDto>();
        foreach (FlowRuleNodeDto node in rule.Nodes)
        {
            if (!string.IsNullOrEmpty(node.EmailPassword))
                node.EmailPassword = RedactedSecret;
            node.WebhookUrl = WebhookSecretProtector.SanitizeUrl(node.WebhookUrl);
            node.WebhookHeaders = WebhookSecretProtector.SanitizeHeaders(node.WebhookHeaders);
        }

        return rule;
    }

    public static MqttConfigurationDto SanitizeMqtt(MqttConfigurationDto? mqtt)
    {
        mqtt ??= new MqttConfigurationDto();
        if (!string.IsNullOrEmpty(mqtt.Password))
            mqtt.Password = RedactedSecret;
        if (!string.IsNullOrEmpty(mqtt.ClientCertificatePassword))
            mqtt.ClientCertificatePassword = RedactedSecret;
        return mqtt;
    }

    private static ProjectConfigurationDto FilterProject(ProjectConfigurationDto? project, ClaimsPrincipal user)
    {
        project ??= new ProjectConfigurationDto();
        ProjectConfigurationDto filtered = new ProjectConfigurationDto
        {
            ProjectId = project.ProjectId,
            Name = project.Name
        };

        if (GatewayAuthEndpoints.CanViewProject(user))
            return SanitizeProject(project);

        if (CanViewDeviceModel(user))
        {
            filtered.Channels = project.Channels;
            filtered.Devices = SanitizeDevices(project.Devices);
        }
        if (GatewayAuthEndpoints.CanViewFlowRules(user))
            filtered.FlowRules = SanitizeFlowRules(project.FlowRules);

        return filtered;
    }

    internal static GatewayRuntimeStatusDto FilterStatus(GatewayRuntimeStatusDto? status, ClaimsPrincipal user)
    {
        status ??= new GatewayRuntimeStatusDto();
        if (CanViewRuntimeOverview(user))
            return status;

        GatewayRuntimeStatusDto filtered = new GatewayRuntimeStatusDto
        {
            IsRunning = status.IsRunning,
            ProjectId = status.ProjectId,
            ProjectRevision = status.ProjectRevision,
            ProjectName = status.ProjectName,
            ConfigurationStore = status.ConfigurationStore,
            StartedTime = status.StartedTime,
            LastReloadTime = status.LastReloadTime,
            ConfigValidation = new ProjectValidationResultDto { IsValid = status.ConfigValidation?.IsValid ?? true }
        };

        if (GatewayAuthEndpoints.CanViewMqtt(user))
            filtered.Mqtt = status.Mqtt ?? new MqttRuntimeStatusDto();
        if (GatewayAuthEndpoints.CanViewOpcUa(user))
            filtered.OpcUa = status.OpcUa ?? new OpcUaServerRuntimeStatusDto();
        if (GatewayAuthEndpoints.CanViewFlowRules(user))
            filtered.FlowRuleEngine = status.FlowRuleEngine ?? new RuleEngineRuntimeStatusDto();
        if (GatewayAuthEndpoints.CanViewHistory(user))
            filtered.History = status.History ?? new HistoryStatsDto();

        return filtered;
    }

    internal static GatewayRuntimeEventEnvelope? FilterRuntimeEvent(GatewayRuntimeEventEnvelope envelope, ClaimsPrincipal user)
    {
        switch (envelope.Type)
        {
            case "tags":
            case "devices":
                return CanViewRuntimeOverview(user) ? envelope : null;
            case "status" when envelope.Data is GatewayRuntimeStatusPatchEvent patch:
                if (CanViewRuntimeOverview(user)) return envelope;
                return new GatewayRuntimeEventEnvelope
                {
                    Type = envelope.Type,
                    ClusterId = envelope.ClusterId,
                    Epoch = envelope.Epoch,
                    SourceInstanceId = envelope.SourceInstanceId,
                    Sequence = envelope.Sequence,
                    Timestamp = envelope.Timestamp,
                    Data = new GatewayRuntimeStatusPatchEvent { Status = FilterStatus(patch.Status, user) }
                };
            case "resync":
            case "haChanged":
                return envelope;
            default:
                return null;
        }
    }

    internal static bool CanViewRuntimeOverview(ClaimsPrincipal user)
    {
        return GatewayAuthEndpoints.CanViewDashboard(user) ||
               GatewayAuthEndpoints.CanViewBigScreen(user) ||
               GatewayAuthEndpoints.CanViewTopology(user) ||
               GatewayAuthEndpoints.CanViewDevices(user);
    }

    private static bool CanViewDeviceModel(ClaimsPrincipal user)
    {
        return GatewayAuthEndpoints.CanViewDevices(user) ||
               GatewayAuthEndpoints.CanViewDashboard(user) ||
               GatewayAuthEndpoints.CanViewBigScreen(user) ||
               GatewayAuthEndpoints.CanViewTopology(user);
    }

    private static bool CanViewStorageHealth(ClaimsPrincipal user)
    {
        return GatewayAuthEndpoints.CanViewDashboard(user) ||
               GatewayAuthEndpoints.CanViewBigScreen(user) ||
               GatewayAuthEndpoints.CanViewHistory(user);
    }

    private static void RedactPlcConnection(PlcConnectionDto connection)
    {
        if (!string.IsNullOrEmpty(connection.Password))
            connection.Password = RedactedSecret;
    }
}
