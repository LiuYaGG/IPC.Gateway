using IPC.Gateway.Scripting.Runtime;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证 C# 脚本编译器对可用 API 和受限能力的边界检查。
/// </summary>
public sealed class GatewayScriptCompilerTests
{
    /// <summary>
    /// 验证使用脚本全局对象的简单异步代码可以通过编译检查。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_SafeScript_ShouldSucceed()
    {
        GatewayScriptCompiler compiler = new();

        var result = await compiler.ValidateAsync("Log.Information(\"ok\"); await Task.CompletedTask; return UtcNow;");

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    /// <summary>
    /// 验证脚本不能通过 System.IO 访问本机文件。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_FileAccess_ShouldBeRejected()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync("return System.IO.File.ReadAllText(\"secret.txt\");");

        Assert.False(result.Success);
    }

    /// <summary>
    /// 验证脚本不能使用无结束条件的 for 循环长期占用运行线程。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_ConditionlessFor_ShouldBeRejected()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync("for (;;) { }");

        Assert.False(result.Success);
    }

    /// <summary>
    /// 验证脚本不能通过预处理指令加载外部程序集。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_ReferenceDirective_ShouldBeRejected()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync("#r \"external.dll\"\nreturn 1;");

        Assert.False(result.Success);
    }
}
