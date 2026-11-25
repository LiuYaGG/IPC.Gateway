using System.Security.Claims;
using System.Text.Json;
using IPC.Gateway.Arbiter;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.HighAvailability;

namespace IPC.Gateway.WebHost.HighAvailability;

internal sealed record TransportPrepareRequest(long DraftVersion,string OperationId,string ExpectedGeneration,bool RotateCertificates=true);
internal sealed record TransportApplyRequest(string PlanId);
internal sealed record TransportOperation(string Phase,TransportPlan Proposal,long DraftVersion,string DraftHash,string PolicyVersion,
    TransportPrepared[] Prepared,TransportProof[] ReadyProofs,string[] Committed,string? Message=null);

internal sealed class HaTransportManagementService(GatewayHaClient client,HaDraftStore store,IConfiguration config,IHostEnvironment host)
{
    private readonly string _path=Path.Combine(Path.GetFullPath(config["Gateway:HighAvailability:ManagementDirectory"] ??
        config["Gateway:HighAvailability:StateDirectory"] ?? Path.Combine(host.ContentRootPath,"Data","HighAvailability"),AppContext.BaseDirectory),"transport-operation.json");
    private static readonly JsonSerializerOptions Json=TransportMigrationFiles.Json;
    private TransportOperation? Read()=>File.Exists(_path)?JsonSerializer.Deserialize<TransportOperation>(File.ReadAllBytes(_path),Json):null;
    private void Save(TransportOperation operation)=>TransportMigrationFiles.Write(_path,operation);
    public bool HasPending=>Read() is {Phase:"Preparing" or "Prepared" or "Partial" or "PendingRestart"};
    private static string Text(JsonElement value,string key)=>value.TryGetProperty(key,out var v)?v.ToString():"";
    private static HaManagementError Error(string code)=>new(409,code,code);
    private async Task<JsonElement> RequireDisabled(string? generation,CancellationToken token)
    {
        if(client.DisasterTakeoverActive)throw Error("HA_TRANSPORT_DISASTER_ACTIVE");
        if(client.Endpoints.Count!=3)throw Error("HA_NOT_PROVISIONED");
        var status=await client.QueryAsync("/v1/status",token);
        if(!status.TryGetProperty("quorumAvailable",out var q)||q.ValueKind!=JsonValueKind.True)throw Error("HA_NO_QUORUM");
        if(Text(status,"mode")!="Disabled" || Text(status,"standaloneNode")!=client.NodeId ||
            generation!=null && Text(status,"modeGeneration")!=generation)throw Error("HA_TRANSPORT_DISABLE_REQUIRED");
        return status;
    }
    private string? RegisteredNode(Uri address)=>config.GetSection("Gateway:HighAvailability:Members").GetChildren()
        .FirstOrDefault(m=>Uri.TryCreate(m["ClientUrl"],UriKind.Absolute,out var uri)&&uri==address)?["NodeId"];
    private Uri OldAddress(string node)=>client.Endpoints.Single(uri=>RegisteredNode(uri)==node);
    private static HaSiteConfiguration FinalConfig(TransportOperation op)=>new(op.Proposal.ClusterId,op.Proposal.Name,op.Proposal.BusinessUrl,
        op.Proposal.PreferredNode,op.Proposal.RenewSeconds,op.Proposal.LeaseSeconds,
        op.Prepared.OrderBy(p=>p.Member.NodeId).Select(p=>new HaMember(p.Member.NodeId,p.Member.ClientUrl,p.Member.PeerUrl,p.Member.GatewayUrl,p.Member.CertificateRef!)).ToArray());
    private static bool SameTransport(HaSiteConfiguration left,HaSiteConfiguration right)=>left.ClusterId==right.ClusterId &&
        TransportMigrationFiles.Hash(left.Members.OrderBy(m=>m.NodeId).ToArray())==TransportMigrationFiles.Hash(right.Members.OrderBy(m=>m.NodeId).ToArray());
    private object View(TransportOperation? op)=>op==null?new {phase="None",restartRequired=false,members=Array.Empty<object>()}:(object)new {
        phase=op.Phase,planId=op.Proposal.PlanId,operationId=op.Proposal.PlanId,rotateCertificates=op.Proposal.RotateCertificates,
        restartRequired=op.Phase=="PendingRestart",message=op.Message,
        members=op.Proposal.Members.OrderBy(m=>m.NodeId).Select(m=>{
            var member=op.Prepared.FirstOrDefault(p=>p.Member.NodeId==m.NodeId)?.Member ?? m;
            return new {member.NodeId,member.ClientUrl,member.PeerUrl,member.GatewayUrl,member.CertificateRef,member.GatewayCertificateSha256,
                state=op.Phase=="Completed"?"Completed":op.Committed.Contains(m.NodeId)?"PendingRestart":op.ReadyProofs.Any(p=>p.NodeId==m.NodeId)?"Prepared":"Pending"};
        }),config=op.Prepared.Length==3?FinalConfig(op):null
    };
    public async Task<object> StatusAsync(CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try {
            var op=Read();
            if(op?.Phase=="PendingRestart" && client.Endpoints.OrderBy(x=>x.AbsoluteUri)
                .SequenceEqual(op.Proposal.Members.Select(m=>new Uri(m.ClientUrl)).OrderBy(x=>x.AbsoluteUri))) {
                bool all=true;
                foreach(var prepared in op.Prepared) {
                    try {
                        var member=await client.QueryMemberAsync(new Uri(prepared.Member.ClientUrl),"/v1/member",token);
                        if(Text(member,"nodeId")!=prepared.Member.NodeId || Text(member,"certificateSha256")!=prepared.Member.CertificateSha256 ||
                            Text(member,"transportPlanId")!=op.Proposal.PlanId || Text(member,"peerUrl")!=new Uri(prepared.Member.PeerUrl).AbsoluteUri ||
                            !member.TryGetProperty("transportMembers",out var actual) || actual.GetArrayLength()!=3 ||
                            op.Prepared.Any(p=>!actual.EnumerateArray().Any(x=>Text(x,"nodeId")==p.Member.NodeId &&
                                Text(x,"clientUrl")==new Uri(p.Member.ClientUrl).AbsoluteUri && Text(x,"peerUrl")==new Uri(p.Member.PeerUrl).AbsoluteUri &&
                                Text(x,"certificateSha256")==p.Member.CertificateSha256)))all=false;
                    } catch(HaRequestException){all=false;}
                }
                if(all) {
                    var confirmedStatus=await client.QueryAsync("/v1/status",token);
                    if(!confirmedStatus.TryGetProperty("quorumAvailable",out var available)||available.ValueKind!=JsonValueKind.True)throw Error("HA_NO_QUORUM");
                    if(Text(confirmedStatus,"transportPlanId")!=op.Proposal.PlanId)throw Error("HA_TRANSPORT_PLAN_CHANGED");
                    var confirmed=confirmedStatus.GetProperty("activeConfig").Deserialize<HaSiteConfiguration>(Json)
                        ?? throw Error("HA_TRANSPORT_COMMIT_NOT_REPLAYED");
                    if(!SameTransport(confirmed,FinalConfig(op)))
                        throw Error("HA_TRANSPORT_PLAN_CHANGED");
                    if(confirmedStatus.TryGetProperty("transportPending",out var pending)&&pending.ValueKind==JsonValueKind.True) {
                        await RequireDisabled(op.Proposal.Generation,token);
                        foreach(var member in op.Prepared)
                            await client.CommandMemberAsync(new Uri(member.Member.ClientUrl),"/v1/transport/complete",new TransportId(op.Proposal.PlanId),token);
                        await client.CommandAsync("/v1/transport-finish",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
                            requestId=op.Proposal.PlanId+"-complete",target=op.Proposal.PlanId,modeGeneration=op.Proposal.Generation},token);
                    }
                    op=op with {Phase="Completed",Message="组网配置已在三个成员生效。"};Save(op);
                    var state=store.Read();
                    store.Save(state with {TransportPlanId=null,LastActive=confirmed,LastRevision=long.Parse(Text(confirmedStatus,"configVersion")),
                        Draft=confirmed,DraftVersion=state.DraftVersion+(state.Draft==null || TransportMigrationFiles.Hash(state.Draft)!=TransportMigrationFiles.Hash(confirmed)?1:0),
                        PrecheckId=null,CheckedAt=null});
                }
            }
            if(op?.Phase=="Completed" && store.Read().TransportPlanId==op.Proposal.PlanId) {
                var latest=await client.QueryAsync("/v1/status",token);
                var confirmed=latest.GetProperty("activeConfig").Deserialize<HaSiteConfiguration>(Json) ?? throw Error("HA_TRANSPORT_COMMIT_NOT_REPLAYED");
                if(!SameTransport(confirmed,FinalConfig(op)))throw Error("HA_TRANSPORT_PLAN_CHANGED");
                var state=store.Read();store.Save(state with {TransportPlanId=null,LastActive=confirmed,LastRevision=long.Parse(Text(latest,"configVersion"))});
            }
            return View(op);
        } finally {store.Gate.Release();}
    }
    public async Task<object> PrepareAsync(TransportPrepareRequest request,CancellationToken token)
    {
        HaManagementService.RequireId(request.OperationId);
        await store.Gate.WaitAsync(token);
        try {
            var status=await RequireDisabled(request.ExpectedGeneration,token);
            var local=store.Read();var op=Read();
            if(local.LastChange?.Status=="Pending")throw Error("HA_OPERATION_PENDING");
            if(local.Draft==null || local.DraftVersion!=request.DraftVersion)throw Error("HA_DRAFT_CHANGED");
            local.Draft.Validate();
            if(local.Draft.ClusterId!=client.ClusterId)throw Error("HA_INVALID_CONFIGURATION");
            string hash=TransportMigrationFiles.Hash(local.Draft);
            if(op is {Phase:"Preparing" or "Prepared" or "Partial" or "PendingRestart"}) {
                if(op.Proposal.PlanId!=request.OperationId || op.DraftVersion!=request.DraftVersion || op.DraftHash!=hash ||
                    op.Proposal.RotateCertificates!=request.RotateCertificates)throw Error("HA_TRANSPORT_PENDING");
                if(local.TransportPlanId==null)store.Save(local with {TransportPlanId=request.OperationId,PrecheckId=null,CheckedAt=null});
                else if(local.TransportPlanId!=request.OperationId)throw Error("HA_TRANSPORT_PENDING");
                if(op.Phase!="Preparing")return View(op);
            } else {
                if(local.TransportPlanId!=null)throw Error("HA_TRANSPORT_PENDING");
                var draft=local.Draft;
                var plan=new TransportPlan(request.OperationId,draft.ClusterId,draft.Name,draft.BusinessUrl,draft.PreferredNode,
                    draft.RenewSeconds,draft.LeaseSeconds,request.ExpectedGeneration,client.NodeId,
                    draft.Members.Select(m=>new TransportMember(m.NodeId,m.ClientUrl,m.PeerUrl,m.GatewayUrl,m.CertificateRef)).ToArray(),request.RotateCertificates);
                op=new("Preparing",plan,request.DraftVersion,hash,Text(status,"configVersion"),[],[],[]);
                Save(op);store.Save(local with {TransportPlanId=request.OperationId,PrecheckId=null,CheckedAt=null});
            }
            try {
                // All old identities must be reachable before any node prepares its replacement.
                foreach(string id in new[]{"A","B","C"}) {
                    var member=await client.QueryMemberAsync(OldAddress(id),"/v1/member",token);
                    if(Text(member,"nodeId")!=id || Text(member,"clusterId")!=client.ClusterId)throw Error("HA_WRONG_IDENTITY");
                    if(!member.TryGetProperty("transportSupported",out var supported)||supported.ValueKind!=JsonValueKind.True)throw Error("HA_TRANSPORT_UPGRADE_REQUIRED");
                }
                await client.CommandAsync("/v1/transport-lock",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
                    requestId=op.Proposal.PlanId+"-lock",target=op.Proposal.PlanId,modeGeneration=op.Proposal.Generation},token);
                foreach(string id in new[]{"A","B","C"}) {
                    if(op.Prepared.Any(p=>p.Member.NodeId==id))continue;
                    var data=await client.CommandMemberAsync(OldAddress(id),"/v1/transport/prepare",op.Proposal,token);
                    var prepared=data.Deserialize<TransportPrepared>(Json) ?? throw Error("HA_TRANSPORT_INVALID_PROOF");
                    if(prepared.Member.NodeId!=id)throw Error("HA_WRONG_IDENTITY");
                    op=op with {Prepared=[..op.Prepared,prepared]};Save(op);
                }
                foreach(string id in new[]{"A","B","C"}) {
                    if(op.ReadyProofs.Any(p=>p.NodeId==id))continue;
                    var data=await client.CommandMemberAsync(OldAddress(id),"/v1/transport/stage",new TransportStage(op.Proposal,op.Prepared),token);
                    var proof=data.Deserialize<TransportProof>(Json) ?? throw Error("HA_TRANSPORT_INVALID_PROOF");
                    if(proof.NodeId!=id)throw Error("HA_WRONG_IDENTITY");
                    op=op with {ReadyProofs=[..op.ReadyProofs,proof]};Save(op);
                }
                op=op with {Phase="Prepared",Message="三个成员已准备同一迁移计划，确认后写入待生效配置。"};Save(op);
                return View(op);
            } catch(Exception ex) {
                Save(op with {Message=ex.Message});throw;
            }
        } finally {store.Gate.Release();}
    }
    public async Task<object> ApplyAsync(TransportApplyRequest request,string? actor,CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try {
            var op=Read() ?? throw Error("HA_TRANSPORT_PLAN_REQUIRED");
            if(request.PlanId!=op.Proposal.PlanId || op.Phase is not ("Prepared" or "Partial" or "PendingRestart"))throw Error("HA_TRANSPORT_PLAN_CHANGED");
            if(op.Phase=="PendingRestart")return View(op);
            var status=await RequireDisabled(op.Proposal.Generation,token);
            var local=store.Read();
            if(local.TransportPlanId!=request.PlanId || op.Phase=="Prepared" && (local.DraftVersion!=op.DraftVersion ||
                local.Draft==null || TransportMigrationFiles.Hash(local.Draft)!=op.DraftHash))throw Error("HA_DRAFT_CHANGED");
            op=op with {Phase="Partial",Message="正在提交同一组网迁移，失败时请重试原计划。"};Save(op);
            var oldMembers=new List<MemberOptions>();
            foreach(string id in new[]{"A","B","C"}) {
                var member=await client.QueryMemberAsync(OldAddress(id),"/v1/member",token);
                // The complete registered manifest includes gateway pins and is signed by every member at stage.
                if(!member.TryGetProperty("transportMembers",out var manifest))throw Error("HA_TRANSPORT_UPGRADE_REQUIRED");
                oldMembers=manifest.Deserialize<List<MemberOptions>>(Json)!;break;
            }
            var actual=op.Prepared.Select(p=>p.Member).OrderBy(m=>m.NodeId).ToArray();
            var transition=new TransportTransition(op.Proposal,actual,oldMembers.ToArray(),op.ReadyProofs);
            await client.CommandAsync("/v1/transport",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
                requestId=op.Proposal.PlanId,expectedConfigVersion=op.PolicyVersion,migration=transition,actor,reason="修改关闭集群后的节点通信配置"},token);
            foreach(string id in new[]{"B","C","A"}) {
                if(op.Committed.Contains(id))continue;
                await client.CommandMemberAsync(OldAddress(id),"/v1/transport/commit",new TransportCommit(op.Proposal.PlanId,op.ReadyProofs),token);
                op=op with {Committed=[..op.Committed,id]};Save(op);
            }
            op=op with {Phase="PendingRestart",Message="三份配置已登记，尚未生效。请依次重启 B、C、A，再检查成员状态。"};Save(op);
            local=store.Read();store.Save(local with {Draft=FinalConfig(op),DraftVersion=local.DraftVersion+1,PrecheckId=null,CheckedAt=null});
            return View(op);
        } finally {store.Gate.Release();}
    }
    public async Task<object> CancelAsync(TransportApplyRequest request,CancellationToken token)
    {
        await store.Gate.WaitAsync(token);
        try {
            var op=Read();if(op==null)return View(null);
            if(request.PlanId!=op.Proposal.PlanId || op.Phase is not ("Preparing" or "Prepared"))throw Error("HA_TRANSPORT_PENDING");
            var cluster=await client.QueryAsync("/v1/status",token);
            if(!cluster.TryGetProperty("quorumAvailable",out var quorum)||quorum.ValueKind!=JsonValueKind.True)throw Error("HA_NO_QUORUM");
            if(cluster.TryGetProperty("transportPending",out var pending)&&pending.ValueKind==JsonValueKind.True) {
                await RequireDisabled(op.Proposal.Generation,token);
                foreach(string id in new[]{"A","B","C"})
                    await client.CommandMemberAsync(OldAddress(id),"/v1/transport/cancel",new TransportId(request.PlanId),token);
                await client.CommandAsync("/v1/transport-cancel",new {clusterId=client.ClusterId,nodeId=client.NodeId,instanceId=client.InstanceId,
                    requestId=op.Proposal.PlanId+"-cancel",target=op.Proposal.PlanId,modeGeneration=op.Proposal.Generation},token);
            } else if(Text(cluster,"transportPlanId")==request.PlanId)throw Error("HA_TRANSPORT_PENDING");
            var local=store.Read();if(local.TransportPlanId==request.PlanId)store.Save(local with {TransportPlanId=null});
            Save(op with {Phase="None",Message=null});return View(null);
        } finally {store.Gate.Release();}
    }
}

internal static class HaTransportManagementEndpoints
{
    public static void MapTransportManagement(this RouteGroupBuilder group)
    {
        static bool Manage(HttpContext context)=>HaManagementEndpoints.Allowed(context.User,GatewayPermissions.ManageHaConfig);
        static IResult Ok(object data)=>Results.Json(new{success=true,data});
        static IResult Forbidden()=>HaManagementEndpoints.Error(403,"FORBIDDEN","无权执行此操作。");
        group.MapGet("/transport",async(HttpContext context,HaTransportManagementService service)=>
            HaManagementEndpoints.Allowed(context.User,GatewayPermissions.ViewHa)?Ok(await service.StatusAsync(context.RequestAborted)):Forbidden());
        group.MapPost("/transport/prepare",async(TransportPrepareRequest request,HttpContext context,HaTransportManagementService service)=>
            Manage(context)?Ok(await service.PrepareAsync(request,context.RequestAborted)):Forbidden());
        group.MapPost("/transport/apply",async(TransportApplyRequest request,HttpContext context,HaTransportManagementService service)=>
            Manage(context)?Ok(await service.ApplyAsync(request,context.User.Identity?.Name,context.RequestAborted)):Forbidden());
        group.MapPost("/transport/cancel",async(TransportApplyRequest request,HttpContext context,HaTransportManagementService service)=>
            Manage(context)?Ok(await service.CancelAsync(request,context.RequestAborted)):Forbidden());
    }
}
