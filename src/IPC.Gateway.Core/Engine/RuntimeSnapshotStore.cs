using System;
using System.Collections.Generic;
using IPC.Runtime.Indexing;
using IPC.Runtime.Values;

namespace IPC.Runtime.Engine
{
    internal sealed class RuntimeSnapshotStore
    {
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

        public bool Upsert(IDictionary<string, TagValueSnapshot> snapshots, TagValueSnapshot snapshot, out TagValueSnapshot clone)
        {
            string key = TagPath.BuildIdentity(snapshot.ChannelId, snapshot.DeviceId, snapshot.GroupId, snapshot.TagId);
            clone = snapshot.Clone();

            TagValueSnapshot? previous;
            bool changed = !snapshots.TryGetValue(key, out previous) || HasChanged(previous, clone);
            snapshots[key] = clone;
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
                   !string.Equals(previous.ErrorMessage ?? string.Empty, current.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        }
    }
}
