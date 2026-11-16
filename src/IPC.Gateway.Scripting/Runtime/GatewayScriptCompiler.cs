using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IPC.Gateway.Scripting.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace IPC.Gateway.Scripting.Runtime;

/// <summary>
/// 使用 Roslyn 校验、编译和缓存受信任管理员编写的 C# 脚本。
/// </summary>
public sealed class GatewayScriptCompiler
{
    private static readonly string[] ForbiddenQualifiedPrefixes =
    [
        "System.IO",
        "System.Diagnostics",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "System.Net",
        "Microsoft.Win32"
    ];

    private static readonly HashSet<string> ForbiddenIdentifiers = new(StringComparer.Ordinal)
    {
        "File", "FileInfo", "Directory", "DirectoryInfo", "Process", "ProcessStartInfo",
        "Assembly", "Activator", "AppDomain", "Environment", "Marshal", "Registry",
        "HttpClient", "WebClient", "Socket", "TcpClient", "UdpClient", "Thread", "GC"
    };

    private sealed record PreparedScript(ScriptValidationResult Validation, ScriptRunner<object?>? Runner);

    // Lazy coalesces concurrent preparations of the same source into one validation/compilation.
    private readonly ConcurrentDictionary<string, Lazy<PreparedScript>> _preparedScripts = new(StringComparer.Ordinal);
    private readonly ScriptOptions _scriptOptions;
    private int _compilationCount;
    private long _cacheHitCount;

    public int CompilationCount => Volatile.Read(ref _compilationCount);
    public long CacheHitCount => Interlocked.Read(ref _cacheHitCount);

    /// <summary>
    /// 创建带固定引用和命名空间导入的脚本编译器。
    /// </summary>
    public GatewayScriptCompiler()
    {
        _scriptOptions = ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Dictionary<,>).Assembly,
                typeof(Task).Assembly,
                typeof(GatewayScriptGlobals).Assembly)
            .AddImports(
                "System",
                "System.Collections.Generic",
                "System.Globalization",
                "System.Linq",
                "System.Threading",
                "System.Threading.Tasks",
                "IPC.Gateway.Scripting.Models",
                "IPC.Gateway.Scripting.Runtime");
    }

    /// <summary>
    /// 执行语法安全检查和 Roslyn 编译检查。
    /// </summary>
    public Task<ScriptValidationResult> ValidateAsync(string sourceCode, CancellationToken cancellationToken = default)
    {
        return ValidateAsync(sourceCode, GatewayScriptType.DatabaseWrite, cancellationToken);
    }

    /// <summary>
    /// 按脚本业务类型执行能力边界检查和 Roslyn 编译检查。
    /// </summary>
    public Task<ScriptValidationResult> ValidateAsync(
        string sourceCode,
        GatewayScriptType scriptType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreparedScript prepared = GetPreparedScript(sourceCode, scriptType);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CloneValidation(prepared.Validation));
    }

    private PreparedScript CompileScript(string source, GatewayScriptType scriptType)
    {
        ScriptValidationResult result = new();
        if (source.Length == 0)
        {
            result.Errors.Add("脚本内容不能为空。");
            return new PreparedScript(result, null);
        }

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: SourceCodeKind.Script));
        SyntaxNode root = syntaxTree.GetRoot();
        InspectSecurityBoundary(root, result);
        InspectScriptTypeBoundary(root, scriptType, result);
        ScriptRunner<object?>? runner = null;
        if (result.Errors.Count == 0)
        {
            Interlocked.Increment(ref _compilationCount);
            Script<object?> script = CSharpScript.Create<object?>(
                source,
                _scriptOptions,
                ResolveGlobalsType(scriptType));
            foreach (Diagnostic diagnostic in script.Compile())
            {
                string message = diagnostic.GetMessage(CultureInfo.GetCultureInfo("zh-CN"));
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                    result.Errors.Add(message);
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    result.Warnings.Add(message);
            }
            // Reuse this Script's compilation rather than creating a second Roslyn Script.
            if (result.Errors.Count == 0)
                runner = script.CreateDelegate();
        }

        result.Errors = result.Errors.Distinct(StringComparer.Ordinal).ToList();
        result.Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToList();
        result.Success = result.Errors.Count == 0;
        return new PreparedScript(result, runner);
    }

    /// <summary>
    /// 运行已校验脚本，并复用按源码哈希缓存的编译委托。
    /// </summary>
    public async Task<object?> RunAsync(
        string sourceCode,
        GatewayScriptGlobals globals,
        CancellationToken cancellationToken = default)
    {
        return await RunAsync(sourceCode, GatewayScriptType.DatabaseWrite, globals, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 按脚本业务类型运行已经通过能力边界校验的脚本。
    /// </summary>
    public async Task<object?> RunAsync(
        string sourceCode,
        GatewayScriptType scriptType,
        GatewayScriptGlobals globals,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreparedScript prepared = GetPreparedScript(sourceCode, scriptType);
        if (!prepared.Validation.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, prepared.Validation.Errors));
        return await prepared.Runner!(globals, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行使用纯计算全局上下文的值处理脚本。
    /// </summary>
    public async Task<object?> RunValueTransformAsync(
        string sourceCode,
        ValueTransformScriptGlobals globals,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ScriptRunner<object?> runner = GetValueTransformRunner(sourceCode);
        return await runner(globals, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在脚本发布或配置加载阶段完成安全检查和 Roslyn 编译，避免占用实时执行超时。
    /// </summary>
    public ScriptValidationResult PrepareValueTransform(string sourceCode)
    {
        return CloneValidation(GetPreparedScript(sourceCode, GatewayScriptType.ValueTransform).Validation);
    }

    internal ScriptRunner<object?> GetValueTransformRunner(string sourceCode)
    {
        PreparedScript prepared = GetPreparedScript(sourceCode, GatewayScriptType.ValueTransform);
        if (!prepared.Validation.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, prepared.Validation.Errors));
        return prepared.Runner!;
    }

    private PreparedScript GetPreparedScript(string sourceCode, GatewayScriptType scriptType)
    {
        string source = sourceCode?.Trim() ?? string.Empty;
        string cacheKey = BuildCacheKey(source, scriptType);
        if (_preparedScripts.TryGetValue(cacheKey, out var prepared)) Interlocked.Increment(ref _cacheHitCount);
        else
        {
            var candidate = new Lazy<PreparedScript>(() => CompileScript(source, scriptType), LazyThreadSafetyMode.ExecutionAndPublication);
            prepared = _preparedScripts.GetOrAdd(cacheKey, candidate);
            if (!ReferenceEquals(candidate, prepared)) Interlocked.Increment(ref _cacheHitCount);
        }
        try { return prepared.Value; }
        catch
        {
            // Unexpected compiler/load failures can be retried, without evicting another caller's entry.
            ((ICollection<KeyValuePair<string, Lazy<PreparedScript>>>)_preparedScripts).Remove(new(cacheKey, prepared));
            throw;
        }
    }

    private static ScriptValidationResult CloneValidation(ScriptValidationResult result) => new()
    {
        Success = result.Success,
        Errors = [.. result.Errors],
        Warnings = [.. result.Warnings]
    };

    /// <summary>
    /// 检查脚本是否尝试访问文件、进程、网络、反射或不受控并发能力。
    /// </summary>
    private static void InspectSecurityBoundary(SyntaxNode root, ScriptValidationResult result)
    {
        if (root.DescendantTrivia(descendIntoTrivia: true).Any(trivia =>
                trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia) || trivia.IsKind(SyntaxKind.LoadDirectiveTrivia)))
            result.Errors.Add("脚本禁止使用 #r 或 #load 加载外部程序集和文件。");

        if (root.DescendantNodes().Any(node => node is UnsafeStatementSyntax))
            result.Errors.Add("脚本禁止使用 unsafe 代码。");
        if (root.DescendantNodes().Any(node => node is LockStatementSyntax))
            result.Errors.Add("脚本禁止使用 lock 创建进程内锁。");
        if (root.DescendantNodes().Any(node => node is WhileStatementSyntax or DoStatementSyntax))
            result.Errors.Add("第一版脚本禁止使用 while 或 do 循环，以降低不可终止循环风险。");
        if (root.DescendantNodes().OfType<ForStatementSyntax>().Any(statement => statement.Condition is null))
            result.Errors.Add("脚本禁止使用缺少结束条件的 for 循环。");

        foreach (UsingDirectiveSyntax usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            string name = usingDirective.Name?.ToString() ?? string.Empty;
            if (ForbiddenQualifiedPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
                result.Errors.Add($"脚本禁止导入命名空间 {name}。");
        }

        foreach (SyntaxNode node in root.DescendantNodes())
        {
            string text = node switch
            {
                QualifiedNameSyntax qualified => qualified.ToString(),
                AliasQualifiedNameSyntax alias => alias.ToString(),
                MemberAccessExpressionSyntax member => member.ToString(),
                _ => string.Empty
            };
            if (text.Length > 0 && ForbiddenQualifiedPrefixes.Any(prefix => text.Contains(prefix, StringComparison.Ordinal)))
                result.Errors.Add($"脚本禁止访问受限 API {text}。");
        }

        foreach (IdentifierNameSyntax identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (ForbiddenIdentifiers.Contains(identifier.Identifier.ValueText))
                result.Errors.Add($"脚本禁止使用受限类型或对象 {identifier.Identifier.ValueText}。");
        }
    }

    /// <summary>
    /// 限制不同业务类型脚本可调用的数据库和点位写入对象。
    /// </summary>
    private static void InspectScriptTypeBoundary(
        SyntaxNode root,
        GatewayScriptType scriptType,
        ScriptValidationResult result)
    {
        HashSet<string> identifiers = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(identifier => identifier.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);

        if (scriptType == GatewayScriptType.DatabaseWrite)
        {
            if (identifiers.Contains("Writes"))
                result.Errors.Add("数据库写入脚本不能调用点位写入 API Writes。");
            if (root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Any(member =>
                    member.Expression is IdentifierNameSyntax identifier &&
                    identifier.Identifier.ValueText == "Tags" &&
                    member.Name.Identifier.ValueText == "WriteAsync"))
            {
                result.Errors.Add("数据库写入脚本不能通过 Tags.WriteAsync 写入点位。");
            }
        }

        if (scriptType == GatewayScriptType.TagLinkage && identifiers.Contains("Database"))
            result.Errors.Add("点位联动脚本不能调用数据库写入 API Database。");

        if (scriptType == GatewayScriptType.ValueTransform)
        {
            foreach (string forbidden in new[] { "Tags", "Writes", "Database", "Trigger", "Task", "Thread" })
            {
                if (identifiers.Contains(forbidden))
                    result.Errors.Add($"值处理脚本不能使用 {forbidden}，只能执行当前输入值的纯计算。");
            }

            if (root.DescendantNodes().Any(node => node is AwaitExpressionSyntax))
                result.Errors.Add("值处理脚本不能使用 await 或启动异步任务。");
            if (root.DescendantNodes().Any(node => node is ForStatementSyntax or ForEachStatementSyntax))
                result.Errors.Add("值处理脚本不能使用 for 或 foreach；集合计算请使用受控 LINQ 表达式。");
            if (root.DescendantNodes().Any(node => node is LocalFunctionStatementSyntax))
                result.Errors.Add("值处理脚本不能声明本地函数，以避免递归导致不可终止执行。");
        }
    }

    /// <summary>
    /// 根据脚本业务类型选择对应的受控全局上下文。
    /// </summary>
    private static Type ResolveGlobalsType(GatewayScriptType scriptType)
    {
        return scriptType == GatewayScriptType.ValueTransform
            ? typeof(ValueTransformScriptGlobals)
            : typeof(GatewayScriptGlobals);
    }

    /// <summary>
    /// 使用脚本类型与源码哈希构造编译缓存键。
    /// </summary>
    private static string BuildCacheKey(string sourceCode, GatewayScriptType scriptType)
    {
        return scriptType + ":" + ResolveGlobalsType(scriptType).FullName + ":" +
               Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCode)));
    }
}
