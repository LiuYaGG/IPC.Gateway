using System.Net.Sockets;
using System.Text.Json;
using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed record HaDisasterRequest(string OperationId, string Reason, string ConfirmationNode,
    bool PeerIsolated, int DurationMinutes = 60);
internal sealed record HaDisasterExitRequest(string OperationId, string Reason);
internal sealed record HaDisasterPeer(string NodeId, bool GatewayReachable, bool MemberReachable);
internal sealed record HaDisasterEvent(string OperationId, string NodeId, string EventType, string Actor,
    string Reason, DateTimeOffset TimestampUtc, DateTimeOffset? ExpiresAtUtc = null);
internal sealed record HaDisasterStatus(bool Active, bool Eligible, string? BlockedReason, string LocalNode,
    HaDisasterTakeover? Grant, HaDisasterPeer[] Peers, HaDisasterEvent[] RecentEvents);

/// <summary>Manual, expiring, node-local authority. It never changes the Raft majority or its ownership log.</summary>
internal sealed class HaDisasterTakeoverService : BackgroundService
{
    private readonly GatewayHaClient _client;
    private readonly HaDraftStore _store;
    private readonly IConfiguration _configuration;
    private readonly HaTransportManagementService _transport;
    private readonly ILogger<HaDisasterTakeoverService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _journalGate = new();
    private readonly string _journalPath;
    private HaDisasterTakeover? _observedGrant;

    public HaDisasterTakeoverService(GatewayHaClient client, HaDraftStore store, IConfiguration configuration,
        IHostEnvironment host, HaTransportManagementService transport, ILogger<HaDisasterTakeoverService> logger)
    {
        _client = client;
        _store = store;
        _configuration = configuration;
        _transport = transport;
        _logger = logger;
        string root = Path.GetFullPath(configuration["Gateway:HighAvailability:ManagementDirectory"] ??
            configuration["Gateway:HighAvailability:StateDirectory"] ?? Path.Combine(host.ContentRootPath, "Data", "HighAvailability"), AppContext.BaseDirectory);
        _journalPath = Path.Combine(root, "disaster-takeovers.json");
    }

    public async Task<HaDisasterStatus> StatusAsync(CancellationToken token)
    {
        var peers = await ProbePeersAsync(token);
        var grant = _client.DisasterTakeover;
        string? blocked = BlockedReason(peers);
        return new(_client.DisasterTakeoverActive, blocked == null, blocked, _client.NodeId,
            grant, peers, ReadEvents().TakeLast(20).Reverse().ToArray());
    }

    private string? BlockedReason(HaDisasterPeer[] peers)
    {
        if (!_client.DisasterTakeoverConfigured)
            return "HA_NOT_PROVISIONED";
        if (_transport.HasPending || _store.Read().TransportPlanId != null) return "HA_DISASTER_TRANSPORT_PENDING";
        if (peers.Length != 2) return "HA_DISASTER_CONFIGURATION_INCOMPLETE";
        if (peers.Any(p => p.MemberReachable || p.GatewayReachable)) return "HA_DISASTER_PEER_REACHABLE";
        if (!_client.DisasterTakeoverActive && _client.CanRunBusiness) return "HA_DISASTER_ALREADY_ACTIVE";
        return null;
    }

    public async Task<HaDisasterStatus> ActivateAsync(HaDisasterRequest request, string actor, CancellationToken token)
    {
        HaManagementService.RequireId(request.OperationId);
        if (!request.PeerIsolated || request.ConfirmationNode != _client.NodeId)
            throw new HaManagementError(400, "HA_DISASTER_CONFIRMATION_REQUIRED", "请确认其他业务网关已停机或隔离，并输入本机节点编号。");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 512 || request.DurationMinutes is < 15 or > 1440)
            throw new HaManagementError(400, "HA_DISASTER_INVALID_REQUEST", "请填写接管原因，并选择 15 分钟至 24 小时的接管期限。");
        await _gate.WaitAsync(token);
        bool storeLocked = false;
        try
        {
            await _store.Gate.WaitAsync(token);
            storeLocked = true;
            if (_client.DisasterTakeover is { Active: true } previous && previous.OperationId == request.OperationId)
                return await StatusAsync(token);
            var peers = await ProbePeersAsync(token);
            if (BlockedReason(peers) is { } reason)
                throw new HaManagementError(409, reason, "当前条件不允许人工灾难接管，请检查其他节点和本机状态。");
            bool renewal = _client.DisasterTakeoverActive;
            // Persist the operator's explicit intent before any authority becomes usable.
            Append(new(request.OperationId, _client.NodeId, renewal ? "DisasterRenewalRequested" : "DisasterTakeoverRequested",
                actor, request.Reason.Trim(), DateTimeOffset.UtcNow));
            HaDisasterTakeover grant;
            try { grant = _client.StartDisasterTakeover(request.OperationId, actor, request.Reason, request.DurationMinutes); }
            catch (HaRequestException ex)
            {
                Append(new(request.OperationId, _client.NodeId, "DisasterRejected", actor, ex.Code, DateTimeOffset.UtcNow));
                throw;
            }
            try
            {
                Append(new(request.OperationId, _client.NodeId, renewal ? "DisasterRenewal" : "DisasterTakeover",
                    actor, grant.Reason, DateTimeOffset.UtcNow, grant.ExpiresAtUtc));
                _observedGrant = grant;
            }
            catch
            {
                _client.StopDisasterTakeover();
                throw;
            }
            _logger.LogWarning("Manual disaster takeover {OperationId} on {Node}; expires at {ExpiresAt}.",
                grant.OperationId, grant.NodeId, grant.ExpiresAtUtc);
            return await StatusAsync(token);
        }
        finally
        {
            if (storeLocked) _store.Gate.Release();
            _gate.Release();
        }
    }

    public async Task<HaDisasterStatus> ExitAsync(HaDisasterExitRequest request, string actor, CancellationToken token)
    {
        HaManagementService.RequireId(request.OperationId);
        await _gate.WaitAsync(token);
        try
        {
            var previous = _client.StopDisasterTakeover() ?? _observedGrant;
            _observedGrant = null;
            if (previous != null)
                Append(new(request.OperationId, _client.NodeId, "DisasterExit", actor,
                    string.IsNullOrWhiteSpace(request.Reason) ? "HA_DISASTER_OPERATOR_EXIT" : request.Reason[..Math.Min(request.Reason.Length, 512)], DateTimeOffset.UtcNow));
            return await StatusAsync(token);
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_client.DisasterTakeover == null && _observedGrant == null) continue;
            await _gate.WaitAsync(stoppingToken);
            try
            {
                var grant = _client.DisasterTakeover;
                if (grant == null)
                {
                    if (_observedGrant is { } restored)
                    {
                        _observedGrant = null;
                        Append(new(restored.OperationId, _client.NodeId, "DisasterPeerRecovered", "System", "HA_DISASTER_QUORUM_RESTORED", DateTimeOffset.UtcNow));
                    }
                    continue;
                }
                var peers = grant.Active ? await ProbePeersAsync(stoppingToken) : [];
                string? reason = !grant.Active ? "HA_DISASTER_EXPIRED" :
                    peers.Length != 2 || _transport.HasPending || _store.Read().TransportPlanId != null ? "HA_DISASTER_MONITOR_FAILED" :
                    peers.Any(p => p.MemberReachable || p.GatewayReachable) ? "HA_DISASTER_PEER_RECOVERED" : null;
                if (reason == null) continue;
                var previous = _client.StopDisasterTakeover();
                _observedGrant = null;
                if (previous != null)
                {
                    Append(new(previous.OperationId, _client.NodeId,
                        reason == "HA_DISASTER_EXPIRED" ? "DisasterExpired" : reason == "HA_DISASTER_PEER_RECOVERED" ? "DisasterPeerRecovered" : "DisasterMonitorFailed", "System", reason, DateTimeOffset.UtcNow));
                    _logger.LogWarning("Manual disaster authority revoked on {Node}: {Reason}.", _client.NodeId, reason);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                var previous = _client.StopDisasterTakeover();
                _observedGrant = null;
                _logger.LogError(ex, "Disaster monitoring failed; manual authority was revoked.");
                if (previous != null)
                    try { Append(new(previous.OperationId, _client.NodeId, "DisasterMonitorFailed", "System", "HA_DISASTER_MONITOR_FAILED", DateTimeOffset.UtcNow)); }
                    catch (Exception journalError) { _logger.LogError(journalError, "Unable to record disaster revocation."); }
            }
            finally { _gate.Release(); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _client.StopDisasterTakeover();
        await base.StopAsync(cancellationToken);
    }

    private async Task<HaDisasterPeer[]> ProbePeersAsync(CancellationToken token)
    {
        if (_client.Endpoints.Count != 3 || _client.NodeId is not ("A" or "B")) return [];
        var active = _store.Read().LastActive;
        var members = _configuration.GetSection("Gateway:HighAvailability:Members").GetChildren()
            .Select(m => new HaMember(m["NodeId"] ?? m.Key, m["ClientUrl"] ?? "", m["PeerUrl"] ?? "", m["GatewayUrl"], m["CertificateRef"] ?? "")).ToArray();
        // Runtime enrollment, rather than a stale pre-migration policy cache, defines which peers must be isolated.
        if (members.Length != 3 || !members.Select(m => m.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(["A", "B", "C"]) ||
            members.Any(m => !Uri.TryCreate(m.ClientUrl, UriKind.Absolute, out var uri) || !_client.Endpoints.Contains(uri)) ||
            members.Select(m => new Uri(m.ClientUrl)).Distinct().Count() != 3)
            return [];
        bool sameTransport = active?.Members is { Length: 3 } stored && members.All(m => stored.Any(s =>
            s.NodeId == m.NodeId && Uri.TryCreate(s.ClientUrl, UriKind.Absolute, out var a) && a == new Uri(m.ClientUrl) &&
            Uri.TryCreate(s.PeerUrl, UriKind.Absolute, out var b) && Uri.TryCreate(m.PeerUrl, UriKind.Absolute, out var c) && b == c));
        if (sameTransport)
            members = members.Select(m => m with { GatewayUrl = active!.Members.Single(s => s.NodeId == m.NodeId).GatewayUrl }).ToArray();
        var peers = members.Where(m => m.NodeId != _client.NodeId).ToArray();
        if (!peers.Select(m => m.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "A", "B", "C" }.Where(n => n != _client.NodeId)))
            return [];
        return await Task.WhenAll(peers.Select(async member =>
        {
            if (!Uri.TryCreate(member.ClientUrl, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" ||
                !Uri.TryCreate(member.PeerUrl, UriKind.Absolute, out var peerEndpoint) || peerEndpoint.Scheme != "https" ||
                member.NodeId != "C" && (!Uri.TryCreate(member.GatewayUrl, UriKind.Absolute, out var web) || web.Scheme is not ("http" or "https")))
                return new HaDisasterPeer(member.NodeId, true, true); // Invalid or incomplete addresses are never evidence of isolation.
            var clientProbe = TcpReachableAsync(endpoint, token);
            var peerProbe = TcpReachableAsync(peerEndpoint, token);
            var gatewayProbe = member.NodeId == "C" ? Task.FromResult(false) : TcpReachableAsync(new Uri(member.GatewayUrl!), token);
            await Task.WhenAll(clientProbe, peerProbe, gatewayProbe);
            return new HaDisasterPeer(member.NodeId, gatewayProbe.Result, clientProbe.Result || peerProbe.Result);
        }));
    }

    private static async Task<bool> TcpReachableAsync(Uri uri, CancellationToken token)
    {
        using var socket = new TcpClient();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(600));
        try { await socket.ConnectAsync(uri.IdnHost, uri.Port, deadline.Token); return true; }
        catch (SocketException) { return false; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return false; }
    }

    private HaDisasterEvent[] ReadEvents()
    {
        lock (_journalGate)
        {
            if (!File.Exists(_journalPath)) return [];
            if (new FileInfo(_journalPath).Length > 4 * 1024 * 1024) throw new InvalidDataException("HA_DISASTER_JOURNAL_INVALID");
            return JsonSerializer.Deserialize<HaDisasterEvent[]>(File.ReadAllBytes(_journalPath), HaDraftStore.Json)
                ?? throw new InvalidDataException("HA_DISASTER_JOURNAL_INVALID");
        }
    }

    private void Append(HaDisasterEvent entry)
    {
        lock (_journalGate)
        {
            var next = ReadEvents().TakeLast(999).Append(entry).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_journalPath)!);
            string temporary = _journalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(file, next, HaDraftStore.Json);
                    file.Flush(true);
                }
                File.Move(temporary, _journalPath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
