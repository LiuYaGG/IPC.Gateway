/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Infrastructure.Persistence
* 项目描述 ：
* 类 名 称 ：GatewayUserStore
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
using System.Security.Cryptography;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;

namespace IPC.Gateway.Core.Infrastructure.Persistence;

public sealed class GatewayUserStore : IGatewayUserRepository
{
    private const int PasswordIterations = 100000;
    private const int PasswordHashLength = 32;
    private readonly SqlSugarConnectionFactory _factory;
    private readonly GatewayBootstrapUserOptions _bootstrapOptions;
    private readonly GatewayAccountSecurityOptions _securityOptions;

    public GatewayUserStore(GatewayDatabaseOptions options, GatewayBootstrapUserOptions? bootstrapOptions = null)
        : this(options, bootstrapOptions, new GatewayAccountSecurityOptions())
    {
    }

    public GatewayUserStore(GatewayDatabaseOptions options, GatewayBootstrapUserOptions? bootstrapOptions, GatewayAccountSecurityOptions? securityOptions)
    {
        _factory = new SqlSugarConnectionFactory(options ?? new GatewayDatabaseOptions());
        _bootstrapOptions = bootstrapOptions ?? new GatewayBootstrapUserOptions();
        _securityOptions = securityOptions ?? new GatewayAccountSecurityOptions();
        EnsureSchema();
        _ = new GatewayRoleStore(options ?? new GatewayDatabaseOptions());
        EnsureDefaultAdmin();
    }

    public GatewayUserInfo? ValidatePassword(string username, string password)
    {
        GatewayUserAuthenticationResult result = Authenticate(username, password, new GatewayAccountLockoutOptions { Enabled = false });
        return result.Success ? result.User : null;
    }

    public async Task<GatewayUserInfo?> ValidatePasswordAsync(string username, string password)
    {
        GatewayUserAuthenticationResult result = await AuthenticateAsync(username, password, new GatewayAccountLockoutOptions { Enabled = false });
        return result.Success ? result.User : null;
    }

    public GatewayUserAuthenticationResult Authenticate(string username, string password, GatewayAccountLockoutOptions? lockoutOptions)
    {
        if (string.IsNullOrWhiteSpace(username))
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");

        GatewayAccountLockoutOptions options = lockoutOptions ?? new GatewayAccountLockoutOptions();
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedUsername = username.Trim();
        GatewayUserEntity entity = db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower())
            .First();

        if (entity == null || !entity.Enabled)
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");

        DateTime nowUtc = DateTime.UtcNow;
        if (options.Enabled && entity.LockoutEndUtc.HasValue && entity.LockoutEndUtc.Value > nowUtc)
            return GatewayUserAuthenticationResult.LockedOut(ToLocalTime(entity.LockoutEndUtc.Value));

        if (!VerifyPassword(password, entity.PasswordSalt, entity.PasswordHash))
        {
            if (options.Enabled)
                RegisterFailedLogin(db, entity, options, nowUtc);
            if (options.Enabled && entity.LockoutEndUtc.HasValue && entity.LockoutEndUtc.Value > nowUtc)
                return GatewayUserAuthenticationResult.LockedOut(ToLocalTime(entity.LockoutEndUtc.Value));
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");
        }

        int changed = SuccessfulLogin(db, entity, options, nowUtc).ExecuteCommand();
        if (changed != 1) return GatewayUserAuthenticationResult.Fail("账号状态已变化，请重新登录。");
        GatewayUserInfo user = ToInfo(entity, includePassword: false);
        return GatewayUserAuthenticationResult.Ok(user);
    }

    public async Task<GatewayUserAuthenticationResult> AuthenticateAsync(string username, string password, GatewayAccountLockoutOptions? lockoutOptions)
    {
        if (string.IsNullOrWhiteSpace(username))
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");

        GatewayAccountLockoutOptions options = lockoutOptions ?? new GatewayAccountLockoutOptions();
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedUsername = username.Trim();
        GatewayUserEntity? entity = await db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower())
            .FirstAsync();

        if (entity == null || !entity.Enabled)
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");

        DateTime nowUtc = DateTime.UtcNow;
        if (options.Enabled && entity.LockoutEndUtc.HasValue && entity.LockoutEndUtc.Value > nowUtc)
            return GatewayUserAuthenticationResult.LockedOut(ToLocalTime(entity.LockoutEndUtc.Value));

        if (!VerifyPassword(password, entity.PasswordSalt, entity.PasswordHash))
        {
            if (options.Enabled)
                await RegisterFailedLoginAsync(db, entity, options, nowUtc);
            if (options.Enabled && entity.LockoutEndUtc.HasValue && entity.LockoutEndUtc.Value > nowUtc)
                return GatewayUserAuthenticationResult.LockedOut(ToLocalTime(entity.LockoutEndUtc.Value));
            return GatewayUserAuthenticationResult.Fail("账号或密码错误。");
        }

        int changed = await SuccessfulLogin(db, entity, options, nowUtc).ExecuteCommandAsync();
        if (changed != 1) return GatewayUserAuthenticationResult.Fail("账号状态已变化，请重新登录。");
        GatewayUserInfo user = ToInfo(entity, includePassword: false);
        return GatewayUserAuthenticationResult.Ok(user);
    }

    public GatewayUserInfo? FindByUsername(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        GatewayUserEntity entity = db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == username.Trim().ToLower())
            .First();

        return entity == null ? null : ToInfo(entity, includePassword: true);
    }

    public async Task<GatewayUserInfo?> FindByUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return null;

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        GatewayUserEntity? entity = await db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == username.Trim().ToLower())
            .FirstAsync();

        return entity == null ? null : ToInfo(entity, includePassword: true);
    }

    public IList<GatewayUserInfo> GetUsers()
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        return db.Queryable<GatewayUserEntity>()
            .OrderBy(item => item.Username)
            .ToList()
            .Select(item => ToInfo(item, includePassword: false))
            .ToList();
    }

    public async Task<IList<GatewayUserInfo>> GetUsersAsync()
    {
        using SqlSugar.ISqlSugarClient db = _factory.Create();
        return (await db.Queryable<GatewayUserEntity>()
            .OrderBy(item => item.Username)
            .ToListAsync())
            .Select(item => ToInfo(item, includePassword: false))
            .ToList();
    }

    public GatewayUserInfo UpsertUser(string username, string displayName, string role, bool enabled, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("请输入账号。", nameof(username));

        string normalizedUsername = username.Trim();
        bool updatePassword = !string.IsNullOrWhiteSpace(password);
        string salt = string.Empty;
        string hash = string.Empty;
        if (updatePassword)
        {
            GatewayPasswordPolicyValidator.Validate(normalizedUsername, password, _securityOptions.Password);
            CreatePasswordHash(password, out salt, out hash);
        }

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedRole = NormalizeRole(role, db);
        GatewayUserEntity? existing = db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower())
            .First();

        if (existing == null)
        {
            if (!updatePassword)
                throw new ArgumentException("新增人员时必须填写密码。", nameof(password));

            db.Insertable(new GatewayUserEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = normalizedUsername,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim(),
                Role = normalizedRole,
                Enabled = enabled,
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordChangedUtc = DateTime.UtcNow,
                FailedLoginCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow
            }).ExecuteCommand();
        }
        else
        {
            string previousStamp = existing.SecurityStamp;
            bool invalidate = updatePassword || existing.Enabled != enabled || existing.Role != normalizedRole;
            if (invalidate) existing.SecurityStamp = Guid.NewGuid().ToString("N");
            existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim();
            existing.Role = normalizedRole;
            existing.Enabled = enabled;
            if (updatePassword)
            {
                existing.PasswordHash = hash;
                existing.PasswordSalt = salt;
                existing.PasswordChangedUtc = DateTime.UtcNow;
                existing.FailedLoginCount = 0;
                existing.LockoutEndUtc = null;
            }

            if (UpdateProfile(db, existing, updatePassword, invalidate, previousStamp).ExecuteCommand() != 1)
                throw new InvalidOperationException("账号状态已变化，请重新加载后保存。");
        }

        GatewayUserInfo? saved = FindByUsername(normalizedUsername);
        if (saved == null)
            throw new InvalidOperationException("User was saved but could not be loaded.");

        saved.PasswordHash = string.Empty;
        saved.PasswordSalt = string.Empty;
        return saved;
    }

    public async Task<GatewayUserInfo> UpsertUserAsync(string username, string displayName, string role, bool enabled, string password)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("请输入账号。", nameof(username));

        string normalizedUsername = username.Trim();
        bool updatePassword = !string.IsNullOrWhiteSpace(password);
        string salt = string.Empty;
        string hash = string.Empty;
        if (updatePassword)
        {
            GatewayPasswordPolicyValidator.Validate(normalizedUsername, password, _securityOptions.Password);
            CreatePasswordHash(password, out salt, out hash);
        }

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedRole = await NormalizeRoleAsync(role, db);
        GatewayUserEntity? existing = await db.Queryable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower())
            .FirstAsync();

        if (existing == null)
        {
            if (!updatePassword)
                throw new ArgumentException("新增人员时必须填写密码。", nameof(password));

            await db.Insertable(new GatewayUserEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Username = normalizedUsername,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim(),
                Role = normalizedRole,
                Enabled = enabled,
                PasswordHash = hash,
                PasswordSalt = salt,
                PasswordChangedUtc = DateTime.UtcNow,
                FailedLoginCount = 0,
                SecurityStamp = Guid.NewGuid().ToString("N"),
                CreatedUtc = DateTime.UtcNow
            }).ExecuteCommandAsync();
        }
        else
        {
            string previousStamp = existing.SecurityStamp;
            bool invalidate = updatePassword || existing.Enabled != enabled || existing.Role != normalizedRole;
            if (invalidate) existing.SecurityStamp = Guid.NewGuid().ToString("N");
            existing.DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim();
            existing.Role = normalizedRole;
            existing.Enabled = enabled;
            if (updatePassword)
            {
                existing.PasswordHash = hash;
                existing.PasswordSalt = salt;
                existing.PasswordChangedUtc = DateTime.UtcNow;
                existing.FailedLoginCount = 0;
                existing.LockoutEndUtc = null;
            }

            if (await UpdateProfile(db, existing, updatePassword, invalidate, previousStamp).ExecuteCommandAsync() != 1)
                throw new InvalidOperationException("账号状态已变化，请重新加载后保存。");
        }

        GatewayUserInfo? saved = await FindByUsernameAsync(normalizedUsername);
        if (saved == null)
            throw new InvalidOperationException("User was saved but could not be loaded.");

        saved.PasswordHash = string.Empty;
        saved.PasswordSalt = string.Empty;
        return saved;
    }

    public void DeleteUser(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedUsername = username.Trim();
        db.Deleteable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower() && item.Username.ToLower() != "admin")
            .ExecuteCommand();
    }

    public async Task DeleteUserAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return;

        using SqlSugar.ISqlSugarClient db = _factory.Create();
        string normalizedUsername = username.Trim();
        await db.Deleteable<GatewayUserEntity>()
            .Where(item => item.Username.ToLower() == normalizedUsername.ToLower() && item.Username.ToLower() != "admin")
            .ExecuteCommandAsync();
    }

    private void EnsureSchema()
    {
        new GatewayDatabaseMigrator(_factory).Migrate();
    }

    private void EnsureDefaultAdmin()
    {
        string username = string.IsNullOrWhiteSpace(_bootstrapOptions.AdminUsername)
            ? "admin"
            : _bootstrapOptions.AdminUsername.Trim();

        if (FindByUsername(username) != null || !_bootstrapOptions.AutoCreateAdmin)
            return;

        if (string.IsNullOrWhiteSpace(_bootstrapOptions.AdminPassword))
            throw new InvalidOperationException("首次创建管理员账号前必须配置 Gateway:Auth:BootstrapAdminPassword。");

        string displayName = string.IsNullOrWhiteSpace(_bootstrapOptions.AdminDisplayName)
            ? "System Administrator"
            : _bootstrapOptions.AdminDisplayName.Trim();
        UpsertUser(username, displayName, "Admin", true, _bootstrapOptions.AdminPassword);
    }

    private static GatewayUserInfo ToInfo(GatewayUserEntity entity, bool includePassword)
    {
        return new GatewayUserInfo
        {
            Id = entity.Id,
            Username = entity.Username,
            DisplayName = entity.DisplayName ?? string.Empty,
            Role = entity.Role,
            Enabled = entity.Enabled,
            SecurityStamp = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(entity.Id + "|" + entity.PasswordHash + "|" + entity.PasswordSalt + "|" + entity.SecurityStamp))),
            PasswordHash = includePassword ? entity.PasswordHash : string.Empty,
            PasswordSalt = includePassword ? entity.PasswordSalt : string.Empty,
            CreatedTime = DateTime.SpecifyKind(entity.CreatedUtc, DateTimeKind.Utc).ToLocalTime(),
            PasswordChangedTime = ToLocalTime(entity.PasswordChangedUtc),
            LastLoginTime = ToLocalTime(entity.LastLoginUtc),
            LastFailedLoginTime = ToLocalTime(entity.LastFailedLoginUtc),
            FailedLoginCount = entity.FailedLoginCount,
            LockoutEndTime = ToLocalTime(entity.LockoutEndUtc)
        };
    }

    private static SqlSugar.IUpdateable<GatewayUserEntity> UpdateProfile(SqlSugar.ISqlSugarClient db,GatewayUserEntity entity,bool password,bool invalidate,string previousStamp)
    {
        var columns = new List<string> {nameof(GatewayUserEntity.DisplayName),nameof(GatewayUserEntity.Role),nameof(GatewayUserEntity.Enabled)};
        if(invalidate)columns.Add(nameof(GatewayUserEntity.SecurityStamp));
        if(password)columns.AddRange([nameof(GatewayUserEntity.PasswordHash),nameof(GatewayUserEntity.PasswordSalt),nameof(GatewayUserEntity.PasswordChangedUtc),nameof(GatewayUserEntity.FailedLoginCount),nameof(GatewayUserEntity.LockoutEndUtc)]);
        return db.Updateable(entity).UpdateColumns(columns.ToArray()).Where(x=>x.Id==entity.Id&&x.SecurityStamp==previousStamp);
    }

    // Each update changes only authentication state and compares the verified credential.
    // Concurrent A/B failures increment the database's current counter.
    private static SqlSugar.IUpdateable<GatewayUserEntity> SuccessfulLogin(SqlSugar.ISqlSugarClient db, GatewayUserEntity entity, GatewayAccountLockoutOptions options, DateTime now)
    {
        var update = db.Updateable<GatewayUserEntity>().SetColumns(x => x.LastLoginUtc == now)
            .Where(x => x.Id == entity.Id && x.Enabled && x.PasswordHash == entity.PasswordHash && x.SecurityStamp == entity.SecurityStamp);
        if (options.Enabled) update = update.Where(x => x.LockoutEndUtc == null || x.LockoutEndUtc <= now);
        if (options.ResetFailedCountOnSuccess) update = update.SetColumns(x => new GatewayUserEntity { FailedLoginCount = 0, LockoutEndUtc = null });
        return update;
    }

    private static SqlSugar.IUpdateable<GatewayUserEntity> FailedLogin(SqlSugar.ISqlSugarClient db, GatewayUserEntity entity, GatewayAccountLockoutOptions options, DateTime now)
    {
        int max = Math.Max(1, options.MaxFailedAttempts);
        DateTime until = now.AddMinutes(Math.Clamp(options.LockoutMinutes, 1, 1440));
        return db.Updateable<GatewayUserEntity>().SetColumns(x => new GatewayUserEntity {
            LastFailedLoginUtc = now,
            FailedLoginCount = x.LockoutEndUtc != null && x.LockoutEndUtc <= now ? 1 : x.FailedLoginCount + 1,
            LockoutEndUtc = (x.LockoutEndUtc != null && x.LockoutEndUtc <= now ? 1 : x.FailedLoginCount + 1) >= max ? until : null
        }).Where(x => x.Id == entity.Id && x.Enabled && x.PasswordHash == entity.PasswordHash && x.SecurityStamp == entity.SecurityStamp &&
            (x.LockoutEndUtc == null || x.LockoutEndUtc <= now));
    }
    private static void RegisterFailedLogin(SqlSugar.ISqlSugarClient db, GatewayUserEntity entity, GatewayAccountLockoutOptions options, DateTime now)
    {
        FailedLogin(db,entity,options,now).ExecuteCommand();
        var current = db.Queryable<GatewayUserEntity>().InSingle(entity.Id);
        entity.LockoutEndUtc = current?.LockoutEndUtc;
    }
    private static async Task RegisterFailedLoginAsync(SqlSugar.ISqlSugarClient db, GatewayUserEntity entity, GatewayAccountLockoutOptions options, DateTime now)
    {
        await FailedLogin(db,entity,options,now).ExecuteCommandAsync();
        var current = await db.Queryable<GatewayUserEntity>().InSingleAsync(entity.Id);
        entity.LockoutEndUtc = current?.LockoutEndUtc;
    }

    private static SqlSugar.IUpdateable<GatewayUserEntity>? PasswordUpdate(SqlSugar.ISqlSugarClient db,GatewayUserEntity? entity,string expectedStamp,string password)
    {
        if(entity==null||!entity.Enabled||ToInfo(entity,false).SecurityStamp!=expectedStamp)return null;
        CreatePasswordHash(password,out string salt,out string hash);
        string stamp=Guid.NewGuid().ToString("N");DateTime now=DateTime.UtcNow;
        return db.Updateable<GatewayUserEntity>().SetColumns(x=>new GatewayUserEntity {PasswordHash=hash,PasswordSalt=salt,
            SecurityStamp=stamp,PasswordChangedUtc=now,FailedLoginCount=0,LockoutEndUtc=null})
            .Where(x=>x.Id==entity.Id&&x.Enabled&&x.SecurityStamp==entity.SecurityStamp&&x.PasswordHash==entity.PasswordHash);
    }
    public bool ChangePasswordIfCurrent(string userId,string securityStamp,string newPassword)
    {
        using var db=_factory.Create();var entity=db.Queryable<GatewayUserEntity>().InSingle(userId);
        var update=PasswordUpdate(db,entity,securityStamp,newPassword);
        return update!=null&&update.ExecuteCommand()==1;
    }
    public async Task<bool> ChangePasswordIfCurrentAsync(string userId,string securityStamp,string newPassword)
    {
        using var db=_factory.Create();var entity=await db.Queryable<GatewayUserEntity>().InSingleAsync(userId);
        var update=PasswordUpdate(db,entity,securityStamp,newPassword);
        return update!=null&&await update.ExecuteCommandAsync()==1;
    }

    public bool IsSessionRevoked(string userId,string sessionId)
    {
        using var db = _factory.Create();
        return db.Queryable<GatewayRevokedSessionEntity>().Any(x => x.SessionId == sessionId && x.UserId == userId);
    }
    public async Task<bool> IsSessionRevokedAsync(string userId,string sessionId)
    {
        using var db = _factory.Create();
        return await db.Queryable<GatewayRevokedSessionEntity>().AnyAsync(x => x.SessionId == sessionId && x.UserId == userId);
    }
    public async Task RevokeSessionAsync(string userId,string sessionId,long expiresAt)
    {
        if (!Guid.TryParse(sessionId,out _) || string.IsNullOrEmpty(userId)) throw new ArgumentException("Invalid login session.");
        using var db = _factory.Create();
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await db.Deleteable<GatewayRevokedSessionEntity>().Where(x => x.ExpiresAt < now).ExecuteCommandAsync();
        if (await db.Queryable<GatewayRevokedSessionEntity>().AnyAsync(x => x.SessionId == sessionId)) return;
        try { await db.Insertable(new GatewayRevokedSessionEntity {UserId=userId,SessionId=sessionId,ExpiresAt=expiresAt}).ExecuteCommandAsync(); }
        catch { if (!await db.Queryable<GatewayRevokedSessionEntity>().AnyAsync(x => x.SessionId == sessionId && x.UserId == userId)) throw; }
    }

    private static DateTime ToLocalTime(DateTime? value)
    {
        if (!value.HasValue || value.Value == DateTime.MinValue)
            return DateTime.MinValue;
        return ToLocalTime(value.Value);
    }

    private static DateTime ToLocalTime(DateTime value)
    {
        if (value == DateTime.MinValue)
            return DateTime.MinValue;
        DateTime utc = value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return utc.ToLocalTime();
    }

    private static string NormalizeRole(string role, SqlSugar.ISqlSugarClient db)
    {
        string requested = string.IsNullOrWhiteSpace(role) ? "Viewer" : role.Trim();
        GatewayRoleEntity entity = db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == requested.ToLower())
            .First();
        if (entity == null)
            throw new ArgumentException("选择的角色不存在。", nameof(role));
        if (!entity.Enabled)
            throw new ArgumentException("选择的角色已停用。", nameof(role));
        return entity.Name;
    }

    private static async Task<string> NormalizeRoleAsync(string role, SqlSugar.ISqlSugarClient db)
    {
        string requested = string.IsNullOrWhiteSpace(role) ? "Viewer" : role.Trim();
        GatewayRoleEntity? entity = await db.Queryable<GatewayRoleEntity>()
            .Where(item => item.Name.ToLower() == requested.ToLower())
            .FirstAsync();
        if (entity == null)
            throw new ArgumentException("选择的角色不存在。", nameof(role));
        if (!entity.Enabled)
            throw new ArgumentException("选择的角色已停用。", nameof(role));
        return entity.Name;
    }

    private static void CreatePasswordHash(string password, out string salt, out string hash)
    {
        byte[] saltBytes = new byte[16];
        RandomNumberGenerator.Fill(saltBytes);
        salt = Convert.ToBase64String(saltBytes);
        hash = Convert.ToBase64String(DerivePasswordHash(password, saltBytes));
    }

    private static bool VerifyPassword(string password, string salt, string hash)
    {
        try
        {
            byte[] saltBytes = Convert.FromBase64String(salt ?? string.Empty);
            byte[] expected = Convert.FromBase64String(hash ?? string.Empty);
            if (saltBytes.Length == 0 || expected.Length != PasswordHashLength)
                return false;

            byte[] actual = DerivePasswordHash(password, saltBytes);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] DerivePasswordHash(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password ?? string.Empty,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            PasswordHashLength);
    }
}

public sealed class GatewayBootstrapUserOptions
{
    public GatewayBootstrapUserOptions()
    {
        AutoCreateAdmin = true;
        AdminUsername = "admin";
        AdminDisplayName = "System Administrator";
        AdminPassword = string.Empty;
    }

    public bool AutoCreateAdmin { get; set; }
    public string AdminUsername { get; set; }
    public string AdminDisplayName { get; set; }
    public string AdminPassword { get; set; }
}
