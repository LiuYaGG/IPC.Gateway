using System.Runtime.InteropServices;
using System.Text.Json;

namespace IPC.Gateway.WebHost;

public sealed class GatewayPackageCompatibility
{
    public string OperatingSystem { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string RuntimeIdentifier { get; set; } = "";
    public string TargetFramework { get; set; } = "net10.0";
    public int RequiredDotNetMajor { get; set; } = 10;
    public int MinimumHaStateVersion { get; set; } = 2;
    public int MaximumHaStateVersion { get; set; } = 3;
    public string MinimumGlibcVersion { get; set; } = "";
}

public sealed partial class GatewayUpdatePackageService
{
    internal const string RuntimeDescriptor = "ipc-gateway-runtime.json";
    internal static GatewayPackageCompatibility CurrentCompatibility() => new()
    {
        OperatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported",
        Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
        MinimumGlibcVersion = OperatingSystem.IsLinux() ? Marshal.PtrToStringAnsi(GnuLibcVersion()) ?? "" : ""
    };

    private int RequiredHaStateVersion()
    {
        if (string.IsNullOrWhiteSpace(_options.HaStateDirectory)) return 2;
        string root = Path.GetFullPath(_options.HaStateDirectory, AppContext.BaseDirectory);
        // Fail closed if an enrolled identity cannot be inspected. Never delete/rebuild it.
        int required = 2;
        foreach (string identity in new[] { Path.Combine(root, "cluster-required"), Path.Combine(Path.GetDirectoryName(root) ?? root, "raft", "identity.json") })
        {
            try { _ = File.GetAttributes(identity); }
            catch (FileNotFoundException) { continue; }
            catch (DirectoryNotFoundException) { continue; }
            using var json = JsonDocument.Parse(File.ReadAllBytes(identity));
            foreach (var field in json.RootElement.EnumerateObject())
                if (field.Name.Equals("ControlVersion", StringComparison.OrdinalIgnoreCase)) required = Math.Max(required, field.Value.GetInt32());
        }
        return required;
    }

    private void ValidateCompatibility(GatewayPackageCompatibility? target)
    {
        if (target == null)
            throw new InvalidOperationException("升级包或回滚点缺少平台与高可用兼容信息，请使用新版脚本重新打包；旧包不可直接执行。");
        var current = CurrentCompatibility();
        if (target.OperatingSystem != current.OperatingSystem || target.Architecture != current.Architecture)
            throw new InvalidOperationException($"安装包平台不匹配：当前 {current.OperatingSystem}/{current.Architecture}，包为 {target.OperatingSystem}/{target.Architecture}。");
        if (target.TargetFramework != "net10.0" || target.RequiredDotNetMajor != 10 || Environment.Version.Major < target.RequiredDotNetMajor)
            throw new InvalidOperationException("安装包所需 .NET 运行时与当前服务不兼容。");
        string expectedRid = (target.OperatingSystem == "windows" ? "win" : "linux") + "-" + target.Architecture;
        if (target.RuntimeIdentifier != expectedRid)
            throw new InvalidOperationException("安装包运行时标识不匹配，请选择明确指定目标平台的安装包。");
        if (OperatingSystem.IsLinux() && (!Version.TryParse(target.MinimumGlibcVersion, out var minimum) ||
            !Version.TryParse(Marshal.PtrToStringAnsi(GnuLibcVersion()), out var installed) || installed < minimum))
            throw new InvalidOperationException("安装包所需 glibc 高于当前系统，或缺少经检测的 glibc 要求，请使用匹配统信系统的安装包。");
        int required = RequiredHaStateVersion();
        if (target.MinimumHaStateVersion < 2 || target.MaximumHaStateVersion < target.MinimumHaStateVersion ||
            required < target.MinimumHaStateVersion || required > target.MaximumHaStateVersion)
            throw new InvalidOperationException($"安装包不能读取当前高可用持久格式 {required}，禁止升级或回滚。请恢复相互匹配的程序、配置和持久状态备份。");
    }

    [DllImport("libc", EntryPoint = "gnu_get_libc_version")]
    private static extern IntPtr GnuLibcVersion();

    private void ValidateExtractedCompatibility(string directory, GatewayPackageCompatibility? expected)
    {
        ValidateCompatibility(expected);
        string descriptor = Path.Combine(directory, RuntimeDescriptor);
        if (!File.Exists(descriptor)) throw new InvalidOperationException("程序缺少平台兼容描述文件，请重新打包。");
        var actual = JsonSerializer.Deserialize<GatewayPackageCompatibility>(File.ReadAllBytes(descriptor), _jsonOptions);
        if (JsonSerializer.Serialize(actual, _jsonOptions) != JsonSerializer.Serialize(expected, _jsonOptions))
            throw new InvalidOperationException("程序与安装包声明的平台兼容信息不一致。");
        string runtimeFile = Path.Combine(directory, "IPC.Gateway.WebHost.runtimeconfig.json");
        using var runtime = JsonDocument.Parse(File.ReadAllBytes(runtimeFile));
        var runtimeOptions = runtime.RootElement.GetProperty("runtimeOptions");
        if (runtimeOptions.GetProperty("tfm").GetString() != expected!.TargetFramework)
            throw new InvalidOperationException("程序实际目标运行时与安装包声明不一致。");
        var frameworks = runtimeOptions.TryGetProperty("frameworks", out var list) ? list.EnumerateArray().ToArray() :
            runtimeOptions.TryGetProperty("framework", out var single) ? [single] : Array.Empty<JsonElement>();
        if (frameworks.Any(f => !Version.TryParse(f.GetProperty("version").GetString(), out var required) || required.Major != 10 || Environment.Version < required))
            throw new InvalidOperationException("程序需要比当前服务更新的 .NET 运行时，请先更新运行时。");
    }

    private string WriteLinuxOfflineInstructions()
    {
        string path = Path.Combine(ResolveUpdateDirectory(), "Linux升级说明.txt");
        File.WriteAllText(path, "请通过安装升级界面执行已准备动作。独立 systemd 更新辅助器由新版 Linux 安装脚本安装。不要以 root 执行由网页或服务账号生成的脚本。\n当前辅助器状态：" + _processLauncher.SupportMessage);
        return path;
    }
}
