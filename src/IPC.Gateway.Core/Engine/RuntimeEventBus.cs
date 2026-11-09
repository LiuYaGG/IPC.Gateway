using System;
using System.Collections.Generic;
using System.Threading;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeEventBus
    {
        private readonly object _syncRoot = new object();
        private readonly Queue<TagValueChangedDispatchItem> _queue = new Queue<TagValueChangedDispatchItem>();
        private readonly Action<TagValueSnapshot> _dispatch;
        private readonly Func<int, bool> _isCurrentGeneration;
        private readonly int _queueLimit;
        private Thread? _dispatcherThread;
        private bool _stopping;
        private int _activeDispatches;
        private long _queued;
        private long _dispatched;
        private long _dropped;
        private int _maxObservedPendingCount;

        public RuntimeEventBus(int queueLimit, Action<TagValueSnapshot> dispatch, Func<int, bool> isCurrentGeneration)
        {
            _queueLimit = Math.Max(1, queueLimit);
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _isCurrentGeneration = isCurrentGeneration ?? throw new ArgumentNullException(nameof(isCurrentGeneration));
        }

        public RuntimeEventBusStats GetStats()
        {
            int pending;
            lock (_syncRoot)
                pending = _queue.Count;

            return new RuntimeEventBusStats
            {
                PendingCount = pending,
                QueueLimit = _queueLimit,
                MaxObservedPendingCount = Volatile.Read(ref _maxObservedPendingCount),
                TotalQueued = Interlocked.Read(ref _queued),
                TotalDispatched = Interlocked.Read(ref _dispatched),
                TotalDropped = Interlocked.Read(ref _dropped)
            };
        }

        public void Publish(TagValueSnapshot snapshot, int runtimeGeneration)
        {
            if (snapshot == null)
                return;

            lock (_syncRoot)
            {
                if (_stopping)
                    return;

                if (_queue.Count >= _queueLimit)
                {
                    _queue.Dequeue();
                    Interlocked.Increment(ref _dropped);
                }

                _queue.Enqueue(new TagValueChangedDispatchItem(snapshot, runtimeGeneration));
                int pendingCount = _queue.Count;
                if (pendingCount > _maxObservedPendingCount)
                    _maxObservedPendingCount = pendingCount;
                Interlocked.Increment(ref _queued);
                Monitor.Pulse(_syncRoot);
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_dispatcherThread != null)
                    return;

                _stopping = false;
                _dispatcherThread = new Thread(ProcessQueue);
                _dispatcherThread.IsBackground = true;
                _dispatcherThread.Name = "IPC Runtime EventBus Dispatcher";
                _dispatcherThread.Start();
            }
        }

        public void Stop()
        {
            Thread? thread;
            lock (_syncRoot)
            {
                _stopping = true;
                _queue.Clear();
                Monitor.PulseAll(_syncRoot);
                thread = _dispatcherThread;
            }

            if (thread != null && thread.IsAlive)
                thread.Join(TimeSpan.FromSeconds(2));

            lock (_syncRoot)
            {
                if (ReferenceEquals(_dispatcherThread, thread))
                    _dispatcherThread = null;
                _activeDispatches = 0;
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _queue.Clear();
                Monitor.PulseAll(_syncRoot);
            }
        }

        public bool Drain(TimeSpan timeout)
        {
            DateTime deadlineUtc = DateTime.UtcNow.Add(timeout);
            lock (_syncRoot)
            {
                while (_queue.Count > 0 || _activeDispatches > 0)
                {
                    TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        return false;

                    Monitor.Wait(_syncRoot, remaining);
                }

                return true;
            }
        }

        public void ResetStats()
        {
            Interlocked.Exchange(ref _queued, 0);
            Interlocked.Exchange(ref _dispatched, 0);
            Interlocked.Exchange(ref _dropped, 0);
            Volatile.Write(ref _maxObservedPendingCount, 0);
        }

        private void ProcessQueue()
        {
            while (true)
            {
                TagValueChangedDispatchItem? item = null;
                lock (_syncRoot)
                {
                    while (!_stopping && _queue.Count == 0)
                        Monitor.Wait(_syncRoot);

                    if (_stopping)
                        return;

                    item = _queue.Dequeue();
                    _activeDispatches++;
                }

                try
                {
                    if (_isCurrentGeneration(item.RuntimeGeneration))
                    {
                        _dispatch(item.Snapshot);
                        Interlocked.Increment(ref _dispatched);
                    }
                }
                finally
                {
                    lock (_syncRoot)
                    {
                        _activeDispatches--;
                        if (_queue.Count == 0 && _activeDispatches == 0)
                            Monitor.PulseAll(_syncRoot);
                    }
                }
            }
        }

        private sealed class TagValueChangedDispatchItem
        {
            public TagValueChangedDispatchItem(TagValueSnapshot snapshot, int runtimeGeneration)
            {
                Snapshot = snapshot;
                RuntimeGeneration = runtimeGeneration;
            }

            public TagValueSnapshot Snapshot { get; private set; }
            public int RuntimeGeneration { get; private set; }
        }
    }

    internal sealed class RuntimeEventBusStats
    {
        public int PendingCount { get; set; }
        public int QueueLimit { get; set; }
        public int MaxObservedPendingCount { get; set; }
        public long TotalQueued { get; set; }
        public long TotalDispatched { get; set; }
        public long TotalDropped { get; set; }
    }
}
