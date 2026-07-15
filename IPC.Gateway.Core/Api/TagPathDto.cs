/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Api
* 项目描述 ：
* 类 名 称 ：TagPathDto
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
namespace IPC.Runtime.Api
{
    
    
    
    
    
    
    
    
    
    public class TagPathDto
    {
        public TagPathDto()
        {
            ChannelId = string.Empty;
            DeviceId = string.Empty;
            GroupId = string.Empty;
            TagId = string.Empty;
            ChannelName = string.Empty;
            DeviceName = string.Empty;
            GroupName = string.Empty;
            TagName = string.Empty;
        }

        public string ChannelId { get; set; }
        public string DeviceId { get; set; }
        public string GroupId { get; set; }
        public string TagId { get; set; }
        public string ChannelName { get; set; }
        public string DeviceName { get; set; }
        public string GroupName { get; set; }
        public string TagName { get; set; }
    }
}
