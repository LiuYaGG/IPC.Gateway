/*----------------------------------------------------------------
* 项目名称 ：IPC.Plc.Communication.Core
* 项目描述 ：
* 类 名 称 ：IPlcClient
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
using System;

namespace IPC.Plc.Communication.Core
{
    
    
    
    
    
    
    
    
    
    
    public interface IPlcClient : IDisposable
    {
        bool IsConnected { get; }
        PlcProtocol Protocol { get; }
        void Connect();
        void Disconnect();
        PlcReadResult Read(string address, PlcDataType dataType, int elementCount, int elementOffset);
        void Write(string address, PlcDataType dataType, string valueText, int elementOffset);
    }
}
