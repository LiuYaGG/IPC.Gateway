using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Configuration;
using IPC.Runtime.Indexing;

namespace IPC.Gateway.WebHost;

internal sealed record PreflightIssue(string Severity, string Code, string Path, string Address, string Detail, int Row, string TagId, string GroupId);
internal sealed class TagPreflightReport
{
    public string Revision { get; set; } = "";
    public int Rows { get; set; }
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Checked { get; set; }
    public int Errors { get; private set; }
    public int Warnings { get; private set; }
    public List<PreflightIssue> Issues { get; } = [];
    internal void Add(bool blocking, string code, string path, string address, string detail, int row = 0, string tagId = "", string groupId = "")
    {
        if (blocking) Errors++; else Warnings++;
        // Reserve space for each severity even on large tables with many mirrored addresses.
        if ((blocking ? Errors : Warnings) <= 250)
            Issues.Add(new(blocking ? "error" : "warning", code, path, address, detail, row, tagId, groupId));
    }
}

internal static class TagPreflight
{
    private sealed record Entry(DeviceConfig Device, GroupConfig? Group, TagConfig Tag, string Path);
    private sealed record Range(Entry Entry, TagAddressFootprint Address);
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
    internal static TagPreflightReport Run(ProjectConfig project, string channelId, string deviceId, string? groupId,
        string? csv, string revision, CancellationToken token)
    {
        var report = new TagPreflightReport { Revision = revision };
        var device = project.Devices.Find(d => Comparer.Equals(d.Id, deviceId) && Comparer.Equals(d.ChannelId, channelId))
            ?? throw new KeyNotFoundException();
        if (groupId != null && !device.Groups.Any(g => Comparer.Equals(g.Id, groupId))) throw new KeyNotFoundException();
        var staged = csv == null ? null : TagTableCsv.Stage(project, csv, channelId, deviceId, report, token, defaultGroupId: groupId);
        Inspect(project, device, groupId, staged, report, token);
        return report;
    }

    internal static TagPreflightReport RunLegacyImport(ProjectConfig project, string channelId, string deviceId, string csv,
        CancellationToken token)
    {
        var report = new TagPreflightReport();
        var staged = TagTableCsv.Stage(project, csv, channelId, deviceId, report, token, legacyAccessNames: true);
        if (staged.Count > 0) Inspect(project, null, null, staged, report, token);
        return report;
    }

    private static void Inspect(ProjectConfig project, DeviceConfig? device, string? groupId,
        IReadOnlyDictionary<TagConfig, int>? staged, TagPreflightReport report, CancellationToken token)
    {
        var channels = project.Channels.ToDictionary(c => c.Id, Comparer);
        var all = new List<Entry>();
        foreach (var d in project.Devices)
        {
            token.ThrowIfCancellationRequested();
            string prefix = (channels.GetValueOrDefault(d.ChannelId)?.Name ?? d.ChannelId) + "." + d.Name + ".";
            all.AddRange(d.Tags.Select(t => new Entry(d, null, t, prefix + t.Name)));
            foreach (var g in d.Groups)
                all.AddRange(g.Tags.Select(t => new Entry(d, g, t, prefix + g.Name + "." + t.Name)));
        }
        var ids = all.GroupBy(e => e.Tag.Id, Comparer).ToDictionary(g => g.Key, g => g.Count(), Comparer);
        var paths = all.Select(e => TagPath.BuildIdentity(e.Device.ChannelId, e.Device.Id, e.Group?.Id ?? "", e.Tag.Id)).ToHashSet(Comparer);
        var byDevice = all.ToLookup(e => e.Device);
        var affected = device == null
            ? byDevice.Where(entries => entries.Any(e => staged!.ContainsKey(e.Tag))).ToDictionary(entries => entries.Key.Id, entries => entries.Key, Comparer)
            : new Dictionary<string, DeviceConfig>(Comparer) { [device.Id] = device };
        foreach (var target in affected.Values)
            InspectDevice(target, byDevice[target], groupId, staged, ids, paths, report, token);

        void CheckReference(string owner, string channel, string dev, string group, string tag)
        {
            if (!string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(dev) && affected.TryGetValue(dev, out var target) && (groupId == null || Comparer.Equals(group, groupId)) &&
                !paths.Contains(TagPath.BuildIdentity(string.IsNullOrWhiteSpace(channel) ? target.ChannelId : channel, dev, group, tag)))
                report.Add(false, "missingRuleReference", owner, "", tag);
        }
        foreach (var rule in project.Rules)
        {
            CheckReference(rule.Name, rule.SourceChannelId, rule.SourceDeviceId, rule.SourceGroupId, rule.SourceTagId);
            foreach (var condition in rule.Conditions)
                CheckReference(rule.Name, condition.SourceChannelId, condition.SourceDeviceId, condition.SourceGroupId, condition.SourceTagId);
        }
        foreach (var rule in project.FlowRules)
            foreach (var node in rule.Nodes)
            {
                token.ThrowIfCancellationRequested();
                string owner = rule.Name + " / " + node.Label;
                CheckReference(owner, node.ChannelId, node.DeviceId, node.GroupId, node.TagId);
                CheckReference(owner, node.RelatedChannelId, node.RelatedDeviceId, node.RelatedGroupId, node.RelatedTagId);
                CheckReference(owner, node.ContextChannelId, node.ContextDeviceId, node.ContextGroupId, node.ContextTagId);
            }
    }

    private static void InspectDevice(DeviceConfig device, IEnumerable<Entry> deviceEntries, string? groupId,
        IReadOnlyDictionary<TagConfig, int>? staged, Dictionary<string, int> ids, HashSet<string> paths,
        TagPreflightReport report, CancellationToken token)
    {
        var names = deviceEntries.GroupBy(e => (e.Group, Name: e.Tag.Name.Trim().ToUpperInvariant()))
            .ToDictionary(g => g.Key, g => g.Count());
        var ranges = new List<Range>();
        var mirrors = new Dictionary<(string, int), Entry>();
        bool Chosen(Entry e) => staged?.ContainsKey(e.Tag) ?? (groupId == null || Comparer.Equals(e.Group?.Id, groupId));
        void Issue(Entry e, bool block, string code, string detail = "")
        {
            if (Chosen(e)) report.Add(block, code, e.Path, e.Tag.Address, detail, staged?.GetValueOrDefault(e.Tag) ?? 0, e.Tag.Id, e.Group?.Id ?? "");
        }
        var driver = PlcDriverPluginRegistry.GetRegisteredDrivers().FirstOrDefault(d =>
            Comparer.Equals(d.Protocol, device.Protocol.ToString()) &&
            (string.IsNullOrEmpty(device.Connection.DriverId) || Comparer.Equals(d.DriverId, device.Connection.DriverId)));
        if (driver == null) report.Add(true, "missingDriver", device.Name, "", device.Connection.DriverId);
        foreach (var e in deviceEntries.OrderBy(e => Chosen(e))) // Existing aliases remain visible to imported rows.
        {
            token.ThrowIfCancellationRequested();
            var t = e.Tag;
            if (Chosen(e))
            {
                report.Checked++;
                if (staged != null && groupId != null && !Comparer.Equals(groupId, e.Group?.Id)) Issue(e, true, "wrongGroup");
                if (ids[t.Id] > 1) Issue(e, true, "duplicateId", t.Id);
                if (names[(e.Group, t.Name.Trim().ToUpperInvariant())] > 1) Issue(e, true, "duplicateName");
                if (t.Name.Contains('/') || t.Id.Contains('/') || t.GroupId.Contains('/')) Issue(e, true, "invalidPath");
                if (t.ElementCount < 1 || t.ElementOffset < 0) Issue(e, true, "invalidField", "elementCount / elementOffset");
                var validation = ProjectConfigValidator.ValidateTag(device, e.Group, t);
                foreach (var error in validation.Errors) Issue(e, true, "validation", error);
                foreach (var warning in validation.Warnings) Issue(e, false, "validation", warning);
                if (t.IsVirtual)
                    foreach (var input in t.VirtualModel.Inputs)
                        if (!paths.Contains(input.TagPath)) Issue(e, true, "missingReference", input.TagPath);
                if (!t.IsVirtual && driver != null &&
                    ((!driver.Capabilities.SupportsWrite && t.AccessMode != TagAccessMode.ReadOnly) ||
                     (!driver.Capabilities.SupportsRead && t.AccessMode != TagAccessMode.WriteOnly)))
                    Issue(e, false, "driverAccess");
            }
            if (t.IsVirtual) continue;
            var address = ProjectConfigValidator.ResolveTagAddress(device, t);
            if (string.IsNullOrWhiteSpace(address)) continue;
            try
            {
                var range = TagAddressFootprint.Inspect(device, t);
                if (range != null)
                {
                    if (range.ReadOnly && t.AccessMode != TagAccessMode.ReadOnly) Issue(e, false, "readOnlyArea");
                    ranges.Add(new(e, range));
                }
                else if (!mirrors.TryAdd((address, t.ElementOffset), e))
                    Issue(e, false, "mirror", mirrors[(address, t.ElementOffset)].Path);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or NotSupportedException)
            {
                Issue(e, true, "validation", ex.Message);
            }
        }
        foreach (var area in ranges.GroupBy(r => r.Address.Area))
        {
            Range? longest = null, longestChosen = null;
            foreach (var r in area.OrderBy(r => r.Address.Start).ThenByDescending(r => r.Address.End))
            {
                var other = Chosen(r.Entry) ? longest : longestChosen;
                if (other != null && other.Address.End > r.Address.Start)
                {
                    var owner = Chosen(r.Entry) ? r : other;
                    bool identical = r.Address.Start == other.Address.Start && r.Address.End == other.Address.End;
                    Issue(owner.Entry, false, identical ? "mirror" : "overlap", (owner == r ? other : r).Entry.Path);
                }
                if (longest == null || r.Address.End > longest.Address.End) longest = r;
                if (Chosen(r.Entry) && (longestChosen == null || r.Address.End > longestChosen.Address.End)) longestChosen = r;
            }
        }
    }
}
