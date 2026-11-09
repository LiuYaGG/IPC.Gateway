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
        private readonly Queue<Func<ValueTask>> _queue = new Queue<Func<ValueTask>>();
        private readonly string _name;
        private bool _processing;

        public DeviceActor(string name)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "DeviceActor" : name.Trim();
        }

        public void Post(Action action)
        {
            if (action == null)
                return;

            PostAsync(delegate (CancellationToken _)
            {
                action();
                return ValueTask.CompletedTask;
            });
        }

        public void PostAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
                return;

            Enqueue(delegate
            {
                return RunPostedActionAsync(action, cancellationToken);
            });
        }

        public void Execute(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            ExecuteAsync(delegate (CancellationToken _)
            {
                action();
                return ValueTask.CompletedTask;
            }).GetAwaiter().GetResult();
        }

        public Task ExecuteAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return InvokeAsync<object?>(async token =>
            {
                await action(token).ConfigureAwait(false);
                return null;
            }, cancellationToken);
        }

        public T Invoke<T>(Func<T> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return InvokeAsync<T>(delegate (CancellationToken _)
            {
                return new ValueTask<T>(action());
            }).GetAwaiter().GetResult();
        }

        public Task<T> InvokeAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken = default)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (ReferenceEquals(Current.Value, this))
                return RunInlineAsync(action, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            TaskCompletionSource<T> completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(delegate
            {
                return RunQueuedActionAsync(action, completion, cancellationToken);
            });

            return completion.Task;
        }

        private static async Task<T> RunInlineAsync<T>(Func<CancellationToken, ValueTask<T>> action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await action(cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask RunPostedActionAsync(Func<CancellationToken, ValueTask> action, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            await action(cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask RunQueuedActionAsync<T>(
            Func<CancellationToken, ValueTask<T>> action,
            TaskCompletionSource<T> completion,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                T result = await action(cancellationToken).ConfigureAwait(false);
                completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        private void Enqueue(Func<ValueTask> action)
        {
            bool startProcessor = false;
            lock (_syncRoot)
            {
                _queue.Enqueue(action);
                if (!_processing)
                {
                    _processing = true;
                    startProcessor = true;
                }
            }

            if (startProcessor)
                _ = Task.Run(DrainQueueAsync);
        }

        private async Task DrainQueueAsync()
        {
            while (true)
            {
                Func<ValueTask>? action;
                lock (_syncRoot)
                {
                    if (_queue.Count == 0)
                    {
                        _processing = false;
                        return;
                    }

                    action = _queue.Dequeue();
                }

                DeviceActor? previous = Current.Value;
                Current.Value = this;
                try
                {
                    await action().ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    Current.Value = previous;
                }
            }
        }

        public override string ToString()
        {
            return _name;
        }
    }
}
