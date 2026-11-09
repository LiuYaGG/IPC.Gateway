using System.Globalization;
using System.Text;
using IPC.Gateway.Core.Application.Gateway;
using IPC.Gateway.Core.Application.Gateway.Contracts;

namespace IPC.Gateway.WebHost;

public sealed class GatewayTagBulkService
{
    private static readonly string[] Headers =
    {
        "channelId", "channelName", "deviceId", "deviceName", "groupId", "groupName", "tagId", "tagName",
        "address", "dataType", "scanRateMs",
        "enabled", "mqttPublishEnabled", "unit", "pointCode", "accessMode", "description"
    };

    private readonly IGatewayApplicationService _gateway;

    public GatewayTagBulkService(IGatewayApplicationService gateway)
    {
        _gateway = gateway;
    }

    public string ExportCsv(string channelId, string deviceId)
    {
        ProjectConfigurationDto project = _gateway.GetProject();
        StringBuilder builder = new StringBuilder();
        builder.AppendLine(string.Join(",", Headers));

        Dictionary<string, ChannelConfigurationDto> channels = project.Channels
            .ToDictionary(channel => channel.Id, StringComparer.OrdinalIgnoreCase);
        foreach (DeviceConfigurationDto device in project.Devices.Where(device =>
                     MatchesFilter(device.ChannelId, channelId) && MatchesFilter(device.Id, deviceId)))
        {
            channels.TryGetValue(device.ChannelId, out ChannelConfigurationDto? channel);
            AppendTags(builder, channel, device, null, device.Tags);
            foreach (GroupConfigurationDto group in device.Groups)
                AppendTags(builder, channel, device, group, group.Tags);
        }

        return builder.ToString();
    }

    public GatewayTagImportResult ImportCsv(string csv, string channelId, string deviceId)
    {
        ProjectConfigurationDto project = _gateway.GetProject();
        GatewayTagImportResult result = new GatewayTagImportResult();
        if (string.IsNullOrWhiteSpace(csv))
            return result;

        IList<string[]> rows = ParseCsv(csv);
        if (rows.Count <= 1)
            return result;

        Dictionary<string, int> headerMap = BuildHeaderMap(rows[0]);
        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace))
                continue;

            result.TotalRows++;
            ImportRow(project, headerMap, row, channelId, deviceId, result, i + 1);
        }

        _gateway.SaveProject(ToSaveCommand(project));
        return result;
    }

    private static void AppendTags(
        StringBuilder builder,
        ChannelConfigurationDto? channel,
        DeviceConfigurationDto device,
        GroupConfigurationDto? group,
        IEnumerable<TagConfigurationDto> tags)
    {
        foreach (TagConfigurationDto tag in tags)
        {
            string[] row =
            {
                device.ChannelId,
                channel?.Name ?? string.Empty,
                device.Id,
                device.Name,
                group?.Id ?? string.Empty,
                group?.Name ?? string.Empty,
                tag.Id,
                tag.Name,
                tag.Address,
                tag.DataType,
                tag.ScanRateMs.ToString(CultureInfo.InvariantCulture),
                tag.Enabled ? "true" : "false",
                tag.MqttPublishEnabled ? "true" : "false",
                tag.Unit,
                tag.PointCode,
                tag.AccessMode,
                tag.Description
            };
            builder.AppendLine(string.Join(",", row.Select(Escape)));
        }
    }

    private static void ImportRow(
        ProjectConfigurationDto project,
        Dictionary<string, int> headerMap,
        string[] row,
        string selectedChannelId,
        string selectedDeviceId,
        GatewayTagImportResult result,
        int rowNumber)
    {
        string channelId = Read(row, headerMap, "channelId");
        string deviceId = Read(row, headerMap, "deviceId");
        DeviceConfigurationDto? device = FindDevice(project, selectedChannelId, selectedDeviceId, channelId, deviceId);
        if (device == null)
        {
            result.Warnings.Add("Row " + rowNumber + ": channel/device identity was not found.");
            return;
        }

        string tagId = Read(row, headerMap, "tagId");
        string tagName = Read(row, headerMap, "tagName");
        if (string.IsNullOrWhiteSpace(tagId) || string.IsNullOrWhiteSpace(tagName))
        {
            result.Warnings.Add("Row " + rowNumber + ": tagId and tagName are required.");
            return;
        }

        string groupId = Read(row, headerMap, "groupId");
        string groupName = Read(row, headerMap, "groupName");
        IList<TagConfigurationDto> targetTags = ResolveTargetTags(device, groupId, groupName);
        TagConfigurationDto? existing = targetTags.FirstOrDefault(tag => string.Equals(tag.Id, tagId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            targetTags.Add(CreateTag(device, groupId, row, headerMap, tagId, tagName));
            result.AddedCount++;
        }
        else
        {
            ApplyTag(existing, row, headerMap);
            result.UpdatedCount++;
        }
    }

    private static IList<TagConfigurationDto> ResolveTargetTags(DeviceConfigurationDto device, string groupId, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            return device.Tags;

        GroupConfigurationDto? group = device.Groups.FirstOrDefault(item => string.Equals(item.Id, groupId, StringComparison.OrdinalIgnoreCase));
        if (group == null)
        {
            group = new GroupConfigurationDto
            {
                Id = groupId.Trim(),
                DeviceId = device.Id,
                Name = string.IsNullOrWhiteSpace(groupName) ? "导入分组" : groupName.Trim(),
                Enabled = true,
                ScanRateMs = device.DefaultScanRateMs
            };
            device.Groups.Add(group);
        }

        return group.Tags;
    }

    private static TagConfigurationDto CreateTag(
        DeviceConfigurationDto device,
        string groupId,
        string[] row,
        Dictionary<string, int> headerMap,
        string tagId,
        string tagName)
    {
        TagConfigurationDto tag = new TagConfigurationDto
        {
            Id = tagId.Trim(),
            DeviceId = device.Id,
            GroupId = groupId.Trim(),
            Name = tagName.Trim(),
            Protocol = device.Protocol,
            DataType = "Int16",
            AccessMode = "Read",
            Enabled = true,
            ScanRateMs = device.DefaultScanRateMs
        };

        ApplyTag(tag, row, headerMap);
        return tag;
    }

    private static void ApplyTag(TagConfigurationDto tag, string[] row, Dictionary<string, int> headerMap)
    {
        tag.Name = ReadOrDefault(row, headerMap, "tagName", tag.Name);
        tag.Address = ReadOrDefault(row, headerMap, "address", tag.Address);
        tag.DataType = ReadOrDefault(row, headerMap, "dataType", tag.DataType);
        tag.Unit = ReadOrDefault(row, headerMap, "unit", tag.Unit);
        tag.PointCode = ReadOrDefault(row, headerMap, "pointCode", tag.PointCode);
        tag.AccessMode = ReadOrDefault(row, headerMap, "accessMode", tag.AccessMode);
        tag.Description = ReadOrDefault(row, headerMap, "description", tag.Description);
        tag.ScanRateMs = ReadInt(row, headerMap, "scanRateMs", tag.ScanRateMs);
        tag.Enabled = ReadBool(row, headerMap, "enabled", tag.Enabled);
        tag.MqttPublishEnabled = ReadBool(row, headerMap, "mqttPublishEnabled", tag.MqttPublishEnabled);
    }

    private static IList<string[]> ParseCsv(string csv)
    {
        List<string[]> rows = new List<string[]>();
        foreach (string line in csv.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
                rows.Add(ParseCsvLine(line).ToArray());
        }

        return rows;
    }

    private static IEnumerable<string> ParseCsvLine(string line)
    {
        StringBuilder field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
                continue;
            }

            if (c == ',' && !quoted)
            {
                yield return field.ToString();
                field.Clear();
                continue;
            }

            field.Append(c);
        }

        yield return field.ToString();
    }

    private static Dictionary<string, int> BuildHeaderMap(string[] headers)
    {
        Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Length; i++)
            map[headers[i].Trim()] = i;
        return map;
    }

    private static DeviceConfigurationDto? FindDevice(
        ProjectConfigurationDto project,
        string selectedChannelId,
        string selectedDeviceId,
        string rowChannelId,
        string rowDeviceId)
    {
        if (!string.IsNullOrWhiteSpace(selectedChannelId) && !string.IsNullOrWhiteSpace(rowChannelId) &&
            !string.Equals(selectedChannelId, rowChannelId, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.IsNullOrWhiteSpace(selectedDeviceId) && !string.IsNullOrWhiteSpace(rowDeviceId) &&
            !string.Equals(selectedDeviceId, rowDeviceId, StringComparison.OrdinalIgnoreCase))
            return null;

        string channelId = string.IsNullOrWhiteSpace(selectedChannelId) ? rowChannelId : selectedChannelId;
        string deviceId = string.IsNullOrWhiteSpace(selectedDeviceId) ? rowDeviceId : selectedDeviceId;
        if (string.IsNullOrWhiteSpace(channelId) || string.IsNullOrWhiteSpace(deviceId))
            return null;

        return project.Devices.FirstOrDefault(item =>
            string.Equals(item.ChannelId, channelId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesFilter(string value, string expected)
    {
        return string.IsNullOrWhiteSpace(expected) || string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string[] row, Dictionary<string, int> headerMap, string name)
    {
        return headerMap.TryGetValue(name, out int index) && index >= 0 && index < row.Length
            ? row[index].Trim()
            : string.Empty;
    }

    private static string ReadOrDefault(string[] row, Dictionary<string, int> headerMap, string name, string current)
    {
        string value = Read(row, headerMap, name);
        return string.IsNullOrWhiteSpace(value) ? current : value;
    }

    private static int ReadInt(string[] row, Dictionary<string, int> headerMap, string name, int current)
    {
        string value = Read(row, headerMap, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : current;
    }

    private static bool ReadBool(string[] row, Dictionary<string, int> headerMap, string name, bool current)
    {
        string value = Read(row, headerMap, name);
        if (string.IsNullOrWhiteSpace(value))
            return current;
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
            return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static SaveProjectConfigurationCommand ToSaveCommand(ProjectConfigurationDto project)
    {
        return new SaveProjectConfigurationCommand
        {
            ProjectId = project.ProjectId,
            Name = project.Name,
            Channels = project.Channels,
            Devices = project.Devices,
            Rules = project.Rules,
            FlowRules = project.FlowRules
        };
    }
}
