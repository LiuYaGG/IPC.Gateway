/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：TagValueChangedEventArgs
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Runtime.Engine
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

namespace IPC.Runtime.Engine
{
    
    
    
    
    
    
    
    
    
    public sealed class TagValueChangedEventArgs : EventArgs
    {
        public TagValueChangedEventArgs(TagValueSnapshot snapshot)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public TagValueSnapshot Snapshot { get; private set; }
    }
}
