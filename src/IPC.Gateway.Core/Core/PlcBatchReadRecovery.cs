using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace IPC.Plc.Communication.Core;

/// <summary>
/// Bounds diagnostic work without losing successful values. Split boundaries belong to
/// one client, expire automatically and never originate from a connection failure.
/// </summary>
public sealed class PlcBatchReadRecovery
{
    private static readonly ConditionalWeakTable<object, BoundaryCache> Caches = new();
    private readonly BoundaryCache _cache;
    private readonly string _profile;
    // Reserve the fourth diagnostic request for a single item. Otherwise a
    // large ambiguous range could consume every split without resolving any
    // item, and cache eviction would restart that unproductive work forever.
    private int _remainingReads = 3;
    private bool _singleFallbackAvailable = true;

    private PlcBatchReadRecovery(object? owner, string profile)
    {
        _cache = owner == null ? new BoundaryCache() : Caches.GetValue(owner, _ => new BoundaryCache());
        _profile = profile;
    }

    public static PlcBatchReadRecovery Start(object? owner, string profile) => new(owner, profile);
    public bool ShouldAvoidAttempt<T>(IList<T> items, Func<T, PlcBatchReadRequest> request, string operation)
        => _cache.Contains(Key(items, 0, items.Count, request, "operation:" + operation));
    public void RememberUnsupportedAttempt<T>(IList<T> items, Func<T, PlcBatchReadRequest> request, string operation)
        => _cache.Remember(Key(items, 0, items.Count, request, "operation:" + operation));

    public void ReadRange<T>(IList<T> items, int start, int end,
        Func<T, PlcBatchReadRequest> request, Func<T, int> index,
        Action<int, int> read, PlcBatchReadResult[] results, string area)
    {
        ReadRangeCore(items, start, end, request, index, read, results, area, false);
        if (TryReserveSingleFallback(items, start, end, index, results))
            ReadRangeCore(items, start, start + 1, request, index, read, results, area, false);
    }

    public async ValueTask ReadRangeAsync<T>(IList<T> items, int start, int end,
        Func<T, PlcBatchReadRequest> request, Func<T, int> index,
        Func<int, int, ValueTask> read, PlcBatchReadResult[] results, string area,
        CancellationToken cancellationToken)
    {
        await ReadRangeCoreAsync(items, start, end, request, index, read, results, area, false, cancellationToken).ConfigureAwait(false);
        if (TryReserveSingleFallback(items, start, end, index, results))
            await ReadRangeCoreAsync(items, start, start + 1, request, index, read, results, area, false, cancellationToken).ConfigureAwait(false);
    }

    private bool TryReserveSingleFallback<T>(IList<T> items, int start, int end, Func<T, int> index, PlcBatchReadResult[] results)
    {
        if (!_singleFallbackAvailable || start >= end) return false;
        for (int i = start; i < end; i++)
            if (results[index(items[i])] is { IsDeferred: false }) return false;
        _singleFallbackAvailable = false;
        results[index(items[start])] = null!;
        return true;
    }

    private void ReleaseSingleReserveOnProgress<T>(IList<T> items, int start, int end,
        Func<T, int> index, PlcBatchReadResult[] results)
    {
        if (!_singleFallbackAvailable) return;
        for (int i = start; i < end; i++)
        {
            if (results[index(items[i])] is not { IsDeferred: false }) continue;
            _singleFallbackAvailable = false;
            _remainingReads++;
            return;
        }
    }

    private void ReadRangeCore<T>(IList<T> items, int start, int end,
        Func<T, PlcBatchReadRequest> request, Func<T, int> index,
        Action<int, int> read, PlcBatchReadResult[] results, string area, bool diagnostic)
    {
        string key = end - start > 1 ? Key(items, start, end, request, area) : string.Empty;
        if (end - start > 1 && _cache.Contains(key))
        {
            Split();
            return;
        }
        if (diagnostic && _remainingReads <= 0)
        {
            Defer(items, start, end, request, index, results);
            return;
        }
        if (diagnostic) _remainingReads--;
        try
        {
            read(start, end);
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
        }
        catch (Exception error)
        {
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
            var scope = PlcFailureClassifier.Classify(error, PlcReadFailureScope.Batch);
            if (!PlcBatchReadResult.IsConnectionFailureScope(scope) &&
                (scope == PlcReadFailureScope.Tag || scope == PlcReadFailureScope.Batch) && end - start > 1)
            {
                _cache.Remember(key);
                Split();
                return;
            }
            Fail(items, start, end, request, index, results, error.Message, scope);
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
            if (PlcBatchReadResult.IsConnectionFailureScope(scope)) throw;
        }
        void Split()
        {
            int middle = start + (end - start) / 2;
            ReadRangeCore(items, start, middle, request, index, read, results, area, true);
            ReadRangeCore(items, middle, end, request, index, read, results, area, true);
        }
    }

    private async ValueTask ReadRangeCoreAsync<T>(IList<T> items, int start, int end,
        Func<T, PlcBatchReadRequest> request, Func<T, int> index,
        Func<int, int, ValueTask> read, PlcBatchReadResult[] results, string area, bool diagnostic,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = end - start > 1 ? Key(items, start, end, request, area) : string.Empty;
        if (end - start > 1 && _cache.Contains(key))
        {
            await Split().ConfigureAwait(false);
            return;
        }
        if (diagnostic && _remainingReads <= 0)
        {
            Defer(items, start, end, request, index, results);
            return;
        }
        if (diagnostic) _remainingReads--;
        try
        {
            await read(start, end).ConfigureAwait(false);
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
        }
        catch (Exception error)
        {
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
            var scope = PlcFailureClassifier.Classify(error, PlcReadFailureScope.Batch);
            if (!PlcBatchReadResult.IsConnectionFailureScope(scope) &&
                (scope == PlcReadFailureScope.Tag || scope == PlcReadFailureScope.Batch) && end - start > 1)
            {
                _cache.Remember(key);
                await Split().ConfigureAwait(false);
                return;
            }
            Fail(items, start, end, request, index, results, error.Message, scope);
            ReleaseSingleReserveOnProgress(items, start, end, index, results);
            if (PlcBatchReadResult.IsConnectionFailureScope(scope)) throw;
        }
        async ValueTask Split()
        {
            int middle = start + (end - start) / 2;
            await ReadRangeCoreAsync(items, start, middle, request, index, read, results, area, true, cancellationToken).ConfigureAwait(false);
            await ReadRangeCoreAsync(items, middle, end, request, index, read, results, area, true, cancellationToken).ConfigureAwait(false);
        }
    }

    private string Key<T>(IList<T> items, int start, int end, Func<T, PlcBatchReadRequest> request, string area)
    {
        var text = new StringBuilder(_profile).Append('|').Append(area).Append('|');
        for (int i = start; i < end; i++)
        {
            var q = request(items[i]);
            text.Append(q.Address.Length).Append(':').Append(q.Address).Append('/').Append((int)q.DataType)
                .Append('/').Append(q.ElementCount).Append('/').Append(q.ElementOffset).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void Defer<T>(IList<T> items, int start, int end, Func<T, PlcBatchReadRequest> request,
        Func<T, int> index, PlcBatchReadResult[] results)
    {
        for (int i = start; i < end; i++)
            results[index(items[i])] ??= PlcBatchReadResult.FromDeferred(request(items[i]), "Batch diagnostic budget exhausted; read deferred.");
    }

    private static void Fail<T>(IList<T> items, int start, int end, Func<T, PlcBatchReadRequest> request,
        Func<T, int> index, PlcBatchReadResult[] results, string message, PlcReadFailureScope scope)
    {
        for (int i = start; i < end; i++)
            results[index(items[i])] ??= PlcBatchReadResult.FromFailure(request(items[i]), message, scope);
    }

    private sealed class BoundaryCache
    {
        private const int Capacity = 256;
        private readonly Dictionary<string, long> _boundaries = new(StringComparer.Ordinal);
        public bool Contains(string key)
        {
            lock (_boundaries)
            {
                if (!_boundaries.TryGetValue(key, out long expires)) return false;
                if (Environment.TickCount64 < expires) return true;
                _boundaries.Remove(key);
                return false;
            }
        }
        public void Remember(string key)
        {
            lock (_boundaries)
            {
                if (_boundaries.Count >= Capacity)
                {
                    string? oldest = null;
                    long firstExpiry = long.MaxValue;
                    foreach (var entry in _boundaries)
                        if (entry.Value < firstExpiry) { oldest = entry.Key; firstExpiry = entry.Value; }
                    if (oldest != null) _boundaries.Remove(oldest);
                }
                _boundaries[key] = Environment.TickCount64 + 45_000;
            }
        }
    }
}
