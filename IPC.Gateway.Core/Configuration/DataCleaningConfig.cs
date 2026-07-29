/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：DataCleaningConfig
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
using System.Collections.Generic;

namespace IPC.Runtime.Configuration
{
    public sealed class DataCleaningConfig
    {
        public DataCleaningConfig()
        {
            EnumMappings = new List<DataCleaningEnumMappingConfig>();
            SourceUnit = string.Empty;
            TargetUnit = string.Empty;
            UnitMultiplier = 1D;
            PreserveLastGoodOnFilter = true;
            ValueScriptId = string.Empty;
            ValueScriptFailurePolicy = "KeepLastGood";
        }

        public bool Enabled { get; set; }
        public bool OutOfRangeEnabled { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public bool DeadbandEnabled { get; set; }
        public double Deadband { get; set; }
        public bool DuplicateFilterEnabled { get; set; }
        public bool SpikeFilterEnabled { get; set; }
        public double SpikeThreshold { get; set; }
        public int SpikeWindowSeconds { get; set; }
        public bool EnumMappingEnabled { get; set; }
        public List<DataCleaningEnumMappingConfig> EnumMappings { get; set; }
        public bool UnitConversionEnabled { get; set; }
        public string SourceUnit { get; set; }
        public string TargetUnit { get; set; }
        public double UnitMultiplier { get; set; }
        public double UnitOffset { get; set; }
        public bool PreserveLastGoodOnFilter { get; set; }
        public bool ValueScriptEnabled { get; set; }
        public string ValueScriptId { get; set; }
        public int ValueScriptVersion { get; set; }
        public int ValueScriptTimeoutMilliseconds { get; set; } = 100;
        public string ValueScriptFailurePolicy { get; set; }

        public static DataCleaningConfig Default()
        {
            return new DataCleaningConfig();
        }
    }

    public sealed class DataCleaningEnumMappingConfig
    {
        public DataCleaningEnumMappingConfig()
        {
            RawValue = string.Empty;
            CleanValue = string.Empty;
            Description = string.Empty;
        }

        public string RawValue { get; set; }
        public string CleanValue { get; set; }
        public string Description { get; set; }
    }
}
