# 轻量倍福 ADS 虚拟 PLC

用于 IPC Gateway 倍福驱动联调，无需安装 TwinCAT、Go 或其他运行环境。

## 直接使用

直接使用 `dist` 目录中的文件：

- `BeckhoffAdsSimulator.exe`：虚拟器主程序。
- `points.json`：通信和点位配置。
- `启动虚拟倍福PLC.bat`：双击启动脚本。

也可以直接双击 EXE。程序默认读取 **EXE 同目录** 下的 `points.json`；文件不存在时会自动生成示例配置。

修改配置后保存并重启程序。关闭控制台窗口或按 `Ctrl+C` 停止。

## 网关连接参数

默认配置对应：

| 网关参数 | 值 |
|---|---|
| 协议 | Beckhoff TwinCAT ADS / TCP |
| 主机 | `127.0.0.1` |
| TCP 端口 | `48898` |
| AMS NetId | `127.0.0.1.1.1` |
| ADS 端口 | `851` |

启动前请停止其他占用 TCP `48898` 的虚拟器。仅本机使用时，也可以把 `host` 改为 `127.0.0.1`。

## 点位配置

示例：

```json
{
  "host": "0.0.0.0",
  "port": 48898,
  "amsNetId": "127.0.0.1.1.1",
  "adsPort": 851,
  "adsState": 5,
  "deviceState": 0,
  "verbose": false,
  "points": [
    { "name": "GVL.bStart", "type": "Bool", "value": false },
    { "name": "GVL.iCounter", "type": "Int16", "value": 123 },
    { "name": "GVL.nSpeed", "type": "Float", "value": 25.5 },
    { "name": "GVL.sText", "type": "String", "length": 80, "value": "hello" },
    { "name": "GVL.aValues", "type": "Int32Array", "value": [1, 2, 3, 4] }
  ]
}
```

字段说明：

- `name`：网关标签地址中填写的完整符号名，不区分大小写。
- `type`：数据类型，应与网关标签类型一致。
- `value`：初始值；网关写入后，虚拟器保留最新值，重启后恢复为初始值。
- `length`：仅 String 使用，表示最大字符数，不包含结尾零；网关字符串长度应与其一致。
- `verbose`：`true` 时输出详细 ADS 请求日志，通常保持 `false`。
- `adsState`：`5` 表示 Run；通常不需要修改。

支持类型：

- 标量：`Bool`、`Int8`、`UInt8`/`Byte`、`Int16`、`UInt16`、`Int32`、`UInt32`、`Int64`、`UInt64`、`Float`、`Double`。
- 字符串：`String`，当前与网关一致使用 ASCII。
- 数组：`BoolArray`、`Int8Array`、`UInt8Array`/`ByteArray`、`Int16Array`、`UInt16Array`、`Int32Array`、`UInt32Array`、`Int64Array`、`UInt64Array`、`FloatArray`、`DoubleArray`。

数组元素数量由 `value` 数组自动确定，例如：

```json
{ "name": "GVL.aSpeed", "type": "FloatArray", "value": [1.5, 2.5, 3.5] }
```

## 已支持的网关通信

- AMS/TCP 动态端口注册与关闭。
- ADS Read State，默认返回 `Run`。
- ADS Read Device Info。
- 符号句柄创建、读取、写入和释放。
- ADS Sum Read / Sum Write / Sum ReadWrite。
- 符号版本及符号表上传信息的基础响应。

本工具用于点位轮询和读写联调，不是完整 TwinCAT Runtime，不执行 PLC 程序，也不提供主动变化订阅推送。

## 命令行

```bash
BeckhoffAdsSimulator.exe -config points.json
```

生成一份新的示例配置（不会覆盖已有文件）：

```bash
BeckhoffAdsSimulator.exe -init -config new-points.json
```

## 从源码编译

安装 Go 后，在本目录运行 `build.bat`，或执行：

```bash
go test ./...
go build -trimpath -ldflags="-s -w" -o dist/BeckhoffAdsSimulator.exe .
```

源码仅使用 Go 标准库，无第三方模块依赖。
