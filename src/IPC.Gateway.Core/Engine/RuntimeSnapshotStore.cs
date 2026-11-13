using System;
using System.Collections.Generic;
using System.Threading;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeSnapshotStore
    {
        private static long _revision;

        // All commits are serialized by RuntimeEngine's configuration/state lock.
        public void Set(IDictionary<string, TagValueSnapshot> snapshots, string key, TagValueSnapshot snapshot)
        {
            snapshot.RuntimeRevision = Interlocked.Increment(ref _revision);
            snapshots[key] = snapshot;
        }

        public bool TryGetById(
            IDictionary<string, TagValueSnapshot> snapshots,
            string channelId,
            string deviceId,
            string groupId,
            string tagId,
            out TagValueSnapshot? snapshot)
        {
            string key = TagPath.BuildIdentity(channelId, deviceId, groupId, tagId);
            if (snapshots.TryGetValue(key, out TagValueSnapshot? current) && current != null)
            {
                snapshot = current.Clone();
                return true;
            }

            snapshot = null;
            return false;
        }

        public List<TagValueSnapshot> GetAll(IDictionary<string, TagValueSnapshot> snapshots)
        {
            List<TagValueSnapshot> result = new List<TagValueSnapshot>();
            foreach (TagValueSnapshot snapshot in snapshots.Values)
            {
                if (snapshot != null)
                    result.Add(snapshot.Clone());
            }

            return result;
        }

        public bool Upsert(IDictionary<string, TagValueSnapshot> snapshots, TagValueSnapshot snapshot,
            out TagValueSnapshot clone, out bool diagnosticsOnly, bool preserveLastReadError = false)
        {
            string key = TagPath.BuildIdentity(snapshot.ChannelId, snapshot.DeviceId, snapshot.GroupId, snapshot.TagId);
            clone = snapshot.Clone();

            bool exists = snapshots.TryGetValue(key, out TagValueSnapshot? previous);
            if (preserveLastReadError && previous != null)
                clone.LastReadError = previous.LastReadError ?? string.Empty;
            bool changed = !exists || HasChanged(previous, clone);
            diagnosticsOnly = !changed && previous != null && (
                previous.TagState != clone.TagState ||
                previous.IsTagIsolated != clone.IsTagIsolated ||
                previous.IsStaticValidationError != clone.IsStaticValidationError ||
                previous.TagConsecutiveFailures != clone.TagConsecutiveFailures ||
                previous.NextTagRecoveryProbeTime != clone.NextTagRecoveryProbeTime);
            Set(snapshots, key, clone);
            return changed;
        }

        public bool HasChanged(TagValueSnapshot? previous, TagValueSnapshot current)
        {
            if (previous == null)
                return true;
            if (previous.Timestamp == DateTime.MinValue)
                return true;

            return !string.Equals(previous.RawValueText ?? string.Empty, current.RawValueText ?? string.Empty, StringComparison.Ordinal) ||
                   !string.Equals(previous.ValueText ?? string.Empty, current.ValueText ?? string.Empty, StringComparison.Ordinal) ||
                   !string.Equals(previous.DataType ?? string.Empty, current.DataType ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                   previous.Quality != current.Quality ||
                   previous.CleaningApplied != current.CleaningApplied ||
                   !string.Equals(previous.CleaningAction ?? string.Empty, current.CleaningAction ?? string.Empty, StringComparison.Ordinal) ||
                   !string.Equals(previous.CleaningMessage ?? string.Empty, current.CleaningMessage ?? string.Empty, StringComparison.Ordinal) ||
                   !string.Equals(previous.ErrorMessage ?? string.Empty, current.ErrorMessage ?? string.Empty, StringComparison.Ordinal) ||
                   !string.Equals(previous.LastReadError ?? string.Empty, current.LastReadError ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
