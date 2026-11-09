using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;

namespace IPC.Gateway.WebHost;

/// <summary>
/// 将脚本类库的敏感信息保护接口适配到网关现有 AES-GCM 密钥体系。
/// </summary>
public sealed class GatewayScriptSecretProtector : IScriptSecretProtector
{
    private readonly GatewaySecretProtector _protector;

    /// <summary>
    /// 使用网关现有敏感信息参数创建脚本连接字符串保护器。
    /// </summary>
    public GatewayScriptSecretProtector(GatewaySecretProtectionOptions options)
    {
        _protector = new GatewaySecretProtector(options);
    }

    /// <summary>
    /// 加密脚本数据库连接字符串。
    /// </summary>
    public string Protect(string value)
    {
        return _protector.Protect(value);
    }

    /// <summary>
    /// 解密脚本运行时使用的数据库连接字符串。
    /// </summary>
    public string Unprotect(string value)
    {
        return _protector.Unprotect(value);
    }
}
