# Modbus UDP 与数据格式配置

## 添加 UDP 设备

1. 在设备管理中新建通道，选择内置 **Modbus UDP** 驱动。
2. 在该通道下新建设备，填写主机 IP、UDP 端口（默认 502）、Unit ID（默认 1）和超时。
3. 按设备手册选择“数据格式”，新设备默认 ABCD。
4. 添加标签。地址和数据类型沿用现有 Modbus 规则，例如 `00001` 为线圈、`10001` 为离散输入、`30001` 为输入寄存器、`40001` 为保持寄存器；这些传统引用地址的第一项映射到协议偏移 0，也可使用明确的 `HR0`、`IR0` 等前缀地址。

传输字段固定为 UDP。Modbus TCP 仍使用独立 TCP 通道，已有协议枚举值保持不变，UDP 追加为新协议。UDP 使用 **MBAP 头 + PDU**，一个请求/响应各对应一个 UDP 数据报；不是带 CRC 的 RTU-over-UDP 透传模式。

支持功能码 01/02/03/04 读取、05/06 单点写入、15/16 多点写入；支持数组、寄存器位访问、固定长度字符串与批读合并/拆分。线圈/寄存器请求按原有上限分段，地址异常在批读中隔离到对应标签。

UDP 无 TCP 握手，建立本地会话不代表远端已响应；需要以实际采集结果确认设备可用。每台设备独立异步通信，响应等待可取消且受超时限制，迟到/重复或错误 Unit ID 的报文不会作为当前请求结果。写入不自动重发；写入超时可能是确认报文丢失，不能据此断定远端未执行。

## 四种数据格式

TCP、UDP、RTU、ASCII 的设备配置统一提供 **ABCD、BADC、CDAB、DCBA**。以 32 位数值的标准字节 `12 34 56 78` 为例：

| 数据格式 | 设备寄存器原始字节 | 处理 |
| --- | --- | --- |
| ABCD | `12 34 56 78` | 高字、高字节在前 |
| BADC | `34 12 78 56` | 每个寄存器内交换字节 |
| CDAB | `56 78 12 34` | 低字在前，寄存器内字节不变 |
| DCBA | `78 56 34 12` | 同时交换字序和字节序 |

读取前转换为标准布局，写入时执行反向转换。格式对 Int16/UInt16、Int32/UInt32、Int64/UInt64、Float、Double 及对应数组生效；数组逐元素转换，批读按每个标签的类型转换，不能把整个批读区或整个数组倒序。

16 位数据只有一个寄存器：ABCD/CDAB 相同，BADC/DCBA 相同。64 位数据按四个 16 位寄存器处理：CDAB 对应 `GH EF CD AB`，DCBA 对应 `HG FE DC BA`（A～H 表示标准的八个字节）。请按设备手册确认 64 位布局。

字符串保持既有 ASCII 字符字节排列；Bool、BoolArray 和寄存器 `.bit` 地址保持物理位编号，不随数值格式交换。字符串长度、补零、截断及奇数长度末尾字节保护沿用已有逻辑。

配置保存于 `DriverOptionsJson` 的 `modbusDataFormat` 字段，无需修改项目 Protobuf 契约。未配置时使用 ABCD；旧配置显式选择 `LowWordFirst` 时映射为 CDAB，显式数据格式优先。旧版本的 Modbus 实际编解码固定为 ABCD，因此曾选择低字在前却仍按 ABCD 工作的设备，升级后应按实际手册核对或显式选 ABCD。

## 实现位置

- `src/IPC.Gateway.Core/ModbusUdp/`：UDP 客户端和异步数据报传输。
- `src/IPC.Gateway.Core/ModbusTcp/ModbusRegisterFormat.cs`：公共数值布局转换。
- `src/IPC.Gateway.Core/ModbusTcp/`、`src/IPC.Gateway.LegacyProtocolPlugins/ModbusTcp/`：网络/串口编解码及批读。

接口和报文参考：[Modbus 官方协议规范](https://www.modbus.org/docs/Modbus_Application_Protocol_V1_1b3.pdf)、[NModbus IP 报文实现](https://github.com/NModbus/NModbus/blob/master/NModbus/IO/ModbusIpTransport.cs)。

## 验证记录（2026-11-11）

后端通过 317 项独立检查：UDP 八种功能码、同步/异步读写、32/64 位整数和浮点、分段数组、固定长度字符串边界、寄存器位保护、批读合并及错误地址拆分、MBAP 长度/协议号/功能码/写回显检查、迟到/重复/错误 Unit ID 报文、丢包超时、取消、写入不重发、多设备互不阻塞、协议目录和 Protobuf 配置，以及现有 TCP 读写回归。

四种格式使用已知原始字节矩阵验证 16/32/64 位数值及数组，包含网络单读、同步/异步混合类型批读、RTU/ASCII 共用编解码和实际批读解码器。RTU/ASCII 没有连接真实串口设备。

前端通过 14 项检查：UDP 默认值、自定义端口保留、类型列表与排序、数据格式选择保存、旧字序显示映射，以及中英文诊断提示。前后端构建通过；前端仍有原有大文件体积提示。测试使用回环地址的独立临时服务，没有连接用户现场设备、修改业务数据库或发布服务。
