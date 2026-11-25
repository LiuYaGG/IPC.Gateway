using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Resilience;
using IPC.Plc.Communication.Core;
using IPC.Plc.Communication.Infrastructure;
using IPC.Runtime.Api;
using IPC.Runtime.Cleaning;
using IPC.Runtime.Configuration;
using IPC.Runtime.Indexing;
using IPC.Runtime.Scaling;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    public sealed partial class RuntimeEngine
    {
        public void ApplyProject(ProjectConfig config)
        {
            // Keep generation publication and deferred release/dispatch work in
            // the same order when multiple callers save concurrently.
            lock (_configurationApplySyncRoot)
                ApplyProjectSerialized(config);
        }

        private void ApplyProjectSerialized(ProjectConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            ProjectConfig runtimeConfig = ProjectConfigCloner.Clone(config) ?? throw new InvalidOperationException("Project configuration clone failed.");
            ProjectConfigStore.Normalize(runtimeConfig);
            Dictionary<string, TagValueSnapshot> previousSnapshots;
            Dictionary<string, TagValueSnapshot> previousSnapshotsByTagId;
            Dictionary<string, DateTime> previousNextReadUtcByTagId;
            Dictionary<string, DeviceRuntimeState> previousDeviceStatesById;
            List<DeviceRuntimeState> statesToRelease = new List<DeviceRuntimeState>();
            List<DeviceRuntimeState> statesToDisconnect = new List<DeviceRuntimeState>();
            HashSet<string> devicesToPollImmediately = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<TagValueSnapshot> changedSnapshots = new List<TagValueSnapshot>();
            Timer? oldTimer = null;
            bool restartTimer;
            int runtimeGeneration;

            lock (_syncRoot)
            {
                runtimeGeneration = Interlocked.Increment(ref _runtimeGeneration);
                ProjectConfig? previousConfig = _config;
                _configuredChannelScheduler.Configure(runtimeConfig);
                restartTimer = _timer != null;
                if (restartTimer)
                {
                    oldTimer = _timer;
                    _timer = null;
                }

                previousSnapshots = new Dictionary<string, TagValueSnapshot>(_snapshotsByPath, StringComparer.OrdinalIgnoreCase);
                previousSnapshotsByTagId = BuildSnapshotLookupByTagIdNoLock(_snapshotsByPath);
                previousNextReadUtcByTagId = new Dictionary<string, DateTime>(_nextReadUtcByTagId, StringComparer.OrdinalIgnoreCase);
                previousDeviceStatesById = new Dictionary<string, DeviceRuntimeState>(_deviceStatesById, StringComparer.OrdinalIgnoreCase);
                if (previousConfig != null && !string.Equals(previousConfig.ProjectId, runtimeConfig.ProjectId, StringComparison.OrdinalIgnoreCase))
                {
                    statesToRelease.AddRange(previousDeviceStatesById.Values);
                    previousDeviceStatesById.Clear();
                    previousSnapshots.Clear();
                    previousSnapshotsByTagId.Clear();
                    previousNextReadUtcByTagId.Clear();
                    _runtimeEvents.Clear();
                    _valueProcessingResetTagIds.Clear();
                    _configurationRefreshTagIds.Clear();
                    previousConfig = null;
                }

                _config = runtimeConfig;
                RebuildChannelNameCacheNoLock(runtimeConfig);
                _index = new TagRuntimeIndex(runtimeConfig);
                _snapshotsByPath.Clear();
                _nextReadUtcByTagId.Clear();
                _deviceStatesById.Clear();
                ClearPollingQueue();
                InitializeDeviceStates(runtimeConfig, previousDeviceStatesById, statesToRelease, statesToDisconnect, devicesToPollImmediately);
                DateTime nowUtc = DateTime.UtcNow;
                InitializeDeviceCadencesNoLock(runtimeConfig, nowUtc, staggerFirstPoll: false);
                RebuildSnapshotsNoLock(runtimeConfig, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);
                ApplyConfigurationScheduleTransitions(previousConfig, runtimeConfig, devicesToPollImmediately, nowUtc);

                if (restartTimer)
                    _timer = new Timer(SchedulePolls, runtimeGeneration, 0, _schedulerIntervalMs);
            }

            if (oldTimer != null)
                oldTimer.Dispose();

            ForceDevicePollsDue(devicesToPollImmediately, DateTime.UtcNow);

            for (int i = 0; i < statesToRelease.Count; i++)
                ReleaseDeviceStateWithoutWaiting(statesToRelease[i]);

            for (int i = 0; i < statesToDisconnect.Count; i++)
                DisconnectDisabledDeviceStateClientWithoutWaiting(statesToDisconnect[i]);

            for (int i = 0; i < changedSnapshots.Count; i++)
                EnqueueTagValueChanged(changedSnapshots[i], runtimeGeneration);
        }

        public WriteTagResponse WriteTag(WriteTagRequest request) => WriteTagAsync(request).GetAwaiter().GetResult();

        public async Task<WriteTagResponse> WriteTagAsync(WriteTagRequest request)
        {
            if (request == null)
                throw new ArgumentNullException("request");

            var requestContext = PlcWriteRequestContext.Capture();
            long writeEpoch = _writeAuthority?.Epoch ?? 0;
            if (_writeAuthority is { CanRunBusiness: false })
                return CreateWriteErrorResponse(request, "HA_NOT_ACTIVE");
            DeviceConfig? device;
            GroupConfig? group;
            TagConfig? tag;
            DeviceRuntimeState? writeDeviceState;
            lock (_syncRoot)
            {
                if (!TryFindWritableTag(request, out device, out group, out tag) || device == null || tag == null)
                    return CreateWriteErrorResponse(request, "Tag was not found.");
                string? targetError = GetWriteTargetError(device, group, tag);
                if (targetError != null)
                    return CreateWriteErrorResponse(request, targetError);
                writeDeviceState = GetDeviceState(device);
                if (writeDeviceState == null)
                    return CreateWriteErrorResponse(request, "Device runtime state was not found.");
            }

            PlcDataType requestDataType;
            if (!TryParseDataType(request.DataType, out requestDataType))
                return CreateWriteErrorResponse(request, "DataType is required and must be a valid PlcDataType name.");

            if (requestDataType != tag.DataType)
            {
                return CreateWriteErrorResponse(
                    request,
                    "DataType does not match tag configuration. Requested: " + requestDataType + ", Configured: " + tag.DataType + ".");
            }

            string valueText = BuildWriteValueText(request, tag);
            string validationError;
            if (!TryValidateWriteValue(tag.DataType, valueText, tag.ElementCount, out validationError))
                return CreateWriteErrorResponse(request, validationError);

            try
            {
                using CancellationTokenSource writeCancellation = CreateOperationTimeoutCancellationTokenSource(device, PlcOperationTimeoutKind.Device);
                ConfiguredChannelLease? configuredWriteLease = null;
                try
                {
                    configuredWriteLease = await _configuredChannelScheduler
                        .AcquireWriteAsync(device, writeCancellation.Token)
                        .ConfigureAwait(false);
                    return await writeDeviceState.Actor.InvokeAsync(async actorToken =>
                    {
                        if (!TryResolveCurrentWriteTarget(writeDeviceState, device, group, tag, out _, out _, out _, out string targetError))
                            return CreateWriteErrorResponse(request, targetError);
                        (bool connected, IPlcClient? client) = await TryEnsureClientAsync(writeDeviceState, actorToken).ConfigureAwait(false);
                        if (!connected || client == null)
                            return CreateWriteErrorResponse(request, "Device is not connected.");

                        // Connection and channel waits can outlive a configuration
                        // save. Recheck immediately before dispatching the write.
                        if (!TryResolveCurrentWriteTarget(writeDeviceState, device, group, tag,
                            out DeviceConfig currentDevice, out GroupConfig? currentGroup, out TagConfig currentTag, out targetError))
                            return CreateWriteErrorResponse(request, targetError);
                        int runtimeGeneration = GetCurrentRuntimeGeneration();
                        DeviceConfig expectedDevice = currentDevice;
                        GroupConfig? expectedGroup = currentGroup;
                        TagConfig expectedTag = currentTag;
                        bool dispatched = false;
                        PlcHaWrite? frame = null;
                        WriteTagResponse WithExecution(WriteTagResponse response)
                        {
                            response.WriteExecution = frame?.Result(response.Success);
                            return response;
                        }
                        try
                        {
                            void CheckTarget()
                            {
                                // Physical buses and synchronous I/O workers
                                // can add another wait after actor admission.
                                if (!TryResolveCurrentWriteTarget(writeDeviceState, expectedDevice, expectedGroup, expectedTag,
                                    out _, out _, out _, out string dispatchError))
                                    throw new InvalidOperationException(dispatchError);
                                actorToken.ThrowIfCancellationRequested();
                            }
                            using var flight = _writeAuthority?.TrackWrite(writeEpoch);
                            frame = _writeAuthority is { RequiresWriteAuthorization: true }
                                ? new PlcHaWrite(_writeAuthority, writeEpoch, requestContext, client, currentDevice.Connection,
                                    ResolveTagAddress(currentDevice, currentTag), currentTag.DataType, currentTag.ElementCount,
                                    currentTag.ElementOffset, valueText, actorToken, CheckTarget, request.Origin, currentDevice.Name)
                                : null;
                            await WriteTagValueAsync(client, writeDeviceState, currentTag, valueText, actorToken, () =>
                            {
                                CheckTarget();
                                dispatched = true;
                            }, frame).ConfigureAwait(false);
                            frame?.Confirm();
                        }
                        catch (Exception ex) when (dispatched && ex is not PlcWriteDeniedException && IsCommunicationException(ex) && (frame == null || frame.Result(false).Outcome == "Unknown"))
                        {
                            // Only a dispatched operation can implicate the connection.
                            // Finish cleanup in the same actor turn before the next command.
                            writeDeviceState.ProtocolCircuitBreaker.RecordFailure(ex.Message);
                            await DropDeviceConnectionAsync(writeDeviceState, ex.Message, CancellationToken.None).ConfigureAwait(false);
                            return WithExecution(CreateWriteErrorResponse(request, ex.Message));
                        }
                        catch (Exception ex)
                        {
                            return WithExecution(CreateWriteErrorResponse(request, ex.Message));
                        }
                        writeDeviceState.ProtocolCircuitBreaker.RecordSuccess();

                        if (!IsCurrentGeneration(runtimeGeneration) ||
                            !TryResolveCurrentWriteTarget(writeDeviceState, currentDevice, currentGroup, currentTag,
                                out currentDevice, out currentGroup, out currentTag, out _))
                            return WithExecution(CreateWriteRefreshWarningResponse(device, group, tag, "Write succeeded, but the tag configuration changed before its current value could be refreshed."));
                        if (CanRead(currentTag))
                        {
                            (bool keepConnected, bool readSucceeded, _) = await ReadTagAsync(client, writeDeviceState, currentGroup, currentTag, runtimeGeneration, actorToken).ConfigureAwait(false);
                            if (!readSucceeded)
                            {
                                string refreshError = keepConnected
                                    ? "Write succeeded, but the current value could not be refreshed."
                                    : "Write succeeded, but device communication failed while refreshing the current value.";
                                return WithExecution(CreateWriteRefreshWarningResponse(currentDevice, currentGroup, currentTag, refreshError));
                            }
                        }

                        TagValueSnapshot? snapshot;
                        ReadTagResponse? currentValue = null;
                        if (TryGetSnapshotById(currentDevice.ChannelId, currentDevice.Id, currentGroup == null ? string.Empty : currentGroup.Id, currentTag.Id, out snapshot) && snapshot != null)
                            currentValue = ReadTagResponse.FromSnapshot(snapshot);

                        return new WriteTagResponse
                        {
                            WriteExecution = frame?.Result(true),
                            Success = true,
                            ChannelId = currentDevice.ChannelId,
                            ChannelName = GetChannelName(currentDevice.ChannelId),
                            DeviceId = currentDevice.Id,
                            GroupId = currentGroup == null ? string.Empty : currentGroup.Id,
                            TagId = currentTag.Id,
                            DeviceName = currentDevice.Name,
                            GroupName = currentGroup == null ? string.Empty : currentGroup.Name,
                            TagName = currentTag.Name,
                            DataType = currentTag.DataType.ToString(),
                            Quality = currentValue == null ? TagQuality.Good.ToString() : currentValue.Quality,
                            Timestamp = DateTime.Now,
                            CurrentValue = currentValue ?? new ReadTagResponse()
                        };
                    }, writeCancellation.Token).ConfigureAwait(false);
                }
                finally
                {
                    configuredWriteLease?.Dispose();
                    TryStartQueuedDevicePolls(Interlocked.CompareExchange(ref _runtimeGeneration, 0, 0));
                }
            }
            catch (Exception ex)
            {
                return CreateWriteErrorResponse(request, ex.Message);
            }
        }

        private void RebuildSnapshotsNoLock(
            ProjectConfig config,
            IDictionary<string, TagValueSnapshot> previousSnapshots,
            IDictionary<string, TagValueSnapshot> previousSnapshotsByTagId,
            IDictionary<string, DateTime> previousNextReadUtcByTagId,
            IList<TagValueSnapshot> changedSnapshots,
            ISet<string>? devicesToPollImmediately,
            DateTime nowUtc)
        {
            if (config == null || config.Devices == null)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                bool deviceEnabled = device.Enabled && _configuredChannelScheduler.IsEnabled(device);
                RebuildTagSnapshotsNoLock(device, null, device.Tags, deviceEnabled, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);

                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null)
                        continue;

                    group.DeviceId = device.Id;
                    RebuildTagSnapshotsNoLock(device, group, group.Tags, deviceEnabled && group.Enabled, previousSnapshots, previousSnapshotsByTagId, previousNextReadUtcByTagId, changedSnapshots, devicesToPollImmediately, nowUtc);
                }
            }
        }

        private void RebuildTagSnapshotsNoLock(
            DeviceConfig device,
            GroupConfig? group,
            IList<TagConfig>? tags,
            bool parentEnabled,
            IDictionary<string, TagValueSnapshot> previousSnapshots,
            IDictionary<string, TagValueSnapshot> previousSnapshotsByTagId,
            IDictionary<string, DateTime> previousNextReadUtcByTagId,
            IList<TagValueSnapshot> changedSnapshots,
            ISet<string>? devicesToPollImmediately,
            DateTime nowUtc)
        {
            if (tags == null)
                return;

            for (int t = 0; t < tags.Count; t++)
            {
                TagConfig tag = tags[t];
                if (tag == null)
                    continue;

                tag.DeviceId = device.Id;
                tag.GroupId = group == null ? string.Empty : group.Id;
                if (tag.Scaling == null)
                    tag.Scaling = ScalingConfig.Default();

                TagValueSnapshot snapshot = CreateConfiguredSnapshot(device, group, tag, parentEnabled);
                DeviceRuntimeState deviceState = _deviceStatesById[device.Id];
                CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
                TagValueSnapshot? previous = FindPreviousSnapshot(device, group, tag, previousSnapshots, previousSnapshotsByTagId);
                if (previous != null && compiledRead.PreservesReadState && parentEnabled && tag.Enabled && CanRead(tag) && CanRestoreConfiguredSnapshot(previous))
                    snapshot = MergeRestoredSnapshot(snapshot, previous);
                // An offline device publishes its unavailable quality once per outage.
                // New or re-enabled physical tags must inherit that known state.
                if (deviceState.UnavailableTagsMarked && snapshot.Quality == TagQuality.Unknown && CanDeviceRead(tag))
                {
                    snapshot.Quality = TagQuality.NotConnected;
                    snapshot.ErrorMessage = "Device is not connected.";
                }
                if (!tag.IsVirtual)
                    ApplyTagRuntimeState(snapshot, compiledRead.Runtime);

                string path = TagPath.BuildIdentity(device.ChannelId, device.Id, group == null ? string.Empty : group.Id, tag.Id);
                _snapshotStore.Set(_snapshotsByPath, path, snapshot);

                DateTime nextReadUtc;
                if (compiledRead.PreservesSchedule && !string.IsNullOrWhiteSpace(tag.Id) && previousNextReadUtcByTagId.TryGetValue(tag.Id, out nextReadUtc))
                    _nextReadUtcByTagId[tag.Id] = nextReadUtc;
                else
                    _nextReadUtcByTagId[tag.Id] = nowUtc;

                if (previous == null || HasTagValueChanged(previous, snapshot))
                    changedSnapshots.Add(snapshot.Clone());

                if ((!compiledRead.PreservesSchedule || previous == null) && parentEnabled && tag.Enabled && CanDeviceRead(tag) && !string.IsNullOrWhiteSpace(device.Id))
                    devicesToPollImmediately?.Add(device.Id);
            }
        }

        private void InitializeSnapshots(ProjectConfig config)
        {
            if (config.Devices == null)
                return;

            for (int d = 0; d < config.Devices.Count; d++)
            {
                DeviceConfig device = config.Devices[d];
                if (device == null)
                    continue;

                bool deviceEnabled = device.Enabled && _configuredChannelScheduler.IsEnabled(device);
                InitializeDeviceTags(device, null, device.Tags, deviceEnabled);

                if (device.Groups == null)
                    continue;

                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || group.Tags == null)
                        continue;

                    group.DeviceId = device.Id;
                    InitializeDeviceTags(device, group, group.Tags, deviceEnabled && group.Enabled);
                }
            }
        }

        private void ScheduleAddressCacheCleanup(DeviceRuntimeState state, IPlcAddressCache cache)
        {
            var plan = state.ReadPlan;
            bool Current() => !_disposed && ReferenceEquals(state.Client, cache) && ReferenceEquals(state.ReadPlan, plan);
            bool TrySchedule() => state.Actor.PostLatestAsync("PruneSymbolHandles", token =>
            {
                // Read the latest plan when the actor runs: another configuration may already be queued.
                if (!ReferenceEquals(state.Client, cache) || !state.Config.Enabled) return ValueTask.CompletedTask;
                return cache.RetainConfiguredAddressesAsync(state.ReadPlan.Entries.Where(entry =>
                    entry.IsStaticallyValid && entry.Tag.Enabled && (entry.Group?.Enabled ?? true)).Select(entry => entry.Request), token);
            });
            if (!TrySchedule()) _ = RetryAsync();

            async Task RetryAsync()
            {
                while (Current())
                {
                    await Task.Delay(25).ConfigureAwait(false);
                    if (Current() && TrySchedule()) return;
                }
            }
        }

        private void InitializeDeviceStates(ProjectConfig config)
        {
            InitializeDeviceStates(config, null, null, null, null);
        }

        private void InitializeDeviceStates(
            ProjectConfig config,
            IDictionary<string, DeviceRuntimeState>? previousStatesById,
            IList<DeviceRuntimeState>? statesToRelease,
            IList<DeviceRuntimeState>? statesToDisconnect,
            ISet<string>? devicesToPollImmediately)
        {
            if (config == null || config.Devices == null)
                return;

            HashSet<string>? reusedDeviceIds = previousStatesById == null
                ? null
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < config.Devices.Count; i++)
            {
                DeviceConfig device = config.Devices[i];
                if (device == null)
                    continue;

                DeviceRuntimeState? previousState;
                if (previousStatesById != null &&
                    previousStatesById.TryGetValue(device.Id, out previousState) &&
                    _configDiffer.CanReuseDeviceState(previousState.Config, device))
                {
                    DeviceRuntimeConfigTransition transition = previousState.ReuseConfig(device, _configuredChannelScheduler.IsEnabled(device));
                    _deviceStatesById[device.Id] = previousState;
                    reusedDeviceIds?.Add(device.Id);
                    if (previousState.Client is IPlcAddressCache addressCache && device.Enabled)
                        ScheduleAddressCacheCleanup(previousState, addressCache);
                    if (transition == DeviceRuntimeConfigTransition.Disabled)
                        statesToDisconnect?.Add(previousState);
                    else if (transition == DeviceRuntimeConfigTransition.Enabled)
                        devicesToPollImmediately?.Add(device.Id);
                    continue;
                }

                _deviceStatesById[device.Id] = new DeviceRuntimeState(device, _protocolDriverCircuitBreakerOptions);
            }

            foreach (var pair in _acquisitionTraces)
                if (!CanTraceAcquisition(pair.Key, pair.Value.TagId)) _acquisitionTraces.TryRemove(pair);

            if (previousStatesById == null || statesToRelease == null)
                return;

            foreach (KeyValuePair<string, DeviceRuntimeState> pair in previousStatesById)
            {
                if (reusedDeviceIds != null && reusedDeviceIds.Contains(pair.Key))
                    continue;
                if (pair.Value != null)
                    statesToRelease.Add(pair.Value);
            }
        }

        private void InitializeDeviceTags(DeviceConfig device, GroupConfig? group, IList<TagConfig>? tags, bool parentEnabled)
        {
            if (tags == null)
                return;

            foreach (TagConfig tag in tags)
            {
                if (tag == null)
                    continue;

                tag.DeviceId = device.Id;
                tag.GroupId = group?.Id ?? string.Empty;
                if (tag.Scaling == null)
                    tag.Scaling = ScalingConfig.Default();

                string path = TagPath.BuildIdentity(device.ChannelId, device.Id, tag.GroupId, tag.Id);
                _snapshotStore.Set(_snapshotsByPath, path, CreateConfiguredSnapshot(device, group, tag, parentEnabled));
                _nextReadUtcByTagId[tag.Id] = DateTime.UtcNow;
            }
        }

        private async ValueTask<bool> TryEnsureDeviceSubscriptionAsync(
            DeviceRuntimeState deviceState,
            IPlcClient client,
            int runtimeGeneration,
            CancellationToken cancellationToken)
        {
            DateTime now = DateTime.UtcNow;
            if (deviceState.SubscriptionUnavailable && now < deviceState.NextSubscriptionRetryUtc)
                return deviceState.Subscription != null && deviceState.Subscription.IsActive;

            DeviceConfig device;
            List<PlcSubscriptionRequest> requests;
            SubscriptionDispatchContext? dispatch;
            IPlcSubscription? existingSubscription;
            lock (_syncRoot)
            {
                device = deviceState.Config;
                if (!IsCurrentDeviceState(deviceState) || !device.Enabled || !_configuredChannelScheduler.IsEnabled(device))
                    return false;
                requests = BuildSubscriptionRequests(deviceState);
                dispatch = FindSubscriptionDispatcher(deviceState);
                existingSubscription = deviceState.Subscription;
            }
            if (requests.Count == 0)
            {
                if (!DeferEmptySubscriptionRetirement(deviceState))
                {
                    InvalidateSubscriptionDispatcher(deviceState);
                    deviceState.DisposeSubscription();
                }
                deviceState.SubscriptionFingerprint = string.Empty;
                return false;
            }

            PlcSubscriptionOptions options = CreateSubscriptionOptions(requests);
            string fingerprint = CreateSubscriptionFingerprint(requests, options);
            if (deviceState.Subscription != null &&
                deviceState.Subscription.IsActive &&
                dispatch != null && ReferenceEquals(dispatch.Subscription, deviceState.Subscription) &&
                string.Equals(deviceState.SubscriptionFingerprint, fingerprint, StringComparison.Ordinal))
                return true;

            try
            {
                if (existingSubscription == null || !existingSubscription.IsActive || dispatch == null ||
                    !ReferenceEquals(dispatch.Subscription, existingSubscription))
                {
                    InvalidateSubscriptionDispatcher(deviceState);
                    deviceState.DisposeSubscription();
                    dispatch = new SubscriptionDispatchContext(deviceState, client, requests);
                    RegisterSubscriptionDispatcher(deviceState, dispatch);
                    SubscriptionDispatchContext callbackDispatch = dispatch;
                    IPlcSubscription subscription = await ExecutePlcOperationAsync(
                        deviceState,
                        token => PlcClientInvoker.SubscribeAsync(client, requests, options, update =>
                        {
                            EnqueueSubscriptionNotification(callbackDispatch, update);
                            return ValueTask.CompletedTask;
                        }, token),
                        CancellationToken.None,
                        PlcOperationTimeoutKind.Subscription,
                        false).ConfigureAwait(false);
                    lock (_syncRoot)
                    {
                        if (!IsCurrentDeviceState(deviceState) || !ReferenceEquals(deviceState.Client, client) ||
                            !deviceState.Config.Enabled || !_configuredChannelScheduler.IsEnabled(deviceState.Config) ||
                            !ReferenceEquals(FindSubscriptionDispatcher(deviceState), dispatch))
                        {
                            subscription.Dispose();
                            InvalidateSubscriptionDispatcher(deviceState);
                            return false;
                        }
                        dispatch.Subscription = subscription;
                        deviceState.Subscription = subscription;
                    }
                }
                else
                {
                    await ExecutePlcOperationAsync(
                        deviceState,
                        token => existingSubscription.UpdateAsync(requests, options, token),
                        CancellationToken.None,
                        PlcOperationTimeoutKind.Subscription,
                        false).ConfigureAwait(false);
                    dispatch!.SetRequests(requests);
                }

                lock (_syncRoot)
                {
                    if (!IsCurrentDeviceState(deviceState) || !deviceState.Config.Enabled ||
                        !_configuredChannelScheduler.IsEnabled(deviceState.Config) ||
                        !ReferenceEquals(FindSubscriptionDispatcher(deviceState), dispatch))
                        return false;
                    List<PlcSubscriptionRequest> currentRequests = BuildSubscriptionRequests(deviceState);
                    string currentFingerprint = CreateSubscriptionFingerprint(currentRequests, CreateSubscriptionOptions(currentRequests));
                    deviceState.SubscriptionFingerprint = string.Equals(fingerprint, currentFingerprint, StringComparison.Ordinal) ? fingerprint : string.Empty;
                    if (deviceState.SubscriptionFingerprint.Length == 0) deviceState.NextPollUtc = DateTime.UtcNow;
                    deviceState.SubscriptionUnavailable = false;
                    deviceState.LastSubscriptionError = string.Empty;
                    deviceState.NextSubscriptionRetryUtc = DateTime.MinValue;
                }
                return true;
            }
            catch (Exception ex)
            {
                InvalidateSubscriptionDispatcher(deviceState);
                deviceState.DisposeSubscription();
                deviceState.SubscriptionFingerprint = string.Empty;
                deviceState.SubscriptionUnavailable = true;
                deviceState.LastSubscriptionError = ex.Message ?? string.Empty;
                deviceState.NextSubscriptionRetryUtc = DateTime.UtcNow.AddMilliseconds(GetDeviceFailureRetryDelayMs(device));
                deviceState.LastTaskStatus = "SubscriptionFallback";
                deviceState.LastTaskError = deviceState.LastSubscriptionError;

                if (ShouldDropDeviceConnectionAfterSubscriptionFailure(deviceState, ex))
                    await DropDeviceConnectionAsync(deviceState, deviceState.LastSubscriptionError, cancellationToken).ConfigureAwait(false);

                return false;
            }
        }

        private List<PlcSubscriptionRequest> BuildSubscriptionRequests(DeviceRuntimeState deviceState)
        {
            DeviceConfig device = deviceState.Config;
            List<PlcSubscriptionRequest> requests = new List<PlcSubscriptionRequest>();
            if (device == null || !device.Enabled || !_configuredChannelScheduler.IsEnabled(device))
                return requests;
            AddSubscriptionRequests(deviceState, null, device == null ? null : device.Tags, requests);
            if (device != null && device.Groups != null)
            {
                for (int g = 0; g < device.Groups.Count; g++)
                {
                    GroupConfig group = device.Groups[g];
                    if (group == null || !group.Enabled)
                        continue;
                    AddSubscriptionRequests(deviceState, group, group.Tags, requests);
                }
            }

            return requests;
        }

        private void AddSubscriptionRequests(
            DeviceRuntimeState deviceState,
            GroupConfig? group,
            IList<TagConfig>? tags,
            IList<PlcSubscriptionRequest> requests)
        {
            DeviceConfig device = deviceState.Config;
            if (device == null || tags == null || requests == null)
                return;

            for (int i = 0; i < tags.Count; i++)
            {
                TagConfig tag = tags[i];
                if (tag == null || !tag.Enabled || !CanDeviceRead(tag))
                    continue;

                CompiledTagRead compiledRead = deviceState.ReadPlan.Get(tag);
                if (!compiledRead.IsStaticallyValid || compiledRead.Runtime.HasRecoveryWork)
                    continue;

                requests.Add(new PlcSubscriptionRequest(
                    tag.Id,
                    compiledRead.Address,
                    tag.DataType,
                    GetReadCount(tag),
                    Math.Max(0, tag.ElementOffset),
                    GetEffectiveScanRateMs(device, group, tag)));
            }
        }

        private static PlcSubscriptionOptions CreateSubscriptionOptions(IList<PlcSubscriptionRequest> requests)
        {
            int minInterval = 1000;
            bool hasInterval = false;
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    PlcSubscriptionRequest request = requests[i];
                    if (request == null || request.SamplingIntervalMs <= 0)
                        continue;

                    if (!hasInterval || request.SamplingIntervalMs < minInterval)
                        minInterval = request.SamplingIntervalMs;
                    hasInterval = true;
                }
            }

            minInterval = ClampInterval(minInterval, 100, 86400000);
            return new PlcSubscriptionOptions
            {
                PublishingIntervalMs = minInterval,
                SamplingIntervalMs = minInterval,
                QueueSize = 1,
                DiscardOldest = true
            };
        }

        private static string CreateSubscriptionFingerprint(IList<PlcSubscriptionRequest> requests, PlcSubscriptionOptions options)
        {
            List<string> parts = new List<string>();
            if (requests != null)
            {
                for (int i = 0; i < requests.Count; i++)
                {
                    PlcSubscriptionRequest request = requests[i];
                    if (request == null)
                        continue;

                    parts.Add(
                        (request.Key ?? string.Empty) + "\u001f" +
                        (request.Address ?? string.Empty) + "\u001f" +
                        request.DataType + "\u001f" +
                        request.ElementCount + "\u001f" +
                        request.ElementOffset + "\u001f" +
                        request.SamplingIntervalMs);
                }
            }

            parts.Sort(StringComparer.OrdinalIgnoreCase);
            return (options == null ? string.Empty : options.PublishingIntervalMs + ":" + options.QueueSize) +
                   "\u001e" +
                   string.Join("\u001e", parts.ToArray());
        }

        private async ValueTask ProcessSubscriptionUpdateAsync(
            DeviceRuntimeState deviceState,
            PlcSubscriptionUpdate update,
            SubscriptionDispatchContext dispatch,
            CancellationToken cancellationToken,
            DateTime? sourceTimestampUtc = null)
        {
            if (deviceState != null)
            {
                try { PlcDriverPluginRegistry.EnsureDriverAllowed(deviceState.Config.Connection, deviceState.Config.Protocol); }
                catch (PlcProtocolException ex)
                {
                    await DropDeviceConnectionAsync(deviceState, ex.Message, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            if (deviceState == null ||
                update == null ||
                !IsCurrentDeviceState(deviceState))
                return;
            bool counted = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeviceConfig device;
                GroupConfig? group;
                TagConfig? tag;
                CompiledTagRead definition;
                int generation;
                DateTime now = DateTime.UtcNow;
                lock (_syncRoot)
                {
                    device = deviceState.Config;
                    if (!IsCurrentDeviceState(deviceState) || !device.Enabled || !_configuredChannelScheduler.IsEnabled(device) ||
                        !ReferenceEquals(FindSubscriptionDispatcher(deviceState), dispatch) ||
                        !ReferenceEquals(deviceState.Subscription, dispatch.Subscription) ||
                        !ReferenceEquals(deviceState.Client, dispatch.Client) ||
                        !deviceState.ReadPlan.TryGet(update.Key, out CompiledTagRead? currentDefinition) || currentDefinition == null)
                        return;
                    definition = currentDefinition;
                    group = definition.Group;
                    tag = definition.Tag;
                    if (!(group?.Enabled ?? true) || !tag.Enabled || !CanDeviceRead(tag) ||
                        !definition.IsStaticallyValid || !MatchesSubscriptionRequest(definition, update.Request))
                        return;
                    generation = GetCurrentRuntimeGeneration();
                    DateTime notificationUtc = sourceTimestampUtc ?? now;
                    if (notificationUtc > deviceState.LastSubscriptionNotificationUtc)
                        deviceState.LastSubscriptionNotificationUtc = notificationUtc;
                    if (!counted) { deviceState.TotalReads++; counted = true; }
                }
                if (update.Success && update.Result != null)
                {
                    if (!ApplyReadSuccess(deviceState, group, tag, update.Result, recordDeviceSuccess: false,
                        runtimeGeneration: generation, sourceTimestampUtc: sourceTimestampUtc, readDefinition: definition))
                        continue;
                    lock (_syncRoot)
                    {
                        if (!IsCurrentGeneration(generation) || !IsCurrentDeviceState(deviceState)) return;
                        if (update.SourceTimestampUtc.HasValue ? update.SourceTimestampUtc.Value < definition.Runtime.LastSourceTimestampUtc : sourceTimestampUtc.HasValue &&
                            TryGetSnapshotById(device.ChannelId, device.Id, group?.Id ?? string.Empty, tag.Id, out TagValueSnapshot? current) &&
                            current != null && current.Timestamp > sourceTimestampUtc.Value.ToLocalTime())
                            return;
                        RecordDeviceCommunicationSuccess(deviceState, confirmedSubscriptionUpdate: true);
                        ScheduleNextSubscriptionFallback(deviceState, group, tag, now);
                    }
                    return;
                }
                ValueTask<bool> failureOperation;
                lock (_syncRoot)
                {
                    if (!IsCurrentGeneration(generation)) continue;
                    if (!IsCurrentDeviceState(deviceState)) return;
                    bool connectionFailure = IsConnectionFailureScope(update.FailureScope);
                    bool historical = update.SourceTimestampUtc.HasValue ? update.SourceTimestampUtc.Value < definition.Runtime.LastSourceTimestampUtc : sourceTimestampUtc.HasValue &&
                        TryGetSnapshotById(device.ChannelId, device.Id, group?.Id ?? string.Empty, tag.Id, out TagValueSnapshot? current) &&
                        current != null && current.Timestamp > sourceTimestampUtc.Value.ToLocalTime();
                    failureOperation = RecordReadFailureAsync(deviceState, group, tag, update.ErrorMessage,
                        connectionFailure, LooksLikeTimeoutMessage(update.ErrorMessage), connectionFailure,
                        update.FailureScope == PlcReadFailureScope.Tag, cancellationToken,
                        runtimeGeneration: generation, readDefinition: definition, sourceTimestampUtc: sourceTimestampUtc, deviceTimestampUtc: update.SourceTimestampUtc);
                    if (!historical) ScheduleNextRead(deviceState, group, tag, generation, true);
                }
                await failureOperation.ConfigureAwait(false);
                return;
            }
        }

        private static bool TryFindTagById(DeviceConfig device, string tagId, out GroupConfig? group, out TagConfig? tag)
        {
            group = null;
            tag = null;
            if (device == null || string.IsNullOrWhiteSpace(tagId))
                return false;

            if (device.Tags != null)
            {
                for (int i = 0; i < device.Tags.Count; i++)
                {
                    TagConfig candidate = device.Tags[i];
                    if (candidate != null && string.Equals(candidate.Id, tagId, StringComparison.OrdinalIgnoreCase))
                    {
                        tag = candidate;
                        return true;
                    }
                }
            }

            if (device.Groups == null)
                return false;

            for (int g = 0; g < device.Groups.Count; g++)
            {
                GroupConfig candidateGroup = device.Groups[g];
                if (candidateGroup == null || candidateGroup.Tags == null)
                    continue;

                for (int t = 0; t < candidateGroup.Tags.Count; t++)
                {
                    TagConfig candidate = candidateGroup.Tags[t];
                    if (candidate != null && string.Equals(candidate.Id, tagId, StringComparison.OrdinalIgnoreCase))
                    {
                        group = candidateGroup;
                        tag = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

    }
}
