/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Configuration
* 项目描述 ：
* 类 名 称 ：ScalingConfig
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
    
    
    
    
    
    
    
    
    
    public sealed class ScalingConfig
    {
        public bool Enabled { get; set; }
        public double Multiplier { get; set; }
        public double Offset { get; set; }
        public bool ClampEnabled { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public int DecimalPlaces { get; set; }
        

        public static ScalingConfig Default()
        {
            return new ScalingConfig
            {
                Enabled = false,
                Multiplier = 1D,
                Offset = 0D,
                ClampEnabled = false,
                MinValue = 0D,
                MaxValue = 0D,
                DecimalPlaces = 2
            };
        }
    }
}
