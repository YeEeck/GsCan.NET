# Wayfinder Map: GsCan.NET 规格

> 地图=本文件；每个决策 ticket 是一个子 issue 文件（`issues/NN-<slug>.md`）。

## Destination

一份可交给实现的规格：GsCan.NET 是 .NET Standard 2.0 上的现代 gs_usb 主机库（本仓库就是这个库）。Windows 只封装 Schildkroet 的 candle.dll（P/Invoke + 现代 C# 外观），不自写 USB 栈。FlintCAN-FD 为第一等设备（双通道、CAN FD、硬件时间戳），并兼容其他标准 gs_usb 设备。实现本身不在这张地图里。

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
- [「02 research：gs_usb 线协议与 FlintCAN-FD 主机可见子集」](issues/02-research-gs-usb-protocol.md) — 线协议在内核 `gs_usb.c`（无独立 uapi）；FlintCAN-FD 可见双通道 FD+HW ts+IDENTIFY+BERR+GET_STATE+TDC+BUS_OFF_RECOVERY，无端接/滤波；TX echo≠上总线，Channel stop 丢 Host TX 无 echo

## Not yet specified

- 接收模型（阻塞轮询 / 事件 / `Channel<T>` / `IObservable`）
- 位时序怎么交给调用方（直接 bitrate vs 原始 bit timing）
- 是否向高级用户暴露底层 candle 句柄
- 热插拔
- 与 python-can、TSMaster 的行为对齐做到哪一层
- 测试策略（实机 FlintCAN-FD / 模拟 Device / 对照 python-can）
- 线程模型与同步上下文

## Out of scope

- 实现代码与 NuGet 发布本身——本图只出规格，实现另开会话
- 托管 WinUSB / 自写 gs_usb 协议栈——目的地已定为封装 candle.dll
- Linux SocketCAN 后端
- CAN 分析仪 GUI / 上位机应用
- CANopen / ISO-TP / UDS / DBC 协议栈
- 固件与硬件
- ZLG / Kvaser 等非 gs_usb 适配器
