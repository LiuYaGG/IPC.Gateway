using IPC.Gateway.Arbiter;
using IPC.Gateway.HaSetup;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed class EmbeddedGatewayMember : IAsyncDisposable
{
    public string? Role { get; private init; }
    public bool Enabled => Setup != null;
    public string? MemberConfig { get; private init; }
    public GatewaySetupCoordinator? Setup { get; private init; }
    public bool Loaded => MemberConfig != null;

    public static EmbeddedGatewayMember Configure(ConfigurationManager configuration)
    {
        var section = configuration.GetSection("Gateway:EmbeddedArbiter");
        string? role = section["Role"];
        if (string.IsNullOrEmpty(role)) return new();
        if (role is not ("A" or "B") || section["ConfigRoot"] is not { Length: > 0 } root || !Path.IsPathFullyQualified(root))
            throw new InvalidDataException("内置仲裁需要角色 A 或 B 和本机配置目录的绝对路径。");

        var setup = new GatewaySetupCoordinator(root, section.GetValue("PairPort", 7381), [role]);
        string? overlay = setup.CompletedGatewayConfigPath();
        string roleDir = Path.Combine(root, role);
        string standaloneFlag = Path.Combine(root, "standalone-before-setup");
        bool commissioningStandalone = role == "A" && overlay == null && File.Exists(standaloneFlag)
            && File.ReadAllText(standaloneFlag).Trim() == "A" && !File.Exists(Path.Combine(root, "setup-complete"))
            && !File.Exists(Path.Combine(roleDir, "gateway-state", "cluster-required"));
        string? memberConfig = null;
        if (overlay != null)
        {
            TransportMigrationFiles.ApplyGatewayStartup(overlay);
            memberConfig = Path.Combine(roleDir, "member.json");
            if (!File.Exists(memberConfig)) throw new InvalidDataException("请恢复本机已有的仲裁成员配置。");
            configuration.AddJsonFile(Path.GetFullPath(overlay), optional: false, reloadOnChange: false);
            if (configuration["Gateway:HighAvailability:NodeId"] != role)
                throw new InvalidDataException("本机网关身份与安装角色不一致。");
            // Once this machine loads the completed topology it can never fall back
            // to the installer's one-time standalone permission by losing a file.
            File.Delete(standaloneFlag);
        }
        var state = Path.Combine(roleDir, "gateway-state");
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gateway:EmbeddedArbiter:AwaitingProvisioning"] = (overlay == null).ToString(),
            ["Gateway:EmbeddedArbiter:CommissioningStandalone"] = commissioningStandalone.ToString(),
            ["Gateway:HighAvailability:StateDirectory"] = state,
            ["Gateway:HighAvailability:ManagementDirectory"] = state
        });
        return new() { Role = role, Setup = setup, MemberConfig = memberConfig };
    }

    public ValueTask DisposeAsync() => Setup?.DisposeAsync() ?? ValueTask.CompletedTask;
}

internal sealed class EmbeddedMemberWorker(EmbeddedGatewayMember member, ILogger<EmbeddedMemberWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (member.MemberConfig == null) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ArbiterHost.RunAsync([], member.MemberConfig, member.Role, embedded: true, stoppingToken);
                if (!stoppingToken.IsCancellationRequested) logger.LogWarning("Embedded arbiter {Role} stopped unexpectedly.", member.Role);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Embedded arbiter {Role} failed. Retrying in five seconds.", member.Role);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
