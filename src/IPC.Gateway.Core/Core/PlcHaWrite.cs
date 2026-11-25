using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IPC.Plc.Communication.Core;

public interface IPlcWriteAttemptAuthorizer
{
    bool IsCluster { get; }
    bool RequiresWriteAuthorization => IsCluster;
    bool CanRunBusiness { get; }
    long Epoch { get; }
    IDisposable? TrackWrite(long admittedEpoch);
    bool IsWriteDriverApproved(IPlcClient driver);
    void RecordOutcome(string attemptId, string result);
    ValueTask AuthorizeAsync(long epoch, string commandId, string attemptId, PlcWriteAttempt attempt, CancellationToken cancellationToken);
}

public sealed record PlcWriteExecution(string CommandId, string? LastAttemptId, int ConfirmedSegments, string Outcome);
public sealed record PlcWriteAttempt(string Digest, string TargetDigest, string Target, string Address, string SourceType, int SegmentIndex);

/// <summary>Implemented by drivers that check every independently effective write at the transport boundary.</summary>
public interface IHaGuardedWriteClient { }
public interface IHaConnectionAdmission { void CheckHaConnection(); }


public sealed class PlcWriteDeniedException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

public sealed record PlcWriteRequestContext(string CommandId, long? ExpectedEpoch)
{
    private static readonly AsyncLocal<PlcWriteRequestContext?> Current = new();
    public static PlcWriteRequestContext Capture() => Current.Value is { CommandId.Length: > 0 } request
        ? request : new(Guid.NewGuid().ToString("N"), Current.Value?.ExpectedEpoch);
    public static IDisposable BindChildWrites(long epoch)
    {
        var old = Current.Value;
        Current.Value = new("", old?.ExpectedEpoch ?? epoch);
        return new Restore(() => Current.Value = old);
    }
    public static IDisposable Push(string? commandId, long? expectedEpoch)
    {
        var old = Current.Value;
        Current.Value = new(string.IsNullOrWhiteSpace(commandId) ? Guid.NewGuid().ToString("N") : commandId, expectedEpoch);
        return new Restore(() => Current.Value = old);
    }
    internal sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}

/// <summary>One scope per logical write; a driver assigns a new ordered attempt to each segment.</summary>
public sealed class PlcHaWrite
{
    private static readonly AsyncLocal<PlcHaWrite?> Active = new();
    private readonly IPlcWriteAttemptAuthorizer _authority;
    private readonly long _epoch;
    private readonly string _commandId;
    private readonly string _targetDigest, _payloadDigest, _address, _source, _target;
    private readonly CancellationToken _token;
    private readonly Action? _beforeSend;
    private int _segment;
    private string? _unconfirmed, _lastAttempt;
    private int _confirmed;
    public PlcWriteExecution Result(bool success) => new(_commandId, _lastAttempt, _confirmed,
        _unconfirmed != null ? "Unknown" : success ? "Confirmed" : _confirmed > 0 ? "Partial" : "NotDispatched");
    public void Confirm()
    {
        if (_unconfirmed is { } attempt)
        {
            _authority.RecordOutcome(attempt, "Confirmed");
            _confirmed++;
            _unconfirmed = null;
        }
    }

    public PlcHaWrite(IPlcWriteAttemptAuthorizer authority, long epoch, PlcWriteRequestContext request, IPlcClient client,
        PlcConnectionOptions connection, string address, PlcDataType type, int count, int offset, string value,
        CancellationToken token, Action? beforeSend = null, string source = "Runtime", string? target = null)
    {
        if (client is not IHaGuardedWriteClient || !authority.IsWriteDriverApproved(client))
            throw new PlcWriteDeniedException("HA_DRIVER_WRITE_UNVERIFIED");
        if (request.ExpectedEpoch.HasValue && request.ExpectedEpoch != epoch) throw new PlcWriteDeniedException("HA_STALE_EPOCH");
        _authority = authority;
        _epoch = epoch;
        _commandId = request.CommandId;
        _target = AuditLabel(target ?? connection.Protocol.ToString());
        _address = AuditLabel(address);
        _source = source;
        _targetDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { connection.Protocol, connection.Host, connection.Port, address })));
        _payloadDigest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { connection, address, type, count, offset, value })));
        _token = token;
        _beforeSend = beforeSend;
    }

    private static string AuditLabel(string value) =>
        value.Length > 256 || value.Any(char.IsControl) || value.Contains("://", StringComparison.Ordinal) ||
        value.IndexOfAny(['?', '@']) >= 0 ? "[redacted]" : value;

    public IDisposable Enter()
    {
        var old = Active.Value;
        Active.Value = this;
        return new PlcWriteRequestContext.Restore(() => Active.Value = old);
    }

    public static ValueTask BeforeSegmentAsync() =>
        Active.Value is { } frame ? frame.AuthorizeSegmentAsync() : ValueTask.CompletedTask;
    public static void BeforeSegment() => BeforeSegmentAsync().AsTask().GetAwaiter().GetResult();

    private async ValueTask AuthorizeSegmentAsync()
    {
        Confirm(); // Reaching another segment implies that the previous call returned to the driver.
        _token.ThrowIfCancellationRequested();
        if (!_authority.CanRunBusiness || _authority.Epoch != _epoch) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
        var segment = Interlocked.Increment(ref _segment);
        string attemptId = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { _commandId, segment })));
        _lastAttempt = attemptId;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_payloadDigest + "/" + segment.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        await _authority.AuthorizeAsync(_epoch, _commandId, attemptId, new PlcWriteAttempt(digest, _targetDigest, _target, _address, _source, segment), _token).ConfigureAwait(false);
        try
        {
            _token.ThrowIfCancellationRequested();
            _beforeSend?.Invoke();
            if (!_authority.CanRunBusiness || _authority.Epoch != _epoch) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
            _unconfirmed = attemptId;
        }
        catch
        {
            _authority.RecordOutcome(attemptId, "NotDispatched");
            throw;
        }
    }
}
