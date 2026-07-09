/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：EdgeRuleEngineService
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class EdgeRuleEngineService : IDisposable
    {
        private const int MaxRecentEvents = 200;
        private const int DefaultExpressionTimeoutMilliseconds = 50;
        private const int MaxExpressionTimeoutMilliseconds = 5000;
        private const int MaxExpressionLength = 512;
        private static readonly HttpClient WebhookHttpClient = new HttpClient();
        private static readonly Regex SafeExpressionCharacters = new Regex(@"^[0-9A-Za-z_{}\.\s+\-*/%(),<>=!&|]+$", RegexOptions.Compiled);
        private static readonly string[] UnsafeExpressionTokens =
        {
            "System",
            "Microsoft",
            "Runtime",
            "Reflection",
            "Process",
            "File",
            "Directory",
            "Environment",
            "Thread",
            "Task",
            "HttpClient",
            "WebClient",
            "Socket",
            "Sql",
            "new",
            "typeof",
            "using",
            "namespace",
            "class",
            "while",
            "for",
            "foreach",
            "function",
            "import",
            "eval"
        };
        private readonly object _syncRoot;
        private readonly IRuntimeService _runtime;
        private readonly ProjectConfig _projectConfig;
        private readonly Func<string, string, int, bool> _mqttPublisher;
        private readonly MqttGatewayOptions _gatewayOptions;
        private readonly CircuitBreaker _circuitBreaker;
        private readonly IModelInferenceService _modelInference;
        private readonly Dictionary<string, EdgeRuleState> _states;
        private readonly Dictionary<string, EdgeRuleRuntimeRuleStatus> _ruleStatuses;
        private readonly Dictionary<string, TagValueSnapshot> _snapshotsByPoint;
        private readonly Dictionary<string, TagValueSnapshot> _snapshotsByPath;
        private readonly List<EdgeRuleRuntimeEvent> _recentEvents;
        private long _evaluationCount;
        private long _triggeredCount;
        private long _clearedCount;
        private long _failedEvaluationCount;
        private DateTime _lastEvaluationTime;
        private DateTime _lastEventTime;
        private DateTime _lastErrorTime;
        private string _lastError;
        private bool _running;
        private bool _disposed;

        public EdgeRuleEngineService(IRuntimeService runtime, ProjectConfig projectConfig, Func<string, string, int, bool> mqttPublisher, MqttGatewayOptions gatewayOptions)
            : this(runtime, projectConfig, mqttPublisher, gatewayOptions, new GatewayResilienceOptions().RuleEngine)
        {
        }

        public EdgeRuleEngineService(
            IRuntimeService runtime,
            ProjectConfig projectConfig,
            Func<string, string, int, bool> mqttPublisher,
            MqttGatewayOptions gatewayOptions,
            CircuitBreakerOptions circuitBreakerOptions,
            IModelInferenceService? modelInference = null)
        {
            _runtime = runtime;
            _projectConfig = projectConfig;
            _mqttPublisher = mqttPublisher;
            _gatewayOptions = gatewayOptions == null ? new MqttGatewayOptions() : gatewayOptions.Clone();
            _circuitBreaker = new CircuitBreaker("RuleEngine", circuitBreakerOptions ?? new GatewayResilienceOptions().RuleEngine);
            _modelInference = modelInference ?? NoopModelInferenceService.Instance;
            _syncRoot = new object();
            _states = new Dictionary<string, EdgeRuleState>(StringComparer.OrdinalIgnoreCase);
            _ruleStatuses = new Dictionary<string, EdgeRuleRuntimeRuleStatus>(StringComparer.OrdinalIgnoreCase);
            _snapshotsByPoint = new Dictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
            _snapshotsByPath = new Dictionary<string, TagValueSnapshot>(StringComparer.OrdinalIgnoreCase);
            _recentEvents = new List<EdgeRuleRuntimeEvent>();
            _lastEvaluationTime = DateTime.MinValue;
            _lastEventTime = DateTime.MinValue;
            _lastErrorTime = DateTime.MinValue;
            _lastError = string.Empty;
        }

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                    return _running;
            }
        }

        public void Start()
        {
            StartCore(true);
        }

        public void StartDetached()
        {
            StartCore(false);
        }

        private void StartCore(bool subscribeToRuntime)
        {
            if (_runtime == null)
                return;

            lock (_syncRoot)
            {
                if (_running)
                    return;
                _running = true;
            }

            if (subscribeToRuntime)
            {
                _runtime.TagValueChanged -= OnTagValueChanged;
                _runtime.TagValueChanged += OnTagValueChanged;
            }
        }

        public void Stop()
        {
            if (_runtime != null)
                _runtime.TagValueChanged -= OnTagValueChanged;

            lock (_syncRoot)
                _running = false;
        }

        public void Reload()
        {
            lock (_syncRoot)
            {
                _states.Clear();
                _ruleStatuses.Clear();
                _snapshotsByPoint.Clear();
                _snapshotsByPath.Clear();
            }
        }

        public IList<EdgeRuleRuntimeEvent> GetRecentEvents()
        {
            lock (_syncRoot)
            {
                List<EdgeRuleRuntimeEvent> events = new List<EdgeRuleRuntimeEvent>();
                for (int i = 0; i < _recentEvents.Count; i++)
                    events.Add(CloneEvent(_recentEvents[i]));
                return events;
            }
        }

        public EdgeRuleEngineStatus GetStatus()
        {
            List<EdgeRuleConfig> rules = GetRules();
            int enabledRuleCount = 0;
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i] != null && rules[i].Enabled)
                    enabledRuleCount++;
            }

            lock (_syncRoot)
            {
                EdgeRuleEngineStatus status = new EdgeRuleEngineStatus
                {
                    IsRunning = _running,
                    Enabled = enabledRuleCount > 0,
                    RuleCount = rules.Count,
                    EnabledRuleCount = enabledRuleCount,
                    ActiveRuleCount = CountActiveRules(),
                    CachedSnapshotCount = _snapshotsByPoint.Count + _snapshotsByPath.Count,
                    RecentEventCount = _recentEvents.Count,
                    EvaluationCount = _evaluationCount,
                    TriggeredCount = _triggeredCount,
                    ClearedCount = _clearedCount,
                    FailedEvaluationCount = _failedEvaluationCount,
                    LastEvaluationTime = _lastEvaluationTime,
                    LastEventTime = _lastEventTime,
                    LastErrorTime = _lastErrorTime,
                    LastError = _lastError,
                    CircuitBreaker = _circuitBreaker.Snapshot()
                };

                for (int i = 0; i < _recentEvents.Count; i++)
                    status.RecentEvents.Add(CloneEvent(_recentEvents[i]));
                for (int i = 0; i < rules.Count; i++)
                    status.Rules.Add(CloneRuleStatus(BuildRuleStatus(rules[i])));
                return status;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Stop();
        }

        private void OnTagValueChanged(object? sender, TagValueChangedEventArgs e)
        {
            if (e == null || e.Snapshot == null)
                return;

            ProcessSnapshot(e.Snapshot);
        }

        public void ProcessSnapshot(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            lock (_syncRoot)
            {
                if (!_running)
                    return;
            }

            RememberSnapshot(snapshot);

            if (!_circuitBreaker.CanExecute())
            {
                RecordEngineDegraded("Rule engine circuit breaker is open; evaluation skipped.");
                return;
            }

            List<EdgeRuleConfig> rules = GetRules();
            if (rules.Count == 0)
                return;

            double value;
            bool hasNumericValue = TryGetNumericValue(snapshot, out value);

            for (int i = 0; i < rules.Count; i++)
            {
                EdgeRuleConfig rule = rules[i];
                if (rule == null || !rule.Enabled)
                    continue;
                if (snapshot.Quality != TagQuality.Good && !HasQualityPolicy(rule))
                    continue;
                if (RequiresNumericValue(rule) && !hasNumericValue && !(snapshot.Quality != TagQuality.Good && HasQualityPolicy(rule)))
                    continue;

                try
                {
                    if (rule.ConditionType == EdgeRuleConditionType.Combination ||
                        rule.ConditionType == EdgeRuleConditionType.Sequence)
                    {
                        RecordEvaluation(rule);
                        EvaluateRule(rule, snapshot, value);
                        _circuitBreaker.RecordSuccess();
                    }
                    else if (Matches(rule, snapshot))
                    {
                        RecordEvaluation(rule);
                        EvaluateRule(rule, snapshot, value);
                        _circuitBreaker.RecordSuccess();
                    }
                }
                catch (Exception ex)
                {
                    _circuitBreaker.RecordFailure(ex.Message);
                    RecordEvaluationFailure(rule, ex);
                    IpcLogService.WriteError("Edge rule evaluation failed: " + rule.Name, ex);
                }
            }
        }

        private List<EdgeRuleConfig> GetRules()
        {
            ProjectConfig config = _projectConfig;
            List<EdgeRuleConfig>? source = config == null ? null : config.Rules;
            List<EdgeRuleConfig> result = new List<EdgeRuleConfig>();
            if (source == null)
                return result;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleConfig rule = source[i];
                if (rule != null)
                    result.Add(rule);
            }
            return result;
        }

        private void EvaluateRule(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value)
        {
            EdgeRuleState state = GetState(rule);
            if (rule.ConditionType != EdgeRuleConditionType.QualityGate &&
                HasQualityPolicy(rule) &&
                !EvaluateQuality(rule, snapshot))
            {
                ApplyBooleanState(rule, snapshot, state, false, "QualityGate", value, 0D);
                return;
            }
            if (rule.ConditionType != EdgeRuleConditionType.QualityGate && snapshot.Quality != TagQuality.Good)
                return;

            value = ApplyValueTransform(rule, snapshot, value);
            if (rule.ConditionType == EdgeRuleConditionType.QualityGate)
            {
                EvaluateQualityGate(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.SlidingWindow)
            {
                EvaluateSlidingWindow(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.WindowCalculation)
            {
                EvaluateSlidingWindow(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Aggregation)
            {
                EvaluateAggregation(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Trend)
            {
                EvaluateTrend(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.AnomalyDetection)
            {
                EvaluateAnomalyDetection(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.ModelInference)
            {
                EvaluateModelInference(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.StateMachine)
            {
                EvaluateStateMachine(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.CycleTime)
            {
                EvaluateCycleTime(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.ProcessTakt)
            {
                EvaluateProcessTakt(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.TagRelation)
            {
                EvaluateTagRelation(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.ContextGate)
            {
                EvaluateContextGate(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Deadband)
            {
                EvaluateDeadband(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.RateOfChange)
            {
                EvaluateRateOfChange(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Hysteresis)
            {
                EvaluateHysteresis(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.MultiLevelAlarm)
            {
                EvaluateMultiLevelAlarm(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Expression)
            {
                EvaluateExpression(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Condition)
            {
                EvaluateSingleCondition(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Sequence)
            {
                EvaluateSequence(rule, snapshot, value, state);
                return;
            }

            if (rule.ConditionType == EdgeRuleConditionType.Combination)
            {
                EvaluateCombination(rule, snapshot, value, state);
                return;
            }

            EvaluateThreshold(rule, snapshot, value, state);
        }

        private double ApplyValueTransform(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value)
        {
            if (rule == null)
                return value;

            return ApplyValueTransform(
                snapshot,
                value,
                rule.TransformMultiplier,
                rule.TransformOffset,
                rule.TransformUseAbsolute,
                rule.TransformExpression,
                rule.TransformTimeoutMilliseconds);
        }

        private double ApplyValueTransform(EdgeRuleConditionConfig condition, TagValueSnapshot snapshot, double value)
        {
            if (condition == null)
                return value;

            return ApplyValueTransform(
                snapshot,
                value,
                condition.TransformMultiplier,
                condition.TransformOffset,
                condition.TransformUseAbsolute,
                condition.TransformExpression,
                DefaultExpressionTimeoutMilliseconds);
        }

        private double ApplyValueTransform(
            TagValueSnapshot snapshot,
            double value,
            double multiplier,
            double offset,
            bool useAbsolute,
            string expression,
            int timeoutMilliseconds)
        {
            double transformed = useAbsolute ? Math.Abs(value) : value;
            transformed = transformed * multiplier + offset;
            if (!string.IsNullOrWhiteSpace(expression))
                transformed = EvaluateNumericFormula(expression, snapshot, transformed, timeoutMilliseconds);
            return transformed;
        }

        private void EvaluateThreshold(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            if (rule.LowLimit > rule.HighLimit)
                return;

            string newState = "Normal";
            double threshold = 0D;
            if (value > rule.HighLimit)
            {
                newState = "High";
                threshold = rule.HighLimit;
            }
            else if (value < rule.LowLimit)
            {
                newState = "Low";
                threshold = rule.LowLimit;
            }

            ApplyBooleanState(rule, snapshot, state, newState != "Normal", newState, value, threshold);
        }

        private void EvaluateDeadband(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            double deadband = Math.Max(0D, rule.Deadband);
            if (deadband <= 0D)
                return;

            if (!state.HasLastValue)
            {
                state.LastValue = value;
                state.LastTimestamp = snapshot.Timestamp;
                state.HasLastValue = true;
                return;
            }

            double delta = Math.Abs(value - state.LastValue);
            if (delta < deadband)
                return;

            state.LastValue = value;
            state.LastTimestamp = snapshot.Timestamp;
            PublishEvent(rule, snapshot, "active", "Deadband", value, deadband, BuildActiveMessage(rule, snapshot, "Deadband"), rule.PublishToMqtt);
        }

        private void EvaluateRateOfChange(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            double limit = Math.Max(0D, rule.RateLimitPerSecond);
            if (limit <= 0D)
                return;

            DateTime timestamp = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            if (!state.HasLastValue)
            {
                state.LastValue = value;
                state.LastTimestamp = timestamp;
                state.HasLastValue = true;
                return;
            }

            double seconds = Math.Max(0.001D, (timestamp - state.LastTimestamp).TotalSeconds);
            double rate = Math.Abs(value - state.LastValue) / seconds;
            state.LastValue = value;
            state.LastTimestamp = timestamp;

            if (rate >= limit)
            {
                ApplyBooleanState(rule, snapshot, state, true, "RateOfChange", value, limit);
            }
            else
            {
                ApplyBooleanState(rule, snapshot, state, false, "RateOfChange", value, limit);
            }
        }

        private void EvaluateHysteresis(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            bool highMode = !string.Equals(rule.HysteresisMode, "Low", StringComparison.OrdinalIgnoreCase);
            bool activeNow = !string.IsNullOrWhiteSpace(state.ActiveState);
            bool active = activeNow;
            if (highMode)
                active = activeNow ? value > rule.HysteresisOffValue : value >= rule.HysteresisOnValue;
            else
                active = activeNow ? value < rule.HysteresisOffValue : value <= rule.HysteresisOnValue;

            string stateName = highMode ? "HysteresisHigh" : "HysteresisLow";
            double threshold = active ? rule.HysteresisOnValue : rule.HysteresisOffValue;
            ApplyBooleanState(rule, snapshot, state, active, stateName, value, threshold);
        }

        private void EvaluateMultiLevelAlarm(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            if (rule.AlarmLevels == null || rule.AlarmLevels.Count == 0)
                return;

            EdgeRuleAlarmLevelConfig? matched = null;
            for (int i = 0; i < rule.AlarmLevels.Count; i++)
            {
                EdgeRuleAlarmLevelConfig level = rule.AlarmLevels[i];
                if (level != null && Compare(value, level.Operator, level.CompareValue))
                    matched = level;
            }

            if (matched == null)
            {
                ApplyBooleanState(rule, snapshot, state, false, "Normal", value, 0D);
                return;
            }

            string stateName = string.IsNullOrWhiteSpace(matched.Name) ? matched.Severity : matched.Name;
            if (string.IsNullOrWhiteSpace(stateName))
                stateName = "Alarm";
            ApplyBooleanState(rule, snapshot, state, true, stateName, value, matched.CompareValue);
        }

        private void EvaluateExpression(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            bool active = EvaluateNumericExpression(rule.Expression, snapshot, value, DefaultExpressionTimeoutMilliseconds);
            ApplyBooleanState(rule, snapshot, state, active, "Expression", value, 0D);
        }

        private void EvaluateQualityGate(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            bool active = EvaluateQuality(rule, snapshot);
            string qualityState = "Quality:" + snapshot.Quality.ToString();
            ApplyBooleanState(rule, snapshot, state, active, qualityState, value, 0D);
        }

        private void EvaluateSlidingWindow(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime timestamp = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            if (state.WindowSamples == null)
                state.WindowSamples = new List<EdgeRuleWindowSample>();

            state.WindowSamples.Add(new EdgeRuleWindowSample(value, timestamp));
            TrimWindowSamples(rule, state.WindowSamples, timestamp);
            if (state.WindowSamples.Count == 0)
                return;

            double statistic = CalculateWindowStatistic(rule.WindowStatistic, state.WindowSamples);
            bool active = Compare(statistic, rule.Operator, rule.CompareValue);
            string stateName = "Window" + NormalizeWindowStatistic(rule.WindowStatistic);
            ApplyBooleanState(rule, snapshot, state, active, stateName, statistic, rule.CompareValue);
        }

        private void EvaluateAggregation(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue, EdgeRuleState state)
        {
            List<double> values = new List<double>();
            TagValueSnapshot eventSnapshot = triggerSnapshot;
            bool triggerIsSource = MatchesSource(rule.SourcePointCode, rule.SourceDeviceName, rule.SourceGroupName, rule.SourceTagName, triggerSnapshot);

            if (triggerIsSource)
            {
                AddNumericValue(values, triggerSnapshot, triggerValue);
            }
            else if (TryGetRuleSnapshot(rule, out TagValueSnapshot sourceSnapshot))
            {
                eventSnapshot = sourceSnapshot;
                AddNumericValue(values, sourceSnapshot, raw => ApplyValueTransform(rule, sourceSnapshot, raw));
            }

            if (HasConfiguredSource(rule.RelatedPointCode, rule.RelatedDeviceName, rule.RelatedGroupName, rule.RelatedTagName) &&
                TryGetRelatedSnapshot(rule, out TagValueSnapshot relatedSnapshot))
            {
                AddNumericValue(values, relatedSnapshot, raw => raw * rule.RelationMultiplier + rule.RelationOffset);
            }

            if (HasConfiguredSource(rule.ContextPointCode, rule.ContextDeviceName, rule.ContextGroupName, rule.ContextTagName) &&
                TryGetContextSnapshot(rule, triggerSnapshot, out TagValueSnapshot contextSnapshot))
            {
                AddNumericValue(values, contextSnapshot, raw => raw);
            }

            if (rule.Conditions != null)
            {
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    EdgeRuleConditionConfig condition = rule.Conditions[i];
                    if (condition == null || !TryGetConditionSnapshot(condition, out TagValueSnapshot conditionSnapshot))
                        continue;

                    AddNumericValue(values, conditionSnapshot, raw => ApplyValueTransform(condition, conditionSnapshot, raw));
                }
            }

            if (values.Count == 0)
                return;

            string statisticName = string.IsNullOrWhiteSpace(rule.AggregationStatistic)
                ? rule.WindowStatistic
                : rule.AggregationStatistic;
            double aggregate = CalculateStatistic(statisticName, values);
            bool active = Compare(aggregate, rule.Operator, rule.CompareValue);
            string stateName = "Aggregation" + NormalizeWindowStatistic(statisticName);
            ApplyBooleanState(rule, eventSnapshot ?? triggerSnapshot, state, active, stateName, aggregate, rule.CompareValue);
        }

        private void EvaluateTrend(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime timestamp = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            if (state.WindowSamples == null)
                state.WindowSamples = new List<EdgeRuleWindowSample>();

            state.WindowSamples.Add(new EdgeRuleWindowSample(value, timestamp));
            TrimWindowSamples(state.WindowSamples, timestamp, Math.Max(1, rule.TrendWindowSeconds), Math.Max(0, rule.TrendSampleCount));
            if (state.WindowSamples.Count < 2)
                return;

            EdgeRuleWindowSample first = state.WindowSamples[0];
            EdgeRuleWindowSample last = state.WindowSamples[state.WindowSamples.Count - 1];
            double seconds = Math.Max(0.001D, (last.Timestamp - first.Timestamp).TotalSeconds);
            double delta = last.Value - first.Value;
            double slope = delta / seconds;
            double minSlope = Math.Max(0D, rule.TrendMinSlopePerSecond);
            double changeThreshold = Math.Max(0D, rule.TrendChangeThreshold);
            double stableDeadband = Math.Max(0D, rule.TrendStableDeadband);
            string mode = NormalizeTrendMode(rule.TrendMode);

            bool active;
            string stateName;
            double metric;
            double threshold;
            if (string.Equals(mode, "Rising", StringComparison.OrdinalIgnoreCase))
            {
                active = minSlope > 0D ? slope >= minSlope : delta >= changeThreshold;
                stateName = "TrendRising";
                metric = minSlope > 0D ? slope : delta;
                threshold = minSlope > 0D ? minSlope : changeThreshold;
            }
            else if (string.Equals(mode, "Falling", StringComparison.OrdinalIgnoreCase))
            {
                active = minSlope > 0D ? slope <= -minSlope : delta <= -changeThreshold;
                stateName = "TrendFalling";
                metric = minSlope > 0D ? slope : delta;
                threshold = minSlope > 0D ? -minSlope : -changeThreshold;
            }
            else if (string.Equals(mode, "Stable", StringComparison.OrdinalIgnoreCase))
            {
                active = Math.Abs(delta) <= stableDeadband;
                stateName = "TrendStable";
                metric = Math.Abs(delta);
                threshold = stableDeadband;
            }
            else
            {
                active = minSlope > 0D ? Math.Abs(slope) >= minSlope : Math.Abs(delta) >= changeThreshold;
                stateName = "TrendSlope";
                metric = minSlope > 0D ? Math.Abs(slope) : Math.Abs(delta);
                threshold = minSlope > 0D ? minSlope : changeThreshold;
            }

            ApplyBooleanState(rule, snapshot, state, active, stateName, metric, threshold);
        }

        private void EvaluateStateMachine(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime now = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            string current = GetStateMachineValue(snapshot);
            bool expected = MatchesStateValue(current, rule.StateExpectedValue);
            bool clear = !string.IsNullOrWhiteSpace(rule.StateClearValue) && MatchesStateValue(current, rule.StateClearValue);

            if (!expected || clear)
            {
                state.StateMachineInExpected = false;
                state.StateMachineEnteredTime = DateTime.MinValue;
                ApplyBooleanState(rule, snapshot, state, false, StateName(rule), value, 0D);
                return;
            }

            if (!state.StateMachineInExpected)
            {
                state.StateMachineInExpected = true;
                state.StateMachineEnteredTime = now;
            }

            int timeoutSeconds = Math.Max(0, rule.StateTimeoutSeconds);
            bool active = timeoutSeconds <= 0 || now - state.StateMachineEnteredTime >= TimeSpan.FromSeconds(timeoutSeconds);
            ApplyBooleanState(rule, snapshot, state, active, StateName(rule), value, timeoutSeconds);
        }

        private void EvaluateCycleTime(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime now = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            string current = GetStateMachineValue(snapshot);
            if (MatchesStateValue(current, rule.CycleStartValue))
            {
                state.CycleStarted = true;
                state.CycleStartedTime = now;
                return;
            }

            if (!state.CycleStarted)
                return;

            int maxSeconds = Math.Max(0, rule.CycleMaxSeconds);
            if (maxSeconds > 0 && !MatchesStateValue(current, rule.CycleEndValue))
            {
                double runningSeconds = Math.Max(0D, (now - state.CycleStartedTime).TotalSeconds);
                if (runningSeconds > maxSeconds)
                    ApplyBooleanState(rule, snapshot, state, true, "CycleTooSlow", runningSeconds, maxSeconds);
                return;
            }

            if (!MatchesStateValue(current, rule.CycleEndValue))
                return;

            double elapsedSeconds = Math.Max(0D, (now - state.CycleStartedTime).TotalSeconds);
            state.CycleStarted = false;
            state.CycleStartedTime = DateTime.MinValue;

            int minSeconds = Math.Max(0, rule.CycleMinSeconds);
            bool tooFast = minSeconds > 0 && elapsedSeconds < minSeconds;
            bool tooSlow = maxSeconds > 0 && elapsedSeconds > maxSeconds;
            if (tooFast)
            {
                ApplyBooleanState(rule, snapshot, state, true, "CycleTooFast", elapsedSeconds, minSeconds);
                return;
            }
            if (tooSlow)
            {
                ApplyBooleanState(rule, snapshot, state, true, "CycleTooSlow", elapsedSeconds, maxSeconds);
                return;
            }

            ApplyBooleanState(rule, snapshot, state, false, "CycleNormal", elapsedSeconds, 0D);
        }

        private void EvaluateProcessTakt(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime now = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            string current = GetStateMachineValue(snapshot);
            double targetSeconds = rule.TaktTargetSeconds > 0D ? rule.TaktTargetSeconds : Math.Max(1D, rule.CycleMaxSeconds);
            double tolerancePercent = Math.Max(0D, rule.TaktTolerancePercent);
            double minSeconds = Math.Max(0D, targetSeconds * (1D - tolerancePercent / 100D));
            double maxSeconds = targetSeconds * (1D + tolerancePercent / 100D);

            if (MatchesStateValue(current, rule.CycleStartValue))
            {
                state.CycleStarted = true;
                state.CycleStartedTime = now;
                return;
            }

            if (!state.CycleStarted)
                return;

            double runningSeconds = Math.Max(0D, (now - state.CycleStartedTime).TotalSeconds);
            if (!MatchesStateValue(current, rule.CycleEndValue))
            {
                if (maxSeconds > 0D && runningSeconds > maxSeconds)
                    ApplyBooleanState(rule, snapshot, state, true, "TaktTooSlow", runningSeconds, maxSeconds);
                return;
            }

            state.CycleStarted = false;
            state.CycleStartedTime = DateTime.MinValue;

            if (minSeconds > 0D && runningSeconds < minSeconds)
            {
                ApplyBooleanState(rule, snapshot, state, true, "TaktTooFast", runningSeconds, minSeconds);
                return;
            }

            if (maxSeconds > 0D && runningSeconds > maxSeconds)
            {
                ApplyBooleanState(rule, snapshot, state, true, "TaktTooSlow", runningSeconds, maxSeconds);
                return;
            }

            ApplyBooleanState(rule, snapshot, state, false, "TaktNormal", runningSeconds, targetSeconds);
        }

        private void EvaluateAnomalyDetection(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            DateTime timestamp = snapshot.Timestamp == DateTime.MinValue ? DateTime.Now : snapshot.Timestamp;
            if (state.WindowSamples == null)
                state.WindowSamples = new List<EdgeRuleWindowSample>();

            TrimWindowSamples(
                state.WindowSamples,
                timestamp,
                Math.Max(1, rule.AnomalyBaselineWindowSeconds),
                Math.Max(0, rule.AnomalyBaselineSampleCount));

            if (state.WindowSamples.Count < 2)
            {
                state.WindowSamples.Add(new EdgeRuleWindowSample(value, timestamp));
                TrimWindowSamples(
                    state.WindowSamples,
                    timestamp,
                    Math.Max(1, rule.AnomalyBaselineWindowSeconds),
                    Math.Max(0, rule.AnomalyBaselineSampleCount));
                return;
            }

            string mode = NormalizeAnomalyMode(rule.AnomalyMode);
            double threshold = Math.Max(0D, rule.AnomalyThreshold);
            bool active = false;
            double metric = 0D;

            if (string.Equals(mode, "Spike", StringComparison.OrdinalIgnoreCase))
            {
                EdgeRuleWindowSample previous = state.WindowSamples[state.WindowSamples.Count - 1];
                metric = Math.Abs(value - previous.Value);
                active = threshold > 0D && metric >= threshold;
            }
            else
            {
                double average = state.WindowSamples.Average(item => item.Value);
                double stdDev = CalculateStatistic("StdDev", state.WindowSamples.Select(item => item.Value).ToList());
                double deviation = Math.Abs(value - average);
                if (string.Equals(mode, "Deviation", StringComparison.OrdinalIgnoreCase))
                {
                    metric = deviation;
                    active = threshold > 0D && deviation >= threshold;
                }
                else
                {
                    metric = stdDev <= 0D ? 0D : deviation / stdDev;
                    active = stdDev > 0D && metric >= (threshold <= 0D ? 3D : threshold);
                    threshold = threshold <= 0D ? 3D : threshold;
                }
            }

            state.WindowSamples.Add(new EdgeRuleWindowSample(value, timestamp));
            TrimWindowSamples(
                state.WindowSamples,
                timestamp,
                Math.Max(1, rule.AnomalyBaselineWindowSeconds),
                Math.Max(0, rule.AnomalyBaselineSampleCount));

            ApplyBooleanState(rule, snapshot, state, active, "Anomaly" + mode, metric, threshold);
        }

        private void EvaluateModelInference(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            ModelInferenceRequest request = BuildModelInferenceRequest(rule, snapshot, value);
            ModelInferenceResult result = _modelInference.Predict(request);
            if (result == null || !result.Success)
            {
                string message = result == null ? "No inference result returned." : result.ErrorMessage;
                throw new InvalidOperationException("ONNX inference failed: " + message);
            }

            double score = result.Score;
            bool active = Compare(score, rule.ModelOperator, rule.ModelThreshold);
            string stateName = string.Equals(rule.ModelPurpose, "QualityPrediction", StringComparison.OrdinalIgnoreCase)
                ? "QualityPrediction"
                : "DeviceAnomaly";
            ApplyBooleanState(rule, snapshot, state, active, stateName, score, rule.ModelThreshold);
        }

        private ModelInferenceRequest BuildModelInferenceRequest(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue)
        {
            List<float> features = new List<float>();
            List<string> inputTags = SplitModelInputTags(rule.ModelInputTags);
            if (inputTags.Count == 0)
            {
                features.Add((float)triggerValue);
            }
            else
            {
                for (int i = 0; i < inputTags.Count; i++)
                {
                    string token = inputTags[i];
                    TagValueSnapshot inputSnapshot;
                    if (MatchesSnapshotToken(triggerSnapshot, token))
                    {
                        inputSnapshot = triggerSnapshot;
                    }
                    else if (!TryGetSnapshotByToken(token, out inputSnapshot))
                    {
                        throw new InvalidOperationException("Model input tag was not found: " + token);
                    }

                    if (inputSnapshot.Quality != TagQuality.Good)
                        throw new InvalidOperationException("Model input tag quality is not good: " + token);

                    double feature;
                    if (!TryGetNumericValue(inputSnapshot, out feature))
                        throw new InvalidOperationException("Model input tag is not numeric: " + token);
                    features.Add((float)feature);
                }
            }

            return new ModelInferenceRequest
            {
                ModelPath = rule.ModelPath,
                ModelPurpose = rule.ModelPurpose,
                InputName = rule.ModelInputName,
                InputNames = rule.ModelInputNames,
                OutputName = rule.ModelOutputName,
                OutputIndex = Math.Max(0, rule.ModelOutputIndex),
                TimeoutMilliseconds = NormalizeModelTimeout(rule.ModelTimeoutMilliseconds),
                Features = features
            };
        }

        private static List<string> SplitModelInputTags(string inputTags)
        {
            List<string> tags = new List<string>();
            if (string.IsNullOrWhiteSpace(inputTags))
                return tags;

            string[] parts = inputTags.Split(new[] { ',', ';', '|', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string tag = NullToEmpty(parts[i]).Trim();
                if (!string.IsNullOrWhiteSpace(tag))
                    tags.Add(tag);
            }

            return tags;
        }

        private static int NormalizeModelTimeout(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
                return 1000;
            return Math.Min(30000, timeoutMilliseconds);
        }

        private static bool MatchesSnapshotToken(TagValueSnapshot snapshot, string token)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(token))
                return false;

            string key = token.Trim();
            if (string.Equals(GetPointCode(snapshot), key, StringComparison.OrdinalIgnoreCase))
                return true;

            string group = snapshot.GroupName ?? string.Empty;
            string path = string.IsNullOrWhiteSpace(group)
                ? NullToEmpty(snapshot.DeviceName).Trim() + "." + NullToEmpty(snapshot.TagName).Trim()
                : NullToEmpty(snapshot.DeviceName).Trim() + "." + group.Trim() + "." + NullToEmpty(snapshot.TagName).Trim();
            return string.Equals(path, key, StringComparison.OrdinalIgnoreCase);
        }

        private void EvaluateTagRelation(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue, EdgeRuleState state)
        {
            TagValueSnapshot sourceSnapshot = MatchesSource(rule.SourcePointCode, rule.SourceDeviceName, rule.SourceGroupName, rule.SourceTagName, triggerSnapshot)
                ? triggerSnapshot
                : TryGetRuleSnapshot(rule, out TagValueSnapshot cachedSource) ? cachedSource : new TagValueSnapshot();
            if (string.IsNullOrWhiteSpace(sourceSnapshot.TagName) && string.IsNullOrWhiteSpace(GetPointCode(sourceSnapshot)))
                return;
            if (sourceSnapshot.Quality != TagQuality.Good)
            {
                ApplyBooleanState(rule, triggerSnapshot, state, false, "TagRelation", triggerValue, 0D);
                return;
            }

            TagValueSnapshot relatedSnapshot;
            if (!TryGetRelatedSnapshot(rule, out relatedSnapshot))
                return;
            if (relatedSnapshot.Quality != TagQuality.Good)
            {
                ApplyBooleanState(rule, sourceSnapshot, state, false, "TagRelation", triggerValue, 0D);
                return;
            }

            double sourceValue;
            double relatedValue;
            if (!TryGetNumericValue(sourceSnapshot, out sourceValue) ||
                !TryGetNumericValue(relatedSnapshot, out relatedValue))
            {
                return;
            }

            sourceValue = ApplyValueTransform(rule, sourceSnapshot, sourceValue);
            double targetValue = relatedValue * rule.RelationMultiplier + rule.RelationOffset;
            bool active = Compare(sourceValue, rule.RelationOperator, targetValue);
            ApplyBooleanState(rule, sourceSnapshot, state, active, "TagRelation", sourceValue, targetValue);
        }

        private void EvaluateContextGate(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue, EdgeRuleState state)
        {
            TagValueSnapshot contextSnapshot;
            if (!TryGetContextSnapshot(rule, triggerSnapshot, out contextSnapshot))
                return;

            string current = GetStateMachineValue(contextSnapshot);
            bool active = CompareStateValue(current, rule.ContextExpectedValue, rule.ContextOperator);
            string stateName = string.IsNullOrWhiteSpace(rule.ContextName) ? "ContextGate" : rule.ContextName.Trim();
            double numericValue = 0D;
            TryGetNumericValue(contextSnapshot, out numericValue);
            ApplyBooleanState(rule, contextSnapshot, state, active, stateName, numericValue, 0D);
        }

        private void EvaluateSingleCondition(EdgeRuleConfig rule, TagValueSnapshot snapshot, double value, EdgeRuleState state)
        {
            bool active = Compare(value, rule.Operator, rule.CompareValue);
            string conditionState = FormatOperator(rule.Operator) + " " + rule.CompareValue.ToString("G", CultureInfo.InvariantCulture);
            ApplyBooleanState(rule, snapshot, state, active, conditionState, value, rule.CompareValue);
        }

        private void EvaluateCombination(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue, EdgeRuleState state)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                return;

            bool anyEvaluated = false;
            bool result = rule.LogicalOperator == EdgeRuleLogicalOperator.And;
            double lastValue = triggerValue;
            double lastThreshold = 0D;

            for (int i = 0; i < rule.Conditions.Count; i++)
            {
                EdgeRuleConditionConfig condition = rule.Conditions[i];
                TagValueSnapshot conditionSnapshot;
                if (!TryGetConditionSnapshot(condition, out conditionSnapshot))
                {
                    if (rule.LogicalOperator == EdgeRuleLogicalOperator.And)
                        return;
                    continue;
                }
                if (conditionSnapshot.Quality != TagQuality.Good)
                {
                    if (rule.LogicalOperator == EdgeRuleLogicalOperator.And)
                        return;
                    continue;
                }

                double conditionValue;
                if (!TryGetNumericValue(conditionSnapshot, out conditionValue))
                {
                    if (rule.LogicalOperator == EdgeRuleLogicalOperator.And)
                        return;
                    continue;
                }

                conditionValue = ApplyValueTransform(condition, conditionSnapshot, conditionValue);
                bool conditionResult = Compare(conditionValue, condition.Operator, condition.CompareValue);
                anyEvaluated = true;
                lastValue = conditionValue;
                lastThreshold = condition.CompareValue;

                if (rule.LogicalOperator == EdgeRuleLogicalOperator.And)
                {
                    result = result && conditionResult;
                    if (!result)
                        break;
                }
                else
                {
                    result = result || conditionResult;
                    if (result)
                        break;
                }
            }

            if (!anyEvaluated)
                return;

            string stateName = rule.LogicalOperator == EdgeRuleLogicalOperator.And ? "CombinationAnd" : "CombinationOr";
            ApplyBooleanState(rule, triggerSnapshot, state, result, stateName, lastValue, lastThreshold);
        }

        private void EvaluateSequence(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, double triggerValue, EdgeRuleState state)
        {
            if (rule.Conditions == null || rule.Conditions.Count == 0)
                return;

            DateTime now = triggerSnapshot.Timestamp == DateTime.MinValue ? DateTime.Now : triggerSnapshot.Timestamp;
            ResetSequenceIfExpired(rule, state, now);

            int expectedIndex = Math.Max(0, state.SequenceStepIndex);
            int matchedIndex = FindMatchedSequenceStep(rule, triggerSnapshot, expectedIndex, out double matchedValue, out double threshold);
            if (matchedIndex < 0)
                return;

            if (expectedIndex > 0 && rule.SequenceMinIntervalSeconds > 0)
            {
                double secondsSinceLastStep = (now - state.SequenceLastStepTime).TotalSeconds;
                if (secondsSinceLastStep < rule.SequenceMinIntervalSeconds)
                    return;
            }

            if (matchedIndex == expectedIndex)
            {
                AdvanceSequence(rule, triggerSnapshot, state, now, matchedValue, threshold);
                return;
            }

            if (rule.SequenceResetOnMismatch)
            {
                ResetSequence(state);
                if (matchedIndex == 0)
                    AdvanceSequence(rule, triggerSnapshot, state, now, matchedValue, threshold);
            }
        }

        private int FindMatchedSequenceStep(EdgeRuleConfig rule, TagValueSnapshot snapshot, int expectedIndex, out double value, out double threshold)
        {
            value = 0D;
            threshold = 0D;
            if (rule == null || rule.Conditions == null)
                return -1;

            int safeExpectedIndex = Math.Max(0, expectedIndex);
            if (safeExpectedIndex < rule.Conditions.Count &&
                TryMatchSequenceStep(rule.Conditions[safeExpectedIndex], snapshot, out value, out threshold))
            {
                return safeExpectedIndex;
            }

            for (int i = 0; i < rule.Conditions.Count; i++)
            {
                if (i == safeExpectedIndex)
                    continue;
                if (TryMatchSequenceStep(rule.Conditions[i], snapshot, out value, out threshold))
                    return i;
            }

            return -1;
        }

        private bool TryMatchSequenceStep(EdgeRuleConditionConfig condition, TagValueSnapshot snapshot, out double value, out double threshold)
        {
            value = 0D;
            threshold = 0D;
            if (condition == null || !MatchesSource(condition.SourcePointCode, condition.SourceDeviceName, condition.SourceGroupName, condition.SourceTagName, snapshot))
                return false;
            if (snapshot.Quality != TagQuality.Good)
                return false;

            double rawValue;
            if (!TryGetNumericValue(snapshot, out rawValue))
                return false;

            value = ApplyValueTransform(condition, snapshot, rawValue);
            threshold = condition.CompareValue;
            return Compare(value, condition.Operator, condition.CompareValue);
        }

        private void AdvanceSequence(EdgeRuleConfig rule, TagValueSnapshot snapshot, EdgeRuleState state, DateTime now, double value, double threshold)
        {
            if (state.SequenceStepIndex == 0)
                state.SequenceStartedTime = now;

            state.SequenceStepIndex++;
            state.SequenceLastStepTime = now;

            if (state.SequenceStepIndex < rule.Conditions.Count)
                return;

            string activeState = "Sequence";
            PublishEvent(rule, snapshot, "active", activeState, value, threshold, BuildActiveMessage(rule, snapshot, activeState), rule.PublishToMqtt);
            ResetSequence(state);
        }

        private void ResetSequenceIfExpired(EdgeRuleConfig rule, EdgeRuleState state, DateTime now)
        {
            if (state.SequenceStepIndex <= 0)
                return;

            if (rule.SequenceWindowSeconds > 0 && now - state.SequenceStartedTime > TimeSpan.FromSeconds(rule.SequenceWindowSeconds))
            {
                ResetSequence(state);
                return;
            }

            if (rule.SequenceStepTimeoutSeconds > 0 && now - state.SequenceLastStepTime > TimeSpan.FromSeconds(rule.SequenceStepTimeoutSeconds))
                ResetSequence(state);
        }

        private static void ResetSequence(EdgeRuleState state)
        {
            if (state == null)
                return;
            state.SequenceStepIndex = 0;
            state.SequenceStartedTime = DateTime.MinValue;
            state.SequenceLastStepTime = DateTime.MinValue;
        }

        private void ApplyBooleanState(EdgeRuleConfig rule, TagValueSnapshot snapshot, EdgeRuleState state, bool active, string activeState, double value, double threshold)
        {
            DateTime now = DateTime.Now;
            if (!active)
            {
                state.PendingState = string.Empty;
                state.PendingSince = DateTime.MinValue;
                if (!string.IsNullOrEmpty(state.ActiveState))
                {
                    int clearDurationSeconds = Math.Max(0, rule.ClearDurationSeconds);
                    if (clearDurationSeconds > 0)
                    {
                        if (!string.Equals(state.PendingClearState, state.ActiveState, StringComparison.OrdinalIgnoreCase))
                        {
                            state.PendingClearState = state.ActiveState;
                            state.PendingClearSince = now;
                        }

                        if (now - state.PendingClearSince < TimeSpan.FromSeconds(clearDurationSeconds))
                            return;
                    }

                    string oldState = state.ActiveState;
                    state.ActiveState = string.Empty;
                    state.ActiveSince = DateTime.MinValue;
                    state.EscalationPublished = false;
                    state.PendingClearState = string.Empty;
                    state.PendingClearSince = DateTime.MinValue;
                    PublishEvent(rule, snapshot, "clear", oldState, value, threshold, BuildClearMessage(rule, snapshot, oldState), rule.PublishToMqtt && rule.PublishOnClear);
                }
                return;
            }

            state.PendingClearState = string.Empty;
            state.PendingClearSince = DateTime.MinValue;
            string newState = string.IsNullOrWhiteSpace(activeState) ? "Active" : activeState;
            int durationSeconds = Math.Max(0, rule.DurationSeconds);
            if (!string.Equals(state.PendingState, newState, StringComparison.OrdinalIgnoreCase))
            {
                state.PendingState = newState;
                state.PendingSince = now;
            }

            if (durationSeconds > 0 && now - state.PendingSince < TimeSpan.FromSeconds(durationSeconds))
                return;

            if (string.Equals(state.ActiveState, newState, StringComparison.OrdinalIgnoreCase))
            {
                if (ShouldPublishEscalation(rule, state, now))
                {
                    state.EscalationPublished = true;
                    PublishEvent(rule, snapshot, "active", "Escalated:" + newState, value, threshold, BuildActiveMessage(rule, snapshot, newState), rule.PublishToMqtt);
                }
                return;
            }

            if (!CanPublishActiveEvent(rule, state, now))
                return;

            state.ActiveState = newState;
            state.ActiveSince = now;
            state.EscalationPublished = false;
            PublishEvent(rule, snapshot, "active", newState, value, threshold, BuildActiveMessage(rule, snapshot, newState), rule.PublishToMqtt);
        }

        private EdgeRuleState GetState(EdgeRuleConfig? rule)
        {
            string id = GetRuleId(rule);
            if (string.IsNullOrWhiteSpace(id))
                id = "__unknown__";

            lock (_syncRoot)
            {
                EdgeRuleState? state;
                if (!_states.TryGetValue(id, out state) || state == null)
                {
                    state = new EdgeRuleState();
                    _states[id] = state;
                }
                return state;
            }
        }

        private void PublishEvent(EdgeRuleConfig rule, TagValueSnapshot snapshot, string eventType, string state, double value, double threshold, string message, bool publishCustomEvent)
        {
            DateTime now = DateTime.Now;
            EdgeRuleState ruleState = GetState(rule);
            if (string.Equals(eventType, "active", StringComparison.OrdinalIgnoreCase) &&
                !CanPublishActiveEvent(rule, ruleState, now))
            {
                return;
            }

            EdgeRuleRuntimeEvent ruleEvent = new EdgeRuleRuntimeEvent
            {
                RuleId = rule.Id,
                RuleName = rule.Name,
                ConditionType = rule.ConditionType,
                EventType = eventType,
                State = state,
                Message = message,
                Snapshot = snapshot.Clone(),
                SourceValues = BuildSourceValues(rule, snapshot),
                Value = value,
                Threshold = threshold,
                Timestamp = now
            };

            string cloudTopic = BuildCloudTopic(rule, snapshot);
            ruleEvent.Topic = cloudTopic;
            ruleEvent.Payload = BuildPayload(ruleEvent);
            AddRecentEvent(ruleEvent);
            if (string.Equals(eventType, "active", StringComparison.OrdinalIgnoreCase))
                MarkActiveEventPublished(ruleState, now);
            IpcLogService.WriteAudit("EdgeRule", rule.Name, eventType + ":" + state + " " + value.ToString("R", CultureInfo.InvariantCulture));

            if (rule.Actions != null && rule.Actions.Count > 0)
            {
                if (CanDispatchActions(rule, ruleState, now))
                    DispatchActions(ruleEvent, rule.Actions, Math.Max(0, rule.ActionDelaySeconds));
                return;
            }

            if (!publishCustomEvent || _mqttPublisher == null)
                return;

            int qos = ClampQos(rule.PublishQos);
            _mqttPublisher(cloudTopic, ruleEvent.Payload, qos);
        }

        private List<EdgeRuleRuntimeSourceValue> BuildSourceValues(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot)
        {
            List<EdgeRuleRuntimeSourceValue> values = new List<EdgeRuleRuntimeSourceValue>();
            AddSourceValue(values, "trigger", triggerSnapshot);
            if (rule == null)
                return values;

            AddConfiguredSourceValue(
                values,
                "source",
                rule.SourcePointCode,
                rule.SourceDeviceName,
                rule.SourceGroupName,
                rule.SourceTagName,
                triggerSnapshot);
            AddConfiguredSourceValue(
                values,
                "related",
                rule.RelatedPointCode,
                rule.RelatedDeviceName,
                rule.RelatedGroupName,
                rule.RelatedTagName,
                triggerSnapshot);
            AddConfiguredSourceValue(
                values,
                "context",
                rule.ContextPointCode,
                rule.ContextDeviceName,
                rule.ContextGroupName,
                rule.ContextTagName,
                triggerSnapshot);

            if (rule.Conditions != null)
            {
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    EdgeRuleConditionConfig condition = rule.Conditions[i];
                    if (condition == null)
                        continue;

                    AddConfiguredSourceValue(
                        values,
                        "condition",
                        condition.SourcePointCode,
                        condition.SourceDeviceName,
                        condition.SourceGroupName,
                        condition.SourceTagName,
                        triggerSnapshot);
                }
            }

            AddModelInputSourceValues(values, rule, triggerSnapshot);
            return values;
        }

        private void AddConfiguredSourceValue(
            List<EdgeRuleRuntimeSourceValue> values,
            string role,
            string pointCode,
            string deviceName,
            string groupName,
            string tagName,
            TagValueSnapshot triggerSnapshot)
        {
            if (!HasConfiguredSource(pointCode, deviceName, groupName, tagName))
                return;

            TagValueSnapshot snapshot;
            if (triggerSnapshot != null && MatchesSource(pointCode, deviceName, groupName, tagName, triggerSnapshot))
            {
                snapshot = triggerSnapshot;
            }
            else if (!TryGetSnapshotBySource(pointCode, deviceName, groupName, tagName, out snapshot))
            {
                return;
            }

            AddSourceValue(values, role, snapshot);
        }

        private void AddModelInputSourceValues(List<EdgeRuleRuntimeSourceValue> values, EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot)
        {
            if (rule == null)
                return;

            List<string> inputTags = SplitModelInputTags(rule.ModelInputTags);
            for (int i = 0; i < inputTags.Count; i++)
            {
                string token = inputTags[i];
                TagValueSnapshot inputSnapshot;
                if (triggerSnapshot != null && MatchesSnapshotToken(triggerSnapshot, token))
                {
                    inputSnapshot = triggerSnapshot;
                }
                else if (!TryGetSnapshotByToken(token, out inputSnapshot))
                {
                    continue;
                }

                AddSourceValue(values, "modelInput", inputSnapshot);
            }
        }

        private static void AddSourceValue(List<EdgeRuleRuntimeSourceValue> values, string role, TagValueSnapshot snapshot)
        {
            if (values == null || snapshot == null)
                return;

            string key = BuildSourceValueKey(snapshot);
            if (values.Any(item => item != null && string.Equals(BuildSourceValueKey(item.Snapshot), key, StringComparison.OrdinalIgnoreCase)))
                return;

            values.Add(new EdgeRuleRuntimeSourceValue
            {
                Role = role ?? string.Empty,
                Snapshot = snapshot.Clone()
            });
        }

        private static string BuildSourceValueKey(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(snapshot.DeviceName) || !string.IsNullOrWhiteSpace(snapshot.TagName))
                return "path:" + BuildPathKey(snapshot.DeviceName, snapshot.GroupName, snapshot.TagName);

            string pointCode = GetPointCode(snapshot);
            if (!string.IsNullOrWhiteSpace(pointCode))
                return "point:" + pointCode.Trim();

            return "tag:" + NullToEmpty(snapshot.TagName).Trim();
        }

        private static bool CanPublishActiveEvent(EdgeRuleConfig rule, EdgeRuleState state, DateTime now)
        {
            if (rule == null || state == null)
                return true;

            int suppressSeconds = Math.Max(Math.Max(0, rule.AlarmSuppressSeconds), Math.Max(0, rule.AlarmReTriggerSeconds));
            if (suppressSeconds <= 0 || state.LastActiveEventTime == DateTime.MinValue)
                return true;

            return now - state.LastActiveEventTime >= TimeSpan.FromSeconds(suppressSeconds);
        }

        private static void MarkActiveEventPublished(EdgeRuleState state, DateTime now)
        {
            if (state == null)
                return;
            state.LastActiveEventTime = now;
        }

        private static bool ShouldPublishEscalation(EdgeRuleConfig rule, EdgeRuleState state, DateTime now)
        {
            if (rule == null || state == null || state.EscalationPublished)
                return false;

            int seconds = Math.Max(0, rule.AlarmEscalateAfterSeconds);
            if (seconds <= 0 || state.ActiveSince == DateTime.MinValue)
                return false;

            return now - state.ActiveSince >= TimeSpan.FromSeconds(seconds);
        }

        private static bool CanDispatchActions(EdgeRuleConfig rule, EdgeRuleState state, DateTime now)
        {
            if (rule == null || state == null)
                return true;

            int cooldownSeconds = Math.Max(0, rule.ActionCooldownSeconds);
            if (cooldownSeconds > 0 &&
                state.LastActionDispatchTime != DateTime.MinValue &&
                now - state.LastActionDispatchTime < TimeSpan.FromSeconds(cooldownSeconds))
            {
                return false;
            }

            int maxPerMinute = Math.Max(0, rule.ActionMaxPerMinute);
            if (maxPerMinute > 0)
            {
                if (state.ActionDispatchWindowStart == DateTime.MinValue ||
                    now - state.ActionDispatchWindowStart >= TimeSpan.FromMinutes(1))
                {
                    state.ActionDispatchWindowStart = now;
                    state.ActionDispatchCount = 0;
                }

                if (state.ActionDispatchCount >= maxPerMinute)
                    return false;

                state.ActionDispatchCount++;
            }

            state.LastActionDispatchTime = now;
            return true;
        }

        private void DispatchActions(EdgeRuleRuntimeEvent ruleEvent, IList<EdgeRuleActionConfig> actions, int delaySeconds)
        {
            List<EdgeRuleActionConfig> actionCopies = CloneActions(actions);
            if (actionCopies.Count == 0)
                return;

            EdgeRuleRuntimeEvent eventCopy = CloneEvent(ruleEvent);
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (delaySeconds > 0)
                    Thread.Sleep(TimeSpan.FromSeconds(Math.Min(3600, delaySeconds)));
                ExecuteActions(eventCopy, actionCopies);
            });
        }

        private void ExecuteActions(EdgeRuleRuntimeEvent ruleEvent, IList<EdgeRuleActionConfig> actions)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                EdgeRuleActionConfig action = actions[i];
                if (!ShouldExecuteAction(ruleEvent, action))
                    continue;

                try
                {
                    ExecuteAction(ruleEvent, action);
                }
                catch (Exception ex)
                {
                    RecordActionFailure(ruleEvent, action, ex);
                    IpcLogService.WriteError("Edge rule action failed: " + ruleEvent.RuleName + "/" + action.ActionType, ex);
                }
            }
        }

        private bool ShouldExecuteAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            if (ruleEvent == null || action == null || !action.Enabled)
                return false;

            if (string.Equals(ruleEvent.EventType, "clear", StringComparison.OrdinalIgnoreCase))
                return action.ExecuteOnClear;

            return action.ExecuteOnActive;
        }

        private void ExecuteAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            if (string.Equals(action.ActionType, FlowRuleNodeTypes.MqttPublish, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteMqttAction(ruleEvent, action);
                return;
            }

            if (string.Equals(action.ActionType, FlowRuleNodeTypes.EmailNotify, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteEmailAction(ruleEvent, action);
                return;
            }

            if (string.Equals(action.ActionType, FlowRuleNodeTypes.WebhookCall, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteWebhookAction(ruleEvent, action);
                return;
            }

            if (string.Equals(action.ActionType, FlowRuleNodeTypes.DebugProbe, StringComparison.OrdinalIgnoreCase))
            {
                ExecuteDebugAction(ruleEvent, action);
            }
        }

        private void ExecuteDebugAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            string label = string.IsNullOrWhiteSpace(action.DebugLabel) ? "DebugProbe" : action.DebugLabel.Trim();
            string message = RenderActionTemplate(FirstText(action.ActiveMessage, action.ClearMessage, "{ruleName}:{eventType}:{state}:{value}"), ruleEvent);
            IpcLogService.WriteAudit("FlowRuleDebug", label, message);
        }

        private void ExecuteMqttAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            if (_mqttPublisher == null)
                return;

            string topic = BuildActionTopic(action, ruleEvent);
            if (string.IsNullOrWhiteSpace(topic))
                topic = ruleEvent.Topic;
            _mqttPublisher(topic, ruleEvent.Payload, ClampQos(action.Qos));
        }

        private void ExecuteEmailAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            if (string.IsNullOrWhiteSpace(action.EmailSmtpHost))
                throw new InvalidOperationException("Email SMTP host is empty.");
            if (string.IsNullOrWhiteSpace(action.EmailFrom))
                throw new InvalidOperationException("Email sender is empty.");
            if (string.IsNullOrWhiteSpace(action.EmailTo))
                throw new InvalidOperationException("Email recipient is empty.");

            using (MailMessage message = new MailMessage())
            {
                message.From = new MailAddress(RenderActionTemplate(action.EmailFrom, ruleEvent));
                AddMailAddresses(message.To, RenderActionTemplate(action.EmailTo, ruleEvent));
                AddMailAddresses(message.CC, RenderActionTemplate(action.EmailCc, ruleEvent));
                message.Subject = RenderActionTemplate(FirstText(action.EmailSubjectTemplate, "{ruleName} {state}"), ruleEvent);
                message.Body = RenderActionTemplate(FirstText(action.EmailBodyTemplate, "{message}"), ruleEvent);
                message.IsBodyHtml = false;

                using (SmtpClient client = new SmtpClient(action.EmailSmtpHost, action.EmailSmtpPort <= 0 ? 25 : action.EmailSmtpPort))
                {
                    client.EnableSsl = action.EmailEnableSsl;
                    if (!string.IsNullOrWhiteSpace(action.EmailUsername))
                        client.Credentials = new NetworkCredential(action.EmailUsername, action.EmailPassword ?? string.Empty);
                    client.Send(message);
                }
            }
        }

        private void ExecuteWebhookAction(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            if (string.IsNullOrWhiteSpace(action.WebhookUrl))
                throw new InvalidOperationException("Webhook URL is empty.");

            int attempts = Math.Max(1, action.WebhookRetryCount + 1);
            Exception? lastError = null;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                try
                {
                    ExecuteWebhookOnce(ruleEvent, action);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt + 1 < attempts)
                        Thread.Sleep(Math.Min(2000, 250 * (attempt + 1)));
                }
            }

            if (lastError != null)
                throw lastError;
        }

        private void ExecuteWebhookOnce(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action)
        {
            string method = string.IsNullOrWhiteSpace(action.WebhookMethod) ? "POST" : action.WebhookMethod.Trim().ToUpperInvariant();
            string body = RenderActionTemplate(FirstText(action.WebhookBodyTemplate, ruleEvent.Payload), ruleEvent);
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(1, action.WebhookTimeoutSeconds));
            using CancellationTokenSource timeoutSource = new CancellationTokenSource(timeout);
            using HttpRequestMessage request = new HttpRequestMessage(
                new HttpMethod(method),
                RenderActionTemplate(action.WebhookUrl, ruleEvent));

            if (ShouldWriteWebhookBody(method) && bodyBytes.Length > 0)
            {
                ByteArrayContent content = new ByteArrayContent(bodyBytes);
                content.Headers.TryAddWithoutValidation("Content-Type", FirstText(action.WebhookContentType, "application/json"));
                request.Content = content;
            }

            ApplyWebhookHeaders(request, request.Content, RenderActionTemplate(action.WebhookHeaders, ruleEvent));

            using HttpResponseMessage response = WebhookHttpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            int statusCode = (int)response.StatusCode;
            if (statusCode >= 400)
                throw new InvalidOperationException("Webhook returned HTTP " + statusCode.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddMailAddresses(MailAddressCollection collection, string addresses)
        {
            if (collection == null || string.IsNullOrWhiteSpace(addresses))
                return;

            string[] parts = addresses.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string address = parts[i].Trim();
                if (!string.IsNullOrWhiteSpace(address))
                    collection.Add(address);
            }
        }

        private static void ApplyWebhookHeaders(HttpRequestMessage request, HttpContent? content, string headers)
        {
            if (request == null || string.IsNullOrWhiteSpace(headers))
                return;

            string[] lines = headers.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int index = line.IndexOf(':');
                if (index <= 0)
                    continue;

                string name = line.Substring(0, index).Trim();
                string value = line.Substring(index + 1).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (string.Equals(name, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (content != null)
                    {
                        content.Headers.Remove("Content-Type");
                        content.Headers.TryAddWithoutValidation("Content-Type", value);
                    }
                }
                else if (!request.Headers.TryAddWithoutValidation(name, value) &&
                         (content == null || !content.Headers.TryAddWithoutValidation(name, value)))
                {
                    throw new InvalidOperationException("Invalid webhook header: " + name);
                }
            }
        }

        private static bool ShouldWriteWebhookBody(string method)
        {
            return !string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        }

        private string BuildActionTopic(EdgeRuleActionConfig action, EdgeRuleRuntimeEvent ruleEvent)
        {
            TagValueSnapshot? snapshot = ruleEvent == null ? null : ruleEvent.Snapshot;
            string template = string.IsNullOrWhiteSpace(action.TopicTemplate)
                ? "ipc/rule/{gatewayId}/{pointCode}/{ruleName}"
                : action.TopicTemplate.Trim();

            string topic = template
                .Replace("{gatewayId}", SanitizeTopicSegment(_gatewayOptions.GatewayId))
                .Replace("{ruleName}", SanitizeTopicSegment(ruleEvent == null ? string.Empty : ruleEvent.RuleName))
                .Replace("{pointCode}", SanitizeTopicSegment(snapshot == null ? string.Empty : GetPointCode(snapshot)))
                .Replace("{device}", SanitizeTopicSegment(snapshot == null ? string.Empty : snapshot.DeviceName))
                .Replace("{group}", SanitizeTopicSegment(snapshot == null || string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName))
                .Replace("{tag}", SanitizeTopicSegment(snapshot == null ? string.Empty : snapshot.TagName))
                .Replace("{state}", SanitizeTopicSegment(ruleEvent == null ? string.Empty : ruleEvent.State))
                .Replace("{eventType}", SanitizeTopicSegment(ruleEvent == null ? string.Empty : ruleEvent.EventType));

            while (topic.IndexOf("//", StringComparison.Ordinal) >= 0)
                topic = topic.Replace("//", "/");
            return topic.Trim('/');
        }

        private string RenderActionTemplate(string template, EdgeRuleRuntimeEvent ruleEvent)
        {
            if (ruleEvent == null)
                return NullToEmpty(template);

            TagValueSnapshot snapshot = ruleEvent.Snapshot;
            string text = NullToEmpty(template);
            return text
                .Replace("{gatewayId}", NullToEmpty(_gatewayOptions.GatewayId))
                .Replace("{gatewayName}", NullToEmpty(_gatewayOptions.GatewayName))
                .Replace("{siteName}", NullToEmpty(_gatewayOptions.SiteName))
                .Replace("{ruleId}", NullToEmpty(ruleEvent.RuleId))
                .Replace("{ruleName}", NullToEmpty(ruleEvent.RuleName))
                .Replace("{conditionType}", ruleEvent.ConditionType.ToString())
                .Replace("{eventType}", NullToEmpty(ruleEvent.EventType))
                .Replace("{state}", NullToEmpty(ruleEvent.State))
                .Replace("{message}", NullToEmpty(ruleEvent.Message))
                .Replace("{payload}", NullToEmpty(ruleEvent.Payload))
                .Replace("{topic}", NullToEmpty(ruleEvent.Topic))
                .Replace("{value}", ruleEvent.Value.ToString("R", CultureInfo.InvariantCulture))
                .Replace("{threshold}", ruleEvent.Threshold.ToString("R", CultureInfo.InvariantCulture))
                .Replace("{timestamp}", ruleEvent.Timestamp.ToString("o"))
                .Replace("{pointCode}", snapshot == null ? string.Empty : GetPointCode(snapshot))
                .Replace("{device}", snapshot == null ? string.Empty : NullToEmpty(snapshot.DeviceName))
                .Replace("{group}", snapshot == null ? string.Empty : NullToEmpty(snapshot.GroupName))
                .Replace("{tag}", snapshot == null ? string.Empty : NullToEmpty(snapshot.TagName))
                .Replace("{unit}", snapshot == null ? string.Empty : NullToEmpty(snapshot.Unit))
                .Replace("{quality}", snapshot == null ? string.Empty : snapshot.Quality.ToString())
                .Replace("{assetPath}", snapshot == null ? string.Empty : NullToEmpty(snapshot.AssetPath))
                .Replace("{businessType}", snapshot == null ? string.Empty : NullToEmpty(snapshot.BusinessType));
        }

        private void RecordActionFailure(EdgeRuleRuntimeEvent ruleEvent, EdgeRuleActionConfig action, Exception ex)
        {
            lock (_syncRoot)
            {
                _lastErrorTime = DateTime.Now;
                _lastError = (ruleEvent == null ? string.Empty : ruleEvent.RuleName + ": ") +
                             (action == null ? string.Empty : action.ActionType + ": ") +
                             (ex == null ? string.Empty : ex.Message);
                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(ruleEvent);
                status.LastErrorTime = _lastErrorTime;
                status.LastError = _lastError;
                status.FailedEvaluationCount++;
                _failedEvaluationCount++;
            }
        }

        private static List<EdgeRuleActionConfig> CloneActions(IList<EdgeRuleActionConfig>? source)
        {
            List<EdgeRuleActionConfig> target = new List<EdgeRuleActionConfig>();
            if (source == null)
                return target;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleActionConfig? action = CloneAction(source[i]);
                if (action != null)
                    target.Add(action);
            }

            return target;
        }

        private static EdgeRuleActionConfig? CloneAction(EdgeRuleActionConfig? source)
        {
            if (source == null)
                return null;

            return new EdgeRuleActionConfig
            {
                Id = source.Id,
                ActionType = source.ActionType,
                Enabled = source.Enabled,
                ExecuteOnActive = source.ExecuteOnActive,
                ExecuteOnClear = source.ExecuteOnClear,
                TopicTemplate = source.TopicTemplate,
                Qos = source.Qos,
                ActiveMessage = source.ActiveMessage,
                ClearMessage = source.ClearMessage,
                EmailSmtpHost = source.EmailSmtpHost,
                EmailSmtpPort = source.EmailSmtpPort,
                EmailEnableSsl = source.EmailEnableSsl,
                EmailUsername = source.EmailUsername,
                EmailPassword = source.EmailPassword,
                EmailFrom = source.EmailFrom,
                EmailTo = source.EmailTo,
                EmailCc = source.EmailCc,
                EmailSubjectTemplate = source.EmailSubjectTemplate,
                EmailBodyTemplate = source.EmailBodyTemplate,
                WebhookUrl = source.WebhookUrl,
                WebhookMethod = source.WebhookMethod,
                WebhookHeaders = source.WebhookHeaders,
                WebhookBodyTemplate = source.WebhookBodyTemplate,
                WebhookContentType = source.WebhookContentType,
                WebhookTimeoutSeconds = source.WebhookTimeoutSeconds,
                WebhookRetryCount = source.WebhookRetryCount,
                DebugLabel = source.DebugLabel
            };
        }

        private static string FirstText(params string[] values)
        {
            if (values == null)
                return string.Empty;

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i].Trim();
            }

            return string.Empty;
        }

        private void AddRecentEvent(EdgeRuleRuntimeEvent ruleEvent)
        {
            lock (_syncRoot)
            {
                if (string.Equals(ruleEvent.EventType, "clear", StringComparison.OrdinalIgnoreCase))
                    _clearedCount++;
                else
                    _triggeredCount++;
                _lastEventTime = ruleEvent.Timestamp == DateTime.MinValue ? DateTime.Now : ruleEvent.Timestamp;

                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(ruleEvent);
                status.IsActive = !string.Equals(ruleEvent.EventType, "clear", StringComparison.OrdinalIgnoreCase);
                status.ActiveState = status.IsActive ? ruleEvent.State : string.Empty;
                if (status.IsActive)
                {
                    status.TriggeredCount++;
                    status.LastTriggeredTime = _lastEventTime;
                }
                else
                {
                    status.ClearedCount++;
                    status.LastClearedTime = _lastEventTime;
                }
                status.RecentEvents.Insert(0, CloneEvent(ruleEvent));
                while (status.RecentEvents.Count > 50)
                    status.RecentEvents.RemoveAt(status.RecentEvents.Count - 1);

                _recentEvents.Insert(0, CloneEvent(ruleEvent));
                while (_recentEvents.Count > MaxRecentEvents)
                    _recentEvents.RemoveAt(_recentEvents.Count - 1);
            }
        }

        private void RecordEvaluation(EdgeRuleConfig rule)
        {
            lock (_syncRoot)
            {
                _evaluationCount++;
                _lastEvaluationTime = DateTime.Now;
                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(rule);
                status.EvaluationCount++;
                status.LastEvaluationTime = _lastEvaluationTime;
            }
        }

        private void RecordEvaluationFailure(EdgeRuleConfig rule, Exception ex)
        {
            lock (_syncRoot)
            {
                _failedEvaluationCount++;
                _lastErrorTime = DateTime.Now;
                _lastError = (rule == null ? string.Empty : rule.Name + ": ") + (ex == null ? string.Empty : ex.Message);
                EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(rule);
                status.FailedEvaluationCount++;
                status.LastErrorTime = _lastErrorTime;
                status.LastError = ex == null ? string.Empty : ex.Message;
            }
        }

        private void RecordEngineDegraded(string message)
        {
            lock (_syncRoot)
            {
                _lastErrorTime = DateTime.Now;
                _lastError = message ?? string.Empty;
            }
        }

        private int CountActiveRules()
        {
            int count = 0;
            foreach (EdgeRuleState state in _states.Values)
            {
                if (state != null && !string.IsNullOrWhiteSpace(state.ActiveState))
                    count++;
            }
            return count;
        }

        private EdgeRuleRuntimeRuleStatus BuildRuleStatus(EdgeRuleConfig? rule)
        {
            EdgeRuleRuntimeRuleStatus status = GetOrCreateRuleStatus(rule);
            EdgeRuleState? state = rule == null ? null : GetState(rule);
            status.RuleId = GetRuleId(rule);
            status.RuleName = rule == null ? string.Empty : rule.Name ?? string.Empty;
            status.ConditionType = rule == null ? string.Empty : rule.ConditionType.ToString();
            status.IsActive = state != null && !string.IsNullOrWhiteSpace(state.ActiveState);
            status.ActiveState = status.IsActive && state != null ? state.ActiveState : string.Empty;
            return status;
        }

        private EdgeRuleRuntimeRuleStatus GetOrCreateRuleStatus(EdgeRuleConfig? rule)
        {
            return GetOrCreateRuleStatus(
                GetRuleId(rule),
                rule == null ? string.Empty : rule.Name,
                rule == null ? string.Empty : rule.ConditionType.ToString());
        }

        private EdgeRuleRuntimeRuleStatus GetOrCreateRuleStatus(EdgeRuleRuntimeEvent? ruleEvent)
        {
            return GetOrCreateRuleStatus(
                ruleEvent == null ? string.Empty : ruleEvent.RuleId,
                ruleEvent == null ? string.Empty : ruleEvent.RuleName,
                ruleEvent == null ? string.Empty : ruleEvent.ConditionType.ToString());
        }

        private EdgeRuleRuntimeRuleStatus GetOrCreateRuleStatus(string? ruleId, string? ruleName, string? conditionType)
        {
            string id = string.IsNullOrWhiteSpace(ruleId) ? ruleName ?? string.Empty : ruleId;
            if (string.IsNullOrWhiteSpace(id))
                id = "__unknown__";

            EdgeRuleRuntimeRuleStatus? status;
            if (!_ruleStatuses.TryGetValue(id, out status) || status == null)
            {
                status = new EdgeRuleRuntimeRuleStatus();
                _ruleStatuses[id] = status;
            }

            status.RuleId = string.IsNullOrWhiteSpace(ruleId) ? status.RuleId : ruleId;
            status.RuleName = ruleName ?? string.Empty;
            status.ConditionType = conditionType ?? string.Empty;
            return status;
        }

        private static string GetRuleId(EdgeRuleConfig? rule)
        {
            if (rule == null)
                return string.Empty;
            return string.IsNullOrWhiteSpace(rule.Id) ? rule.Name ?? string.Empty : rule.Id;
        }

        private static bool Matches(EdgeRuleConfig rule, TagValueSnapshot snapshot)
        {
            if ((rule.ConditionType == EdgeRuleConditionType.Combination ||
                 rule.ConditionType == EdgeRuleConditionType.Sequence) &&
                rule.Conditions != null &&
                rule.Conditions.Count > 0)
            {
                for (int i = 0; i < rule.Conditions.Count; i++)
                {
                    EdgeRuleConditionConfig condition = rule.Conditions[i];
                    if (condition != null && MatchesSource(condition.SourcePointCode, condition.SourceDeviceName, condition.SourceGroupName, condition.SourceTagName, snapshot))
                        return true;
                }
            }

            if (rule.ConditionType == EdgeRuleConditionType.Expression &&
                ExpressionReferencesSnapshot(rule.Expression, snapshot))
                return true;

            if (rule.ConditionType == EdgeRuleConditionType.Aggregation)
            {
                if (MatchesSource(rule.RelatedPointCode, rule.RelatedDeviceName, rule.RelatedGroupName, rule.RelatedTagName, snapshot) ||
                    MatchesSource(rule.ContextPointCode, rule.ContextDeviceName, rule.ContextGroupName, rule.ContextTagName, snapshot))
                    return true;

                if (rule.Conditions != null)
                {
                    for (int i = 0; i < rule.Conditions.Count; i++)
                    {
                        EdgeRuleConditionConfig condition = rule.Conditions[i];
                        if (condition != null && MatchesSource(condition.SourcePointCode, condition.SourceDeviceName, condition.SourceGroupName, condition.SourceTagName, snapshot))
                            return true;
                    }
                }
            }

            if (rule.ConditionType == EdgeRuleConditionType.TagRelation &&
                MatchesSource(rule.RelatedPointCode, rule.RelatedDeviceName, rule.RelatedGroupName, rule.RelatedTagName, snapshot))
                return true;

            if (rule.ConditionType == EdgeRuleConditionType.ContextGate &&
                MatchesSource(rule.ContextPointCode, rule.ContextDeviceName, rule.ContextGroupName, rule.ContextTagName, snapshot))
                return true;

            return MatchesSource(rule.SourcePointCode, rule.SourceDeviceName, rule.SourceGroupName, rule.SourceTagName, snapshot);
        }

        private static bool MatchesSource(string sourcePointCode, string sourceDeviceName, string sourceGroupName, string sourceTagName, TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return false;

            bool hasPathScope = HasPathScope(sourceDeviceName, sourceGroupName, sourceTagName);
            if (hasPathScope)
                return MatchesConfiguredSourceFields(sourceDeviceName, sourceGroupName, sourceTagName, snapshot);

            string configuredPointCode = NullToEmpty(sourcePointCode).Trim();
            if (!string.IsNullOrWhiteSpace(configuredPointCode))
            {
                if (string.Equals(configuredPointCode, GetPointCode(snapshot), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(configuredPointCode, NullToEmpty(snapshot.PointCode).Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasPathScope(string deviceName, string groupName, string tagName)
        {
            return !string.IsNullOrWhiteSpace(deviceName) ||
                   !string.IsNullOrWhiteSpace(groupName) ||
                   !string.IsNullOrWhiteSpace(tagName);
        }

        private static bool MatchesConfiguredSourceFields(string sourceDeviceName, string sourceGroupName, string sourceTagName, TagValueSnapshot snapshot)
        {
            return MatchesConfiguredField(sourceDeviceName, snapshot.DeviceName) &&
                   MatchesConfiguredField(sourceGroupName, snapshot.GroupName) &&
                   MatchesConfiguredField(sourceTagName, snapshot.TagName);
        }

        private static bool MatchesConfiguredField(string configured, string actual)
        {
            string expected = NullToEmpty(configured).Trim();
            return string.IsNullOrWhiteSpace(expected) ||
                   string.Equals(expected, NullToEmpty(actual).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasConfiguredSource(string pointCode, string deviceName, string groupName, string tagName)
        {
            return !string.IsNullOrWhiteSpace(pointCode) ||
                   !string.IsNullOrWhiteSpace(deviceName) ||
                   !string.IsNullOrWhiteSpace(groupName) ||
                   !string.IsNullOrWhiteSpace(tagName);
        }

        private static bool ExpressionReferencesSnapshot(string expression, TagValueSnapshot snapshot)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(expression))
                return false;

            string pointCode = GetPointCode(snapshot);
            string path = (NullToEmpty(snapshot.DeviceName).Trim() + "." +
                           NullToEmpty(snapshot.GroupName).Trim() + "." +
                           NullToEmpty(snapshot.TagName).Trim()).Trim('.');

            return Regex.Matches(expression, "\\{([^{}]+)\\}")
                .Cast<Match>()
                .Any(match =>
                {
                    string token = match.Groups[1].Value.Trim();
                    return !string.Equals(token, "value", StringComparison.OrdinalIgnoreCase) &&
                           (string.Equals(token, pointCode, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(token, path, StringComparison.OrdinalIgnoreCase));
                });
        }

        private void RememberSnapshot(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            TagValueSnapshot clone = snapshot.Clone();
            lock (_syncRoot)
            {
                string pointCode = GetPointCode(snapshot);
                if (!string.IsNullOrWhiteSpace(pointCode))
                    _snapshotsByPoint[pointCode] = clone.Clone();
                _snapshotsByPath[BuildPathKey(snapshot.DeviceName, snapshot.GroupName, snapshot.TagName)] = clone;
            }
        }

        private bool TryGetConditionSnapshot(EdgeRuleConditionConfig condition, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            if (condition == null)
                return false;

            return TryGetSnapshotBySource(
                condition.SourcePointCode,
                condition.SourceDeviceName,
                condition.SourceGroupName,
                condition.SourceTagName,
                out snapshot);
        }

        private bool TryGetRuleSnapshot(EdgeRuleConfig rule, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            if (rule == null)
                return false;

            return TryGetSnapshotBySource(
                rule.SourcePointCode,
                rule.SourceDeviceName,
                rule.SourceGroupName,
                rule.SourceTagName,
                out snapshot);
        }

        private bool TryGetRelatedSnapshot(EdgeRuleConfig rule, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            if (rule == null)
                return false;

            return TryGetSnapshotBySource(
                rule.RelatedPointCode,
                rule.RelatedDeviceName,
                rule.RelatedGroupName,
                rule.RelatedTagName,
                out snapshot);
        }

        private bool TryGetContextSnapshot(EdgeRuleConfig rule, TagValueSnapshot triggerSnapshot, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            if (rule == null)
                return false;

            bool hasContextSource =
                !string.IsNullOrWhiteSpace(rule.ContextPointCode) ||
                !string.IsNullOrWhiteSpace(rule.ContextDeviceName) ||
                !string.IsNullOrWhiteSpace(rule.ContextTagName);
            if (!hasContextSource)
            {
                snapshot = triggerSnapshot == null ? new TagValueSnapshot() : triggerSnapshot.Clone();
                return triggerSnapshot != null;
            }

            return TryGetSnapshotBySource(
                rule.ContextPointCode,
                rule.ContextDeviceName,
                rule.ContextGroupName,
                rule.ContextTagName,
                out snapshot);
        }

        private bool TryGetSnapshotBySource(string pointCode, string deviceName, string groupName, string tagName, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            lock (_syncRoot)
            {
                bool hasPathScope = HasPathScope(deviceName, groupName, tagName);
                if (hasPathScope)
                {
                    string pathKey = BuildPathKey(deviceName, groupName, tagName);
                    TagValueSnapshot? pathSnapshot;
                    if (_snapshotsByPath.TryGetValue(pathKey, out pathSnapshot) &&
                        pathSnapshot != null)
                    {
                        snapshot = pathSnapshot.Clone();
                        return true;
                    }

                    TagValueSnapshot? scopedSnapshot = _snapshotsByPath.Values.FirstOrDefault(item =>
                        item != null && MatchesConfiguredSourceFields(deviceName, groupName, tagName, item));
                    if (scopedSnapshot != null)
                    {
                        snapshot = scopedSnapshot.Clone();
                        return true;
                    }

                    return false;
                }

                string normalizedPointCode = NullToEmpty(pointCode).Trim();
                TagValueSnapshot? pointSnapshot;
                if (!string.IsNullOrWhiteSpace(normalizedPointCode) &&
                    _snapshotsByPoint.TryGetValue(normalizedPointCode, out pointSnapshot) &&
                    pointSnapshot != null)
                {
                    snapshot = pointSnapshot.Clone();
                    return true;
                }

                string fallbackPathKey = BuildPathKey(deviceName, groupName, tagName);
                TagValueSnapshot? fallbackPathSnapshot;
                if (_snapshotsByPath.TryGetValue(fallbackPathKey, out fallbackPathSnapshot) &&
                    fallbackPathSnapshot != null)
                {
                    snapshot = fallbackPathSnapshot.Clone();
                    return true;
                }
            }

            return false;
        }

        private static string BuildPathKey(string deviceName, string groupName, string tagName)
        {
            return NullToEmpty(deviceName).Trim() + "\u001F" +
                   NullToEmpty(groupName).Trim() + "\u001F" +
                   NullToEmpty(tagName).Trim();
        }

        private static bool HasQualityPolicy(EdgeRuleConfig rule)
        {
            return rule != null &&
                   (rule.ConditionType == EdgeRuleConditionType.QualityGate ||
                    !string.IsNullOrWhiteSpace(rule.QualityValues));
        }

        private static bool RequiresNumericValue(EdgeRuleConfig rule)
        {
            if (rule == null)
                return true;

            if (rule.ConditionType == EdgeRuleConditionType.ModelInference)
                return string.IsNullOrWhiteSpace(rule.ModelInputTags);

            return rule.ConditionType != EdgeRuleConditionType.QualityGate &&
                   rule.ConditionType != EdgeRuleConditionType.StateMachine &&
                   rule.ConditionType != EdgeRuleConditionType.CycleTime &&
                   rule.ConditionType != EdgeRuleConditionType.ProcessTakt &&
                   rule.ConditionType != EdgeRuleConditionType.ContextGate;
        }

        private static bool EvaluateQuality(EdgeRuleConfig rule, TagValueSnapshot snapshot)
        {
            if (rule == null || snapshot == null)
                return false;

            string configured = string.IsNullOrWhiteSpace(rule.QualityValues) ? "Good" : rule.QualityValues;
            HashSet<string> qualities = new HashSet<string>(
                configured
                    .Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim()),
                StringComparer.OrdinalIgnoreCase);

            if (qualities.Count == 0)
                qualities.Add(TagQuality.Good.ToString());

            bool contains = qualities.Contains(snapshot.Quality.ToString());
            bool exclude = string.Equals(rule.QualityOperator, "NotIn", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(rule.QualityOperator, "Exclude", StringComparison.OrdinalIgnoreCase);
            return exclude ? !contains : contains;
        }

        private static void TrimWindowSamples(EdgeRuleConfig rule, List<EdgeRuleWindowSample> samples, DateTime now)
        {
            int windowSeconds = rule == null ? 60 : Math.Max(1, rule.WindowSeconds);
            int sampleLimit = rule == null ? 0 : Math.Max(0, rule.WindowSampleCount);
            TrimWindowSamples(samples, now, windowSeconds, sampleLimit);
        }

        private static void TrimWindowSamples(List<EdgeRuleWindowSample> samples, DateTime now, int windowSeconds, int sampleLimit)
        {
            if (samples == null)
                return;

            DateTime oldest = now.AddSeconds(-Math.Max(1, windowSeconds));
            samples.RemoveAll(sample => sample.Timestamp < oldest);

            sampleLimit = Math.Max(0, sampleLimit);
            if (sampleLimit <= 0)
                return;

            while (samples.Count > sampleLimit)
                samples.RemoveAt(0);
        }

        private static double CalculateWindowStatistic(string statistic, IList<EdgeRuleWindowSample> samples)
        {
            if (samples == null || samples.Count == 0)
                return 0D;

            return CalculateStatistic(statistic, samples.Select(item => item.Value).ToList());
        }

        private static double CalculateStatistic(string statistic, IList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0D;

            string normalized = NormalizeWindowStatistic(statistic);
            if (string.Equals(normalized, "Count", StringComparison.OrdinalIgnoreCase))
                return values.Count;
            if (string.Equals(normalized, "Min", StringComparison.OrdinalIgnoreCase))
                return values.Min();
            if (string.Equals(normalized, "Max", StringComparison.OrdinalIgnoreCase))
                return values.Max();
            if (string.Equals(normalized, "Sum", StringComparison.OrdinalIgnoreCase))
                return values.Sum();
            if (string.Equals(normalized, "First", StringComparison.OrdinalIgnoreCase))
                return values[0];
            if (string.Equals(normalized, "Last", StringComparison.OrdinalIgnoreCase))
                return values[values.Count - 1];
            if (string.Equals(normalized, "Range", StringComparison.OrdinalIgnoreCase))
                return values.Max() - values.Min();
            if (string.Equals(normalized, "StdDev", StringComparison.OrdinalIgnoreCase))
            {
                double average = values.Average();
                double variance = values.Sum(item => Math.Pow(item - average, 2D)) / values.Count;
                return Math.Sqrt(variance);
            }

            return values.Average();
        }

        private static void AddNumericValue(List<double> values, TagValueSnapshot snapshot, double value)
        {
            if (values == null || snapshot == null || snapshot.Quality != TagQuality.Good)
                return;

            values.Add(value);
        }

        private static void AddNumericValue(List<double> values, TagValueSnapshot snapshot, Func<double, double> projector)
        {
            if (values == null || snapshot == null || snapshot.Quality != TagQuality.Good)
                return;

            double rawValue;
            if (!TryGetNumericValue(snapshot, out rawValue))
                return;

            values.Add(projector == null ? rawValue : projector(rawValue));
        }

        private static string NormalizeWindowStatistic(string statistic)
        {
            string value = NullToEmpty(statistic).Trim();
            if (string.Equals(value, "Avg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Mean", StringComparison.OrdinalIgnoreCase))
                return "Average";
            if (string.IsNullOrWhiteSpace(value))
                return "Average";
            return value;
        }

        private static string NormalizeTrendMode(string mode)
        {
            string value = NullToEmpty(mode).Trim();
            if (string.Equals(value, "Up", StringComparison.OrdinalIgnoreCase))
                return "Rising";
            if (string.Equals(value, "Down", StringComparison.OrdinalIgnoreCase))
                return "Falling";
            if (string.Equals(value, "Flat", StringComparison.OrdinalIgnoreCase))
                return "Stable";
            if (string.IsNullOrWhiteSpace(value))
                return "Slope";
            return value;
        }

        private static string NormalizeAnomalyMode(string mode)
        {
            string value = NullToEmpty(mode).Trim();
            if (string.Equals(value, "StdDev", StringComparison.OrdinalIgnoreCase))
                return "ZScore";
            if (string.IsNullOrWhiteSpace(value))
                return "ZScore";
            return value;
        }

        private static string GetStateMachineValue(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(snapshot.ValueText))
                return snapshot.ValueText.Trim();
            if (snapshot.Value != null)
                return Convert.ToString(snapshot.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Empty;
        }

        private static bool MatchesStateValue(string current, string expected)
        {
            string left = NullToEmpty(current).Trim();
            string right = NullToEmpty(expected).Trim();
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            double leftNumber;
            double rightNumber;
            return double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out leftNumber) &&
                   double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out rightNumber) &&
                   Math.Abs(leftNumber - rightNumber) < 0.000001D;
        }

        private static bool CompareStateValue(string current, string expected, EdgeRuleComparisonOperator op)
        {
            string left = NullToEmpty(current).Trim();
            string right = NullToEmpty(expected).Trim();
            double leftNumber;
            double rightNumber;
            if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out leftNumber) &&
                double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out rightNumber))
            {
                return Compare(leftNumber, op, rightNumber);
            }

            int textCompare = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            switch (op)
            {
                case EdgeRuleComparisonOperator.Equal:
                    return textCompare == 0;
                case EdgeRuleComparisonOperator.NotEqual:
                    return textCompare != 0;
                case EdgeRuleComparisonOperator.GreaterThan:
                    return textCompare > 0;
                case EdgeRuleComparisonOperator.GreaterThanOrEqual:
                    return textCompare >= 0;
                case EdgeRuleComparisonOperator.LessThan:
                    return textCompare < 0;
                case EdgeRuleComparisonOperator.LessThanOrEqual:
                    return textCompare <= 0;
                default:
                    return false;
            }
        }

        private static string StateName(EdgeRuleConfig rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.StateName))
                return "StateMachine";
            return rule.StateName.Trim();
        }

        private static bool Compare(double value, EdgeRuleComparisonOperator op, double compareValue)
        {
            switch (op)
            {
                case EdgeRuleComparisonOperator.GreaterThan:
                    return value > compareValue;
                case EdgeRuleComparisonOperator.GreaterThanOrEqual:
                    return value >= compareValue;
                case EdgeRuleComparisonOperator.LessThan:
                    return value < compareValue;
                case EdgeRuleComparisonOperator.LessThanOrEqual:
                    return value <= compareValue;
                case EdgeRuleComparisonOperator.Equal:
                    return Math.Abs(value - compareValue) < 0.000001D;
                case EdgeRuleComparisonOperator.NotEqual:
                    return Math.Abs(value - compareValue) >= 0.000001D;
                default:
                    return false;
            }
        }

        private static string FormatOperator(EdgeRuleComparisonOperator op)
        {
            switch (op)
            {
                case EdgeRuleComparisonOperator.GreaterThan:
                    return ">";
                case EdgeRuleComparisonOperator.GreaterThanOrEqual:
                    return ">=";
                case EdgeRuleComparisonOperator.LessThan:
                    return "<";
                case EdgeRuleComparisonOperator.LessThanOrEqual:
                    return "<=";
                case EdgeRuleComparisonOperator.Equal:
                    return "==";
                case EdgeRuleComparisonOperator.NotEqual:
                    return "!=";
                default:
                    return "?";
            }
        }

        private bool EvaluateNumericExpression(string expression, TagValueSnapshot snapshot, double value, int timeoutMilliseconds)
        {
            object result = ComputeExpression(string.IsNullOrWhiteSpace(expression) ? "{value} > 0" : expression, snapshot, value, timeoutMilliseconds);
            if (result is bool boolValue)
                return boolValue;
            if (result is byte || result is sbyte || result is short || result is ushort ||
                result is int || result is uint || result is long || result is ulong ||
                result is float || result is double || result is decimal)
                return Convert.ToDouble(result, CultureInfo.InvariantCulture) != 0D;

            bool parsedBool;
            if (bool.TryParse(Convert.ToString(result, CultureInfo.InvariantCulture), out parsedBool))
                return parsedBool;

            double parsedNumber;
            return double.TryParse(Convert.ToString(result, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedNumber) &&
                   parsedNumber != 0D;
        }

        private double EvaluateNumericFormula(string expression, TagValueSnapshot snapshot, double value, int timeoutMilliseconds)
        {
            object result = ComputeExpression(expression, snapshot, value, timeoutMilliseconds);
            if (result is byte || result is sbyte || result is short || result is ushort ||
                result is int || result is uint || result is long || result is ulong ||
                result is float || result is double || result is decimal)
                return Convert.ToDouble(result, CultureInfo.InvariantCulture);

            double parsedNumber;
            if (double.TryParse(Convert.ToString(result, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsedNumber))
                return parsedNumber;

            throw new InvalidOperationException("Transform expression did not return a numeric value.");
        }

        private object ComputeExpression(string expression, TagValueSnapshot snapshot, double value, int timeoutMilliseconds)
        {
            string text = string.IsNullOrWhiteSpace(expression) ? "{value}" : expression.Trim();
            ValidateSandboxSourceExpression(text);
            text = Regex.Replace(text, "\\{([^{}]+)\\}", delegate (Match match)
            {
                string token = match.Groups[1].Value.Trim();
                if (string.Equals(token, "value", StringComparison.OrdinalIgnoreCase))
                    return value.ToString("R", CultureInfo.InvariantCulture);

                TagValueSnapshot tokenSnapshot;
                if (TryGetSnapshotByToken(token, out tokenSnapshot))
                {
                    double tokenValue;
                    if (tokenSnapshot.Quality == TagQuality.Good && TryGetNumericValue(tokenSnapshot, out tokenValue))
                        return tokenValue.ToString("R", CultureInfo.InvariantCulture);
                }

                return "0";
            });

            text = text
                .Replace("&&", " AND ")
                .Replace("||", " OR ")
                .Replace("==", "=")
                .Replace("!=", "<>");

            text = ApplyNumericFunctions(text);
            ValidateSandboxExpression(text);
            return ComputeSandboxedExpression(text, NormalizeExpressionTimeout(timeoutMilliseconds));
        }

        private static object ComputeSandboxedExpression(string text, int timeoutMilliseconds)
        {
            Task<object> task = Task.Factory.StartNew(
                () => new DataTable().Compute(text, string.Empty),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                TaskScheduler.Default);

            try
            {
                if (!task.Wait(TimeSpan.FromMilliseconds(timeoutMilliseconds)))
                    throw new TimeoutException("Expression execution timed out.");
                return task.GetAwaiter().GetResult();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1)
            {
                throw ex.InnerExceptions[0];
            }
        }

        private static void ValidateSandboxExpression(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Expression cannot be empty.");
            if (text.Length > MaxExpressionLength)
                throw new InvalidOperationException("Expression exceeds the sandbox length limit.");
            if (!SafeExpressionCharacters.IsMatch(text))
                throw new InvalidOperationException("Expression contains unsupported characters.");

            for (int i = 0; i < UnsafeExpressionTokens.Length; i++)
            {
                string token = UnsafeExpressionTokens[i];
                if (Regex.IsMatch(text, "\\b" + Regex.Escape(token) + "\\b", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("Expression contains a blocked sandbox token.");
            }
        }

        private static void ValidateSandboxSourceExpression(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Expression cannot be empty.");
            if (text.Length > MaxExpressionLength)
                throw new InvalidOperationException("Expression exceeds the sandbox length limit.");
        }

        private static int NormalizeExpressionTimeout(int timeoutMilliseconds)
        {
            if (timeoutMilliseconds <= 0)
                return DefaultExpressionTimeoutMilliseconds;
            return Math.Min(MaxExpressionTimeoutMilliseconds, timeoutMilliseconds);
        }

        private static string ApplyNumericFunctions(string text)
        {
            string result = text ?? string.Empty;
            string number = "[-+]?(?:\\d+\\.?\\d*|\\.\\d+)(?:[eE][-+]?\\d+)?";
            result = ReplaceUnaryNumericFunction(result, "abs", number, Math.Abs);
            result = ReplaceUnaryNumericFunction(result, "round", number, Math.Round);
            result = ReplaceUnaryNumericFunction(result, "floor", number, Math.Floor);
            result = ReplaceUnaryNumericFunction(result, "ceil", number, Math.Ceiling);
            return result;
        }

        private static string ReplaceUnaryNumericFunction(string text, string name, string numberPattern, Func<double, double> function)
        {
            string pattern = "\\b" + name + "\\s*\\(\\s*(" + numberPattern + ")\\s*\\)";
            return Regex.Replace(text, pattern, delegate (Match match)
            {
                double value;
                if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    return match.Value;
                return function(value).ToString("R", CultureInfo.InvariantCulture);
            }, RegexOptions.IgnoreCase);
        }

        private bool TryGetSnapshotByToken(string token, out TagValueSnapshot snapshot)
        {
            snapshot = new TagValueSnapshot();
            string key = NullToEmpty(token).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            lock (_syncRoot)
            {
                TagValueSnapshot? pointSnapshot;
                if (_snapshotsByPoint.TryGetValue(key, out pointSnapshot) &&
                    pointSnapshot != null)
                {
                    snapshot = pointSnapshot.Clone();
                    return true;
                }

                string[] parts = key.Split('.');
                if (parts.Length >= 2)
                {
                    string device = parts[0];
                    string tag = parts[parts.Length - 1];
                    string group = parts.Length > 2 ? string.Join(".", parts, 1, parts.Length - 2) : string.Empty;
                    TagValueSnapshot? pathSnapshot;
                    if (_snapshotsByPath.TryGetValue(BuildPathKey(device, group, tag), out pathSnapshot) &&
                        pathSnapshot != null)
                    {
                        snapshot = pathSnapshot.Clone();
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryGetNumericValue(TagValueSnapshot snapshot, out double value)
        {
            value = 0D;
            if (snapshot == null)
                return false;

            object current = snapshot.Value;
            if (current is byte || current is sbyte || current is short || current is ushort ||
                current is int || current is uint || current is long || current is ulong ||
                current is float || current is double || current is decimal)
            {
                value = Convert.ToDouble(current, CultureInfo.InvariantCulture);
                return true;
            }

            string text = snapshot.ValueText;
            if (string.IsNullOrWhiteSpace(text) && current != null)
                text = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static string BuildActiveMessage(EdgeRuleConfig rule, TagValueSnapshot snapshot, string state)
        {
            if (!string.IsNullOrWhiteSpace(rule.ActiveMessage))
                return ReplaceTokens(rule.ActiveMessage, rule, snapshot, state);
            return ReplaceTokens("{ruleName} triggered: {pointCode} = {value}", rule, snapshot, state);
        }

        private static string BuildClearMessage(EdgeRuleConfig rule, TagValueSnapshot snapshot, string state)
        {
            if (!string.IsNullOrWhiteSpace(rule.ClearMessage))
                return ReplaceTokens(rule.ClearMessage, rule, snapshot, state);
            return ReplaceTokens("{ruleName} cleared: {pointCode} = {value}", rule, snapshot, state);
        }

        private static string ReplaceTokens(string template, EdgeRuleConfig rule, TagValueSnapshot snapshot, string state)
        {
            return NullToEmpty(template)
                .Replace("{ruleName}", NullToEmpty(rule.Name))
                .Replace("{pointCode}", GetPointCode(snapshot))
                .Replace("{device}", NullToEmpty(snapshot.DeviceName))
                .Replace("{group}", string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName)
                .Replace("{tag}", NullToEmpty(snapshot.TagName))
                .Replace("{value}", NullToEmpty(snapshot.ValueText))
                .Replace("{state}", NullToEmpty(state));
        }

        private string BuildTopic(EdgeRuleConfig rule, TagValueSnapshot snapshot)
        {
            string template = string.IsNullOrWhiteSpace(rule.PublishTopicTemplate)
                ? "ipc/rule/{gatewayId}/{pointCode}/{ruleName}"
                : rule.PublishTopicTemplate.Trim();

            string topic = template
                .Replace("{gatewayId}", SanitizeTopicSegment(_gatewayOptions.GatewayId))
                .Replace("{ruleName}", SanitizeTopicSegment(rule.Name))
                .Replace("{pointCode}", SanitizeTopicSegment(GetPointCode(snapshot)))
                .Replace("{device}", SanitizeTopicSegment(snapshot.DeviceName))
                .Replace("{group}", SanitizeTopicSegment(string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName))
                .Replace("{tag}", SanitizeTopicSegment(snapshot.TagName));

            while (topic.IndexOf("//", StringComparison.Ordinal) >= 0)
                topic = topic.Replace("//", "/");
            return topic.Trim('/');
        }

        private string BuildCloudTopic(EdgeRuleConfig rule, TagValueSnapshot snapshot)
        {
            string topic = "ipc/rule/{gatewayId}/{pointCode}/{ruleName}"
                .Replace("{gatewayId}", SanitizeTopicSegment(_gatewayOptions.GatewayId))
                .Replace("{pointCode}", SanitizeTopicSegment(GetPointCode(snapshot)))
                .Replace("{ruleName}", SanitizeTopicSegment(rule.Name));

            while (topic.IndexOf("//", StringComparison.Ordinal) >= 0)
                topic = topic.Replace("//", "/");
            return topic.Trim('/');
        }

        private string BuildPayload(EdgeRuleRuntimeEvent ruleEvent)
        {
            TagValueSnapshot snapshot = ruleEvent.Snapshot;
            return "{" +
                   "\"messageType\":\"ruleEvent\"," +
                   "\"eventType\":\"" + JsonEscape(ruleEvent.EventType) + "\"," +
                   "\"protocolVersion\":\"" + JsonEscape(_gatewayOptions.CloudProtocolVersion) + "\"," +
                   "\"gatewayId\":\"" + JsonEscape(_gatewayOptions.GatewayId) + "\"," +
                   "\"gatewayName\":\"" + JsonEscape(_gatewayOptions.GatewayName) + "\"," +
                   "\"siteName\":\"" + JsonEscape(_gatewayOptions.SiteName) + "\"," +
                   "\"configVersion\":" + _gatewayOptions.ConfigVersion.ToString(CultureInfo.InvariantCulture) + "," +
                   "\"ruleId\":\"" + JsonEscape(ruleEvent.RuleId) + "\"," +
                   "\"ruleName\":\"" + JsonEscape(ruleEvent.RuleName) + "\"," +
                   "\"conditionType\":\"" + JsonEscape(ruleEvent.ConditionType.ToString()) + "\"," +
                   "\"state\":\"" + JsonEscape(ruleEvent.State) + "\"," +
                   "\"action\":\"" + JsonEscape(ruleEvent.EventType) + "\"," +
                   "\"message\":\"" + JsonEscape(ruleEvent.Message) + "\"," +
                   "\"value\":" + ruleEvent.Value.ToString("R", CultureInfo.InvariantCulture) + "," +
                   "\"threshold\":" + ruleEvent.Threshold.ToString("R", CultureInfo.InvariantCulture) + "," +
                   "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
                   "\"tag\":\"" + JsonEscape(snapshot.TagName) + "\"," +
                   "\"pointCode\":\"" + JsonEscape(GetPointCode(snapshot)) + "\"," +
                   "\"assetPath\":\"" + JsonEscape(snapshot.AssetPath) + "\"," +
                   "\"businessType\":\"" + JsonEscape(snapshot.BusinessType) + "\"," +
                   "\"source\":\"RuleEngine\"," +
                   "\"unit\":\"" + JsonEscape(snapshot.Unit) + "\"," +
                   "\"quality\":\"" + JsonEscape(snapshot.Quality.ToString()) + "\"," +
                   "\"timestamp\":\"" + JsonEscape(ruleEvent.Timestamp.ToString("o")) + "\"," +
                   "\"sourceValues\":" + BuildSourceValuesPayload(ruleEvent.SourceValues) +
                   "}";
        }

        private static string BuildSourceValuesPayload(IList<EdgeRuleRuntimeSourceValue> sourceValues)
        {
            if (sourceValues == null || sourceValues.Count == 0)
                return "[]";

            StringBuilder builder = new StringBuilder();
            builder.Append('[');
            for (int i = 0; i < sourceValues.Count; i++)
            {
                EdgeRuleRuntimeSourceValue sourceValue = sourceValues[i];
                if (sourceValue == null)
                    continue;

                if (builder.Length > 1)
                    builder.Append(',');
                builder.Append(BuildSourceValuePayload(sourceValue));
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static string BuildSourceValuePayload(EdgeRuleRuntimeSourceValue sourceValue)
        {
            TagValueSnapshot snapshot = sourceValue == null || sourceValue.Snapshot == null
                ? new TagValueSnapshot()
                : sourceValue.Snapshot;

            return "{" +
                   "\"role\":\"" + JsonEscape(sourceValue == null ? string.Empty : sourceValue.Role) + "\"," +
                   "\"device\":\"" + JsonEscape(snapshot.DeviceName) + "\"," +
                   "\"group\":\"" + JsonEscape(snapshot.GroupName) + "\"," +
                   "\"tag\":\"" + JsonEscape(snapshot.TagName) + "\"," +
                   "\"pointCode\":\"" + JsonEscape(GetPointCode(snapshot)) + "\"," +
                   "\"value\":" + FormatJsonValue(snapshot.Value) + "," +
                   "\"valueText\":\"" + JsonEscape(snapshot.ValueText) + "\"," +
                   "\"rawValue\":" + FormatJsonValue(snapshot.RawValue) + "," +
                   "\"rawValueText\":\"" + JsonEscape(snapshot.RawValueText) + "\"," +
                   "\"dataType\":\"" + JsonEscape(snapshot.DataType) + "\"," +
                   "\"unit\":\"" + JsonEscape(snapshot.Unit) + "\"," +
                   "\"quality\":\"" + JsonEscape(snapshot.Quality.ToString()) + "\"," +
                   "\"timestamp\":\"" + JsonEscape(snapshot.Timestamp == DateTime.MinValue ? string.Empty : snapshot.Timestamp.ToString("o")) + "\"" +
                   "}";
        }

        private static string FormatJsonValue(object value)
        {
            if (value == null)
                return "null";

            if (value is bool boolean)
                return boolean ? "true" : "false";
            if (value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong)
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
            if (value is float floatValue)
                return float.IsNaN(floatValue) || float.IsInfinity(floatValue)
                    ? "null"
                    : floatValue.ToString("R", CultureInfo.InvariantCulture);
            if (value is double doubleValue)
                return double.IsNaN(doubleValue) || double.IsInfinity(doubleValue)
                    ? "null"
                    : doubleValue.ToString("R", CultureInfo.InvariantCulture);
            if (value is decimal decimalValue)
                return decimalValue.ToString(CultureInfo.InvariantCulture);

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return "\"" + JsonEscape(text) + "\"";
        }

        private static EdgeRuleRuntimeEvent CloneEvent(EdgeRuleRuntimeEvent source)
        {
            if (source == null)
                return new EdgeRuleRuntimeEvent();

            return new EdgeRuleRuntimeEvent
            {
                RuleId = source.RuleId,
                RuleName = source.RuleName,
                ConditionType = source.ConditionType,
                EventType = source.EventType,
                State = source.State,
                Message = source.Message,
                Topic = source.Topic,
                Payload = source.Payload,
                Snapshot = source.Snapshot.Clone(),
                SourceValues = CloneSourceValues(source.SourceValues),
                Value = source.Value,
                Threshold = source.Threshold,
                Timestamp = source.Timestamp
            };
        }

        private static List<EdgeRuleRuntimeSourceValue> CloneSourceValues(IList<EdgeRuleRuntimeSourceValue> source)
        {
            List<EdgeRuleRuntimeSourceValue> clone = new List<EdgeRuleRuntimeSourceValue>();
            if (source == null)
                return clone;

            for (int i = 0; i < source.Count; i++)
            {
                EdgeRuleRuntimeSourceValue sourceValue = source[i];
                if (sourceValue == null)
                    continue;

                clone.Add(new EdgeRuleRuntimeSourceValue
                {
                    Role = sourceValue.Role,
                    Snapshot = sourceValue.Snapshot == null ? new TagValueSnapshot() : sourceValue.Snapshot.Clone()
                });
            }

            return clone;
        }

        private static EdgeRuleRuntimeRuleStatus CloneRuleStatus(EdgeRuleRuntimeRuleStatus source)
        {
            if (source == null)
                return new EdgeRuleRuntimeRuleStatus();

            EdgeRuleRuntimeRuleStatus clone = new EdgeRuleRuntimeRuleStatus
            {
                RuleId = source.RuleId,
                RuleName = source.RuleName,
                ConditionType = source.ConditionType,
                IsActive = source.IsActive,
                ActiveState = source.ActiveState,
                LastEvaluationTime = source.LastEvaluationTime,
                LastTriggeredTime = source.LastTriggeredTime,
                LastClearedTime = source.LastClearedTime,
                LastErrorTime = source.LastErrorTime,
                LastError = source.LastError,
                EvaluationCount = source.EvaluationCount,
                TriggeredCount = source.TriggeredCount,
                ClearedCount = source.ClearedCount,
                FailedEvaluationCount = source.FailedEvaluationCount
            };

            for (int i = 0; i < source.RecentEvents.Count; i++)
                clone.RecentEvents.Add(CloneEvent(source.RecentEvents[i]));
            return clone;
        }

        private static int ClampQos(int qos)
        {
            if (qos < 0)
                return 0;
            if (qos > 1)
                return 1;
            return qos;
        }

        private static string GetPointCode(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(snapshot.PointCode))
                return snapshot.PointCode.Trim();

            string group = string.IsNullOrWhiteSpace(snapshot.GroupName) ? "_" : snapshot.GroupName.Trim();
            return (NullToEmpty(snapshot.DeviceName).Trim() + "." + group + "." + NullToEmpty(snapshot.TagName).Trim()).Trim('.');
        }

        private static string SanitizeTopicSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "_";

            string text = value.Trim().Replace('\\', '/').Replace('+', '_').Replace('#', '_');
            while (text.IndexOf("//", StringComparison.Ordinal) >= 0)
                text = text.Replace("//", "/");
            return text.Trim('/');
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t");
        }

        private static string NullToEmpty(string value)
        {
            return value ?? string.Empty;
        }

        
        
        
        
        
        
        
        
        
        private sealed class EdgeRuleState
        {
            public EdgeRuleState()
            {
                ActiveState = string.Empty;
                PendingState = string.Empty;
                PendingClearState = string.Empty;
                WindowSamples = new List<EdgeRuleWindowSample>();
            }

            public string ActiveState { get; set; }
            public DateTime ActiveSince { get; set; }
            public string PendingState { get; set; }
            public DateTime PendingSince { get; set; }
            public string PendingClearState { get; set; }
            public DateTime PendingClearSince { get; set; }
            public DateTime LastActiveEventTime { get; set; }
            public bool EscalationPublished { get; set; }
            public DateTime LastActionDispatchTime { get; set; }
            public DateTime ActionDispatchWindowStart { get; set; }
            public int ActionDispatchCount { get; set; }
            public bool HasLastValue { get; set; }
            public double LastValue { get; set; }
            public DateTime LastTimestamp { get; set; }
            public int SequenceStepIndex { get; set; }
            public DateTime SequenceStartedTime { get; set; }
            public DateTime SequenceLastStepTime { get; set; }
            public List<EdgeRuleWindowSample> WindowSamples { get; set; }
            public bool StateMachineInExpected { get; set; }
            public DateTime StateMachineEnteredTime { get; set; }
            public bool CycleStarted { get; set; }
            public DateTime CycleStartedTime { get; set; }
        }

        private sealed class EdgeRuleWindowSample
        {
            public EdgeRuleWindowSample(double value, DateTime timestamp)
            {
                Value = value;
                Timestamp = timestamp;
            }

            public double Value { get; private set; }
            public DateTime Timestamp { get; private set; }
        }
    }
}
