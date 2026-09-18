# Wayfinder Map: GsCan.NET 规格

> 地图=本文件；每个决策 ticket 是一个子 issue 文件（`issues/NN-<slug>.md`）。

## Destination

一份可交给实现的规格：GsCan.NET 是 .NET Standard 2.0 上的现代 gs_usb 主机库（本仓库就是这个库）。Windows 只封装 Schildkroet 的 candle.dll（P/Invoke + 现代 C# 外观），不自写 USB 栈。FlintCAN-FD 为第一等设备（双通道、CAN FD、硬件时间戳），并兼容其他标准 gs_usb 设备。实现本身不在这张地图里。

Handoff：[spec.md](spec.md)（`ready-for-agent`）。

## Notes

- 领域：gs_usb 主机库（Windows / candle.dll）
- 技能：制图与推进用 `grilling`、`domain-modeling`；事实用 `research`；API 外观用 `prototype`；模块界面用 `codebase-design`（深模块：小界面、大实现，candle.dll 细节藏在 P/Invoke 层后面）
- 术语：主机库用仓库根 `CONTEXT.md`；设备侧（Host TX / TX echo / Bus-off 等）只读 `D:\Code\StmProject\flintcan-fd\CONTEXT.md`，不要抄进来
- 参考固件与协议头：`D:\Code\StmProject\flintcan-fd\firmware\include\gs_usb.h`
- 站位：计划，不实现。本图走完的标志是规格可handoff，不是 NuGet 包已发布
- 平台：运行时绑定 Windows + candle.dll；netstandard2.0 是编译目标，不因此承诺 Linux SocketCAN 后端
- Tracker：本地 markdown，见 `docs/agents/issue-tracker.md`；本 effort 在 `.scratch/gscan-net-spec/`

## Decisions so far

<!-- 索引：每行一个已关闭 ticket：标题 + 链接 + 一句话答案 -->
- [「01 research：Schildkroet candle.dll 身份与 API 清单」](issues/01-research-candle-dll.md) — Schildkroet/CANgaroo 的 CandleApiDriver/api（LGPL-3、WinUSB、含 CAN FD）；无官方预编译 candle.dll，需自建；IDENTIFY/端接/TDC/滤波无头文件导出
- [「02 research：gs_usb 线协议与 FlintCAN-FD 主机可见子集」](issues/02-research-gs-usb-protocol.md) — 线协议在内核 `gs_usb.c`（无独立 uapi）；FlintCAN-FD 可见双通道 FD+HW ts+IDENTIFY+BERR+GET_STATE+TDC+BUS_OFF_RECOVERY，无端接/滤波；TX echo≠上总线，Channel stop 丢 Host TX 无 echo
- [「03 research：现有 candle/gs_usb 的 .NET 封装」](issues/03-research-existing-dotnet-wrappers.md) — 无可用 .NET 依赖；Candle.NET 仅作对照（无 FD、停更）；Schildkroet 无 C# 包装；公共界面从零设计
- [「06 research：candle.dll 对 gs_usb 的能力缺口」](issues/06-research-candle-vs-gsusb-gap.md) — FlintCAN 数据面 candle 基本覆盖；IDENTIFY/TDC/BUS_OFF_RECOVERY/自选 echo_id/BERR 模式开关 DLL 无导出；GET_STATE 导出但 BREQ 号与现协议不符
- [「04 grilling：程序集、NuGet 与核心类型命名」](issues/04-grilling-naming.md) — NuGet/程序集/命名空间均为 `GsCan`；类型 `Device`/`Channel`/`CanFrame`；公共名不出现 Candle
- [「05 grilling：仅 netstandard2.0 还是多目标」](issues/05-grilling-target-frameworks.md) — 只 `netstandard2.0`；公共面不许现代 TFM 专属成员；以后加 net10 另开票
- [「07 prototype：公共 API 外观草稿」](issues/07-prototype-public-api.md) — 工作外观：Device 拥有 Channel；TryRead 超时轮询；一个 CanFrame+Kind；整数 bitrate；资产在 `prototype/public-api`
- [「08 grilling：错误模型与 TX echo / Bus-off 主机语义」](issues/08-grilling-error-and-echo.md) — 配置抛异常、总线走帧；Echo=FIFO 且≠上总线；Stop 静默丢未完成 Send；不暴露 State/恢复；Overflow 是标志、BERR 是 Error 帧
- [「09 grilling：candle.dll 随 NuGet 再分发」](issues/09-grilling-native-packaging.md) — 单包 `GsCan` 内嵌自建动态库；RID + `.targets` 旁路拷贝；默认按文件名加载；LGPL 声明与对应源码
- [「10 grilling：第一版规格的 gs_usb 能力范围」](issues/10-grilling-v1-capability-scope.md) — v1：双通道 + CAN FD + 硬件时间戳 + Echo/Overflow/Error 帧 + 模式位；IDENTIFY/TDC/端接/滤波/State/恢复/自选 echo_id 本图范围外
- [「11 grilling：剩余雾如何收口」](issues/11-grilling-remaining-fog.md) — TryRead-only；整数 bitrate；无句柄/无热插拔事件；Send+TryRead 可跨线程；验收=实机 FlintCAN-FD 走通工作外观

## Not yet specified

（空。决策齐，可汇编 handoff 规格。）

## Out of scope

- 实现代码与 NuGet 发布本身——本图只出规格，实现另开会话
- 托管 WinUSB / 自写 gs_usb 协议栈——目的地已定为封装 candle.dll
- Linux SocketCAN 后端
- CAN 分析仪 GUI / 上位机应用
- CANopen / ISO-TP / UDS / DBC 协议栈
- 固件与硬件
- ZLG / Kvaser 等非 gs_usb 适配器
- IDENTIFY、TDC、软件端接、硬件滤波——candle.dll 无导出（见 10）
- GET_STATE / `Channel.State`、BUS_OFF_RECOVERY——08 已切且 DLL 缺口（见 10）
- 自选 echo_id、BERR 模式开关——DLL 无导出（见 10）
- 事件 / `Channel<T>` / `IObservable`——v1 只有 `TryRead`（见 11）
- 原始 bit timing——只有整数 bitrate（见 11）
- 底层 candle 句柄——公共面不出现（见 11）
- 与 python-can / TSMaster 行为对齐（见 11）
- 模拟设备、对照 python-can——留给实现会话（见 11）
