/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：DatabaseMigration
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Infrastructure.Persistence
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class DatabaseMigration
{
    public DatabaseMigration(string id, string description, string postgreSql, string mySql, string sqlServer, string sqlite)
    {
        Id = id;
        Description = description;
        SqlByProvider = new Dictionary<DbType, string>
        {
            [DbType.PostgreSQL] = postgreSql,
            [DbType.MySql] = mySql,
            [DbType.SqlServer] = sqlServer,
            [DbType.Sqlite] = sqlite
        };
    }

    public string Id { get; }
    public string Description { get; }
    public IReadOnlyDictionary<DbType, string> SqlByProvider { get; }

    public string GetSql(DbType dbType)
    {
        return SqlByProvider.TryGetValue(dbType, out string? sql) ? sql : SqlByProvider[DbType.PostgreSQL];
    }
}
