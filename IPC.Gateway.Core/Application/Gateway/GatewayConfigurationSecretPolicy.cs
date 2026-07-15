using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.Core.Application.Gateway;

internal static class GatewayConfigurationSecretPolicy
{
    internal const string RedactedSecret = "********";

    public static GatewaySyncDto SanitizeSync(GatewaySyncDto? sync)
    {
        sync ??= new GatewaySyncDto();
        sync.Project = SanitizeProject(sync.Project);
        sync.Mqtt = SanitizeMqtt(sync.Mqtt);
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

    public static void PreserveProjectSecrets(SaveProjectConfigurationCommand? command, ProjectConfigurationDto? currentProject)
    {
        if (command == null)
            return;

        currentProject ??= new ProjectConfigurationDto();
        foreach (DeviceConfigurationDto device in command.Devices ?? Array.Empty<DeviceConfigurationDto>())
            PreserveDeviceSecrets(device, FindDevice(currentProject.Devices, device.Id, device.Name));

        foreach (EdgeRuleConfigurationDto rule in command.Rules ?? Array.Empty<EdgeRuleConfigurationDto>())
            PreserveRuleSecrets(rule, FindRule(currentProject.Rules, rule.Id, rule.Name));

        foreach (FlowRuleDefinitionDto rule in command.FlowRules ?? Array.Empty<FlowRuleDefinitionDto>())
            PreserveFlowRuleSecrets(rule, FindFlowRule(currentProject.FlowRules, rule.Id, rule.Name));
    }

    public static void PreserveDeviceSecrets(SaveDeviceConfigurationCommand? command, ProjectConfigurationDto? currentProject, string deviceId)
    {
        if (command == null)
            return;

        PreserveDeviceSecrets(command, FindDevice(currentProject?.Devices, deviceId, command.Name));
    }

    public static void PreserveRuleSecrets(SaveRuleConfigurationCommand? command, ProjectConfigurationDto? currentProject, string ruleId)
    {
        if (command == null)
            return;

        PreserveRuleSecrets(command, FindRule(currentProject?.Rules, ruleId, command.Name));
    }

    public static void PreserveFlowRuleSecrets(SaveFlowRuleDefinitionCommand? command, ProjectConfigurationDto? currentProject, string ruleId)
    {
        if (command == null)
            return;

        PreserveFlowRuleSecrets(command, FindFlowRule(currentProject?.FlowRules, ruleId, command.Name));
    }

    public static void PreserveMqttSecrets(SaveMqttConfigurationCommand? command, MqttConfigurationDto? current)
    {
        if (command == null)
            return;

        if (IsRedactedSecret(command.Password))
            command.Password = current?.Password ?? string.Empty;
        if (IsRedactedSecret(command.ClientCertificatePassword))
            command.ClientCertificatePassword = current?.ClientCertificatePassword ?? string.Empty;
    }

    public static void ClearRedactedDeviceSecrets(SaveDeviceConfigurationCommand? command)
    {
        if (command == null)
            return;

        PreserveDeviceSecrets(command, null);
    }

    public static void ClearRedactedRuleSecrets(SaveRuleConfigurationCommand? command)
    {
        if (command == null)
            return;

        PreserveRuleSecrets(command, null);
    }

    public static void ClearRedactedFlowRuleSecrets(SaveFlowRuleDefinitionCommand? command)
    {
        if (command == null)
            return;

        PreserveFlowRuleSecrets(command, null);
    }

    private static void PreserveDeviceSecrets(DeviceConfigurationDto device, DeviceConfigurationDto? current)
    {
        device.Connection ??= new PlcConnectionDto();
        PlcConnectionDto? currentConnection = current?.Connection;
        if (IsRedactedSecret(device.Connection.Password))
            device.Connection.Password = currentConnection?.Password ?? string.Empty;
    }

    private static void PreserveRuleSecrets(EdgeRuleConfigurationDto rule, EdgeRuleConfigurationDto? current)
    {
        rule.Actions ??= new List<EdgeRuleActionDto>();
        foreach (EdgeRuleActionDto action in rule.Actions)
        {
            EdgeRuleActionDto? currentAction = FindAction(current?.Actions, action);
            if (IsRedactedSecret(action.EmailPassword))
                action.EmailPassword = currentAction?.EmailPassword ?? string.Empty;
            action.WebhookUrl = WebhookSecretProtector.PreserveUrl(action.WebhookUrl, currentAction?.WebhookUrl);
            action.WebhookHeaders = WebhookSecretProtector.PreserveHeaders(action.WebhookHeaders, currentAction?.WebhookHeaders);
        }
    }

    private static void PreserveFlowRuleSecrets(FlowRuleDefinitionDto rule, FlowRuleDefinitionDto? current)
    {
        rule.Nodes ??= new List<FlowRuleNodeDto>();
        foreach (FlowRuleNodeDto node in rule.Nodes)
        {
            FlowRuleNodeDto? currentNode = FindNode(current?.Nodes, node);
            if (IsRedactedSecret(node.EmailPassword))
                node.EmailPassword = currentNode?.EmailPassword ?? string.Empty;
            node.WebhookUrl = WebhookSecretProtector.PreserveUrl(node.WebhookUrl, currentNode?.WebhookUrl);
            node.WebhookHeaders = WebhookSecretProtector.PreserveHeaders(node.WebhookHeaders, currentNode?.WebhookHeaders);
        }
    }

    private static void RedactPlcConnection(PlcConnectionDto connection)
    {
        if (!string.IsNullOrEmpty(connection.Password))
            connection.Password = RedactedSecret;
    }

    private static bool IsRedactedSecret(string? value)
    {
        return string.Equals(value?.Trim(), RedactedSecret, StringComparison.Ordinal);
    }

    private static DeviceConfigurationDto? FindDevice(IEnumerable<DeviceConfigurationDto>? devices, string id, string name)
    {
        return FindByIdOrName(devices, id, name, item => item.Id, item => item.Name);
    }

    private static EdgeRuleConfigurationDto? FindRule(IEnumerable<EdgeRuleConfigurationDto>? rules, string id, string name)
    {
        return FindByIdOrName(rules, id, name, item => item.Id, item => item.Name);
    }

    private static FlowRuleDefinitionDto? FindFlowRule(IEnumerable<FlowRuleDefinitionDto>? rules, string id, string name)
    {
        return FindByIdOrName(rules, id, name, item => item.Id, item => item.Name);
    }

    private static T? FindByIdOrName<T>(
        IEnumerable<T>? items,
        string id,
        string name,
        Func<T, string> idSelector,
        Func<T, string> nameSelector)
        where T : class
    {
        if (items == null)
            return null;

        string normalizedId = Normalize(id);
        if (!string.IsNullOrEmpty(normalizedId))
        {
            T? match = items.FirstOrDefault(item => Normalize(idSelector(item)) == normalizedId);
            if (match != null)
                return match;
        }

        string normalizedName = Normalize(name);
        return string.IsNullOrEmpty(normalizedName)
            ? null
            : items.FirstOrDefault(item => Normalize(nameSelector(item)) == normalizedName);
    }

    private static EdgeRuleActionDto? FindAction(IEnumerable<EdgeRuleActionDto>? actions, EdgeRuleActionDto action)
    {
        if (actions == null)
            return null;

        string id = Normalize(action.Id);
        if (!string.IsNullOrEmpty(id))
        {
            EdgeRuleActionDto? match = actions.FirstOrDefault(item => Normalize(item.Id) == id);
            if (match != null)
                return match;
        }

        string debugLabel = Normalize(action.DebugLabel);
        if (!string.IsNullOrEmpty(debugLabel))
        {
            EdgeRuleActionDto? match = actions.FirstOrDefault(item => Normalize(item.DebugLabel) == debugLabel);
            if (match != null)
                return match;
        }

        string topic = Normalize(action.TopicTemplate);
        string actionType = Normalize(action.ActionType);
        return actions.FirstOrDefault(item =>
            Normalize(item.TopicTemplate) == topic &&
            Normalize(item.ActionType) == actionType);
    }

    private static FlowRuleNodeDto? FindNode(IEnumerable<FlowRuleNodeDto>? nodes, FlowRuleNodeDto node)
    {
        if (nodes == null)
            return null;

        string id = Normalize(node.Id);
        if (!string.IsNullOrEmpty(id))
        {
            FlowRuleNodeDto? match = nodes.FirstOrDefault(item => Normalize(item.Id) == id);
            if (match != null)
                return match;
        }

        string label = Normalize(node.Label);
        return string.IsNullOrEmpty(label)
            ? null
            : nodes.FirstOrDefault(item => Normalize(item.Label) == label);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }
}
