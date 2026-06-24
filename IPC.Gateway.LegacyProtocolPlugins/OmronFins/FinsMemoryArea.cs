/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.OmronFins
* 项目描述 ：
* 类 名 称 ：FinsMemoryArea
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Plc.Communication.OmronFins
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
namespace IPC.Plc.Communication.OmronFins
{
    
    
    
    
    
    
    
    
    
    internal sealed class FinsMemoryArea
    {
        public FinsMemoryArea(string name, byte wordCode, byte bitCode)
        {
            Name = name;
            WordCode = wordCode;
            BitCode = bitCode;
        }

        public string Name { get; private set; }
        public byte WordCode { get; private set; }
        public byte BitCode { get; private set; }
    }
}
