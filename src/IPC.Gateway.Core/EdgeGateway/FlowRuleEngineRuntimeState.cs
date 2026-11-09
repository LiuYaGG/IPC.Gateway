using System;
using System.Collections.Generic;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    public sealed class FlowRuleEngineRuntimeState
    {
        public IList<FlowRuleRuntimeStateEntry> Rules { get; set; } = new List<FlowRuleRuntimeStateEntry>();
        public IList<EdgeRuleRuntimeEvent> RecentEvents { get; set; } = new List<EdgeRuleRuntimeEvent>();
        public IList<EdgeRuleRuntimeRuleStatus> RuleStatuses { get; set; } = new List<EdgeRuleRuntimeRuleStatus>();
        public long EvaluationCount { get; set; }
        public long TriggeredCount { get; set; }
        public long ClearedCount { get; set; }
        public long FailedEvaluationCount { get; set; }
        public long ActionFailureCount { get; set; }
        public DateTime LastEvaluationTime { get; set; }
        public DateTime LastEventTime { get; set; }
        public DateTime LastErrorTime { get; set; }
        public string LastError { get; set; } = string.Empty;
    }

    public sealed class FlowRuleRuntimeStateEntry
    {
        public string RuleId { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string ActiveState { get; set; } = string.Empty;
        public string ActiveSeverity { get; set; } = string.Empty;
        public string ActiveMessageOverride { get; set; } = string.Empty;
        public DateTime ActiveSince { get; set; }
        public string PendingState { get; set; } = string.Empty;
        public DateTime PendingSince { get; set; }
        public string PendingClearState { get; set; } = string.Empty;
        public DateTime PendingClearSince { get; set; }
        public TagValueSnapshot? LastEvaluationSnapshot { get; set; }
        public double LastEvaluationValue { get; set; }
        public double LastEvaluationThreshold { get; set; }
        public DateTime LastPeriodicEvaluationTime { get; set; }
        public DateTime LastActiveEventTime { get; set; }
        public bool EscalationPublished { get; set; }
        public DateTime LastActiveActionDispatchTime { get; set; }
        public DateTime ActiveActionDispatchWindowStart { get; set; }
        public int ActiveActionDispatchCount { get; set; }
        public DateTime LastClearActionDispatchTime { get; set; }
        public DateTime ClearActionDispatchWindowStart { get; set; }
        public int ClearActionDispatchCount { get; set; }
        public bool HasLastValue { get; set; }
        public double LastValue { get; set; }
        public DateTime LastTimestamp { get; set; }
        public int SequenceStepIndex { get; set; }
        public DateTime SequenceStartedTime { get; set; }
        public DateTime SequenceLastStepTime { get; set; }
        public IList<FlowRuleWindowSampleState> WindowSamples { get; set; } = new List<FlowRuleWindowSampleState>();
        public bool StateMachineInExpected { get; set; }
        public DateTime StateMachineEnteredTime { get; set; }
        public bool CycleStarted { get; set; }
        public DateTime CycleStartedTime { get; set; }
    }

    public sealed class FlowRuleWindowSampleState
    {
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
