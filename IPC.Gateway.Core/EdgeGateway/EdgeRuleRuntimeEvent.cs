/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：EdgeRuleRuntimeEvent
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
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class EdgeRuleRuntimeEvent
    {
        public EdgeRuleRuntimeEvent()
        {
            RuleId = string.Empty;
            RuleName = string.Empty;
            ConditionType = EdgeRuleConditionType.Threshold;
            EventType = string.Empty;
            State = string.Empty;
            Message = string.Empty;
            Topic = string.Empty;
            Payload = string.Empty;
            Snapshot = new TagValueSnapshot();
            SourceValues = new List<EdgeRuleRuntimeSourceValue>();
            Value = 0D;
            Threshold = 0D;
            Timestamp = DateTime.Now;
        }

        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public EdgeRuleConditionType ConditionType { get; set; }
        public string EventType { get; set; }
        public string State { get; set; }
        public string Message { get; set; }
        public string Topic { get; set; }
        public string Payload { get; set; }
        public TagValueSnapshot Snapshot { get; set; }
        public List<EdgeRuleRuntimeSourceValue> SourceValues { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public sealed class EdgeRuleRuntimeSourceValue
    {
        public EdgeRuleRuntimeSourceValue()
        {
            Role = string.Empty;
            Snapshot = new TagValueSnapshot();
        }

        public string Role { get; set; }
        public TagValueSnapshot Snapshot { get; set; }
    }
}
