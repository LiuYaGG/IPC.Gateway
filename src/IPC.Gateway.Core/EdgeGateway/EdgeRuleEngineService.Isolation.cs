using System;
using System.Collections.Generic;
using System.Linq;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private readonly object _evaluationSyncRoot = new object();
        private readonly Dictionary<string, CircuitBreaker> _ruleCircuitBreakers =
            new Dictionary<string, CircuitBreaker>(StringComparer.OrdinalIgnoreCase);
        private CircuitBreakerOptions _ruleCircuitBreakerOptions = new CircuitBreakerOptions();

        private void EvaluateRuleSafely(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, bool recordEvaluation)
        {
            CircuitBreaker breaker = GetRuleCircuitBreaker(rule);
            if (!breaker.CanExecute())
            {
                RecordRuleCircuitOpen(rule, breaker);
                return;
            }

            try
            {
                if (recordEvaluation)
                    RecordEvaluation(rule);
                EvaluateRule(rule, snapshot, value);
                breaker.RecordSuccess();
            }
            catch (Exception ex)
            {
                breaker.RecordFailure(ex.Message);
                RecordEvaluationFailure(rule, ex);
                IpcLogService.WriteError("Edge rule evaluation failed: " + rule.Name, ex);
            }
        }

        private CircuitBreaker GetRuleCircuitBreaker(EdgeRuleConfig rule)
        {
            string id = GetRuleId(rule);
            lock (_syncRoot)
            {
                if (!_ruleCircuitBreakers.TryGetValue(id, out CircuitBreaker? breaker))
                {
                    breaker = new CircuitBreaker("Rule:" + id, _ruleCircuitBreakerOptions);
                    _ruleCircuitBreakers[id] = breaker;
                }

                return breaker;
            }
        }

        private void RecordRuleCircuitOpen(EdgeRuleConfig rule, CircuitBreaker breaker)
        {
            CircuitBreakerStatus snapshot = breaker.Snapshot();
            lock (_syncRoot)
            {
                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(rule);
                status.LastErrorTime = DateTime.Now;
                status.LastError = "Rule circuit breaker is " + snapshot.State + "; evaluation skipped.";
                _lastErrorTime = status.LastErrorTime;
                _lastError = (rule == null ? string.Empty : rule.Name + ": ") + status.LastError;
            }
        }

        private CircuitBreakerStatus BuildRuleCircuitBreakerStatus()
        {
            List<CircuitBreakerStatus> statuses;
            lock (_syncRoot)
                statuses = _ruleCircuitBreakers.Values.Select(item => item.Snapshot()).ToList();

            if (statuses.Count == 0)
            {
                return new CircuitBreakerStatus
                {
                    Name = "RuleEngine/PerRule",
                    Enabled = _ruleCircuitBreakerOptions.Enabled,
                    State = "Closed",
                    DegradedMode = _ruleCircuitBreakerOptions.DegradedMode
                };
            }

            bool anyOpen = statuses.Any(item => item.IsOpen);
            bool anyHalfOpen = statuses.Any(item => item.IsHalfOpen);
            return new CircuitBreakerStatus
            {
                Name = "RuleEngine/PerRule",
                Enabled = _ruleCircuitBreakerOptions.Enabled,
                State = anyOpen ? "Open" : anyHalfOpen ? "HalfOpen" : "Closed",
                IsOpen = anyOpen,
                IsHalfOpen = anyHalfOpen,
                ConsecutiveFailures = statuses.Sum(item => item.ConsecutiveFailures),
                ConsecutiveSuccesses = statuses.Sum(item => item.ConsecutiveSuccesses),
                TotalFailures = statuses.Sum(item => item.TotalFailures),
                TotalSuccesses = statuses.Sum(item => item.TotalSuccesses),
                TotalTrips = statuses.Sum(item => item.TotalTrips),
                TotalRejected = statuses.Sum(item => item.TotalRejected),
                OpenedTime = MaxTime(statuses.Select(item => item.OpenedTime)),
                NextRetryTime = MinFutureTime(statuses.Select(item => item.NextRetryTime)),
                LastFailureTime = MaxTime(statuses.Select(item => item.LastFailureTime)),
                LastFailureMessage = statuses
                    .OrderByDescending(item => item.LastFailureTime)
                    .Select(item => item.LastFailureMessage)
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? string.Empty,
                DegradedMode = "SkipFailedRuleOnly"
            };
        }

        private static DateTime MaxTime(IEnumerable<DateTime> values)
        {
            DateTime result = DateTime.MinValue;
            foreach (DateTime value in values)
            {
                if (value > result)
                    result = value;
            }
            return result;
        }

        private static DateTime MinFutureTime(IEnumerable<DateTime> values)
        {
            DateTime result = DateTime.MinValue;
            foreach (DateTime value in values)
            {
                if (value == DateTime.MinValue)
                    continue;
                if (result == DateTime.MinValue || value < result)
                    result = value;
            }
            return result;
        }
    }
}
