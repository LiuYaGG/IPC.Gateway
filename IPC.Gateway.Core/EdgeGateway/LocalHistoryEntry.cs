/*----------------------------------------------------------------
* 项目名称 ：IPC.EdgeGateway
* 项目描述 ：
* 类 名 称 ：LocalHistoryEntry
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.EdgeGateway
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

namespace IPC.EdgeGateway
{
    
    
    
    
    
    
    
    
    
    public sealed class LocalHistoryEntry
    {
        public LocalHistoryEntry()
        {
            Timestamp = DateTime.MinValue;
            Type = string.Empty;
            Source = string.Empty;
            Summary = string.Empty;
            Detail = string.Empty;
        }

        public DateTime Timestamp { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
        public string Summary { get; set; }
        public string Detail { get; set; }
    }
}
