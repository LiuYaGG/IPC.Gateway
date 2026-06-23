/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：EdgeRuleConditionType
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Configuration
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
namespace IPC.Runtime.Configuration
{
    public enum EdgeRuleConditionType
    {
        Threshold = 0,
        Deadband = 1,
        RateOfChange = 2,
        Condition = 3,
        Combination = 4,
        Hysteresis = 5,
        MultiLevelAlarm = 6,
        Expression = 7,
        Sequence = 8,
        QualityGate = 9,
        SlidingWindow = 10,
        StateMachine = 11,
        CycleTime = 12,
        TagRelation = 13,
        ContextGate = 14,
        Aggregation = 15,
        WindowCalculation = 16,
        Trend = 17,
        ProcessTakt = 18,
        AnomalyDetection = 19,
        ModelInference = 20
    }
}
