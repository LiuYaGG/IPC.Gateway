using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IPC.EdgeGateway;

public sealed record HistoryValueFilter(string Path, DateTimeOffset From, DateTimeOffset To,
    string? Quality = null, string? ChannelId = null, string? DeviceId = null, string? GroupId = null, string? TagId = null);
public sealed record HistoryValueRow(DateTimeOffset Timestamp, string Path, string ChannelId, string DeviceId,
    string GroupId, string TagId, string? ValueText, string? RawValueText, string Quality, string? DataType,
    string? Unit, string? ErrorMessage, string? EdgeProcessingType, JsonElement? Details = null);
public sealed record HistoryValuePage(long Total, int Page, int PageSize, string Snapshot, IReadOnlyList<HistoryValueRow> Items);
public sealed record HistoryPointOption(string Id, string Name, string? Path);
public sealed class HistoryIndexUnavailableException() : Exception("History query index is not ready.");
public sealed class HistoryPathConflictException() : Exception("The selected path belongs to multiple points in this time range.");
public sealed class HistorySnapshotExpiredException() : Exception("The history result has expired. Run the query again.");

public static class HistoryValuePath
{
    public static string Build(string? channel, string? device, string? group, string? tag)
    {
        static string Segment(string? value, string fallback) =>
            (string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()).Replace("\\", "\\\\").Replace(".", "\\.");
        string prefix = Segment(channel, "Channel") + "." + Segment(device, "Device");
        if (group != null) prefix += "." + Segment(group, "Group");
        return prefix + "." + Segment(tag, "Tag");
    }

    public static bool IsValid(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        int parts = 1, length = 0;
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '.')
            {
                if (length == 0 || ++parts > 4) return false;
                length = 0;
                continue;
            }
            if (path[i] == '\\' && (++i == path.Length || path[i] is not ('.' or '\\'))) return false;
            length++;
        }
        return parts >= 3 && length > 0;
    }
}

// A read transaction pins the WAL snapshot while a JSON or Excel response is streamed.
public sealed class HistoryValueStream : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly SqliteCommand _command;
    private readonly CancellationToken _request;
    internal HistoryValueStream(SqliteConnection connection, SqliteTransaction transaction, SqliteCommand command,
        long total, string snapshot, CancellationToken request)
    {
        _connection = connection;
        _transaction = transaction;
        _command = command;
        _request = request;
        Total = total;
        Snapshot = snapshot;
    }
    public long Total { get; }
    public string Snapshot { get; }
    public IAsyncEnumerable<HistoryValueRow> Items => Read(_request);

    private async IAsyncEnumerable<HistoryValueRow> Read([EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var cancel = HistoryValueIndex.InterruptOnCancel(_connection, ct);
        await using var reader = await _command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            string? Optional(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
            yield return new HistoryValueRow(new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                Optional(6), Optional(7) ?? Optional(6), reader.GetString(8), Optional(9), Optional(10), Optional(11), Optional(12),
                reader.IsDBNull(13) ? null : JsonSerializer.Deserialize<JsonElement>(reader.GetString(13)));
        }
    }

    public ValueTask DisposeAsync()
    {
        _command.Dispose();
        _transaction.Dispose();
        _connection.Dispose();
        return ValueTask.CompletedTask;
    }
}
