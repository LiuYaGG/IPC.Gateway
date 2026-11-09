using System;
using System.Collections.Generic;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed partial class EdgeRuleEngineService
    {
        private const int ModelEvaluationQueueCapacity = 256;
        private readonly RuleActionExecutor _modelExecutor = new RuleActionExecutor(
            "IPC Rule Model Worker",
            ModelEvaluationQueueCapacity,
            2);
        private readonly Dictionary<string, PendingModelEvaluation> _pendingModelEvaluations =
            new Dictionary<string, PendingModelEvaluation>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _scheduledModelRules =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void QueueModelEvaluation(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value)
        {
            CircuitBreaker breaker = GetRuleCircuitBreaker(rule);
            if (!breaker.CanExecute())
            {
                RecordRuleCircuitOpen(rule, breaker);
                return;
            }

            ModelInferenceRequest request;
            try
            {
                request = BuildModelInferenceRequest(rule, snapshot, value);
                RecordEvaluation(rule);
            }
            catch (Exception ex)
            {
                breaker.RecordFailure(ex.Message);
                RecordEvaluationFailure(rule, ex);
                return;
            }

            string ruleId = GetRuleId(rule);
            bool needsWorker;
            lock (_syncRoot)
            {
                _pendingModelEvaluations[ruleId] = new PendingModelEvaluation(rule, snapshot.Clone(), request);
                needsWorker = _scheduledModelRules.Add(ruleId);
            }

            if (!needsWorker)
                return;

            if (!_modelExecutor.TryEnqueue(() => ProcessModelEvaluations(ruleId), TimeSpan.Zero))
            {
                lock (_syncRoot)
                {
                    _scheduledModelRules.Remove(ruleId);
                    _pendingModelEvaluations.Remove(ruleId);
                }
                RecordModelQueueFailure(rule);
            }
        }

        private void ProcessModelEvaluations(string ruleId)
        {
            while (true)
            {
                PendingModelEvaluation? pending;
                lock (_syncRoot)
                {
                    if (!_pendingModelEvaluations.TryGetValue(ruleId, out pending) || pending == null)
                    {
                        _scheduledModelRules.Remove(ruleId);
                        return;
                    }
                    _pendingModelEvaluations.Remove(ruleId);
                }

                ModelInferenceResult result;
                try
                {
                    result = _modelInference.Predict(pending.Request);
                    if (result == null || !result.Success)
                    {
                        string message = result == null ? "No inference result returned." : result.ErrorMessage;
                        throw new InvalidOperationException("ONNX inference failed: " + message);
                    }
                }
                catch (Exception ex)
                {
                    if (!HasNewerModelEvaluation(ruleId))
                        RecordModelEvaluationFailure(pending.Rule, ex);
                    continue;
                }

                lock (_evaluationSyncRoot)
                {
                    lock (_syncRoot)
                    {
                        if (!_running)
                            return;
                        if (_pendingModelEvaluations.ContainsKey(ruleId))
                            continue;
                    }

                    EdgeRuleState state = GetState(pending.Rule);
                    double score = result.Score;
                    bool active = Compare(score, pending.Rule.ModelOperator, pending.Rule.ModelThreshold);
                    string stateName = string.Equals(pending.Rule.ModelPurpose, "QualityPrediction", StringComparison.OrdinalIgnoreCase)
                        ? "QualityPrediction"
                        : "DeviceAnomaly";
                    ApplyBooleanState(pending.Rule, pending.Snapshot, state, active, stateName, score, pending.Rule.ModelThreshold);
                    GetRuleCircuitBreaker(pending.Rule).RecordSuccess();
                }
            }
        }

        private bool HasNewerModelEvaluation(string ruleId)
        {
            lock (_syncRoot)
                return _pendingModelEvaluations.ContainsKey(ruleId);
        }

        private void RecordModelEvaluationFailure(EdgeRuleConfig rule, Exception ex)
        {
            GetRuleCircuitBreaker(rule).RecordFailure(ex.Message);
            RecordEvaluationFailure(rule, ex);
            IpcLogService.WriteError("Edge rule model evaluation failed: " + rule.Name, ex);
        }

        private void RecordModelQueueFailure(EdgeRuleConfig rule)
        {
            InvalidOperationException error = new InvalidOperationException("Rule model evaluation queue is full.");
            RecordModelEvaluationFailure(rule, error);
        }

        private void CancelModelEvaluations()
        {
            lock (_syncRoot)
            {
                _pendingModelEvaluations.Clear();
                _scheduledModelRules.Clear();
            }
            _modelExecutor.CancelPending();
        }

        private sealed class PendingModelEvaluation
        {
            public PendingModelEvaluation(EdgeRuleConfig rule, TagValueSnapshot snapshot, ModelInferenceRequest request)
            {
                Rule = rule;
                Snapshot = snapshot;
                Request = request;
            }

            public EdgeRuleConfig Rule { get; }
            public TagValueSnapshot Snapshot { get; }
            public ModelInferenceRequest Request { get; }
        }
    }
}
