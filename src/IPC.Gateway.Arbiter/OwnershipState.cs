using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;

namespace IPC.Gateway.Arbiter;

public sealed record PolicyMember(string NodeId, string ClientUrl, string PeerUrl, string? GatewayUrl, string? CertificateRef);
public sealed record Policy(string Name, string BusinessUrl, string PreferredNode, int RenewSeconds, int LeaseSeconds,
    string? ClusterId = null, PolicyMember[]? Members = null);
public sealed record Request
{
    public string ClusterId { get; init; } = "";
    public string NodeId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string Epoch { get; init; } = "";
    public string ConfigVersion { get; init; } = "";
    public string ExpectedConfigVersion { get; init; } = "";
    public bool Ready { get; init; }
    public bool? Enabled { get; init; }
    public string? ModeGeneration { get; init; }
    public string? BusinessEpoch { get; init; }
    public DateTimeOffset? GrantedAtUtc { get; init; }
    public DateTimeOffset? BusinessReadyAtUtc { get; init; }
    public DateTimeOffset? FirstSampleAtUtc { get; init; }
    public string Target { get; init; } = "";
    public bool Maintenance { get; init; }
    public string CommandId { get; init; } = "";
    public string AttemptId { get; init; } = "";
    public string AttemptSequence { get; init; } = "";
    public string Digest { get; init; } = "";
    public string TargetDigest { get; init; } = "";
    public string SourceType { get; init; } = "";
    public int SegmentIndex { get; init; }
    public Policy? Config { get; init; }
    public string? Actor { get; init; }
    public string? Reason { get; init; }
    public TransportTransition? Migration { get; init; }
}

internal sealed record Proposal(string Id, string Operation, Request Request, long BaseIndex, DateTimeOffset Timestamp, bool Expired, bool AllReachable)
{
    // Old WAL entries must replay with their original admission rules.
    public int ControlVersion { get; init; }
}
internal sealed record Observation(string NodeId, string InstanceId, string ConfigVersion, bool Ready, DateTimeOffset Timestamp,
    string? BusinessEpoch, DateTimeOffset? GrantedAtUtc, DateTimeOffset? BusinessReadyAtUtc, DateTimeOffset? FirstSampleAtUtc);
internal sealed record Audit(string Operation, string RequestId, string NodeId, string Target, string Epoch, string CommitIndex,
    DateTimeOffset Timestamp, string Result, string CommandId, string AttemptId, string Digest, string TargetDigest,
    string SourceType, int SegmentIndex, string ActiveEpoch, string? Actor, string? Reason)
{
    public string? Source { get; init; }
    public string? EventType { get; init; }
    public string? PreviousEpoch { get; init; }
    public string? InstanceId { get; init; }
    public string? RelatedOperationId { get; init; }
    public string? Status { get; init; }
    public string? ReasonCode { get; init; }
}
internal sealed record AdminDecision(string Operation, string Fingerprint, string ActorNode, string Target, long StartEpoch,
    long Index, long PolicyVersion, string Error, long? CompletedEpoch = null, long? CompletedIndex = null, string? FailedAfterSubmission = null);
internal sealed record PermitReceipt(string AttemptId, string CommandId, string Digest, string TargetDigest, string SourceType, int SegmentIndex);
internal sealed class Registry
{
    public long Index { get; set; }
    public long Epoch { get; set; }
    public string? Owner { get; set; }
    public string? Instance { get; set; }
    public string? Target { get; set; }
    public string? ActiveProject { get; set; }
    public int LeaseAtGrant { get; set; }
    public int LongestLeaseInEpoch { get; set; }
    public long PolicyVersion { get; set; }
    public Policy Policy { get; set; } = new("", "", "", 1, 5);
    public Dictionary<string, Observation> Readiness { get; set; } = [];
    public HashSet<string> Maintenance { get; set; } = [];
    public long HighestAttemptSequence { get; set; }
    public SortedDictionary<long, PermitReceipt> Receipts { get; set; } = [];
    public Dictionary<string, long> AttemptIds { get; set; } = [];
    public Queue<PermitReceipt> RecentPermits { get; set; } = new();
    public List<Audit> Switchovers { get; set; } = [];
    public List<Audit> Writes { get; set; } = [];
    public Dictionary<string, AdminDecision> AdminDecisions { get; set; } = [];
    public string? TransferId { get; set; }
    // Audit context survives release and mode changes, which intentionally clear Owner.
    public string? LastGrantedNode { get; set; }
    public bool GrantAfterEnable { get; set; }
    public string? EnrollmentRequestId { get; set; }
    public string? EnrollmentNode { get; set; }
    public HashSet<string> CancelledEnrollments { get; set; } = [];
    public int StateFormat { get; set; }
    public string Mode { get; set; } = "Enabled";
    public long ModeGeneration { get; set; }
    public string? StandaloneNode { get; set; }
    public string? ModeOperation { get; set; }
    public HashSet<string> ParkedNodes { get; set; } = [];
    public HashSet<string> ModeAcks { get; set; } = [];
    public string? TransportPlanId { get; set; }
    public bool TransportInProgress { get; set; }
    public string? TransportOperationId { get; set; }
}

internal sealed partial class OwnershipState(ArbiterOptions options) : SimpleStateMachine(new DirectoryInfo(Path.Combine(options.StateDirectory, "snapshot")))
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object sync = new();
    private Registry registry = new() { StateFormat = 2 };
    private Dictionary<string, PermitReceipt> recentById = new(StringComparer.Ordinal);
    private long leaseDeadline;
    private long leaderTerm = -1;
    private (string Id, string Result) lastDecision;

    protected override async ValueTask RestoreAsync(FileInfo snapshotFile, CancellationToken token)
    {
        await using var stream = snapshotFile.OpenRead();
        var restored = await JsonSerializer.DeserializeAsync<Registry>(stream, Json, token) ?? throw new InvalidDataException("Invalid ownership snapshot.");
        if (restored.StateFormat < 2)
        {
            // v1 required both business nodes to persist the latch before the first policy.
            if (restored.PolicyVersion > 0) restored.ParkedNodes.UnionWith(BusinessNodes());
            else if (restored.EnrollmentRequestId != null)
                restored.ParkedNodes.UnionWith(restored.Readiness.Values.Where(n => n.Ready).Select(n => n.NodeId));
            restored.StateFormat = 2;
        }
        lock (sync)
        {
            registry = restored;
            registry.LastGrantedNode ??= restored.Owner ?? restored.Switchovers.LastOrDefault(a => a.Operation == "acquire" && a.Result == "Committed")?.NodeId;
            recentById = restored.RecentPermits.ToDictionary(r => r.AttemptId, StringComparer.Ordinal);
            leaderTerm = -1;
            leaseDeadline = NewDeadline(restored.LongestLeaseInEpoch);
        }
    }

    protected override ValueTask PersistAsync(IAsyncBinaryWriter writer, CancellationToken token)
    {
        byte[] bytes;
        lock (sync) bytes = JsonSerializer.SerializeToUtf8Bytes(registry, Json);
        return writer.Invoke(bytes, token);
    }

    internal Proposal Prepare(string operation, Request request, long term, bool allReachable)
    {
        lock (sync)
        {
            ObserveLeaderTerm(term);
            bool expired = Stopwatch.GetTimestamp() >= leaseDeadline;
            return new(Guid.NewGuid().ToString("N"), operation, request, registry.Index, DateTimeOffset.UtcNow, expired, allReachable) { ControlVersion = 2 };
        }
    }

    private static long NewDeadline(int leaseSeconds) => Stopwatch.GetTimestamp() + Stopwatch.Frequency * (leaseSeconds + 1L);

    private void ObserveLeaderTerm(long term)
    {
        if (leaderTerm == term) return;
        // A new leader cannot compare another machine's lease clock with its own.
        leaderTerm = term;
        leaseDeadline = NewDeadline(registry.LongestLeaseInEpoch);
    }

    internal string Result(string id)
    {
        lock (sync) return lastDecision.Id == id ? lastDecision.Result : "HA_COMMIT_UNKNOWN";
    }

    internal (string ConfigVersion, string CommitIndex) LocalProgress()
    {
        lock (sync) return (registry.PolicyVersion.ToString(), registry.Index.ToString());
    }

    internal object Lease()
    {
        lock (sync)
        {
            var r = registry;
            return new { owner = r.Owner, instanceId = r.Instance, epoch = r.Epoch.ToString(), commitIndex = r.Index.ToString(),
                leaseSeconds = r.LeaseAtGrant, renewSeconds = r.Policy.RenewSeconds, transferTarget = r.Target };
        }
    }

    internal object Status(bool quorum, string? leader, long term, ISet<Uri> reachable)
    {
        lock (sync)
        {
            var r = registry;
            if (quorum && leader == options.NodeId) ObserveLeaderTerm(term);
            bool current = quorum && r.Owner != null && Stopwatch.GetTimestamp() < leaseDeadline;
            return new { options.ClusterId, options.NodeId, options.Self.ClientUrl, options.Self.PeerUrl, owner = current ? r.Owner : null,
                lastKnownOwner = r.Owner, instanceId = current ? r.Instance : null, epoch = r.Epoch.ToString(), commitIndex = r.Index.ToString(),
                quorumAvailable = quorum, reachableMembers = reachable.Count, leader, authority = quorum ? "Quorum" : "LocalLastKnown",
                leaseSeconds = r.LeaseAtGrant == 0 ? r.Policy.LeaseSeconds : r.LeaseAtGrant, renewSeconds = r.Policy.RenewSeconds,
                transferTarget = r.Target, transferId = r.TransferId, configVersion = r.PolicyVersion.ToString(), policyVersion = r.PolicyVersion.ToString(),
                enrollmentRequested = r.EnrollmentRequestId != null, enrollmentRequestId = r.EnrollmentRequestId,
                enrollmentNode = r.EnrollmentNode, mode = r.Mode, modeGeneration = r.ModeGeneration.ToString(),
                standaloneNode = r.StandaloneNode, parkedNodes = r.ParkedNodes.ToArray(), modeAcks = r.ModeAcks.ToArray(),
                modeOperation = r.ModeOperation,transportPlanId=r.TransportPlanId,transportPending=r.TransportInProgress,transportOperationId=r.TransportOperationId,
                activeConfig = r.PolicyVersion > 0 ? r.Policy : null, readiness = r.Readiness.Values.ToArray(), maintenance = r.Maintenance.ToArray(),
                members = options.Members.Select(m => new {m.NodeId, m.ClientUrl, m.PeerUrl, reachable = reachable.Contains(m.PeerUrl),
                    arbiterRole = !quorum || leader == null ? "Unknown" : leader == m.NodeId ? "Leader" : "Follower" }).ToArray() };
        }
    }

    internal object Page(bool writes, int page, int size, string? nodeId, string? result, string? operation, DateTimeOffset? from, DateTimeOffset? to)
    {
        lock (sync)
        {
            var source = (writes ? registry.Writes : ReadSwitchovers()).Where(e =>
                (nodeId == null || e.NodeId == nodeId || !writes && (e.Source == nodeId || e.Target == nodeId)) && (result == null || e.Result == result) &&
                (operation == null || e.Operation == operation) &&
                (from == null || e.Timestamp >= from) && (to == null || e.Timestamp <= to)).Reverse().ToArray();
            int offset = (int)Math.Min(int.MaxValue, (long)(page - 1) * size);
            return new { items = source.Skip(offset).Take(size).ToArray(), total = source.Length, page, pageSize = size };
        }
    }

    private IEnumerable<Audit> ReadSwitchovers()
    {
        string? previousOwner = null;
        foreach (var entry in registry.Switchovers)
        {
            var row = entry;
            if (row.Operation == "acquire")
            {
                // Old snapshots retain real acquire entries. Expose them without inventing
                // a failover cause or a source that predates the retained audit window.
                row = row with { Source = row.Source ?? (row.Epoch == "1" ? null : previousOwner),
                    Target = string.IsNullOrEmpty(row.Target) ? row.NodeId : row.Target,
                    EventType = row.EventType ?? (row.Epoch == "1" ? "InitialGrant" : "OwnershipGrant"),
                    Status = row.Status ?? (row.Result == "Committed" ? "Completed" : "Rejected") };
                if (row.Result == "Committed") previousOwner = row.NodeId;
            }
            else row = row with { Source = row.Source ?? row.NodeId };
            if ((row.Operation is "transfer" or "config" or "maintenance" or "mode" or "transport") &&
                registry.AdminDecisions.TryGetValue(row.RequestId, out var decision) && row.Operation == decision.Operation)
                row = row with { Status = DecisionStatus(decision), ReasonCode = decision.FailedAfterSubmission ?? decision.Error };
            yield return row;
        }
    }

    private static string DecisionStatus(AdminDecision d) =>
        d.Error != "" || d.FailedAfterSubmission != null ? "Rejected" : d.Operation is "config" or "maintenance" or "transport" or "transport-finish" ? "Applied" : d.CompletedIndex != null ? "Completed" : "Pending";

    internal object? Operation(string type, string id)
    {
        lock (sync)
        {
            if (type == "permit")
                return registry.Writes.LastOrDefault(a => a.AttemptId == id && a.Result == "Committed") ??
                    registry.Writes.LastOrDefault(a => a.AttemptId == id);
            if (!registry.AdminDecisions.TryGetValue(id, out var d) || type != d.Operation)
                return type == "transfer" ? ReadSwitchovers().LastOrDefault(a => a.RequestId == id && (a.Operation is "acquire" or "release")) : null;
            string status = DecisionStatus(d);
            return new { operationId = id, source = d.ActorNode, target = d.Target, status, previousEpoch = d.StartEpoch.ToString(),
                epoch = d.CompletedEpoch?.ToString(), commitIndex = (d.CompletedIndex ?? d.Index).ToString(),
                configVersion = d.PolicyVersion.ToString(), reasonCode = d.FailedAfterSubmission ?? d.Error };
        }
    }

    protected override ValueTask<bool> ApplyAsync(LogEntry entry, CancellationToken token)
    {
        if (!entry.TryGetPayload(out var payload) || payload.IsEmpty) return new(false);
        var p = JsonSerializer.Deserialize<Proposal>(payload.ToArray(), Json) ?? throw new InvalidDataException("Empty control command.");
        lock (sync)
        {
            var r = p.Request;
            long previousEpoch = registry.Epoch;
            string? previousOwner = registry.Owner ?? registry.LastGrantedNode;
            string? transferId = registry.TransferId;
            string? transferTarget = registry.Target;
            bool grantAfterEnable = registry.GrantAfterEnable;
            string previousMode = registry.Mode;
            bool admin = p.Operation is "config" or "transfer" or "maintenance" or "mode" or "transport" or "transport-finish" or "transport-lock" or "transport-cancel";
            bool duplicate = false;
            string outcome;
            if (registry.Index != p.BaseIndex) outcome = "HA_STALE_STATE";
            else if (admin)
            {
                // Exclude the submitter's transient process ID so a client can resolve a lost response after restart.
                object parameters = p.Operation is "transport" or "transport-finish" or "transport-lock" or "transport-cancel"
                    ? new {p.Operation,r.NodeId,r.ExpectedConfigVersion,r.Migration,r.ModeGeneration,r.Target,r.Actor,r.Reason}
                    : p.Operation == "mode"
                    ? new { p.Operation, r.NodeId, r.ModeGeneration, r.Enabled, r.Target, r.Actor, r.Reason }
                    : new { p.Operation, r.NodeId, expectedVersion = p.Operation == "config" ? r.ExpectedConfigVersion : r.Epoch,
                        r.Target, r.Maintenance, r.Config, r.Actor, r.Reason };
                var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(parameters, Json)));
                if (registry.AdminDecisions.TryGetValue(r.RequestId, out var old))
                {
                    duplicate = true;
                    outcome = old.Fingerprint == fingerprint ? old.Error : "HA_ID_REUSED";
                }
                else
                {
                    outcome = Apply(p, entry.Index);
                    // A heartbeat can expire between management preflight and this commit.
                    // The enrollment worker retries the same operation once a candidate is ready.
                    if (!(p.ControlVersion >= 2 && p.Operation == "config" && registry.PolicyVersion == 0 && outcome == "HA_TARGET_NOT_READY"))
                        registry.AdminDecisions[r.RequestId] = new(p.Operation, fingerprint, r.NodeId, r.Target, registry.Epoch,
                            entry.Index, registry.PolicyVersion, outcome);
                }
            }
            else outcome = Apply(p, entry.Index);
            registry.Index = entry.Index;
            lastDecision = (p.Id, outcome);
            if (outcome == "" && previousMode == "Enabling" && registry.Mode == "Enabled") registry.GrantAfterEnable = true;
            if (!duplicate && p.Operation is not ("transport-lock" or "transport-cancel" or "transport-finish") && (admin || p.Operation is "permit" or "release" or "enroll" or "cancel-enroll" || p.Operation == "acquire" && outcome == ""))
            {
                string epoch = registry.Epoch.ToString();
                var row = new Audit(p.Operation, r.RequestId, r.NodeId, r.Target, p.Operation == "permit" ? r.Epoch : epoch, entry.Index.ToString(), p.Timestamp,
                    outcome == "" ? "Committed" : outcome, r.CommandId, r.AttemptId, r.Digest, r.TargetDigest, r.SourceType, r.SegmentIndex, epoch, r.Actor, r.Reason)
                {
                    Source = p.Operation == "acquire" ? previousOwner : r.NodeId,
                    PreviousEpoch = previousEpoch.ToString(), InstanceId = r.InstanceId,
                    Status = outcome != "" ? "Rejected" : p.Operation == "transfer" ? "Pending" : "Applied"
                };
                if (p.Operation == "acquire" && outcome == "")
                {
                    bool planned = transferId != null && transferTarget == r.NodeId;
                    var request = planned ? registry.Switchovers.LastOrDefault(a => a.Operation == "transfer" && a.RequestId == transferId) : null;
                    row = row with { Source = previousEpoch == 0 ? null : previousOwner, Target = r.NodeId,
                        RelatedOperationId = planned ? transferId : null, Actor = request?.Actor, Reason = request?.Reason,
                        Status = "Completed", EventType = previousEpoch == 0 ? "InitialGrant" : planned ? "PlannedTransfer" :
                            grantAfterEnable ? "ClusterActivation" : previousOwner == null ? "OwnershipGrant" :
                            previousOwner != r.NodeId ? "AutomaticFailover" : "OwnershipRecovery" };
                    registry.LastGrantedNode = r.NodeId;
                    registry.GrantAfterEnable = false;
                }
                else if (p.Operation == "release") row = row with { Target = transferTarget ?? "", EventType = "OwnershipRelease", RelatedOperationId = transferId };
                else if (p.Operation == "transfer") row = row with { EventType = "PlannedTransferRequest" };
                else if (p.Operation == "mode") row = row with { EventType = "ClusterModeChange" };
                Append(p.Operation == "permit" ? registry.Writes : registry.Switchovers, row, 4096);
            }
        }
        return new(entry.Index % (registry.Receipts.Count < 1024 ? 256 : 4096) == 0);
    }

    private static void Append<T>(List<T> list, T item, int limit)
    {
        if (list.Count >= limit) list.RemoveRange(0, list.Count - limit + 1);
        list.Add(item);
    }

    private IEnumerable<string> BusinessNodes() =>
        options.Members.Where(m => !string.IsNullOrEmpty(m.GatewayCertificateSha256)).Select(m => m.NodeId);

    private string Apply(Proposal p, long index)
    {
        var s = registry;
        var q = p.Request;
        bool owner = s.Owner == q.NodeId && s.Instance == q.InstanceId && s.Epoch.ToString() == q.Epoch;
        bool Available(string node) => !s.Maintenance.Contains(node) && s.Readiness.TryGetValue(node, out var r) &&
            r.Ready && r.Timestamp <= p.Timestamp.AddSeconds(5) && p.Timestamp - r.Timestamp <= TimeSpan.FromSeconds(Math.Max(30, s.Policy.LeaseSeconds * 3));
        bool business(string node) => options.Members.Any(m => m.NodeId == node && !string.IsNullOrEmpty(m.GatewayCertificateSha256));
        if (p.Operation == "mode")
        {
            if(q.Enabled==true && s.TransportInProgress)return "HA_TRANSPORT_PENDING";
            if (q.ModeGeneration != s.ModeGeneration.ToString()) return "HA_MODE_CHANGED";
            if (q.Enabled == null || !business(q.NodeId)) return "HA_INVALID_REQUEST";
            if (s.ModeOperation != null || s.TransferId != null) return "HA_OPERATION_PENDING";
            if (q.Enabled == false)
            {
                if (s.Mode != "Enabled" || q.Target != q.NodeId || s.Maintenance.Contains(q.Target)) return "HA_INVALID_TARGET";
                if (s.EnrollmentRequestId == null && s.PolicyVersion == 0) return "HA_NOT_CONFIGURED";
                if (!BusinessNodes().Where(n => n != q.Target).All(s.ParkedNodes.Contains)) return "HA_PEER_NOT_PARKED";
                s.StandaloneNode = q.Target;
                s.Mode = "Disabling";
            }
            else
            {
                if (s.Mode != "Disabled" || s.StandaloneNode == null) return "HA_MODE_CHANGED";
                s.Mode = "Enabling";
            }
            s.ModeOperation = q.RequestId;
            s.ModeGeneration++;
            s.ModeAcks.Clear();
            return "";
        }
        if (p.Operation == "mode-ack")
        {
            if (q.ModeGeneration != s.ModeGeneration.ToString()) return "HA_MODE_CHANGED";
            if (!business(q.NodeId) || s.Mode == "Disabled" || (s.Mode == "Enabled" && s.EnrollmentRequestId == null))
                return "HA_INVALID_REQUEST";
            s.ParkedNodes.Add(q.NodeId);
            if (s.Mode == "Enabled") return "";
            s.ModeAcks.Add(q.NodeId);
            // A parked proof requires the node to persist revocation, stop its business
            // period and drain pending writes before sending this acknowledgement.
            if (s.Owner == q.NodeId && s.Instance == q.InstanceId) ClearOwner(s);
            if (s.StandaloneNode != null && s.ModeAcks.Contains(s.StandaloneNode) &&
                BusinessNodes().All(s.ParkedNodes.Contains) && (s.Owner == null || p.Expired))
            {
                ClearOwner(s);
                bool enabling = s.Mode == "Enabling";
                s.Mode = enabling ? "Enabled" : "Disabled";
                if (enabling)
                {
                    s.StandaloneNode = null;
                    if (s.PolicyVersion == 0)
                    {
                        // The enrollment preceding standalone mode was superseded.
                        // Keep durable parking proofs, but bind any new draft to a new request.
                        if (s.EnrollmentRequestId != null) s.CancelledEnrollments.Add(s.EnrollmentRequestId);
                        s.EnrollmentRequestId = null;
                        s.EnrollmentNode = null;
                    }
                }
                s.Readiness.Clear();
                if (s.ModeOperation is { } id && s.AdminDecisions.TryGetValue(id, out var op))
                    s.AdminDecisions[id] = op with { CompletedIndex = index };
                s.ModeOperation = null;
            }
            return "";
        }
        // No leases, permits or configuration commits during standalone operation
        // or either transition. The old owner may still release after draining.
        if (p.Operation is "transport-lock" or "transport-cancel") return LockTransport(q,p.Operation=="transport-cancel");
        if (p.Operation == "transport-finish") return FinishTransport(q);
        if (p.Operation == "transport") return ApplyTransport(q);
        if (s.Mode != "Enabled" && p.Operation != "release") return "HA_CLUSTER_DISABLED";
        if (p.ControlVersion >= 2 && p.Operation is ("enroll" or "config") &&
            (q.ModeGeneration ?? "0") != s.ModeGeneration.ToString()) return "HA_MODE_CHANGED";
        if (p.Operation == "cancel-enroll")
        {
            if (s.CancelledEnrollments.Contains(q.Target)) return "";
            if (s.PolicyVersion != 0 || s.Owner != null || s.EnrollmentRequestId != q.Target ||
                s.EnrollmentNode != q.NodeId || s.ParkedNodes.Count != 0) return "HA_ENROLLMENT_CHANGED";
            s.CancelledEnrollments.Add(q.Target);
            s.EnrollmentRequestId = null;
            s.EnrollmentNode = null;
            s.Readiness.Clear();
            return "";
        }
        if (p.Operation == "enroll")
        {
            if (s.CancelledEnrollments.Contains(q.RequestId)) return "HA_ENROLLMENT_CANCELLED";
            if (p.ControlVersion >= 2 && s.PolicyVersion == 0 && s.EnrollmentRequestId != null && s.EnrollmentRequestId != q.RequestId)
                return "HA_OPERATION_PENDING";
            if (s.EnrollmentRequestId == null)
            {
                s.EnrollmentRequestId = q.RequestId;
                s.EnrollmentNode = q.NodeId;
                s.Readiness.Clear();
            }
            return "";
        }
        if (p.Operation == "readiness")
        {
            if (q.Ready && string.IsNullOrWhiteSpace(q.ConfigVersion)) return "HA_TARGET_NOT_READY";
            s.Readiness[q.NodeId] = new(q.NodeId, q.InstanceId, q.ConfigVersion, q.Ready, p.Timestamp,
                q.BusinessEpoch, q.GrantedAtUtc, q.BusinessReadyAtUtc, q.FirstSampleAtUtc);
            if (p.ControlVersion < 2 && q.Ready && s.EnrollmentRequestId != null) s.ParkedNodes.Add(q.NodeId);
            return "";
        }
        if (p.Operation == "config")
        {
            if(s.TransportInProgress)return "HA_TRANSPORT_PENDING";
            if (q.ExpectedConfigVersion != s.PolicyVersion.ToString()) return "HA_CONFIG_CONFLICT";
            if (q.Config is not { } cfg || cfg.RenewSeconds is < 1 or > 60 || cfg.LeaseSeconds < 3 * cfg.RenewSeconds ||
                cfg.LeaseSeconds > 300 || !business(cfg.PreferredNode) || cfg.ClusterId != options.ClusterId || !ValidMembers(cfg.Members) ||
                string.IsNullOrWhiteSpace(cfg.Name) || cfg.Name.Length > 160 ||
                !string.IsNullOrEmpty(cfg.BusinessUrl) && (!Uri.TryCreate(cfg.BusinessUrl, UriKind.Absolute, out var businessUrl) ||
                    businessUrl.Scheme is not ("https" or "http") || businessUrl.UserInfo != "")) return "HA_INVALID_POLICY";
            if (s.PolicyVersion == 0)
            {
                if (s.EnrollmentRequestId == null) return "HA_ENROLLMENT_REQUIRED";
                if (p.ControlVersion >= 2 && s.EnrollmentRequestId != "enroll-" + q.RequestId) return "HA_ENROLLMENT_CHANGED";
                var nodes = BusinessNodes().ToArray();
                if (p.ControlVersion >= 2)
                {
                    if (!nodes.All(s.ParkedNodes.Contains) || !nodes.Any(Available)) return "HA_TARGET_NOT_READY";
                }
                else
                {
                    if (!p.AllReachable) return "HA_MEMBERS_NOT_READY";
                    if (nodes.Any(n => !Available(n)) || s.Readiness[nodes[0]].ConfigVersion != s.Readiness[nodes[1]].ConfigVersion) return "HA_TARGET_NOT_READY";
                    s.ParkedNodes.UnionWith(nodes);
                }
            }
            s.Policy = cfg;
            s.PolicyVersion++;
            return "";
        }
        if (s.PolicyVersion == 0) return "HA_NOT_CONFIGURED";
        switch (p.Operation)
        {
            case "acquire":
                if (s.Owner != null && !p.Expired) return "HA_OWNER_ACTIVE";
                if (!q.Ready || !Available(q.NodeId) || s.Readiness[q.NodeId].InstanceId != q.InstanceId ||
                    s.Readiness[q.NodeId].ConfigVersion != q.ConfigVersion) return "HA_TARGET_NOT_READY";
                if (p.ControlVersion < 2 && s.ActiveProject != null && s.ActiveProject != q.ConfigVersion)
                {
                    // Existing clusters have an unprefixed release-dependent fingerprint.
                    // Admit that format transition once, after both business nodes attest
                    // to the same new configuration and the previous lease has ended.
                    bool upgradingFingerprint = s.ActiveProject.Length == 64 && s.ActiveProject.All(char.IsAsciiHexDigit)
                        && q.ConfigVersion.Length == 67 && q.ConfigVersion.StartsWith("v2:", StringComparison.Ordinal)
                        && q.ConfigVersion[3..].All(char.IsAsciiHexDigit);
                    if (!upgradingFingerprint || !options.Members.Where(m => business(m.NodeId)).All(m =>
                        Available(m.NodeId) && s.Readiness[m.NodeId].ConfigVersion == q.ConfigVersion))
                        return "HA_TARGET_NOT_READY";
                }
                if (s.Target != null && q.NodeId != s.Target)
                {
                    if (Available(s.Target) && (p.ControlVersion >= 2 || s.Readiness[s.Target].ConfigVersion == s.ActiveProject)) return "HA_HANDOVER_REQUIRED";
                    // The source has drained (or its lease expired) and the intended target disappeared.
                    // A healthy eligible gateway may take a new epoch without being stuck on an orphaned operation.
                    if (s.TransferId is {} abandoned && s.AdminDecisions.TryGetValue(abandoned, out var pending))
                        s.AdminDecisions[abandoned] = pending with { FailedAfterSubmission = "HA_TARGET_NOT_READY", CompletedIndex = index };
                    s.TransferId = null;
                }
                if (s.Epoch == 0 && q.NodeId != s.Policy.PreferredNode && Available(s.Policy.PreferredNode)) return "HA_PREFERRED_NODE";
                s.Owner = q.NodeId; s.Instance = q.InstanceId; s.Epoch++; s.ActiveProject = q.ConfigVersion;
                if (s.TransferId is {} id && s.AdminDecisions.TryGetValue(id, out var decision))
                    s.AdminDecisions[id] = decision with { CompletedEpoch = s.Epoch, CompletedIndex = index };
                s.Target = null; s.TransferId = null; s.HighestAttemptSequence = 0; s.Receipts.Clear(); s.AttemptIds.Clear(); s.LeaseAtGrant = s.Policy.LeaseSeconds;
                s.LongestLeaseInEpoch = s.LeaseAtGrant;
                leaseDeadline = NewDeadline(s.LeaseAtGrant);
                return "";
            case "renew":
                if (!owner || p.Expired) return "HA_STALE_EPOCH";
                if (s.Target != null)
                {
                    if (p.ControlVersion < 2 || Available(s.Target)) return "HA_HANDOVER_REQUIRED";
                    if (s.TransferId is {} abandoned && s.AdminDecisions.TryGetValue(abandoned, out var pending))
                        s.AdminDecisions[abandoned] = pending with { FailedAfterSubmission = "HA_TARGET_NOT_READY", CompletedIndex = index };
                    s.TransferId = null;
                    s.Target = null;
                }
                if (!q.Ready || !Available(q.NodeId) || s.Readiness[q.NodeId].InstanceId != q.InstanceId ||
                    s.Readiness[q.NodeId].ConfigVersion != q.ConfigVersion) return "HA_TARGET_NOT_READY";
                s.LeaseAtGrant = s.Policy.LeaseSeconds; s.ActiveProject = q.ConfigVersion;
                s.LongestLeaseInEpoch = Math.Max(s.LongestLeaseInEpoch, s.LeaseAtGrant);
                leaseDeadline = Math.Max(leaseDeadline, NewDeadline(s.LeaseAtGrant));
                return "";
            case "release":
                if (!owner) return "HA_STALE_EPOCH";
                ClearOwner(s);
                return "";
            case "permit":
                if (!owner || p.Expired || s.Target != null) return "HA_STALE_EPOCH";
                if (string.IsNullOrEmpty(q.CommandId) || string.IsNullOrEmpty(q.AttemptId) || q.AttemptId != q.RequestId ||
                    q.Digest is not { Length: 64 } || !q.Digest.All(Uri.IsHexDigit) ||
                    q.TargetDigest == null || q.TargetDigest.Length != 0 && (q.TargetDigest.Length != 64 || !q.TargetDigest.All(Uri.IsHexDigit)) ||
                    q.SourceType == null || q.SourceType.Length > 64 || q.SourceType.Any(char.IsControl) || q.SegmentIndex < 0 ||
                    !long.TryParse(q.AttemptSequence, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var sequence) || sequence <= 0) return "HA_INVALID_ATTEMPT";
                return RecordPermit(s, q, sequence);
            case "transfer":
            case "maintenance":
                if (s.Epoch.ToString() != q.Epoch) return "HA_STALE_EPOCH";
                if (!business(q.Target) && (p.Operation != "maintenance" || !options.Members.Any(m => m.NodeId == q.Target))) return "HA_INVALID_TARGET";
                if (p.Operation == "transfer")
                {
                    if (!owner || p.Expired) return "HA_STALE_EPOCH";
                    if (s.Target != null || q.Target == s.Owner || !Available(q.Target) ||
                        (p.ControlVersion < 2 && s.Readiness[q.Target].ConfigVersion != s.ActiveProject)) return "HA_TARGET_NOT_READY";
                    s.Target = q.Target;
                    s.TransferId = q.RequestId;
                }
                else if (q.Maintenance)
                {
                    if (!p.AllReachable || s.Owner == q.Target || s.Target == q.Target ||
                        (s.Maintenance.Count != 0 && !s.Maintenance.Contains(q.Target))) return "HA_MAINTENANCE_BLOCKED";
                    s.Maintenance.Add(q.Target);
                }
                else s.Maintenance.Remove(q.Target);
                return "";
            default: return "HA_UNKNOWN_COMMAND";
        }
    }

    private static void ClearOwner(Registry s)
    {
        s.Owner = null;
        s.Instance = null;
        s.LeaseAtGrant = 0;
        s.LongestLeaseInEpoch = 0;
    }

    private string RecordPermit(Registry state, Request request, long sequence)
    {
        const int window = 16384;
        var receipt = new PermitReceipt(request.AttemptId, request.CommandId, request.Digest.ToUpperInvariant(),
            request.TargetDigest.ToUpperInvariant(), request.SourceType, request.SegmentIndex);
        if (sequence <= state.HighestAttemptSequence - window) return "HA_STALE_ATTEMPT";
        if (state.Receipts.TryGetValue(sequence, out var previous))
            return previous == receipt ? "HA_DUPLICATE_ATTEMPT" : "HA_ID_REUSED";
        // An external caller may retry the same logical command at B without supplying the old
        // epoch. Preserve recent concrete authorizations across takeovers as well as restarts.
        if (recentById.TryGetValue(receipt.AttemptId, out var recent))
            return recent == receipt ? "HA_DUPLICATE_ATTEMPT" : "HA_ID_REUSED";
        if (state.AttemptIds.ContainsKey(receipt.AttemptId)) return "HA_ID_REUSED";
        state.HighestAttemptSequence = Math.Max(state.HighestAttemptSequence, sequence);
        while (state.Receipts.Count > 0)
        {
            var oldest = state.Receipts.First();
            if (oldest.Key > state.HighestAttemptSequence - window) break;
            state.Receipts.Remove(oldest.Key);
            state.AttemptIds.Remove(oldest.Value.AttemptId);
        }
        state.Receipts.Add(sequence, receipt);
        state.AttemptIds.Add(receipt.AttemptId, sequence);
        state.RecentPermits.Enqueue(receipt);
        recentById.Add(receipt.AttemptId, receipt);
        if (state.RecentPermits.Count > window) recentById.Remove(state.RecentPermits.Dequeue().AttemptId);
        return "";
    }

    private bool ValidMembers(PolicyMember[]? members)
    {
        if (members is not { Length: 3 } || members.Any(m => m is null)) return false;
        var expected=registry.PolicyVersion>0 && registry.Policy.Members is {Length:3} prior
            ? prior.Select(m=>new {m.NodeId,ClientUrl=new Uri(m.ClientUrl),PeerUrl=new Uri(m.PeerUrl)})
            : _originalMembers.Select(m=>new {m.NodeId,m.ClientUrl,m.PeerUrl});
        foreach (var member in expected)
        {
            var matches = members.Where(m => m.NodeId == member.NodeId).ToArray();
            if (matches is not [var configured] || !Uri.TryCreate(configured.ClientUrl, UriKind.Absolute, out var client) ||
                client != member.ClientUrl || !Uri.TryCreate(configured.PeerUrl, UriKind.Absolute, out var peer) || peer != member.PeerUrl ||
                configured.CertificateRef is { Length: > 256 } || !string.IsNullOrEmpty(configured.GatewayUrl) &&
                (!Uri.TryCreate(configured.GatewayUrl, UriKind.Absolute, out var gateway) || gateway.Scheme is not ("https" or "http") || gateway.UserInfo != ""))
                return false;
        }
        return true;
    }
}
