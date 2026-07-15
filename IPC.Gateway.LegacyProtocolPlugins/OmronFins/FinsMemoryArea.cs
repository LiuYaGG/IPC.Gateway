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
        public FinsMemoryArea(
            string name,
            byte wordCode,
            byte bitCode,
            int maximumAddress,
            bool supportsWord = true,
            bool supportsBit = true,
            bool bitAddressUsesWordIndex = false)
        {
            Name = name;
            WordCode = wordCode;
            BitCode = bitCode;
            MaximumAddress = maximumAddress;
            SupportsWord = supportsWord;
            SupportsBit = supportsBit;
            BitAddressUsesWordIndex = bitAddressUsesWordIndex;
        }

        public string Name { get; }
        public byte WordCode { get; }
        public byte BitCode { get; }
        public int MaximumAddress { get; }
        public bool SupportsWord { get; }
        public bool SupportsBit { get; }
        public bool BitAddressUsesWordIndex { get; }
    }
}
