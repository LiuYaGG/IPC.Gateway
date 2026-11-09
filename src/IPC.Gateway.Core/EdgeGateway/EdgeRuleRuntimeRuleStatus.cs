/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：EdgeRuleRuntimeRuleStatus
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

namespace IPC.EdgeGateway
{
    public sealed class EdgeRuleRuntimeRuleStatus
    {
        public EdgeRuleRuntimeRuleStatus()
        {
            RuleId = string.Empty;
            RuleName = string.Empty;
            ConditionType = string.Empty;
            ActiveState = string.Empty;
            LastError = string.Empty;
            LastEvaluationTime = DateTime.MinValue;
            LastTriggeredTime = DateTime.MinValue;
            LastClearedTime = DateTime.MinValue;
            LastErrorTime = DateTime.MinValue;
            RecentEvents = new List<EdgeRuleRuntimeEvent>();
        }

        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string ConditionType { get; set; }
        public bool IsActive { get; set; }
        public string ActiveState { get; set; }
        public DateTime LastEvaluationTime { get; set; }
        public DateTime LastTriggeredTime { get; set; }
        public DateTime LastClearedTime { get; set; }
        public DateTime LastErrorTime { get; set; }
        public string LastError { get; set; }
        public long EvaluationCount { get; set; }
        public long TriggeredCount { get; set; }
        public long ClearedCount { get; set; }
        public long FailedEvaluationCount { get; set; }
        public long ActionFailureCount { get; set; }
        public IList<EdgeRuleRuntimeEvent> RecentEvents { get; set; }
    }
}
