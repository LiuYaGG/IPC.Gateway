using IPC.Gateway.Core.Resilience;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway;

public sealed record RuleReplayActionPreview(string Type, string? Topic, string? Payload, int DelaySeconds);

public sealed partial class EdgeRuleEngineService
{
    private Action<EdgeRuleRuntimeEvent, IReadOnlyList<RuleReplayActionPreview>>? _replayOutput;
    private DateTime _replayTime;
    private DateTime RuleTime => _replayOutput == null ? DateTime.Now : _replayTime;

    public static EdgeRuleEngineService CreateReplay(EdgeRuleConfig rule, DateTime startUtc,
        bool english, Action<EdgeRuleRuntimeEvent, IReadOnlyList<RuleReplayActionPreview>> output, MqttGatewayOptions? mqttPreviewOptions = null)
    {
        var language = new GatewayMessageLanguage();
        language.SetLocale(english ? "en-US" : "zh-CN");
        var engine = new EdgeRuleEngineService(null!, new ProjectConfig { Rules = [rule] }, (_, _, _) => false,
            mqttPreviewOptions ?? new MqttGatewayOptions(), new CircuitBreakerOptions { Enabled = false }, messageLanguage: language)
        {
            _replayTime = startUtc,
            _replayOutput = output,
            _running = true
        };
        return engine;
    }

    public bool ReplayNeedsTimer => _timerRules.Count != 0;

    public void AdvanceReplay(DateTime utc, TagValueSnapshot? sample, bool tick)
    {
        if (_replayOutput == null) throw new InvalidOperationException("The rule engine is not running a replay.");
        _replayTime = utc;
        if (sample != null) ProcessSnapshot(sample);
        if (tick) RunRuleTimerTick();
    }

    private void CaptureReplayOutput(EdgeRuleConfig rule, EdgeRuleState state, EdgeRuleRuntimeEvent ruleEvent, bool publishCustomEvent)
    {
        var previews = new List<RuleReplayActionPreview>();
        if (ruleEvent.EventType != "value" && GetAlarmHistoryPolicy(rule).Record)
            previews.Add(new("AlarmRecord", null, null, 0));
        if (rule.Actions.Count > 0)
        {
            bool dispatch = CanDispatchActions(rule, state, ruleEvent.Timestamp, ruleEvent.EventType);
            foreach (var action in rule.Actions)
            {
                if (!dispatch || !ShouldExecuteAction(ruleEvent, action)) continue;
                string? topic = null;
                string? payload = null;
                if (string.Equals(action.ActionType, FlowRuleNodeTypes.MqttPublish, StringComparison.OrdinalIgnoreCase))
                {
                    topic = FirstText(BuildActionTopic(action, ruleEvent), ruleEvent.Topic);
                    var template = ruleEvent.EventType == "clear" ? action.ClearMessage : action.ActiveMessage;
                    payload = BuildPayload(ruleEvent, compactMqtt: true, messageTemplate: template);
                }
                previews.Add(new(action.ActionType, topic, payload, Math.Clamp(rule.ActionDelaySeconds, 0, 3600)));
            }
        }
        else if (publishCustomEvent)
            previews.Add(new(FlowRuleNodeTypes.MqttPublish, ruleEvent.Topic, BuildPayload(ruleEvent, compactMqtt: true), 0));

        _replayOutput!(ruleEvent, previews);
    }
}
