# Windows 服务发布与更新指南

## 首次发布

1. 打包机准备 .NET 10 SDK、Node.js 与 npm 依赖，在 VS Code 打开整个仓库。
2. 在 `src/IPC.Gateway.Web` 安装前端依赖；执行 `script/windows/生成安装升级包.ps1`，选择 Install、win-x64。
3. 在 `artifacts/packages` 找到 ZIP，解压到临时目录。
4. 将 `payload` 内容复制到 `E:\IPCGateway\app` 等正式目录，检查主程序 DLL、`wwwroot/index.html`、`Drivers`。
5. 目标机安装 ASP.NET Core Runtime 10，配置 SQL Server 及现场认证/加密密钥。生产配置文件为正式目录的 `appsettings.Production.json`。
6. 管理员 PowerShell 执行安装脚本：

```powershell
& .\script\windows\安装Windows服务.ps1 -PublishDirectory E:\IPCGateway\app -DataDirectory E:\IPCGateway\data -EnableServiceRecovery
```

7. 在 Windows“服务”中启动 IPCGateway，访问 `http://127.0.0.1:5184`，检查登录、设备、标签和实际启用模块。

Node.js 只用于构建前端，目标服务运行不依赖 Node.js，也不要求 IIS。远程访问时需显式设置监听地址并配置网络访问规则。

## 已部署系统更新

优先参考 [ZIP 升级操作指南](ZIP升级操作指南.md)。选择 Upgrade，生成目标平台包，备份数据库后在安装升级页面上传、校验、准备并执行。
如果手工更新：先停止服务，备份程序与生产配置，再同步新程序文件，保留数据目录和原配置，最后启动并检查。
已有显式密钥不得随更新改变；只备份程序不等于备份数据库与历史数据。

若更换正式程序位置，停服后重新运行安装脚本更新服务路径。源码目录移动不会自动改变已安装服务的程序目录。

## 卸载

管理员 PowerShell 执行：

```powershell
& .\script\windows\卸载Windows服务.ps1
```

脚本等待服务进程退出，移除服务注册；程序、数据及数据库保留。自定义服务名时，安装和卸载传入相同 ServiceName。

## 常见问题

- 创建服务“拒绝访问”：管理员身份运行 PowerShell。
- 找不到 DLL：PublishDirectory 应指向发布后的程序目录，不是源码目录或 ZIP 根目录。
- 启动失败：检查 .NET 运行时、数据库连接、认证密钥、端口和目录权限。
- 更新后仍是旧界面：确认实际服务路径与当前版本，浏览器刷新前端资源；不要仅替换某个前端文件。
- 无法覆盖文件：服务或其他进程尚未退出，等待文件释放后再更新。
