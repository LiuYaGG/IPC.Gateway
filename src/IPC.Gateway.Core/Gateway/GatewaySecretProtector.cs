/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Core.Gateway
* 项目描述 ：
* 类 名 称 ：GatewaySecretProtector
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Core.Gateway
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
using System.Text;

namespace IPC.Gateway.Core.Gateway;

public sealed class GatewaySecretProtector
{
    public const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;
    private readonly bool _enabled;

    public GatewaySecretProtector(GatewaySecretProtectionOptions? options = null)
    {
        options ??= new GatewaySecretProtectionOptions();
        _enabled = options.Enabled;
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(ResolveMasterKey(options)));
    }

    public bool IsProtected(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public string Protect(string? value)
    {
        string text = value ?? string.Empty;
        if (!_enabled || string.IsNullOrEmpty(text) || IsProtected(text))
            return text;

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] plain = Encoding.UTF8.GetBytes(text);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];

        using AesGcm aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        byte[] payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string Unprotect(string? value)
    {
        string text = value ?? string.Empty;
        if (!_enabled || string.IsNullOrEmpty(text) || !IsProtected(text))
            return text;

        try
        {
            byte[] payload = Convert.FromBase64String(text.Substring(Prefix.Length));
            if (payload.Length < NonceSize + TagSize)
                return text;

            byte[] nonce = payload.AsSpan(0, NonceSize).ToArray();
            byte[] tag = payload.AsSpan(NonceSize, TagSize).ToArray();
            byte[] cipher = payload.AsSpan(NonceSize + TagSize).ToArray();
            byte[] plain = new byte[cipher.Length];

            using AesGcm aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return text;
        }
    }

    private static string ResolveMasterKey(GatewaySecretProtectionOptions options)
    {
        string configured = options.MasterKey?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string environmentName = string.IsNullOrWhiteSpace(options.EnvironmentVariableName)
            ? "IPC_GATEWAY_SECRET_KEY"
            : options.EnvironmentVariableName.Trim();
        string? environmentValue = Environment.GetEnvironmentVariable(environmentName);
        if (!string.IsNullOrWhiteSpace(environmentValue))
            return environmentValue.Trim();

        return string.Join("|", new[]
        {
            "ipc-gateway-local-secret",
            Environment.MachineName,
            Environment.UserName,
            AppContext.BaseDirectory
        });
    }
}
