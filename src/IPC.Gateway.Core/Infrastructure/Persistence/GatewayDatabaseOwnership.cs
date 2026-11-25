using IPC.Plc.Communication.Core;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public interface IGatewayDatabaseOwner : IPlcWriteAttemptAuthorizer
{
    bool ManagedStandalone => false;
    string ClusterId { get; }
    string NodeId { get; }
    string InstanceId { get; }
}

// The arbiter elects the owner. This row serializes business database transactions with installation of its new epoch.
internal static class GatewayDatabaseOwnership
{
    private static SugarParameter[] Parameters(IGatewayDatabaseOwner owner) =>
        [new("@cluster", owner.ClusterId), new("@instance", owner.InstanceId), new("@epoch", owner.Epoch)];

    public static void Install(ISqlSugarClient db, IGatewayDatabaseOwner owner)
    {
        if (!owner.CanRunBusiness || owner.Epoch <= 0) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
        const string sql = "update gateway_ha_owner set cluster_id=@cluster, instance_id=@instance, epoch=@epoch, revision=revision+1 " +
            "where slot=1 and (cluster_id='' or cluster_id=@cluster) and (epoch<@epoch or (epoch=@epoch and instance_id=@instance))";
        if (db.Ado.ExecuteCommand(sql, Parameters(owner)) != 1) throw new PlcWriteDeniedException("HA_DATABASE_EPOCH_CONFLICT");
    }

    public static void GuardTransaction(ISqlSugarClient db, IGatewayDatabaseOwner? owner)
    {
        if (owner?.ManagedStandalone == true)
        {
            // Serialize an already-admitted singleton transaction with the next HA epoch.
            int locked = db.Ado.ExecuteCommand("update gateway_ha_owner set revision=revision+1 where slot=1 and cluster_id=@cluster",
                new SugarParameter("@cluster", owner.ClusterId));
            if (locked != 1 || !owner.ManagedStandalone || !owner.CanRunBusiness)
                throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
            return;
        }
        if (owner?.IsCluster != true) return;
        if (!owner.CanRunBusiness) throw new PlcWriteDeniedException("HA_NOT_ACTIVE");
        const string sql = "update gateway_ha_owner set revision=revision+1 where slot=1 and cluster_id=@cluster and instance_id=@instance and epoch=@epoch";
        if (db.Ado.ExecuteCommand(sql, Parameters(owner)) != 1) throw new PlcWriteDeniedException("HA_DATABASE_EPOCH_CONFLICT");
    }
}
