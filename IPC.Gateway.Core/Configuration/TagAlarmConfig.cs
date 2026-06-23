/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：TagAlarmConfig
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
    
    
    
    
    
    
    
    
    
    public sealed class TagAlarmConfig
    {
        public TagAlarmConfig()
        {
            Enabled = false;
            LowLimit = 0D;
            HighLimit = 0D;
            LowAlarmMessage = string.Empty;
            HighAlarmMessage = string.Empty;
            WarningDeviation = 0D;
            LowWarningMessage = string.Empty;
            HighWarningMessage = string.Empty;
        }

        public bool Enabled { get; set; }
        public double LowLimit { get; set; }
        public double HighLimit { get; set; }
        public string LowAlarmMessage { get; set; }
        public string HighAlarmMessage { get; set; }
        public double WarningDeviation { get; set; }
        public string LowWarningMessage { get; set; }
        public string HighWarningMessage { get; set; }

        public static TagAlarmConfig Default()
        {
            return new TagAlarmConfig();
        }
    }
}
