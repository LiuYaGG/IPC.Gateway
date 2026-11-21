using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine;

public sealed partial class RuntimeEngine
{
    private enum AcquisitionLane { Initial, Healthy, Recovery }

    // Every turn releases the actor and the configured channel. A large scan is
    // continued through admission rather than holding the device for all tags.
    private async Task PollDeviceAsync(DeviceRuntimeState state, int generation, CancellationToken cancellationToken)
    {
        if (!IsCurrentGeneration(generation)) return;
        await state.Actor.ExecuteAsync(async token =>
        {
            if (!IsCurrentGeneration(generation) || !IsCurrentDeviceState(state)) return;
            DeviceConfig device = state.Config;
            if (!device.Enabled || !_configuredChannelScheduler.IsEnabled(device)) return;
            using IDisposable? recoveryIo = state.CurrentPollIsRecovery ? PlcClientInvoker.BeginRecoveryProbe() : null;
            state.LastPollTime = DateTime.Now;
            if (DateTime.UtcNow < state.NextReconnectUtc) return;
            state.CurrentRecoveryMetricsRecorded = false;
            var connection = await TryEnsureClientAsync(state, token).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation)) return;
            if (!connection.Connected || connection.Client == null)
            {
                MarkDeviceUnavailableTagsOnce(state);
                return;
            }
            if (state.IsIsolated)
            {
                await ProbeDeviceRecoveryAsync(state, connection.Client, generation, token).ConfigureAwait(false);
                return;
            }

            if (state.RecoveryState == "Recovered") state.RecoveryState = "Idle";
            IPlcClient client = connection.Client;
            bool subscriptionFallback = false;
            if (CanUseSubscription(client, device))
            {
                subscriptionFallback = await TryEnsureDeviceSubscriptionAsync(state, client, generation, token).ConfigureAwait(false);
                if (!IsCurrentGeneration(generation)) return;
                if (state.Client == null || !state.Client.IsConnected)
                {
                    MarkDeviceUnavailableTagsOnce(state);
                    return;
                }
                client = state.Client;
            }
            await RunReadTurnAsync(state, client, generation, subscriptionFallback, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask RunReadTurnAsync(DeviceRuntimeState state, IPlcClient client, int generation,
        bool subscriptionFallback, CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        var lanes = new[] { new List<CompiledTagRead>(), new List<CompiledTagRead>(), new List<CompiledTagRead>() };
        lock (_syncRoot)
        {
        if (!IsCurrentGeneration(generation)) return;
        foreach (CompiledTagRead entry in state.ReadPlan.Entries)
        {
            TagConfig tag = entry.Tag;
            if (!tag.Enabled || !CanDeviceRead(tag) || entry.Group?.Enabled == false) continue;
            if (!entry.IsStaticallyValid)
            {
                _nextReadUtcByTagId[tag.Id] = DateTime.MaxValue;
                continue;
            }
            if (entry.Runtime.HasRecoveryWork)
            {
                ScheduleTagRecoveryProbe(tag, entry.Runtime.NextRecoveryProbeUtc);
                if (entry.Runtime.CanProbe(now)) lanes[(int)AcquisitionLane.Recovery].Add(entry);
                continue;
            }
            if (!IsDue(tag, now)) continue;
            if (entry.Runtime.LastReadAttemptUtc == DateTime.MinValue)
                lanes[(int)AcquisitionLane.Initial].Add(entry);
            else if (subscriptionFallback && !IsConfigurationRefreshPending(tag.Id))
                ScheduleNextSubscriptionFallback(state, entry.Group, tag, now);
            else
                lanes[(int)AcquisitionLane.Healthy].Add(entry);
        }
        }

        // Work conserving weighted round robin: healthy reads own half the
        // slots, and first reads and probes each receive a bounded turn.
        AcquisitionLane[] order = AcquisitionOrder;
        int originalTurn = state.ReadDispatchTurn;
        AcquisitionLane? lane = null;
        for (int attempt = 0; attempt < order.Length; attempt++)
        {
            AcquisitionLane candidate = order[state.ReadDispatchTurn++ % order.Length];
            if (state.ReadDispatchTurn >= int.MaxValue - 4) state.ReadDispatchTurn = 0;
            if (lanes[(int)candidate].Count > 0) { lane = candidate; break; }
        }
        if (lane == null) return;
        if (lane == AcquisitionLane.Recovery && !state.CurrentPollIsRecovery)
        {
            // A notification/configuration change can alter the lane while a
            // device was queued. Re-admit the probe through the recovery pool.
            state.ReadDispatchTurn = originalTurn;
            return;
        }

        List<CompiledTagRead> candidates = lanes[(int)lane.Value];
        string cursor = GetAcquisitionCursor(state, lane.Value);
        int cursorOrdinal = state.ReadPlan.TryGet(cursor, out var cursorEntry) && cursorEntry != null ? cursorEntry.Ordinal : -1;
        int start = candidates.FindIndex(entry => entry.Ordinal > cursorOrdinal);
        if (start < 0) start = 0;
        // A transport failure of a whole range does not prove each address
        // invalid. Revalidate such ranges in small batches; if that small batch
        // fails again, its members fall back to individual probes.
        bool recoveryBatch = lane == AcquisitionLane.Recovery && candidates[start].Runtime.BatchVerificationPending;
        if (recoveryBatch)
        {
            candidates = candidates.FindAll(entry => entry.Runtime.BatchVerificationPending);
            start = candidates.FindIndex(entry => entry.Ordinal > cursorOrdinal);
            if (start < 0) start = 0;
        }
        bool batch = (lane != AcquisitionLane.Recovery || recoveryBatch) && PlcClientInvoker.SupportsBatchRead(client);
        int limit = lane == AcquisitionLane.Recovery ? 1 : lane == AcquisitionLane.Initial ? _initialReadItemsPerTurn : _maxReadItemsPerTurn;
        if (recoveryBatch && batch) limit = 4;
        if (batch)
        {
            PlcClientCapabilities capabilities = PlcClientInvoker.GetCapabilities(client);
            if (PlcDriverPluginRegistry.TryGetCapabilities(state.Config.Connection, state.Config.Protocol, out var driver) && driver.SupportsBatchRead)
                capabilities.MaxBatchItems = driver.MaxBatchItems;
            if (capabilities.MaxBatchItems > 0) limit = Math.Min(limit, capabilities.MaxBatchItems);
        }
        else limit = Math.Min(limit, _singleReadsPerTurn);

        int count = Math.Min(limit, candidates.Count);
        if (batch)
        {
            var reads = new List<PendingTagRead>(count);
            for (int offset = 0; offset < count; offset++)
            {
                CompiledTagRead entry = candidates[(start + offset) % candidates.Count];
                reads.Add(new PendingTagRead(entry));
                SetAcquisitionCursor(state, lane.Value, entry.Tag.Id);
            }
            await ReadBatchTagsAsync(client, state, reads, now, generation, subscriptionFallback, cancellationToken, recoveryBatch).ConfigureAwait(false);
            return;
        }

        Stopwatch turn = Stopwatch.StartNew();
        for (int offset = 0; offset < count; offset++)
        {
            if (!IsCurrentGeneration(generation)) return;
            CompiledTagRead entry = candidates[(start + offset) % candidates.Count];
            SetAcquisitionCursor(state, lane.Value, entry.Tag.Id);
            bool wasRecovering = entry.Runtime.HasRecoveryWork;
            var result = await ReadTagAsync(client, state, entry.Group, entry.Tag, generation, cancellationToken,
                recoveryProbe: lane == AcquisitionLane.Recovery, scheduled: true).ConfigureAwait(false);
            if (!IsCurrentGeneration(generation)) return;
            if (entry.Runtime.HasRecoveryWork)
                ScheduleTagRecoveryProbe(entry.Tag, entry.Runtime.NextRecoveryProbeUtc);
            else if (subscriptionFallback && result.ReadSucceeded && !wasRecovering)
                ScheduleNextSubscriptionFallback(state, entry.Group, entry.Tag, DateTime.UtcNow);
            else
                ScheduleNextRead(state, entry.Group, entry.Tag, generation, !result.ReadSucceeded, entry.Runtime.LastPeriodicSampleValid);
            if (!result.KeepConnected || turn.ElapsedMilliseconds >= _readTurnBudgetMs) break;
        }
    }

    private static readonly AcquisitionLane[] AcquisitionOrder =
        [AcquisitionLane.Initial, AcquisitionLane.Healthy, AcquisitionLane.Recovery, AcquisitionLane.Healthy];

    private void RecordRecoveryOperationStart(DeviceRuntimeState state, CompiledTagRead? entry = null)
    {
        DateTime startedUtc = DateTime.UtcNow;
        if (state.IsIsolated && !state.CurrentRecoveryMetricsRecorded &&
            state.NextRecoveryProbeUtc != DateTime.MinValue && state.NextRecoveryProbeUtc <= startedUtc)
        {
            state.CurrentRecoveryMetricsRecorded = true;
            _acquisitionMetrics.RecordDeviceProbeQueueDelayMs((startedUtc - state.NextRecoveryProbeUtc).TotalMilliseconds);
        }
        if (entry?.Runtime.HasRecoveryWork == true && entry.Runtime.NextRecoveryProbeUtc != DateTime.MinValue &&
            entry.Runtime.NextRecoveryProbeUtc <= startedUtc)
            _acquisitionMetrics.RecordTagProbeQueueDelayMs((startedUtc - entry.Runtime.NextRecoveryProbeUtc).TotalMilliseconds);
    }

    private bool ShouldQueueRecoveryPoll(DeviceRuntimeState state, DateTime now)
    {
        if (state.IsIsolated) return true;
        if (state.Subscription?.IsActive == true && string.IsNullOrEmpty(state.SubscriptionFingerprint)) return false;
        bool[] ready = new bool[3];
        foreach (CompiledTagRead entry in state.ReadPlan.Entries)
        {
            if (!entry.IsStaticallyValid || !entry.Tag.Enabled || !CanDeviceRead(entry.Tag) || entry.Group?.Enabled == false) continue;
            if (entry.Runtime.HasRecoveryWork)
            {
                if (entry.Runtime.CanProbe(now)) ready[(int)AcquisitionLane.Recovery] = true;
            }
            else if (IsDue(entry.Tag, now))
            {
                if (entry.Runtime.LastReadAttemptUtc == DateTime.MinValue) ready[(int)AcquisitionLane.Initial] = true;
                else if (state.Subscription?.IsActive != true || IsConfigurationRefreshPending(entry.Tag.Id)) ready[(int)AcquisitionLane.Healthy] = true;
            }
        }
        for (int offset = 0; offset < AcquisitionOrder.Length; offset++)
        {
            AcquisitionLane lane = AcquisitionOrder[(state.ReadDispatchTurn + offset) % AcquisitionOrder.Length];
            if (ready[(int)lane]) return lane == AcquisitionLane.Recovery;
        }
        return false;
    }

    private static string GetAcquisitionCursor(DeviceRuntimeState state, AcquisitionLane lane) => lane switch
    {
        AcquisitionLane.Initial => state.LastInitialReadTagId,
        AcquisitionLane.Recovery => state.LastTagRecoveryProbeId,
        _ => state.LastHealthyReadTagId
    };

    private static void SetAcquisitionCursor(DeviceRuntimeState state, AcquisitionLane lane, string id)
    {
        if (lane == AcquisitionLane.Initial) state.LastInitialReadTagId = id;
        else if (lane == AcquisitionLane.Recovery) state.LastTagRecoveryProbeId = id;
        else state.LastHealthyReadTagId = id;
    }
}
