using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using IPC.EdgeGateway;

namespace IPC.Gateway.WebHost;

internal sealed class GatewayHistoryQueries
{
    internal readonly SemaphoreSlim ExternalSlots = new(2, 2);
    internal readonly SemaphoreSlim UiSlots = new(2, 2);
    internal readonly SemaphoreSlim ExportSlots = new(1, 1);
    private readonly SemaphoreSlim _downloadSlots = new(5, 5);
    private readonly TimeSpan _timeout;
    private readonly long _maxTempBytes;
    private readonly long _minFreeBytes;
    private readonly ILogger<GatewayHistoryQueries> _log;
    private readonly object _pageLock = new();
    private readonly Dictionary<string, PageTicket> _pages = new();
    private long _tempBytes;

    public GatewayHistoryQueries(IConfiguration configuration, ILogger<GatewayHistoryQueries> log)
    {
        var settings = configuration.GetSection("Gateway:HistoryQuery");
        _timeout = TimeSpan.FromSeconds(Math.Clamp(settings.GetValue("TimeoutSeconds", 15), 1, 3600));
        _maxTempBytes = (long)Math.Clamp(settings.GetValue("MaxTemporaryMiB", 512), 1, 1024 * 1024) * 1024 * 1024;
        _minFreeBytes = (long)Math.Clamp(settings.GetValue("MinimumFreeMiB", 64), 1, 1024 * 1024) * 1024 * 1024;
        _log = log;
    }

    internal async Task<IResult> Run(HttpContext ctx, SemaphoreSlim slots, Func<CancellationToken,Task<IResult>> operation)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        IResult Error(int status, string code, string zh, string en) => GatewayHistoryEndpoints.Error(ctx, status, code, zh, en);
        if (!GatewayAuthEndpoints.CanViewHistory(ctx.User))
            return Error(403, "HISTORY_FORBIDDEN", "缺少历史查看权限。", "History view permission is required.");
        if (!slots.Wait(0))
        {
            ctx.Response.Headers.RetryAfter = "1";
            return Error(429, "HISTORY_QUERY_BUSY", "历史查询繁忙，请稍后重试。", "History queries are busy. Try again shortly.");
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
        deadline.CancelAfter(_timeout);
        try
        {
            var result = await operation(deadline.Token);
            if (result is IAsyncDisposable file) ctx.Response.RegisterForDisposeAsync(file);
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception) when (ctx.RequestAborted.IsCancellationRequested) { throw new OperationCanceledException(ctx.RequestAborted); }
        catch (Exception) when (deadline.IsCancellationRequested)
        {
            return Error(503, "HISTORY_QUERY_TIMEOUT", "历史查询超时，请缩短时间范围或稍后重试。", "History query timed out. Narrow the time range or retry later.");
        }
        catch (HistoryPathConflictException) { return Error(409, "HISTORY_PATH_CONFLICT", "此时间段内同一路径对应多个点位。", "This path identifies multiple points during the requested interval."); }
        catch (HistorySnapshotExpiredException) { return Error(409, "HISTORY_SNAPSHOT_EXPIRED", "历史数据已变化，请重新查询。", "The historical result has expired. Run the query again."); }
        catch (HistoryIndexUnavailableException)
        {
            ctx.Response.Headers.RetryAfter = "2";
            return Error(503, "HISTORY_INDEX_UNAVAILABLE", "历史查询索引尚未就绪，请稍后重试。", "The history index is not ready. Try again shortly.");
        }
        catch (ArgumentException) { return Error(400, "HISTORY_INVALID_ARGUMENT", "查询参数无效，请检查点位、含时区的起止时间及分页参数。", "Invalid point, timestamp, pagination or unsupported query parameter."); }
        catch (TempSpaceException) { return Error(507, "HISTORY_TEMP_CAPACITY", "临时响应空间不足，请缩短时间范围或稍后重试。", "Insufficient temporary response space. Narrow the time range or try again later."); }
        catch (TooManyDownloadsException) { return Error(429, "HISTORY_QUERY_BUSY", "历史下载繁忙，请稍后重试。", "History downloads are busy. Try again shortly."); }
        catch (Exception ex)
        {
            _log.LogError(ex, "History query failed at {Endpoint}", ctx.Request.Path);
            return Error(503, "HISTORY_QUERY_UNAVAILABLE", "历史查询暂时不可用。", "Historical data is temporarily unavailable.");
        }
        finally { slots.Release(); }
    }

    internal async Task<IResult> PreparePublicResponseAsync(HistoryValueIndex index, HistoryValueFilter filter, long size, CancellationToken ct)
    {
        int? limit = size is > 0 and <= int.MaxValue ? (int)size : null;
        await using var stream = await index.OpenStreamAsync(filter, null, ct, take: limit, countTotal: false);
        var file = NewFile("application/json; charset=utf-8");
        try
        {
            using var writer = new Utf8JsonWriter(file.Stream);
            writer.WriteStartObject();
            writer.WriteBoolean("success", true);
            writer.WriteStartArray("data");
            long count = 0;
            await foreach (var row in stream.Items.WithCancellation(ct))
            {
                writer.WriteStartObject();
                writer.WriteString("timestamp", row.Timestamp.ToUniversalTime());
                writer.WriteString("path", row.Path);
                writer.WriteString("dataType", row.DataType);
                writer.WriteString("valueText", row.ValueText);
                writer.WriteString("rawValueText", row.RawValueText);
                writer.WriteString("unit", row.Unit);
                writer.WriteString("quality", row.Quality);
                writer.WriteEndObject();
                if (writer.BytesPending >= 32 * 1024)
                {
                    file.ReserveTo(writer.BytesCommitted + writer.BytesPending);
                    await writer.FlushAsync(ct);
                }
                if (++count == size) break;
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            file.ReserveTo(writer.BytesCommitted + writer.BytesPending);
            await writer.FlushAsync(ct);
            ct.ThrowIfCancellationRequested();
            return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }

    private sealed record PageTicket(string Owner, HistoryValueFilter Filter, int Page, int Size, string Snapshot, byte[] Digest, DateTime Expires);

    private static string Owner(ClaimsPrincipal user) =>
        user.Identity!.AuthenticationType + "/" + user.Identity.Name + "/" + (user.FindFirst("apiTokenId") ?? user.FindFirst("jti"))?.Value;

    private static byte[] Digest(IReadOnlyList<HistoryValueRow> rows, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        using var sink = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            JsonSerializer.Serialize(sink, row);
        }
        sink.FlushFinalBlock();
        return sha.Hash!;
    }

    internal string RememberPage(ClaimsPrincipal user, HistoryValueFilter filter, HistoryValuePage result, CancellationToken ct)
    {
        var ticket = new PageTicket(Owner(user), filter, result.Page, result.PageSize, result.Snapshot,
            Digest(result.Items,ct), DateTime.UtcNow.AddMinutes(5));
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        lock (_pageLock)
        {
            if (_pages.Count >= 512) _pages.Remove(_pages.MinBy(pair => pair.Value.Expires).Key);
            _pages.Add(key, ticket);
        }
        return key;
    }

    internal async Task<IReadOnlyList<HistoryValueRow>?> GetPageAsync(ClaimsPrincipal user, HistoryValueIndex index, string? token, CancellationToken ct)
    {
        PageTicket? ticket;
        lock (_pageLock)
        {
            if (token == null || !_pages.TryGetValue(token,out ticket) || ticket.Owner != Owner(user) || DateTime.UtcNow >= ticket.Expires) return null;
        }
        var page = await index.QueryAsync(ticket.Filter, ticket.Page, ticket.Size, ticket.Snapshot, ct);
        return Digest(page.Items, ct).AsSpan().SequenceEqual(ticket.Digest) ? page.Items : null;
    }

    internal IResult PrepareWorkbook(IReadOnlyList<HistoryValueRow> rows, bool english, CancellationToken ct)
    {
        var file = NewFile("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            (english ? "Tag-history-" : "点位历史-") + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".xlsx");
        try
        {
            using var output = new WorkbookOutput(file);
            HistoryPageWorkbook.Write(output, rows, english, ct);
            ct.ThrowIfCancellationRequested();
            return file;
        }
        catch { file.Dispose(); throw; }
    }

    private TemporaryResponse NewFile(string type, string? name = null)
    {
        if (!_downloadSlots.Wait(0)) throw new TooManyDownloadsException();
        try { return new TemporaryResponse(this,type,name); }
        catch { _downloadSlots.Release(); throw; }
    }

    private sealed class TempSpaceException : IOException;
    private sealed class TooManyDownloadsException : Exception;

    // XLSX uses ZipArchive. A forward-only stream lets us budget each compressed block before the write.
    private sealed class WorkbookOutput(TemporaryResponse file) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => file.Stream.Length;
        public override long Position { get => file.Stream.Position; set => throw new NotSupportedException(); }
        public override void Flush() => file.Stream.Flush();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            file.ReserveTo(file.Stream.Position + buffer.Length);
            file.Stream.Write(buffer);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // Hold the download slot and account its bytes until the HTTP transfer completes.
    private sealed class TemporaryResponse : IResult, IDisposable, IAsyncDisposable
    {
        private readonly GatewayHistoryQueries _owner;
        private readonly string _type;
        private readonly string? _name;
        private readonly DriveInfo _drive;
        private long _reserved, _nextDiskCheck;
        private int _disposed;
        public FileStream Stream { get; }

        public TemporaryResponse(GatewayHistoryQueries owner,string type,string? name)
        {
            _owner = owner;
            _type = type;
            _name = name;
            string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            _drive = new DriveInfo(Path.GetPathRoot(path)!);
            if (_drive.AvailableFreeSpace < owner._minFreeBytes) throw new TempSpaceException();
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Stream = new FileStream(path, options);
        }
        public void ReserveTo(long length)
        {
            long delta = length - _reserved;
            if (delta <= 0) return;
            if (length >= _nextDiskCheck)
            {
                if (_drive.AvailableFreeSpace - delta < _owner._minFreeBytes) throw new TempSpaceException();
                _nextDiskCheck = length + 1024 * 1024;
            }
            if (Interlocked.Add(ref _owner._tempBytes, delta) > _owner._maxTempBytes)
            {
                Interlocked.Add(ref _owner._tempBytes,-delta);
                throw new TempSpaceException();
            }
            _reserved = length;
        }
        public async Task ExecuteAsync(HttpContext context)
        {
            try
            {
                Stream.Position = 0;
                await Results.File(Stream, _type, _name).ExecuteAsync(context);
            }
            finally { Dispose(); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed,1) != 0) return;
            try { Stream.Dispose(); }
            finally
            {
                Interlocked.Add(ref _owner._tempBytes,-_reserved);
                _owner._downloadSlots.Release();
            }
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
