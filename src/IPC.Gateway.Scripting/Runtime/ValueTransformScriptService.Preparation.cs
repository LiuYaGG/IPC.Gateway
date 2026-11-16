using System.Diagnostics;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Models;
using IPC.Runtime.Configuration;

namespace IPC.Gateway.Scripting.Runtime;

public sealed partial class ValueTransformScriptService
{
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _loaded)) return;
        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _loaded)) return;
            var document = await GatewayStartupTiming.MeasureAsync("读取值处理脚本配置",
                () => _configurationStore.LoadAsync(cancellationToken)).ConfigureAwait(false);
            // A script-center reload may have published a newer document during the read.
            PublishDefinitions(document, onlyIfUnloaded: true);
        }
        finally { _loadGate.Release(); }
    }

    /// <summary>只预编译运行中的标签和规则实际引用的版本，历史版本按需准备。</summary>
    public async Task PrepareProjectAsync(ProjectConfig project, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var elapsed = Stopwatch.StartNew();
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        var published = Volatile.Read(ref _publishedScripts);
        var references = ProjectReferences(project).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var needed = new List<GatewayScriptDefinition>();
        int missing = 0, invalid = 0;
        foreach (string reference in references)
        {
            if (published.TryGetValue(reference, out var script)) needed.Add(script);
            else missing++;
        }
        // Equal sources share validation/compilation even when different script IDs/versions refer to them.
        var unique = needed.DistinctBy(script => script.PublishedSourceCode.Trim(), StringComparer.Ordinal).ToArray();
        int before = _compiler.CompilationCount;
        long hitsBefore = _compiler.CacheHitCount;
        await Parallel.ForEachAsync(unique, new ParallelOptions
        {
            MaxDegreeOfParallelism = 2,
            CancellationToken = cancellationToken
        }, (script, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (!_compiler.PrepareValueTransform(script.PublishedSourceCode).Success)
            {
                Interlocked.Increment(ref invalid);
                GatewayStartupTiming.RecordDetail("预编译项目引用脚本", $"scriptId={script.Id} version={script.PublishedVersion} result=Invalid");
            }
            return ValueTask.CompletedTask;
        }).ConfigureAwait(false);
        GatewayStartupTiming.RecordDetail("预编译项目引用脚本", $"references={references.Length} uniqueSources={unique.Length} " +
            $"compiled={_compiler.CompilationCount - before} cacheHits={_compiler.CacheHitCount - hitsBefore} " +
            $"missing={missing} invalid={invalid} elapsedMs={elapsed.ElapsedMilliseconds}");
    }

    private static IEnumerable<string> ProjectReferences(ProjectConfig project)
    {
        var disabledChannels = (project.Channels ?? []).Where(channel => !channel.Enabled)
            .Select(channel => channel.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in project.Devices ?? [])
        {
            if (!device.Enabled || disabledChannels.Contains(device.ChannelId)) continue;
            var tags = (device.Tags ?? []).Concat((device.Groups ?? []).Where(group => group.Enabled).SelectMany(group => group.Tags ?? []));
            foreach (var tag in tags)
            {
                var cleaning = tag.Cleaning;
                if (tag.Enabled && cleaning is { Enabled: true, ValueScriptEnabled: true } &&
                    !string.IsNullOrWhiteSpace(cleaning.ValueScriptId))
                    yield return BuildCacheKey(cleaning.ValueScriptId, cleaning.ValueScriptVersion);
            }
        }
        foreach (var rule in project.Rules ?? [])
        {
            if (!rule.Enabled) continue;
            if (!string.IsNullOrWhiteSpace(rule.ValueScriptId))
                yield return BuildCacheKey(rule.ValueScriptId, rule.ValueScriptVersion);
            foreach (var condition in rule.Conditions ?? [])
                if (!string.IsNullOrWhiteSpace(condition.ValueScriptId))
                    yield return BuildCacheKey(condition.ValueScriptId, condition.ValueScriptVersion);
        }
        foreach (var flow in project.FlowRules ?? [])
        {
            if (!flow.Enabled || string.Equals(flow.LifecycleState, FlowRuleLifecycleStates.Archived, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var node in flow.Nodes ?? [])
                if (!string.IsNullOrWhiteSpace(node.ValueScriptId) &&
                    (string.Equals(node.NodeType, FlowRuleNodeTypes.ValueScript, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.NodeType, FlowRuleNodeTypes.Transform, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(node.NodeType, FlowRuleNodeTypes.Function, StringComparison.OrdinalIgnoreCase)))
                    yield return BuildCacheKey(node.ValueScriptId, node.ValueScriptVersion);
        }
    }
}
