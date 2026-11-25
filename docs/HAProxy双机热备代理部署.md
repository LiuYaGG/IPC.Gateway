# HAProxy 双机热备代理部署

在银河麒麟 C 上安装免费的 HAProxy 社区版，统一代理 Windows A 和统信 B。HAProxy 自带主动健康检查，无需额外探测脚本；主备接管继续由网关现有高可用集群负责。

| 角色 | 地址 |
| --- | --- |
| A：Windows 网关 | `http://192.168.110.154:5184` |
| B：统信 UOS 网关 | `http://192.168.164.131:5184` |
| C：银河麒麟，仲裁成员及 HAProxy | `192.168.164.128` |
| 统一网站入口 | `http://192.168.164.128:8080` |
| HAProxy 统计页，仅 C 本机 | `http://127.0.0.1:8404/` |

本文先完成内网 HTTP 验证，不改网关源码或 C 原有仲裁配置。已有 HTTPS 要求时保留安全要求，按第 5 节处理。

**本次银河麒麟 C 已确认使用 APT 包装器，安装和注册服务请直接采用文末“附录：银河麒麟 V11 的 APT 包装器与独立目录部署”。独立部署的服务名为 `ipc-gateway-proxy.service`，避免与 KARE 的 `haproxy.service` 冲突。正文第 4～6、8～9 节的网关配置、代理配置及验收步骤仍适用；正文包管理安装方式中的服务管理命令需相应使用新名称。**

## 1. 下载地址

- [HAProxy 官网与下载列表](https://www.haproxy.org/#down)。
- [HAProxy 3.2 配置手册](https://docs.haproxy.org/3.2/configuration.html)。
- [Debian / Ubuntu 软件包说明](https://haproxy.debian.net/)。

截至 2026 年 11 月 24 日，3.2 LTS 维护分支为 3.2.27：[源码下载](https://www.haproxy.org/download/3.2/src/haproxy-3.2.27.tar.gz)、[SHA-256 校验文件](https://www.haproxy.org/download/3.2/src/haproxy-3.2.27.tar.gz.sha256)。这是源码包，不能解压后直接运行。[版本来源](https://www.haproxy.org/#down)

**本次优先直接用 C 的原生软件仓库安装**，不必先在 Windows 下载源码。本配置面向 HAProxy 2.6 及以上版本，应使用发行版提供安全维护的版本；不要给银河麒麟直接添加跨发行版的 Debian 软件源或 Ubuntu PPA。

## 2. 在 C 进入原生系统

此前 C 终端处于 KARE 兼容环境，文件和服务可能无法被原生 systemd 看到。在 C 原来的终端执行：

```bash
sudo nsenter --target 1 --mount --root --wd -- /bin/bash --noprofile --norc
```

若已在上述原生 root 窗口，继续使用即可。后续 C 命令全部在该窗口运行：

```bash
export PATH=/usr/sbin:/usr/bin:/sbin:/bin
unset LD_PRELOAD LD_LIBRARY_PATH BASH_ENV ENV
id -u
cat /etc/os-release
```

应显示用户编号 `0` 和原生银河麒麟 V11。下文用原生 `/usr/bin/systemctl`。[nsenter 说明](https://man7.org/linux/man-pages/man1/nsenter.1.html)

## 3. 安装软件

在 C 原生 root 窗口依次执行，每条成功后再继续：

```bash
apt-get update
apt-get install -y haproxy curl ca-certificates
command -v haproxy
haproxy -v
```

若找不到包、没有候选版本，或版本低于 2.6，先收集 `cat /etc/os-release` 和 `apt-cache policy haproxy` 的输出核对原生软件源，不要随意添加其他发行版的软件源。安装器可能已经启动默认服务，后文会在校验新配置后重启。

如果安装提示成功但命令不存在，先用 `dpkg-query -W haproxy` 和 `dpkg-query -L haproxy` 核对包记录及实际安装路径，同时检查 `/proc/1/root` 中能否看到对应文件。此前 C 使用过兼容环境，不能只凭安装提示判断原生系统已有程序。后续命令使用原生 PATH 中实际找到的 `haproxy`，不强制假设二进制在 `/usr/sbin`。

## 4. 核对 A、B 的配置

A、B 应使用同一共享业务库和相同的 `Gateway:Auth:Secret`，使登录令牌能在切换后继续验证。它与 `Gateway:Security:SecretStorage:MasterKey` 不同；核对现有值，不要重新生成或替换其他密钥。

共享脚本数据库连接等敏感配置还要求 A、B 使用相同的 `Gateway:Security:SecretStorage:MasterKey`，并保持 `SecretStorage.Enabled=true`。沿用现有主机能够解密共享数据的密钥，不生成新值。B 默认读取 `/etc/ipc-gateway/ipc-gateway.env`；其中的 `Gateway__Security__SecretStorage__MasterKey` 会覆盖生产 JSON 配置。若日志出现“共享脚本数据库连接解密失败”，先统一有效密钥并检查该覆盖项，再重启 B 完成预热。

现场生产配置通常为 A 的 `E:\IPCGateway\1731\app\appsettings.Production.json` 和 B 的 `/opt/ipc-gateway/app/appsettings.Production.json`。若通过环境变量覆盖，以实际生效配置为准。

在两边原配置的 **`Gateway` 对象内**添加或修改：

```json
"ForwardedHeaders": {
  "Enabled": true,
  "KnownProxies": ["192.168.164.128"],
  "KnownNetworks": []
}
```

这是局部配置，不能替换整个文件；保留其他实际使用的可信代理。它让网关识别转发后的客户端 IP 和协议。A、B 的监听地址必须允许 C 访问，不能只绑定 `127.0.0.1`。如 `AllowedHosts` 限制了主机名，需允许入口 IP `192.168.164.128`，以后换域名时也加入实际域名。

配置修改后按现有方式重启对应服务。已启用热备时，先改备机；主机先交接业务再重启。通过 A/B 各自的直连网址执行这些维护。

## 5. 从 C 检查主机接口

探测使用与代理一致的 Host。在 C 执行：

```bash
curl -sS -i --connect-timeout 2 --max-time 3 -H 'Host: 192.168.164.128:8080' http://192.168.110.154:5184/health/ha-active
curl -sS -i --connect-timeout 2 --max-time 3 -H 'Host: 192.168.164.128:8080' http://192.168.164.131:5184/health/ha-active
```

| 结果 | 处理 |
| --- | --- |
| `200`，正文 `{"active":true}` | 启动就绪且具备业务主身份，可以转发 |
| `503`，正文 `{"active":false}` | 备机或暂未就绪，不转发 |
| `401` / `403` | 先解决探测认证或访问控制 |
| `400` | 检查 AllowedHosts 是否允许 C 的入口 IP |
| 重定向到 HTTPS | 使用实际 HTTPS 端点和可信证书配置 |
| 拒绝连接 / 超时 | 检查服务、监听、网络和防火墙 |

正常热备应是一台 `200`、另一台 `503`。A 单机、B 管理待命时可先验证入口，但**自动接管验收必须先完成并启用高可用集群**。若两台都独立单机运行、同时返回 `200`，下面的代理将返回 `503`，应先解决集群状态。

健康检查鉴权由 `Gateway:Security:Api:RequireAuthenticationForHealth` 控制。已启用时保留认证要求，使用专用 API Token。将下一节配置的 `http-check send` 行末尾追加：

```haproxy
hdr X-API-Token YOUR_HEALTH_CHECK_TOKEN
```

追加到同一行，并将占位值换成实际 Token；A/B 需接受同一专用 Token。若现场自定义了 `Gateway:Security:ApiTokens:HeaderName`，头名也要相应修改。不要把 `401` 当健康状态，或直接关闭工业安全；真实 Token 不提交 Git。

若 `Gateway:Security:ApiTokens:RequireHttps` 为 `true`，探测必须走实际 HTTPS 后端，并配置可信 CA、主机名验证和 SNI。本文 HTTP 配置不适用该情况，不要用 `verify none` 或伪造转发协议绕过 HTTPS 要求。

## 6. 写入代理配置

先执行 `ss -lntp`，确认 C 的 8080 和 8404 未被其他业务占用。备份现有配置：

```bash
if [ -f /etc/haproxy/haproxy.cfg ]; then
  cp -a /etc/haproxy/haproxy.cfg "/etc/haproxy/haproxy.cfg.before-ipc-$(date +%Y%m%d-%H%M%S)"
fi
```

在 C 原生 root 窗口完整粘贴以下内容。项目中也有[同版参考配置](参考配置/haproxy双机热备.cfg.example)。

```bash
cat > /etc/haproxy/haproxy.cfg <<'HAPROXY_CONFIG'
# Community HAProxy >= 2.6. Install on node C's native Linux system.
# HTTP test entry: http://192.168.164.128:8080
# Local statistics: http://127.0.0.1:8404/
global
    log stdout format raw local0
    maxconn 2048
    user haproxy
    group haproxy

defaults
    log global
    mode http
    option httplog
    option dontlognull
    option http-server-close
    retries 0
    retry-on none
    timeout connect 2s
    timeout client 5m
    timeout server 5m
    timeout http-request 30s
    timeout http-keep-alive 10s
    timeout check 1s

frontend ipc_gateway_entry
    bind 0.0.0.0:8080
    # Both servers are regular servers: zero or two healthy nodes reject traffic.
    acl exactly_one_active nbsrv(ipc_gateways) eq 1
    http-request deny deny_status 503 unless exactly_one_active
    # This proxy is the only trusted external entry in this configuration.
    http-request del-header Forwarded
    http-request set-header X-Forwarded-For %[src]
    http-request set-header X-Forwarded-Proto http
    default_backend ipc_gateways

backend ipc_gateways
    option httpchk
    http-check send meth GET uri /health/ha-active ver HTTP/1.1 hdr Host 192.168.164.128:8080
    http-check expect status 200
    # Marking a node down also disconnects its old SSE and other active sessions.
    default-server inter 1s fall 2 rise 2 on-marked-down shutdown-sessions
    server gateway_a 192.168.110.154:5184 check
    server gateway_b 192.168.164.131:5184 check
    # On HAProxy >= 3.2 only, append "init-state fully-down" to each server above
    # to require successful checks before it first becomes available.

listen ipc_gateway_stats
    bind 127.0.0.1:8404
    mode http
    stats enable
    stats uri /
    stats refresh 2s
    stats show-legends
HAPROXY_CONFIG
chown root:haproxy /etc/haproxy/haproxy.cfg
chmod 640 /etc/haproxy/haproxy.cfg
```

每秒检查两台，连续两次成功才恢复，连续两次失败则停用；只允许一个健康节点接收业务。节点停用时关闭其已有连接，促使 SSE 重连；不自动重试 PLC 写入等业务请求。客户端/后端的不活动超时为 5 分钟，SSE 会发送心跳，代理不缓存流式响应。

关闭连接也可能中断正在保存、上传或执行写入的请求。遇到结果未知，先核实操作是否已经完成，再决定是否重试；尤其不能直接重复下发 PLC 写入命令。

基础配置首次启动有健康检查收敛窗口，启动后先检查统计页再使用。本指南仅对 **3.2 及更新的受维护分支**建议为两个 `server` 行追加 `init-state fully-down`，使初次启动也先通过检查；先用 `haproxy -v` 确认版本，再做语法校验。不要把探测状态理解成逐请求、零延迟的主身份验证。[配置手册](https://docs.haproxy.org/3.2/configuration.html)

## 7. 校验并启动

```bash
haproxy -c -f /etc/haproxy/haproxy.cfg
```

配置有效、没有 `ALERT` 错误后再执行：

```bash
/usr/bin/systemctl enable haproxy.service
/usr/bin/systemctl restart haproxy.service
/usr/bin/systemctl status haproxy.service --no-pager -l
```

预期 `Active: active (running)`。使用发行版提供的 systemd 单元，不额外添加 `daemon`。这一步不会自行启用网关集群，也不会改变 A/B 的主备角色。

## 8. 放行端口并访问

防火墙允许客户端网段访问 C 的 TCP 8080，允许 C（`192.168.164.128`）访问 A/B 的 TCP 5184。8404 保持本机监听，不对外放行。A 可在 Windows Defender 防火墙高级入站规则中检查；B/C 按当前系统防火墙添加规则，原有仲裁端口 7281、7282 保持不变。

等待探测稳定后，在 C 检查：

```bash
curl -sS -I -H 'Host: 192.168.164.128:8080' http://127.0.0.1:8080/
curl -fsS 'http://127.0.0.1:8404/;csv' | awk -F, '$1=="ipc_gateways" && ($2=="gateway_a" || $2=="gateway_b") {print $2, $18}'
```

预期一台 `UP`、一台 `DOWN`。备机的 `DOWN` 表示不接受主业务流量，不代表进程故障。Windows 浏览器打开 **http://192.168.164.128:8080** 并重新登录一次，确认实时值持续刷新；A/B 直连 IP 的 Cookie 不会自动变成新入口的 Cookie。

在“系统 → 高可用 → 高可用配置”把“稳定业务地址”填写为 `http://192.168.164.128:8080` 并按页面流程保存。它不替代任何成员的 7281/7282 地址，集群开关仍按原计划操作。

## 9. 验收主备切换

1. 确认集群已启用、C 仲裁正常，A/B 只有一个主机。保持浏览器固定入口打开并观察变化点位。
2. 以 A 为主为例，确认 A 探针为 `200`、B 为 `503`。
3. 在 Windows“服务”中停止 **IPC Gateway Edge Service（IPCGateway）**；只停网关服务，不关闭承载虚拟机的宿主电脑。
4. 等待网关原有机制让 B 接管，再从 C 检查：B 探针应为 `200`，统计页随后显示 B 为 `UP`。
5. 确认浏览器地址不变、实时推送和点位刷新恢复，有效期内的登录能继续使用。
6. 启动 A 的服务，核对集群仍只有一个主机。是否切回 A 按网关策略，HAProxy 只跟随实际主身份。

也可先通过高可用界面做一次计划交接。实际恢复耗时包含网关接管、代理探测确认和浏览器重连；每秒探测不等于保证一秒内全部恢复。短暂 `503`、SSE 断流需要在现场记录耗时验收。

## 10. 排障、停用与边界

```bash
/usr/bin/systemctl status haproxy.service --no-pager -l
/usr/bin/journalctl -u haproxy.service -n 100 --no-pager -a -o cat
haproxy -c -f /etc/haproxy/haproxy.cfg
```

| 现象 | 检查方向 |
| --- | --- |
| 8080 拒绝连接 | 服务、监听端口、C 防火墙和虚拟机网络 |
| 入口 `503` | 两个探针和统计页：零台或两台 `UP` 都会拒绝转发 |
| 两台 `DOWN` | 接管未完成、认证失败、要求 HTTPS、网络不通 |
| 切换后登录失效 | A/B 登录密钥、共享账号库、Cookie 入口、令牌有效期 |
| systemd 显示服务不存在 | 是否又在兼容环境操作，返回第 2 节原生窗口 |
| 未知配置关键字 | 版本是否匹配，修改后先执行 `haproxy -c` |
| 页面能开、实时值不更新 | SSE 连接和网关日志、当前主身份、其他代理缓冲 |

升级、驱动签名、证书和维护操作继续用 A/B 直连地址。升级包等属于节点本地，多步操作经统一入口时可能跨节点，尤其在切换期间。

停用代理执行 `/usr/bin/systemctl disable --now haproxy.service`；确需卸载再执行 `apt-get remove haproxy`，保留配置备份。若此前有其他 HAProxy 业务，应恢复原配置并校验重启，不能直接卸载。不要停止 C 的 `ipc-gateway-arbiter-c.service` 或删除 `/etc/ipc-gateway/ha-v2/C`。

本入口只代理网站和 HTTP Web API，不代表 MQTT、OPC UA 已获得相同的连接迁移能力。健康探针判断主身份和启动就绪，个别 PLC/点位错误不会仅凭它触发切换。

C 也是统一入口的单点：C 故障时可直连 A/B 排障；B/C 若都在 A 的同一宿主机，宿主断电仍会同时影响所有节点，不能用这种部署验证物理故障隔离。

## 附录：银河麒麟 V11 的 APT 包装器与独立目录部署

本次 C 的实际检查结果为 `/usr/bin/apt-get -> apt_wrapper`，而 `/usr/bin/apt-get.orig` 是保留的原始工具。包装器会调用麒麟的包管理适配逻辑；此前“下载成功”并未让原生 `dpkg-query` 找到 HAProxy，原路径下也没有二进制。此前日志中的临时缓存 `/var/tmp/kysdk-package/apt-cache/haproxy_2.8.5-ok1_amd64.deb` 在当前原生窗口中也无法访问，下面重新下载到明确的持久目录。

这台 C 不再使用第 3 节的普通 APT 安装步骤，采用以下独立目录部署。`apt-get.orig` 仅负责从本机已配置的软件源下载官方包，`dpkg-deb -x` 仅解包，不执行包安装脚本。原生软件包数据库不会因此登记 HAProxy；后续以程序验证及手动注册的服务状态为准。

### A. 下载、解包并验证程序

在第 2 节的 C 原生 root 窗口完整执行。目录已有多个版本的安装包时会停止，不随意选择其中一个。

```bash
(
  set -eu
  install -d -m 755 /opt/ipc-haproxy/packages
  cd /opt/ipc-haproxy/packages
  /usr/bin/apt-get.orig download haproxy
  set -- haproxy_*_amd64.deb
  if [ "$#" -ne 1 ] || [ ! -f "$1" ]; then
    echo 'Expected exactly one HAProxy amd64 package; check the download output.' >&2
    exit 1
  fi
  dpkg-deb -f "$1" Package Version Architecture Depends
  dpkg-deb -x "$1" /opt/ipc-haproxy/app
  ipc_haproxy_bin=$(find /opt/ipc-haproxy/app -type f \
    \( -path '*/sbin/haproxy' -o -path '*/bin/haproxy' \) -print -quit)
  test -n "$ipc_haproxy_bin"
  printf 'HAProxy executable: %s\n' "$ipc_haproxy_bin"
  ldd "$ipc_haproxy_bin"
  "$ipc_haproxy_bin" -v
)
```

如果提示找不到软件包，可先执行 `/usr/bin/apt-get.orig update`，成功后重新执行本段。如果再次出现 `_apt` 无法访问下载目录、改用 root 的下载警告，以最终下载是否成功为准。

只有 `ldd` 不再显示缺失的动态库、且 `-v` 正常显示版本，才能继续。如果出现 `not found`、GLIBC/OpenSSL 版本错误或其他运行错误，保留该输出继续核对依赖，不能据此把兼容环境的 libc 或动态加载器复制到原生系统。

以下服务示例按检查结果为 `/opt/ipc-haproxy/app/usr/sbin/haproxy` 编写。若上面的实际路径不同，服务中两处二进制路径也要使用实际值。

### B. 创建账号并写入代理配置

```bash
getent group haproxy >/dev/null || groupadd --system haproxy
id haproxy >/dev/null 2>&1 || useradd --system --gid haproxy \
  --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin haproxy
install -d -m 750 -o root -g haproxy /etc/haproxy
chown -R root:root /opt/ipc-haproxy/app
chmod -R a+rX /opt/ipc-haproxy/app
```

按正文第 4、5 节核对 A/B。**首次独立部署时 `/etc/haproxy/haproxy.cfg` 不存在是正常情况，无需备份，直接创建。**在 C 的原生 root 窗口继续执行下面完整配置：

```bash
if [ -f /etc/haproxy/haproxy.cfg ]; then
  cp -a /etc/haproxy/haproxy.cfg "/etc/haproxy/haproxy.cfg.before-ipc-$(date +%Y%m%d-%H%M%S)"
fi
cat > /etc/haproxy/haproxy.cfg <<'HAPROXY_CONFIG'
global
    log stdout format raw local0
    maxconn 2048
    user haproxy
    group haproxy

defaults
    log global
    mode http
    option httplog
    option dontlognull
    option http-server-close
    retries 0
    retry-on none
    timeout connect 2s
    timeout client 5m
    timeout server 5m
    timeout http-request 30s
    timeout http-keep-alive 10s
    timeout check 1s

frontend ipc_gateway_entry
    bind 0.0.0.0:8080
    acl exactly_one_active nbsrv(ipc_gateways) eq 1
    http-request deny deny_status 503 unless exactly_one_active
    http-request del-header Forwarded
    http-request set-header X-Forwarded-For %[src]
    http-request set-header X-Forwarded-Proto http
    default_backend ipc_gateways

backend ipc_gateways
    option httpchk
    http-check send meth GET uri /health/ha-active ver HTTP/1.1 hdr Host 192.168.164.128:8080
    http-check expect status 200
    default-server inter 1s fall 2 rise 2 on-marked-down shutdown-sessions
    server gateway_a 192.168.110.154:5184 check
    server gateway_b 192.168.164.131:5184 check

listen ipc_gateway_stats
    bind 127.0.0.1:8404
    mode http
    stats enable
    stats uri /
    stats refresh 2s
    stats show-legends
HAPROXY_CONFIG
chown root:haproxy /etc/haproxy/haproxy.cfg
chmod 640 /etc/haproxy/haproxy.cfg
```

独立目录部署的语法检查命令为：

```bash
/opt/ipc-haproxy/app/usr/sbin/haproxy -c -f /etc/haproxy/haproxy.cfg
```

### C. 注册原生 systemd 服务

本次已确认 systemd 对 `haproxy.service` 优先加载 `/opt/kare/lib/systemd/system/haproxy.service`，其启动命令为 KARE 的 `haproxy.bin start`；旧开机链接也指向该文件，因此会与我们创建的同名源文件冲突。这里改用专用名称 `ipc-gateway-proxy.service`。

在同一个原生 root 窗口执行以下整段。它会确认程序路径并再次校验配置，停用匹配到的旧 KARE HAProxy 服务，然后创建和启动新服务。已有同名自定义服务时先备份。

```bash
(
set -eu
mapfile -t ipc_haproxy_bins < <(find /opt/ipc-haproxy/app -type f \
  \( -path '*/sbin/haproxy' -o -path '*/bin/haproxy' \) -print)
if [ "${#ipc_haproxy_bins[@]}" -ne 1 ]; then
  echo 'Expected exactly one HAProxy executable.' >&2
  exit 1
fi
ipc_haproxy_bin=${ipc_haproxy_bins[0]}
"$ipc_haproxy_bin" -c -V -f /etc/haproxy/haproxy.cfg
if [ "$(/usr/bin/systemctl show haproxy.service -p FragmentPath --value)" = /opt/kare/lib/systemd/system/haproxy.service ]; then
  /usr/bin/systemctl stop haproxy.service
  /usr/bin/systemctl disable haproxy.service
fi
if [ -f /etc/systemd/system/ipc-gateway-proxy.service ]; then
  cp -a /etc/systemd/system/ipc-gateway-proxy.service \
    "/etc/systemd/system/ipc-gateway-proxy.service.before-ipc-$(date +%Y%m%d-%H%M%S)"
fi
cat > /etc/systemd/system/ipc-gateway-proxy.service <<HAPROXY_UNIT
[Unit]
Description=IPC Gateway HAProxy
After=network-online.target
Wants=network-online.target
StartLimitIntervalSec=60
StartLimitBurst=5

[Service]
Type=simple
WorkingDirectory=/etc/haproxy
ExecStartPre=${ipc_haproxy_bin} -c -f /etc/haproxy/haproxy.cfg
ExecStart=${ipc_haproxy_bin} -W -f /etc/haproxy/haproxy.cfg
Restart=on-failure
RestartSec=5
LimitNOFILE=8192
TimeoutStopSec=30
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
HAPROXY_UNIT
chmod 644 /etc/systemd/system/ipc-gateway-proxy.service
/usr/bin/systemctl daemon-reload
/usr/bin/systemctl show ipc-gateway-proxy.service -p FragmentPath -p ExecStart
test "$(/usr/bin/systemctl show ipc-gateway-proxy.service -p FragmentPath --value)" = /etc/systemd/system/ipc-gateway-proxy.service
/usr/bin/systemctl reset-failed ipc-gateway-proxy.service
/usr/bin/systemctl restart ipc-gateway-proxy.service
/usr/bin/systemctl status ipc-gateway-proxy.service --no-pager -l
/usr/bin/systemctl enable ipc-gateway-proxy.service
)
```

随后按正文第 8、9 节访问统一入口和验收切换。这里使用 `-W` 启用 master-worker 模式，配置不含 `daemon`，因此以前台方式交给原生 systemd 管理。不在服务命令中使用仅供调试的 `-db` 参数。[HAProxy 2.8 管理手册](https://docs.haproxy.org/2.8/management.html)

独立目录部署后，状态、日志和重启分别使用 `systemctl status ipc-gateway-proxy.service`、`journalctl -u ipc-gateway-proxy.service` 和 `systemctl restart ipc-gateway-proxy.service`。停用使用 `/usr/bin/systemctl disable --now ipc-gateway-proxy.service`。因为没有通过软件包数据库安装，`apt-get remove haproxy` 不负责卸载这套独立部署；保留程序、配置和服务文件用于回退，确认不再使用后再按它们的明确路径单独移除。此过程不修改 C 的仲裁目录或仲裁服务。


## OPC UA TCP 统一入口

OPC UA 使用独立 TCP 端口，与本文件的 HTTP 代理分开配置。新版本支持分离本地监听和对外公布地址，并提供主身份与 UA 运行状态联合探针。完整步骤见 [OPC UA统一入口与主备切换](OPC%20UA统一入口与主备切换.md)，配置片段见 [haproxy-opcua.cfg.example](参考配置/haproxy-opcua.cfg.example)。
