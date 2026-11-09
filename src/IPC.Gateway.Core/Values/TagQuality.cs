/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Values
* 项目描述 ：
* 类 名 称 ：TagQuality
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
namespace IPC.Runtime.Values
{
    public enum TagQuality
    {
        Unknown = 0,
        Good = 1,
        Bad = 2,
        Disabled = 3,
        NotConnected = 4,
        ReadError = 5,
        NotFound = 6,
        AccessDenied = 7,
        OutOfRange = 8,
        Filtered = 9,
        Spike = 10
    }
}
