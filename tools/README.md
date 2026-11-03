# tools — CAN 调试工具

`tools/` 放实机配套程序，面向 Windows + gs_usb 适配器。CanBench、CanBenchLive、
CanBenchTest 都直接引用 `src/GsCan`，不再各自声明 P/Invoke；CanProbe 保留裸 interop
作对照；CanFrame.ps1 是独立脚本。

`candle_api.dll` 不随工具复制：`tools/Directory.Build.targets` 在构建后把
`src/GsCan/runtimes/win-x64/native/candle_api.dll` 放到每个程序的输出目录，
和 GsCan 库用的是同一份。

## 一览

| 工具 | 形态 | 用途 |
|---|---|---|
| [CanBench](CanBench/) | WinForms 窗口 | 命令台：`.list`/CSV 导入导出、逐条备注、按行/周期发送、Trace + Latest、ID 过滤、Trace 存 CSV |
| [CanBenchLive](CanBenchLive/) | 控制台 | 用 CanBench 的传输层跑一次实机收发：发一帧，看 Echo/应答/错误 |
| [CanBenchTest](CanBenchTest/) | 控制台 | 离屏自检：`.list`/CSV 往返与逐字节对齐、Latest 聚合、ID 过滤、周期统计；不需要硬件 |
| [CanProbe](CanProbe/) | 控制台 | 刻意绕过 GsCan 的裸 `candle_api` 探针：通道/波特率/ID/周期/错误帧；自带一个可替换的示例自检序列 |
| [CanFrame.ps1](CanFrame.ps1) | PowerShell | 按位域拼/解帧的示例（某电阻模拟卡的 29 位 ID 与 float 编码），结果可直接贴进 CanBench / `.list` |

## 构建与运行

```powershell
dotnet build tools/CanBench/CanBench.csproj -c Release
.\tools\CanBench\bin\Release\net10.0-windows\win-x64\CanBench.exe

dotnet build tools/CanBenchLive/CanBenchLive.csproj -c Release
.\tools\CanBenchLive\bin\Release\net10.0-windows\win-x64\CanBenchLive.exe

# 不需要硬件
dotnet run --project tools/CanBenchTest/CanBenchTest.csproj -c Release

dotnet build tools/CanProbe/CanProbe.csproj -c Release
.\tools\CanProbe\bin\Release\net10.0\win-x64\CanProbe.exe --ch=0 --ms=3000
```

整个解决方案一起构建（工具也挂在 `GsCan.sln` 的 `tools` 目录下）：

```powershell
dotnet build GsCan.sln -c Release
```

## 设备占用

同一块适配器同时只能被一个程序打开。`SET_DEVICE_MODE`（native error 10）表示设备被占用：
先关掉 GsCan View 或另一个工具，再点「刷新」。

## 示例数据

`samples/` 下有两份 `.list` 样例（每条 76 字节）：

- `commands.list` —— 42 条命令，`CanBenchTest` 用它做逐字节往返校验。
- `bench.list` —— 32 条手工整理的联调序列，适合在 CanBench 里逐条发。

CanBench 可以直接打开任意一份。

## 许可

工具源码随仓库发布。`candle_api.dll` 仍是 LGPL-3.0-or-later，以动态链接方式使用，
可以按许可证条款替换。
