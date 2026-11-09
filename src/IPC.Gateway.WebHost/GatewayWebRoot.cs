/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.WebHost
* 项目描述 ：
* 类 名 称 ：GatewayWebRoot
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.WebHost
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
namespace IPC.Gateway.WebHost;

public static class GatewayWebRoot
{
    public static string PrepareBeforeBuilder(string contentRootPath, string appBasePath)
    {
        foreach (string requiredPath in GetRequiredStartupPaths(contentRootPath, appBasePath))
        {
            Directory.CreateDirectory(requiredPath);
        }

        string webRoot = Resolve(contentRootPath, appBasePath);
        Directory.CreateDirectory(webRoot);
        return webRoot;
    }

    public static string Resolve(string contentRootPath, string appBasePath)
    {
        IReadOnlyList<string> candidates = GetCandidatePaths(contentRootPath, appBasePath);
        foreach (string candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "index.html")))
                return candidate;
        }

        return candidates[0];
    }

    internal static IReadOnlyList<string> GetRequiredStartupPaths(string contentRootPath, string appBasePath)
    {
        List<string> paths = new List<string>
        {
            Path.Combine(contentRootPath, "wwwroot"),
            Path.Combine(appBasePath, "wwwroot")
        };

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = new List<string>();
        foreach (string path in paths)
        {
            string fullPath = Path.GetFullPath(path);
            if (seen.Add(fullPath))
                normalized.Add(fullPath);
        }

        return normalized;
    }

    internal static IReadOnlyList<string> GetCandidatePaths(string contentRootPath, string appBasePath)
    {
        List<string> candidates = new List<string>
        {
            Path.Combine(contentRootPath, "wwwroot"),
            Path.Combine(appBasePath, "wwwroot"),
            Path.Combine(contentRootPath, "..", "IPC.Gateway.Web", "dist"),
            Path.Combine(appBasePath, "..", "..", "..", "..", "IPC.Gateway.Web", "dist")
        };

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> normalized = new List<string>();
        foreach (string candidate in candidates)
        {
            string fullPath = Path.GetFullPath(candidate);
            if (seen.Add(fullPath))
                normalized.Add(fullPath);
        }

        return normalized;
    }
}
