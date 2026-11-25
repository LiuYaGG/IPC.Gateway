using System.Net;
using System.Security.Cryptography;

namespace IPC.Gateway.WebHost;

/// <summary>
/// Node-local, single-use login challenges and verification tickets. Only the IP address
/// already processed by the trusted forwarded-header middleware may be supplied here.
/// Nothing is persisted or shared with another HA member.
/// </summary>
public sealed class GatewayLoginCaptchaService
{
    public const int ImageWidth = 320;
    public const int ImageHeight = 160;
    public const int PieceSize = 48;
    public const int OffsetTolerance = 4;
    public const string RateLimitError = "操作过于频繁，请稍后重新验证。";
    public const string CapacityError = "验证码服务繁忙，请稍后重新验证。";
    private const int MaximumChallenges = 2048;
    private const int MaximumTickets = 2048;
    private const int MaximumClients = 4096;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, ChallengeEntry> _challenges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TicketEntry> _tickets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClientLimits> _clients = new(StringComparer.Ordinal);
    private readonly ClientLimits _globalLimits = new();
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    public GatewayLoginCaptchaService(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public bool TryCreateChallenge(IPAddress? remoteIp, out GatewayLoginCaptchaChallenge? challenge, out string errorMessage)
    {
        challenge = null;
        string client = NormalizeIp(remoteIp);
        string id = RandomToken();
        int targetX = RandomNumberGenerator.GetInt32(64, ImageWidth - PieceSize - 8);
        int pieceY = RandomNumberGenerator.GetInt32(28, ImageHeight - PieceSize - 12);
        DateTimeOffset expiresAt;
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Cleanup(now);
            if (!AllowRequest(client, Operation.Challenge, now, out errorMessage)) return false;
            if (_challenges.Count >= MaximumChallenges)
            {
                errorMessage = CapacityError;
                return false;
            }
            expiresAt = now.Add(Lifetime);
            _challenges.Add(id, new ChallengeEntry(client, targetX, expiresAt));
        }

        // Image generation does not hold the lock used to atomically consume tickets.
        try
        {
            var images = GatewayLoginCaptchaImage.Create(targetX, pieceY);
            challenge = new GatewayLoginCaptchaChallenge(id, images.Background, images.Piece,
                ImageWidth, ImageHeight, PieceSize, PieceSize, pieceY, expiresAt);
            errorMessage = string.Empty;
            return true;
        }
        catch
        {
            lock (_sync) _challenges.Remove(id);
            throw;
        }
    }

    public bool TryVerify(IPAddress? remoteIp, string? challengeId, double offsetX,
        out GatewayLoginCaptchaVerification? verification, out string errorMessage)
    {
        verification = null;
        string client = NormalizeIp(remoteIp);
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Cleanup(now);
            // Consume even an incorrect/IP-mismatched attempt. The answer never survives a guess.
            ChallengeEntry? entry = null;
            if (IsToken(challengeId)) _challenges.Remove(challengeId!, out entry);
            if (!AllowRequest(client, Operation.Verify, now, out errorMessage)) return false;
            if (entry == null || entry.ExpiresAt <= now || entry.Client != client)
            {
                errorMessage = "验证码已失效或已使用，请重新验证。";
                return false;
            }
            if (!double.IsFinite(offsetX) || offsetX < 0 || offsetX > ImageWidth - PieceSize ||
                Math.Abs(offsetX - entry.TargetX) > OffsetTolerance)
            {
                errorMessage = "拼图位置不正确，请重新验证。";
                return false;
            }
            if (_tickets.Count >= MaximumTickets)
            {
                errorMessage = CapacityError;
                return false;
            }

            string token = RandomToken();
            DateTimeOffset expiresAt = now.Add(Lifetime);
            _tickets.Add(token, new TicketEntry(client, expiresAt));
            verification = new GatewayLoginCaptchaVerification(token, expiresAt);
            errorMessage = string.Empty;
            return true;
        }
    }

    /// <summary>Must succeed before invoking account/password authentication, including failed login attempts.</summary>
    public bool TryConsumeForLogin(IPAddress? remoteIp, string? captchaToken, out string errorMessage)
    {
        string client = NormalizeIp(remoteIp);
        lock (_sync)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            Cleanup(now);
            TicketEntry? ticket = null;
            if (IsToken(captchaToken)) _tickets.Remove(captchaToken!, out ticket);
            if (!AllowRequest(client, Operation.Login, now, out errorMessage)) return false;
            if (string.IsNullOrWhiteSpace(captchaToken))
            {
                errorMessage = "请先完成滑块验证。";
                return false;
            }
            if (ticket == null || ticket.ExpiresAt <= now || ticket.Client != client)
            {
                errorMessage = "验证凭据已过期、已使用或节点已切换，请重新验证。";
                return false;
            }
            errorMessage = string.Empty;
            return true;
        }
    }

    private bool AllowRequest(string client, Operation operation, DateTimeOffset now, out string errorMessage)
    {
        errorMessage = RateLimitError;
        if (!_clients.TryGetValue(client, out ClientLimits? limits))
        {
            if (_clients.Count >= MaximumClients) return false;
            limits = new ClientLimits();
            _clients.Add(client, limits);
        }
        limits.LastSeen = now;
        int limit = operation == Operation.Verify ? 24 : 12;
        // Both IP and node-wide windows are bounded; rejecting new IPs cannot evict rate state.
        bool clientAllowed = limits.Windows[(int)operation].TryTake(now, limit);
        if (!clientAllowed || !_globalLimits.Windows[(int)operation].TryTake(now, limit * 25)) return false;
        errorMessage = string.Empty;
        return true;
    }

    private void Cleanup(DateTimeOffset now)
    {
        if (now < _nextCleanup) return;
        foreach (string id in _challenges.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            _challenges.Remove(id);
        foreach (string token in _tickets.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            _tickets.Remove(token);
        foreach (string client in _clients.Where(pair => pair.Value.LastSeen.Add(Lifetime) <= now).Select(pair => pair.Key).ToArray())
            _clients.Remove(client);
        _nextCleanup = now.Add(CleanupInterval);
    }

    private static string NormalizeIp(IPAddress? address) =>
        address == null ? "unknown" : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();

    private static string RandomToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool IsToken(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    private enum Operation { Challenge, Verify, Login }
    private sealed record ChallengeEntry(string Client, int TargetX, DateTimeOffset ExpiresAt);
    private sealed record TicketEntry(string Client, DateTimeOffset ExpiresAt);

    private sealed class ClientLimits
    {
        public DateTimeOffset LastSeen { get; set; }
        public RateCounter[] Windows { get; } = [new(), new(), new()];
    }

    // Sliding windows prevent a client from doubling its allowance at a minute boundary.
    private sealed class RateCounter
    {
        private readonly Queue<DateTimeOffset> _attempts = new();
        public bool TryTake(DateTimeOffset now, int limit)
        {
            while (_attempts.TryPeek(out DateTimeOffset time) && time.Add(RateWindow) <= now)
                _attempts.Dequeue();
            if (_attempts.Count >= limit) return false;
            _attempts.Enqueue(now);
            return true;
        }
    }
}

public sealed record GatewayLoginCaptchaChallenge(string ChallengeId, string BackgroundImage, string PieceImage,
    int Width, int Height, int PieceWidth, int PieceHeight, int PieceY, DateTimeOffset ExpiresAt)
{
    public bool Success => true;
}

public sealed record GatewayLoginCaptchaVerification(string VerificationToken, DateTimeOffset ExpiresAt)
{
    public bool Success => true;
}

public sealed class GatewayLoginCaptchaVerifyRequest
{
    public string ChallengeId { get; set; } = string.Empty;
    public double OffsetX { get; set; }
}
