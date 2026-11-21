using System.Diagnostics;
using System.Text.RegularExpressions;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

public sealed record FlowReplayCoverage(string TagId, string Name, string Path, int Samples, int Excluded,
    DateTimeOffset? First, DateTimeOffset? Last, bool Preheated);
public sealed record FlowReplayEvent(DateTime Timestamp, string EventType, string State, string Condition, string Message, double? Value, double? Threshold,
    IReadOnlyList<RuleReplayActionPreview> Actions, string TagName, string Quality, string OriginalValue);
public sealed record FlowReplayReport(string RuleName, int Version, bool Draft, IReadOnlyList<FlowReplayCoverage> Coverage,
    IReadOnlyList<FlowReplayEvent> Events, int Triggered, int Cleared, int Outputs, int Errors, bool Truncated,
    IReadOnlyList<string> Warnings, long ElapsedMs);

public sealed class FlowReplayException(string code) : Exception(code);

public static class FlowRuleReplay
{
    private const int MaxTags = 32;
    private const int MaxRows = 50_000;
    private const int MaxEvents = 1_000;
    private const int MaxInputCharacters = 16 * 1024 * 1024;
    private const int MaxOutputCharacters = 2 * 1024 * 1024;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private sealed record ReplayPoint(string ChannelId, string Channel, string DeviceId, string Device, string GroupId,
        string Group, TagConfig Tag)
    {
        public string Path => HistoryValuePath.Build(Channel, Device, GroupId.Length == 0 ? null : Group, Tag.Name);
        public string LegacyPath => string.Join(".", new[] { Device, Group, Tag.Name }.Where(value => value.Length != 0));
        public string PointCode => First(Tag.PointCode, Join(Channel, Device, Group.Length == 0 ? "_" : Group, Tag.Name));
    }

    public static async Task<FlowReplayReport> Run(HistoryValueIndex history, ProjectConfig project, FlowRuleDefinition definition,
        bool draft, DateTimeOffset from, DateTimeOffset to, bool english, CancellationToken cancellationToken,
        MqttGatewayOptions? mqttPreviewOptions = null)
    {
        var stopwatch = Stopwatch.StartNew();
        if (from < DateTimeOffset.UnixEpoch || to <= from || to - from > TimeSpan.FromDays(1))
            throw new FlowReplayException("INVALID_WINDOW");
        if (definition.Nodes is not { Count: > 0 and <= 128 } ||
            definition.Edges is not { Count: <= 512 } ||
            FlowRuleGraphValidator.Validate(definition).Count > 0)
            throw new FlowReplayException("INVALID_RULE");

        var saved = draft || definition.CompiledRuleId.Length == 0 ? null : project.Rules.FirstOrDefault(rule =>
            string.Equals(rule.Id, definition.CompiledRuleId, StringComparison.OrdinalIgnoreCase));
        var compiled = FlowRuleEngineService.CompileReplayRule(definition, saved) ?? throw new FlowReplayException("INVALID_RULE");
        // These extensions marshal execution to external runtimes. Never call the
        // production implementations from a historical replay.
        if (compiled.ConditionType == EdgeRuleConditionType.ModelInference ||
            !string.IsNullOrWhiteSpace(compiled.ValueScriptId) ||
            definition.Nodes.Any(node => Equal(node.NodeType, FlowRuleNodeTypes.ModelInference) || Equal(node.NodeType, FlowRuleNodeTypes.ValueScript)))
            throw new FlowReplayException("UNSUPPORTED_RULE");

        var points = ResolvePoints(project, definition, compiled, cancellationToken);
        if (points.Count == 0) throw new FlowReplayException("NO_BINDINGS");

        int requestedWarmup = RequiredWarmupSeconds(compiled);
        DateTimeOffset warmFrom = from.AddSeconds(-Math.Min(requestedWarmup, 1800));
        var coverage = new List<FlowReplayCoverage>(points.Count);
        var samples = new List<TagValueSnapshot>();
        string? snapshot = null;
        int rawRows = 0;
        long inputCharacters = 0;
        foreach (ReplayPoint point in points)
        {
            var filter = new HistoryValueFilter(string.Empty, warmFrom, to, ChannelId: point.ChannelId,
                DeviceId: point.DeviceId, GroupId: point.GroupId, TagId: point.Tag.Id);
            await using var stream = await history.OpenStreamAsync(filter, snapshot, cancellationToken,
                take: MaxRows - rawRows + 1, countTotal: false);
            snapshot = stream.Snapshot;
            int count = 0;
            int excluded = 0;
            DateTimeOffset? first = null;
            DateTimeOffset? last = null;
            bool preheated = false;
            var pointSamples = new List<TagValueSnapshot>();
            await foreach (HistoryValueRow row in stream.Items.WithCancellation(cancellationToken))
            {
                if (++rawRows > MaxRows) throw new FlowReplayException("SAMPLE_LIMIT");
                inputCharacters += (row.ValueText?.Length ?? 0) + (row.RawValueText?.Length ?? 0) + (row.ErrorMessage?.Length ?? 0);
                if (inputCharacters > MaxInputCharacters) throw new FlowReplayException("SAMPLE_LIMIT");
                if (!string.IsNullOrEmpty(row.EdgeProcessingType) && row.EdgeProcessingType != "raw")
                {
                    excluded++;
                    continue;
                }
                if (row.Timestamp < from) preheated = true;
                else count++;
                first = row.Timestamp;
                last ??= row.Timestamp;
                pointSamples.Add(ToSnapshot(point, row));
            }
            // The index breaks ties by persisted sequence; reverse each input's
            // newest-first batches before the stable chronological merge.
            pointSamples.Reverse();
            samples.AddRange(pointSamples);
            coverage.Add(new(point.Tag.Id, point.Tag.Name, point.Path, count, excluded, first, last, preheated));
        }

        var warnings = new List<string> { "EVENT_HISTORY" };
        if (coverage.Any(point => point.Excluded > 0)) warnings.Add("DERIVED_SKIPPED");
        if (coverage.Any(point => !point.Preheated)) warnings.Add("NO_BASELINE");
        if (requestedWarmup > 1800) warnings.Add("WARMUP_LIMIT");
        if (coverage.Any(point => point.Samples == 0)) warnings.Add("INPUT_WINDOW_EMPTY");
        if (compiled.ConditionType == EdgeRuleConditionType.NoChangeTimeout) warnings.Add("NO_CHANGE_LIMITED");
        if (coverage.Any(point => point.First == null))
            return new(definition.Name, definition.Version, draft, coverage, [], 0, 0, 0, 0, false, warnings, stopwatch.ElapsedMilliseconds);

        var events = new List<FlowReplayEvent>();
        int triggered = 0, cleared = 0, outputs = 0;
        long outputCharacters = 0;
        DateTime effectiveStart = from.UtcDateTime;
        // A missing initial reference is unknown until its first observed sample.
        foreach (var point in coverage.Where(point => !point.Preheated))
            if (point.First!.Value.UtcDateTime > effectiveStart)
                effectiveStart = point.First.Value.UtcDateTime;

        using var engine = EdgeRuleEngineService.CreateReplay(compiled, warmFrom.UtcDateTime, english, (row, actions) =>
        {
            if (row.Timestamp < effectiveStart || row.Timestamp >= to.UtcDateTime) return;
            switch (row.EventType)
            {
                case "active": triggered++; break;
                case "clear": cleared++; break;
                case "value": outputs++; break;
            }
            if (events.Count >= MaxEvents || outputCharacters >= MaxOutputCharacters) return;
            var compactActions = actions.Select(action => action with
            {
                Topic = action.Topic == null ? null : LimitText(action.Topic),
                Payload = action.Payload == null ? null : LimitText(action.Payload)
            }).ToArray();
            string message = LimitText(RuleMessageText.Message(row.Message, english));
            string value = LimitText(row.Snapshot.ValueText);
            outputCharacters += message.Length + value.Length + compactActions.Sum(action => (action.Topic?.Length ?? 0) + (action.Payload?.Length ?? 0));
            if (outputCharacters <= MaxOutputCharacters)
                events.Add(new(row.Timestamp, row.EventType, RuleMessageText.Label(row.State, english),
                    RuleMessageText.Label(row.ConditionType.ToString(), english), message,
                    double.IsFinite(row.Value) ? row.Value : null, double.IsFinite(row.Threshold) ? row.Threshold : null,
                    compactActions, row.Snapshot.TagName, row.Snapshot.Quality.ToString(), value));
        }, mqttPreviewOptions);
        DateTime nextTick = warmFrom.UtcDateTime;
        foreach (TagValueSnapshot sample in samples.OrderBy(sample => sample.Timestamp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (engine.ReplayNeedsTimer && nextTick < sample.Timestamp)
            {
                cancellationToken.ThrowIfCancellationRequested();
                engine.AdvanceReplay(nextTick, null, tick: true);
                nextTick += Tick;
            }
            engine.AdvanceReplay(sample.Timestamp, sample, tick: false);
        }
        while (engine.ReplayNeedsTimer && nextTick < to.UtcDateTime)
        {
            cancellationToken.ThrowIfCancellationRequested();
            engine.AdvanceReplay(nextTick, null, tick: true);
            nextTick += Tick;
        }
        return new(definition.Name, definition.Version, draft, coverage, events, triggered, cleared, outputs,
            (int)Math.Min(int.MaxValue, engine.GetStatus().FailedEvaluationCount), triggered + cleared + outputs > events.Count,
            warnings, stopwatch.ElapsedMilliseconds);
    }

    private static IReadOnlyList<ReplayPoint> ResolvePoints(ProjectConfig project, FlowRuleDefinition definition, EdgeRuleConfig rule,
        CancellationToken cancellationToken)
    {
        var configured = new List<ReplayPoint>();
        var channelNames = project.Channels.ToDictionary(channel => channel.Id, channel => channel.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var device in project.Devices)
        {
            var channel = channelNames.GetValueOrDefault(device.ChannelId, string.Empty);
            foreach (var tag in device.Tags) configured.Add(new(device.ChannelId, channel, device.Id, device.Name, string.Empty, string.Empty, tag));
            foreach (var group in device.Groups)
                foreach (var tag in group.Tags) configured.Add(new(device.ChannelId, channel, device.Id, device.Name, group.Id, group.Name, tag));
        }
        var matched = new Dictionary<string, ReplayPoint>(StringComparer.OrdinalIgnoreCase);
        string Bind(string token)
        {
            if (token.Length == 0) return string.Empty;
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = configured.Where(point => Equal(point.Tag.Id, token)).Take(2).ToArray();
            if (candidates.Length == 0)
                candidates = configured.Where(point =>
                    Equal(point.PointCode, token) || Equal(point.Path, token) || Equal(point.LegacyPath, token) ||
                    Equal(Join(point.Channel, point.Device, point.Group, point.Tag.Name), token)).Take(2).ToArray();
            if (candidates.Length != 1) throw new FlowReplayException(candidates.Length == 0 ? "NO_BINDINGS" : "AMBIGUOUS_BINDING");
            matched.TryAdd(candidates[0].Tag.Id, candidates[0]);
            if (matched.Count > MaxTags) throw new FlowReplayException("INPUT_LIMIT");
            return candidates[0].Tag.Id;
        }

        rule.SourceTagId = Bind(First(rule.SourceTagId, rule.SourcePointCode, Join(rule.SourceDeviceName, rule.SourceGroupName, rule.SourceTagName)));
        rule.RelatedTagId = Bind(First(rule.RelatedTagId, rule.RelatedPointCode, Join(rule.RelatedDeviceName, rule.RelatedGroupName, rule.RelatedTagName)));
        rule.ContextTagId = Bind(First(rule.ContextTagId, rule.ContextPointCode, Join(rule.ContextDeviceName, rule.ContextGroupName, rule.ContextTagName)));
        foreach (var condition in rule.Conditions)
            condition.SourceTagId = Bind(First(condition.SourceTagId, condition.SourcePointCode,
                Join(condition.SourceDeviceName, condition.SourceGroupName, condition.SourceTagName)));

        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in definition.Nodes)
        {
            if (Equal(node.NodeType, FlowRuleNodeTypes.TagInput))
                tokens.Add(First(node.TagId, node.PointCode, Join(node.DeviceName, node.GroupName, node.TagName)));
            foreach (Match match in Regex.Matches(node.Expression + " " + node.TransformExpression, @"\{([^{}]+)\}"))
                if (!string.Equals(match.Groups[1].Value.Trim(), "value", StringComparison.OrdinalIgnoreCase))
                    tokens.Add(match.Groups[1].Value.Trim());
        }
        foreach (string token in tokens) Bind(token);
        return matched.Values.ToArray();
    }

    private static int RequiredWarmupSeconds(EdgeRuleConfig rule) => new[]
    {
        300, rule.WindowSeconds, rule.TrendWindowSeconds, rule.AnomalyBaselineWindowSeconds,
        rule.SequenceWindowSeconds, rule.DurationSeconds, rule.ClearDurationSeconds, rule.NoChangeTimeoutSeconds,
        rule.StateTimeoutSeconds, rule.CycleMaxSeconds
    }.Max();

    private static TagValueSnapshot ToSnapshot(ReplayPoint point, HistoryValueRow row) => new()
    {
        ChannelId = point.ChannelId, ChannelName = point.Channel, DeviceId = point.DeviceId, DeviceName = point.Device,
        GroupId = point.GroupId, GroupName = point.Group, TagId = point.Tag.Id, TagName = point.Tag.Name,
        PointCode = point.Tag.PointCode, AssetPath = point.Tag.AssetPath, BusinessType = point.Tag.BusinessType,
        Value = row.ValueText ?? string.Empty, ValueText = row.ValueText ?? string.Empty, RawValue = row.RawValueText ?? string.Empty,
        RawValueText = row.RawValueText ?? string.Empty, DataType = row.DataType ?? point.Tag.DataType.ToString(), Unit = row.Unit ?? string.Empty,
        Timestamp = row.Timestamp.UtcDateTime, Quality = Enum.TryParse<TagQuality>(row.Quality, true, out var quality) ? quality : TagQuality.Unknown,
        ErrorMessage = row.ErrorMessage ?? string.Empty
    };

    private static bool Equal(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static string Join(params string[] values) => string.Join(".", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string LimitText(string value) => value.Length > 8_192 ? value[..8_192] : value;
}
