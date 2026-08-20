using IPC.Gateway.Core.Application.Gateway.Contracts;
using IPC.Gateway.Core.Gateway;
using IPC.Gateway.Inference;
using IPC.Plc.Communication.Core;
using IPC.Runtime.Configuration;
using IPC.Runtime.Engine;
using IPC.Runtime.Values;

namespace IPC.Gateway.Tests;

/// <summary>
/// 验证模型目录基础持久化、虚拟标签配置映射和统一快照发布能力。
/// </summary>
public sealed class OnnxModelCenterTests
{
    /// <summary>
    /// 验证模型基础信息可以写入独立目录并在服务重建后恢复。
    /// </summary>
    [Fact]
    public void Catalog_SaveModel_ShouldPersistMetadata()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ipc-model-center-" + Guid.NewGuid().ToString("N"));
        try
        {
            OnnxModelCatalogOptions options = new() { Directory = directory };
            OnnxModelCatalogService first = new(options, new FakeInferenceService());
            OnnxModelDefinition saved = first.SaveModel(new SaveOnnxModelRequest
            {
                Name = "设备异常模型",
                Purpose = "DeviceAnomaly",
                Description = "测试目录持久化"
            });

            OnnxModelCatalogService second = new(options, new FakeInferenceService());
            OnnxModelDefinition restored = Assert.Single(second.GetModels());

            Assert.Equal(saved.Id, restored.Id);
            Assert.Equal("设备异常模型", restored.Name);
            Assert.Equal("DeviceAnomaly", restored.Purpose);
            Assert.Empty(restored.Versions);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 验证虚拟模型标签通过 DTO 往返后保留固定模型版本和输入绑定。
    /// </summary>
    [Fact]
    public void VirtualTag_ContractRoundTrip_ShouldPreserveModelBinding()
    {
        TagConfig source = new()
        {
            Name = "预测结果",
            DataType = PlcDataType.Double,
            IsVirtual = true,
            VirtualModel = new VirtualModelTagConfig
            {
                ModelId = "model-1",
                ModelVersion = 3,
                OutputName = "score",
                OutputIndex = 1,
                Inputs =
                [
                    new VirtualModelInputBindingConfig
                    {
                        FeatureName = "temperature",
                        TagPath = "channel/device/group/tag",
                        Multiplier = 0.1D,
                        Offset = 2D
                    }
                ]
            }
        };

        TagConfigurationDto dto = GatewayConfigurationContractMapper.ToDto(source, "SiemensS7");
        TagConfig restored = GatewayConfigurationContractMapper.ToConfig(dto);

        Assert.True(restored.IsVirtual);
        Assert.Equal("model-1", restored.VirtualModel.ModelId);
        Assert.Equal(3, restored.VirtualModel.ModelVersion);
        VirtualModelInputBindingConfig input = Assert.Single(restored.VirtualModel.Inputs);
        Assert.Equal("channel/device/group/tag", input.TagPath);
        Assert.Equal(0.1D, input.Multiplier);
    }

    /// <summary>
    /// 验证虚拟模型结果进入普通运行时快照存储而不需要设备驱动读取。
    /// </summary>
    [Fact]
    public void Runtime_PublishVirtualSnapshot_ShouldExposeSnapshot()
    {
        using RuntimeEngine runtime = new();
        runtime.Start(new ProjectConfig { Name = "Virtual Test" });

        runtime.PublishVirtualSnapshot(new TagValueSnapshot
        {
            ChannelId = "channel",
            DeviceId = "device",
            GroupId = "group",
            TagId = "virtual-tag",
            TagName = "预测结果",
            DataType = "Double",
            Value = 0.82D,
            ValueText = "0.82",
            RawValue = 0.82D,
            RawValueText = "0.82",
            Quality = TagQuality.Good,
            Timestamp = DateTime.Now
        });

        TagValueSnapshot snapshot = Assert.Single(runtime.GetSnapshots());
        Assert.Equal("virtual-tag", snapshot.TagId);
        Assert.Equal("0.82", snapshot.ValueText);
        Assert.Equal(TagQuality.Good, snapshot.Quality);
    }

    /// <summary>
    /// 验证只读虚拟标签可以被运行时读取，但不会被误判为设备只写标签。
    /// </summary>
    [Fact]
    public void Runtime_StartWithVirtualTag_ShouldNotMarkTagAsWriteOnly()
    {
        ProjectConfig project = new()
        {
            Name = "Virtual Read Test",
            Devices =
            [
                new DeviceConfig
                {
                    Id = "siemens-device",
                    ChannelId = "siemens-channel",
                    Name = "测试西门子PLC",
                    Enabled = true,
                    Protocol = PlcProtocol.SiemensS7,
                    Tags =
                    [
                        new TagConfig
                        {
                            Id = "onnx-tag",
                            Name = "ONNX预测结果",
                            Enabled = true,
                            IsVirtual = true,
                            AccessMode = TagAccessMode.ReadOnly,
                            DataType = PlcDataType.Double,
                            VirtualModel = new VirtualModelTagConfig()
                        }
                    ]
                }
            ]
        };

        using RuntimeEngine runtime = new();
        runtime.Start(project);

        TagValueSnapshot snapshot = Assert.Single(runtime.GetSnapshots());
        Assert.Equal("onnx-tag", snapshot.TagId);
        Assert.NotEqual(TagQuality.AccessDenied, snapshot.Quality);
        Assert.DoesNotContain("write-only", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提供模型目录测试所需的无外部依赖推理服务。
    /// </summary>
    private sealed class FakeInferenceService : IModelInferenceService
    {
        /// <summary>
        /// 返回固定成功分数。
        /// </summary>
        public ModelInferenceResult Predict(ModelInferenceRequest request) => new()
        {
            Success = true,
            Score = 0.5D,
            Outputs = [0.5D]
        };

        /// <summary>
        /// 测试服务没有需要释放的资源。
        /// </summary>
        public void Dispose()
        {
        }
    }
}
