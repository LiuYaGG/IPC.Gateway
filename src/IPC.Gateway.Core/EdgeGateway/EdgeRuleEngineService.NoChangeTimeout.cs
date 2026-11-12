using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

public sealed partial class EdgeRuleEngineService
{
    private void EvaluateNoChangeTimeout(EdgeRuleConfig rule, TagValueSnapshot? snapshot, EdgeRuleState state, DateTime now)
    {
        if (rule.NoChangeTimeoutSeconds <= 0 || rule.NoChangeMaxAgeSeconds <= 0 || !double.IsFinite(rule.NoChangeDeadband) || rule.NoChangeDeadband < 0)
            throw new ArgumentException("连续无变化超时参数无效：超时和数据最大间隔必须大于 0，变化死区必须为非负有限数。");
        state.NoChangeTracker ??= new NoChangeTimeoutTracker();
        if (snapshot != null && HasQualityPolicy(rule) && !EvaluateQuality(rule, snapshot)) snapshot = null;
        TagValueSnapshot? observed = snapshot;
        if (snapshot?.Quality == TagQuality.Good &&
            (rule.TransformMultiplier != 1 || rule.TransformOffset != 0 || rule.TransformUseAbsolute ||
             !string.IsNullOrWhiteSpace(rule.TransformExpression) || !string.IsNullOrWhiteSpace(rule.ValueScriptId)))
        {
            if (!TryGetNumericValue(snapshot, out double numericValue))
                throw new ArgumentException("连续无变化超时的数值处理节点需要数值输入。");
            observed = snapshot.Clone();
            observed.Value = ApplyValueTransform(rule, snapshot, numericValue);
        }
        state.NoChangeTracker.Update(observed, now, rule.NoChangeDeadband, rule.NoChangeMaxAgeSeconds);
        var tracker = state.NoChangeTracker;
        if (snapshot != null) state.LastEvaluationSnapshot = snapshot.Clone();
        state.LastEvaluationValue = tracker.ElapsedSeconds;
        state.LastEvaluationThreshold = rule.NoChangeTimeoutSeconds;
        if (!tracker.Available)
        {
            // Unknown input is neither a heartbeat failure nor proof of recovery.
            state.PendingState = string.Empty;
            state.PendingSince = DateTime.MinValue;
            state.PendingClearState = string.Empty;
            state.PendingClearSince = DateTime.MinValue;
            state.NoChangeClearRequested = false;
            return;
        }
        if (tracker.Changed) state.NoChangeClearRequested = true;
        if (tracker.ElapsedSeconds >= rule.NoChangeTimeoutSeconds)
        {
            state.NoChangeClearRequested = false;
            ApplyBooleanState(rule, snapshot!, state, true, "NoChangeTimeout", tracker.ElapsedSeconds, rule.NoChangeTimeoutSeconds);
        }
        else if (state.NoChangeClearRequested || string.IsNullOrWhiteSpace(state.ActiveState))
            ApplyBooleanState(rule, snapshot!, state, false, "NoChangeTimeout", tracker.ElapsedSeconds, rule.NoChangeTimeoutSeconds);
    }
}
