# 仓库与供应链治理

本项目面向制造业现场交付，仓库需要做到可审计、可复现、可清理。以下规则用于减少“本机能跑、现场不可复现”和依赖漂移风险。

## 已接入规则

- 根目录 `.gitignore` 阻止新增 `.vs/`、`bin/`、`obj/`、`node_modules/`、`artifacts/`、运行日志和本机配置覆盖文件。
- 根目录 `.gitattributes` 固定源码/配置文件为文本 diff，二进制构建产物按 binary 处理，避免 `.cs` 文件被误判为二进制。
- `Directory.Build.props` 启用 NuGet lock file 机制，并在 CI 模式下启用 locked restore。
- `IPC.Gateway.Web/package.json` 的直接依赖改为精确版本，实际解析版本由 `package-lock.json` 固定。
- `scripts/supply-chain/Test-SupplyChain.ps1` 提供统一检查入口。

## 本地检查

离线检查：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/supply-chain/Test-SupplyChain.ps1
```

联网环境增加漏洞审计：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/supply-chain/Test-SupplyChain.ps1 -OnlineAudit
```

CI 或发布分支建议启用严格仓库卫生检查：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/supply-chain/Test-SupplyChain.ps1 -OnlineAudit -EnforceRepositoryHygiene
```

## 依赖规则

- NuGet `PackageReference` 必须使用精确版本，不使用 `*`、版本范围或浮动版本。
- npm 直接依赖必须使用精确版本；升级依赖时同时更新 `package.json` 和 `package-lock.json`。
- 生产发布前必须跑一次在线漏洞审计；离线工厂环境至少保留最近一次审计报告。
- 任何新增商业闭源 SDK、PLC 驱动或 native 依赖，需要记录来源、版本、许可证和交付范围。

## 仓库清理待办

当前仓库历史中已经跟踪了较多生成物，例如 `.vs/`、`bin/`、`obj/`、`artifacts/`、`IPC.Gateway.Web/node_modules/`。这次先防止新增污染，没有直接从索引移除，避免影响正在进行的开发。

建议在独立清理分支执行：

```powershell
git rm -r --cached .vs artifacts IPC.Gateway.Web/node_modules
git ls-files | Select-String '(^|/)(bin|obj)/' | ForEach-Object { git rm --cached -- "$($_.Line)" }
```

清理后重新运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/supply-chain/Test-SupplyChain.ps1 -EnforceRepositoryHygiene
dotnet test IPC.Gateway.Tests\IPC.Gateway.Tests.csproj --no-restore
npm --prefix IPC.Gateway.Web run build
```

## 密钥与配置

- 生产密钥不得提交到仓库，使用环境变量、密钥管理系统或现场受控配置文件注入。
- `appsettings.Production.example.json` 只保留示例占位值。
- 本机覆盖文件使用 `appsettings.Local.json` 或 `*.local.json`，不纳入版本控制。
