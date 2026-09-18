# 09 grilling：candle.dll 随 NuGet 再分发

Type: grilling
Status: resolved
Blocked by: 01

## Question

candle.dll 怎么进包、进谁的包？「01 research：Schildkroet candle.dll 身份与 API 清单」会给出许可证和位宽事实；本票决定分发策略。

要定：

- 许可证是否允许随 NuGet 附带二进制；不允许时调用方如何自行提供 DLL
- x86 / x64 如何布局（`runtimes/win-x86|win-x64/native` 还是并列目录）
- 加载策略：默认旁边搜、还是可配置路径
- 是否单独打一个 native 包，还是托管包内嵌

不讨论 Linux。不讨论自写 WinUSB。

## 产出

一条可写进规格的 native 分发与加载策略。

## Answer

2026-09-19 grilling，Q1 A。

规格条款：

- **一个 NuGet `GsCan`**，内嵌从 Schildkroet CANgaroo `api/` **自行编译**的动态库（无官方预编译资产）。不把 GPL-2 的 CANgaroo 应用编进包。
- **布局**：`runtimes/win-x86/native/` 与 `runtimes/win-x64/native/`，并提供 `.targets` 把 native 拷到输出目录旁，供 .NET Framework 与 `DllImport` 使用。
- **加载**：默认按文件名搜索。公共面不提供路径配置，不出现 Candle 名。
- **LGPL-3.0-or-later**：动态链接、可替换；包内附 GPL/LGPL 文本；提供对应 `api/` 源码或等价获取方式。
- 输出文件名（`candle.dll` vs 其他）留给实现。

`CONTEXT.md` 无新术语。
