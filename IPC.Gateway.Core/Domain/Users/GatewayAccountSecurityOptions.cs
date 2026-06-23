/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：GatewayAccountSecurityOptions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Domain.Users
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
namespace IPC.Gateway.Core.Domain.Users;

public sealed class GatewayAccountSecurityOptions
{
    public GatewayAccountSecurityOptions()
    {
        Password = new GatewayPasswordPolicyOptions();
        Lockout = new GatewayAccountLockoutOptions();
    }

    public GatewayPasswordPolicyOptions Password { get; set; }
    public GatewayAccountLockoutOptions Lockout { get; set; }
}

public sealed class GatewayPasswordPolicyOptions
{
    public bool Enabled { get; set; } = true;
    public int MinLength { get; set; } = 8;
    public int MaxLength { get; set; } = 128;
    public bool RequireUppercase { get; set; } = false;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigit { get; set; } = true;
    public bool RequireSymbol { get; set; } = true;
    public bool RejectUsernameInPassword { get; set; } = true;
}

public sealed class GatewayAccountLockoutOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public bool ResetFailedCountOnSuccess { get; set; } = true;
}

public sealed class GatewayUserAuthenticationResult
{
    public bool Success { get; set; }
    public GatewayUserInfo? User { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public bool Locked { get; set; }
    public DateTime LockoutEndTime { get; set; }

    public static GatewayUserAuthenticationResult Ok(GatewayUserInfo user)
    {
        return new GatewayUserAuthenticationResult
        {
            Success = true,
            User = user
        };
    }

    public static GatewayUserAuthenticationResult Fail(string message)
    {
        return new GatewayUserAuthenticationResult
        {
            Success = false,
            ErrorMessage = message ?? string.Empty
        };
    }

    public static GatewayUserAuthenticationResult LockedOut(DateTime lockoutEndTime)
    {
        return new GatewayUserAuthenticationResult
        {
            Success = false,
            Locked = true,
            LockoutEndTime = lockoutEndTime,
            ErrorMessage = "账号已被锁定，请稍后再试。"
        };
    }
}
