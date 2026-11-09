using System;
using System.Collections.Generic;
using System.Threading;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    public sealed class TagValueChangedWorker : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly Queue<TagValueSnapshot> _queue = new Queue<TagValueSnapshot>();
        private readonly string _threadName;
        private readonly int _queueLimit;
        private readonly Action<TagValueSnapshot> _handler;
        private readonly Action<Exception>? _errorHandler;
        private Thread? _thread;
        private bool _stopping;
        private int _activeDispatches;
        private long _droppedCount;
        private int _maxObservedPendingCount;
        private bool _disposed;

        public TagValueChangedWorker(string threadName, int queueLimit, Action<TagValueSnapshot> handler, Action<Exception>? errorHandler = null)
        {
            _threadName = string.IsNullOrWhiteSpace(threadName) ? "IPC Tag Value Worker" : threadName.Trim();
            _queueLimit = Math.Max(1, queueLimit);
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            _errorHandler = errorHandler;
        }

        public int PendingCount
        {
            get
            {
                lock (_syncRoot)
                    return _queue.Count;
            }
        }

        public long DroppedCount
        {
            get
            {
                lock (_syncRoot)
                    return _droppedCount;
            }
        }

        public int MaxObservedPendingCount
        {
            get
            {
                lock (_syncRoot)
                    return _maxObservedPendingCount;
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                if (_thread != null && _thread.IsAlive)
                    return;

                _stopping = false;
                _thread = new Thread(ProcessQueue);
                _thread.IsBackground = true;
                _thread.Name = _threadName;
                _thread.Start();
            }
        }

        public void Enqueue(TagValueSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            TagValueSnapshot clone = snapshot.Clone();
            lock (_syncRoot)
            {
                if (_stopping)
                    return;

                if (_queue.Count >= _queueLimit)
                {
                    _queue.Dequeue();
                    _droppedCount++;
                }

                _queue.Enqueue(clone);
                if (_queue.Count > _maxObservedPendingCount)
                    _maxObservedPendingCount = _queue.Count;
                Monitor.Pulse(_syncRoot);
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

        public void Stop(TimeSpan timeout)
        {
            Thread? thread;
            lock (_syncRoot)
            {
                _stopping = true;
                _queue.Clear();
                Monitor.PulseAll(_syncRoot);
                thread = _thread;
            }

            if (thread != null && thread.IsAlive)
                thread.Join(timeout);

            lock (_syncRoot)
            {
                if (ReferenceEquals(_thread, thread) && (thread == null || !thread.IsAlive))
                {
                    _thread = null;
                    _activeDispatches = 0;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop(TimeSpan.FromSeconds(2));
        }

        private void ProcessQueue()
        {
            while (true)
            {
                TagValueSnapshot? snapshot;
                lock (_syncRoot)
                {
                    while (!_stopping && _queue.Count == 0)
                        Monitor.Wait(_syncRoot);

                    if (_stopping)
                        return;

                    snapshot = _queue.Dequeue();
                    _activeDispatches++;
                }

                try
                {
                    _handler(snapshot);
                }
                catch (Exception ex)
                {
                    _errorHandler?.Invoke(ex);
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
    }
}
