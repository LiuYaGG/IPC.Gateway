using System;
using System.Collections.Generic;
using System.Threading;

namespace IPC.EdgeGateway
{
    internal sealed class RuleActionExecutor : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly PriorityQueue<WorkItem, long> _queue = new PriorityQueue<WorkItem, long>();
        private readonly Thread[] _workers;
        private readonly int _capacity;
        private readonly Action<Exception>? _errorHandler;
        private bool _stopping;
        private bool _disposed;
        private long _droppedCount;

        public RuleActionExecutor(string threadName, int capacity, int workerCount, Action<Exception>? errorHandler = null)
        {
            _capacity = Math.Max(1, capacity);
            _errorHandler = errorHandler;
            _workers = new Thread[Math.Max(1, workerCount)];
            string name = string.IsNullOrWhiteSpace(threadName) ? "IPC Rule Action" : threadName.Trim();

            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i] = new Thread(ProcessQueue)
                {
                    IsBackground = true,
                    Name = name + " " + (i + 1).ToString()
                };
                _workers[i].Start();
            }
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

        public bool TryEnqueue(Action action, TimeSpan delay)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            DateTime dueUtc = DateTime.UtcNow.Add(delay < TimeSpan.Zero ? TimeSpan.Zero : delay);
            lock (_syncRoot)
            {
                if (_stopping || _disposed)
                    return false;
                if (_queue.Count >= _capacity)
                {
                    _droppedCount++;
                    return false;
                }

                _queue.Enqueue(new WorkItem(action, dueUtc), dueUtc.Ticks);
                Monitor.PulseAll(_syncRoot);
                return true;
            }
        }

        public void CancelPending()
        {
            lock (_syncRoot)
            {
                _queue.Clear();
                Monitor.PulseAll(_syncRoot);
            }
        }

        public void Stop(TimeSpan timeout)
        {
            lock (_syncRoot)
            {
                _stopping = true;
                _queue.Clear();
                Monitor.PulseAll(_syncRoot);
            }

            DateTime deadlineUtc = DateTime.UtcNow.Add(timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout);
            for (int i = 0; i < _workers.Length; i++)
            {
                Thread worker = _workers[i];
                if (!worker.IsAlive)
                    continue;

                TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;
                worker.Join(remaining);
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
                WorkItem? workItem = null;
                lock (_syncRoot)
                {
                    while (!_stopping)
                    {
                        if (_queue.Count == 0)
                        {
                            Monitor.Wait(_syncRoot);
                            continue;
                        }

                        WorkItem next = _queue.Peek();
                        TimeSpan remaining = next.DueUtc - DateTime.UtcNow;
                        if (remaining > TimeSpan.Zero)
                        {
                            Monitor.Wait(_syncRoot, remaining);
                            continue;
                        }

                        workItem = _queue.Dequeue();
                        Monitor.PulseAll(_syncRoot);
                        break;
                    }

                    if (_stopping)
                        return;
                }

                try
                {
                    workItem?.Action();
                }
                catch (Exception ex)
                {
                    _errorHandler?.Invoke(ex);
                }
            }
        }

        private sealed class WorkItem
        {
            public WorkItem(Action action, DateTime dueUtc)
            {
                Action = action;
                DueUtc = dueUtc;
            }

            public Action Action { get; }
            public DateTime DueUtc { get; }
        }
    }
}
