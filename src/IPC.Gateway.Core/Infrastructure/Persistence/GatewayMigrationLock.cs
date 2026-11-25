using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

// Held by the same physical session that runs the DDL. The database releases it if that process dies.
internal static class GatewayMigrationLock
{
    public static void Run(ISqlSugarClient db, string resource, Action action)
    {
        var type = db.CurrentConnectionConfig.DbType;
        if (type == DbType.Sqlite) { action(); return; }
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(resource));
        var key = new SugarParameter("@lockKey", type == DbType.PostgreSQL
            ? (object)BinaryPrimitives.ReadInt64BigEndian(digest) : Convert.ToHexString(digest));
        string acquire = type switch
        {
            DbType.SqlServer => "declare @r int; exec @r=sys.sp_getapplock @Resource=@lockKey,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=0; select case when @r>=0 then 1 else 0 end",
            DbType.MySql => "select get_lock(@lockKey,0)",
            DbType.PostgreSQL => "select case when pg_try_advisory_lock(@lockKey) then 1 else 0 end",
            _ => throw new NotSupportedException("Unsupported migration lock provider.")
        };
        string release = type switch
        {
            DbType.SqlServer => "exec sys.sp_releaseapplock @Resource=@lockKey,@LockOwner='Session'",
            DbType.MySql => "select release_lock(@lockKey)",
            _ => "select pg_advisory_unlock(@lockKey)"
        };
        // SqlSugar normally opens/closes on each statement. A session lock must survive transaction
        // boundaries and MySQL's implicit DDL commits.
        var cfg = db.CurrentConnectionConfig;
        bool autoClose = cfg.IsAutoCloseConnection;
        cfg.IsAutoCloseConnection = false;
        try
        {
            db.Ado.Open();
            var wait = Stopwatch.StartNew();
            while (db.Ado.GetInt(acquire, key) != 1)
            {
                if (wait.Elapsed >= TimeSpan.FromSeconds(60)) throw new TimeoutException("Another gateway is migrating the business database.");
                Thread.Sleep(250);
            }
            try { action(); }
            finally { db.Ado.ExecuteCommand(release, key); }
        }
        finally { db.Ado.Close(); cfg.IsAutoCloseConnection = autoClose; }
    }
}
