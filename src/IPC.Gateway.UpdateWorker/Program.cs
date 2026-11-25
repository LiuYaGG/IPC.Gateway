using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Root only controls the fixed service. All mutable-file work runs as the gateway account.
if (!OperatingSystem.IsLinux() || args.Length < 1) return 2;
var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
Config c;
try
{
    TrustedRootPath(args[0]);
    if ((File.GetUnixFileMode(args[0]) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0) throw new IOException("Writable helper configuration.");
    c = JsonSerializer.Deserialize<Config>(File.ReadAllBytes(args[0]), jsonOptions) ?? throw new IOException("Missing configuration.");
    if (c.ServiceUser == "root") throw new IOException("Gateway must use an unprivileged service account.");
    TrustedRootPath(c.DotNetPath); TrustedRootPath(c.WorkerAssembly);
    foreach (var path in new[] { c.InstallDirectory, c.UpdateDirectory, c.HaStateDirectory }.Where(p => p.Length > 0))
    {
        if (!Path.IsPathFullyQualified(path) || path.TrimEnd('/') is "" or "/opt" or "/usr" or "/etc" or "/var" or "/var/lib" or "/home" or "/root") throw new IOException("Unsafe fixed directory.");
        RejectLinks(path);
    }
    if (!Regex.IsMatch(c.ServiceName, "^[a-zA-Z_][a-zA-Z0-9_-]*$") || !Regex.IsMatch(c.ServiceUser, "^[a-zA-Z_][a-zA-Z0-9_-]*$")) throw new IOException("Invalid service identity.");
    if (!Uri.TryCreate(c.HealthUrl, UriKind.Absolute, out var health) || health.Scheme != "http" || !health.IsLoopback) throw new IOException("Health check must use loopback HTTP.");
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
string receipt = Path.Combine(c.UpdateDirectory, "pending-action.json"), request = Path.Combine(c.UpdateDirectory, "worker-request.json");
string id = "";
bool stopped = false;
try
{
    if (args.Length > 1)
    {
        // Child mode is invoked through runuser, never with root privileges.
        if (Environment.UserName != c.ServiceUser || args.Length < 3) throw new IOException("File phase requires the service account.");
        id = args[2]; ValidateId(id);
        await FilePhase(args[1], args.Length > 3 ? args[3] : "");
        return 0;
    }
    RejectLinks(request);
    if (new FileInfo(request).Length > 4096) throw new IOException("Invalid worker request.");
    var trigger = JsonNode.Parse(File.ReadAllBytes(request));
    id = trigger?["actionId"]?.GetValue<string>() ?? ""; ValidateId(id);
    await Child("validate"); // Consumes request and validates using unprivileged file access.
    await Task.Delay(5000);
    string work = await Systemctl("show", c.ServiceName + ".service", "-p", "WorkingDirectory", "--value");
    if (work.TrimEnd('/') != c.InstallDirectory.TrimEnd('/')) throw new IOException("服务的安装目录不匹配，禁止执行更新。");
    await Child("stopping");
    await Child("authorize-stop");
    await Systemctl("stop", c.ServiceName + ".service"); stopped = true;
    if (await Systemctl("show", c.ServiceName + ".service", "-p", "MainPID", "--value") != "0") throw new IOException("网关进程尚未退出，禁止替换文件。");
    await Child("replace");
    await Systemctl("reset-failed", c.ServiceName + ".service");
    await Systemctl("start", c.ServiceName + ".service"); stopped = false;
    await Child("starting");
    await Child("wait-ready");
    await Child("complete"); return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    if (args.Length == 1) try { File.Delete(request); } catch { }
    if (args.Length == 1 && id.Length > 0)
        try { await Child("fail", ex.Message + (stopped ? " 服务保持停止，请人工检查后恢复。" : "")); } catch (Exception fail) { Console.Error.WriteLine(fail.Message); }
    return 1;
}

async Task Child(string phase, string message = "")
{
    // Fixed root-owned dotnet/worker paths from installation, no uploaded shell script.
    await Run("/usr/sbin/runuser", ["-u", c.ServiceUser, "--", c.DotNetPath, c.WorkerAssembly, args[0], phase, id, message]);
}
Task<string> Systemctl(params string[] arguments) => Run("/usr/bin/systemctl", ["--system", .. arguments]);
async Task<string> Run(string file, string[] arguments)
{
    var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (string a in arguments) start.ArgumentList.Add(a);
    using var p = Process.Start(start) ?? throw new IOException("Could not start trusted helper operation.");
    var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(c.TimeoutSeconds, 30, 86400)));
    await p.WaitForExitAsync(timeout.Token);
    string text = await output; Console.Write(text);
    if (p.ExitCode != 0) throw new IOException("更新辅助器操作失败：" + await error);
    return text.Trim();
}
async Task FilePhase(string phase, string failure)
{
    RejectLinks(receipt);
    var action = JsonNode.Parse(File.ReadAllBytes(receipt))?.AsObject() ?? throw new IOException("Missing pending action.");
    string log = Path.Combine(c.UpdateDirectory, "execution-" + id + ".log");
    action["executionLogPath"] = log;
    string Text(string key) => action[key]?.GetValue<string>() ?? "";
    if (Text("actionId") != id) throw new IOException("待执行动作已变化，请重新准备更新。");
    if (phase == "fail") { File.Delete(request); action["errorMessage"] = failure; action["status"] = "Failed"; File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + failure + "\n"); Save(); return; }
    if (action["preservePaths"] is JsonArray requested)
        foreach (var item in requested)
        {
            string name = item?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains('\\') || name is "." or ".." ||
                name.Equals("wwwroot", StringComparison.OrdinalIgnoreCase) || name.Equals("Drivers", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ipc-gateway-runtime.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
                throw new IOException("保留目录配置不能包含程序、驱动、前端或兼容描述文件。");
            c.PreservePaths = [.. c.PreservePaths, name];
        }
    if (phase == "authorize-stop")
    {
        string proofPath = Path.Combine(c.UpdateDirectory, "stop-authorization.json"); RejectLinks(proofPath);
        using var proof = JsonDocument.Parse(File.ReadAllBytes(proofPath));
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        using var body = new StringContent(JsonSerializer.Serialize(new { actionId = id, token = proof.RootElement.GetProperty("token").GetString() }), System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(new Uri(new Uri(c.HealthUrl), "/api/maintenance/updates/stop-admission"), body);
        using var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        if (!response.IsSuccessStatusCode || !result.RootElement.TryGetProperty("ready", out var ready) || ready.ValueKind != JsonValueKind.True)
            throw new IOException("停止服务前的高可用授权未通过，请重新核实集群并准备更新。");
        return;
    }
    if (phase == "wait-ready")
    {
        string proofPath = Path.Combine(c.UpdateDirectory, "worker-completion.json"); RejectLinks(proofPath);
        using var proof = JsonDocument.Parse(File.ReadAllBytes(proofPath));
        string? token = proof.RootElement.GetProperty("token").GetString();
        using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(c.TimeoutSeconds))
        {
            try
            {
                using var body = new StringContent(JsonSerializer.Serialize(new { actionId = id, token }), System.Text.Encoding.UTF8, "application/json");
                using var response = await http.PostAsync(new Uri(new Uri(c.HealthUrl), "/api/maintenance/updates/worker-ready"), body);
                using var result = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
                if (response.IsSuccessStatusCode && result.RootElement.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True) return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
            await Task.Delay(1000);
        }
        throw new IOException("网关已启动，但在期限内未确认目标程序和动作已恢复，请检查启动状态和服务日志。");
    }
    if (phase == "complete") { action["appliedTime"] = DateTime.UtcNow; Progress(100, "升级完成", "Applied"); return; }
    if (phase == "starting") { Progress(85, "正在等待网关初始化完成"); return; }
    if (phase == "stopping") { Progress(20, "正在停止网关服务"); return; }
    if (phase == "validate")
    {
        if (Text("status") != "Scheduled") throw new IOException("当前动作不处于等待执行状态。");
        File.Delete(request);
    }
    else if (phase != "replace" || Text("status") != "Applying") throw new IOException("升级执行阶段不匹配。");
    if (Path.GetFullPath(Text("targetDirectory")).TrimEnd('/') != c.InstallDirectory.TrimEnd('/')) throw new IOException("目标目录与安装登记不一致。");
    string source = Path.GetFullPath(Text("sourceDirectory")), operation = Text("actionType");
    string allowedRoot = Path.Combine(c.UpdateDirectory, operation == "Upgrade" ? "staging" : operation == "Rollback" ? "rollbacks" : throw new IOException("Invalid action."));
    if (!source.StartsWith(Path.GetFullPath(allowedRoot).TrimEnd('/') + '/', StringComparison.Ordinal)) throw new IOException("更新源目录超出允许的升级目录。");
    RejectLinks(source); CheckTree(source); VerifyCompatibility(source);
    if (phase == "validate") { Progress(10, "正在验证服务与更新文件"); return; }
    RejectLinks(c.InstallDirectory); CheckTree(c.InstallDirectory, true);
    if (operation == "Upgrade")
    {
        string backup = Path.GetFullPath(Text("rollbackDirectory"));
        if (!backup.StartsWith(Path.Combine(c.UpdateDirectory, "rollbacks") + '/', StringComparison.Ordinal)) throw new IOException("回滚目录超出允许的升级目录。");
        RejectLinks(backup); Progress(35, "正在保存停机后的回滚文件");
        using var rollback = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(backup, "rollback.json")));
        var expected = JsonNode.Parse(rollback.RootElement.GetProperty("compatibility").GetRawText());
        var installed = JsonNode.Parse(File.ReadAllBytes(Path.Combine(c.InstallDirectory, "ipc-gateway-runtime.json")));
        if (!JsonNode.DeepEquals(expected, installed)) throw new IOException("当前程序兼容信息已变化，请重新准备升级。");
        CopyProgram(c.InstallDirectory, Path.Combine(backup, "payload"));
    }
    Progress(55, "正在替换程序文件"); CopyProgram(source, c.InstallDirectory); Progress(75, "正在启动网关服务");
    await Task.CompletedTask;
    void Progress(int percent, string message, string status = "Applying")
    {
        action["status"] = status; action["progressPercent"] = percent; action["progressMessage"] = message; action["errorMessage"] = "";
        Save(); File.AppendAllText(log, DateTime.UtcNow.ToString("O") + $" {percent}% {message}\n"); Console.WriteLine($"{percent}% {message}");
    }
    void Save()
    {
        string temp = receipt + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, action.ToJsonString(jsonOptions)); File.Move(temp, receipt, true);
    }
}
void CopyProgram(string from, string to, bool preserveRoot = true)
{
    Directory.CreateDirectory(to); RejectLinks(to);
    foreach (var item in Directory.EnumerateFileSystemEntries(from))
    {
        string name = Path.GetFileName(item);
        if (preserveRoot && c.PreservePaths.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
        RejectLinks(item); string dest = Path.Combine(to, name); RejectLinks(dest);
        if (Directory.Exists(item)) CopyProgram(item, dest, false);
        else { File.Copy(item, dest, true); File.SetUnixFileMode(dest, File.GetUnixFileMode(item)); }
    }
    foreach (var item in Directory.EnumerateFileSystemEntries(to))
    {
        string name = Path.GetFileName(item);
        if (preserveRoot && c.PreservePaths.Contains(name, StringComparer.OrdinalIgnoreCase) || File.Exists(Path.Combine(from, name)) || Directory.Exists(Path.Combine(from, name))) continue;
        RejectLinks(item); if (Directory.Exists(item)) Directory.Delete(item, true); else File.Delete(item);
    }
}
void CheckTree(string directory, bool preserve = false)
{
    foreach (var item in Directory.EnumerateFileSystemEntries(directory))
    {
        if (preserve && c.PreservePaths.Contains(Path.GetFileName(item), StringComparer.OrdinalIgnoreCase)) continue;
        RejectLinks(item); if (Directory.Exists(item)) CheckTree(item);
    }
}
void VerifyCompatibility(string directory)
{
    foreach (string marker in new[] {
        Path.Combine(c.HaStateDirectory, "transport-operation.json"),
        Path.Combine(c.HaStateDirectory, "management.json"),
        Path.Combine(Path.GetDirectoryName(c.HaStateDirectory) ?? c.HaStateDirectory, "raft", "transport-migration.json") })
    {
        try { _ = File.GetAttributes(marker); }
        catch (FileNotFoundException) { continue; }
        catch (DirectoryNotFoundException) { continue; }
        RejectLinks(marker); using var pending = JsonDocument.Parse(File.ReadAllBytes(marker));
        foreach (var field in pending.RootElement.EnumerateObject())
            if (field.Name.Equals("TransportPlanId", StringComparison.OrdinalIgnoreCase) && field.Value.ValueKind == JsonValueKind.String ||
                field.Name.Equals("Phase", StringComparison.OrdinalIgnoreCase) && field.Value.GetString() is "Preparing" or "Prepared" or "Partial" or "Staged" or "Committed" or "PendingRestart")
                throw new IOException("组网迁移尚未完成，禁止更新程序。");
    }
    using var descriptor = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "ipc-gateway-runtime.json")));
    var d = descriptor.RootElement; int required = 2;
    foreach (string latch in new[] { Path.Combine(c.HaStateDirectory, "cluster-required"), Path.Combine(Path.GetDirectoryName(c.HaStateDirectory) ?? c.HaStateDirectory, "raft", "identity.json") })
    {
        if (c.HaStateDirectory.Length == 0) continue;
        try { _ = File.GetAttributes(latch); }
        catch (FileNotFoundException) { continue; }
        catch (DirectoryNotFoundException) { continue; }
        RejectLinks(latch); using var identity = JsonDocument.Parse(File.ReadAllBytes(latch));
        foreach (var field in identity.RootElement.EnumerateObject()) if (field.Name.Equals("ControlVersion", StringComparison.OrdinalIgnoreCase)) required = Math.Max(required, field.Value.GetInt32());
    }
    string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    if (!Version.TryParse(d.GetProperty("minimumGlibcVersion").GetString(), out var minimum) ||
        !Version.TryParse(System.Runtime.InteropServices.Marshal.PtrToStringAnsi(Native.GnuLibcVersion()), out var installed) || installed < minimum)
        throw new IOException("更新文件所需 glibc 高于当前系统，禁止执行。");
    if (d.GetProperty("operatingSystem").GetString() != "linux" || d.GetProperty("architecture").GetString() != arch || d.GetProperty("runtimeIdentifier").GetString() != "linux-" + arch ||
        d.GetProperty("targetFramework").GetString() != "net10.0" || d.GetProperty("requiredDotNetMajor").GetInt32() != 10 ||
        d.GetProperty("minimumHaStateVersion").GetInt32() > required || d.GetProperty("maximumHaStateVersion").GetInt32() < required)
        throw new IOException("更新文件与本机平台或高可用持久格式不兼容。");
}
static void ValidateId(string id) { if (!Regex.IsMatch(id, "^(upgrade|rollback)-[0-9]{14}-[a-f0-9]{8}$")) throw new IOException("更新动作号格式无效。"); }
static void TrustedRootPath(string path)
{
    path = Path.GetFullPath(path);
    FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    if (entry.LinkTarget != null) path = entry.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Invalid trusted path.");
    for (string? current = path; current != null; current = Path.GetDirectoryName(current))
    {
        using var stat = Process.Start(new ProcessStartInfo("/usr/bin/stat") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, ArgumentList = { "-Lc", "%u %a", "--", current } })!;
        string[] fields = stat.StandardOutput.ReadToEnd().Trim().Split(' '); stat.WaitForExit();
        if (stat.ExitCode != 0 || fields.Length != 2 || fields[0] != "0" || (Convert.ToInt32(fields[1], 8) & 18) != 0) throw new IOException("更新辅助器、运行时与配置必须由 root 拥有，且不可被组或其他用户写入。");
    }
}
static void RejectLinks(string path)
{
    for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
        if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("升级目录中不允许使用符号链接。");
}
sealed class Config
{
    public string InstallDirectory { get; set; } = "";
    public string UpdateDirectory { get; set; } = "";
    public string HaStateDirectory { get; set; } = "";
    public string ServiceName { get; set; } = "";
    public string ServiceUser { get; set; } = "";
    public string HealthUrl { get; set; } = "";
    public string WorkerAssembly { get; set; } = "";
    public string DotNetPath { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
    public string[] PreservePaths { get; set; } = ["Data", "logs", "appsettings.json", "appsettings.Production.json", "appsettings.Development.json"];
}
static class Native
{
    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "gnu_get_libc_version")]
    internal static extern IntPtr GnuLibcVersion();
}
