# Docker 部署指南

## 从源码构建并发布

需要 Docker 引擎与 Compose v2，Windows Docker Desktop 选择 Linux 容器模式。
将 `script/docker/gateway.env.example` 复制为同目录 `gateway.env`，设置数据库连接、认证/加密密钥和初始账号密码。
默认 Compose 文件连接外部 SQL Server，不新建数据库容器。

Windows PowerShell：

```powershell
& .\script\docker\发布Docker.ps1
```

Linux Bash：

```bash
bash script/docker/发布Docker.sh
```

脚本从源码构建前端、后端和镜像，后台启动容器。Dockerfile 位于 `script/docker/Dockerfile`，构建上下文是仓库根目录；不要把 script/docker 本身当作源码上下文。
容器里的 127.0.0.1 不是宿主机，连接宿主机 SQL Server 使用局域网 IP 或 host.docker.internal。

## 持久化与更新

`/app/Data` 映射到持久化数据卷，包含历史、MQTT 队列、证书、脚本和模型。保持相同的 Compose 项目名、环境文件与数据卷。
更新通过构建新镜像、重建容器完成，不通过网页 ZIP 覆盖容器内部程序。

离线环境先导出/加载版本镜像，再选择 NoBuild / NO_BUILD=1：

```powershell
& .\script\docker\发布Docker.ps1 -NoBuild -Image ipc-gateway:版本号
```

```bash
NO_BUILD=1 IMAGE=ipc-gateway:版本号 bash script/docker/发布Docker.sh
```

`docker-compose.yml` 是另一个含 PostgreSQL 的示例，通过 ComposeFile / COMPOSE_FILE 明确选择；不能将其视为当前 SQL Server 的自动迁移方案。

## 卸载

```powershell
& .\script\docker\卸载Docker.ps1
```

```bash
bash script/docker/卸载Docker.sh
```

移除容器及网络，保留镜像、环境文件、持久化数据卷和外部数据库。自定义项目名、Compose 文件和环境文件时，卸载传入相同参数。
完整参数见 [发布部署指南](发布部署指南.md)。
