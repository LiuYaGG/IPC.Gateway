using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace IPC.Gateway.HaSetup;

/// <summary>Owns the local member identities. The caller authorizes all management operations.</summary>
public sealed class GatewaySetupCoordinator : IAsyncDisposable
{
    private sealed record Session(string Secret, DateTimeOffset Expires, string? PlanHash);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _pairGate = new(1, 1);
    private readonly SemaphoreSlim _commitGate = new(1, 1);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly SetupFiles _files;
    private readonly HashSet<string> _allowed;
    private readonly int _pairPort;
    private FileStream? _identityLock;
    private PairingListener? _listener;
    private CancellationTokenSource? _sessionExpiry;
    private bool _disposed;

    public GatewaySetupCoordinator(string configRoot, int pairPort = 7381, string[]? allowedRoles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRoot);
        if (pairPort is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(pairPort));
        _files = new(configRoot);
        _pairPort = pairPort;
        _allowed = (allowedRoles ?? SetupFiles.Roles).ToHashSet(StringComparer.Ordinal);
        if (_allowed.Count == 0 || !_allowed.IsSubsetOf(SetupFiles.Roles))
            throw new ArgumentException("本机角色无效。", nameof(allowedRoles));
    }

    public event Action? ConfigurationCommitted;

    private void RequireLocalRole(string nodeId)
    {
        if (!_allowed.Contains(nodeId)) throw new ArgumentException("请选择本机安装的角色。");
    }

    private void LockIdentityDirectory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_identityLock != null) return;
        if (!Directory.Exists(_files.Root)) SetupFiles.CreatePrivateDirectory(_files.Root);
        _identityLock = new FileStream(Path.Combine(_files.Root, ".setup.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public SetupStatus Status()
    {
        lock (_sync)
        {
            var members = _files.Identities().ToArray();
            foreach (var member in members) RequireLocalRole(member.NodeId);
            var plans = members.Select(m => _files.InstalledPlan(m.NodeId)).ToArray();
            var plan = plans.FirstOrDefault(p => p != null);
            bool complete = plan != null && plans.All(p => p?.ClusterId == plan.ClusterId);
            if (complete && members.Any(m => m.NodeId == "A"))
            {
                string marker = Path.Combine(_files.Root, "setup-complete");
                complete = File.Exists(marker) && File.ReadAllText(marker) == plan!.ClusterId;
            }
            return new(members, plan?.ClusterId, complete, _pairPort);
        }
    }

    public string? CompletedGatewayConfigPath()
    {
        lock (_sync)
        {
            var status = Status();
            var member = status.Roles.SingleOrDefault(m => m.NodeId is "A" or "B");
            if (!status.Configured || member == null) return null;
            var path = Path.Combine(_files.Folder(member.NodeId), "gateway-ha.json");
            if (!File.Exists(path)) throw new InvalidDataException("组网配置不完整，请恢复本机网关身份文件。");
            return path;
        }
    }

    public Member RegisterIdentity(RoleSettings input)
    {
        RequireLocalRole(input.NodeId);
        lock (_sync)
        {
            LockIdentityDirectory();
            var status = Status();
            if (status.ClusterId != null && !status.Roles.Any(m => m.NodeId == input.NodeId))
                throw new InvalidOperationException("本机已经绑定组网配置。");
            return _files.CreateIdentity(input);
        }
    }

    public async Task<PairingCode> IssueCodeAsync(RoleChoice input, CancellationToken ct)
    {
        RequireLocalRole(input.NodeId);
        if (input.NodeId == "A") throw new ArgumentException("请在远端 B 或 C 生成配对码。");
        await _pairGate.WaitAsync(ct);
        try
        {
            lock (_sync)
            {
                LockIdentityDirectory();
                var roles = Status().Roles;
                if (roles.Any(r => r.NodeId == "A") || !roles.Any(r => r.NodeId == input.NodeId))
                    throw new ArgumentException("请先在远端登记本机成员身份。");
            }
            _listener ??= await PairingListener.StartAsync(_pairPort, this, ct);
            var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var expiry = DateTimeOffset.UtcNow.AddMinutes(10);
            lock (_sync) _sessions[input.NodeId] = new(secret, expiry, null);
            _sessionExpiry?.Cancel();
            _sessionExpiry?.Dispose();
            _sessionExpiry = new CancellationTokenSource();
            _ = StopIdleListenerAsync(expiry, _sessionExpiry.Token);
            return new(_listener.CertificatePin + "." + secret, expiry);
        }
        finally { _pairGate.Release(); }
    }

    private async Task StopIdleListenerAsync(DateTimeOffset deadline, CancellationToken token)
    {
        try
        {
            var delay = deadline - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            await _pairGate.WaitAsync(token);
            try
            {
                if (!token.IsCancellationRequested && _listener != null)
                {
                    lock (_sync) _sessions.Clear();
                    await _listener.DisposeAsync();
                    _listener = null;
                }
            }
            finally { _pairGate.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private bool Authorized(HttpContext ctx, string? role) =>
        role is "B" or "C" && _allowed.Contains(role) && _sessions.TryGetValue(role, out var session) &&
        session.Expires > DateTimeOffset.UtcNow && Equal(ctx.Request.Headers.Authorization.ToString(), "Bearer " + session.Secret);

    internal IResult RemoteIdentity(HttpContext ctx, RoleChoice input)
    {
        lock (_sync) return Authorized(ctx, input.NodeId)
            ? Results.Ok(_files.Identities().Single(m => m.NodeId == input.NodeId)) : Results.StatusCode(403);
    }

    internal IResult ApplyRemote(HttpContext ctx, ApplyPlan input)
    {
        bool notify;
        lock (_sync)
        {
            if (!Authorized(ctx, input.NodeId)) return Results.StatusCode(403);
            ArgumentNullException.ThrowIfNull(input.Plan);
            var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input.Plan, SetupFiles.Json)));
            var session = _sessions[input.NodeId];
            if (session.PlanHash != null && session.PlanHash != hash) return Results.Conflict();
            notify = !Status().Configured;
            _files.Install(input.NodeId, input.Plan);
            _sessions[input.NodeId] = session with { PlanHash = hash };
        }
        if (notify) ConfigurationCommitted?.Invoke();
        return Results.Ok(new { applied = true });
    }

    public async Task<SetupCommitResult> CommitAsync(CreateCluster input, CancellationToken ct)
    {
        RequireLocalRole("A");
        await _commitGate.WaitAsync(ct);
        try
        {
            Member[] local;
            lock (_sync)
            {
                LockIdentityDirectory();
                local = Status().Roles;
            }
            if (!local.Any(m => m.NodeId == "A")) throw new ArgumentException("请先登记本机角色 A。");
            if (input.Peers == null || input.Peers.Any(p => p == null) || input.Peers.Length != 3 - local.Length ||
                !local.Select(m => m.NodeId).Concat(input.Peers.Select(p => p.NodeId)).ToHashSet().SetEquals(SetupFiles.Roles))
                throw new ArgumentException("请填写每个远端成员的配对信息。");
            var nodes = new List<Member>(local);
            foreach (var peer in input.Peers)
            {
                var node = await SendAsync<Member>(peer, "identity", new RoleChoice(peer.NodeId), ct);
                if (node.NodeId != peer.NodeId) throw new InvalidDataException("远端成员身份不匹配。");
                nodes.Add(node);
            }
            var plan = new Plan(input.ClusterId, input.Name, input.BusinessUrl, input.PreferredNode,
                nodes.OrderBy(n => n.NodeId, StringComparer.Ordinal).ToArray());
            SetupFiles.Validate(plan);
            lock (_sync)
            {
                if (!local.SequenceEqual(_files.Identities())) throw new InvalidOperationException("本机成员已更改，请重新提交组网配置。");
                foreach (var node in local) _files.Install(node.NodeId, plan);
            }
            foreach (var peer in input.Peers) await SendAsync<JsonElement>(peer, "apply", new ApplyPlan(peer.NodeId, plan), ct);
            var completed = Path.Combine(_files.Root, "setup-complete");
            bool notify;
            lock (_sync)
            {
                notify = !File.Exists(completed);
                if (notify) SetupFiles.WriteNew(completed, Encoding.UTF8.GetBytes(plan.ClusterId));
            }
            if (notify) ConfigurationCommitted?.Invoke();
            return new(plan.Members, _files.Root);
        }
        finally { _commitGate.Release(); }
    }

    internal static bool Equal(string? left, string right) => left?.Length == right.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static async Task<T> SendAsync<T>(Peer peer, string action, object data, CancellationToken ct)
    {
        if (peer.Code?.Trim().Split('.') is not [var pin,var secret] || pin.Length != 64 || secret.Length != 48 ||
            !pin.All(char.IsAsciiHexDigit) || !secret.All(char.IsAsciiHexDigit)) throw new ArgumentException("配对码格式无效。");
        var address = SetupFiles.Endpoint(peer.Address, true);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert != null &&
            cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() >= DateTime.UtcNow &&
            Equal(cert.GetCertHashString(HashAlgorithmName.SHA256), pin);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 65536 };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        using var response = await http.PostAsJsonAsync(new Uri(address, "pair/" + action), data, ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)) ?? throw new InvalidDataException("远端返回了空响应。");
    }

    public async ValueTask DisposeAsync()
    {
        await _commitGate.WaitAsync();
        try
        {
            await _pairGate.WaitAsync();
            try
            {
                lock (_sync) { _disposed = true; _sessions.Clear(); }
                _sessionExpiry?.Cancel();
                _sessionExpiry?.Dispose();
                _sessionExpiry = null;
                if (_listener != null) await _listener.DisposeAsync();
                _listener = null;
                lock (_sync) { _identityLock?.Dispose(); _identityLock = null; }
            }
            finally { _pairGate.Release(); }
        }
        finally { _commitGate.Release(); }
    }
}
