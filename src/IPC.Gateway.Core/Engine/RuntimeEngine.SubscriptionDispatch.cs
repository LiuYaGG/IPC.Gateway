using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine;

public sealed partial class RuntimeEngine
{
    private readonly object _subscriptionDispatchSyncRoot = new();
    private readonly Dictionary<DeviceRuntimeState, SubscriptionDispatchContext> _subscriptionDispatchers = new();

    private SubscriptionDispatchContext? FindSubscriptionDispatcher(DeviceRuntimeState state)
    {
        lock (_subscriptionDispatchSyncRoot)
            return _subscriptionDispatchers.TryGetValue(state, out SubscriptionDispatchContext? context) && !context.Closed ? context : null;
    }

    private void RegisterSubscriptionDispatcher(DeviceRuntimeState state, SubscriptionDispatchContext context)
    {
        lock (_subscriptionDispatchSyncRoot)
        {
            if (_subscriptionDispatchers.Remove(state, out SubscriptionDispatchContext? previous)) previous.Close();
            _subscriptionDispatchers[state] = context;
        }
    }

    private void InvalidateSubscriptionDispatcher(DeviceRuntimeState state)
    {
        lock (_subscriptionDispatchSyncRoot)
            if (_subscriptionDispatchers.Remove(state, out SubscriptionDispatchContext? context)) context.Close();
    }

    private void StopSubscriptionDispatchers()
    {
        lock (_subscriptionDispatchSyncRoot)
        {
            foreach (SubscriptionDispatchContext context in _subscriptionDispatchers.Values) context.Close();
            _subscriptionDispatchers.Clear();
        }
        _valueProcessingResetTagIds.Clear();
        _configurationRefreshTagIds.Clear();
    }

    private static bool MatchesSubscriptionRequest(CompiledTagRead definition, PlcSubscriptionRequest request) =>
        string.Equals(definition.Tag.Id, request.Key, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(definition.Address, request.Address, StringComparison.Ordinal) &&
        definition.Tag.DataType == request.DataType &&
        definition.Request.ElementCount == request.ElementCount &&
        definition.Request.ElementOffset == request.ElementOffset;

    private void EnqueueSubscriptionNotification(SubscriptionDispatchContext context, PlcSubscriptionUpdate update)
    {
        if (update == null || context.Closed || _disposed || !context.IsActiveRequest(update.Request)) return;
        // Once a driver has been detached, even a late native callback cannot
        // retain another value or revive the old subscription's queue.
        if (context.Subscription != null && !ReferenceEquals(context.State.Subscription, context.Subscription))
        {
            context.Close();
            return;
        }
        lock (context.SyncRoot)
        {
            if (context.Closed || context.Fault != null) return;
            try { context.Notifications.Enqueue(SubscriptionNotification.FromUpdate(update)); }
            catch (Exception ex) { context.Fault = "Subscription notification spool failed: " + ex.Message; }
        }
        ScheduleSubscriptionDrain(context);
    }

    private void ScheduleSubscriptionDrain(SubscriptionDispatchContext context)
    {
        lock (context.SyncRoot)
        {
            if (context.Closed || context.DrainScheduled || (context.Notifications.Count == 0 && context.Fault == null)) return;
            context.DrainScheduled = true;
        }
        if (context.State.Actor.PostLatestAsync(context.WakeKey, token => DrainSubscriptionNotificationsAsync(context, token))) return;
        // Only the wake-up is retried/coalesced. Every PLC value and quality
        // transition stays in the FIFO, including its file-backed overflow.
        _ = Task.Run(async () =>
        {
            while (!context.Closed && !_disposed)
            {
                await Task.Delay(10).ConfigureAwait(false);
                if (context.State.Actor.PostLatestAsync(context.WakeKey, token => DrainSubscriptionNotificationsAsync(context, token))) return;
            }
        });
    }

    private bool DeferEmptySubscriptionRetirement(DeviceRuntimeState state)
    {
        SubscriptionDispatchContext? context = FindSubscriptionDispatcher(state);
        if (context == null) return false;
        // Stop admitting callbacks from monitors that are being removed, but
        // finish transitions already observed while those monitors were valid.
        context.SetRequests(Array.Empty<PlcSubscriptionRequest>());
        lock (context.SyncRoot)
        {
            if (context.Closed || (context.Notifications.Count == 0 && !context.DrainScheduled)) return false;
            context.RetireWhenEmpty = true;
            return true;
        }
    }

    private bool RetireEmptySubscription(SubscriptionDispatchContext context)
    {
        IPlcSubscription? subscription = null;
        lock (_syncRoot)
        {
            lock (context.SyncRoot)
            {
                if (context.Closed || !context.RetireWhenEmpty || context.Notifications.Count != 0) return false;
            }
            DeviceRuntimeState state = context.State;
            if (ReferenceEquals(FindSubscriptionDispatcher(state), context))
            {
                if (ReferenceEquals(state.Subscription, context.Subscription))
                {
                    subscription = state.Subscription;
                    state.Subscription = null;
                }
                state.SubscriptionFingerprint = string.Empty;
                InvalidateSubscriptionDispatcher(state);
            }
            else context.Close();
        }
        try { subscription?.Dispose(); } catch { }
        return true;
    }

    private async ValueTask DrainSubscriptionNotificationsAsync(SubscriptionDispatchContext context, CancellationToken token)
    {
        try
        {
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < 64; i++)
            {
                SubscriptionNotification? notification = null;
                string? fault;
                lock (context.SyncRoot)
                {
                    if (context.Closed) return;
                    fault = context.Fault;
                    if (fault == null && context.Notifications.Count > 0)
                    {
                        try { notification = context.Notifications.Dequeue(); }
                        catch (Exception ex) { fault = context.Fault = "Subscription notification spool failed: " + ex.Message; }
                    }
                }
                if (fault != null)
                {
                    FailSubscriptionDispatch(context, fault);
                    return;
                }
                if (notification == null) return;
                await ProcessSubscriptionUpdateAsync(context.State, notification.ToUpdate(), context, token, notification.TimestampUtc).ConfigureAwait(false);
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= _readTurnBudgetMs) break;
            }
        }
        catch (Exception ex)
        {
            FailSubscriptionDispatch(context, "Subscription notification processing failed: " + ex.Message);
        }
        finally
        {
            lock (context.SyncRoot) context.DrainScheduled = false;
            // Yield to pending writes/polls between batches without changing
            // notification order, so a burst cannot monopolize the actor.
            if (!RetireEmptySubscription(context)) ScheduleSubscriptionDrain(context);
        }
    }

    private void FailSubscriptionDispatch(SubscriptionDispatchContext context, string message)
    {
        try { IPC.IpcLogService.WriteError("SubscriptionQueueFault deviceId=" + context.State.Config.Id + " " + message); }
        catch { }
        IPlcSubscription? subscription = null;
        lock (_syncRoot)
        {
            DeviceRuntimeState state = context.State;
            if (IsCurrentDeviceState(state) && ReferenceEquals(FindSubscriptionDispatcher(state), context))
            {
                subscription = state.Subscription;
                state.Subscription = null;
                state.SubscriptionFingerprint = string.Empty;
                state.SubscriptionUnavailable = true;
                state.LastSubscriptionError = message;
                state.LastTaskStatus = "SubscriptionQueueFault";
                state.LastTaskError = message;
                state.NextSubscriptionRetryUtc = DateTime.UtcNow.AddMilliseconds(GetDeviceFailureRetryDelayMs(state.Config));
                ForceReadableTagsDueNoLock(state, state.Config.Tags, DateTime.UtcNow);
                foreach (GroupConfig group in state.Config.Groups)
                    if (group != null && group.Enabled) ForceReadableTagsDueNoLock(state, group.Tags, DateTime.UtcNow);
                state.NextPollUtc = DateTime.UtcNow;
                InvalidateSubscriptionDispatcher(state);
            }
        }
        try { subscription?.Dispose(); } catch { }
        context.Close();
    }

    private sealed class SubscriptionDispatchContext
    {
        public readonly object SyncRoot = new();
        public readonly OverflowQueue<SubscriptionNotification> Notifications = new(256);
        public readonly DeviceRuntimeState State;
        public readonly IPlcClient Client;
        private readonly Dictionary<string, PlcSubscriptionRequest> _activeRequests = new(StringComparer.OrdinalIgnoreCase);
        public readonly string WakeKey = "subscription-drain:" + Guid.NewGuid().ToString("N");
        public IPlcSubscription? Subscription;
        public volatile bool Closed;
        public bool DrainScheduled;
        public bool RetireWhenEmpty;
        public string? Fault;

        public SubscriptionDispatchContext(DeviceRuntimeState state, IPlcClient client, IList<PlcSubscriptionRequest> requests)
        {
            State = state;
            Client = client;
            SetRequests(requests);
        }

        public void SetRequests(IList<PlcSubscriptionRequest> requests)
        {
            lock (SyncRoot)
            {
                _activeRequests.Clear();
                foreach (PlcSubscriptionRequest request in requests) _activeRequests[request.Key] = request;
                if (requests.Count > 0) RetireWhenEmpty = false;
            }
        }

        public bool IsActiveRequest(PlcSubscriptionRequest request)
        {
            lock (SyncRoot)
                return !Closed && _activeRequests.TryGetValue(request.Key, out PlcSubscriptionRequest? active) &&
                    active.Address == request.Address && active.DataType == request.DataType &&
                    active.ElementCount == request.ElementCount && active.ElementOffset == request.ElementOffset;
        }

        public void Close()
        {
            lock (SyncRoot)
            {
                Closed = true;
                _activeRequests.Clear();
                try { Notifications.Clear(); }
                catch (Exception ex)
                {
                    Fault ??= "Subscription notification spool cleanup failed: " + ex.Message;
                    State.LastSubscriptionError = Fault;
                }
            }
        }
    }

    private sealed class SubscriptionNotification
    {
        public string Key { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public PlcDataType DataType { get; set; }
        public int ElementCount { get; set; }
        public int ElementOffset { get; set; }
        public int SamplingIntervalMs { get; set; }
        public bool Success { get; set; }
        public ushort TypeCode { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public object? Value { get; set; }
        public string Error { get; set; } = string.Empty;
        public PlcReadFailureScope FailureScope { get; set; }
        public DateTime TimestampUtc { get; set; }
        public DateTime? SourceTimestampUtc { get; set; }
        public bool IsUncertain { get; set; }

        public static SubscriptionNotification FromUpdate(PlcSubscriptionUpdate update) => new()
        {
            Key = update.Request.Key, Address = update.Request.Address, DataType = update.Request.DataType,
            ElementCount = update.Request.ElementCount, ElementOffset = update.Request.ElementOffset,
            SamplingIntervalMs = update.Request.SamplingIntervalMs, Success = update.Success,
            TypeCode = update.Result?.TypeCode ?? 0, TypeName = update.Result?.TypeName ?? string.Empty,
            Value = update.Result?.Value is Array array ? array.Clone() : update.Result?.Value,
            IsUncertain = update.Result?.IsUncertain ?? false, SourceTimestampUtc = update.SourceTimestampUtc,
            Error = update.ErrorMessage, FailureScope = update.FailureScope, TimestampUtc = update.TimestampUtc
        };

        public PlcSubscriptionUpdate ToUpdate()
        {
            var request = new PlcSubscriptionRequest(Key, Address, DataType, ElementCount, ElementOffset, SamplingIntervalMs);
            return Success ? PlcSubscriptionUpdate.FromSuccess(request, new PlcReadResult(TypeCode, TypeName, Value!) { IsUncertain = IsUncertain, SourceTimestampUtc = SourceTimestampUtc }) :
                PlcSubscriptionUpdate.FromFailure(request, Error, FailureScope, SourceTimestampUtc);
        }
    }
}
