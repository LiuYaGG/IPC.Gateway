namespace IPC.Gateway.Arbiter;

internal sealed partial class OwnershipState
{
    private readonly MemberOptions[] _originalMembers=TransportMigrationFiles.OriginalMembers(options);
    internal (string Mode,string? StandaloneNode,string Generation) TransportGuard()
    {
        lock(sync)return(registry.Mode,registry.StandaloneNode,registry.ModeGeneration.ToString());
    }
    internal bool TransportApplied(string id) { lock(sync)return registry.TransportPlanId==id; }
    internal string? ActiveTransportPlanId { get { lock(sync)return registry.TransportPlanId; } }
    internal bool TransportPending { get {lock(sync)return registry.TransportInProgress;} }
    internal bool TransportLocked(string id){lock(sync)return registry.TransportInProgress && registry.TransportOperationId==id;}
    private string LockTransport(Request request,bool cancel)
    {
        if(registry.Mode!="Disabled" || registry.StandaloneNode!=request.NodeId || request.ModeGeneration!=registry.ModeGeneration.ToString() ||
            !Guid.TryParse(request.Target,out _))return "HA_TRANSPORT_MODE_CHANGED";
        if(cancel) {
            if(registry.TransportOperationId!=request.Target || registry.TransportPlanId==request.Target)return "HA_TRANSPORT_PENDING";
            registry.TransportInProgress=false;registry.TransportOperationId=null;return "";
        }
        if(registry.TransportInProgress && registry.TransportOperationId!=request.Target)return "HA_TRANSPORT_PENDING";
        registry.TransportInProgress=true;registry.TransportOperationId=request.Target;return "";
    }
    private string FinishTransport(Request request)
    {
        if(registry.Mode!="Disabled" || registry.StandaloneNode!=request.NodeId || request.ModeGeneration!=registry.ModeGeneration.ToString() ||
            request.Target!=registry.TransportPlanId)return "HA_TRANSPORT_MODE_CHANGED";
        registry.TransportInProgress=false;registry.TransportOperationId=null;return "";
    }
    private string ApplyTransport(Request request)
    {
            var migration=request.Migration;
            if(migration==null)return "HA_TRANSPORT_INVALID_PLAN";
            var plan=migration.Plan;
            if(registry.Mode!="Disabled" || registry.ModeGeneration.ToString()!=plan.Generation || registry.StandaloneNode!=plan.StandaloneNode)
                return "HA_TRANSPORT_MODE_CHANGED";
            if(request.NodeId!=registry.StandaloneNode || request.ExpectedConfigVersion!=registry.PolicyVersion.ToString())return "HA_CONFIG_CONFLICT";
            if(!registry.TransportInProgress || registry.TransportOperationId!=plan.PlanId)return "HA_TRANSPORT_PENDING";
            try {
                TransportMigration.Validate(plan);
                var permit=new TransportPermit("Staged",plan,migration.OldMembers,"","",new(migration.Members[0],migration.Proofs[0]),
                    migration.Members,TransportMigrationFiles.Fingerprint(plan.ClusterId,migration.Members));
                TransportMigrationFiles.VerifyProofs(permit,migration.Proofs,TransportMigrationFiles.ReadyText(permit));
            } catch(Exception ex) when(ex is InvalidDataException or ArgumentException or IndexOutOfRangeException) {return "HA_TRANSPORT_INVALID_PROOF";}
            registry.Policy=new Policy(plan.Name,plan.BusinessUrl,plan.PreferredNode,plan.RenewSeconds,plan.LeaseSeconds,
                plan.ClusterId,migration.Members.Select(m=>new PolicyMember(m.NodeId,m.ClientUrl,m.PeerUrl,m.GatewayUrl,m.CertificateRef)).ToArray());
            registry.PolicyVersion++;
            registry.TransportPlanId=plan.PlanId;
            registry.TransportInProgress=true;
            return "";
    }
}
