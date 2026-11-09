/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：EdgeRuleEngineStatus
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
using IPC.Gateway.Core.Resilience;

namespace IPC.EdgeGateway
{
    public sealed class EdgeRuleEngineStatus
    {
        public EdgeRuleEngineStatus()
        {
            LastError = string.Empty;
            RecentEvents = new List<EdgeRuleRuntimeEvent>();
            Rules = new List<EdgeRuleRuntimeRuleStatus>();
            CircuitBreaker = new CircuitBreakerStatus { Name = "RuleEngine", Enabled = true };
            LastEvaluationTime = DateTime.MinValue;
            LastEventTime = DateTime.MinValue;
            LastErrorTime = DateTime.MinValue;
        }

        public bool IsRunning { get; set; }
        public bool Enabled { get; set; }
        public int RuleCount { get; set; }
        public int EnabledRuleCount { get; set; }
        public int ActiveRuleCount { get; set; }
        public int CachedSnapshotCount { get; set; }
        public int RecentEventCount { get; set; }
        public long EvaluationCount { get; set; }
        public long TriggeredCount { get; set; }
        public long ClearedCount { get; set; }
        public long FailedEvaluationCount { get; set; }
        public long ActionFailureCount { get; set; }
        public int PendingActionCount { get; set; }
        public long DroppedActionCount { get; set; }
        public int PendingInputEventCount { get; set; }
        public int MaxObservedPendingInputEventCount { get; set; }
        public long DroppedInputEventCount { get; set; }
        public DateTime LastEvaluationTime { get; set; }
        public DateTime LastEventTime { get; set; }
        public DateTime LastErrorTime { get; set; }
        public string LastError { get; set; }
        public CircuitBreakerStatus CircuitBreaker { get; set; }
        public IList<EdgeRuleRuntimeEvent> RecentEvents { get; set; }
        public IList<EdgeRuleRuntimeRuleStatus> Rules { get; set; }
    }
}
