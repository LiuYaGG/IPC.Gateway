using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Scripting.Abstractions;
using IPC.Gateway.Scripting.Models;
using IPC.Gateway.Scripting.Runtime;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证值处理脚本的类型转换、原生 C# 执行和固定版本行为。
/// </summary>
public sealed class ValueTransformScriptServiceTests
{
    /// <summary>
    /// 验证草稿测试可以执行三角函数并按声明类型输出。
    /// </summary>
    [Fact]
    public void Test_MathScript_ShouldReturnConvertedValue()
    {
        ValueTransformScriptService service = CreateService();

        ValueTransformExecutionResult result = service.Test(new ValueTransformScriptTestRequest
        {
            SourceCode = "return Math.Round(Math.Sin(Input.AsDouble()), 4);",
            InputDataType = "Double",
            OutputDataType = "Double",
            ValueText = "1.5707963267948966",
            TimeoutMilliseconds = 500
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(1D, Assert.IsType<double>(result.Value));
    }

    /// <summary>
    /// 验证同一脚本的旧发布版本和新发布版本可以被分别执行。
    /// </summary>
    [Fact]
    public void Execute_PinnedVersions_ShouldUseMatchingSourceCode()
    {
        ValueTransformScriptService service = CreateService();
        service.Reload(new ScriptConfigurationDocument
        {
            Scripts =
            [
                new GatewayScriptDefinition
                {
                    Id = "script-1",
                    Name = "版本测试",
                    ScriptType = GatewayScriptType.ValueTransform,
                    OutputDataType = "Double",
                    PublishedVersion = 2,
                    PublishedSourceCode = "return Input.AsDouble() + 2D;",
                    PublishedVersions =
                    [
                        new ValueTransformPublishedVersion
                        {
                            Version = 1,
                            SourceCode = "return Input.AsDouble() + 1D;",
                            PublishedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
                        },
                        new ValueTransformPublishedVersion
                        {
                            Version = 2,
                            SourceCode = "return Input.AsDouble() + 2D;",
                            PublishedUtc = DateTimeOffset.UtcNow
                        }
                    ]
                }
            ]
        });

        ValueTransformExecutionResult version1 = service.Execute(CreateRequest(1));
        ValueTransformExecutionResult version2 = service.Execute(CreateRequest(2));

        Assert.True(version1.Success, version1.ErrorMessage);
        Assert.True(version2.Success, version2.ErrorMessage);
        Assert.Equal(11D, Assert.IsType<double>(version1.Value));
        Assert.Equal(12D, Assert.IsType<double>(version2.Value));
    }

    /// <summary>
    /// 验证标签清洗未指定强制输出类型时采用脚本声明的输出类型。
    /// </summary>
    [Fact]
    public void Execute_TagCleaningDoubleToBool_ShouldKeepDeclaredBoolOutput()
    {
        ValueTransformScriptService service = CreateService();
        service.Reload(new ScriptConfigurationDocument
        {
            Scripts =
            [
                new GatewayScriptDefinition
                {
                    Id = "script-double-to-bool",
                    Name = "数值阈值转布尔",
                    ScriptType = GatewayScriptType.ValueTransform,
                    ValueTransformScope = ValueTransformScriptScope.TagCleaning,
                    InputDataType = "Double",
                    OutputDataType = "Bool",
                    PublishedVersion = 1,
                    PublishedSourceCode = "return Input.AsDouble() > 10D;"
                }
            ]
        });

        ValueTransformExecutionResult result = service.Execute(new ValueTransformExecutionRequest
        {
            ScriptId = "script-double-to-bool",
            ScriptVersion = 1,
            Value = 12.5D,
            ValueText = "12.5",
            DataType = "Double",
            Usage = "TagCleaning",
            TimeoutMilliseconds = 500
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(Assert.IsType<bool>(result.Value));
        Assert.Equal("Bool", result.OutputDataType);
        Assert.Equal("True", result.ValueText);
    }

    /// <summary>
    /// 创建不访问磁盘的值处理脚本服务。
    /// </summary>
    private static ValueTransformScriptService CreateService()
    {
        return new ValueTransformScriptService(new EmptyConfigurationStore(), new GatewayScriptCompiler());
    }

    /// <summary>
    /// 创建固定版本执行请求。
    /// </summary>
    private static ValueTransformExecutionRequest CreateRequest(int version)
    {
        return new ValueTransformExecutionRequest
        {
            ScriptId = "script-1",
            ScriptVersion = version,
            Value = 10D,
            ValueText = "10",
            DataType = "Double",
            ExpectedOutputDataType = "Double",
            Timestamp = DateTimeOffset.Now,
            TimeoutMilliseconds = 500
        };
    }

    /// <summary>
    /// 提供空脚本配置，测试通过显式 Reload 注入发布版本。
    /// </summary>
    private sealed class EmptyConfigurationStore : IScriptConfigurationStore
    {
        /// <summary>
        /// 返回空配置文档。
        /// </summary>
        public Task<ScriptConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ScriptConfigurationDocument());
        }

        /// <summary>
        /// 测试不需要持久化配置。
        /// </summary>
        public Task SaveAsync(ScriptConfigurationDocument document, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
