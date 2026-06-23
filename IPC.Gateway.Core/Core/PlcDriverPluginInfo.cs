/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Core
* 项目描述 ：
* 类 名 称 ：PlcDriverPluginInfo
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.Core
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
namespace IPC.Plc.Communication.Core
{
    
    
    
    
    
    
    
    
    
    public sealed class PlcDriverPluginInfo
    {
        public PlcDriverPluginInfo()
        {
            DriverId = string.Empty;
            DisplayName = string.Empty;
            AssemblyPath = string.Empty;
            Protocol = string.Empty;
            Version = string.Empty;
            MinGatewayVersion = string.Empty;
            MaxGatewayVersion = string.Empty;
            LoadContextId = string.Empty;
        }

        public string DriverId { get; set; }
        public string DisplayName { get; set; }
        public string Protocol { get; set; }
        public string Version { get; set; }
        public string MinGatewayVersion { get; set; }
        public string MaxGatewayVersion { get; set; }
        public string AssemblyPath { get; set; }
        public string LoadContextId { get; set; }
        public bool BuiltIn { get; set; }
    }
}
