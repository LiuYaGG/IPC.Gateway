using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IPC.Runtime.Configuration;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private readonly HashSet<string> _restoredRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _seedingRuntimeSnapshots;

        public FlowRuleEngineRuntimeState CaptureRuntimeState()
        {
            lock (_evaluationSyncRoot)
            {
                lock (_syncRoot)
                {
                    FlowRuleEngineRuntimeState result = new FlowRuleEngineRuntimeState
                    {
                        EvaluationCount = _evaluationCount,
                        TriggeredCount = _triggeredCount,
                        ClearedCount = _clearedCount,
                        FailedEvaluationCount = _failedEvaluationCount,
                        ActionFailureCount = _actionFailureCount,
                        LastEvaluationTime = _lastEvaluationTime,
                        LastEventTime = _lastEventTime,
                        LastErrorTime = _lastErrorTime,
                        LastError = _lastError
                    };

                    Dictionary<string, EdgeRuleConfig> rules = GetRules()
                        .Where(rule => rule != null)
                        .GroupBy(GetRuleId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                    foreach (KeyValuePair<string, EdgeRuleState> item in _states)
                    {
                        if (item.Value == null || !rules.TryGetValue(item.Key, out EdgeRuleConfig? rule))
                            continue;
                        result.Rules.Add(ToRuntimeState(item.Key, BuildRuleFingerprint(rule), item.Value));
                    }

                    for (int i = 0; i < _recentEvents.Count; i++)
                        result.RecentEvents.Add(CloneEvent(_recentEvents[i]));
                    foreach (EdgeRuleRuntimeRuleStatus status in _ruleStatuses.Values)
                        result.RuleStatuses.Add(CloneRuleStatus(status));
                    return result;
                }
            }
        }

        public void RestoreRuntimeState(FlowRuleEngineRuntimeState state)
        {
            if (state == null)
                return;

            lock (_evaluationSyncRoot)
            {
                lock (_syncRoot)
                {
                    Dictionary<string, EdgeRuleConfig> rules = GetRules()
                        .Where(rule => rule != null && rule.Enabled)
                        .GroupBy(GetRuleId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                    _restoredRuleIds.Clear();
                    for (int i = 0; i < state.Rules.Count; i++)
                    {
                        FlowRuleRuntimeStateEntry entry = state.Rules[i];
                        if (entry == null ||
                            !rules.TryGetValue(entry.RuleId, out EdgeRuleConfig? rule) ||
                            !string.Equals(entry.Fingerprint, BuildRuleFingerprint(rule), StringComparison.Ordinal))
                        {
                            continue;
                        }

                        _states[entry.RuleId] = FromRuntimeState(entry);
                        _restoredRuleIds.Add(entry.RuleId);
                    }

                    _recentEvents.Clear();
                    for (int i = 0; i < state.RecentEvents.Count && i < MaxRecentEvents; i++)
                        _recentEvents.Add(CloneEvent(state.RecentEvents[i]));

                    _ruleStatuses.Clear();
                    for (int i = 0; i < state.RuleStatuses.Count; i++)
                    {
                        EdgeRuleRuntimeRuleStatus status = state.RuleStatuses[i];
                        if (status == null || !rules.ContainsKey(status.RuleId))
                            continue;
                        _ruleStatuses[status.RuleId] = CloneRuleStatus(status);
                    }

                    _evaluationCount = state.EvaluationCount;
                    _triggeredCount = state.TriggeredCount;
                    _clearedCount = state.ClearedCount;
                    _failedEvaluationCount = state.FailedEvaluationCount;
                    _actionFailureCount = state.ActionFailureCount;
                    _lastEvaluationTime = state.LastEvaluationTime;
                    _lastEventTime = state.LastEventTime;
                    _lastErrorTime = state.LastErrorTime;
                    _lastError = state.LastError ?? string.Empty;
                }
            }
        }

        public void RestoreOpenAlarms(IReadOnlyList<GatewayAlarmRecord> records, bool afterRestart)
        {
            if (records.Count == 0) return;
            lock (_evaluationSyncRoot)
            {
                var byRule = records.ToLookup(r => r.RuleId, StringComparer.OrdinalIgnoreCase);
                foreach (var rule in GetRules())
                {
                    if (!rule.Enabled || IsSingleOccurrence(rule)) continue;
                    var key = RuleAlarmKey(rule);
                    var open = byRule[rule.Id].LastOrDefault(r => r.AlarmCorrelationKey == key)
                        ?? byRule[rule.Id].LastOrDefault(r => IsLegacyAlarmForSource(rule, r));
                    if (open == null) continue;
                    var state = GetState(rule);
                    state.HasRecordedOpenAlarm = true;
                    state.OpenAlarmKey = open.AlarmCorrelationKey;
                    if (string.IsNullOrEmpty(state.ActiveState))
                    {
                        state.ActiveState = open.State;
                        state.ActiveSince = open.Timestamp.ToLocalTime();
                        state.AwaitingFirstAlarmObservation = true;
                    }
                    state.AwaitingFirstAlarmObservation |= afterRestart;
                    state.RestoredAfterRestart |= afterRestart;
                }
            }
        }

        private static bool IsLegacyAlarmForSource(EdgeRuleConfig rule, GatewayAlarmRecord record)
        {
            // The old action-based records describe only their originating tag.
            // They cannot establish that all dependencies of a multi-input rule are unchanged.
            static bool Match(string expected, string value) => string.IsNullOrWhiteSpace(expected) ||
                string.Equals(expected, value, StringComparison.OrdinalIgnoreCase);
            if (rule.ConditionType is EdgeRuleConditionType.Combination or EdgeRuleConditionType.Aggregation or
                EdgeRuleConditionType.TagRelation or EdgeRuleConditionType.ContextGate or EdgeRuleConditionType.ModelInference or
                EdgeRuleConditionType.Expression) return false;
            if (rule.ConditionType == EdgeRuleConditionType.Threshold &&
                !((record.State == "High" && record.Threshold == rule.HighLimit) ||
                  (record.State == "Low" && record.Threshold == rule.LowLimit))) return false;
            string legacyKey = "rule2:" + JsonSerializer.Serialize(new[] { record.RuleId, "legacy", record.ChannelId,
                record.DeviceId, record.GroupId, record.TagId });
            return legacyKey == record.AlarmCorrelationKey && !string.IsNullOrWhiteSpace(rule.SourceTagId) &&
                Match(rule.SourceTagId, record.TagId) && Match(rule.SourceChannelId, record.ChannelId) &&
                Match(rule.SourceDeviceId, record.DeviceId) &&
                (string.IsNullOrWhiteSpace(rule.SourceDeviceId)
                    ? Match(rule.SourceGroupId, record.GroupId)
                    : string.Equals(rule.SourceGroupId, record.GroupId, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildRuleFingerprint(EdgeRuleConfig rule)
        {
            return JsonSerializer.Serialize(rule) + (rule.ValuePipeline.Count == 0 ? string.Empty : JsonSerializer.Serialize(rule.ValuePipeline));
        }

        private static FlowRuleRuntimeStateEntry ToRuntimeState(string ruleId, string fingerprint, EdgeRuleState state)
        {
            FlowRuleRuntimeStateEntry result = new FlowRuleRuntimeStateEntry
            {
                LastValuePipelineTimestamp = state.LastValuePipelineTimestamp,
                ValueWindows = CloneValueWindows(state.ValueWindows),
                RuleId = ruleId,
                AwaitingFirstAlarmObservation = state.AwaitingFirstAlarmObservation,
                RestoredAfterRestart = state.RestoredAfterRestart,
                AlarmWarmupSince = state.AlarmWarmupSince,
                NoChangeTracker = state.NoChangeTracker?.Clone(),
                NoChangeClearRequested = state.NoChangeClearRequested,
                Fingerprint = fingerprint,
                ActiveState = state.ActiveState,
                ActiveSeverity = state.ActiveSeverity,
                ActiveMessageOverride = state.ActiveMessageOverride,
                ActiveSince = state.ActiveSince,
                PendingState = state.PendingState,
                PendingSince = state.PendingSince,
                PendingClearState = state.PendingClearState,
                PendingClearSince = state.PendingClearSince,
                LastEvaluationSnapshot = state.LastEvaluationSnapshot?.Clone(),
                LastEvaluationValue = state.LastEvaluationValue,
                LastEvaluationThreshold = state.LastEvaluationThreshold,
                LastPeriodicEvaluationTime = state.LastPeriodicEvaluationTime,
                LastActiveEventTime = state.LastActiveEventTime,
                EscalationPublished = state.EscalationPublished,
                LastActiveActionDispatchTime = state.LastActiveActionDispatchTime,
                ActiveActionDispatchWindowStart = state.ActiveActionDispatchWindowStart,
                ActiveActionDispatchCount = state.ActiveActionDispatchCount,
                LastClearActionDispatchTime = state.LastClearActionDispatchTime,
                ClearActionDispatchWindowStart = state.ClearActionDispatchWindowStart,
                ClearActionDispatchCount = state.ClearActionDispatchCount,
                HasLastValue = state.HasLastValue,
                LastValue = state.LastValue,
                LastTimestamp = state.LastTimestamp,
                SequenceStepIndex = state.SequenceStepIndex,
                SequenceStartedTime = state.SequenceStartedTime,
                SequenceLastStepTime = state.SequenceLastStepTime,
                StateMachineInExpected = state.StateMachineInExpected,
                StateMachineEnteredTime = state.StateMachineEnteredTime,
                CycleStarted = state.CycleStarted,
                CycleStartedTime = state.CycleStartedTime
            };

            for (int i = 0; i < state.WindowSamples.Count; i++)
            {
                EdgeRuleWindowSample sample = state.WindowSamples[i];
                result.WindowSamples.Add(new FlowRuleWindowSampleState { Value = sample.Value, Timestamp = sample.Timestamp });
            }
            return result;
        }

        private static EdgeRuleState FromRuntimeState(FlowRuleRuntimeStateEntry source)
        {
            EdgeRuleState state = new EdgeRuleState
            {
                LastValuePipelineTimestamp = source.LastValuePipelineTimestamp,
                ValueWindows = CloneValueWindows(source.ValueWindows),
                NoChangeTracker = source.NoChangeTracker?.Clone(),
                NoChangeClearRequested = source.NoChangeClearRequested,
                ActiveState = source.ActiveState ?? string.Empty,
                AwaitingFirstAlarmObservation = source.AwaitingFirstAlarmObservation,
                RestoredAfterRestart = source.RestoredAfterRestart,
                AlarmWarmupSince = source.AlarmWarmupSince,
                ActiveSeverity = source.ActiveSeverity ?? string.Empty,
                ActiveMessageOverride = source.ActiveMessageOverride ?? string.Empty,
                ActiveSince = source.ActiveSince,
                PendingState = source.PendingState ?? string.Empty,
                PendingSince = source.PendingSince,
                PendingClearState = source.PendingClearState ?? string.Empty,
                PendingClearSince = source.PendingClearSince,
                LastEvaluationSnapshot = source.LastEvaluationSnapshot?.Clone(),
                LastEvaluationValue = source.LastEvaluationValue,
                LastEvaluationThreshold = source.LastEvaluationThreshold,
                LastPeriodicEvaluationTime = source.LastPeriodicEvaluationTime,
                LastActiveEventTime = source.LastActiveEventTime,
                EscalationPublished = source.EscalationPublished,
                LastActiveActionDispatchTime = source.LastActiveActionDispatchTime,
                ActiveActionDispatchWindowStart = source.ActiveActionDispatchWindowStart,
                ActiveActionDispatchCount = source.ActiveActionDispatchCount,
                LastClearActionDispatchTime = source.LastClearActionDispatchTime,
                ClearActionDispatchWindowStart = source.ClearActionDispatchWindowStart,
                ClearActionDispatchCount = source.ClearActionDispatchCount,
                HasLastValue = source.HasLastValue,
                LastValue = source.LastValue,
                LastTimestamp = source.LastTimestamp,
                SequenceStepIndex = source.SequenceStepIndex,
                SequenceStartedTime = source.SequenceStartedTime,
                SequenceLastStepTime = source.SequenceLastStepTime,
                StateMachineInExpected = source.StateMachineInExpected,
                StateMachineEnteredTime = source.StateMachineEnteredTime,
                CycleStarted = source.CycleStarted,
                CycleStartedTime = source.CycleStartedTime
            };

            state.WindowSamples.Clear();
            for (int i = 0; i < source.WindowSamples.Count; i++)
            {
                FlowRuleWindowSampleState sample = source.WindowSamples[i];
                if (sample != null)
                    state.WindowSamples.Add(new EdgeRuleWindowSample(sample.Value, sample.Timestamp));
            }
            return state;
        }
    }
}
