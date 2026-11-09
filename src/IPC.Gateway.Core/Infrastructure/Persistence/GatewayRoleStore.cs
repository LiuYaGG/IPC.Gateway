/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayRoleStore
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
using System.Text.Json;
using System.Text.RegularExpressions;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using SqlSugar;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class GatewayRoleStore : IGatewayRoleRepository
{
    private static readonly Regex RoleNamePattern = new Regex("^[A-Za-z][A-Za-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private readonly SqlSugarConnectionFactory _factory;

    public GatewayRoleStore(GatewayDatabaseOptions options)
    {
        _factory = new SqlSugarConnectionFactory(options ?? new GatewayDatabaseOptions());
        EnsureSchema();
        EnsureDefaultRoles();
    }

    public IList<GatewayRoleInfo> GetRoles()
    {
        using ISqlSugarClient db = _factory.Create();
        Dictionary<string, int> userCounts = LoadUserCounts(db);
        return db.Queryable<GatewayRoleEntity>()
            .OrderBy(item => item.IsSystem, OrderByType.Desc)
            .OrderBy(item => item.Name)
            .ToList()
            .Select(item => ToInfo(item, userCounts))
            .ToList();
    }

    public async Task<IList<GatewayRoleInfo>> GetRolesAsync()
    {
        using ISqlSugarClient db = _factory.Create();
        Dictionary<string, int> userCounts = await LoadUserCountsAsync(db);
        return (await db.Queryable<GatewayRoleEntity>()
            .OrderBy(item => item.IsSystem, OrderByType.Desc)
            .OrderBy(item => item.Name)
            .ToListAsync())
            .Select(item => ToInfo(item, userCounts))
            .ToList();
    }

    public GatewayRoleInfo? FindByName(string roleName)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: false);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity entity = db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .First();
        return entity == null ? null : ToInfo(entity, LoadUserCounts(db));
    }

    public async Task<GatewayRoleInfo?> FindByNameAsync(string roleName)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: false);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return null;

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity? entity = await db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .FirstAsync();
        return entity == null ? null : ToInfo(entity, await LoadUserCountsAsync(db));
    }

    public GatewayRoleInfo UpsertRole(string roleName, string displayName, string description, bool enabled, IEnumerable<string> permissions)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: true);
        IReadOnlyList<string> normalizedPermissions = GatewayPermissions.Normalize(permissions);

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity? existing = db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .First();

        DateTime utcNow = DateTime.UtcNow;
        if (existing == null)
        {
            existing = new GatewayRoleEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedUtc = utcNow,
                IsSystem = IsSystemRole(normalizedName)
            };
        }
        else if (existing.IsSystem && !enabled)
        {
            throw new InvalidOperationException("系统角色不能停用。");
        }

        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim();
        existing.Description = description?.Trim() ?? string.Empty;
        existing.Enabled = existing.IsSystem || enabled;
        existing.PermissionsJson = JsonSerializer.Serialize(normalizedPermissions);
        existing.UpdatedUtc = utcNow;

        if (string.IsNullOrWhiteSpace(existing.Id))
            existing.Id = Guid.NewGuid().ToString("N");

        if (db.Queryable<GatewayRoleEntity>().Any(item => item.Id == existing.Id))
            db.Updateable(existing).ExecuteCommand();
        else
            db.Insertable(existing).ExecuteCommand();

        GatewayRoleInfo? saved = FindByName(normalizedName);
        return saved ?? throw new InvalidOperationException("Role was saved but could not be loaded.");
    }

    public async Task<GatewayRoleInfo> UpsertRoleAsync(string roleName, string displayName, string description, bool enabled, IEnumerable<string> permissions)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: true);
        IReadOnlyList<string> normalizedPermissions = GatewayPermissions.Normalize(permissions);

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity? existing = await db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .FirstAsync();

        DateTime utcNow = DateTime.UtcNow;
        if (existing == null)
        {
            existing = new GatewayRoleEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedUtc = utcNow,
                IsSystem = IsSystemRole(normalizedName)
            };
        }
        else if (existing.IsSystem && !enabled)
        {
            throw new InvalidOperationException("系统角色不能停用。");
        }

        existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedName : displayName.Trim();
        existing.Description = description?.Trim() ?? string.Empty;
        existing.Enabled = existing.IsSystem || enabled;
        existing.PermissionsJson = JsonSerializer.Serialize(normalizedPermissions);
        existing.UpdatedUtc = utcNow;

        if (string.IsNullOrWhiteSpace(existing.Id))
            existing.Id = Guid.NewGuid().ToString("N");

        GatewayRoleEntity? stored = await db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Id == existing.Id)
            .FirstAsync();
        if (stored != null)
            await db.Updateable(existing).ExecuteCommandAsync();
        else
            await db.Insertable(existing).ExecuteCommandAsync();

        GatewayRoleInfo? saved = await FindByNameAsync(normalizedName);
        return saved ?? throw new InvalidOperationException("Role was saved but could not be loaded.");
    }

    public void DeleteRole(string roleName)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: false);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return;

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity entity = db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .First();
        if (entity == null)
            return;

        if (entity.IsSystem || IsSystemRole(entity.Name))
            throw new InvalidOperationException("系统角色不能删除。");

        int userCount = db.Queryable<GatewayUserEntity>()
            .Where(item => item.Role.ToLower() == normalizedName.ToLower())
            .Count();
        if (userCount > 0)
            throw new InvalidOperationException("该角色仍有关联人员，不能删除。");

        db.Deleteable<GatewayRoleEntity>()
            .Where(item => item.Id == entity.Id)
            .ExecuteCommand();
    }

    public async Task DeleteRoleAsync(string roleName)
    {
        string normalizedName = NormalizeRoleName(roleName, throwOnInvalid: false);
        if (string.IsNullOrWhiteSpace(normalizedName))
            return;

        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity? entity = await db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == normalizedName.ToLower())
            .FirstAsync();
        if (entity == null)
            return;

        if (entity.IsSystem || IsSystemRole(entity.Name))
            throw new InvalidOperationException("系统角色不能删除。");

        int userCount = (await db.Queryable<GatewayUserEntity>()
            .Where(item => item.Role.ToLower() == normalizedName.ToLower())
            .Select(item => item.Id)
            .ToListAsync())
            .Count;
        if (userCount > 0)
            throw new InvalidOperationException("该角色仍有关联人员，不能删除。");

        await db.Deleteable<GatewayRoleEntity>()
            .Where(item => item.Id == entity.Id)
            .ExecuteCommandAsync();
    }

    public static IReadOnlyList<string> GetDefaultPermissions(string roleName)
    {
        return GatewayPermissions.GetDefaultPermissionsForRole(roleName);
    }

    private void EnsureSchema()
    {
        new GatewayDatabaseMigrator(_factory).Migrate();
    }

    private void EnsureDefaultRoles()
    {
        EnsureDefaultRole("Admin", "管理员", "拥有系统全部管理权限。");
        EnsureDefaultRole("Operator", "操作员", "可查看运行状态并维护网关配置。");
        EnsureDefaultRole("Viewer", "观察员", "仅查看运行状态和配置。");
    }

    private void EnsureDefaultRole(string name, string displayName, string description)
    {
        using ISqlSugarClient db = _factory.Create();
        GatewayRoleEntity? existing = db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == name.ToLower())
            .First();
        if (existing != null)
        {
            existing.IsSystem = true;
            existing.Enabled = true;
            if (string.IsNullOrWhiteSpace(existing.PermissionsJson) || existing.PermissionsJson == "[]")
                existing.PermissionsJson = JsonSerializer.Serialize(GetDefaultPermissions(name));
            existing.UpdatedUtc = DateTime.UtcNow;
            db.Updateable(existing).ExecuteCommand();
            return;
        }

        DateTime utcNow = DateTime.UtcNow;
        db.Insertable(new GatewayRoleEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            DisplayName = displayName,
            Description = description,
            Enabled = true,
            IsSystem = true,
            PermissionsJson = JsonSerializer.Serialize(GetDefaultPermissions(name)),
            CreatedUtc = utcNow,
            UpdatedUtc = utcNow
        }).ExecuteCommand();
    }

    private static Dictionary<string, int> LoadUserCounts(ISqlSugarClient db)
    {
        return db.Queryable<GatewayUserEntity>()
            .GroupBy(item => item.Role)
            .Select(item => new RoleUserCountProjection { Role = item.Role, Count = SqlFunc.AggregateCount(item.Role) })
            .ToList()
            .ToDictionary(item => item.Role ?? string.Empty, item => item.Count, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, int>> LoadUserCountsAsync(ISqlSugarClient db)
    {
        return (await db.Queryable<GatewayUserEntity>()
            .GroupBy(item => item.Role)
            .Select(item => new RoleUserCountProjection { Role = item.Role, Count = SqlFunc.AggregateCount(item.Role) })
            .ToListAsync())
            .ToDictionary(item => item.Role ?? string.Empty, item => item.Count, StringComparer.OrdinalIgnoreCase);
    }

    private static GatewayRoleInfo ToInfo(GatewayRoleEntity entity, IReadOnlyDictionary<string, int> userCounts)
    {
        DateTime createdUtc = entity.CreatedUtc == DateTime.MinValue ? DateTime.UtcNow : entity.CreatedUtc;
        DateTime updatedUtc = entity.UpdatedUtc == DateTime.MinValue ? createdUtc : entity.UpdatedUtc;
        return new GatewayRoleInfo
        {
            Id = entity.Id,
            Name = entity.Name,
            DisplayName = entity.DisplayName ?? string.Empty,
            Description = entity.Description ?? string.Empty,
            Enabled = entity.Enabled,
            IsSystem = entity.IsSystem,
            Permissions = ParsePermissions(entity.PermissionsJson).ToList(),
            UserCount = userCounts.TryGetValue(entity.Name, out int count) ? count : 0,
            CreatedTime = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc).ToLocalTime(),
            UpdatedTime = DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc).ToLocalTime()
        };
    }

    private static IReadOnlyList<string> ParsePermissions(string permissionsJson)
    {
        try
        {
            return GatewayPermissions.Normalize(JsonSerializer.Deserialize<IList<string>>(permissionsJson ?? "[]"));
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string NormalizeRoleName(string roleName, bool throwOnInvalid)
    {
        string value = roleName?.Trim() ?? string.Empty;
        if (string.Equals(value, "admin", StringComparison.OrdinalIgnoreCase))
            return "Admin";
        if (string.Equals(value, "operator", StringComparison.OrdinalIgnoreCase))
            return "Operator";
        if (string.Equals(value, "viewer", StringComparison.OrdinalIgnoreCase))
            return "Viewer";

        if (RoleNamePattern.IsMatch(value))
            return value;

        if (throwOnInvalid)
            throw new ArgumentException("角色编码需以字母开头，仅支持字母、数字、下划线和短横线。", nameof(roleName));
        return string.Empty;
    }

    private static bool IsSystemRole(string roleName)
    {
        return string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(roleName, "Operator", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(roleName, "Viewer", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RoleUserCountProjection
    {
        public string Role { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
