using System.Globalization;
using System.Text;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.WebHost;

public sealed class GatewayTagBulkService(IGatewayApplicationService gateway, GatewayCoreService runtime)
{
    private static readonly string[] Headers =
    [
        "channelId", "channelName", "deviceId", "deviceName", "groupId", "groupName", "tagId", "tagName",
        "address", "dataType", "scanRateMs", "enabled", "mqttPublishEnabled", "unit", "pointCode", "accessMode",
        "description", "elementCount", "elementOffset"
    ];

    public string ExportCsv(string channelId, string deviceId)
    {
        var project = gateway.GetProject();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Headers));
        var channels = project.Channels.ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var device in project.Devices.Where(device =>
                     MatchesFilter(device.ChannelId, channelId) && MatchesFilter(device.Id, deviceId)))
        {
            channels.TryGetValue(device.ChannelId, out var channel);
            AppendTags(builder, channel, device, null, device.Tags);
            foreach (var group in device.Groups)
                AppendTags(builder, channel, device, group, group.Tags);
        }
        return builder.ToString();
    }

    public GatewayTagImportResult ImportCsv(string csv, string channelId, string deviceId, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        return runtime.MutateDeviceProject(project =>
        {
            token.ThrowIfCancellationRequested();
            var report = TagPreflight.RunLegacyImport(project, channelId?.Trim() ?? "", deviceId?.Trim() ?? "", csv, token);
            // The old endpoint accepts header-only files as a no-op.
            if (report.Errors == 1 && report.Issues is [{ Code: "emptyFile" }]) return new GatewayTagImportResult();
            if (report.Errors > 0)
            {
                var errors = report.Issues.Where(issue => issue.Severity == "error").Take(12).Select(Describe);
                throw new ArgumentException(
                    "Tag import rejected (" + report.Errors.ToString(CultureInfo.InvariantCulture) + " errors). " + string.Join(" | ", errors));
            }
            token.ThrowIfCancellationRequested();
            return new GatewayTagImportResult
            {
                TotalRows = report.Rows, AddedCount = report.Added, UpdatedCount = report.Updated,
                Warnings = report.Issues.Select(Describe).ToList()
            };
        });
    }

    private static string Describe(PreflightIssue issue)
    {
        string row = issue.Row > 0 ? "Row " + issue.Row.ToString(CultureInfo.InvariantCulture) + ": " : "";
        string text = row + string.Join(" · ", new[] { issue.Path, issue.Address, issue.Code, issue.Detail }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return text.Length <= 512 ? text : text[..512] + "…";
    }

    private static void AppendTags(StringBuilder builder, ChannelConfigurationDto? channel, DeviceConfigurationDto device,
        GroupConfigurationDto? group, IEnumerable<TagConfigurationDto> tags)
    {
        foreach (var tag in tags)
        {
            string[] row =
            [
                device.ChannelId, channel?.Name ?? "", device.Id, device.Name, group?.Id ?? "", group?.Name ?? "",
                tag.Id, tag.Name, tag.Address, tag.DataType, tag.ScanRateMs.ToString(CultureInfo.InvariantCulture),
                tag.Enabled ? "true" : "false", tag.MqttPublishEnabled ? "true" : "false", tag.Unit, tag.PointCode,
                tag.AccessMode, tag.Description, tag.ElementCount.ToString(CultureInfo.InvariantCulture),
                tag.ElementOffset.ToString(CultureInfo.InvariantCulture)
            ];
            builder.AppendLine(string.Join(",", row.Select(Escape)));
        }
    }

    private static bool MatchesFilter(string value, string expected) =>
        string.IsNullOrWhiteSpace(expected) || string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string Escape(string value)
    {
        value ??= "";
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
