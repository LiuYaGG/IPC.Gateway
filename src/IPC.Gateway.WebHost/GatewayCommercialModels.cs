using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.WebHost;

public sealed class GatewayDeviceTemplateSummary
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int GroupCount { get; set; }
    public int TagCount { get; set; }
}

public sealed class GatewayDeviceTemplateApplyRequest
{
    public string DeviceName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public int DefaultScanRateMs { get; set; }
}

public sealed class GatewayDeviceTemplateApplyResult
{
    public DeviceConfigurationDto Device { get; set; } = new DeviceConfigurationDto();
    public int AddedGroupCount { get; set; }
    public int AddedTagCount { get; set; }
}

public sealed class GatewayTagImportResult
{
    public int TotalRows { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public IList<string> Warnings { get; set; } = new List<string>();
}

public sealed class GatewayProjectRestoreResult
{
    public string BackupId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public int TagCount { get; set; }
}
