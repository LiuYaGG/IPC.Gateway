# 倍福 ADS 读写客户端

基于 C#、.NET 10、WinForms 和倍福官方 `Beckhoff.TwinCAT.Ads 7.0.292` 包的独立读写客户端。

## 打开与运行

1. 安装 Visual Studio 2022，并启用“.NET 桌面开发”工作负载。
2. 安装 .NET 10 SDK。
3. 双击 `BeckhoffAdsTool.sln`。
4. 等待 NuGet 还原官方 ADS 包，然后按 F5 运行。

也可以在解决方案目录执行：

```powershell
dotnet restore
dotnet run --project src/BeckhoffAdsTool/BeckhoffAdsTool.csproj
```

## 使用方法

1. 输入目标 PLC 的 AMS NetId，例如 `192.168.1.20.1.1`。
2. 输入 ADS 端口，TwinCAT 3 第一个 PLC Runtime 通常为 `851`。
3. 点击“连接”。客户端会连接并读取 ADS 设备状态。
4. 输入 TwinCAT 符号地址，例如 `GVL.iCounter`、`MAIN.bStart` 或 `GVL.values`。
5. 选择数据类型，点击“读取”、“写入”或“写入并读回”。

## 数据格式

- 支持 Bool、8/16/32/64 位整数、Float、Double、String 及对应的一维数值数组。
- Bool 可输入 `true`、`false`、`1`、`0`。
- 数组使用逗号、分号或空白分隔，例如 `1,2,3,4`。
- 读取数组时，“数量/长度”表示元素数量。
- String 使用 ASCII 编码，“数量/长度”表示 PLC 中 STRING 的声明长度，不包含末尾零字节。默认 STRING(80) 应填写 `80`。
- 小数使用不变区域格式，小数点为 `.`。

## 源码结构

- `Program.cs`：应用入口。
- `MainForm.cs`：界面操作和异步流程。
- `MainForm.Designer.cs`：WinForms 界面布局。
- `AdsClientService.cs`：官方 AdsClient 的连接、状态、句柄和原始读写封装。
- `AdsValueCodec.cs`：ADS 小端字节编码与解码。

## 注意

- 本工具通过 ADS 符号句柄访问变量，PLC 中必须存在对应符号。
- 写入前请确认地址和数据类型，避免修改真实设备中的工艺值。
- 当前版本不支持 WSTRING、结构体和多维数组。
