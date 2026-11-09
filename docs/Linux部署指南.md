# Linux 部署指南

## 准备与安装

目标机需要 systemd、Bash、rsync（支持 --chown）、realpath 和 ASP.NET Core Runtime 10。
在开发电脑用 `script/windows/生成安装升级包.ps1` 生成 linux-x64 或 linux-arm64 包，传到 Linux 后解压。

```bash
sudo bash script/linux/发布Linux服务.sh /tmp/ipc-release/payload
```

首次安装默认不启动。环境文件为 `/etc/ipc-gateway/ipc-gateway.env`，参考 `script/linux/gateway.env.example` 配置已有数据库、认证和加密密钥，再启动服务。
默认程序目录 `/opt/ipc-gateway/app`，数据位于该目录的 Data 子目录。

```bash
sudo systemctl start ipc-gateway
sudo systemctl status ipc-gateway
sudo journalctl -u ipc-gateway -f
```

## 自定义与更新

可指定 INSTALL_DIR、DATA_DIR、SERVICE_NAME、SERVICE_USER、SERVICE_GROUP、ENVIRONMENT_FILE、SERVICE_URL、DOTNET_PATH、START_SERVICE。
程序、数据及 unit 路径使用不含空格的绝对路径。安装与更新使用相同设置。

```bash
sudo INSTALL_DIR=/opt/ipc-gateway/app DATA_DIR=/var/lib/ipc-gateway SERVICE_URL=http://0.0.0.0:5184 START_SERVICE=1 bash script/linux/发布Linux服务.sh /tmp/ipc-release/payload
```

更新时将新的 Linux ZIP 解压到独立目录，再运行同一发布脚本。它会停止当前服务，保留 Data、logs、生产配置及外部数据目录，同步程序后恢复原来运行的服务。
更新前自行备份程序、配置和数据库。此流程没有自动网页回滚点。

Linux 当前不支持网页一键执行 ZIP 升级；上传和校验成功不代表自动停服、替换及重启已实现。

## 卸载

```bash
sudo bash script/linux/卸载Linux服务.sh
```

卸载只移除 systemd 服务，保留程序、环境文件、服务用户、数据和数据库。
