using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.WebHost;

public sealed class GatewayDeviceTemplateService
{
    private readonly IGatewayApplicationService _gateway;
    private readonly IReadOnlyList<DeviceTemplateDefinition> _templates;

    public GatewayDeviceTemplateService(IGatewayApplicationService gateway)
    {
        _gateway = gateway;
        _templates = CreateTemplates();
    }

    public IReadOnlyList<GatewayDeviceTemplateSummary> ListTemplates()
    {
        return _templates.Select(template => new GatewayDeviceTemplateSummary
        {
            TemplateId = template.TemplateId,
            Name = template.Name,
            Protocol = template.Protocol,
            Description = template.Description,
            GroupCount = template.Groups.Count,
            TagCount = template.Groups.Sum(group => group.Tags.Count)
        }).ToList();
    }

    public GatewayDeviceTemplateApplyResult Apply(string templateId, GatewayDeviceTemplateApplyRequest request)
    {
        DeviceTemplateDefinition template = _templates.FirstOrDefault(item => string.Equals(item.TemplateId, templateId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("Device template was not found: " + templateId);
        request ??= new GatewayDeviceTemplateApplyRequest();

        ProjectConfigurationDto project = _gateway.GetProject();
        string deviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? template.Name : request.DeviceName.Trim();
        if (project.Devices.Any(item => string.Equals(item.Name, deviceName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Device already exists: " + deviceName);

        DeviceConfigurationDto device = CreateDevice(template, request, deviceName);
        project.Devices.Add(device);
        ProjectConfigurationDto saved = _gateway.SaveProject(ToSaveCommand(project));
        DeviceConfigurationDto savedDevice = saved.Devices.FirstOrDefault(item => string.Equals(item.Name, deviceName, StringComparison.OrdinalIgnoreCase)) ?? device;

        return new GatewayDeviceTemplateApplyResult
        {
            Device = savedDevice,
            AddedGroupCount = device.Groups.Count,
            AddedTagCount = device.Groups.Sum(group => group.Tags.Count)
        };
    }

    private static DeviceConfigurationDto CreateDevice(DeviceTemplateDefinition template, GatewayDeviceTemplateApplyRequest request, string deviceName)
    {
        int scanRate = request.DefaultScanRateMs <= 0 ? 1000 : request.DefaultScanRateMs;
        DeviceConfigurationDto device = new DeviceConfigurationDto
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = deviceName,
            Protocol = template.Protocol,
            Enabled = true,
            DefaultScanRateMs = scanRate,
            FailureRetryDelayMs = 1000,
            MaxFailureRetryDelayMs = 30000,
            Connection = new PlcConnectionDto
            {
                Protocol = template.Protocol,
                Host = string.IsNullOrWhiteSpace(request.Host) ? template.DefaultHost : request.Host.Trim(),
                Port = request.Port > 0 ? request.Port : template.DefaultPort,
                TimeoutMilliseconds = 3000
            }
        };

        foreach (DeviceTemplateGroup group in template.Groups)
        {
            string groupName = string.IsNullOrWhiteSpace(request.GroupName) ? group.Name : request.GroupName.Trim();
            GroupConfigurationDto targetGroup = new GroupConfigurationDto
            {
                Id = Guid.NewGuid().ToString("N"),
                DeviceId = device.Id,
                Name = groupName,
                Enabled = true,
                ScanRateMs = scanRate
            };
            foreach (DeviceTemplateTag tag in group.Tags)
                targetGroup.Tags.Add(CreateTag(device, targetGroup, tag));
            device.Groups.Add(targetGroup);
        }

        return device;
    }

    private static TagConfigurationDto CreateTag(DeviceConfigurationDto device, GroupConfigurationDto group, DeviceTemplateTag source)
    {
        return new TagConfigurationDto
        {
            Id = Guid.NewGuid().ToString("N"),
            DeviceId = device.Id,
            GroupId = group.Id,
            Protocol = device.Protocol,
            Name = source.Name,
            Address = source.Address,
            DataType = source.DataType,
            AccessMode = source.AccessMode,
            Enabled = true,
            MqttPublishEnabled = true,
            Unit = source.Unit,
            PointCode = source.PointCode,
            Description = source.Description
        };
    }

    private static SaveProjectConfigurationCommand ToSaveCommand(ProjectConfigurationDto project)
    {
        return new SaveProjectConfigurationCommand
        {
            ProjectId = project.ProjectId,
            Name = project.Name,
            Devices = project.Devices,
            Rules = project.Rules,
            FlowRules = project.FlowRules
        };
    }

    private static IReadOnlyList<DeviceTemplateDefinition> CreateTemplates()
    {
        return new List<DeviceTemplateDefinition>
        {
            new DeviceTemplateDefinition("modbus-tcp-basic", "Modbus TCP Standard Machine", "ModbusTcp", "Common status, alarm, speed and temperature points.", "127.0.0.1", 502)
                .AddGroup("Process", new[]
                {
                    Tag("RunStatus", "00001", "Boolean", "Read", "", "RUN", "Machine running state."),
                    Tag("FaultStatus", "00002", "Boolean", "Read", "", "FAULT", "Machine fault state."),
                    Tag("Speed", "40001", "Float", "Read", "rpm", "SPEED", "Main speed feedback."),
                    Tag("Temperature", "40003", "Float", "Read", "degC", "TEMP", "Process temperature.")
                }),
            new DeviceTemplateDefinition("siemens-s7-basic", "Siemens S7 Standard Machine", "SiemensS7", "Baseline S7 tags for production equipment.", "192.168.0.10", 102)
                .AddGroup("Process", new[]
                {
                    Tag("RunStatus", "DB1.DBX0.0", "Boolean", "Read", "", "RUN", "Machine running state."),
                    Tag("FaultStatus", "DB1.DBX0.1", "Boolean", "Read", "", "FAULT", "Machine fault state."),
                    Tag("Speed", "DB1.DBD4", "Float", "Read", "rpm", "SPEED", "Main speed feedback."),
                    Tag("Count", "DB1.DBD8", "Int32", "Read", "pcs", "COUNT", "Production count.")
                }),
            new DeviceTemplateDefinition("opcua-basic", "OPC UA Standard Machine", "OpcUa", "OPC UA node layout for machine status and production count.", "opc.tcp://127.0.0.1", 4840)
                .AddGroup("Process", new[]
                {
                    Tag("RunStatus", "ns=2;s=Machine.RunStatus", "Boolean", "Read", "", "RUN", "Machine running state."),
                    Tag("FaultStatus", "ns=2;s=Machine.FaultStatus", "Boolean", "Read", "", "FAULT", "Machine fault state."),
                    Tag("Temperature", "ns=2;s=Machine.Temperature", "Float", "Read", "degC", "TEMP", "Process temperature."),
                    Tag("Count", "ns=2;s=Machine.Count", "Int32", "Read", "pcs", "COUNT", "Production count.")
                })
        };
    }

    private static DeviceTemplateTag Tag(string name, string address, string dataType, string accessMode, string unit, string pointCode, string description)
    {
        return new DeviceTemplateTag(name, address, dataType, accessMode, unit, pointCode, description);
    }
}

internal sealed record DeviceTemplateDefinition(string TemplateId, string Name, string Protocol, string Description, string DefaultHost, int DefaultPort)
{
    public IList<DeviceTemplateGroup> Groups { get; } = new List<DeviceTemplateGroup>();

    public DeviceTemplateDefinition AddGroup(string name, IEnumerable<DeviceTemplateTag> tags)
    {
        Groups.Add(new DeviceTemplateGroup(name, tags.ToList()));
        return this;
    }
}

internal sealed record DeviceTemplateGroup(string Name, IList<DeviceTemplateTag> Tags);

internal sealed record DeviceTemplateTag(string Name, string Address, string DataType, string AccessMode, string Unit, string PointCode, string Description);
