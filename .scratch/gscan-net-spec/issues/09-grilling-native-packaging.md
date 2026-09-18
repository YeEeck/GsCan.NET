# 09 grilling：candle.dll 随 NuGet 再分发

Type: grilling
Status: open
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
