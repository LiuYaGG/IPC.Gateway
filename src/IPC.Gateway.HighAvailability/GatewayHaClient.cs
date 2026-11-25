using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IPC.Plc.Communication.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IPC.Gateway.HighAvailability;

public sealed class HaRequestException(int statusCode, string code, string? detail = null)
    : Exception(detail ?? code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public static class HighAvailabilityRegistration
{
    public static IServiceCollection AddGatewayHighAvailability(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<GatewayHaClient>();
        services.AddSingleton<IPlcWriteAttemptAuthorizer>(sp => sp.GetRequiredService<GatewayHaClient>());
        services.AddSingleton<IPC.Gateway.Core.Infrastructure.Persistence.IGatewayDatabaseOwner>(sp => sp.GetRequiredService<GatewayHaClient>());
        return services;
    }
}

public sealed partial class GatewayHaClient : IPC.Gateway.Core.Infrastructure.Persistence.IGatewayDatabaseOwner, IDisposable
{
    private readonly HttpClient _http;
    private readonly Lazy<HaWriteJournal> _writes;
    private readonly object _inflightLock = new();
    private int _inflight;
    private TaskCompletionSource? _drained;

    private sealed class WriteInFlight(GatewayHaClient client) : IDisposable
    {
        public void Dispose()
        {
            lock (client._inflightLock)
                if (--client._inflight == 0)
                {
                    client._drained!.TrySetResult();
                    if (!client._businessPeriod) client._disasterDraining = false;
                }
        }
    }

    public IDisposable? TrackWrite(long admittedEpoch)
    {
        if (!IsCluster && Endpoints.Count == 0) return null;
        lock (_inflightLock)
        {
            if (!CanRunBusiness || admittedEpoch != Epoch) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
            if (_inflight++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return new WriteInFlight(this);
        }
    }

    public Task WaitForWritesToDrainAsync(CancellationToken token)
    {
        lock (_inflightLock)
            return _inflight == 0 ? Task.CompletedTask : _drained!.Task.WaitAsync(token);
    }
    public IReadOnlyList<HaWriteResult> GetWriteResults(int limit) => IsCluster ? _writes.Value.Read(limit) : [];
    public (long Total, IReadOnlyList<HaWriteResult> Items) ReadWriteResults(int page, int size, string? node, string? result,
        string? from,string? to,string? commandId=null) => _writes.Value.Search(page,size,node,result,from,to,commandId);
    public IReadOnlyDictionary<string,HaWriteResult> FindWriteResults(string[] ids) => _writes.Value.Find(ids);
    private readonly string _latchPath;
    private readonly HashSet<string> _approvedDriverHashes;
    private sealed record DriverApproval(bool Allowed);
    private readonly ConditionalWeakTable<Type, DriverApproval> _guardedDrivers = new();
    public string? ShippedDriverHash { get; } = Assembly.GetEntryAssembly()?.GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == "HaGuardedProtocolPluginSha256")?.Value;
    private readonly bool _configured;
    private readonly List<X509Certificate2> _certificates = [];
    private int _latched, _lastEndpoint;
    private long _deadline, _epoch, _permitSequence;
    public string? ReadinessError { get; set; }
    private volatile bool _draining;
    public string ClusterId { get; }
    public string NodeId { get; }
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public IReadOnlyList<Uri> Endpoints { get; }
    public bool AwaitingProvisioning { get; }
    public bool IsCluster => AwaitingProvisioning || !ManagedStandalone && (_configured || Volatile.Read(ref _latched) != 0 || IsParked);
    public long Epoch => ManagedStandalone ? 0 : Interlocked.Read(ref _epoch);
    public bool CanRunBusiness => !_disasterDraining && (DisasterTakeoverActive ||
        !IsParked && (!IsCluster || (!_draining && Stopwatch.GetTimestamp() < Interlocked.Read(ref _deadline))));
    private volatile bool _businessStarted;
    public bool BusinessReady => (!IsCluster && !ManagedStandalone) || (_businessStarted && CanRunBusiness);
    private sealed record PhaseTimes(string? BusinessEpoch, DateTimeOffset? GrantedAtUtc, DateTimeOffset? BusinessReadyAtUtc, DateTimeOffset? FirstSampleAtUtc);
    private PhaseTimes _phases = new(null, null, null, null);
    private readonly object _phaseLock = new();
    public void MarkBusinessStarted()
    {
        _businessStarted = true;
        if (!IsCluster) return;
        lock (_phaseLock) _phases = _phases with { BusinessReadyAtUtc = _phases.BusinessReadyAtUtc ?? DateTimeOffset.UtcNow };
    }
    public void MarkFirstSample(long epoch)
    {
        lock (_phaseLock)
            if (CanRunBusiness && Epoch == epoch && !_phases.FirstSampleAtUtc.HasValue)
                _phases = _phases with { FirstSampleAtUtc = DateTimeOffset.UtcNow };
    }
    public bool HandoverRequested => _draining;
    public TimeSpan RenewInterval { get; private set; } = TimeSpan.FromSeconds(1);
    public string LastError { get; private set; } = string.Empty;

    public GatewayHaClient(IConfiguration config)
    {
        var s = config.GetSection("Gateway:HighAvailability");
        if (config.GetValue<bool>("Gateway:EmbeddedArbiter:AwaitingProvisioning"))
        {
            NodeId = config["Gateway:EmbeddedArbiter:Role"]!;
            AwaitingProvisioning = NodeId != "A" || !config.GetValue<bool>("Gateway:EmbeddedArbiter:CommissioningStandalone");
            ClusterId = "";
            Endpoints = [];
            _approvedDriverHashes = [];
            _latchPath = Path.Combine(s["StateDirectory"]!, "cluster-required");
            _writes = new(() => new HaWriteJournal(Path.GetDirectoryName(_latchPath)!));
            _http = new HttpClient();
            return;
        }
        _approvedDriverHashes = (s.GetSection("VerifiedWriteDriverSha256").Get<string[]>() ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_approvedDriverHashes.Any(h => h.Length != 64 || !h.All(Uri.IsHexDigit)))
            throw new InvalidOperationException("A verified write driver requires a SHA-256 digest.");
        string mode = s["Mode"] ?? "Standalone";
        if (!(mode.Equals("Cluster", StringComparison.OrdinalIgnoreCase) || mode.Equals("Standalone", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Gateway:HighAvailability:Mode must be Standalone or Cluster.");
        _configured = mode.Equals("Cluster", StringComparison.OrdinalIgnoreCase);
        NodeId = s["NodeId"]?.Trim() ?? "";
        ClusterId = s["ClusterId"]?.Trim() ?? "";
        _latchPath = Path.Combine(Path.GetFullPath(s["StateDirectory"] ?? "Data/HighAvailability", AppContext.BaseDirectory), "cluster-required");
        _writes = new(() => new HaWriteJournal(Path.GetDirectoryName(_latchPath)!));
        try { _ = File.GetAttributes(_latchPath); _latched = 1; }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        Endpoints = (s.GetSection("Endpoints").Get<string[]>() ?? []).Select(raw =>
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("Each registered arbiter endpoint must be an HTTPS origin.");
            return uri;
        }).ToArray();
        if (IsCluster || Endpoints.Count != 0)
        {
            if (Endpoints.Distinct().Count() != 3 || Endpoints.Count != 3 || ClusterId.Length == 0 || NodeId.Length == 0)
                throw new InvalidOperationException("HA requires a cluster ID, business node ID and three distinct registered HTTPS endpoints.");
        }
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        if (s["CertificatePath"] is { Length: > 0 } path)
        {
            string? password = s["CertificatePasswordFile"] is { Length: > 0 } passwordFile
                ? File.ReadAllText(Path.GetFullPath(passwordFile, AppContext.BaseDirectory)).TrimEnd('\r', '\n')
                : s["CertificatePassword"];
            var cert = X509CertificateLoader.LoadPkcs12FromFile(Path.GetFullPath(path, AppContext.BaseDirectory), password);
            if (!cert.HasPrivateKey) throw new InvalidOperationException("HA client certificate must have a private key.");
            _certificates.Add(cert);
            handler.ClientCertificates.Add(cert);
        }
        else if (s["CertificateThumbprint"] is { Length: > 0 } thumbprint)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);
            var cert = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, true).OfType<X509Certificate2>().FirstOrDefault(x => x.HasPrivateKey)
                ?? throw new InvalidOperationException("HA machine certificate was not found.");
            _certificates.Add(cert);
            handler.ClientCertificates.Add(cert);
        }
        if (Endpoints.Count != 0 && handler.ClientCertificates.Count == 0)
            throw new InvalidOperationException("Provision the HA client certificate before contacting the arbiters.");
        if (s.GetValue<bool>("TrustRegisteredMemberPins"))
        {
            var pins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in s.GetSection("Members").GetChildren())
            {
                string? pin = member["CertificateSha256"];
                if (!Uri.TryCreate(member["ClientUrl"], UriKind.Absolute, out var url) || !Endpoints.Contains(url) ||
                    pin?.Length != 64 || !pin.All(char.IsAsciiHexDigit) || !pins.TryAdd(url.GetLeftPart(UriPartial.Authority), pin))
                    throw new InvalidOperationException("HA_INVALID_MEMBER_CERTIFICATE_PIN");
            }
            if (pins.Count != 3) throw new InvalidOperationException("HA_INVALID_MEMBER_CERTIFICATE_PIN");
            handler.ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
            {
                if (request.RequestUri is null || cert is null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0 ||
                    !pins.TryGetValue(request.RequestUri.GetLeftPart(UriPartial.Authority), out var pin) ||
                    !string.Equals(pin, cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase))
                    return false;
                var now = DateTime.UtcNow;
                return now >= cert.NotBefore.ToUniversalTime() && now <= cert.NotAfter.ToUniversalTime() &&
                    chain != null && chain.ChainStatus.All(s => (s.Status & ~(X509ChainStatusFlags.UntrustedRoot | X509ChainStatusFlags.PartialChain)) == 0);
            };
        }
        else if (s["CaCertificatePath"] is { Length: > 0 } caPath)
        {
            var root = X509CertificateLoader.LoadCertificateFromFile(Path.GetFullPath(caPath, AppContext.BaseDirectory));
            _certificates.Add(root);
            handler.ServerCertificateCustomValidationCallback = (_, cert, originalChain, errors) =>
            {
                if (cert == null || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0) return false;
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.ApplicationPolicy.Add(new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"));
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                if (originalChain != null)
                    foreach (var element in originalChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                return chain.Build(cert);
            };
        }
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        if (_configured || _latched != 0) QuiesceForEnrollment();
        if (Endpoints.Count == 3) OpenModeState();
    }

    /// <summary>Durable and one-way. An enrolled installation cannot silently resume singleton writes.</summary>
    public void QuiesceForEnrollment()
    {
        if (Endpoints.Count != 3) throw new HaRequestException(409, "HA_NOT_PROVISIONED");
        Directory.CreateDirectory(Path.GetDirectoryName(_latchPath)!);
        lock (_latchLock)
        {
            byte[] identity = JsonSerializer.SerializeToUtf8Bytes(new { ClusterId, NodeId,
                Endpoints = Endpoints.Select(x=>x.AbsoluteUri).Order(StringComparer.Ordinal).ToArray() });
            if (File.Exists(_latchPath))
            {
                byte[] actual=File.ReadAllBytes(_latchPath);
                using var document=JsonDocument.Parse(actual);
                if(document.RootElement.TryGetProperty("ControlVersion",out var control) && control.TryGetInt32(out int version) && version==3)
                    identity=JsonSerializer.SerializeToUtf8Bytes(new {ClusterId,NodeId,
                        Endpoints=Endpoints.Select(x=>x.AbsoluteUri).Order(StringComparer.Ordinal).ToArray(),ControlVersion=3});
                if (!actual.AsSpan().SequenceEqual(identity))
                    throw new InvalidDataException("HA_IDENTITY_CHANGED");
            }
            else
            {
                var temporary = _latchPath+"."+Guid.NewGuid().ToString("N")+".tmp";
                try
                {
                    using (var file = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    { file.Write(identity); file.Flush(true); }
                    File.Move(temporary,_latchPath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            Volatile.Write(ref _latched, 1);
            _ = _writes.Value;
        }
    }
    private readonly object _latchLock = new();

    public Task<JsonElement> QueryMemberAsync(Uri registeredEndpoint, string relativePath, CancellationToken ct)
    {
        if (!Endpoints.Contains(registeredEndpoint)) throw new HaRequestException(400, "HA_UNREGISTERED_MEMBER");
        return SendAsync(registeredEndpoint, relativePath, null, ct);
    }

    public Task<JsonElement> CommandMemberAsync(Uri registeredEndpoint,string relativePath,object body,CancellationToken ct)
    {
        if(!Endpoints.Contains(registeredEndpoint))throw new HaRequestException(400,"HA_UNREGISTERED_MEMBER");
        return SendAsync(registeredEndpoint,relativePath,body,ct);
    }

    public Task<JsonElement> QueryAsync(string relativePath, CancellationToken ct) => RequestAsync(relativePath, null, ct);
    public Task<JsonElement> CommandAsync(string relativePath, object body, CancellationToken ct) => RequestAsync(relativePath, body, ct);

    private async Task<JsonElement> RequestAsync(string path, object? body, CancellationToken ct)
    {
        int start = Volatile.Read(ref _lastEndpoint);
        JsonElement? stale = null;
        for (int i = 0; i < Endpoints.Count; ++i)
        {
            int index = (start + i) % Endpoints.Count;
            try
            {
                var data = await SendAsync(Endpoints[index], path, body, ct);
                if (body == null && path == "/v1/status" && data.TryGetProperty("quorumAvailable", out var quorum) && quorum.ValueKind == JsonValueKind.False)
                {
                    stale = data;
                    continue;
                }
                Volatile.Write(ref _lastEndpoint, index);
                return data;
            }
            catch (HaRequestException ex) when (ex.StatusCode == 503 && ex.Code is "HA_NOT_LEADER" or "HA_NO_QUORUM" or "HA_MEMBER_UNAVAILABLE") { }
        }
        return stale ?? throw new HaRequestException(503,"HA_NO_QUORUM");
    }

    private async Task<JsonElement> SendAsync(Uri origin, string path, object? body, CancellationToken ct)
    {
        if (!path.StartsWith("/v1/", StringComparison.Ordinal) || path.Contains('\u005c') || path.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Only arbiter API paths are accepted.", nameof(path));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(path.StartsWith("/v1/transport",StringComparison.Ordinal) ? TimeSpan.FromSeconds(20) :
            path == "/v1/preflight" ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(1250));
        try
        {
            using var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, new Uri(origin, path));
            if (body != null) request.Content = JsonContent.Create(body);
            using var response = await _http.SendAsync(request, timeout.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new HaRequestException(404, "HA_OPERATION_NOT_FOUND");
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
                throw new HaRequestException((int)response.StatusCode, "HA_WRONG_IDENTITY");
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
            var root = doc.RootElement;
            if (!response.IsSuccessStatusCode || !root.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
                throw new HaRequestException((int)response.StatusCode, Text(root,"errorCode") ?? "HA_INVALID_RESPONSE", Text(root,"errorMessage"));
            return root.GetProperty("data").Clone();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or OperationCanceledException or JsonException)
        { throw new HaRequestException(503, "HA_MEMBER_UNAVAILABLE"); }
    }

    internal static string? Text(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var value) ? value.ToString() : null;

    public async Task MaintainLeaseAsync(string configVersion, bool ready, CancellationToken ct, bool renewOnly = false)
    {
        var admissionMode = _localMode;
        if (_draining || !IsCluster || IsParked) return;
        try
        {
            var phases = Volatile.Read(ref _phases);
            var readyBody = new { phases.BusinessEpoch, phases.GrantedAtUtc, phases.BusinessReadyAtUtc, phases.FirstSampleAtUtc, clusterId = ClusterId, nodeId = NodeId, instanceId = InstanceId, ready, configVersion, requestId = Guid.NewGuid().ToString("N") };
            await CommandAsync("/v1/readiness", readyBody, ct);
            if (!ready) return;
            var started = Stopwatch.GetTimestamp();
            if (renewOnly && !CanRunBusiness) return;
            string path = renewOnly || CanRunBusiness ? "/v1/renew" : "/v1/acquire";
            var lease = await CommandAsync(path, new { readyBody.clusterId, readyBody.nodeId, readyBody.instanceId, ready, configVersion, epoch = Epoch.ToString(), requestId = Guid.NewGuid().ToString("N") }, ct);
            if (Text(lease,"owner") != NodeId || Text(lease,"instanceId") != InstanceId ||
                !long.TryParse(Text(lease,"epoch"), out var epoch) || epoch < 1 ||
                !int.TryParse(Text(lease,"leaseSeconds"), out var ttl) || ttl is < 2 or > 3600)
                throw new HaRequestException(503, "HA_INVALID_LEASE");
            var safeDeadline = started + (long)((ttl * .8) * Stopwatch.Frequency);
            if (safeDeadline <= Stopwatch.GetTimestamp()) throw new HaRequestException(503, "HA_EXPIRED_RESPONSE");
            lock (_inflightLock)
            {
                if (!ReferenceEquals(admissionMode, _localMode) || (renewOnly && (epoch != Epoch || !CanRunBusiness))) return;
                if (Interlocked.Exchange(ref _epoch, epoch) != epoch)
                    lock (_phaseLock) _phases = new(epoch.ToString(), DateTimeOffset.UtcNow, null, null);
                Interlocked.Exchange(ref _deadline, safeDeadline);
            }
            if (double.TryParse(Text(lease,"renewSeconds"), System.Globalization.CultureInfo.InvariantCulture, out var interval) && interval > 0)
                RenewInterval = TimeSpan.FromSeconds(Math.Min(interval, ttl / 3d));
            LastError = "";
        }
        catch (HaRequestException ex)
        {
            LastError = ex.Code;
            if (renewOnly && ex.Code == "HA_HANDOVER_REQUIRED") _draining = true;
            if (ex.StatusCode == 409 || _draining) Interlocked.Exchange(ref _deadline, 0);
        }
    }

    public void RevokeLocal()
    {
        _businessStarted = false;
        Interlocked.Exchange(ref _deadline, 0);
    }
    public async Task ReleaseAfterDrainAsync(CancellationToken ct)
    {
        RevokeLocal();
        if (Epoch <= 0) return;
        try
        {
            await CommandAsync("/v1/release", new { clusterId=ClusterId, nodeId=NodeId, instanceId=InstanceId, epoch=Epoch.ToString(), requestId=Guid.NewGuid().ToString("N") },ct);
        }
        catch (HaRequestException ex) { LastError = ex.Code; }
        finally { _draining = false; }
    }

    public async ValueTask AuthorizeAsync(long epoch, string commandId, string attemptId, PlcWriteAttempt attempt, CancellationToken cancellationToken)
    {
        if (!CanRunBusiness || Epoch != epoch) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
        try { _writes.Value.Reserve(commandId, attemptId, epoch.ToString(), NodeId, InstanceId, attempt); }
        catch (Exception ex) when (ex is not PlcWriteDeniedException) { throw new PlcWriteDeniedException("HA_WRITE_JOURNAL_UNAVAILABLE"); }
        if (DisasterTakeoverActive)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanRunBusiness || Epoch != epoch || !DisasterTakeoverActive)
                throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
            // A local manual decision is never reported as a quorum permit.
            try { _writes.Value.Update(attemptId, "Unknown"); }
            catch (Exception) { throw new PlcWriteDeniedException("HA_WRITE_JOURNAL_UNAVAILABLE"); }
            return;
        }
        try
        {
            long sequence = Interlocked.Increment(ref _permitSequence);
            if (sequence <= 0) throw new PlcWriteDeniedException("HA_EPOCH_CAPACITY");
            var permit = await CommandAsync("/v1/permit", new { clusterId=ClusterId, nodeId=NodeId, instanceId=InstanceId,
                attemptSequence=sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                epoch=epoch.ToString(), requestId=attemptId, commandId, attemptId, digest=attempt.Digest, targetDigest=attempt.TargetDigest, sourceType=attempt.SourceType, segmentIndex=attempt.SegmentIndex }, cancellationToken);
            if (Text(permit,"epoch") != epoch.ToString() || !long.TryParse(Text(permit,"commitIndex"), out long index) || index <= 0)
                throw new PlcWriteDeniedException("HA_INVALID_PERMIT");
            _writes.Value.Update(attemptId, "Unknown", index.ToString());
            if (!CanRunBusiness || Epoch != epoch) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
        }
        catch (HaRequestException ex)
        {
            RecordOutcome(attemptId, ex.Code);
            throw new PlcWriteDeniedException(ex.Code);
        }
        catch (OperationCanceledException) { throw new PlcWriteDeniedException("HA_AUTHORIZATION_CANCELLED"); }
        catch (Microsoft.Data.Sqlite.SqliteException) { throw new PlcWriteDeniedException("HA_WRITE_JOURNAL_UNAVAILABLE"); }
    }
    public bool IsWriteDriverApproved(IPlcClient driver) =>
        _guardedDrivers.GetValue(driver.GetType(), type => new DriverApproval(IsGuardedDriver(type))).Allowed;

    private bool IsGuardedDriver(Type type)
    {
        if (type.Assembly == typeof(PlcHaWrite).Assembly)
            return type == typeof(IPC.Plc.Communication.VirtualPlc.VirtualPlcClient) ||
                type == typeof(IPC.Plc.Communication.ModbusTcp.ModbusTcpClient) ||
                type == typeof(IPC.Plc.Communication.ModbusUdp.ModbusUdpClient);
        bool shippedType = type.Assembly.GetName().Name == "IPC.Gateway.LegacyProtocolPlugins" &&
            type.FullName is "IPC.Plc.Communication.Cip.CipClient" or "IPC.Plc.Communication.Cip.EtherNetIpClient"
                or "IPC.Plc.Communication.Pccc.PcccClient" or "IPC.Plc.Communication.MitsubishiMc.McClient"
                or "IPC.Plc.Communication.SiemensS7.S7Client" or "IPC.Plc.Communication.OmronFins.FinsClient"
                or "IPC.Plc.Communication.ModbusRtu.ModbusRtuClient" or "IPC.Plc.Communication.ModbusAscii.ModbusAsciiClient";
        if (string.IsNullOrEmpty(type.Assembly.Location) || ((!shippedType || ShippedDriverHash == null) && _approvedDriverHashes.Count == 0))
            return false;
        using var file = File.OpenRead(type.Assembly.Location);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file));
        return shippedType && string.Equals(hash, ShippedDriverHash, StringComparison.OrdinalIgnoreCase) || _approvedDriverHashes.Contains(hash);
    }

    public void RecordOutcome(string attemptId, string result)
    {
        try { _writes.Value.Update(attemptId, result); }
        catch (Exception) { throw new PlcWriteDeniedException("HA_WRITE_JOURNAL_UNAVAILABLE"); }
    }
    public void Dispose()
    {
        _modeFile?.Dispose();
        _http.Dispose();
        foreach (var cert in _certificates) cert.Dispose();
    }
}
