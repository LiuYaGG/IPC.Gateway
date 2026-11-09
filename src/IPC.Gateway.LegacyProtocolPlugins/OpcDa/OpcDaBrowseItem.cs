/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OpcDa
* 项目描述 ：
* 类 名 称 ：OpcDaBrowseItem
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OpcDa
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
namespace IPC.Plc.Communication.OpcDa
{
    
    
    
    
    
    
    
    
    
    
    public sealed class OpcDaBrowseItem
    {
        public string Name { get; set; }
        public string ItemId { get; set; }
        public bool IsLeaf { get; set; }
        public int Level { get; set; }
    }
}
