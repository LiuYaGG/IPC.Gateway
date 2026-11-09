using System;
using System.Collections.Generic;
using System.Threading;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private const int RuleTimerPeriodMilliseconds = 250;
        private const int TimeSeriesSampleIntervalMilliseconds = 1000;
        private Timer? _ruleTimer;
        private int _ruleTimerRunning;

        private static bool NeedsRuleTimer(EdgeRuleConfig rule)
        {
            if (rule == null)
                return false;

            if (rule.DurationSeconds > 0 ||
                rule.ClearDurationSeconds > 0 ||
                rule.AlarmEscalateAfterSeconds > 0 ||
                rule.AlarmReTriggerSeconds > 0)
            {
                return true;
            }

            return rule.ConditionType == EdgeRuleConditionType.SlidingWindow ||
                   rule.ConditionType == EdgeRuleConditionType.WindowCalculation ||
                   rule.ConditionType == EdgeRuleConditionType.Trend ||
                   rule.ConditionType == EdgeRuleConditionType.AnomalyDetection ||
                   rule.ConditionType == EdgeRuleConditionType.StateMachine ||
                   rule.ConditionType == EdgeRuleConditionType.CycleTime ||
                   rule.ConditionType == EdgeRuleConditionType.ProcessTakt ||
                   rule.ConditionType == EdgeRuleConditionType.Sequence;
        }

        private void StartRuleScheduler()
        {
            StopRuleScheduler();
            _ruleTimer = new Timer(
                _ => RunRuleTimerTick(),
                null,
                RuleTimerPeriodMilliseconds,
                RuleTimerPeriodMilliseconds);
        }

        private void StopRuleScheduler()
        {
            Timer? timer = Interlocked.Exchange(ref _ruleTimer, null);
            if (timer != null)
                timer.Dispose();
        }

        private void SeedRuntimeSnapshots()
        {
            if (_runtime == null)
                return;

            IList<TagValueSnapshot> snapshots;
            try
            {
                snapshots = _runtime.GetSnapshots() ?? new List<TagValueSnapshot>();
            }
            catch (Exception ex)
            {
                RecordEngineDegraded("Rule snapshot initialization failed: " + ex.Message);
                return;
            }

            lock (_evaluationSyncRoot)
            {
                for (int i = 0; i < snapshots.Count; i++)
                {
                    TagValueSnapshot snapshot = snapshots[i];
                    if (snapshot != null)
                        RememberSnapshot(snapshot);
                }

                _seedingRuntimeSnapshots = true;
                try
                {
                    for (int i = 0; i < snapshots.Count; i++)
                    {
                        TagValueSnapshot snapshot = snapshots[i];
                        if (snapshot != null)
                            ProcessSnapshotCore(snapshot);
                    }
                }
                finally
                {
                    _seedingRuntimeSnapshots = false;
                    _restoredRuleIds.Clear();
                }
            }
        }

        private void RunRuleTimerTick()
        {
            if (Interlocked.CompareExchange(ref _ruleTimerRunning, 1, 0) != 0)
                return;

            try
            {
                lock (_evaluationSyncRoot)
                {
                    lock (_syncRoot)
                    {
                        if (!_running)
                            return;
                    }

                    DateTime now = DateTime.Now;
                    for (int i = 0; i < _timerRules.Count; i++)
                    {
                        EdgeRuleConfig rule = _timerRules[i];
                        if (rule == null || !rule.Enabled)
                            continue;

                        var breaker = GetRuleCircuitBreaker(rule);
                        if (!breaker.CanExecute())
                        {
                            RecordRuleCircuitOpen(rule, breaker);
                            continue;
                        }

                        try
                        {
                            AdvanceRuleTimer(rule, now);
                            breaker.RecordSuccess();
                        }
                        catch (Exception ex)
                        {
                            CircuitBreakerForTimerFailure(rule, ex);
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _ruleTimerRunning, 0);
            }
        }

        private void AdvanceRuleTimer(EdgeRuleConfig rule, DateTime now)
        {
            EdgeRuleState state = GetState(rule);
            if (rule.ConditionType == EdgeRuleConditionType.Sequence)
                ResetSequenceIfExpired(rule, state, now);

            TagValueSnapshot? snapshot = state.LastEvaluationSnapshot;
            if (snapshot == null && TryGetRuleSnapshot(rule, out TagValueSnapshot cachedSnapshot))
                snapshot = cachedSnapshot;
            if (snapshot == null)
                return;

            if (IsTimeSeriesRule(rule) &&
                (state.LastPeriodicEvaluationTime == DateTime.MinValue ||
                 now - state.LastPeriodicEvaluationTime >= TimeSpan.FromMilliseconds(TimeSeriesSampleIntervalMilliseconds)))
            {
                TagValueSnapshot periodicSnapshot = snapshot.Clone();
                periodicSnapshot.Timestamp = now;
                if (periodicSnapshot.Quality == TagQuality.Good &&
                    TryGetNumericValue(periodicSnapshot, out double periodicValue))
                {
                    state.LastPeriodicEvaluationTime = now;
                    EvaluateRuleSafely(rule, periodicSnapshot, periodicValue, recordEvaluation: true);
                    snapshot = state.LastEvaluationSnapshot ?? periodicSnapshot;
                }
            }

            if (!string.IsNullOrWhiteSpace(state.PendingState) &&
                rule.DurationSeconds > 0 &&
                now - state.PendingSince >= TimeSpan.FromSeconds(rule.DurationSeconds))
            {
                ApplyBooleanState(
                    rule,
                    snapshot,
                    state,
                    true,
                    state.PendingState,
                    state.LastEvaluationValue,
                    state.LastEvaluationThreshold);
            }

            if (!string.IsNullOrWhiteSpace(state.PendingClearState) &&
                rule.ClearDurationSeconds > 0 &&
                now - state.PendingClearSince >= TimeSpan.FromSeconds(rule.ClearDurationSeconds))
            {
                ApplyBooleanState(
                    rule,
                    snapshot,
                    state,
                    false,
                    state.PendingClearState,
                    state.LastEvaluationValue,
                    state.LastEvaluationThreshold);
            }

            if (rule.ConditionType == EdgeRuleConditionType.StateMachine &&
                state.StateMachineInExpected &&
                string.IsNullOrWhiteSpace(state.ActiveState) &&
                rule.StateTimeoutSeconds > 0 &&
                now - state.StateMachineEnteredTime >= TimeSpan.FromSeconds(rule.StateTimeoutSeconds))
            {
                ApplyBooleanState(rule, snapshot, state, true, StateName(rule), state.LastEvaluationValue, rule.StateTimeoutSeconds);
            }

            if (rule.ConditionType == EdgeRuleConditionType.CycleTime && state.CycleStarted && rule.CycleMaxSeconds > 0)
            {
                double elapsed = Math.Max(0D, (now - state.CycleStartedTime).TotalSeconds);
                if (elapsed > rule.CycleMaxSeconds)
                    ApplyBooleanState(rule, snapshot, state, true, "CycleTooSlow", elapsed, rule.CycleMaxSeconds);
            }

            if (rule.ConditionType == EdgeRuleConditionType.ProcessTakt && state.CycleStarted)
            {
                ResolveProcessTaktRange(rule, out _, out _, out double maximum);
                double elapsed = Math.Max(0D, (now - state.CycleStartedTime).TotalSeconds);
                if (maximum > 0D && elapsed > maximum)
                    ApplyBooleanState(rule, snapshot, state, true, "TaktTooSlow", elapsed, maximum);
            }

            PublishTimedLifecycleEvents(rule, state, snapshot, now);
        }

        private void PublishTimedLifecycleEvents(EdgeRuleConfig rule, EdgeRuleState state, TagValueSnapshot snapshot, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(state.ActiveState))
                return;

            if (ShouldPublishEscalation(rule, state, now))
            {
                if (PublishEvent(
                    rule,
                    snapshot,
                    "active",
                    "Escalated:" + state.ActiveState,
                    state.LastEvaluationValue,
                    state.LastEvaluationThreshold,
                    BuildStateActiveMessage(rule, snapshot, state.ActiveState, state),
                    rule.PublishToMqtt))
                {
                    state.EscalationPublished = true;
                }
            }

            int retriggerSeconds = Math.Max(0, rule.AlarmReTriggerSeconds);
            if (retriggerSeconds > 0 &&
                state.LastActiveEventTime != DateTime.MinValue &&
                now - state.LastActiveEventTime >= TimeSpan.FromSeconds(retriggerSeconds))
            {
                PublishEvent(
                    rule,
                    snapshot,
                    "active",
                    "Retriggered:" + state.ActiveState,
                    state.LastEvaluationValue,
                    state.LastEvaluationThreshold,
                    BuildStateActiveMessage(rule, snapshot, state.ActiveState, state),
                    rule.PublishToMqtt);
            }
        }

        private void CircuitBreakerForTimerFailure(EdgeRuleConfig rule, Exception ex)
        {
            GetRuleCircuitBreaker(rule).RecordFailure(ex.Message);
            RecordEvaluationFailure(rule, ex);
            IpcLogService.WriteError("Edge rule timer evaluation failed: " + rule.Name, ex);
        }

        private static bool IsTimeSeriesRule(EdgeRuleConfig rule)
        {
            return rule.ConditionType == EdgeRuleConditionType.SlidingWindow ||
                   rule.ConditionType == EdgeRuleConditionType.WindowCalculation ||
                   rule.ConditionType == EdgeRuleConditionType.Trend ||
                   rule.ConditionType == EdgeRuleConditionType.AnomalyDetection;
        }
    }
}
