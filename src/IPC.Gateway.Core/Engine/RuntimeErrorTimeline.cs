/*----------------------------------------------------------------
* 项目名称 ：IPC.Runtime.Engine
* 项目描述 ：
* 类 名 称 ：RuntimeErrorTimeline
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
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine;

public sealed class RuntimeErrorTimeline
{
    private readonly object _syncRoot = new object();
    private readonly List<RuntimeErrorDetail> _events = new List<RuntimeErrorDetail>();
    private readonly int _capacity;

    public RuntimeErrorTimeline(int capacity)
    {
        _capacity = capacity <= 0 ? 100 : capacity;
    }

    public void Add(RuntimeErrorDetail detail)
    {
        if (detail == null || string.IsNullOrWhiteSpace(detail.Message))
            return;

        RuntimeErrorDetail copy = detail.Clone();
        if (copy.Timestamp == DateTime.MinValue)
            copy.Timestamp = DateTime.Now;

        lock (_syncRoot)
        {
            _events.Add(copy);
            TrimNoLock();
        }
    }

    public IList<RuntimeErrorDetail> GetRecent(int maxCount)
    {
        int take = maxCount <= 0 ? _capacity : maxCount;
        lock (_syncRoot)
        {
            return _events
                .OrderByDescending(item => item.Timestamp)
                .Take(take)
                .Select(item => item.Clone())
                .ToList();
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
            _events.Clear();
    }

    private void TrimNoLock()
    {
        if (_events.Count <= _capacity)
            return;

        List<RuntimeErrorDetail> keep = _events
            .OrderByDescending(item => item.Timestamp)
            .Take(_capacity)
            .ToList();

        _events.Clear();
        _events.AddRange(keep);
    }
}
