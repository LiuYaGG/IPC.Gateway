using IPC.Gateway.HighAvailability;
using System.Globalization;
using System.Text.Json;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed class HaManagementService(GatewayHaClient client, HaDraftStore store, IConfiguration config, HaHistoryReplica history)
{
    private static readonly HttpClient GatewayProbe = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false, PooledConnectionLifetime = TimeSpan.FromMinutes(1)
    }) { Timeout = TimeSpan.FromSeconds(3), MaxResponseContentBufferSize = 8192 };

    private ClusterSwitchSnapshot ClusterSwitch(JsonElement status)
    {
        bool quorum = Flag(status, "quorumAvailable");
        var mode = Field(status,"mode");
        bool supported = mode is "Enabled" or "Disabled" or "Disabling" or "Enabling";
        string phase = !quorum ? (client.Endpoints.Count == 0 && !client.IsCluster ? "Unconfigured" : "Unknown")
            : mode is null or "Enabled" && Number(status,"configVersion")==0 && !Flag(status,"enrollmentRequested") ? "Unconfigured"
            : supported ? mode! : "Unknown";
        return new(phase,Field(status,"modeGeneration") ?? "0",Field(status,"standaloneNode"),supported && quorum,
            Field(status,"modeOperation"),phase == "Unknown" ? (quorum ? "仲裁成员需要更新。" : "无法取得多数派") : null);
    }

    // Only a refused connection, an unreachable host/network or a timeout is evidence of
    // an offline peer. A live legacy endpoint, failed TLS or wrong identity blocks demotion.
    private async Task<bool> GatewayOnlineAsync(HaMember member,CancellationToken ct)
    {
        if (!Uri.TryCreate(member.GatewayUrl,UriKind.Absolute,out var url) || url.Scheme is not ("http" or "https"))
            throw new HaManagementError(409,"HA_INVALID_TARGET","缺少网关管理地址。");
        try
        {
            using var response = await GatewayProbe.GetAsync(new Uri(url,"/health/ha-node"),ct);
            if (!response.IsSuccessStatusCode) throw new HaManagementError(409,"HA_GATEWAY_IDENTITY","网关地址可达，但未能核实节点身份。");
            var node=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:ct);
            if (Field(node,"nodeId") != member.NodeId || Field(node,"clusterId") != client.ClusterId ||
                (member.NodeId==client.NodeId && Field(node,"instanceId")!=client.InstanceId))
                throw new HaManagementError(409,"HA_GATEWAY_IDENTITY","网关地址对应的节点身份不匹配。");
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
        catch (HttpRequestException ex) when (ex.InnerException is System.Net.Sockets.SocketException sock && sock.SocketErrorCode is
            System.Net.Sockets.SocketError.ConnectionRefused or System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable or System.Net.Sockets.SocketError.TimedOut) {return false;}
        catch (Exception ex) when(ex is HttpRequestException or JsonException)
        { throw new HaManagementError(409,"HA_GATEWAY_IDENTITY","无法核实网关身份，请检查登记的管理地址和证书。"); }
    }

    public async Task<ClusterSwitchSnapshot> ChangeModeAsync(ClusterSwitchRequest req,string? actor,CancellationToken ct)
    {
        RequireId(req.OperationId);
        if (req.Enabled && store.Read().TransportPlanId != null)
            throw new HaManagementError(409,"HA_TRANSPORT_PENDING","组网变更尚未完成，请等待三个成员使用同一计划重启后再启用热备。");
        if (req.ExpectedGeneration == null || !long.TryParse(req.ExpectedGeneration,out long generation) || generation<0)
            throw DraftConflict();
        var status=await client.QueryAsync("/v1/status",ct);
        var snapshot=ClusterSwitch(status);
        if (!snapshot.Supported) throw new HaManagementError(409,"HA_MODE_UNAVAILABLE","无法确认多数派或仲裁版本尚未更新。");
        try
        {
            // A retry after a lost response must not need another reachability check.
            var previous=await client.QueryAsync("/v1/mode/"+req.OperationId,ct);
            if (Field(previous,"status")=="Rejected") throw new HaManagementError(409,Field(previous,"reasonCode") ?? "HA_MODE_REJECTED","模式变更已被拒绝。");
            return snapshot;
        }
        catch (HaRequestException ex) when(ex.StatusCode==404) { }
        if (snapshot.Generation!=req.ExpectedGeneration) throw DraftConflict();
        if (!req.Enabled)
        {
            if (req.StandaloneNode!=client.NodeId || client.NodeId is not ("A" or "B"))
                throw new HaManagementError(409,"HA_INVALID_TARGET","请直连需要保留的单机网关。");
            var cfg=ActiveConfig(status) ?? store.Read().Draft ?? Template();
            var business=cfg.Members.Where(m=>m.NodeId is "A" or "B").ToArray();
            var online=await Task.WhenAll(business.Select(m=>GatewayOnlineAsync(m,ct)));
            if (business.Length!=2 || !business.Where((m,i)=>online[i]).Select(m=>m.NodeId).SequenceEqual([client.NodeId]))
                throw new HaManagementError(409,"HA_SINGLE_GATEWAY_REQUIRED","关闭热备要求只有当前业务网关可达，请先停止另一台网关。");
        }
        await client.CommandAsync("/v1/mode",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
            requestId=req.OperationId,modeGeneration=req.ExpectedGeneration,enabled=req.Enabled,target=req.Enabled ? "" : client.NodeId,actor},ct);
        return ClusterSwitch(await client.QueryAsync("/v1/status",ct));
    }
    private sealed record Observation(JsonElement Value, DateTimeOffset At);
    private Observation? _lastAuthoritative;
    private static string? Field(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString() : null;
    private static long Number(JsonElement e, string key) =>
        long.TryParse(Field(e, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;
    private static bool Flag(JsonElement e, string key) => bool.TryParse(Field(e, key), out bool value) && value;
    private static JsonElement[] List(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object &&
        e.TryGetProperty(key, out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().ToArray() : [];
    private HaSiteConfiguration? ActiveConfig(JsonElement e)
    {
        if (Number(e,"configVersion") < 1 || !e.TryGetProperty("activeConfig", out var v) ||
            v.ValueKind != JsonValueKind.Object) return null;
        var policy=v.Deserialize<HaSiteConfiguration>(HaDraftStore.Json);
        return policy == null ? null : policy with {ClusterId=client.ClusterId,
            Members=policy.Members ?? store.Read().LastActive?.Members ?? Template().Members};
    }
    private HaSiteConfiguration Template()
    {
        var entries = config.GetSection("Gateway:HighAvailability:Members").GetChildren().ToDictionary(
            x => x["NodeId"] ?? x.Key, StringComparer.OrdinalIgnoreCase);
        var members = new[] { "A", "B", "C" }.Select((id, index) =>
        {
            entries.TryGetValue(id, out var m);
            return new HaMember(id, m?["ClientUrl"] ?? client.Endpoints.ElementAtOrDefault(index)?.ToString() ?? "",
                m?["PeerUrl"] ?? "", m?["GatewayUrl"] ?? "", m?["CertificateRef"] ?? "");
        }).ToArray();
        var ha=config.GetSection("Gateway:HighAvailability");
        return new(client.ClusterId, ha["Name"] ?? "", ha["BusinessUrl"] ?? "", ha["PreferredNode"] ?? "A", 1, 5, members);
    }

    private async Task<JsonElement> LatestAsync(CancellationToken ct)
    {
        if (client.Endpoints.Count != 3) return default;
        try { return await client.QueryAsync("/v1/status", ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HaRequestException or HttpRequestException or OperationCanceledException)
        { return default; }
    }

    public object Capabilities() => new
    {
        clusterId = client.IsCluster || client.DisasterTakeoverActive ? client.ClusterId : null,
        mode = client.IsCluster ? "Cluster" : "Standalone", connectedNode = client.NodeId,
        localRole = client.DisasterTakeoverActive ? "EmergencyActive" : !client.IsCluster ? "Standalone" : client.CanRunBusiness ? "Active" : "Standby",
        disaster = client.DisasterTakeover,
        canWrite = client.BusinessReady, epoch = client.Epoch.ToString(CultureInfo.InvariantCulture),
        sourceInstanceId = client.InstanceId, reasonCodes = client.IsCluster && !client.BusinessReady ? new[] { "HA_NOT_ACTIVE" } : []
    };

    public async Task<object> StatusAsync(bool allowSwitch, CancellationToken ct)
    {
        JsonElement latest = await LatestAsync(ct);
        bool authoritative = Flag(latest,"quorumAvailable");
        var now = DateTimeOffset.UtcNow;
        if (authoritative) Volatile.Write(ref _lastAuthoritative, new Observation(latest,now));
        var last = Volatile.Read(ref _lastAuthoritative);
        var observed = latest.ValueKind == JsonValueKind.Object ? latest : last?.Value ?? default;
        var active = ActiveConfig(latest) ?? store.Read().LastActive ?? Template();
        string? owner = Field(latest, "owner");
        var clusterMembers = latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("members", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().ToArray() : [];
        return new
        {
            mode = client.IsCluster ? "Cluster" : "Standalone", connectedNode = client.NodeId,
            localRole = client.DisasterTakeoverActive ? "EmergencyActive" : !client.IsCluster ? "Standalone" : client.CanRunBusiness ? "Active" : authoritative ? "Standby" : "Demoted",
            disaster = client.DisasterTakeover,
            authority = client.DisasterTakeoverActive ? "LocalDisaster" : authoritative ? "Quorum" : "LocalLastKnown",
            owner = client.DisasterTakeoverActive ? client.NodeId : owner,
            businessUrl = authoritative || client.DisasterTakeoverActive ? active.BusinessUrl : null,
            epoch = authoritative ? Field(latest, "epoch") : null,
            lastKnownOwner = Field(observed, "lastKnownOwner"), lastKnownEpoch = Field(observed, "epoch") ?? client.Epoch.ToString(CultureInfo.InvariantCulture),
            reachableMembers = authoritative ? (int?)Number(latest, "reachableMembers") : null,
            memberCount = 3, observedAtUtc = latest.ValueKind == JsonValueKind.Object ? now : last?.At, historyReplica = history.Status,
            clusterSwitch = ClusterSwitch(latest),
            members = active.Members.Select(member =>
            {
                var observation = clusterMembers.FirstOrDefault(n => Field(n, "nodeId") == member.NodeId);
                var business = List(latest,"readiness").FirstOrDefault(n => Field(n,"nodeId") == member.NodeId);
                bool ready = authoritative && IsReady(latest, member.NodeId);
                return new { member.NodeId, member.ClientUrl, member.PeerUrl, member.GatewayUrl,
                    role = member.NodeId == "C" ? "ArbiterOnly" : member.NodeId == owner ? "Active" : authoritative ? "Standby" : "Unknown",
                    reachable = observation.ValueKind == JsonValueKind.Object && observation.TryGetProperty("reachable",out var reachable) ? (bool?)(reachable.ValueKind==JsonValueKind.True) : null,
                    ready, maintenance = List(latest, "maintenance").Any(x => x.ToString() == member.NodeId),
                    configVersion = Field(business, "configVersion"), observedAtUtc = Field(business, "timestamp"),
                    businessEpoch = Field(business,"businessEpoch"), grantedAtUtc = Field(business,"grantedAtUtc"),
                    businessReadyAtUtc = Field(business,"businessReadyAtUtc"), firstSampleAtUtc = Field(business,"firstSampleAtUtc"),
                    reasonCodes = member.NodeId == client.NodeId && client.ReadinessError is { Length: > 0 } reason ? new[]{reason} : [],
                    arbiterRole = Field(observation,"arbiterRole") };
            }),
            canStartSwitchover = allowSwitch && authoritative && owner == client.NodeId && client.BusinessReady,
            reasonCodes = client.IsCluster && !authoritative ? new[] { "HA_NO_QUORUM" } : []
        };
    }

    public async Task<object> ConfigAsync(CancellationToken ct)
    {
        var state = store.Read();
        var latest = await LatestAsync(ct);
        var active = ActiveConfig(latest) ?? state.LastActive;
        long revision = latest.ValueKind == JsonValueKind.Object ? Number(latest, "configVersion") : state.LastRevision;
        var template = Template();
        var progress = client.Endpoints.Select(async address =>
        {
            var mapped = (active ?? state.Draft ?? template).Members.FirstOrDefault(m =>
                Uri.TryCreate(m.ClientUrl,UriKind.Absolute,out var u) && u==address);
            try
            {
                var member = await client.QueryMemberAsync(address,"/v1/member",ct);
                long applied = Number(member,"configVersion");
                return new HaMemberProgress(Field(member,"nodeId"),applied,Field(member,"commitIndex"),
                    !Flag(latest,"quorumAvailable") ? "Observed" : applied < revision ? "CatchingUp" : applied == 0 ? "Pending" : "Applied");
            }
            catch (HaRequestException) { return new HaMemberProgress(mapped?.NodeId,null,null,"Unavailable"); }
        });
        return new { active, state.Draft, template, nodeProgress = await Task.WhenAll(progress),
            clusterSwitch = ClusterSwitch(latest),
            state.DraftVersion, state.PrecheckId, state.LastChange,
            revision,
            stale = client.IsCluster && !Flag(latest,"quorumAvailable") };
    }

    public async Task<object> SaveDraftAsync(DraftRequest req, CancellationToken ct)
    {
        if (req.Config == null) throw new HaManagementError(400,"HA_INVALID_CONFIGURATION","缺少配置。");
        req.Config.Validate();
        if (!string.IsNullOrEmpty(client.ClusterId) && req.Config.ClusterId != client.ClusterId)
            throw new HaManagementError(409,"HA_CLUSTER_ID_IMMUTABLE","已登记集群的 ID 不可修改，请保留原集群身份。");
        await store.Gate.WaitAsync(ct);
        try
        {
            var state = store.Read();
            if (req.ExpectedDraftVersion != state.DraftVersion) throw DraftConflict();
            if (state.TransportPlanId != null)
                throw new HaManagementError(409,"HA_TRANSPORT_PENDING","组网变更计划尚未完成，请完成或取消原计划后再修改配置。");
            if (state.LastChange?.Status == "Pending")
                throw new HaManagementError(409,"HA_OPERATION_PENDING","配置正在应用，请等待当前操作完成。");
            state = state with { Draft = req.Config, DraftVersion = state.DraftVersion + 1, PrecheckId = null, CheckedAt = null };
            store.Save(state);
            return new { state.DraftVersion, state.Draft };
        }
        finally { store.Gate.Release(); }
    }

    private static HaManagementError DraftConflict() => new(409,"HA_DRAFT_CHANGED","配置草稿已变化，请重新读取。");
    private static Uri Authority(string url) => new(new Uri(url).GetLeftPart(UriPartial.Authority));
    private bool MatchesManifest(JsonElement actual, HaMember member)
    {
        if (Field(actual,"nodeId") != member.NodeId ||
            !Uri.TryCreate(Field(actual,"clientUrl"),UriKind.Absolute,out var clientUrl) || clientUrl != new Uri(member.ClientUrl) ||
            !Uri.TryCreate(Field(actual,"peerUrl"),UriKind.Absolute,out var peerUrl) || peerUrl != new Uri(member.PeerUrl))
            return false;
        string? pin = Field(actual, "certificateSha256");
        if (string.IsNullOrWhiteSpace(pin)) return false;
        if (string.Equals(pin, member.CertificateRef, StringComparison.OrdinalIgnoreCase)) return true;
        var registered = config.GetSection("Gateway:HighAvailability:Members").GetChildren()
            .FirstOrDefault(m => (m["NodeId"] ?? m.Key) == member.NodeId);
        return registered?["CertificateRef"] == member.CertificateRef &&
            string.Equals(registered["CertificateSha256"], pin, StringComparison.OrdinalIgnoreCase);
    }
    private async Task<HaCheck[]> InspectAsync(HaSiteConfiguration draft, CancellationToken ct)
    {
        if (draft.ClusterId != client.ClusterId || client.Endpoints.Count != 3)
            return [new(client.NodeId, false, "请先在安装配置中登记与草稿一致的集群及三个可信客户端地址。")];
        // A policy commit does not change Raft listeners, the durable membership
        // fingerprint or certificate trust. Never report such a draft as applicable.
        // Deploying a new manifest requires a separate, coordinated offline migration.
        var installed = Template();
        var deploymentChanges = draft.Members.Where(member =>
        {
            var registered = installed.Members.Single(m => m.NodeId == member.NodeId);
            var entry = config.GetSection("Gateway:HighAvailability:Members").GetChildren()
                .FirstOrDefault(m => (m["NodeId"] ?? m.Key) == member.NodeId);
            return !Uri.TryCreate(registered.ClientUrl,UriKind.Absolute,out var clientUrl) || clientUrl != new Uri(member.ClientUrl) ||
                !Uri.TryCreate(registered.PeerUrl,UriKind.Absolute,out var peerUrl) || peerUrl != new Uri(member.PeerUrl) ||
                !string.Equals(registered.CertificateRef,member.CertificateRef,StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(entry?["CertificateSha256"],member.CertificateRef,StringComparison.OrdinalIgnoreCase);
        }).Select(member => new HaCheck(member.NodeId,false,"HA_MEMBER_REPROVISION_REQUIRED")).ToArray();
        if (deploymentChanges.Length > 0) return deploymentChanges;
        var checks = draft.Members.Select(async member =>
        {
            var uri = Authority(member.ClientUrl);
            if (!client.Endpoints.Any(p => Uri.Compare(p, uri, UriComponents.SchemeAndServer,
                    UriFormat.SafeUnescaped,StringComparison.OrdinalIgnoreCase) == 0))
                return new HaCheck(member.NodeId, false, "客户端地址不在安装时登记的清单内。");
            try
            {
                var status = await client.QueryMemberAsync(uri, "/v1/preflight", ct);
                var manifest = List(status,"members");
                bool identity = Field(status,"clusterId") == draft.ClusterId && MatchesManifest(status, member) &&
                    manifest.Length == 3 && draft.Members.All(m => manifest.Any(entry => MatchesManifest(entry, m)));
                bool passed = identity && Flag(status,"ready");
                var details=List(status,"checks").Where(x=>!Flag(x,"passed"))
                    .Select(x=>$"{member.NodeId} → {Field(x,"nodeId")} ({Field(x,"reasonCode")})").ToList();
                if (!Flag(status,"storageWritable")) details.Add("HA_STORAGE_UNAVAILABLE");
                if (passed && member.NodeId is "A" or "B")
                {
                    try
                    {
                        if (!await GatewayOnlineAsync(member,ct))
                        {
                            passed=false;
                            details.Add("HA_GATEWAY_UNREACHABLE");
                        }
                    }
                    catch (HaManagementError ex)
                    {
                        passed=false;
                        details.Add(ex.Code);
                    }
                }
                return new HaCheck(member.NodeId, passed, passed ? "HA_PREFLIGHT_PASSED" :
                    identity ? "HA_MEMBER_CHECK_FAILED" : "HA_MANIFEST_MISMATCH",details.ToArray());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && ex is HaRequestException or HttpRequestException or OperationCanceledException)
            { return new HaCheck(member.NodeId, false, "无法通过已登记证书连接仲裁成员。"); }
        });
        return await Task.WhenAll(checks);
    }

    public async Task<object> CheckDraftAsync(PrecheckRequest req, CancellationToken ct)
    {
        var state = store.Read();
        if (state.TransportPlanId != null)
            throw new HaManagementError(409,"HA_TRANSPORT_PENDING","组网变更计划尚未完成，请完成或取消原计划后再进行配置预检。");
        if (state.DraftVersion != req.DraftVersion || state.Draft == null) throw DraftConflict();
        var checks = await InspectAsync(state.Draft, ct);
        bool passed = checks.All(c => c.Passed);
        await store.Gate.WaitAsync(ct);
        try
        {
            state = store.Read();
            if (state.TransportPlanId != null)
                throw new HaManagementError(409,"HA_TRANSPORT_PENDING","组网变更计划尚未完成，请完成或取消原计划后再进行配置预检。");
            if (state.DraftVersion != req.DraftVersion) throw DraftConflict();
            state = state with { PrecheckId = passed ? Guid.NewGuid().ToString("N") : null, CheckedAt = DateTimeOffset.UtcNow };
            store.Save(state);
            return new { state.PrecheckId, passed, checks, state.DraftVersion };
        }
        finally { store.Gate.Release(); }
    }

    public async Task<object> ApplyAsync(ApplyRequest req, string? actor, CancellationToken ct)
    {
        RequireId(req.OperationId);
        await store.Gate.WaitAsync(ct);
        try
        {
            var s = store.Read();
            if (s.TransportPlanId != null)
                throw new HaManagementError(409,"HA_TRANSPORT_PENDING","组网变更计划尚未完成，请完成或取消原计划后再应用配置。");
            if (s.LastChange is { } prev && prev.OperationId == req.OperationId)
            {
                if (prev.DraftVersion != req.DraftVersion || prev.ExpectedRevision != req.ExpectedRevision) throw DraftConflict();
                return prev;
            }
            if (s.LastChange?.Status == "Pending") throw new HaManagementError(409,"HA_OPERATION_PENDING","已有配置正在应用。");
            if (s.Draft == null || s.DraftVersion != req.DraftVersion) throw DraftConflict();
            if (string.IsNullOrWhiteSpace(s.PrecheckId) || s.PrecheckId != req.PrecheckId || s.CheckedAt < DateTimeOffset.UtcNow.AddMinutes(-10))
                throw new HaManagementError(409,"HA_PRECHECK_REQUIRED","请重新进行三节点预检。");
            if ((await InspectAsync(s.Draft, ct)).Any(x => !x.Passed))
                throw new HaManagementError(409,"HA_PRECHECK_FAILED","成员身份或连接发生变化。");
            var cluster = await client.QueryAsync("/v1/status",ct);
            if (Field(cluster,"mode") is "Disabled" or "Disabling" or "Enabling")
                throw new HaManagementError(409,"HA_CLUSTER_DISABLED","请先开启集群开关。");
            var pending = new HaChange(req.OperationId, "Pending","正在确认多数派提交结果。", s.DraftVersion, req.ExpectedRevision,actor,DateTimeOffset.UtcNow,
                Field(cluster,"modeGeneration") ?? "0");
            store.Save(s with { LastChange = pending });
            return pending;
        }
        catch (HaRequestException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            var state = store.Read();
            if (state.LastChange is { } p && p.OperationId == req.OperationId)
                store.Save(state with { LastChange = p with { Status="Rejected", Message=ex.Code } });
            throw;
        }
        finally { store.Gate.Release(); }
    }

    public async Task ReconcileAsync(CancellationToken ct)
    {
        if (client.Endpoints.Count != 3) return;
        var local = store.Read();
        var cluster = await LatestAsync(ct);
        if (!Flag(cluster,"quorumAvailable"))
        {
            if (local.LastChange is {Status:"Pending"} pending)
            {
                var detail=Field(cluster,"reachableMembers") is {} reachable
                    ? $"仲裁不足（{reachable}/3），无法确认授主。多数派恢复后自动继续。"
                    : "无法连接仲裁多数派，操作结果尚未确认。多数派恢复后自动继续。";
                if(detail!=pending.Message)
                {
                    await store.Gate.WaitAsync(ct);
                    try
                    {
                        var current=store.Read();
                        if(current.LastChange is {Status:"Pending"} same && same.OperationId==pending.OperationId)
                            store.Save(current with {LastChange=same with {Message=detail}});
                    }
                    finally {store.Gate.Release();}
                }
            }
            return;
        }
        await client.ReconcileBusinessModeAsync(cluster,ct);
        if (local.LastChange is not { Status: "Pending" } op || local.Draft == null) return;
        await store.Gate.WaitAsync(ct);
        try
        {
            local = store.Read();
            if (local.LastChange?.OperationId != op.OperationId || local.LastChange.Status != "Pending") return;
            JsonElement decision;
            try { decision = await client.QueryAsync("/v1/config/" + op.OperationId, ct); }
            catch (HaRequestException ex) when (ex.StatusCode == 404)
            {
                if (op.ModeGeneration != (Field(cluster,"modeGeneration") ?? "0") || Field(cluster,"mode") is "Disabled" or "Disabling" or "Enabling")
                {
                    store.Save(local with {LastChange=op with {Status="Cancelled",Message="集群开关已变更，原配置操作已结束。"}});
                    return;
                }
                if (!Flag(cluster,"enrollmentRequested"))
                {
                    try { await client.CommandAsync("/v1/enroll", new {clusterId=client.ClusterId,nodeId=client.NodeId,
                        instanceId=client.InstanceId,modeGeneration=op.ModeGeneration,requestId="enroll-" + op.OperationId},ct); }
                    catch (HaRequestException err) when(err.Code=="HA_ENROLLMENT_CANCELLED")
                    { store.Save(local with {LastChange=op with {Status="Cancelled",Message="对端已离线，原网关继续单机运行。"}}); }
                    return;
                }
                if (op.ExpectedRevision == 0)
                {
                    var parked=List(cluster,"parkedNodes").Select(n=>n.ToString()).ToHashSet();
                    string peer=client.NodeId=="A" ? "B" : "A";
                    if (Field(cluster,"mode")==null)
                        throw new HaManagementError(409,"HA_MODE_UNAVAILABLE","请先升级仲裁成员。");
                    if (!parked.Contains(peer) && !client.IsCluster && Field(cluster,"enrollmentNode")==client.NodeId &&
                        !await GatewayOnlineAsync(local.Draft!.Members.Single(m=>m.NodeId==peer),ct))
                    {
                        await client.CommandAsync("/v1/cancel-enroll",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
                            target="enroll-"+op.OperationId,requestId=op.OperationId+"-cancel"},ct);
                        store.Save(local with {LastChange=op with {Status="Cancelled",Message="对端已离线，原网关继续单机运行。"}});
                        return;
                    }
                    string? waiting=!parked.Contains(peer) ? "等待对端网关确认退出单机业务。"
                        : !parked.Contains(client.NodeId) ? "对端已退出单机，等待本机完成业务交接。"
                        : !IsReady(cluster,"A") && !IsReady(cluster,"B") ? "网关正在准备业务，请检查节点就绪原因。" : null;
                    if (waiting != null)
                    {
                        if (op.Message!=waiting) store.Save(local with {LastChange=op with {Message=waiting}});
                        return;
                    }
                }
                await client.CommandAsync("/v1/config", new {clusterId=client.ClusterId,nodeId=client.NodeId,
                    instanceId=client.InstanceId,epoch=client.Epoch.ToString(CultureInfo.InvariantCulture),requestId=op.OperationId,
                    modeGeneration=op.ModeGeneration,expectedConfigVersion=op.ExpectedRevision.ToString(CultureInfo.InvariantCulture),actor=op.Actor,config=local.Draft},ct);
                return;
            }
            bool applied=Field(decision,"status") == "Applied";
            store.Save(local with {LastChange=op with {Status=applied ? "Applied" : "Rejected",
                Message=applied ? "配置已由多数派提交。" : Field(decision,"reasonCode") ?? "配置被拒绝。"},
                LastActive=applied ? local.Draft : local.LastActive, LastRevision=applied ? Number(decision,"configVersion") : local.LastRevision});
        }
        finally { store.Gate.Release(); }
    }

    public async Task<object> ReadChangeAsync(string id, CancellationToken ct)
    {
        RequireId(id);
        if (store.Read().LastChange is { } local && local.OperationId == id) return local;
        if (client.Endpoints.Count == 3) return await client.QueryAsync("/v1/config/"+id,ct);
        throw new HaManagementError(404,"HA_OPERATION_NOT_FOUND","未找到此节点的操作。");
    }
    public static void RequireId(string? id)
    {
        if (id == null || !Guid.TryParse(id,out _)) throw new HaManagementError(400,"HA_INVALID_REQUEST_ID","需要有效的操作 ID。");
    }

    internal static bool IsReady(JsonElement s,string node)
    {
        if (s.ValueKind!=JsonValueKind.Object || !s.TryGetProperty("readiness",out var list) || list.ValueKind!=JsonValueKind.Array)
            return false;
        if (s.TryGetProperty("maintenance",out var maintenance) && maintenance.ValueKind==JsonValueKind.Array &&
            maintenance.EnumerateArray().Any(m=>m.ToString()==node)) return false;
        return list.EnumerateArray().Any(m=>Field(m,"nodeId")==node && Flag(m,"ready") &&
            DateTimeOffset.TryParse(Field(m,"timestamp"),out var last) && last.AddSeconds(Math.Max(30,Number(s,"leaseSeconds")*3))>=DateTimeOffset.UtcNow);
    }
}
