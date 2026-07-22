# IPC Gateway 免费协议仿真环境

安装目录：`D:\IPC-Simulators`。本环境不包含已经由用户准备好的 OPC UA、Virtual PLC、MQTT，也不包含串口协议。

## 已部署服务

| 协议 | 实现 | 许可证 | 监听地址 | 关键参数 |
|---|---|---|---|---|
| Modbus TCP | PyModbus | BSD-3-Clause | `0.0.0.0:1502/TCP` | Unit ID 1 |
| Siemens S7 | python-snap7 / Snap7 Server | MIT / LGPL | `0.0.0.0:1102/TCP` | Rack 0, Slot 1, DB1 |
| BACnet/IP | BACpypes3 | MIT | `<本机局域网IP>:47808/UDP` | Device Instance 599 |
| EtherNet/IP CIP | cpppo | GPL-3.0 | `0.0.0.0:44818/TCP+UDP` | Logix 风格标签 |
| SNMP | SNMPSim | BSD-2-Clause | `0.0.0.0:1161/UDP` | SNMPv2c, community `public` |
| DNP3 | OpenDNP3 3.1.2 | Apache-2.0 | `0.0.0.0:20000/TCP` | Master 1, Outstation 10 |

所有软件均可免费使用；其中 cpppo 是 GPL-3.0，适合本地测试，若将它或其衍生代码对外分发需遵守 GPL。

## 操作

以管理员 PowerShell 执行：

```powershell
& 'D:\IPC-Simulators\Start-All.ps1'
& 'D:\IPC-Simulators\Status.ps1'
& 'D:\IPC-Simulators\Stop-All.ps1'
```

Windows 登录启动任务名称为 `IPC Protocol Simulators`，它以当前用户的普通权限运行，不使用 SYSTEM。日志位于 `D:\IPC-Simulators\logs`，点位清单为 `D:\IPC-Simulators\points.csv`。

## 网关连接参数

- Modbus TCP：Host `127.0.0.1`，Port `1502`，Unit ID `1`。
- Siemens S7：Host `127.0.0.1`，Port `1102`，Rack `0`，Slot `1`。
- BACnet/IP：Device Instance `599`，UDP Port `47808`。同机客户端必须使用另一个本地 BACnet UDP 端口，不能同时绑定 47808。
- EtherNet/IP：Host `127.0.0.1`，Port `44818`。
- SNMP：Host `127.0.0.1`，Port `1161`，Version `v2c`，Community `public`。
- DNP3：Host `127.0.0.1`，Port `20000`；`dnp3LocalAddress=1`，`dnp3RemoteAddress=10`。建议初测关闭主动上送和周期扫描，仅按点读取。

DNP3 的 `BinaryOutput` 与 `AnalogOutput` 支持写命令。当前 OpenDNP3 3.1.2 的模拟量输出静态值按整数变体稳定互通，因此端到端写测试使用 Int32；Float/Double 命令能到达仿真站，但不作为当前网关组合的无损精度验收项。

## 网络边界

防火墙规则只对 Windows `Private` 配置文件和 `LocalSubnet` 放行，不对 Public/Tailscale/Cloudflare 公网入口开放。仿真协议通常无加密和认证，不应直接发布到公网。
