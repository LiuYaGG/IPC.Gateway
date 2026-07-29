using IPC.Gateway.Scripting.Runtime;
using IPC.Gateway.Scripting.Models;

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

    /// <summary>
    /// 验证数据库写入脚本不能调用点位写入 API。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_DatabaseScriptUsingWrites_ShouldBeRejected()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync(
            "await Writes.SetAsync(\"channel/device/group/tag\", 1);",
            GatewayScriptType.DatabaseWrite);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("不能调用点位写入", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证点位联动脚本不能调用数据库 API。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_TagLinkageScriptUsingDatabase_ShouldBeRejected()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync(
            "await Database.InsertAsync(\"target\", new { Value = 1 });",
            GatewayScriptType.TagLinkage);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("不能调用数据库写入", StringComparison.Ordinal));
    }

    /// <summary>
    /// 验证合法的点位联动脚本可以通过编译检查。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_TagLinkageScriptUsingWrites_ShouldSucceed()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync(
            "var value = Tags.ReadInt32(\"channel/device/group/source\"); await Writes.SetAsync(\"channel/device/group/target\", value); return value;",
            GatewayScriptType.TagLinkage);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    /// <summary>
    /// 验证值处理脚本可以使用原生数学函数处理输入。
    /// </summary>
    [Fact]
    public async Task ValidateAsync_ValueTransformUsingMath_ShouldSucceed()
    {
        var result = await new GatewayScriptCompiler().ValidateAsync(
            "return Math.Round(Math.Sin(Input.AsDouble()), 4);",
            GatewayScriptType.ValueTransform);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
    }

    /// <summary>
    /// 验证值处理脚本不能越权读取点位或写入数据库。
    /// </summary>
    [Theory]
    [InlineData("return Tags.ReadDouble(\"channel/device/group/tag\");")]
    [InlineData("return Database.InsertAsync(\"target\", new { Value = 1 });")]
    [InlineData("return Writes.SetAsync(\"channel/device/group/tag\", 1);")]
    public async Task ValidateAsync_ValueTransformUsingSideEffects_ShouldBeRejected(string sourceCode)
    {
        var result = await new GatewayScriptCompiler().ValidateAsync(sourceCode, GatewayScriptType.ValueTransform);

        Assert.False(result.Success);
    }
}
