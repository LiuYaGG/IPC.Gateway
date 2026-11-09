using System;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private const int RuleActionQueueCapacity = 4096;
        private const int RuleActionWorkerCount = 4;
        private readonly RuleActionExecutor _actionExecutor = new RuleActionExecutor(
            "IPC Rule Action Worker",
            RuleActionQueueCapacity,
            RuleActionWorkerCount);

        private bool IsActionEventCurrent(EdgeRuleRuntimeEvent ruleEvent)
        {
            if (ruleEvent == null)
                return false;

            lock (_evaluationSyncRoot)
            {
                lock (_syncRoot)
                {
                    if (!_running)
                        return false;
                }

                EdgeRuleState state = GetStateById(string.IsNullOrWhiteSpace(ruleEvent.RuleId) ? ruleEvent.RuleName : ruleEvent.RuleId);
                if (string.Equals(ruleEvent.EventType, "clear", StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrWhiteSpace(state.ActiveState);

                if (ruleEvent.ConditionType == IPC.Runtime.Configuration.EdgeRuleConditionType.Deadband ||
                    ruleEvent.ConditionType == IPC.Runtime.Configuration.EdgeRuleConditionType.Sequence)
                {
                    return true;
                }

                string expectedState = ruleEvent.State ?? string.Empty;
                int separator = expectedState.IndexOf(':');
                if (separator >= 0 && separator + 1 < expectedState.Length)
                    expectedState = expectedState.Substring(separator + 1);

                return !string.IsNullOrWhiteSpace(state.ActiveState) &&
                       string.Equals(state.ActiveState, expectedState, StringComparison.OrdinalIgnoreCase);
            }
        }

        private EdgeRuleState GetStateById(string ruleId)
        {
            string id = string.IsNullOrWhiteSpace(ruleId) ? "__unknown__" : ruleId.Trim();
            lock (_syncRoot)
            {
                if (!_states.TryGetValue(id, out EdgeRuleState? state) || state == null)
                {
                    state = new EdgeRuleState();
                    _states[id] = state;
                }

                return state;
            }
        }

        private void RecordActionQueueFailure(EdgeRuleRuntimeEvent ruleEvent)
        {
            lock (_syncRoot)
            {
                _lastErrorTime = DateTime.Now;
                _lastError = (ruleEvent == null ? string.Empty : ruleEvent.RuleName + ": ") +
                             "Rule action queue is full; action dispatch was rejected.";
                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(ruleEvent);
                status.LastErrorTime = _lastErrorTime;
                status.LastError = _lastError;
                status.ActionFailureCount++;
                _actionFailureCount++;
            }
        }
    }
}
