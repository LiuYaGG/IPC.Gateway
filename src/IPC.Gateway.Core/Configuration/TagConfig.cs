/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：TagConfig
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
using IPC.Plc.Communication.Core;

namespace IPC.Runtime.Configuration
{
    
    
    
    
    
    
    
    
    
    public sealed class TagConfig
    {
        public TagConfig()
        {
            Id = Guid.NewGuid().ToString("N");
            DeviceId = string.Empty;
            GroupId = string.Empty;
            Name = "Tag";
            Address = string.Empty;
            MeterAddress = string.Empty;
            MeterDataIdentifier = string.Empty;
            MeterType = string.Empty;
            DataType = PlcDataType.Int16;
            ElementCount = 1;
            ElementOffset = 0;
            Enabled = true;
            MqttPublishEnabled = false;
            AccessMode = TagAccessMode.ReadWrite;
            ScanRateMs = 0;
            FailureRetryDelayMs = 0;
            Unit = string.Empty;
            PointCode = string.Empty;
            AssetPath = string.Empty;
            BusinessType = string.Empty;
            Source = string.Empty;
            Precision = -1;
            Scaling = ScalingConfig.Default();
            Cleaning = DataCleaningConfig.Default();
            Alarm = TagAlarmConfig.Default();
            Description = string.Empty;
            VirtualModel = new VirtualModelTagConfig();
        }

        public string Id { get; set; }
        public string DeviceId { get; set; }
        public string GroupId { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string MeterAddress { get; set; }
        public string MeterDataIdentifier { get; set; }
        public string MeterType { get; set; }
        public PlcDataType DataType { get; set; }
        public int ElementCount { get; set; }
        public int ElementOffset { get; set; }
        public bool Enabled { get; set; }
        public bool MqttPublishEnabled { get; set; }
        public TagAccessMode AccessMode { get; set; }
        public int ScanRateMs { get; set; }
        public int FailureRetryDelayMs { get; set; }
        public string Unit { get; set; }
        public string PointCode { get; set; }
        public string AssetPath { get; set; }
        public string BusinessType { get; set; }
        public string Source { get; set; }
        public int Precision { get; set; }
        public ScalingConfig Scaling { get; set; }
        public DataCleaningConfig Cleaning { get; set; }
        public TagAlarmConfig Alarm { get; set; }
        public string Description { get; set; }
        public bool IsVirtual { get; set; }
        public VirtualModelTagConfig VirtualModel { get; set; }
    }
}
