/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Api
* 项目描述 ：
* 类 名 称 ：WriteTagRequest
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
    
    
    
    
    
    
    
    
    
    public sealed class WriteTagRequest : TagPathDto
    {
        public WriteTagRequest()
        {
            DataType = string.Empty;
            Value = string.Empty;
            ValueText = string.Empty;
        }

        public string DataType { get; set; }
        public object Value { get; set; }
        public string ValueText { get; set; }
    }
}
