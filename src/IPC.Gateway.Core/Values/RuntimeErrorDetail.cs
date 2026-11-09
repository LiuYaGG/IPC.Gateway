/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Values
* 项目描述 ：
* 类 名 称 ：RuntimeErrorDetail
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Values
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

namespace IPC.Runtime.Values
{
    
    
    
    
    
    
    
    
    
    public sealed class RuntimeErrorDetail
    {
        public RuntimeErrorDetail()
        {
            Category = string.Empty;
            ChannelId = string.Empty;
            ChannelName = string.Empty;
            DeviceId = string.Empty;
            DeviceName = string.Empty;
            GroupId = string.Empty;
            GroupName = string.Empty;
            TagId = string.Empty;
            TagName = string.Empty;
            Message = string.Empty;
            Suggestion = string.Empty;
            Source = string.Empty;
            Timestamp = DateTime.MinValue;
        }

        public string Category { get; set; }
        public string ChannelId { get; set; }
        public string ChannelName { get; set; }
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string GroupId { get; set; }
        public string GroupName { get; set; }
        public string TagId { get; set; }
        public string TagName { get; set; }
        public string Message { get; set; }
        public string Suggestion { get; set; }
        public string Source { get; set; }
        public DateTime Timestamp { get; set; }

        public RuntimeErrorDetail Clone()
        {
            return (RuntimeErrorDetail)MemberwiseClone();
        }
    }
}
