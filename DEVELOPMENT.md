# IPC Gateway 开发文档

本文档面向开发人员，说明当前项目结构、运行方式、配置、数据库、插件、测试和维护工具。

## 技术栈

- 后端：.NET 10、ASP.NET Core Minimal API、SqlSugar。
- 前端：Vue 3、TypeScript、Vite、Element Plus、ECharts。
- 默认数据库：PostgreSQL。
- 可选数据库：MySQL、SQL Server、SQLite。
- 协议插件：基于统一 `IProtocolDriver` / `IPlcClient` 驱动模型。

## 解决方案结构

- `IPC.Gateway.WebHost`：Web API、认证鉴权、静态资源托管、运行托管启动、健康检查。
- `IPC.Gateway.Core`：网关核心模型、应用服务、采集调度、运行状态、SqlSugar 持久化、协议抽象。
- `IPC.Gateway.FlowRules`：流程规则引擎、节点解释、流程执行能力。
- `IPC.Gateway.DataProcessing`：边缘侧数据清洗、压缩、降采样、补点、对齐、聚合。
- `IPC.Gateway.Mqtt`：MQTT 发布、离线缓存、Sparkplug B 相关能力。
- `IPC.Gateway.Inference`：ONNX 本地模型推理。
- `IPC.Gateway.Watchdog`：看门狗、自恢复、异常重启保护。
- `IPC.Gateway.LegacyProtocolPlugins`：从 Winform/旧协议迁移而来的 PLC 协议插件。
- `IPC.Gateway.Maintenance`：离线维护命令，例如管理员密码重置。
- `IPC.Gateway.Web`：Vue + Element Plus 前端。
- `IPC.Gateway.Tests`：后端单元测试、集成测试和回归测试。
- `IPC.Gateway.LoadTests`：模拟设备和采集压测用例。
- `deployment`：Windows Service、Linux systemd、Docker、离线包和发布检查脚本。

新增能力时优先放入职责明确的类库，不要把所有新代码都继续堆到 `IPC.Gateway.Core`。例如规则流程放 `IPC.Gateway.FlowRules`，数据处理放 `IPC.Gateway.DataProcessing`，模型推理放 `IPC.Gateway.Inference`，看门狗放 `IPC.Gateway.Watchdog`。

## 本地开发运行

后端构建：

```powershell
dotnet build IPC.Gateway.slnx --no-restore
```

前端安装和构建：

```powershell
cd IPC.Gateway.Web
npm install
npm run build
```

启动 WebHost：

```powershell
dotnet run --project IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj
```

前端开发服务：

```powershell
cd IPC.Gateway.Web
npm run dev
```

WebHost 发布时会复制 `IPC.Gateway.Web/dist` 到 `wwwroot`，所以发布前需要先构建前端。

## 配置入口

主要配置文件在：

- `IPC.Gateway.WebHost/appsettings.json`
- `IPC.Gateway.WebHost/appsettings.Development.json`
- `IPC.Gateway.WebHost/appsettings.Production.example.json`

常用配置段：

- `Gateway:Database`：数据库类型、连接参数、自动建库。
- `Gateway:Auth`：登录密钥、Token 时长、初始管理员账号。
- `Gateway:Security`：密码策略、登录锁定、TLS、API Token、证书和密钥存储。
- `Gateway:Runtime`：采集调度、队列、水位、超时、背压和线程隔离。
- `Gateway:Mqtt`：MQTT 普通模式、TLS、证书、离线缓存、Sparkplug B。
- `Gateway:OpcUa`：OPC UA Server 监听、命名空间、证书和采样参数。
- `Gateway:History`：历史库、冷热分层、保留策略、清洗和聚合。
- `Gateway:Watchdog`：看门狗、自恢复和异常重启保护。

生产环境必须提供非默认的 `Gateway:Auth:Secret` 和初始管理员密码。

## 数据库

默认数据库是 PostgreSQL。SqlSugar 连接配置来自 `Gateway:Database`：

```json
"Database": {
  "Provider": "PostgreSQL",
  "ConnectionString": "",
  "Host": "localhost",
  "Port": 5432,
  "Database": "ipc_gateway",
  "Username": "postgres",
  "Password": "",
  "AutoCreateDatabase": true
}
```

切换 SQL Server 示例：

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "",
  "Host": "localhost",
  "Port": 1433,
  "Database": "ipc_gateway",
  "Username": "sa",
  "Password": "your-password",
  "AutoCreateDatabase": true
}
```

也可以使用完整连接字符串：

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=localhost,1433;Database=ipc_gateway;User Id=sa;Password=your-password;TrustServerCertificate=True;Encrypt=False;",
  "AutoCreateDatabase": false
}
```

数据库迁移由 `GatewayDatabaseMigrator` 自动执行，迁移定义在 `GatewayMigrations`。程序启动或相关仓储初始化时会检查并执行缺失迁移。

注意：自动迁移负责建库建表和结构演进，不负责 PostgreSQL 到 SQL Server 的业务数据搬迁。跨数据库搬迁需要单独导入数据或后续补维护命令。

## 认证、角色与权限

认证入口在 `GatewayAuthEndpoints` 和 `GatewayAuthService`。

权限目录在：

- 后端：`IPC.Gateway.Core/Domain/Users/GatewayPermissionCatalog.cs`
- 前端：`IPC.Gateway.Web/src/utils/permissions.ts`

新增按钮级权限时需要同步：

1. 后端 `GatewayPermissions` 常量。
2. 后端权限 `Catalog` 中文名称和描述。
3. 默认角色权限或旧权限展开逻辑。
4. WebHost 鉴权方法。
5. 前端 `PERMISSIONS` 常量。
6. 页面按钮显示和 API 调用保护。

当前人员密码相关接口：

- 当前用户修改密码：`PUT /api/auth/password`
- 管理员重置人员密码：`PUT /api/auth/users/{username}/password`
- 离线重置管理员密码：`IPC.Gateway.Maintenance reset-admin --password "new-password"`

## 前端页面

入口文件：

- `IPC.Gateway.Web/src/App.vue`
- `IPC.Gateway.Web/src/api.ts`
- `IPC.Gateway.Web/src/styles.css`

主要页面组件：

- `BigScreenView.vue`：大屏总览。
- `DeviceTopologyView.vue`：设备拓扑。
- `DashboardView.vue`：运行总览。
- `DevicesView.vue`：设备、分组、标签和当前值。
- `RulesView.vue`：简单规则。
- `FlowRulesView.vue`：流程规则编辑器。
- `MqttView.vue`：MQTT 配置。
- `OpcUaView.vue`：OPC UA Server 配置。
- `HistoryView.vue`：历史库和边缘数据处理配置。
- `AuditView.vue`：审计日志。
- `SecurityView.vue`：工业安全。
- `MaintenanceView.vue`：看门狗、安装升级和版本回滚。
- `UsersView.vue`：人员管理。
- `RolesView.vue`：角色管理。
- `PermissionsView.vue`：权限分配。

前端定时刷新页面时，涉及配置表单的页面需要避免覆盖正在编辑的草稿。已有模式可参考 `App.vue` 中的 `shouldPreserveActiveConfigDraft`。

## 协议驱动插件

统一接口：

- `IProtocolDriver`
- `IPlcClient`

插件发现依赖插件程序集和 `*.ipc-driver.json` manifest。已迁移协议插件位于 `IPC.Gateway.LegacyProtocolPlugins`。

当前旧协议插件包括：

- Modbus TCP
- Modbus RTU
- Mitsubishi MC
- Mitsubishi MC 1E
- Mitsubishi Serial
- Mitsubishi QL Serial
- Omron FINS
- Siemens S7
- CIP
- OPC DA
- OPC UA Client

发布时需要将协议插件复制到 WebHost 的 `Drivers` 目录。`IPC.Gateway.LegacyProtocolPlugins.csproj` 已包含构建后复制到 WebHost Debug 输出目录的逻辑。

新增协议建议：

1. 新建独立协议插件类库。
2. 实现统一驱动接口。
3. 提供 manifest，包含 `driverId`、`displayName`、`version`、`minGatewayVersion`。
4. 将协议连接参数映射到统一设备模型。
5. 标签字段保持界面友好，特别是 DL/T 645 和 CJ/T 188 的协议、表地址、数据标识、表类型。
6. 增加协议连接、读写、异常、超时和卸载测试。

## 采集调度与运行状态

采集调度支持：

- 设备独立采集周期。
- 标签独立采集周期。
- 失败重试。
- 慢设备隔离。
- 采集线程隔离。
- 背压、限流和队列水位保护。
- 任务状态、队列状态、超时统计。

运行状态包括：

- 设备在线状态。
- 标签最近值、质量、更新时间和最近错误。
- 采集成功率。
- MQTT 状态。
- 规则引擎状态。
- 历史库状态。
- OPC UA Server 状态。
- 系统 CPU、内存和队列状态。

运行状态持久化和缓存化由 SqlSugar 持久化仓储和运行时状态服务负责。

## 规则与流程规则

简单规则保存在当前规则配置模型中，流程规则保存在 `FlowRuleDefinition` JSON 中。

流程规则执行由 `IPC.Gateway.FlowRules` 负责。简单流程可编译成当前规则配置，复杂流程由流程解释服务执行。

流程规则节点类型包括：

- 输入和标签选择。
- 阈值、滞回、多级告警、表达式。
- 聚合、窗口、趋势、状态机、节拍、异常检测。
- 转换节点：乘数、偏移、取绝对值、表达式。
- 脚本或低代码函数节点，必须有沙箱和超时限制。
- 动作节点：MQTT、邮件、Webhook。

调试模式支持执行路径高亮和模拟输入。

## 数据清洗与历史处理

`IPC.Gateway.DataProcessing` 提供边缘侧数据处理能力：

- 越界质量标记。
- 死区过滤。
- 重复值过滤。
- 毛刺过滤。
- 枚举映射。
- 单位换算。
- 清洗前/清洗后双值保存。
- 数据压缩。
- 降采样。
- 补点。
- 对齐。
- 聚合。

前端历史库页面已按压缩、采样、补点、聚合等配置拆分组件。

## MQTT、OPC UA 和历史库状态

WebHost 暴露状态 API，前端用于展示：

- MQTT 连接和离线缓存状态。
- 规则引擎状态。
- 历史库状态。
- OPC UA Server 状态。
- 健康检查状态。

MQTT 支持普通模式、TLS、用户名密码、客户端证书、离线缓存和 Sparkplug B。

OPC UA Server 用于向 MES、SCADA、上位系统提供标准数据访问。

## 看门狗和部署

`IPC.Gateway.Watchdog` 作为独立 hosted service 运行，监控运行时、调度器、MQTT、历史库、规则引擎、OPC UA Server 等状态。

支持部署方式：

- Windows Service。
- Linux systemd。
- Docker。

部署脚本和说明在 `deployment` 目录下。

离线安装和升级包通过 `deployment/package/build-ipc-gateway-package.ps1` 生成。Web 端安装升级页面支持上传、准备升级、创建回滚点和版本回滚。

## 维护工具

管理员密码离线重置：

```powershell
dotnet run --project IPC.Gateway.Maintenance/IPC.Gateway.Maintenance.csproj -- reset-admin --password "new-password"
```

可选参数：

```powershell
--username <name>
--config-dir <path>
--environment <name>
```

该工具复用现有 SqlSugar 用户仓储和密码策略，不直接拼 SQL 修改密码。

## 测试与验证

后端测试：

```powershell
dotnet test IPC.Gateway.Tests/IPC.Gateway.Tests.csproj --no-restore
```

前端构建：

```powershell
cd IPC.Gateway.Web
npm run build
```

依赖漏洞检查：

```powershell
dotnet list IPC.Gateway.slnx package --vulnerable --include-transitive
```

如果本机正在运行 WebHost，默认输出目录里的 DLL 可能被锁定。可以使用独立输出目录验证 WebHost 编译：

```powershell
dotnet build IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj --no-restore --no-dependencies -o artifacts/verify-webhost-build
```

验证后清理临时输出目录。

## 发布注意事项

- 生产环境不要提交真实数据库密码、认证密钥、证书密码。
- `Gateway:Auth:Secret` 生产环境必须是非默认值且不少于 32 个字符。
- `appsettings.Development.json` 不应进入生产发布包。
- 发布 WebHost 前必须先构建前端。
- 发布包中应包含 `wwwroot/index.html`。
- 发布包中应包含所需协议插件和 `Drivers` 目录。
- 部署到反向代理后面时，需要正确配置 Forwarded Headers 的可信代理边界。
