using System;

namespace IPC.Gateway.Core.Gateway;

/// <summary>
/// 在数据库配置边界解析密码密文，避免密文被直接用于数据库连接。
/// </summary>
public static class GatewayDatabasePasswordResolver
{
    // 按项目要求内置对称密钥；更换密钥前必须先用旧密钥解密所有配置密文。
    private const string EncryptionKey = "IPC.Gateway.Database.Password.2026.FixedKey";

    public static string Decrypt(string? configuredPassword, bool passwordEncrypted)
    {
        string password = configuredPassword ?? string.Empty;
        if (!passwordEncrypted || string.IsNullOrEmpty(password))
            return password;

        GatewaySecretProtector protector = CreateProtector();
        if (!protector.IsProtected(password))
            throw new InvalidOperationException(
                "Gateway:Database:PasswordEncrypted 已启用，但 Password 不是 " + GatewaySecretProtector.Prefix + " 格式的密文。");

        string decrypted = protector.Unprotect(password);
        if (string.Equals(password, decrypted, StringComparison.Ordinal))
            throw new InvalidOperationException("数据库密码解密失败，请检查密文是否完整，以及代码中的加密密钥是否被修改。");

        return decrypted;
    }

    public static string Encrypt(string? plainText)
    {
        string password = plainText ?? string.Empty;
        if (string.IsNullOrEmpty(password))
            return string.Empty;

        GatewaySecretProtector protector = CreateProtector();
        if (protector.IsProtected(password))
            return password;

        string encrypted = protector.Protect(password);
        string verified = Decrypt(encrypted, true);
        if (!string.Equals(password, verified, StringComparison.Ordinal))
            throw new InvalidOperationException("数据库密码加密校验失败。");

        return encrypted;
    }

    private static GatewaySecretProtector CreateProtector()
    {
        // GatewaySecretProtector 使用 SHA-256 派生 256 位密钥，并执行 AES-GCM 加解密。
        return new GatewaySecretProtector(new GatewaySecretProtectionOptions
        {
            Enabled = true,
            MasterKey = EncryptionKey
        });
    }
}
