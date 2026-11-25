using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotNext.Net.Cluster.Consensus.Raft;

namespace IPC.Gateway.Arbiter;

public sealed record TransportMember(string NodeId, string ClientUrl, string PeerUrl, string? GatewayUrl,
    string? CertificateRef = null, string? CertificateSha256 = null, string? GatewayCertificateSha256 = null);
public sealed record TransportPlan(string PlanId, string ClusterId, string Name, string BusinessUrl, string PreferredNode,
    int RenewSeconds, int LeaseSeconds, string Generation, string StandaloneNode, TransportMember[] Members, bool RotateCertificates);
public sealed record TransportProof(string NodeId, string Certificate, string Signature);
public sealed record TransportPrepared(TransportMember Member, TransportProof Proof);
public sealed record TransportStage(TransportPlan Plan, TransportPrepared[] Prepared);
public sealed record TransportCommit(string PlanId, TransportProof[] Proofs);
public sealed record TransportTransition(TransportPlan Plan, TransportMember[] Members, MemberOptions[] OldMembers, TransportProof[] Proofs);
public sealed record TransportId(string PlanId);
public sealed record TransportPermit(string Phase, TransportPlan Proposal, MemberOptions[] OldMembers,
    string OldFingerprint, string OldIdentity, TransportPrepared Local, TransportMember[]? Members = null,
    string? FinalFingerprint = null, TransportProof[]? ReadyProofs = null);

/// <summary>Explicit local migration evidence. It never creates or discards a Raft log.</summary>
public static class TransportMigrationFiles
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public const string MarkerName = "transport-migration.json";
    public static JsonNode LoadJson(string path)=>JsonNode.Parse(File.ReadAllBytes(path),new JsonNodeOptions{PropertyNameCaseInsensitive=true})
        ?? throw new InvalidDataException("HA_TRANSPORT_INVALID_PLAN");
    public static string Fingerprint(string cluster, IEnumerable<TransportMember> members) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
            ClusterId = cluster, Members = members.OrderBy(x => x.NodeId, StringComparer.Ordinal).Select(x => new {
                x.NodeId, ClientUrl = new Uri(x.ClientUrl), PeerUrl = new Uri(x.PeerUrl), business = x.NodeId != "C"
            }) })));
    public static string Hash<T>(T item) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item, Json)));
    public static string PreparedText(TransportPlan p, TransportMember m) => Hash(new { p.PlanId, proposal = Hash(p), m });
    public static string ReadyText(TransportPermit p) => Hash(new { p.Proposal.PlanId, proposal = Hash(p.Proposal), p.Members, p.FinalFingerprint });
    public static void Write(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var f = new FileStream(tmp, options)) { f.Write(bytes); f.Flush(true); }
            File.Move(tmp, path, true);
        } finally { File.Delete(tmp); }
    }
    public static void Write<T>(string path, T item) => Write(path, JsonSerializer.SerializeToUtf8Bytes(item, Json));
    public static TransportPermit? Read(string stateDirectory)
    {
        string path = Path.Combine(stateDirectory, MarkerName);
        return File.Exists(path) ? JsonSerializer.Deserialize<TransportPermit>(File.ReadAllBytes(path), Json)
            ?? throw new InvalidDataException("HA_TRANSPORT_INVALID_PERMIT") : null;
    }
    public static int IdentityControlVersion(string stateDirectory)
    {
        string identity=Path.Combine(stateDirectory,"identity.json");
        if(File.Exists(identity)) {
            using var file=JsonDocument.Parse(File.ReadAllBytes(identity));
            if(file.RootElement.TryGetProperty("controlVersion",out var value) && value.GetInt32()==3)return 3;
        }
        return Read(stateDirectory) is {Phase:"Committed" or "Completed"}?3:MembershipPreflight.WireVersion;
    }
    public static bool HasPending(string memberConfigFile)
    {
        string state=LoadJson(memberConfigFile)["Arbiter"]!["StateDirectory"]!.GetValue<string>();
        return Read(state) is { Phase: "Prepared" or "Staged" or "Committed" };
    }
    public static void VerifyProofs(TransportPermit permit, TransportProof[] proofs, string content)
    {
        if (proofs is not {Length:3} || proofs.Any(p=>p==null) || !proofs.Select(x => x.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(["A", "B", "C"]))
            throw new InvalidDataException("HA_TRANSPORT_PROOFS_REQUIRED");
        foreach (var proof in proofs) {
            using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(proof.Certificate));
            var old = permit.OldMembers.Single(x => x.NodeId == proof.NodeId);
            using var rsa = certificate.GetRSAPublicKey();
            if (!old.Matches(ArbiterOptions.Fingerprint(certificate), true) || rsa == null ||
                !rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(content), Convert.FromBase64String(proof.Signature),
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new InvalidDataException("HA_TRANSPORT_INVALID_PROOF");
        }
    }
    public static TransportPermit VerifyCommitted(ArbiterOptions options)
    {
        var p = Read(options.StateDirectory) ?? throw new InvalidDataException("HA_TRANSPORT_PERMIT_REQUIRED");
        if (p.Phase is not ("Committed" or "Completed") || p.Proposal.ClusterId != options.ClusterId || p.Local.Member.NodeId != options.NodeId ||
            p.Members == null || p.FinalFingerprint != options.MembershipFingerprint ||
            Fingerprint(p.Proposal.ClusterId, p.Members) != p.FinalFingerprint || p.ReadyProofs == null ||
            !options.Members.OrderBy(x => x.NodeId).Select(x => new { x.NodeId, url=x.ClientUrl.AbsoluteUri, peer=x.PeerUrl.AbsoluteUri, x.CertificateSha256, x.GatewayCertificateSha256 })
              .SequenceEqual(p.Members.OrderBy(x=>x.NodeId).Select(x=>new {x.NodeId,url=new Uri(x.ClientUrl).AbsoluteUri,peer=new Uri(x.PeerUrl).AbsoluteUri,CertificateSha256=x.CertificateSha256!,x.GatewayCertificateSha256})))
            throw new InvalidDataException("HA_TRANSPORT_INVALID_PERMIT");
        VerifyProofs(p, p.ReadyProofs, ReadyText(p));
        return p;
    }
    public static bool MigrateIdentity(ArbiterOptions options, string existing, string next)
    {
        var p = VerifyCommitted(options);
        if (existing != p.OldIdentity)
            throw new InvalidDataException("HA_TRANSPORT_IDENTITY_MISMATCH");
        Write(Path.Combine(options.StateDirectory, "identity.before-transport-" + p.Proposal.PlanId + ".json"), System.Text.Encoding.UTF8.GetBytes(existing));
        Write(Path.Combine(options.StateDirectory, "identity.json"), System.Text.Encoding.UTF8.GetBytes(next));
        return true;
    }
    public static void ApplyGatewayStartup(string overlay)
    {
        CompletePendingFiles(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(overlay))!, "member.json"));
        var json = LoadJson(overlay).AsObject();
        var ha = json["Gateway"]!["HighAvailability"]!;
        string role = ha["NodeId"]!.GetValue<string>();
        string memberConfig = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(overlay))!, "member.json");
        if (!File.Exists(memberConfig)) return;
        var options = JsonSerializer.Deserialize<ArbiterEnvelope>(File.ReadAllBytes(memberConfig), Json)!.Arbiter;
        var permit = Read(options.StateDirectory);
        if (permit is not { Phase: "Committed" or "Completed" }) return;
        permit = VerifyCommitted(options);
        string latch = Path.Combine(ha["StateDirectory"]!.GetValue<string>(), "cluster-required");
        if (!File.Exists(latch)) throw new InvalidDataException("HA_TRANSPORT_MISSING_GATEWAY_IDENTITY");
        byte[] old = JsonSerializer.SerializeToUtf8Bytes(new { ClusterId = options.ClusterId, NodeId = role,
            Endpoints = permit.OldMembers.Select(x=>x.ClientUrl.AbsoluteUri).Order(StringComparer.Ordinal).ToArray() });
        byte[] legacyNext = JsonSerializer.SerializeToUtf8Bytes(new { ClusterId = options.ClusterId, NodeId = role,
            Endpoints = options.Members.Select(x=>x.ClientUrl.AbsoluteUri).Order(StringComparer.Ordinal).ToArray() });
        byte[] next = JsonSerializer.SerializeToUtf8Bytes(new { ClusterId = options.ClusterId, NodeId = role,
            Endpoints = options.Members.Select(x=>x.ClientUrl.AbsoluteUri).Order(StringComparer.Ordinal).ToArray(),ControlVersion=3 });
        byte[] actual = File.ReadAllBytes(latch);
        if (actual.AsSpan().SequenceEqual(next)) return;
        byte[] v3Old=JsonSerializer.SerializeToUtf8Bytes(new { ClusterId=options.ClusterId,NodeId=role,
            Endpoints=permit.OldMembers.Select(x=>x.ClientUrl.AbsoluteUri).Order(StringComparer.Ordinal).ToArray(),ControlVersion=3 });
        if (!actual.AsSpan().SequenceEqual(old) && !actual.AsSpan().SequenceEqual(legacyNext) && !actual.AsSpan().SequenceEqual(v3Old))
            throw new InvalidDataException("HA_TRANSPORT_GATEWAY_IDENTITY_MISMATCH");
        Write(latch + ".before-transport-" + permit.Proposal.PlanId, actual);
        Write(latch, next);
    }
    public static void CompletePendingFiles(string memberConfig,bool honorEnvironment=false)
    {
        if(!File.Exists(memberConfig))return;
        var options=JsonSerializer.Deserialize<ArbiterEnvelope>(File.ReadAllBytes(memberConfig),Json)!.Arbiter;
        string directory=Path.GetDirectoryName(Path.GetFullPath(memberConfig))!;
        if(honorEnvironment && Environment.GetEnvironmentVariable("Arbiter__StateDirectory") is {Length:>0} stateOverride)
            options.StateDirectory=stateOverride;
        if(!Path.IsPathFullyQualified(options.StateDirectory))throw new InvalidDataException("HA_TRANSPORT_STATE_DIRECTORY");
        options.CertificatePath=Path.GetFullPath(options.CertificatePath,directory);
        if(options.CertificatePasswordFile is {Length:>0})options.CertificatePasswordFile=Path.GetFullPath(options.CertificatePasswordFile,directory);
        var permit=Read(options.StateDirectory);
        if(permit is not {Phase:"Committed"})return;
        if(permit.Proposal.ClusterId!=options.ClusterId || permit.Local.Member.NodeId!=options.NodeId || permit.ReadyProofs==null || permit.Members==null)
            throw new InvalidDataException("HA_TRANSPORT_INVALID_PERMIT");
        VerifyProofs(permit,permit.ReadyProofs,ReadyText(permit));
        // The commit record and three ready signatures authorize completing an interrupted multi-file update.
        string? pass=options.CertificatePasswordFile is {Length:>0} file?File.ReadAllText(file).TrimEnd('\r','\n'):options.CertificatePassword;
        using var cert=X509CertificateLoader.LoadPkcs12FromFile(options.CertificatePath,pass);
        new TransportMigration(options,cert,memberConfig).WriteConfigurations(permit);
    }
    public static MemberOptions[] OriginalMembers(ArbiterOptions options)
    {
        var current=options.Members;
        string fingerprint=options.MembershipFingerprint;
        string folder=Path.Combine(options.StateDirectory,"transport-plans");
        if(!Directory.Exists(folder))return current;
        var history=Directory.EnumerateFiles(folder,"permit.json",SearchOption.AllDirectories)
            .Select(path=>JsonSerializer.Deserialize<TransportPermit>(File.ReadAllBytes(path),Json)!)
            .Where(p=>p.Phase is "Committed" or "Completed").ToArray();
        var visited=new HashSet<string>();
        while(visited.Add(fingerprint)) {
            var migration=history.LastOrDefault(p=>p.FinalFingerprint==fingerprint && p.OldFingerprint!=fingerprint);
            if(migration==null)break;
            if(migration.ReadyProofs==null)throw new InvalidDataException("HA_TRANSPORT_INVALID_PERMIT");
            VerifyProofs(migration,migration.ReadyProofs,ReadyText(migration));
            current=migration.OldMembers;fingerprint=migration.OldFingerprint;
        }
        return current;
    }
    private sealed record ArbiterEnvelope(ArbiterOptions Arbiter);
}

internal sealed class TransportMigration(ArbiterOptions options, X509Certificate2 signingCertificate, string configFile)
{
    private readonly object _gate = new();
    private string Marker => Path.Combine(options.StateDirectory, TransportMigrationFiles.MarkerName);
    private string Root => Path.GetDirectoryName(Path.GetFullPath(configFile))!;
    private static readonly JsonSerializerOptions Json = TransportMigrationFiles.Json;
    internal static void Validate(TransportPlan p)
    {
        ArgumentNullException.ThrowIfNull(p);
        bool Origin(string s, bool tls) => Uri.TryCreate(s,UriKind.Absolute,out var u) && (u.Scheme=="https" || !tls && u.Scheme=="http") &&
            u.UserInfo=="" && u.Query=="" && u.Fragment=="" && (!tls || u.AbsolutePath=="/");
        if (!Guid.TryParse(p.PlanId,out _) || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>128 ||
            !Origin(p.BusinessUrl,false) || p.PreferredNode is not ("A" or "B") || p.RenewSeconds is <1 or >30 ||
            p.LeaseSeconds<3*p.RenewSeconds || p.LeaseSeconds>120 || p.Members is not {Length:3} || p.Members.Any(m=>m==null) ||
            !p.Members.Select(x=>x.NodeId).ToHashSet(StringComparer.Ordinal).SetEquals(["A","B","C"]) ||
            p.Members.Any(x=>!Origin(x.ClientUrl,true)||!Origin(x.PeerUrl,true)||new Uri(x.ClientUrl).Port==new Uri(x.PeerUrl).Port||
                (x.NodeId=="C" ? !string.IsNullOrEmpty(x.GatewayUrl) : !Origin(x.GatewayUrl ?? "",false))) ||
            p.Members.SelectMany(x=>new[]{new Uri(x.ClientUrl),new Uri(x.PeerUrl)}).Distinct().Count()!=6)
            throw new InvalidDataException("HA_TRANSPORT_INVALID_PLAN");
    }
    private TransportProof Sign(string text)
    {
        using var rsa = signingCertificate.GetRSAPrivateKey() ?? throw new InvalidDataException("HA_TRANSPORT_KEY_REQUIRED");
        return new(options.NodeId, Convert.ToBase64String(signingCertificate.RawData),
            Convert.ToBase64String(rsa.SignData(System.Text.Encoding.UTF8.GetBytes(text),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)));
    }
    private void Authorize(string caller, OwnershipState state, TransportPlan? plan = null)
    {
        var status = state.TransportGuard();
        if (status.Mode != "Disabled" || status.StandaloneNode != caller || caller is not ("A" or "B") ||
            plan != null && (plan.ClusterId != options.ClusterId || plan.Generation != status.Generation || plan.StandaloneNode != caller))
            throw new InvalidOperationException("HA_TRANSPORT_DISABLE_REQUIRED");
    }
    private string Work(string id) => Path.Combine(options.StateDirectory,"transport-plans",id);
    private void SavePermit(TransportPermit permit)
    {
        TransportMigrationFiles.Write(Path.Combine(Work(permit.Proposal.PlanId),"permit.json"),permit);
        TransportMigrationFiles.Write(Marker,permit);
    }
    public TransportPrepared Prepare(TransportPlan plan, string caller, OwnershipState state)
    {
        lock (_gate) {
            Validate(plan); Authorize(caller,state,plan);
            if(!state.TransportLocked(plan.PlanId))throw new InvalidOperationException("HA_TRANSPORT_PLAN_REQUIRED");
            if(Environment.GetEnvironmentVariables().Keys.Cast<string>().Any(key=>
                key.Equals("Arbiter__CertificatePath",StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Arbiter__CertificatePasswordFile",StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("Arbiter__Members__",StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("HA_TRANSPORT_ENV_OVERRIDE");
            var previous=TransportMigrationFiles.Read(options.StateDirectory);
            if(previous?.Proposal.PlanId==plan.PlanId) {
                if(TransportMigrationFiles.Hash(previous.Proposal)!=TransportMigrationFiles.Hash(plan))throw new InvalidOperationException("HA_TRANSPORT_PLAN_CHANGED");
                return previous.Local;
            }
            if(previous is { Phase: "Prepared" or "Staged" } || previous is {Phase:"Committed"} && previous.FinalFingerprint!=options.MembershipFingerprint)
                throw new InvalidOperationException("HA_TRANSPORT_PENDING");
            // Verify write access before creating keys or reporting a plan ready.
            string probe=Path.Combine(Root,".transport-write-"+Guid.NewGuid().ToString("N"));
            try { TransportMigrationFiles.Write(probe,Array.Empty<byte>()); } finally { File.Delete(probe); }
            string work=Work(plan.PlanId); Directory.CreateDirectory(work);
            if(!OperatingSystem.IsWindows())File.SetUnixFileMode(work,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            var self=plan.Members.Single(x=>x.NodeId==options.NodeId);
            string Generate(string name,X509Certificate2 existing) {
                string[] hosts=[new Uri(self.ClientUrl).IdnHost,new Uri(self.PeerUrl).IdnHost];
                bool create=plan.RotateCertificates || hosts.Any(x=>!existing.MatchesHostname(x,false,false));
                using var certificate=create ? MakeCertificate("IPC-"+options.NodeId+"-"+name,hosts) : new X509Certificate2(existing);
                string password=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
                TransportMigrationFiles.Write(Path.Combine(work,name+".pfx"),certificate.Export(X509ContentType.Pfx,password));
                TransportMigrationFiles.Write(Path.Combine(work,name+".password"),System.Text.Encoding.UTF8.GetBytes(password));
                return ArbiterOptions.Fingerprint(certificate);
            }
            if(!plan.RotateCertificates && !string.IsNullOrEmpty(self.CertificateRef) &&
                !options.Self.Matches(self.CertificateRef,true))throw new InvalidOperationException("HA_TRANSPORT_USE_CERTIFICATE_ROTATION");
            string pin=Generate("member",signingCertificate);string? gateway=null;
            if(options.NodeId!="C") {
                string overlay=Path.Combine(Root,"gateway-ha.json");
                if(!File.Exists(overlay))throw new InvalidOperationException("HA_TRANSPORT_EMBEDDED_REQUIRED");
                var ha=TransportMigrationFiles.LoadJson(overlay)["Gateway"]!["HighAvailability"]!;
                using var cert=X509CertificateLoader.LoadPkcs12FromFile(ha["CertificatePath"]!.GetValue<string>(),
                    File.ReadAllText(ha["CertificatePasswordFile"]!.GetValue<string>()).TrimEnd('\r','\n'));
                gateway=Generate("gateway",cert);
            }
            self=self with { CertificateRef=pin,CertificateSha256=pin,GatewayCertificateSha256=gateway };
            var prepared=new TransportPrepared(self,Sign(TransportMigrationFiles.PreparedText(plan,self)));
            string identity=File.ReadAllText(Path.Combine(options.StateDirectory,"identity.json"));
            var permit=new TransportPermit("Prepared",plan,options.Members,options.MembershipFingerprint,identity,prepared);
            SavePermit(permit); return prepared;
        }
    }
    public TransportProof Stage(TransportStage input,string caller,OwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Plan);
        lock(_gate) {
            Authorize(caller,state,input.Plan);
            var permit=TransportMigrationFiles.Read(options.StateDirectory) ?? throw new InvalidOperationException("HA_TRANSPORT_PLAN_REQUIRED");
            if(permit.Proposal.PlanId!=input.Plan.PlanId || TransportMigrationFiles.Hash(permit.Proposal)!=TransportMigrationFiles.Hash(input.Plan) ||
                input.Prepared is not {Length:3} || input.Prepared.Any(x=>x==null||x.Member==null||x.Proof==null) || !input.Prepared.Select(x=>x.Member.NodeId).ToHashSet().SetEquals(["A","B","C"]))
                throw new InvalidOperationException("HA_TRANSPORT_PLAN_CHANGED");
            return StageVerified(input,permit);
        }
    }
    private TransportProof StageVerified(TransportStage input,TransportPermit permit)
    {
        // Each member signs its own newly generated identity; no private key crosses the network.
        foreach(var prepared in input.Prepared) {
            var wanted=input.Plan.Members.Single(m=>m.NodeId==prepared.Member.NodeId);
            if(new Uri(wanted.ClientUrl)!=new Uri(prepared.Member.ClientUrl) || new Uri(wanted.PeerUrl)!=new Uri(prepared.Member.PeerUrl) ||
                wanted.GatewayUrl!=prepared.Member.GatewayUrl)throw new InvalidDataException("HA_TRANSPORT_PLAN_CHANGED");
            var proof=prepared.Proof;
            using var cert=X509CertificateLoader.LoadCertificate(Convert.FromBase64String(proof.Certificate));
            using var rsa=cert.GetRSAPublicKey();
            if(proof.NodeId!=prepared.Member.NodeId || !permit.OldMembers.Single(x=>x.NodeId==proof.NodeId).Matches(ArbiterOptions.Fingerprint(cert),true) ||
                rsa==null || !rsa.VerifyData(System.Text.Encoding.UTF8.GetBytes(TransportMigrationFiles.PreparedText(input.Plan,prepared.Member)),
                    Convert.FromBase64String(proof.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))
                throw new InvalidDataException("HA_TRANSPORT_INVALID_PROOF");
        }
        var members=input.Prepared.Select(x=>x.Member).OrderBy(x=>x.NodeId,StringComparer.Ordinal).ToArray();
        if(members.Single(x=>x.NodeId==options.NodeId)!=permit.Local.Member)throw new InvalidDataException("HA_TRANSPORT_LOCAL_IDENTITY_CHANGED");
        string fingerprint=TransportMigrationFiles.Fingerprint(options.ClusterId,members);
        var next=permit with {Phase="Staged",Members=members,FinalFingerprint=fingerprint};
        if(permit.Phase=="Committed") {
            if(permit.FinalFingerprint!=fingerprint)throw new InvalidDataException("HA_TRANSPORT_PLAN_CHANGED");
            return Sign(TransportMigrationFiles.ReadyText(permit));
        }
        SavePermit(next);
        return Sign(TransportMigrationFiles.ReadyText(next));
    }
    public object Commit(TransportCommit input,string caller,OwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock(_gate) {
            var permit=TransportMigrationFiles.Read(options.StateDirectory) ?? throw new InvalidOperationException("HA_TRANSPORT_PLAN_REQUIRED");
            Authorize(caller,state,permit.Proposal);
            if(permit.Proposal.PlanId!=input.PlanId || permit.Phase is not ("Staged" or "Committed") || permit.Members==null)
                throw new InvalidOperationException("HA_TRANSPORT_PLAN_CHANGED");
            if(!state.TransportApplied(input.PlanId))throw new InvalidOperationException("HA_TRANSPORT_COMMIT_NOT_REPLAYED");
            TransportMigrationFiles.VerifyProofs(permit,input.Proofs,TransportMigrationFiles.ReadyText(permit));
            permit=permit with {Phase="Committed",ReadyProofs=input.Proofs};
            // Durable permit precedes files, making an interrupted file update retryable.
            SavePermit(permit);
            WriteConfigurations(permit);
            return new {nodeId=options.NodeId,state="PendingRestart"};
        }
    }
    public void Cancel(TransportId input,string caller,OwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock(_gate) {
            var permit=TransportMigrationFiles.Read(options.StateDirectory);
            if(permit==null || permit.Phase=="Completed" && permit.Proposal.PlanId!=input.PlanId)return;Authorize(caller,state,permit.Proposal);
            if(permit.Proposal.PlanId!=input.PlanId || permit.Phase=="Committed")throw new InvalidOperationException("HA_TRANSPORT_PENDING");
            File.Delete(Marker);
        }
    }
    public void Complete(TransportId input,string caller,OwnershipState state)
    {
        ArgumentNullException.ThrowIfNull(input);
        lock(_gate) {
            var permit=TransportMigrationFiles.VerifyCommitted(options);Authorize(caller,state,permit.Proposal);
            if(input.PlanId!=permit.Proposal.PlanId)throw new InvalidOperationException("HA_TRANSPORT_PLAN_CHANGED");
            SavePermit(permit with {Phase="Completed"});
        }
    }
    public bool BlocksEnable => TransportMigrationFiles.Read(options.StateDirectory) is {Phase:"Prepared" or "Staged" or "Committed"};
    public void RestorePolicy(OwnershipState state)
    {
        if(TransportMigrationFiles.Read(options.StateDirectory) is not {Phase:"Committed" or "Completed"})return;
        var permit=TransportMigrationFiles.VerifyCommitted(options);
        if(!state.TransportApplied(permit.Proposal.PlanId))throw new InvalidDataException("HA_TRANSPORT_COMMIT_NOT_REPLAYED");
    }
    public void ValidateConsensus(Request input)
    {
        var entry=TransportMigrationFiles.Read(options.StateDirectory) ?? throw new InvalidOperationException("HA_TRANSPORT_PLAN_REQUIRED");
        var transition=input.Migration ?? throw new InvalidDataException("HA_TRANSPORT_INVALID_PLAN");
        if(entry.Phase is not ("Staged" or "Committed") || entry.Proposal.PlanId!=transition.Plan.PlanId ||
            TransportMigrationFiles.Hash(entry.Proposal)!=TransportMigrationFiles.Hash(transition.Plan) ||
            TransportMigrationFiles.Hash(entry.Members)!=TransportMigrationFiles.Hash(transition.Members) ||
            TransportMigrationFiles.Hash(options.Members)!=TransportMigrationFiles.Hash(transition.OldMembers))
            throw new InvalidDataException("HA_TRANSPORT_PLAN_CHANGED");
        TransportMigrationFiles.VerifyProofs(entry,transition.Proofs,TransportMigrationFiles.ReadyText(entry));
    }
    public bool ReadyToFinish(string planId)=>TransportMigrationFiles.Read(options.StateDirectory) is {Phase:"Completed"} permit &&
        permit.Proposal.PlanId==planId && permit.FinalFingerprint==options.MembershipFingerprint;
    internal void WriteConfigurations(TransportPermit permit)
    {
        var p=permit.Proposal;var members=permit.Members!;
        string work=Work(p.PlanId),backup=Path.Combine(work,"before");Directory.CreateDirectory(backup);
        void Save(string path,JsonNode json) {
            string saved=Path.Combine(backup,Path.GetFileName(path));
            if(File.Exists(path)&&!File.Exists(saved))TransportMigrationFiles.Write(saved,File.ReadAllBytes(path));
            TransportMigrationFiles.Write(path,System.Text.Encoding.UTF8.GetBytes(json.ToJsonString(Json)));
        }
        var member=TransportMigrationFiles.LoadJson(configFile);
        member["Arbiter"]!["StateDirectory"]=options.StateDirectory;
        member["Arbiter"]!["Members"]=JsonSerializer.SerializeToNode(members,Json);
        member["Arbiter"]!["CertificatePath"]=Path.Combine(work,"member.pfx");
        member["Arbiter"]!["CertificatePasswordFile"]=Path.Combine(work,"member.password");
        Save(configFile,member);
        if(options.NodeId!="C") {
            string path=Path.Combine(Root,"gateway-ha.json");var overlay=TransportMigrationFiles.LoadJson(path);
            var ha=overlay["Gateway"]!["HighAvailability"]!;
            ha["Members"]=JsonSerializer.SerializeToNode(members,Json);ha["Endpoints"]=JsonSerializer.SerializeToNode(members.Select(x=>x.ClientUrl),Json);
            ha["Name"]=p.Name;ha["BusinessUrl"]=p.BusinessUrl;ha["PreferredNode"]=p.PreferredNode;
            ha["CertificatePath"]=Path.Combine(work,"gateway.pfx");ha["CertificatePasswordFile"]=Path.Combine(work,"gateway.password");
            ha["TrustRegisteredMemberPins"]=true;
            Save(path,overlay);
        }
        Save(Path.Combine(Root,"topology.setup.json"),JsonSerializer.SerializeToNode(new {p.ClusterId,p.Name,p.BusinessUrl,p.PreferredNode,Members=members},Json)!);
        Save(Path.Combine(Root,"identity.setup.json"),JsonSerializer.SerializeToNode(members.Single(x=>x.NodeId==options.NodeId),Json)!);
    }
    private static X509Certificate2 MakeCertificate(string subject,IEnumerable<string> hosts)
    {
        using var rsa=RSA.Create(3072);var request=new CertificateRequest("CN="+subject,rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();foreach(string host in hosts.Distinct(StringComparer.OrdinalIgnoreCase))
            if(IPAddress.TryParse(host.Trim('[',']'),out var ip))san.AddIpAddress(ip);else san.AddDnsName(host);
        request.CertificateExtensions.Add(san.Build());request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1"),new("1.3.6.1.5.5.7.3.2")},true));
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddYears(2));
    }
    public static void Map(WebApplication app,TransportMigration? migration,SemaphoreSlim serial)
    {
        if(migration==null)return;
        app.MapPost("/v1/transport/{action}",async(string action,HttpContext ctx,OwnershipState state,IRaftCluster raft)=> {
            if(action is not ("prepare" or "stage" or "commit" or "cancel" or "complete"))return Results.NotFound();
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await serial.WaitAsync(timeout.Token);
            try {
                await raft.ApplyReadBarrierAsync(timeout.Token);
                string caller=ctx.Items["caller"]?.ToString() ?? "";
                object? result=action switch {
                    "prepare"=>migration.Prepare((await ctx.Request.ReadFromJsonAsync<TransportPlan>(timeout.Token))!,caller,state),
                    "stage"=>migration.Stage((await ctx.Request.ReadFromJsonAsync<TransportStage>(timeout.Token))!,caller,state),
                    "commit"=>migration.Commit((await ctx.Request.ReadFromJsonAsync<TransportCommit>(timeout.Token))!,caller,state),
                    "complete"=>Complete(await ctx.Request.ReadFromJsonAsync<TransportId>(timeout.Token)),
                    _=>Cancel(await ctx.Request.ReadFromJsonAsync<TransportId>(timeout.Token))
                };
                object Cancel(TransportId? input){migration.Cancel(input!,caller,state);return new {cancelled=true};}
                object Complete(TransportId? input){migration.Complete(input!,caller,state);return new {completed=true};}
                return Results.Json(new {success=true,data=result});
            }
            catch(Exception e) when(e is InvalidOperationException or InvalidDataException or ArgumentException or JsonException)
            {return Results.Json(new{success=false,errorCode=e.Message,errorMessage=e.Message},statusCode:409);}
            catch(Exception e) when(e is CryptographicException or FormatException)
            {return Results.Json(new{success=false,errorCode="HA_TRANSPORT_INVALID_PROOF",errorMessage="迁移证书或三成员证明无效。"},statusCode:409);}
            catch(IOException)
            {return Results.Json(new{success=false,errorCode="HA_TRANSPORT_STORAGE_FAILED",errorMessage="无法持久写入组网配置，请检查目录权限和磁盘容量后重试原计划。"},statusCode:409);}
            catch(UnauthorizedAccessException){return Results.Json(new{success=false,errorCode="HA_TRANSPORT_CONFIG_READONLY",errorMessage="仲裁服务无权更新配置目录，请更新部署脚本后重新安装服务。"},statusCode:409);}
            finally{serial.Release();}
        });
    }
}
