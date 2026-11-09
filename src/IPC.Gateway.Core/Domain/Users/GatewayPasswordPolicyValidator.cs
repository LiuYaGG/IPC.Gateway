/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Domain.Users
* 项目描述 ：
* 类 名 称 ：GatewayPasswordPolicyValidator
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

public static class GatewayPasswordPolicyValidator
{
    public static void Validate(string username, string password, GatewayPasswordPolicyOptions? options)
    {
        options ??= new GatewayPasswordPolicyOptions();
        if (!options.Enabled)
            return;

        string value = password ?? string.Empty;
        int minLength = Clamp(options.MinLength, 1, 256);
        int maxLength = Clamp(options.MaxLength, minLength, 512);

        if (value.Length < minLength)
            throw new ArgumentException($"密码长度不能少于 {minLength} 位。", nameof(password));
        if (value.Length > maxLength)
            throw new ArgumentException($"密码长度不能超过 {maxLength} 位。", nameof(password));
        if (options.RequireUppercase && !value.Any(char.IsUpper))
            throw new ArgumentException("密码必须包含至少一个大写字母。", nameof(password));
        if (options.RequireLowercase && !value.Any(char.IsLower))
            throw new ArgumentException("密码必须包含至少一个小写字母。", nameof(password));
        if (options.RequireDigit && !value.Any(char.IsDigit))
            throw new ArgumentException("密码必须包含至少一个数字。", nameof(password));
        if (options.RequireSymbol && !value.Any(IsSymbol))
            throw new ArgumentException("密码必须包含至少一个特殊字符。", nameof(password));
        if (options.RejectUsernameInPassword && !string.IsNullOrWhiteSpace(username) &&
            value.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("密码不能包含账号名。", nameof(password));
    }

    private static bool IsSymbol(char value)
    {
        return !char.IsLetterOrDigit(value) && !char.IsWhiteSpace(value);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
            return min;
        if (value > max)
            return max;
        return value;
    }
}
