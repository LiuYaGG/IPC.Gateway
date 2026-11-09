/*----------------------------------------------------------------
* 项目名称 ：IPC.Gateway.Inference
* 项目描述 ：
* 类 名 称 ：OnnxInferenceServiceCollectionExtensions
* 类 描 述 ：
* 所在的域 ：
* 命名空间 ：IPC.Gateway.Inference
* 机器名称 ：UNKNOWN 
* CLR 版本 ：10.0.0
* 作    者 ：ipc
* 创建时间 ：2026-06-23 17:52:06
* 更新时间 ：2026-06-23 17:52:06
* 版 本 号 ：v1.0.0.0
*******************************************************************
* Copyright @ ipc 2026. All rights reserved.
*******************************************************************
//----------------------------------------------------------------*/
using IPC.Gateway.Core.Gateway;
using Microsoft.Extensions.DependencyInjection;

namespace IPC.Gateway.Inference;

public static class OnnxInferenceServiceCollectionExtensions
{
    public static IServiceCollection AddGatewayOnnxInference(
        this IServiceCollection services,
        OnnxModelCatalogOptions? options = null)
    {
        services.AddSingleton<IModelInferenceService, OnnxModelInferenceService>();
        services.AddSingleton(options ?? new OnnxModelCatalogOptions());
        services.AddSingleton<OnnxModelCatalogService>();
        return services;
    }
}
