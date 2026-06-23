/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Api
* 项目描述 ：
* 类 名 称 ：WriteTagResponse
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Api
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
using IPC.Runtime.Values;

namespace IPC.Runtime.Api
{
    
    
    
    
    
    
    
    
    
    public sealed class WriteTagResponse
    {
        public WriteTagResponse()
        {
            DeviceName = string.Empty;
            GroupName = string.Empty;
            TagName = string.Empty;
            DataType = string.Empty;
            Quality = TagQuality.Unknown.ToString();
            Timestamp = DateTime.MinValue;
            ErrorMessage = string.Empty;
            CurrentValue = new ReadTagResponse();
        }

        public bool Success { get; set; }
        public string DeviceName { get; set; }
        public string GroupName { get; set; }
        public string TagName { get; set; }
        public string DataType { get; set; }
        public string Quality { get; set; }
        public DateTime Timestamp { get; set; }
        public string ErrorMessage { get; set; }
        public ReadTagResponse CurrentValue { get; set; }
    }
}
