using System.Diagnostics;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace IPC.Gateway.WebHost;

public interface IGatewayUpdateProcessLauncher
{
    bool IsSupported { get; }
    void Launch(string scriptPath, string actionId);
}

public sealed class GatewayUpdateProcessLauncher : IGatewayUpdateProcessLauncher
{
    public bool IsSupported => OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService();

    public void Launch(string scriptPath, string actionId)
    {
        if (!IsSupported) throw new InvalidOperationException("界面自动升级仅支持以 Windows 服务方式运行的网关。");
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new(powershell) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(scriptPath)! };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Automatic", "-ExpectedActionId", actionId })
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动独立升级进程。");
    }
}
