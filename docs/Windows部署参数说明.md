# Windows 部署参数说明

脚本位于 `script/windows`，统一使用中文名。

| 安装脚本参数 | 含义 |
| --- | --- |
| PublishDirectory | 发布后的程序目录，必须包含 IPC.Gateway.WebHost.dll |
| DataDirectory | 可选的持久化数据根目录 |
| ServiceName | 服务名，默认 IPCGateway |
| DisplayName | Windows 服务显示名称 |
| Url | HTTP 监听地址，默认 http://127.0.0.1:5184 |
| Environment | 额外的 KEY=value 环境变量；显式参数覆盖同名旧设置 |
| EnableServiceRecovery | 启用 Windows 服务失败恢复及看门狗宿主退出配合 |
| RestartDelaySeconds | 服务恢复等待秒数 |
| Start | 配置后立即启动 |
| WhatIf | 预览操作，不修改系统 |

卸载脚本支持 ServiceName、Force 和 WhatIf，保留程序与数据。
打包脚本支持 Version、PackageType、Configuration、RepositoryRoot、RuntimeIdentifier、OutputDirectory、BuildId、SigningPrivateKeyPath、Signer、SkipFrontendBuild 和 NoRestore。
省略版本时按日期和时间生成；明确平台时包名附加平台后缀；签名需要支持 RSA.ImportFromPem 的 PowerShell 运行时。

详见 [发布部署指南](发布部署指南.md)。
