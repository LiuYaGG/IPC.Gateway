/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：IRuntimeService
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
using System.Collections.Generic;
using IPC.Runtime.Api;
using IPC.Runtime.Configuration;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    
    
    
    
    
    
    
    
    
    
    public interface IRuntimeService : IDisposable
    {
        event EventHandler<TagValueChangedEventArgs>? TagValueChanged;

        bool IsRunning { get; }
        int MaxConcurrentDevicePolls { get; }

        void Start(ProjectConfig config);
        void Stop();

        bool TryGetSnapshotById(string channelId, string deviceId, string groupId, string tagId, out TagValueSnapshot? snapshot)
        {
            snapshot = null;
            return false;
        }
        IList<TagValueSnapshot> GetSnapshots();
        void RestoreSnapshots(IList<TagValueSnapshot> snapshots);
        IList<DeviceRuntimeStatus> GetDeviceStatuses();
        RuntimeSchedulerStatus GetSchedulerStatus();
        IList<RuntimeErrorDetail> GetRecentErrors(int maxCount);
        ReadTagResponse ReadCached(ReadTagRequest request);
        ReadTagsResponse ReadCached(ReadTagsRequest request);
        ReadTagsResponse QueryCached(ReadTagRequest request);
        ReadTagsResponse ReadTagByDeviceCached(string channelId, string deviceId, string tagId);
        ReadTagsResponse ReadGroupCached(string channelId, string deviceId, string groupId);
        WriteTagResponse WriteTag(WriteTagRequest request);
    }
}
