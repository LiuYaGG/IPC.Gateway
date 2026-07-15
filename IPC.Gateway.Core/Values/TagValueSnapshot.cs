/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Values
* 项目描述 ：
* 类 名 称 ：TagValueSnapshot
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Values
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
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Values
{
    
    
    
    
    
    
    
    
    
    public sealed class TagValueSnapshot
    {
        public TagValueSnapshot()
        {
            ChannelId = string.Empty;
            ChannelName = string.Empty;
            DeviceId = string.Empty;
            DeviceProtocol = string.Empty;
            GroupId = string.Empty;
            TagId = string.Empty;
            DeviceName = string.Empty;
            GroupName = string.Empty;
            TagName = string.Empty;
            RawValue = string.Empty;
            RawValueText = string.Empty;
            Value = string.Empty;
            ValueText = string.Empty;
            Unit = string.Empty;
            PointCode = string.Empty;
            AssetPath = string.Empty;
            BusinessType = string.Empty;
            Source = string.Empty;
            Precision = -1;
            DataType = string.Empty;
            Alarm = TagAlarmConfig.Default();
            CleaningAction = string.Empty;
            CleaningMessage = string.Empty;
            Quality = TagQuality.Unknown;
            Timestamp = DateTime.MinValue;
            ErrorMessage = string.Empty;
            TagState = "Unknown";
        }

        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public string DeviceId { get; set; }
        public string DeviceProtocol { get; set; }
        public string GroupId { get; set; }
        public string TagId { get; set; }
        public string DeviceName { get; set; }
        public string GroupName { get; set; }
        public string TagName { get; set; }
        public object RawValue { get; set; }
        public string RawValueText { get; set; }
        public object Value { get; set; }
        public string ValueText { get; set; }
        public string Unit { get; set; }
        public string PointCode { get; set; }
        public string AssetPath { get; set; }
        public string BusinessType { get; set; }
        public string Source { get; set; }
        public int Precision { get; set; }
        public string DataType { get; set; }
        public bool MqttPublishEnabled { get; set; }
        public TagAlarmConfig Alarm { get; set; }
        public bool CleaningApplied { get; set; }
        public string CleaningAction { get; set; }
        public string CleaningMessage { get; set; }
        public TagQuality Quality { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }
        public string TagState { get; set; }
        public bool IsTagIsolated { get; set; }
        public bool IsStaticValidationError { get; set; }
        public int TagConsecutiveFailures { get; set; }
        public DateTime NextTagRecoveryProbeTime { get; set; }

        public TagValueSnapshot Clone()
        {
            TagValueSnapshot snapshot = (TagValueSnapshot)MemberwiseClone();
            if (Alarm != null)
            {
                snapshot.Alarm = new TagAlarmConfig
                {
                    Enabled = Alarm.Enabled,
                    LowLimit = Alarm.LowLimit,
                    HighLimit = Alarm.HighLimit,
                    LowAlarmMessage = Alarm.LowAlarmMessage,
                    HighAlarmMessage = Alarm.HighAlarmMessage,
                    WarningDeviation = Alarm.WarningDeviation,
                    LowWarningMessage = Alarm.LowWarningMessage,
                    HighWarningMessage = Alarm.HighWarningMessage
                };
            }
            return snapshot;
        }
    }
}
