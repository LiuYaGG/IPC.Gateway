using Microsoft.Data.Sqlite;
using IPC.Plc.Communication.Core;

namespace IPC.Gateway.HighAvailability;

public sealed record HaWriteResult(string AttemptId, string CommandId, string Epoch, string NodeId,
    string InstanceId, string Digest, string Result, string? CommitIndex, DateTimeOffset Timestamp, string Target, string Address, string SourceType, int SegmentIndex, string TargetDigest);

/// <summary>Only the process which inserted the reservation may send. An interrupted send stays Unknown.</summary>
internal sealed class HaWriteJournal
{
    private readonly string _connection;

    public HaWriteJournal(string directory)
    {
        Directory.CreateDirectory(directory);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "write-attempts.sqlite") }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS attempts (
                attempt TEXT PRIMARY KEY, command TEXT NOT NULL, epoch TEXT NOT NULL, node TEXT NOT NULL, instance TEXT NOT NULL,
                digest TEXT NOT NULL, outcome TEXT NOT NULL, idx TEXT, utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_attempt_command ON attempts(command);
            """;
        command.ExecuteNonQuery();
        using var tx = db.BeginTransaction();
        command.Transaction = tx;
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) < 1)
        {
            command.CommandText = """
                ALTER TABLE attempts ADD COLUMN target TEXT NOT NULL DEFAULT '';
                ALTER TABLE attempts ADD COLUMN address TEXT NOT NULL DEFAULT '';
                ALTER TABLE attempts ADD COLUMN source TEXT NOT NULL DEFAULT '';
                ALTER TABLE attempts ADD COLUMN segment INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE attempts ADD COLUMN target_digest TEXT NOT NULL DEFAULT '';
                PRAGMA user_version=1;
                """;
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA synchronous=FULL;";
        cmd.ExecuteNonQuery();
        return db;
    }

    internal void Reserve(string command, string attempt, string epoch, string node, string instance, PlcWriteAttempt metadata)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "INSERT INTO attempts VALUES ($attempt,$command,$epoch,$node,$instance,$digest,'NotDispatched',NULL,$utc,$target,$address,$source,$segment,$targetDigest) ON CONFLICT(attempt) DO NOTHING";
        cmd.Parameters.AddWithValue("$attempt", attempt);
        cmd.Parameters.AddWithValue("$command", command);
        cmd.Parameters.AddWithValue("$epoch", epoch);
        cmd.Parameters.AddWithValue("$node", node);
        cmd.Parameters.AddWithValue("$instance", instance);
        cmd.Parameters.AddWithValue("$digest", metadata.Digest);
        cmd.Parameters.AddWithValue("$target", metadata.Target);
        cmd.Parameters.AddWithValue("$address", metadata.Address);
        cmd.Parameters.AddWithValue("$source", metadata.SourceType);
        cmd.Parameters.AddWithValue("$segment", metadata.SegmentIndex);
        cmd.Parameters.AddWithValue("$targetDigest", metadata.TargetDigest);
        cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        if (cmd.ExecuteNonQuery() != 1) throw new PlcWriteDeniedException("HA_DUPLICATE_ATTEMPT");
    }

    internal void Update(string attempt, string result, string? index = null)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE attempts SET outcome=$outcome, idx=COALESCE($idx,idx) WHERE attempt=$attempt";
        cmd.Parameters.AddWithValue("$outcome", result);
        cmd.Parameters.AddWithValue("$idx", (object?)index ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$attempt", attempt);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("HA_WRITE_JOURNAL_UNAVAILABLE");
    }

    internal IReadOnlyList<HaWriteResult> Read(int limit)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT attempt,command,epoch,node,instance,digest,outcome,idx,utc,target,address,source,segment,target_digest FROM attempts ORDER BY rowid DESC LIMIT $n";
        cmd.Parameters.AddWithValue("$n", Math.Clamp(limit,1,500));
        return Rows(cmd);
    }

    internal (long Total,IReadOnlyList<HaWriteResult> Items) Search(int page,int size,string? node,string? result,string? from,string? to,string? commandId)
    {
        using var db=Open();
        using var tx=db.BeginTransaction(deferred:true);
        using var cmd=db.CreateCommand();
        cmd.Transaction=tx;
        string where=" WHERE ($node='' OR node=$node) AND ($result='' OR outcome=$result) AND ($from='' OR julianday(utc)>=julianday($from)) AND ($to='' OR julianday(utc)<=julianday($to))";
        if (!string.IsNullOrEmpty(commandId))
        {
            where += " AND command=$command";
            cmd.Parameters.AddWithValue("$command", commandId);
        }
        foreach (var (key,value) in new[]{("$node",node),("$result",result),("$from",from),("$to",to)})
            cmd.Parameters.AddWithValue(key,value ?? "");
        cmd.CommandText="SELECT COUNT(*) FROM attempts"+where;
        long total=(long)cmd.ExecuteScalar()!;
        cmd.CommandText="SELECT attempt,command,epoch,node,instance,digest,outcome,idx,utc,target,address,source,segment,target_digest FROM attempts"+where+" ORDER BY rowid DESC LIMIT $n OFFSET $offset";
        cmd.Parameters.AddWithValue("$n",size);
        cmd.Parameters.AddWithValue("$offset",(long)(page-1)*size);
        var rows=Rows(cmd);
        tx.Commit();
        return(total,rows);
    }

    internal IReadOnlyDictionary<string,HaWriteResult> Find(string[] ids)
    {
        if (ids.Length==0) return new Dictionary<string,HaWriteResult>();
        using var db=Open();
        using var cmd=db.CreateCommand();
        var parameters=ids.Distinct().Select((value,i)=>cmd.Parameters.AddWithValue("$id"+i,value).ParameterName).ToArray();
        cmd.CommandText="SELECT attempt,command,epoch,node,instance,digest,outcome,idx,utc,target,address,source,segment,target_digest FROM attempts WHERE attempt IN ("+string.Join(',',parameters)+")";
        return Rows(cmd).ToDictionary(x=>x.AttemptId);
    }

    private static IReadOnlyList<HaWriteResult> Rows(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var results = new List<HaWriteResult>();
        while (r.Read())
            results.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                r.GetString(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), DateTimeOffset.Parse(r.GetString(8)),
                r.GetString(9),r.GetString(10),r.GetString(11),r.GetInt32(12),r.GetString(13)));
        return results;
    }
}
