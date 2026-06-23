# IPC Gateway

IPC Gateway 是面向制造业现场的边缘计算网关。当前项目提供 Web 管理界面、协议采集、规则引擎、MQTT/OPC UA 对外服务、历史库、权限审计、看门狗和离线升级能力。

## 当前支持的主要操作

### 登录与账号

- 用户登录，登录页带图形验证码。
- 当前用户修改密码。
- 人员管理：新增、编辑、删除、筛选人员。
- 人员密码重置：拥有权限的管理员可为其他用户重置密码。
- 角色管理：新增、编辑、删除角色。
- 权限分配：按角色分配页面级和按钮级权限。

### 运行监控

- 大屏总览：展示网关健康、模块状态、系统资源、告警、趋势和慢设备。
- 运行总览：展示设备数、在线状态、标签数、成功率、CPU、内存、MQTT、规则、历史库等运行状态。
- 设备拓扑：查看设备、协议、分组、标签和服务拓扑关系。
- 健康检查：支持 live、ready 和 API readiness 检查。
- 错误详情：查看设备、采集、规则、MQTT、历史库等最近错误。
![输入图片说明](%E5%A4%A7%E5%B1%8F%E6%80%BB%E8%A7%88.png)
![输入图片说明](%E8%BF%90%E8%A1%8C%E6%80%BB%E8%A7%88.png)
![输入图片说明](%E8%AE%BE%E5%A4%87%E6%8B%93%E6%89%91.png)

### 设备与标签

- 设备管理：新增、编辑、删除设备。
- 按协议展示不同连接参数表单。
- 分组管理：新增、编辑、删除分组，支持启用和采集周期。
- 标签管理：新增、编辑、删除标签，支持地址、数据类型、采集周期、失败重试和写入值。
- 标签当前值：展示当前值、质量、更新时间、最近错误。
- 标签缩放：支持标签值放大或缩小指定倍数。
- 友好字段：DL/T 645、CJ/T 188 标签保留协议、表地址、数据标识、表类型字段。
![输入图片说明](%E8%AE%BE%E5%A4%87%E7%AE%A1%E7%90%86.png)

### 协议采集

- 统一协议驱动模型：`IPlcClient` / `IProtocolDriver`。
- 支持插件化协议驱动发现、加载、卸载和版本校验。
- 支持 DL/T 645、CJ/T 188、Modbus TCP 抄表类协议配置字段。
- 支持设备独立采集周期、标签独立采集周期、失败重试、慢设备隔离、采集线程隔离。
- 支持采集队列状态、任务状态、超时统计、背压、限流和队列水位保护。

### 数据处理与历史库

- 历史数据保存。
- 清洗前/清洗后双值保存。
- 数据清洗规则：越界质量标记、死区过滤、重复值过滤、毛刺过滤、枚举映射、单位换算。
- 边缘侧处理：压缩、降采样、补点、对齐、聚合。
- 历史库配置：冷热分层、保留策略、压缩、自动清理。
- 历史库状态 API 和前端状态展示。
![输入图片说明](%E5%8E%86%E5%8F%B2%E5%BA%93.png)

### 规则引擎

- 简单规则：单标签阈值、条件、死区、变化率。
- 组合规则：多个条件，支持 AND / OR。
- 规则事件与调试：active 状态、触发时间、恢复时间、事件列表、最近错误、评估次数、触发次数、恢复次数。
- 模拟测试规则：手动输入值并预览触发、active/clear 消息。
- 流程规则编辑器：节点、连线、缩放、适配画布、迷你地图、节点搜索、复制粘贴、自动布局、执行路径高亮。
- 流程规则能力：滞回规则、多级告警、表达式规则、聚合、窗口计算、趋势判断、状态机、工艺节拍、异常检测、顺序/时序规则。
- 函数节点：支持乘数、偏移、取绝对值、表达式处理。
- 动作节点：支持 MQTT 发布、邮件通知、Webhook。
![输入图片说明](%E6%B5%81%E7%A8%8B%E8%A7%84%E5%88%99.png)

### 通讯与对外服务

- MQTT 普通模式。
- MQTT Sparkplug B 模式。
- MQTT TLS、用户名密码、客户端证书配置。
- MQTT 连接状态、发布状态、离线缓存和积压状态展示。
- OPC UA Server：让网关采集到的数据以标准 OPC UA 方式提供给 MES、SCADA、上位系统。
- OPC UA Server 配置和运行状态展示。


### 工业安全与审计

- 账号策略：密码复杂度、登录锁定。
- API Token。
- 设备证书、MQTT 客户端证书、TLS 证书管理。
- 密钥加密存储。
- 接口鉴权。
- 操作审计和配置变更审计。
- 审计日志查看和导出。
![输入图片说明](%E5%B7%A5%E4%B8%9A%E5%AE%89%E5%85%A8.png)

### 运维与部署

- 看门狗、自恢复、异常重启保护。
- Windows Service、Linux systemd、Docker 三种部署方式。
- 安装包、升级包、离线升级、版本回滚。
- 管理员密码维护工具：`IPC.Gateway.Maintenance reset-admin --password "new-password"`。

### 本地模型推理

- 支持本地 ONNX 模型推理。
- 可用于质量预测和设备异常预警类边缘推理场景。

## 本地部署方法

### 直接运行源码

1. 启动数据库。默认使用 PostgreSQL，配置位置为 `IPC.Gateway.WebHost/appsettings.json` 的 `Gateway:Database`。
2. 构建前端：

```powershell
cd IPC.Gateway.Web
npm install
npm run build
cd ..
```

3. 启动后端 WebHost：

```powershell
dotnet run --project IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj
```

4. 打开 Web 页面。默认地址以控制台输出为准，常见为：

```text
http://localhost:5184
```

首次运行时会按配置执行数据库迁移并初始化必要表结构。

### Windows Service 部署

1. 构建前端并发布 WebHost：

```powershell
Push-Location IPC.Gateway.Web
npm install
npm run build
Pop-Location

dotnet publish IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release -o C:\IPC.Gateway\app
dotnet publish IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o C:\IPC.Gateway\app\Drivers /p:UseAppHost=false
```

2. 以管理员身份安装服务：

```powershell
deployment/windows/install-ipc-gateway-service.ps1 `
  -PublishDirectory C:\IPC.Gateway\app `
  -Url http://127.0.0.1:5184 `
  -DataDirectory C:\IPC.Gateway\data `
  -EnableServiceRecovery `
  -Start
```

3. 检查健康状态：

```powershell
Invoke-RestMethod http://127.0.0.1:5184/health/live
Invoke-RestMethod http://127.0.0.1:5184/health/ready
```

卸载服务：

```powershell
deployment/windows/remove-ipc-gateway-service.ps1
```

### Linux systemd 部署

1. 构建前端并发布：

```bash
cd IPC.Gateway.Web
npm install
npm run build
cd ..

dotnet publish IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj -c Release -o ./artifacts/publish/ipc-gateway
dotnet publish IPC.Gateway.LegacyProtocolPlugins/IPC.Gateway.LegacyProtocolPlugins.csproj -c Release -o ./artifacts/publish/ipc-gateway/Drivers /p:UseAppHost=false
```

2. 安装 systemd 服务：

```bash
sudo PUBLISH_DIR=/absolute/path/to/artifacts/publish/ipc-gateway \
  SERVICE_URL=http://127.0.0.1:5184 \
  START_SERVICE=1 \
  deployment/linux/install-ipc-gateway-systemd.sh
```

3. 常用运维命令：

```bash
sudo systemctl status ipc-gateway
sudo journalctl -u ipc-gateway -f
sudo systemctl restart ipc-gateway
sudo systemctl stop ipc-gateway
```

### Docker 部署

Docker Compose 文件包含 WebHost 和 PostgreSQL，适合本地或单机部署验证：

```bash
docker compose -f deployment/docker/docker-compose.yml up -d --build
```

打开：

```text
http://localhost:5184
```

查看日志和重启：

```bash
docker compose -f deployment/docker/docker-compose.yml logs -f ipc-gateway
docker compose -f deployment/docker/docker-compose.yml restart ipc-gateway
```

停止：

```bash
docker compose -f deployment/docker/docker-compose.yml down
```

生产使用前请修改认证密钥、初始管理员密码和数据库密码，并将 `/app/Data` 挂载到持久化存储。

### 离线安装包和升级包

生成安装包或升级包：

```powershell
deployment/package/build-ipc-gateway-package.ps1 -PackageType Install -Version 1.0.0
deployment/package/build-ipc-gateway-package.ps1 -PackageType Upgrade -Version 1.0.1
```

升级包可在 Web 页面 `安装升级` 中上传，系统会校验包、创建回滚点，并生成离线升级动作。实际应用升级建议安排在维护窗口执行。

## 测试流程

### 后端测试

运行后端测试：

```powershell
dotnet test IPC.Gateway.Tests/IPC.Gateway.Tests.csproj --no-restore
```

运行解决方案构建：

```powershell
dotnet build IPC.Gateway.slnx --no-restore
```

如果本机正在运行 WebHost，输出目录 DLL 可能被占用。可使用独立输出目录验证 WebHost：

```powershell
dotnet build IPC.Gateway.WebHost/IPC.Gateway.WebHost.csproj --no-restore --no-dependencies -o artifacts/verify-webhost-build
```

### 前端测试

构建前端：

```powershell
cd IPC.Gateway.Web
npm run build
```

本地预览：

```powershell
npm run dev -- --host 127.0.0.1 --port 5173
```

### 健康检查

服务启动后检查：

```powershell
Invoke-RestMethod http://127.0.0.1:5184/health/live
Invoke-RestMethod http://127.0.0.1:5184/health/ready
Invoke-RestMethod http://127.0.0.1:5184/api/health/ready
```

`live` 用于进程存活检查，`ready` 用于判断网关是否可以接收业务流量。

### 浏览器冒烟测试

建议每次发布前至少验证：

- 登录页可以打开，验证码正常显示。
- 使用管理员账号可以登录。
- 默认进入大屏总览。
- 左侧菜单可以展开、折叠、切换。
- 运行总览能显示设备、标签、成功率、CPU、内存和模块状态。
- 设备管理能新增、编辑、删除设备、分组、标签。
- 人员管理能新增人员、编辑人员、重置密码。
- 权限分配能看到大屏总览、设备拓扑和按钮级权限。
- MQTT、OPC UA、历史库配置保存后不会被自动刷新覆盖。
- 流程规则编辑器能新增节点、连线、保存和调试。
- 审计日志能记录登录、配置变更、权限相关操作。

### 依赖安全检查

检查 NuGet 依赖漏洞：

```powershell
dotnet list IPC.Gateway.slnx package --vulnerable --include-transitive
```

### 发布前最小检查清单

- 后端测试通过。
- 前端构建通过。
- WebHost 发布包中存在 `wwwroot/index.html`。
- 协议插件已发布到 WebHost `Drivers` 目录。
- 数据库连接、认证密钥、管理员初始密码已按目标环境配置。
- `/health/ready` 返回符合预期的状态。
- 浏览器冒烟测试通过。
