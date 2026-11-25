using System.IO.Compression;
using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed partial class HistoryValueIndex
{
    // Byte offsets always refer to the decompressed stream. A checksum over complete
    // lines identifies the former active file after rotation, even after gzip/move.
    private const ulong HashSeed = 14695981039346656037UL;
    private sealed record Source(long Id, string Name, long Offset, ulong Checksum, long Size, long Modified, string PhysicalPath);
    private sealed record Point(string ChannelId, string DeviceId, string GroupId, string TagId,
        string Channel, string Device, string Group, string Tag);
    private const string Schema = """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        CREATE TABLE IF NOT EXISTS history_meta(epoch TEXT NOT NULL);
        INSERT INTO history_meta SELECT lower(hex(randomblob(16))) WHERE NOT EXISTS (SELECT 1 FROM history_meta);
        CREATE TABLE IF NOT EXISTS history_file(id INTEGER PRIMARY KEY, name TEXT UNIQUE NOT NULL,
            offset INTEGER NOT NULL,checksum INTEGER NOT NULL,size INTEGER NOT NULL,modified INTEGER NOT NULL,physical TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS history_point(id INTEGER PRIMARY KEY,
            channel_id TEXT COLLATE NOCASE NOT NULL,device_id TEXT COLLATE NOCASE NOT NULL,
            group_id TEXT COLLATE NOCASE NOT NULL,tag_id TEXT COLLATE NOCASE NOT NULL,
            channel_name TEXT NOT NULL,device_name TEXT NOT NULL,group_name TEXT NOT NULL,tag_name TEXT NOT NULL,path TEXT NOT NULL,
            UNIQUE(channel_id,device_id,group_id,tag_id,path));
        CREATE INDEX IF NOT EXISTS history_path ON history_point(path);
        CREATE TABLE IF NOT EXISTS history_value(seq INTEGER PRIMARY KEY AUTOINCREMENT,
            file_id INTEGER NOT NULL REFERENCES history_file(id) ON DELETE CASCADE,
            point_id INTEGER NOT NULL,ticks INTEGER NOT NULL,value TEXT,raw TEXT,quality TEXT COLLATE NOCASE NOT NULL,
            dtype TEXT, unit TEXT,error TEXT,processing TEXT,details TEXT);
        CREATE INDEX IF NOT EXISTS history_file_rows ON history_value(file_id);
        CREATE INDEX IF NOT EXISTS history_time ON history_value(ticks DESC,seq DESC);
        CREATE INDEX IF NOT EXISTS history_point_time ON history_value(point_id,ticks DESC,seq DESC);
        """;

    private async Task RunBackgroundAsync(CancellationToken ct)
    {
        bool initialized = false;
        string? lastFailure = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!initialized)
                    {
                        Directory.CreateDirectory(Path.Combine(_hot, ".query-index"));
                        using var schema = Open();
                        using var cmd = schema.CreateCommand();
                        cmd.CommandText = Schema;
                        cmd.ExecuteNonQuery();
                        initialized = true;
                    }
                    Synchronize(ct);
                    _ready = true;
                    lastFailure = null;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _ready = false;
                    if (lastFailure != ex.Message)
                        IpcLogService.WriteError("History query index refresh failed.", ex);
                    lastFailure = ex.Message;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
        catch (Exception) when (ct.IsCancellationRequested) { }
        finally { _ready = false; }
    }

    private Dictionary<string, FileInfo> ListSources()
    {
        var result = new Dictionary<string, FileInfo>(StringComparer.Ordinal);
        foreach (string directory in new[] { _hot, _cold, Path.Combine(_hot, ".ha-replica") }.Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "values-*.jsonl*"))
            {
                if (!LocalHistoryService.TryGetHistoryFileDate(path, out _)) continue;
                var file = new FileInfo(path);
                string name = file.Name.EndsWith(".gz", StringComparison.Ordinal) ? file.Name[..^3] : file.Name;
                // Prefer the original during the short archive hand-over. Its offset is unchanged.
                if (!result.TryGetValue(name, out var previous) || previous.Extension == ".gz" && file.Extension != ".gz")
                    result[name] = file;
            }
        }
        return result;
    }

    private readonly Dictionary<string, (long Size, long Modified)> _badArchives = new(StringComparer.Ordinal);

    private void Synchronize(CancellationToken ct)
    {
        var files = ListSources();
        using var db = Open();
        using var cancel = InterruptOnCancel(db, ct);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id,name,offset,checksum,size,modified,physical FROM history_file";
        var known = new Dictionary<string, Source>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var source = new Source(reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2), unchecked((ulong)reader.GetInt64(3)),
                    reader.GetInt64(4), reader.GetInt64(5), reader.GetString(6));
                known.Add(source.Name, source);
            }

        foreach (var (name, file) in files.OrderBy(x => x.Key.Length == "values-yyyyMMdd.jsonl".Length)
                     .ThenBy(x => x.Key, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            long size = file.Length, modified = file.LastWriteTimeUtc.Ticks;
            known.TryGetValue(name, out var old);
            if (old != null && old.PhysicalPath == file.FullName && old.Size == size && old.Modified == modified) continue;
            if (_badArchives.TryGetValue(file.FullName, out var failed) && failed == (size, modified)) continue;
            // A completed file is an independent checkpoint. Moving a former active file
            // to its sealed name retains all row IDs in the same transaction.
            using var tx = db.BeginTransaction();
            try
            {
                string daily = name[..15] + ".jsonl"; // values-yyyyMMdd
                bool renamed = UpdateFile(db, tx, name, file, size, modified, old,
                    name != daily && !name.Contains("-ha-",StringComparison.Ordinal) ? known.GetValueOrDefault(daily) : null, ct);
                ct.ThrowIfCancellationRequested();
                tx.Commit();
                if (renamed) known.Remove(daily);
                _badArchives.Remove(file.FullName);
            }
            catch (InvalidDataException ex) when (file.Extension == ".gz")
            {
                // An invalid archive must not disable queries for the other days.
                _badArchives[file.FullName] = (size, modified);
                IpcLogService.WriteError("Skipping invalid history archive " + file.Name, ex);
            }
        }

        var missing = known.Values.Where(source => !files.ContainsKey(source.Name)).ToList();
        if (missing.Count != 0)
        {
            using var tx = db.BeginTransaction();
            foreach (var source in missing)
                Execute(db, tx, "DELETE FROM history_file WHERE id=$id", source.Id);
            InvalidateRemovedRows(db, tx);
            ct.ThrowIfCancellationRequested();
            tx.Commit();
        }
        foreach (var path in _badArchives.Keys.Where(path => !File.Exists(path)).ToList()) _badArchives.Remove(path);
    }

    private static bool UpdateFile(SqliteConnection db, SqliteTransaction tx, string name, FileInfo file, long size,
        long modified, Source? old, Source? former, CancellationToken ct)
    {
        using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new IndexWriter(db, tx);
        using var gzip = file.Extension == ".gz" ? new GZipStream(input, CompressionMode.Decompress, true) : null;
        Stream content = (Stream?)gzip ?? input;
        bool renamed = false;
        if (old == null && former?.Offset > 0 && MatchesPrefix(content, former, ct))
        {
            old = former;
            renamed = true;
            Execute(db, tx, "UPDATE history_file SET name=$value WHERE id=$id", former.Id, name);
        }
        else if (old != null)
        {
            if (old.PhysicalPath == file.FullName && content.CanSeek && size > old.Size)
                content.Position = old.Offset;
            else if (!MatchesPrefix(content, old, ct))
            {
                Execute(db, tx, "DELETE FROM history_file WHERE id=$id", old.Id);
                InvalidateRemovedRows(db, tx);
                old = null;
            }
        }
        if (old == null)
        {
            input.Position = 0;
            if (gzip != null)
            {
                using var fresh = new GZipStream(input, CompressionMode.Decompress, true);
                StoreFile(db, tx, writer, fresh, null, name, file, size, modified, ct);
                return false;
            }
        }
        StoreFile(db, tx, writer, content, old, name, file, size, modified, ct);
        return renamed;
    }

    private static void InvalidateRemovedRows(SqliteConnection db, SqliteTransaction tx)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM history_point WHERE NOT EXISTS(SELECT 1 FROM history_value WHERE point_id=history_point.id);
            UPDATE history_meta SET epoch=lower(hex(randomblob(16)));
            """;
        cmd.ExecuteNonQuery();
    }

    private static ulong Hash(ulong hash, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) hash = unchecked((hash ^ b) * 1099511628211UL);
        return hash;
    }

    private static bool MatchesPrefix(Stream stream, Source previous, CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        ulong hash = HashSeed;
        long remaining = previous.Offset;
        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int n = stream.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
            if (n == 0) return false;
            hash = Hash(hash, buffer.AsSpan(0, n));
            remaining -= n;
        }
        return hash == previous.Checksum;
    }

    private static void Execute(SqliteConnection db, SqliteTransaction tx, string sql, long id, string? value = null)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$id", id);
        if (value != null) cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    private static void StoreFile(SqliteConnection db, SqliteTransaction tx, IndexWriter writer, Stream content, Source? old,
        string name, FileInfo file, long size, long modified, CancellationToken ct)
    {
        long offset = old?.Offset ?? 0;
        ulong checksum = old?.Checksum ?? HashSeed;
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        long fileId;
        if (old == null)
        {
            cmd.CommandText = "INSERT INTO history_file(name,offset,checksum,size,modified,physical) VALUES($name,0,0,0,0,'') RETURNING id";
            cmd.Parameters.AddWithValue("$name", name);
            fileId = (long)cmd.ExecuteScalar()!;
            cmd.Parameters.Clear();
        }
        else fileId = old.Id;

        // A StreamReader buffers past the end of a line. Track complete UTF-8 lines explicitly
        // so partial writes, long arrays and multibyte text resume at the correct byte offset.
        byte[] buffer = new byte[65536];
        using var partial = new MemoryStream();
        int bytes;
        long remaining = content.CanSeek ? Math.Max(0, size - content.Position) : long.MaxValue;
        while (remaining > 0 && (bytes = content.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            ct.ThrowIfCancellationRequested();
            remaining -= bytes;
            int start = 0;
            while (start < bytes)
            {
                int end = Array.IndexOf(buffer, (byte)'\n', start, bytes - start);
                int count = (end < 0 ? bytes : end) - start;
                if (end < 0) { partial.Write(buffer, start, count); break; }
                ReadOnlySpan<byte> line;
                if (partial.Length > 0)
                {
                    partial.Write(buffer, start, count);
                    line = partial.GetBuffer().AsSpan(0, (int)partial.Length);
                }
                else line = buffer.AsSpan(start, count);
                checksum = unchecked((Hash(checksum, line) ^ 10) * 1099511628211UL);
                offset += line.Length + 1;
                if (line.StartsWith("\uFEFF"u8)) line = line[3..];
                writer.Add(fileId, line);
                partial.SetLength(0);
                start = end + 1;
            }
        }
        cmd.CommandText = "UPDATE history_file SET offset=$offset,checksum=$hash,size=$size,modified=$mtime,physical=$path WHERE id=$id";
        cmd.Parameters.AddWithValue("$offset", offset);
        cmd.Parameters.AddWithValue("$hash", unchecked((long)checksum));
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$mtime", modified);
        cmd.Parameters.AddWithValue("$path", file.FullName);
        cmd.Parameters.AddWithValue("$id", fileId);
        cmd.ExecuteNonQuery();
    }

    private sealed class IndexWriter : IDisposable
    {
        private readonly SqliteCommand _point;
        private readonly SqliteCommand _sample;
        private readonly Dictionary<Point, long> _points = new();
        public IndexWriter(SqliteConnection db, SqliteTransaction tx)
        {
            _point = db.CreateCommand();
            _point.Transaction = tx;
            _point.CommandText = """
                INSERT INTO history_point(channel_id,device_id,group_id,tag_id,channel_name,device_name,group_name,tag_name,path)
                VALUES($c,$d,$g,$t,$cn,$dn,$gn,$tn,$p) ON CONFLICT DO NOTHING;
                SELECT id FROM history_point WHERE channel_id=$c AND device_id=$d AND group_id=$g AND tag_id=$t AND path=$p;
                """;
            _sample = db.CreateCommand();
            _sample.Transaction = tx;
            _sample.CommandText = """
                INSERT INTO history_value(file_id,point_id,ticks,value,raw,quality,dtype,unit,error,processing,details)
                VALUES($f,$p,$time,$v,$raw,$q,$dt,$unit,$err,$proc,$details)
                """;
            foreach (string p in new[] { "$c", "$d", "$g", "$t", "$cn", "$dn", "$gn", "$tn", "$p" }) _point.Parameters.Add(new SqliteParameter(p, DBNull.Value));
            foreach (string p in new[] { "$f", "$p", "$time", "$v", "$raw", "$q", "$dt", "$unit", "$err", "$proc", "$details" }) _sample.Parameters.Add(new SqliteParameter(p, DBNull.Value));
            _point.Prepare();
            _sample.Prepare();
        }

        private static JsonDocument? ReadRecord(ReadOnlySpan<byte> line)
        {
            var utf8 = new Utf8JsonReader(line);
            try { return JsonDocument.ParseValue(ref utf8); }
            catch (JsonException)
            {
                // Older gateway writers left some ASCII control characters unescaped
                // inside strings. Repair those only; skip independently malformed lines.
                var escaped = new StringBuilder();
                bool quoted = false, slash = false, repaired = false;
                foreach (char c in Encoding.UTF8.GetString(line))
                {
                    if (quoted && c < ' ')
                    {
                        escaped.Append("\\u").Append(((int)c).ToString("X4"));
                        repaired = true;
                    }
                    else escaped.Append(c);
                    if (slash) slash = false;
                    else if (c == '\\' && quoted) slash = true;
                    else if (c == '"') quoted = !quoted;
                }
                if (repaired)
                {
                    try { return JsonDocument.Parse(escaped.ToString()); }
                    catch (JsonException) { }
                }
                return null;
            }
        }

        public void Add(long fileId, ReadOnlySpan<byte> line)
        {
            using var document = ReadRecord(line);
            if (document == null) return;
            if (document.RootElement.ValueKind != JsonValueKind.Object) return;
            string? Get(string key) => document.RootElement.TryGetProperty(key, out var field) && field.ValueKind != JsonValueKind.Null ? field.ToString() : null;
            if (!document.RootElement.TryGetProperty("timestamp", out var time) || time.ValueKind != JsonValueKind.String ||
                !time.TryGetDateTimeOffset(out var timestamp) || Get("type") != "value") return;
            var point = new Point(Get("channelId") ?? "", Get("deviceId") ?? "", Get("groupId") ?? "", Get("tagId") ?? "",
                Get("channel") ?? "", Get("device") ?? "", Get("group") ?? "", Get("tag") ?? "");
            if (point.TagId.Length == 0 && point.Tag.Length == 0) return;
            if (!_points.TryGetValue(point, out long pointId))
            {
                _point.Parameters["$c"].Value = point.ChannelId;
                _point.Parameters["$d"].Value = point.DeviceId;
                _point.Parameters["$g"].Value = point.GroupId;
                _point.Parameters["$t"].Value = point.TagId;
                _point.Parameters["$cn"].Value = point.Channel;
                _point.Parameters["$dn"].Value = point.Device;
                _point.Parameters["$gn"].Value = point.Group;
                _point.Parameters["$tn"].Value = point.Tag;
                _point.Parameters["$p"].Value = HistoryValuePath.Build(point.Channel, point.Device,
                    point.GroupId.Length == 0 && point.Group.Length == 0 ? null : point.Group, point.Tag);
                pointId = (long)_point.ExecuteScalar()!;
                _points.Add(point, pointId);
            }
            string quality = Get("quality") ?? "Unknown";
            string? value = Get("valueText");
            string? raw = Get("rawValueText");
            if (quality is "NotConnected" or "Unknown" or "Bad" or "ReadError" or "AccessDenied" or "Disabled" or "NotFound")
            {
                // Legacy lines use empty text for a missing reading. A present
                // raw value (for example on a script failure) is still useful.
                if (value == "") value = null;
                if (value == null && raw == "") raw = null;
            }
            _sample.Parameters["$f"].Value = fileId;
            _sample.Parameters["$p"].Value = pointId;
            _sample.Parameters["$time"].Value = timestamp.UtcTicks;
            _sample.Parameters["$v"].Value = (object?)value ?? DBNull.Value;
            _sample.Parameters["$raw"].Value = (object?)raw ?? DBNull.Value;
            _sample.Parameters["$q"].Value = quality;
            _sample.Parameters["$dt"].Value = (object?)Get("dataType") ?? DBNull.Value;
            _sample.Parameters["$unit"].Value = (object?)Get("unit") ?? DBNull.Value;
            _sample.Parameters["$err"].Value = (object?)Get("errorMessage") ?? DBNull.Value;
            _sample.Parameters["$proc"].Value = (object?)Get("edgeProcessingType") ?? DBNull.Value;
            Dictionary<string, JsonElement>? details = null;
            foreach (var field in document.RootElement.EnumerateObject())
            {
                if (field.Name is not ("cleaningApplied" or "cleaningAction" or "cleaningMessage" or "edgeProcessingReason" or
                    "edgeAggregateMethod" or "edgeSampleCount" or "edgeOriginalTimestamp" or "edgeWindowStart" or "edgeWindowEnd")) continue;
                if (field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.False || field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() == "") continue;
                (details ??= new())[field.Name] = field.Value;
            }
            _sample.Parameters["$details"].Value = details == null ? DBNull.Value : JsonSerializer.Serialize(details);
            _sample.ExecuteNonQuery();
        }
        public void Dispose()
        {
            _point.Dispose();
            _sample.Dispose();
        }
    }
}
