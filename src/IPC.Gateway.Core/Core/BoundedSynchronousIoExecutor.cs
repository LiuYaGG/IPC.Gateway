using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Plc.Communication.Core
{
    public sealed class BoundedSynchronousIoExecutor : IDisposable
    {
        private readonly BlockingCollection<IWorkItem> _queue;
        private readonly List<Thread> _workers;
        private int _disposed;

        public BoundedSynchronousIoExecutor(int workerCount, int capacity)
            : this(workerCount, capacity, "PLC synchronous I/O", false)
        {
        }

        public BoundedSynchronousIoExecutor(
            int workerCount,
            int capacity,
            string workerName,
            bool singleThreadedApartment)
        {
            if (workerCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(workerCount));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _queue = new BlockingCollection<IWorkItem>(new ConcurrentQueue<IWorkItem>(), capacity);
            _workers = new List<Thread>(workerCount);
            for (int index = 0; index < workerCount; index++)
            {
                Thread worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = (string.IsNullOrWhiteSpace(workerName) ? "PLC synchronous I/O" : workerName) + " " + (index + 1)
                };
                if (singleThreadedApartment && OperatingSystem.IsWindows())
                    worker.SetApartmentState(ApartmentState.STA);
                _workers.Add(worker);
                worker.Start();
            }
        }

        public int Capacity => _queue.BoundedCapacity;
        public int PendingCount => _queue.Count;

        public ValueTask InvokeAsync(Action operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            return new ValueTask(Enqueue(new ActionWorkItem(operation, cancellationToken), cancellationToken));
        }

        public ValueTask<T> InvokeAsync<T>(Func<T> operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            FuncWorkItem<T> workItem = new FuncWorkItem<T>(operation, cancellationToken);
            Enqueue(workItem, cancellationToken);
            return new ValueTask<T>(workItem.Task);
        }

        private Task Enqueue(IWorkItem workItem, CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_queue.TryAdd(workItem))
                throw new PlcCommunicationException(
                    "The bounded synchronous PLC I/O queue is full (capacity " + Capacity + ").");
            return workItem.Task;
        }

        private void WorkerLoop()
        {
            foreach (IWorkItem workItem in _queue.GetConsumingEnumerable())
                workItem.Execute();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _queue.CompleteAdding();
        }

        private interface IWorkItem
        {
            Task Task { get; }
            void Execute();
        }

        private sealed class ActionWorkItem : IWorkItem
        {
            private readonly Action _operation;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource _completion;

            public ActionWorkItem(Action operation, CancellationToken cancellationToken)
            {
                _operation = operation;
                _cancellationToken = cancellationToken;
                _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task Task => _completion.Task;

            public void Execute()
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _completion.TrySetCanceled(_cancellationToken);
                    return;
                }

                try
                {
                    _operation();
                    _completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }
        }

        private sealed class FuncWorkItem<T> : IWorkItem
        {
            private readonly Func<T> _operation;
            private readonly CancellationToken _cancellationToken;
            private readonly TaskCompletionSource<T> _completion;

            public FuncWorkItem(Func<T> operation, CancellationToken cancellationToken)
            {
                _operation = operation;
                _cancellationToken = cancellationToken;
                _completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public Task<T> Task => _completion.Task;
            Task IWorkItem.Task => Task;

            public void Execute()
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    _completion.TrySetCanceled(_cancellationToken);
                    return;
                }

                try
                {
                    _completion.TrySetResult(_operation());
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                }
            }
        }
    }
}
