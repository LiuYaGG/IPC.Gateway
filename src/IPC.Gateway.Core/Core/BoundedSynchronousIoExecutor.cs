using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public sealed class BoundedSynchronousIoExecutor : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly LinkedList<WorkItem> _queue = new LinkedList<WorkItem>();
        private readonly List<Thread> _workers;
        private readonly int _capacity;
        private bool _disposed;

        public BoundedSynchronousIoExecutor(int workerCount, int capacity)
            : this(workerCount, capacity, "PLC synchronous I/O", false) { }

        public BoundedSynchronousIoExecutor(int workerCount, int capacity, string workerName, bool singleThreadedApartment)
        {
            if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _workers = new List<Thread>(workerCount);
            for (int index = 0; index < workerCount; index++)
            {
                var worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = (string.IsNullOrWhiteSpace(workerName) ? "PLC synchronous I/O" : workerName) + " " + (index + 1)
                };
                if (singleThreadedApartment && OperatingSystem.IsWindows()) worker.SetApartmentState(ApartmentState.STA);
                _workers.Add(worker);
                worker.Start();
            }
        }

        public int Capacity => _capacity;
        public int PendingCount { get { lock (_syncRoot) return _queue.Count; } }

        public ValueTask InvokeAsync(Action operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var item = new ActionWorkItem(operation, cancellationToken);
            Enqueue(item);
            return new ValueTask(item.Task);
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            var item = new FuncWorkItem<T>(operation, cancellationToken);
            Enqueue(item);
            return new ValueTask<T>(item.ResultTask);
        }

        private void Enqueue(WorkItem item)
        {
            item.Token.ThrowIfCancellationRequested();
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_queue.Count >= _capacity)
                    throw new PlcCommunicationException("The bounded synchronous PLC I/O queue is full (capacity " + Capacity + ").");
                item.Node = _queue.AddLast(item);
                Monitor.Pulse(_syncRoot);
            }
            if (!item.Token.CanBeCanceled) return;
            CancellationTokenRegistration registration = item.Token.UnsafeRegister(state => CancelQueued((WorkItem)state!), item);
            bool keep;
            lock (_syncRoot)
            {
                keep = item.Node != null;
                if (keep) item.Registration = registration;
            }
            if (!keep) registration.Unregister();
        }

        private void CancelQueued(WorkItem item)
        {
            bool removed = false;
            lock (_syncRoot)
            {
                if (item.Node != null)
                {
                    _queue.Remove(item.Node);
                    item.Node = null;
                    removed = true;
                }
            }
            if (removed)
            {
                item.Cancel();
                item.Registration.Unregister();
            }
        }

        private void WorkerLoop()
        {
            while (true)
            {
                WorkItem item;
                lock (_syncRoot)
                {
                    while (_queue.First == null && !_disposed) Monitor.Wait(_syncRoot);
                    if (_queue.First == null) return;
                    item = _queue.First.Value;
                    _queue.RemoveFirst();
                    item.Node = null;
                }
                item.Registration.Unregister();
                // Cancellation can remove waiting work immediately, but may not complete
                // an active synchronous operation early and release its physical lease.
                item.Execute();
            }
        }

        public void Dispose()
        {
            List<WorkItem> abandoned;
            lock (_syncRoot)
            {
                if (_disposed) return;
                _disposed = true;
                abandoned = new List<WorkItem>(_queue);
                _queue.Clear();
                foreach (WorkItem item in abandoned) item.Node = null;
                Monitor.PulseAll(_syncRoot);
            }
            foreach (WorkItem item in abandoned)
            {
                item.Registration.Unregister();
                item.Reject(new ObjectDisposedException(nameof(BoundedSynchronousIoExecutor)));
            }
        }

        private abstract class WorkItem(CancellationToken token)
        {
            public CancellationToken Token { get; } = token;
            public LinkedListNode<WorkItem>? Node;
            public CancellationTokenRegistration Registration;
            public abstract Task Task { get; }
            public abstract void Execute();
            public abstract void Cancel();
            public abstract void Reject(Exception exception);
        }

        private sealed class ActionWorkItem(Action operation, CancellationToken token) : WorkItem(token)
        {
            private readonly TaskCompletionSource _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            public override Task Task => _completion.Task;
            public override void Cancel() => _completion.TrySetCanceled(Token);
            public override void Reject(Exception exception) => _completion.TrySetException(exception);
            public override void Execute()
            {
                if (Token.IsCancellationRequested) { Cancel(); return; }
                try { operation(); _completion.TrySetResult(); }
                catch (Exception ex) { _completion.TrySetException(ex); }
            }
        }

        private sealed class FuncWorkItem<T>(Func<T> operation, CancellationToken token) : WorkItem(token)
        {
            private readonly TaskCompletionSource<T> _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            public Task<T> ResultTask => _completion.Task;
            public override Task Task => _completion.Task;
            public override void Cancel() => _completion.TrySetCanceled(Token);
            public override void Reject(Exception exception) => _completion.TrySetException(exception);
            public override void Execute()
            {
                if (Token.IsCancellationRequested) { Cancel(); return; }
                try { _completion.TrySetResult(operation()); }
                catch (Exception ex) { _completion.TrySetException(ex); }
            }
        }
    }
}
