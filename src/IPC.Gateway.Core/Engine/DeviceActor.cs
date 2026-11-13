using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IPC.Runtime.Engine
{
    internal sealed class DeviceActor
    {
        private static readonly AsyncLocal<DeviceActor?> Current = new AsyncLocal<DeviceActor?>();
        private readonly object _syncRoot = new object();
        private readonly LinkedList<WorkItem> _queue = new LinkedList<WorkItem>();
        private readonly Dictionary<string, WorkItem> _latest = new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        private readonly string _name;
        private readonly int _capacity;
        private bool _processing;
        private long _droppedPostCount;

        public DeviceActor(string name, int capacity = 2048)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _name = string.IsNullOrWhiteSpace(name) ? "DeviceActor" : name.Trim();
            _capacity = capacity;
        }

        public int Capacity => _capacity;
        public int PendingCount { get { lock (_syncRoot) return _queue.Count; } }
        public long DroppedPostCount => Interlocked.Read(ref _droppedPostCount);

        public void Post(Action action)
        {
            if (action == null) return;
            PostAsync(_ => { action(); return ValueTask.CompletedTask; });
        }

        public void PostAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            if (action == null || cancellationToken.IsCancellationRequested) return;
            if (!TryPostAsync(action, cancellationToken)) throw QueueFull();
        }

        // Reliable callers retain their event if this returns false, and retry the wake-up.
        // In particular, protocol subscriptions must not silently coalesce value/quality edges.
        public bool TryPostAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) return false;
            return TryEnqueue(new WorkItem(() => RunPostedActionAsync(action, cancellationToken), null, cancellationToken), true);
        }

        // Optional wake-up coalescing: only the pending action for this key is replaced.
        // The event payload itself must live in an owner-managed reliable queue.
        public bool PostLatestAsync(string key, Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(action);
            if (cancellationToken.IsCancellationRequested) return false;
            return TryEnqueue(new WorkItem(() => RunPostedActionAsync(action, cancellationToken), null, cancellationToken, key), true);
        }

        public void Execute(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            ExecuteAsync(_ => { action(); return ValueTask.CompletedTask; }).GetAwaiter().GetResult();
        }

        public Task ExecuteAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            return InvokeAsync<object?>(async token => { await action(token).ConfigureAwait(false); return null; }, cancellationToken);
        }

        public T Invoke<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);
            return InvokeAsync<T>(_ => new ValueTask<T>(action())).GetAwaiter().GetResult();
        }

        public Task<T> InvokeAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (ReferenceEquals(Current.Value, this)) return RunInlineAsync(action, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);

            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new WorkItem(() => RunQueuedActionAsync(action, completion, cancellationToken),
                () => completion.TrySetCanceled(cancellationToken), cancellationToken);
            if (!TryEnqueue(item, false)) completion.TrySetException(QueueFull());
            return completion.Task;
        }

        private InvalidOperationException QueueFull() => new InvalidOperationException(
            "Device actor '" + _name + "' queue is full (capacity " + _capacity + ").");

        private static async Task<T> RunInlineAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return await action(token).ConfigureAwait(false);
        }

        private static async ValueTask RunPostedActionAsync(Func<CancellationToken, ValueTask> action, CancellationToken token)
        {
            if (!token.IsCancellationRequested) await action(token).ConfigureAwait(false);
        }

        private static async ValueTask RunQueuedActionAsync<T>(Func<CancellationToken, ValueTask<T>> action,
            TaskCompletionSource<T> completion, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(await action(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { completion.TrySetCanceled(token); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }

        private bool TryEnqueue(WorkItem item, bool posted)
        {
            bool startProcessor = false;
            WorkItem? replaced = null;
            lock (_syncRoot)
            {
                if (item.Key != null && _latest.TryGetValue(item.Key, out replaced))
                {
                    item.Node = replaced.Node;
                    item.Node!.Value = item;
                    replaced.Node = null;
                    _latest[item.Key] = item;
                }
                else
                {
                    if (_queue.Count >= _capacity)
                    {
                        if (posted) Interlocked.Increment(ref _droppedPostCount);
                        return false;
                    }
                    item.Node = _queue.AddLast(item);
                    if (item.Key != null) _latest[item.Key] = item;
                }
                if (!_processing) { _processing = true; startProcessor = true; }
            }

            // Never wait for a cancellation callback while holding the actor lock.
            replaced?.Registration.Unregister();
            RegisterQueuedCancellation(item);
            if (startProcessor) _ = Task.Run(DrainQueueAsync);
            return true;
        }

        private void RegisterQueuedCancellation(WorkItem item)
        {
            if (!item.Token.CanBeCanceled) return;
            CancellationTokenRegistration registration = item.Token.UnsafeRegister(
                state => CancelQueued((WorkItem)state!), item);
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
                    if (item.Key != null) _latest.Remove(item.Key);
                    removed = true;
                }
            }
            if (removed)
            {
                item.Cancel?.Invoke();
                item.Registration.Unregister();
            }
        }

        private async Task DrainQueueAsync()
        {
            while (true)
            {
                WorkItem item;
                lock (_syncRoot)
                {
                    if (_queue.First == null) { _processing = false; return; }
                    item = _queue.First.Value;
                    _queue.RemoveFirst();
                    item.Node = null;
                    if (item.Key != null) _latest.Remove(item.Key);
                }
                item.Registration.Unregister();
                DeviceActor? previous = Current.Value;
                Current.Value = this;
                try { await item.Run().ConfigureAwait(false); }
                catch { /* Posted callbacks own their error reporting. */ }
                finally { Current.Value = previous; }
            }
        }

        private sealed class WorkItem(Func<ValueTask> run, Action? cancel, CancellationToken token, string? key = null)
        {
            public Func<ValueTask> Run { get; } = run;
            public Action? Cancel { get; } = cancel;
            public CancellationToken Token { get; } = token;
            public string? Key { get; } = key;
            public LinkedListNode<WorkItem>? Node;
            public CancellationTokenRegistration Registration;
        }

        public override string ToString() => _name;
    }
}
