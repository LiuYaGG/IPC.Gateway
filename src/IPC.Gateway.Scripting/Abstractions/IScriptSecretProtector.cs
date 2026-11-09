namespace IPC.Gateway.Scripting.Abstractions;

/// <summary>
/// 定义数据库连接字符串的加密和解密边界。
/// </summary>
public interface IScriptSecretProtector
{
    /// <summary>
    /// 加密需要持久化的敏感文本。
    /// </summary>
    string Protect(string value);

    /// <summary>
    /// 解密运行时需要使用的敏感文本。
    /// </summary>
    string Unprotect(string value);
}
