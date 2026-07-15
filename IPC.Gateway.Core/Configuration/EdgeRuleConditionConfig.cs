/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：EdgeRuleConditionConfig
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
using System;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public sealed class EdgeRuleConditionConfig
    {
        public EdgeRuleConditionConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            SourceChannelId = string.Empty;
            SourceChannelName = string.Empty;
            SourceDeviceId = string.Empty;
            SourceGroupId = string.Empty;
            SourceTagId = string.Empty;
            SourcePointCode = string.Empty;
            SourceDeviceName = string.Empty;
            SourceGroupName = string.Empty;
            SourceTagName = string.Empty;
            SourceDataType = string.Empty;
            Operator = EdgeRuleComparisonOperator.GreaterThan;
            CompareValue = 0D;
            TransformMultiplier = 1D;
            TransformOffset = 0D;
            TransformUseAbsolute = false;
            TransformExpression = string.Empty;
        }

        public string Id { get; set; }
        public string SourceChannelId { get; set; }
        public string SourceChannelName { get; set; }
        public string SourceDeviceId { get; set; }
        public string SourceGroupId { get; set; }
        public string SourceTagId { get; set; }
        public string SourcePointCode { get; set; }
        public string SourceDeviceName { get; set; }
        public string SourceGroupName { get; set; }
        public string SourceTagName { get; set; }
        public string SourceDataType { get; set; }
        public EdgeRuleComparisonOperator Operator { get; set; }
        public double CompareValue { get; set; }
        public double TransformMultiplier { get; set; }
        public double TransformOffset { get; set; }
        public bool TransformUseAbsolute { get; set; }
        public string TransformExpression { get; set; }
    }
}
