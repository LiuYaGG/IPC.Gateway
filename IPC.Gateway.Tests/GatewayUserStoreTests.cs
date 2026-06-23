/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Tests
* 项目描述 ：
* 类 名 称 ：GatewayUserStoreTests
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Tests
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
using IPC.Gateway.Core.Application.Users;
using IPC.Gateway.Core.Domain.Users;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Core.Infrastructure.Persistence;

namespace IPC.Gateway.Tests;

public sealed class GatewayUserStoreTests : IDisposable
{
    private readonly string _rootDirectory;
    private readonly string _databasePath;

    public GatewayUserStoreTests()
    {
        _rootDirectory = Path.Combine(Path.GetTempPath(), "ipc-gateway-user-tests", Guid.NewGuid().ToString("N"));
        _databasePath = Path.Combine(_rootDirectory, "gateway.db");
    }

    [Fact]
    public void UpsertUser_HashesPasswordAndValidatesEnabledUser()
    {
        GatewayUserStore store = CreateStore();

        var saved = store.UpsertUser("operator1", "Line Operator", "Operator", true, "Correct#12345");
        var stored = store.FindByUsername("operator1");
        var authenticated = store.ValidatePassword("operator1", "Correct#12345");

        Assert.Equal("operator1", saved.Username);
        Assert.Equal(string.Empty, saved.PasswordHash);
        Assert.Equal(string.Empty, saved.PasswordSalt);
        Assert.NotNull(stored);
        Assert.NotEqual("Correct#12345", stored.PasswordHash);
        Assert.False(string.IsNullOrWhiteSpace(stored.PasswordHash));
        Assert.False(string.IsNullOrWhiteSpace(stored.PasswordSalt));
        Assert.NotNull(authenticated);
        Assert.Equal("Operator", authenticated.Role);
        Assert.Equal(string.Empty, authenticated.PasswordHash);
        Assert.Equal(string.Empty, authenticated.PasswordSalt);
    }

    [Fact]
    public void ValidatePassword_RejectsWrongPasswordAndDisabledUser()
    {
        GatewayUserStore store = CreateStore();
        store.UpsertUser("viewer1", "Viewer", "Viewer", true, "Correct#12345");

        Assert.Null(store.ValidatePassword("viewer1", "Wrong#12345"));

        store.UpsertUser("viewer1", "Viewer", "Viewer", false, string.Empty);

        Assert.Null(store.ValidatePassword("viewer1", "Correct#12345"));
    }

    [Fact]
    public void UpsertUser_RejectsWeakPasswordWhenPolicyIsEnabled()
    {
        GatewayUserStore store = CreateStore();

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            store.UpsertUser("weakuser", "Weak User", "Viewer", true, "123456"));

        Assert.Contains("密码长度", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpsertUser_AllowsPasswordWithoutUppercaseByDefault()
    {
        GatewayUserStore store = CreateStore();

        store.UpsertUser("operator3", "Line Operator", "Operator", true, "lower#123");

        GatewayUserInfo? authenticated = store.ValidatePassword("operator3", "lower#123");
        Assert.NotNull(authenticated);
        Assert.Equal("operator3", authenticated.Username);
    }

    [Fact]
    public void ResetPassword_ChangesOnlyPassword()
    {
        GatewayUserStore store = CreateStore();
        GatewayUserApplicationService users = new GatewayUserApplicationService(store);
        store.UpsertUser("operator4", "Line Operator", "Operator", true, "lower#123");

        GatewayUserInfo saved = users.ResetPassword("operator4", "lower#456");
        GatewayUserInfo? oldPassword = store.ValidatePassword("operator4", "lower#123");
        GatewayUserInfo? newPassword = store.ValidatePassword("operator4", "lower#456");

        Assert.Equal("operator4", saved.Username);
        Assert.Equal("Line Operator", saved.DisplayName);
        Assert.Equal("Operator", saved.Role);
        Assert.True(saved.Enabled);
        Assert.Null(oldPassword);
        Assert.NotNull(newPassword);
        Assert.Equal("Operator", newPassword.Role);
    }

    [Fact]
    public void Authenticate_LocksAccountAfterConfiguredFailedAttempts()
    {
        GatewayAccountSecurityOptions security = new GatewayAccountSecurityOptions
        {
            Lockout = new GatewayAccountLockoutOptions
            {
                Enabled = true,
                MaxFailedAttempts = 2,
                LockoutMinutes = 10,
                ResetFailedCountOnSuccess = true
            }
        };
        GatewayUserStore store = CreateStore(CreateOptions(), security);
        store.UpsertUser("operator2", "Line Operator", "Operator", true, "Correct#12345");

        GatewayUserAuthenticationResult firstFailure = store.Authenticate("operator2", "Wrong#12345", security.Lockout);
        GatewayUserAuthenticationResult secondFailure = store.Authenticate("operator2", "Wrong#12345", security.Lockout);
        GatewayUserAuthenticationResult lockedResult = store.Authenticate("operator2", "Correct#12345", security.Lockout);
        GatewayUserInfo? stored = store.FindByUsername("operator2");

        Assert.False(firstFailure.Success);
        Assert.False(firstFailure.Locked);
        Assert.False(secondFailure.Success);
        Assert.True(secondFailure.Locked);
        Assert.False(lockedResult.Success);
        Assert.True(lockedResult.Locked);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.FailedLoginCount);
        Assert.True(stored.LockoutEndTime > DateTime.Now);
    }

    [Fact]
    public void UpsertUser_RejectsMissingOrDisabledRole()
    {
        GatewayDatabaseOptions options = CreateOptions();
        GatewayUserStore store = CreateStore(options);

        Assert.Throws<ArgumentException>(() =>
            store.UpsertUser("missingrole1", "Missing Role", "MissingRole", true, "Correct#12345"));

        GatewayRoleStore roles = new GatewayRoleStore(options);
        roles.UpsertRole("Maintenance", "Maintenance", string.Empty, false, Array.Empty<string>());

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            store.UpsertUser("maintenance1", "Maintenance", "Maintenance", true, "Correct#12345"));
        Assert.Contains("停用", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (!Directory.Exists(_rootDirectory))
            return;

        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }

    private GatewayUserStore CreateStore()
    {
        return CreateStore(CreateOptions());
    }

    private GatewayUserStore CreateStore(GatewayDatabaseOptions options)
    {
        return CreateStore(options, new GatewayAccountSecurityOptions());
    }

    private GatewayUserStore CreateStore(GatewayDatabaseOptions options, GatewayAccountSecurityOptions securityOptions)
    {
        return new GatewayUserStore(options, new GatewayBootstrapUserOptions
        {
            AutoCreateAdmin = false
        }, securityOptions);
    }

    private GatewayDatabaseOptions CreateOptions()
    {
        return new GatewayDatabaseOptions
        {
            Provider = "Sqlite",
            Database = _databasePath,
            AutoCreateDatabase = true
        };
    }
}
