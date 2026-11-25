using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace IPC.Gateway.WebHost;

public interface IGatewayUpdateProcessLauncher
{
    bool IsSupported { get; }
    string SupportMessage => IsSupported ? "" : "当前部署未配置独立升级执行器。";
    void Launch(string scriptPath, string actionId);
}

public sealed class GatewayUpdateProcessLauncher(GatewayUpdateMaintenanceOptions? options = null) : IGatewayUpdateProcessLauncher
{
    public bool IsSupported => OperatingSystem.IsWindows() ? WindowsServiceHelpers.IsWindowsService() : ReadLinuxConfiguration() != null;
    public string SupportMessage => IsSupported ? "" : OperatingSystem.IsLinux()
        ? "Linux 网页升级执行器尚未安装，或当前运行在容器中。请使用新版 Linux 安装脚本安装独立 systemd 更新辅助器；Docker 请更新镜像。"
        : "界面自动升级要求 Windows 服务或已安装独立更新辅助器的 Linux systemd 服务。";

    private LinuxUpdateConfiguration? ReadLinuxConfiguration()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true" ||
            !Directory.Exists("/run/systemd/system") || string.IsNullOrWhiteSpace(options?.LinuxWorkerConfiguration)) return null;
        try
        {
            string path = Path.GetFullPath(options.LinuxWorkerConfiguration);
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0) return null;
            var c = System.Text.Json.JsonSerializer.Deserialize<LinuxUpdateConfiguration>(File.ReadAllBytes(path), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (c == null || Path.GetFullPath(c.InstallDirectory) != Path.GetFullPath(options.InstallDirectory.Length > 0 ? options.InstallDirectory : AppContext.BaseDirectory).TrimEnd('/') ||
                Path.GetFullPath(c.UpdateDirectory) != Path.GetFullPath(options.UpdateDirectory, options.InstallDirectory.Length > 0 ? Path.GetFullPath(options.InstallDirectory) : AppContext.BaseDirectory).TrimEnd('/') ||
                !File.Exists(c.WorkerAssembly) || !File.Exists(c.PathUnit) ||
                !System.Text.RegularExpressions.Regex.IsMatch(c.ServiceName, "^[a-zA-Z_][a-zA-Z0-9_-]*$")) return null;
            using var watcher = Process.Start(new ProcessStartInfo("/usr/bin/systemctl")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "--system", "is-active", "--quiet", c.ServiceName + "-update.path" }
            });
            if (watcher == null || !watcher.WaitForExit(2000)) { try { watcher?.Kill(); } catch { } return null; }
            if (watcher.ExitCode != 0) return null;
            return c;
        }
        catch { return null; }
    }

    public void Launch(string scriptPath, string actionId)
    {
        if (!IsSupported) throw new InvalidOperationException(SupportMessage);
        if (OperatingSystem.IsLinux())
        {
            var c = ReadLinuxConfiguration() ?? throw new InvalidOperationException(SupportMessage);
            string request = Path.Combine(c.UpdateDirectory, "worker-request.json");
            string authorization = Path.Combine(c.UpdateDirectory, "stop-authorization.json");
            string proofTemporary = authorization + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string temporary = request + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var proofStream = new FileStream(proofTemporary, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite }))
                    System.Text.Json.JsonSerializer.Serialize(proofStream, new { actionId, token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), processId = Environment.ProcessId, expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2) });
                File.Move(proofTemporary, authorization, overwrite: true);
                File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(new { actionId }));
                File.Move(temporary, request, overwrite: false);
            }
            finally { File.Delete(temporary); File.Delete(proofTemporary); }
            return;
        }
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(powershell) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(scriptPath)! };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Automatic", "-ExpectedActionId", actionId })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立升级进程。");
    }
}

internal sealed class LinuxUpdateConfiguration
{
    public string ServiceName { get; set; } = "";
    public string InstallDirectory { get; set; } = "";
    public string UpdateDirectory { get; set; } = "";
    public string WorkerAssembly { get; set; } = "";
    public string PathUnit { get; set; } = "";
}
