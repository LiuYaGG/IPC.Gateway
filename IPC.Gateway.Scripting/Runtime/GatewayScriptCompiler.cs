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

    private readonly ConcurrentDictionary<string, ScriptRunner<object?>> _compiledScripts = new(StringComparer.Ordinal);
    private readonly ScriptOptions _scriptOptions;

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
        ScriptValidationResult result = new();
        string source = sourceCode?.Trim() ?? string.Empty;
        if (source.Length == 0)
        {
            result.Errors.Add("脚本内容不能为空。");
            return Task.FromResult(result);
        }

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: SourceCodeKind.Script), cancellationToken: cancellationToken);
        SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
        InspectSecurityBoundary(root, result);
        if (result.Errors.Count == 0)
        {
            Script<object?> script = CSharpScript.Create<object?>(source, _scriptOptions, typeof(GatewayScriptGlobals));
            foreach (Diagnostic diagnostic in script.Compile(cancellationToken))
            {
                string message = diagnostic.GetMessage(CultureInfo.GetCultureInfo("zh-CN"));
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                    result.Errors.Add(message);
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                    result.Warnings.Add(message);
            }
        }

        result.Errors = result.Errors.Distinct(StringComparer.Ordinal).ToList();
        result.Warnings = result.Warnings.Distinct(StringComparer.Ordinal).ToList();
        result.Success = result.Errors.Count == 0;
        return Task.FromResult(result);
    }

    /// <summary>
    /// 运行已校验脚本，并复用按源码哈希缓存的编译委托。
    /// </summary>
    public async Task<object?> RunAsync(
        string sourceCode,
        GatewayScriptGlobals globals,
        CancellationToken cancellationToken = default)
    {
        ScriptValidationResult validation = await ValidateAsync(sourceCode, cancellationToken).ConfigureAwait(false);
        if (!validation.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, validation.Errors));
        string cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceCode)));
        ScriptRunner<object?> runner = _compiledScripts.GetOrAdd(cacheKey, _ =>
            CSharpScript.Create<object?>(sourceCode, _scriptOptions, typeof(GatewayScriptGlobals)).CreateDelegate(cancellationToken));
        return await runner(globals, cancellationToken).ConfigureAwait(false);
    }

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
}
