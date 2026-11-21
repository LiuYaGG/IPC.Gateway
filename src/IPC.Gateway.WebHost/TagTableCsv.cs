using System.Globalization;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using Microsoft.VisualBasic.FileIO;

namespace IPC.Gateway.WebHost;

internal static class TagTableCsv
{
    // Stage changes on the detached project used for both preview and commit.
    internal static Dictionary<TagConfig, int> Stage(ProjectConfig project, string csv, string channelId,
        string deviceId, TagPreflightReport report, CancellationToken token, bool legacyAccessNames = false, string? defaultGroupId = null)
    {
        var affected = new Dictionary<TagConfig, int>();
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF')))
        {
            TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");
        var devices = project.Devices.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var groupIds = project.Devices.SelectMany(d => d.Groups).Select(g => g.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var destinations = new Dictionary<GroupConfig, Dictionary<string, TagConfig>>();
        var direct = new Dictionary<DeviceConfig, Dictionary<string, TagConfig>>();
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var header = parser.ReadFields() ?? [];
            for (int i = 0; i < header.Length; i++)
                if (!columns.TryAdd(header[i].Trim(), i))
                    report.Add(true, "duplicateColumn", "", "", header[i], 1);
            if (!columns.ContainsKey("tagId") || !columns.ContainsKey("tagName"))
                report.Add(true, "missingColumns", "", "", "tagId, tagName", 1);
            if (report.Errors > 0) return affected;

            while (!parser.EndOfData)
            {
                token.ThrowIfCancellationRequested();
                int line = checked((int)parser.LineNumber);
                var fields = parser.ReadFields()!;
                report.Rows++;
                string Get(string key) => columns.TryGetValue(key, out int i) && i < fields.Length ? fields[i].Trim() : "";
                string id = Get("tagId"), name = Get("tagName"), address = Get("address");
                void Error(string code, string detail) => report.Add(true, code, name, address, detail, line);
                if (fields.Length != header.Length) { Error("csvColumns", ""); continue; }
                string sourceDevice = Get("deviceId"), sourceChannel = Get("channelId");
                if (!devices.TryGetValue(sourceDevice.Length == 0 ? deviceId : sourceDevice, out var device)
                    || !Matches(deviceId, device.Id) || !Matches(channelId, device.ChannelId) || !Matches(sourceChannel, device.ChannelId))
                { Error("wrongDevice", ""); continue; }
                if (id.Length == 0 || name.Length == 0) { Error("missingIdentity", ""); continue; }
                string groupId = columns.ContainsKey("groupId") ? Get("groupId") : defaultGroupId ?? "";
                var group = device.Groups.Find(g => g.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase));
                if (groupId.Length != 0 && group == null)
                {
                    string groupName = Get("groupName");
                    if (groupName.Length == 0) groupName = "导入分组";
                    if (groupId.Contains('/') || groupName.Contains('/')) { Error("invalidPath", groupName); continue; }
                    if (device.Groups.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                    { Error("duplicateGroupName", groupName); continue; }
                    if (!groupIds.Add(groupId)) { Error("duplicateGroupId", groupId); continue; }
                    group = new GroupConfig { Id = groupId, DeviceId = device.Id,
                        Name = groupName, Enabled = true, ScanRateMs = device.DefaultScanRateMs };
                    device.Groups.Add(group);
                }
                var tags = group?.Tags ?? device.Tags;
                Dictionary<string, TagConfig> byId;
                if (group == null)
                {
                    if (!direct.TryGetValue(device, out byId!)) direct[device] = byId = Index(tags);
                }
                else if (!destinations.TryGetValue(group, out byId!)) destinations[group] = byId = Index(tags);
                if (!byId.TryGetValue(id, out var tag))
                {
                    tag = new TagConfig { Id = id, DeviceId = device.Id, GroupId = groupId, AccessMode = TagAccessMode.ReadOnly };
                    tags.Add(tag);
                    byId.Add(id, tag);
                    report.Added++;
                }
                else report.Updated++;
                if (!affected.TryAdd(tag, line)) { Error("duplicateRow", id); continue; }
                tag.Name = name;
                void IfSet(string key, Action<string> set) { string value = Get(key); if (value.Length > 0) set(value); }
                void SetEnum<T>(string key, Action<T> set) where T : struct, Enum =>
                    IfSet(key, s => { if (Enum.GetNames<T>().Contains(s, StringComparer.OrdinalIgnoreCase)) set(Enum.Parse<T>(s, true)); else Error("invalidField", key + " = " + s); });
                void SetNumber(string key, int min, Action<int> set) =>
                    IfSet(key, s => { if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min) set(v); else Error("invalidField", key + " = " + s); });
                void SetBool(string key, Action<bool> set) => IfSet(key, s => {
                    switch (s.ToLowerInvariant())
                    {
                        case "1": case "true": case "yes": set(true); break;
                        case "0": case "false": case "no": set(false); break;
                        default: Error("invalidField", key + " = " + s); break;
                    }
                });
                IfSet("address", s => tag.Address = s);
                SetEnum<PlcDataType>("dataType", v => tag.DataType = v);
                string access = Get("accessMode");
                if (legacyAccessNames && access.Equals("Read", StringComparison.OrdinalIgnoreCase)) tag.AccessMode = TagAccessMode.ReadOnly;
                else if (legacyAccessNames && access.Equals("Write", StringComparison.OrdinalIgnoreCase)) tag.AccessMode = TagAccessMode.WriteOnly;
                else SetEnum<TagAccessMode>("accessMode", v => tag.AccessMode = v);
                SetNumber("elementCount", 1, v => tag.ElementCount = v);
                SetNumber("elementOffset", 0, v => tag.ElementOffset = v);
                SetNumber("scanRateMs", 0, v => tag.ScanRateMs = v);
                SetBool("enabled", v => tag.Enabled = v);
                SetBool("mqttPublishEnabled", v => tag.MqttPublishEnabled = v);
                IfSet("unit", s => tag.Unit = s);
                IfSet("pointCode", s => tag.PointCode = s);
                IfSet("description", s => tag.Description = s);
            }
            if (report.Rows == 0) report.Add(true, "emptyFile", "", "", "");
        }
        catch (MalformedLineException ex) { report.Add(true, "malformedCsv", "", "", "", (int)ex.LineNumber); }
        return affected;
    }

    private static Dictionary<string, TagConfig> Index(IEnumerable<TagConfig> tags) =>
        tags.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    private static bool Matches(string filter, string value) => filter.Length == 0 || filter.Equals(value, StringComparison.OrdinalIgnoreCase);
}
