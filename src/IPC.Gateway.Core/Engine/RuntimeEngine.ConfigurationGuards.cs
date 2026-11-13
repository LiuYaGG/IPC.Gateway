using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;

namespace IPC.Runtime.Engine;

public sealed partial class RuntimeEngine
{
    private readonly object _configurationApplySyncRoot = new();
    private readonly ConcurrentDictionary<string, byte> _valueProcessingResetTagIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _configurationRefreshTagIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<DeviceRuntimeState, byte> _subscriptionCleanupScheduled = new();

    private bool ConsumeValueProcessingReset(string? tagId)
    {
        if (string.IsNullOrEmpty(tagId)) return false;
        _configurationRefreshTagIds.TryRemove(tagId, out _);
        return _valueProcessingResetTagIds.TryRemove(tagId, out _);
    }

    private bool IsConfigurationRefreshPending(string? tagId) =>
        !string.IsNullOrEmpty(tagId) && _configurationRefreshTagIds.ContainsKey(tagId);

    private bool IsValueProcessingResetPending(string? tagId) =>
        !string.IsNullOrEmpty(tagId) && _valueProcessingResetTagIds.ContainsKey(tagId);

    private string? GetWriteTargetError(DeviceConfig device, GroupConfig? group, TagConfig tag)
    {
        if (!device.Enabled) return "Device is disabled.";
        if (!_configuredChannelScheduler.IsEnabled(device)) return "Configured channel is disabled.";
        if (group != null && !group.Enabled) return "Group is disabled.";
        if (!tag.Enabled) return "Tag is disabled.";
        if (!CanWrite(tag)) return "Tag is read-only.";
        return null;
    }

    private bool TryResolveCurrentWriteTarget(
        DeviceRuntimeState state, DeviceConfig originalDevice, GroupConfig? originalGroup, TagConfig originalTag,
        out DeviceConfig device, out GroupConfig? group, out TagConfig tag, out string error)
    {
        lock (_syncRoot)
        {
            device = state.Config;
            group = null;
            tag = null!;
            error = "Tag configuration changed while the write was waiting; submit the write again.";
            if (!IsCurrentDeviceState(state) ||
                !string.Equals(device.Id, originalDevice.Id, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(device.ChannelId, originalDevice.ChannelId, StringComparison.OrdinalIgnoreCase) ||
                !TryFindTagById(device, originalTag.Id, out group, out TagConfig? currentTag) || currentTag == null ||
                !string.Equals(group?.Id ?? string.Empty, originalGroup?.Id ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !HasSameWriteDefinition(originalDevice, originalTag, device, currentTag))
                return false;
            tag = currentTag;
            string? targetError = GetWriteTargetError(device, group, tag);
            if (targetError != null)
            {
                error = targetError;
                return false;
            }
            error = string.Empty;
            return true;
        }
    }

    private static bool HasSameWriteDefinition(DeviceConfig oldDevice, TagConfig oldTag, DeviceConfig device, TagConfig tag) =>
        string.Equals(ResolveTagAddress(oldDevice, oldTag), ResolveTagAddress(device, tag), StringComparison.Ordinal) &&
        oldTag.DataType == tag.DataType && oldTag.ElementCount == tag.ElementCount &&
        oldTag.ElementOffset == tag.ElementOffset && oldTag.AccessMode == tag.AccessMode &&
        oldTag.IsVirtual == tag.IsVirtual &&
        JsonSerializer.Serialize(oldTag.Scaling) == JsonSerializer.Serialize(tag.Scaling);

    private static bool IsChannelEnabled(ProjectConfig? project, string channelId)
    {
        if (project?.Channels != null)
            foreach (ChannelConfig channel in project.Channels)
                if (channel != null && string.Equals(channel.Id, channelId, StringComparison.OrdinalIgnoreCase))
                    return channel.Enabled;
        return true;
    }

    private static bool HasSameValueProcessing(TagConfig oldTag, TagConfig tag) =>
        JsonSerializer.Serialize(oldTag.Scaling) == JsonSerializer.Serialize(tag.Scaling) &&
        JsonSerializer.Serialize(oldTag.Cleaning) == JsonSerializer.Serialize(tag.Cleaning);

    private void ApplyConfigurationScheduleTransitions(ProjectConfig? previousConfig, ProjectConfig config,
        ISet<string> devicesToPollImmediately, DateTime nowUtc)
    {
        var previousDevices = new Dictionary<string, DeviceConfig>(StringComparer.OrdinalIgnoreCase);
        if (previousConfig?.Devices != null)
            foreach (DeviceConfig device in previousConfig.Devices)
                if (device != null) previousDevices[device.Id] = device;
        var currentTagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DeviceConfig device in config.Devices)
        {
            if (device == null || !_deviceStatesById.TryGetValue(device.Id, out DeviceRuntimeState? state)) continue;
            previousDevices.TryGetValue(device.Id, out DeviceConfig? previousDevice);
            var previousTags = new Dictionary<string, (GroupConfig? Group, TagConfig Tag)>(StringComparer.OrdinalIgnoreCase);
            if (previousDevice != null)
            {
                foreach (TagConfig tag in previousDevice.Tags)
                    if (tag != null) previousTags[tag.Id] = (null, tag);
                foreach (GroupConfig group in previousDevice.Groups)
                    if (group?.Tags != null)
                        foreach (TagConfig tag in group.Tags)
                            if (tag != null) previousTags[tag.Id] = (group, tag);
            }
            bool channelEnabled = IsChannelEnabled(config, device.ChannelId);
            bool parentReenabled = device.Enabled && channelEnabled && previousDevice != null &&
                (!previousDevice.Enabled || !IsChannelEnabled(previousConfig, previousDevice.ChannelId));

            if (!device.Enabled || !channelEnabled)
            {
                InvalidateSubscriptionDispatcher(state);
                state.SubscriptionFingerprint = string.Empty;
                // Cleanup runs after an already dispatched operation, while
                // notification validation stops accepting values immediately.
                ScheduleDisabledSubscriptionCleanup(state);
            }

            ApplyTagTransitions(device, null, device.Tags);
            foreach (GroupConfig group in device.Groups)
                if (group != null) ApplyTagTransitions(device, group, group.Tags);

            // An existing subscription is updated only when its actual monitored
            // definitions or sampling intervals changed; renames/metadata do not
            // interrupt valid monitors.
            if (state.Subscription?.IsActive == true && device.Enabled && channelEnabled)
            {
                List<PlcSubscriptionRequest> requests = BuildSubscriptionRequests(state);
                string fingerprint = CreateSubscriptionFingerprint(requests, CreateSubscriptionOptions(requests));
                if (!string.Equals(fingerprint, state.SubscriptionFingerprint, StringComparison.Ordinal))
                {
                    state.SubscriptionFingerprint = string.Empty;
                    state.NextPollUtc = nowUtc;
                    devicesToPollImmediately.Add(device.Id);
                }
            }

            void ApplyTagTransitions(DeviceConfig currentDevice, GroupConfig? group, IList<TagConfig>? tags)
            {
                if (tags == null) return;
                foreach (TagConfig tag in tags)
                {
                    if (tag == null) continue;
                    currentTagIds.Add(tag.Id);
                    if (!device.Enabled || !channelEnabled || !(group?.Enabled ?? true) || !tag.Enabled || !CanDeviceRead(tag))
                        state.ReadPlan.Get(tag).Runtime.ResetHealthyBaseline();
                    if (previousDevice == null || !previousTags.TryGetValue(tag.Id, out var previous))
                        continue;
                    GroupConfig? oldGroup = previous.Group;
                    TagConfig oldTag = previous.Tag;
                    bool processingChanged = !HasSameValueProcessing(oldTag, tag);
                    if (processingChanged) _valueProcessingResetTagIds[tag.Id] = 0;
                    bool groupReenabled = (group?.Enabled ?? true) && !(oldGroup?.Enabled ?? true);
                    bool tagReenabled = tag.Enabled && !oldTag.Enabled;
                    bool scheduleChanged = GetEffectiveScanRateMs(previousDevice, oldGroup) != GetEffectiveScanRateMs(currentDevice, group) ||
                        GetFailureRetryDelayMs(previousDevice, oldTag) != GetFailureRetryDelayMs(currentDevice, tag);
                    if (!device.Enabled || !channelEnabled || !(group?.Enabled ?? true) || !tag.Enabled || !CanDeviceRead(tag) ||
                        !(parentReenabled || groupReenabled || tagReenabled || scheduleChanged || processingChanged)) continue;
                    TagRuntimeState runtime = state.ReadPlan.Get(tag).Runtime;
                    _configurationRefreshTagIds[tag.Id] = 0;
                    _nextReadUtcByTagId[tag.Id] = runtime.HasRecoveryWork ? runtime.NextRecoveryProbeUtc : nowUtc;
                    if (_nextReadUtcByTagId[tag.Id] <= nowUtc) devicesToPollImmediately.Add(device.Id);
                }
            }
        }
        foreach (string tagId in _valueProcessingResetTagIds.Keys)
            if (!currentTagIds.Contains(tagId)) _valueProcessingResetTagIds.TryRemove(tagId, out _);
        foreach (string tagId in _configurationRefreshTagIds.Keys)
            if (!currentTagIds.Contains(tagId)) _configurationRefreshTagIds.TryRemove(tagId, out _);
    }

    private void ScheduleDisabledSubscriptionCleanup(DeviceRuntimeState state)
    {
        if (!_subscriptionCleanupScheduled.TryAdd(state, 0)) return;
        bool TrySchedule() => state.Actor.PostLatestAsync("subscription-parent-disabled", token =>
        {
            try
            {
                IPlcSubscription? subscription = null;
                lock (_syncRoot)
                {
                    if (!state.Config.Enabled || !_configuredChannelScheduler.IsEnabled(state.Config))
                    {
                        subscription = state.Subscription;
                        state.Subscription = null;
                    }
                }
                try { subscription?.Dispose(); } catch { }
            }
            finally { _subscriptionCleanupScheduled.TryRemove(state, out _); }
            return System.Threading.Tasks.ValueTask.CompletedTask;
        });
        if (TrySchedule()) return;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (!_disposed)
            {
                await System.Threading.Tasks.Task.Delay(10).ConfigureAwait(false);
                if (TrySchedule()) return;
            }
            _subscriptionCleanupScheduled.TryRemove(state, out _);
        });
    }
}
