using System.Globalization;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

public sealed partial class EdgeRuleEngineService
{
    private static bool HasWindowCalculation(EdgeRuleConfig rule) =>
        rule.ValuePipeline.Count > 0 || rule.ConditionType == EdgeRuleConditionType.WindowCalculation;

    private bool TryCalculateWindowPipeline(EdgeRuleConfig rule, EdgeRuleState state,
        ref TagValueSnapshot snapshot, ref double value)
    {
        var timestamp = snapshot.Timestamp.ToUniversalTime();
        if (snapshot.Timestamp == DateTime.MinValue || timestamp <= state.LastValuePipelineTimestamp) return false;
        state.LastValuePipelineTimestamp = timestamp;
        // Invalid samples are not statistical samples and must not publish a stale calculation.
        if (snapshot.Quality != TagQuality.Good || !double.IsFinite(value))
        {
            state.LastEvaluationSnapshot = snapshot.Clone();
            if (rule.ConditionType == EdgeRuleConditionType.NoChangeTimeout)
                EvaluateNoChangeTimeout(rule, snapshot, state, RuleTime);
            return false;
        }
        // A rejected gate or failed transform must not leave the previous good result available to timers.
        state.LastEvaluationSnapshot = snapshot.Clone();
        state.LastEvaluationSnapshot.Quality = TagQuality.Bad;
        snapshot = snapshot.Clone();
        if (rule.ValuePipeline.Count == 0)
        {
            value = ApplyValueTransform(rule, snapshot, value);
            value = CalculateWindowValue(state, "window", rule.WindowStatistic, rule.WindowSeconds, rule.WindowSampleCount, timestamp, value);
        }
        else foreach (var node in rule.ValuePipeline)
        {
            snapshot.Value = value;
            snapshot.ValueText = value.ToString("R", CultureInfo.InvariantCulture);
            if (string.Equals(node.NodeType, FlowRuleNodeTypes.QualityGate, StringComparison.OrdinalIgnoreCase))
            {
                if (!EvaluateQuality(new EdgeRuleConfig { QualityOperator = node.QualityOperator, QualityValues = node.QualityValues }, snapshot)) return false;
            }
            else if (string.Equals(node.NodeType, FlowRuleNodeTypes.WindowCalculation, StringComparison.OrdinalIgnoreCase))
                value = CalculateWindowValue(state, node.Id, node.WindowStatistic, node.WindowSeconds, node.WindowSampleCount, timestamp, value);
            else
                value = ApplyValueTransform(snapshot, value, node.TransformMultiplier, node.TransformOffset,
                    node.TransformUseAbsolute, node.TransformExpression, node.TransformTimeoutMilliseconds,
                    node.ValueScriptId, node.ValueScriptVersion);
            if (!double.IsFinite(value)) throw new InvalidOperationException("窗口计算结果必须为有限数值。");
        }
        snapshot.Value = value;
        snapshot.ValueText = value.ToString("R", CultureInfo.InvariantCulture);
        snapshot.DataType = "Double";
        state.LastEvaluationSnapshot = snapshot.Clone();
        state.LastEvaluationValue = value;
        return true;
    }

    private static double CalculateWindowValue(EdgeRuleState state, string id, string statistic,
        int seconds, int sampleCount, DateTime timestamp, double value)
    {
        if (!double.IsFinite(value)) throw new InvalidOperationException("窗口计算输入必须为有限数值。");
        if (!state.ValueWindows.TryGetValue(id, out var samples))
            state.ValueWindows[id] = samples = new List<FlowRuleWindowSampleState>();
        samples.Add(new() { Timestamp = timestamp, Value = value });
        var oldest = timestamp.AddSeconds(-Math.Max(1, seconds));
        samples.RemoveAll(sample => sample.Timestamp < oldest);
        int limit = sampleCount <= 0 ? 10000 : Math.Min(10000, sampleCount);
        if (samples.Count > limit) samples.RemoveRange(0, samples.Count - limit);
        return CalculateStatistic(statistic, samples.Select(s => s.Value).ToList());
    }

    private static Dictionary<string, List<FlowRuleWindowSampleState>> CloneValueWindows(Dictionary<string, List<FlowRuleWindowSampleState>> windows) =>
        windows.ToDictionary(pair => pair.Key, pair => pair.Value.Select(s => new FlowRuleWindowSampleState { Timestamp = s.Timestamp, Value = s.Value }).ToList());
}
