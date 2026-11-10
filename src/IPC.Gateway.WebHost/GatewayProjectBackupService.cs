using System.Text.Json;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.WebHost;

public sealed class GatewayProjectBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IGatewayApplicationService _gateway;

    public GatewayProjectBackupService(IGatewayApplicationService gateway)
    {
        _gateway = gateway;
    }

    public ProjectBackupDocument CreateBackup()
    {
        GatewaySyncDto sync = _gateway.GetSync();
        return new ProjectBackupDocument
        {
            Project = GatewayConfigurationContractMapper.ToConfig(sync.Project),
            Mqtt = GatewayConfigurationContractMapper.ToConfig(sync.Mqtt),
            OpcUa = GatewayConfigurationContractMapper.ToConfig(sync.OpcUa),
            History = GatewayConfigurationContractMapper.ToConfig(sync.History),
            StorageHealth = GatewayConfigurationContractMapper.ToConfig(sync.StorageHealth)
        };
    }

    public byte[] CreateBackupBytes()
    {
        return ProjectProtobufCodec.SerializeBackup(CreateBackup());
    }

    public GatewayProjectRestoreResult Restore(byte[] payload)
    {
        ProjectBackupDocument backup = ProjectProtobufCodec.DeserializeBackup(payload);

        ProjectConfigurationDto project = _gateway.SaveProject(ToCommand<SaveProjectConfigurationCommand>(GatewayConfigurationContractMapper.ToDto(backup.Project)));
        _gateway.UpdateMqttOptions(ToCommand<SaveMqttConfigurationCommand>(GatewayConfigurationContractMapper.ToDto(backup.Mqtt)));
        _gateway.UpdateOpcUaOptions(ToCommand<SaveOpcUaServerConfigurationCommand>(GatewayConfigurationContractMapper.ToDto(backup.OpcUa)));
        _gateway.UpdateHistoryOptions(ToCommand<SaveHistoryConfigurationCommand>(GatewayConfigurationContractMapper.ToDto(backup.History)));
        _gateway.UpdateStorageHealthOptions(ToCommand<SaveStorageHealthConfigurationCommand>(GatewayConfigurationContractMapper.ToDto(backup.StorageHealth)));

        return new GatewayProjectRestoreResult
        {
            BackupId = backup.BackupId,
            ProjectId = project.ProjectId,
            ProjectName = project.Name,
            DeviceCount = project.Devices.Count,
            TagCount = project.Devices.Sum(device => device.Tags.Count + device.Groups.Sum(group => group.Tags.Count))
        };
    }

    private static T ToCommand<T>(object source)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(source, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
