# IPC Gateway 商业驱动插件授权

## 授权边界

IPC Gateway 主程序、设备模板、标签批量导入导出、项目备份恢复及其他非插件功能均可免费使用，不因许可证缺失、过期或无效而停机。

`IPC.Gateway.LegacyProtocolPlugins` 是独立的商业驱动插件。插件在创建协议客户端前自行验证许可证；主程序不负责放行，也不能通过关闭 Web 中间件绕过插件验证。

未授权时：

- 主程序正常启动，免费功能和非商业驱动正常工作。
- 商业插件仍可被发现并显示配置界面，但创建连接时会失败，相关设备显示离线并返回明确的授权错误。
- 已运行的商业驱动最迟在重新创建连接或服务重启后重新校验。插件会缓存校验结果 5 秒。

## 安全模型

- Windows 使用 `MachineGuid` 与系统卷序列号生成机器码。
- Linux 使用 `machine-id` 和可用的 DMI 标识；容器部署应配置固定的机器标识策略。
- 许可证使用 RSA-3072、SHA-256 和 PSS 签名，并绑定产品、机器码、客户、版本和有效期。
- 网关与插件只部署公钥；签发私钥只保存在授权方电脑。
- 插件源代码不进入公共 Git 仓库，交付时仅发布插件二进制文件。

## 签发客户端

签发客户端位于主仓库之外：

`E:\IPC\IPC.Gateway.LicenseIssuerClient`

首次使用时初始化签发密钥，妥善备份加密私钥和密码。私钥不得部署到客户环境或提交到 Git。

## 客户申请与安装

1. 管理员登录 IPC Gateway。
2. 打开“安装升级”页面，在“商业驱动授权”卡片点击“复制申请码”。
3. 将申请码交给授权方。
4. 授权方在独立客户端中解析申请码并签发许可证。
5. 客户点击“导入许可证”，选择签发的 JSON 文件。

网关会在导入时验证签名、产品、机器码和有效期。插件在创建协议客户端时还会独立执行同等校验。

## 功能代码

新许可证建议填写：

- `commercial-drivers`：允许插件中的全部商业驱动。
- `driver:legacy.rockwell-cip`：仅允许指定驱动。
- `legacy.rockwell-cip`：也可直接填写驱动 ID。

为了兼容已经签发的许可证：有效许可证如果完全不包含 `commercial-drivers`、`driver:*` 或 `legacy.*` 项，则视为允许全部商业驱动。以后新签发的许可证应统一使用 `commercial-drivers` 或明确的驱动项。

设备数和点位数不再由主程序或插件限制，旧许可证中的 `MaxDevices`、`MaxTags` 字段仅为兼容保留。

## 生产配置

```json
{
  "Gateway": {
    "License": {
      "ProductId": "IPC.Gateway",
      "LicenseFile": "Data/License/ipc-gateway-license.json",
      "LicenseText": "",
      "TrustedPublicKeyPem": "",
      "TrustedPublicKeyFile": "Data/License/ipc-gateway-license-public.pem",
      "RequireValidLicense": true,
      "RequireMachineBinding": true,
      "MachineIdOverride": ""
    }
  }
}
```

插件默认读取应用目录下的：

- `Data/License/ipc-gateway-license.json`
- `Data/License/ipc-gateway-license-public.pem`

如需改变插件读取位置，可设置进程环境变量：

- `IPC_GATEWAY_COMMERCIAL_LICENSE_FILE`
- `IPC_GATEWAY_COMMERCIAL_PUBLIC_KEY_FILE`

`LicenseText` 非空时会覆盖文件许可证，但插件只读取文件。商业插件部署中应保持 `LicenseText` 为空，使用文件导入方式，以保证管理页面与插件读取同一份许可证。

## Git 与发布

`.gitignore` 已排除 `IPC.Gateway.LegacyProtocolPlugins/`。主解决方案不直接包含该项目；本机目录存在时，WebHost 构建和发布会自动编译并复制插件到 `Drivers/`，公共源码副本中没有该目录时仍可正常构建免费主程序。
