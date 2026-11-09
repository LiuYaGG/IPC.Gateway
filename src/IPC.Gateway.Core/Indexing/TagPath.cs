/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Indexing
* 项目描述 ：
* 类 名 称 ：TagPath
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Indexing
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

namespace IPC.Runtime.Indexing
{
    
    
    
    
    
    
    
    
    
    public static class TagPath
    {
        public static string BuildIdentity(string channelId, string deviceId, string groupId, string tagId)
        {
            return Normalize(channelId) + "/" + Normalize(deviceId) + "/" + Normalize(groupId) + "/" + Normalize(tagId);
        }

        public static string Normalize(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Trim().ToUpperInvariant();
        }

        public static void ValidateName(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(fieldName + " cannot be empty.", fieldName);
            if (value.IndexOf('/') >= 0)
                throw new ArgumentException(fieldName + " cannot contain '/'.", fieldName);
        }
    }
}
