# GsCan.NET v1 — gs_usb 主机库

Status: ready-for-agent

术语以仓库根 `CONTEXT.md` 为准。设备侧（Host TX、TX echo、Bus-off 等）只引用 FlintCAN-FD 的术语表，不在本规格改写。

## Problem Statement

Windows 上要用托管代码接入 gs_usb 设备（第一等是 FlintCAN-FD：双通道 CAN FD、硬件时间戳），现成可用的只有 C 的 candle API 源码和 python-can。NuGet 上没有能打 CAN FD、又对得上这份 native 的 .NET 库。调用方不想自己 P/Invoke，也不想再写一套 USB 栈。

## Solution

交付一个 .NET Standard 2.0 库 **GsCan**：公共面是 `Device` / `Channel` / `CanFrame`。Windows 上封装从 Schildkroet CANgaroo `api/` 自建的动态库（经 WinUSB 说 gs_usb）。调用方发现设备、开通道、按整数 bitrate 启动、收发帧；Echo、Overflow、总线错误都是 `CanFrame` 的种类或标志，不是「上了总线」的成功回报。

## User Stories

1. As a .NET 主机开发者, I want 用 NuGet 引用 `GsCan`, so that 不必自己绑 native 或找 C 头文件。
2. As a .NET Framework 4.6.1+ 调用方, I want 消费 `netstandard2.0` 资产, so that 不必升到现代 TFM 才能接入 FlintCAN-FD。
3. As a 现代 .NET 调用方, I want 同一个 `netstandard2.0` 包, so that 不必等第二套 TFM。
4. As a FlintCAN-FD 用户, I want 一块双通道适配器被看成一个 Device、两路 Channel, so that 可以同时用 CAN1 和 CAN2。
5. As a FlintCAN-FD 用户, I want 在 Channel 上开 CAN FD（仲裁 bitrate + data bitrate）, so that 能发 64 字节 FD 帧。
6. As a 只用经典 CAN 的用户, I want 只设 `Bitrate`、不设 `DataBitrate`, so that 经典设备（CANable / candleLight）也能用同一套 API。
7. As a 主机开发者, I want `Device.List` 列出当前插着的适配器, so that 我能选对那一块 FlintCAN-FD。
8. As a 主机开发者, I want `List` 给出每块 Device 的路径和 Channel 数量, so that 打开前就知道是单路还是双路。
9. As a 主机开发者, I want `Device.Open` 之后 Channel 已经在 `Channels` 里, so that 不必再对每一路做一次 Open。
10. As a 主机开发者, I want `Device` 实现 `IDisposable`, so that using 结束时释放 USB 句柄。
11. As a 主机开发者, I want 对某一路 `Start(ChannelOptions)` 才上总线, so that 另一路可以仍停着。
12. As a 主机开发者, I want `Stop` 把该路从总线上拿下来, so that 可以改 bitrate 再 Start。
13. As a 主机开发者, I want `Send` 立刻返回, so that 收包循环不会被发送卡住。
14. As a 主机开发者, I want 用 `TryRead` 带超时收下一帧, so that 不必上事件或 `IObservable`。
15. As a 主机开发者, I want USB IN 的分路藏在库里, so that 我只对 `Channel.TryRead` 说话，不必自己看帧属于哪一路。
16. As a 主机开发者, I want 经典帧和 FD 帧都是 `CanFrame`, so that 收包循环只有一个类型。
17. As a 主机开发者, I want 用 `CanFrame.Classic` / `CanFrame.Fd` 构造要发的帧, so that 不必填满所有标志位。
18. As a 主机开发者, I want `IsFd`、`BitRateSwitch`、`ErrorStateIndicator`、`Extended`、`Remote` 落在同一帧上, so that FD 与经典的差别是字段而不是类型。
19. As a 主机开发者, I want 硬件时间戳在 `CanFrame.TimestampMicroseconds`, so that 不必另开时间 API。
20. As a 主机开发者, I want `TryRead` 拿到的 Echo 表示这次 `Send` 在设备上结束, so that 我不会把它当成已经上了总线。
21. As a 主机开发者, I want 同一 Channel 上 Echo 按 FIFO 对 `Send`, so that 在 echo_id 被 DLL 写死为 0 时仍能数清完成了几帧。
22. As a 主机开发者, I want `Stop` 之后未完成的 `Send` 不再出现 Echo, so that 行为与设备侧 Channel stop 一致。
23. As a 主机开发者, I want `Stop` 不抛、不合成取消帧, so that 关机不是错误路径。
24. As a 主机开发者, I want 若在乎发送完成则先把 Echo 收完再 `Stop`, so that 我清楚哪些 Send 被丢掉。
25. As a 主机开发者, I want 打开/`Start`/立刻失败的 `Send` 抛 `GsCanException`, so that 配置错误在热路径之外。
26. As a 主机开发者, I want `TryRead` 超时返回 `false`, so that 收包循环里不必 try/catch。
27. As a 主机开发者, I want 总线错误以 `Kind = Error` 的 `CanFrame` 出现, so that Bus-off 和 BERR 走同一条收包路径。
28. As a 主机开发者, I want 库不区分 FlintCAN-FD 的 Hardware bus-off 与主机看见的 Bus-off, so that 公共面不会假装能看见固件内部状态。
29. As a 主机开发者, I want 没有 `Channel.State` 和总线恢复 API, so that 我不会去调 DLL 里 BREQ 号就不对的 GET_STATE。
30. As a 主机开发者, I want Overflow 是某帧上的 `Overflow` 标志, so that RX 丢包不会变成异常或事件。
31. As a 主机开发者, I want 能设 ListenOnly / Loopback / OneShot, so that 可以只听、自测、或禁止控制器自动重传。
32. As a 主机开发者, I want 一个线程 `TryRead`、另一个线程 `Send` 同一 Channel, so that 收发不必挤在一个循环里。
33. As a 主机开发者, I want `Start` / `Stop` / `Dispose` 与收发互斥, so that 生命周期操作不会和热路径打架。
34. As a 主机开发者, I want 同一 Channel 上不能两个 `TryRead` 同时进行, so that 分路队列不会被两个读者拆开。
35. As a 主机开发者, I want 使用中拔掉设备时进行中的调用以 `GsCanException` 失败, so that 我不必等一个永远不到的帧。
36. As a 主机开发者, I want 没有设备到达/离开事件, so that 公共面保持小；需要时再 `List`。
37. As a 主机开发者, I want `List` 只是当时快照, so that 我不会以为枚举结果会自动更新。
38. As a 主机开发者, I want 公共类型、命名空间、包 id 都不出现 Candle, so that 我学的是 GsCan 而不是某份 C API。
39. As a 主机开发者, I want 安装 `GsCan` 之后 native 已在输出目录旁, so that `DllImport` 在 Framework 和现代 SDK 上都能找到动态库。
40. As a 主机开发者, I want 不必配置 native 路径, so that 公共面不泄漏加载细节。
41. As a 合规使用者, I want 包内带 LGPL/GPL 文本和对应 `api/` 源码获取方式, so that 我可以替换那份动态库。
42. As a 合规使用者, I want native 是独立动态库而不是静态编进托管程序集, so that 满足 LGPL 的共享库替换路径。
43. As a FlintCAN-FD 用户, I want 验收时双通道 FD 能收发、能看到 Echo、Stop 后无残留 Echo, so that 实现是否合格有一块实机标准。
44. As a 其他 gs_usb 设备用户, I want 同一 API 在单通道经典设备上仍能 List/Open/Start/Send/TryRead, so that 库不是 FlintCAN 私有协议。
45. As a 主机开发者, I want 不出现 Identify / TDC / 软件端接 / 硬件滤波 API, so that 我不会去调 DLL 没有的导出。
46. As a 主机开发者, I want 不能自选 echo_id, so that 我不会按 python-can 的 echo_id 模型去写对账。
47. As a 主机开发者, I want 不能关掉 BERR reporting, so that 错误帧该来就会来。
48. As a 库实现者, I want 公共界面小、candle 细节藏在后面, so that 以后换 native 文件名或修 GET_STATE BREQ 不必改调用方。
49. As a 库实现者, I want 只打 `netstandard2.0`, so that 公共成员不能偷偷用现代 TFM 才有的类型。
50. As a 后续维护者, I want 不把 CANgaroo 那个 GPL-2 应用打进包, so that 托管库的许可证不被应用的 GPL 拖死。

## Implementation Decisions

- **一个模块**：`GsCan`（NuGet / 程序集 / 根命名空间）。深模块：调用方只学 `Device`、`Channel`、`CanFrame`、`GsCanException`。P/Invoke、WinUSB、分路、FIFO 对账都在实现里。
- **目标框架**：只 `netstandard2.0`。公共界面必须能在这一份上表达。以后加 `net10.0` 另开规格，且不得塞进 netstandard2.0 表达不了的公共成员。
- **Native**：从 Schildkroet CANgaroo `api/` 自行编译动态库（LGPL-3.0-or-later），WinUSB 后端。无官方预编译。不附带 GPL-2 的 CANgaroo 应用。动态链接、可被用户替换。包内附 GPL/LGPL 文本及对应源码或等价获取方式。
- **打包**：单一 NuGet `GsCan`。native 放 `runtimes/win-x86/native` 与 `runtimes/win-x64/native`，并用 `.targets` 拷到输出目录旁。默认按文件名加载。公共面无路径配置。输出文件名留给实现。
- **公共名**：不出现 Candle / candle.dll。
- **工作外观**（来自已接受的 prototype/public-api 草稿；08/10/11 补了语义，类型形状如下）：

```csharp
namespace GsCan
{
    public sealed class Device : IDisposable
    {
        public static IReadOnlyList<DeviceInfo> List();
        public static Device Open(DeviceInfo info);
        public IReadOnlyList<Channel> Channels { get; }
        public void Dispose();
    }

    public sealed class DeviceInfo
    {
        public string Path { get; }
        public int ChannelCount { get; }
    }

    public sealed class Channel
    {
        public Device Device { get; }
        public int Index { get; }
        public void Start(ChannelOptions options);
        public void Stop();
        public void Send(CanFrame frame);
        public bool TryRead(out CanFrame frame, int timeoutMilliseconds);
    }

    public sealed class ChannelOptions
    {
        public int Bitrate { get; set; }
        public int? DataBitrate { get; set; } // null = 经典 only
        public bool ListenOnly { get; set; }
        public bool Loopback { get; set; }
        public bool OneShot { get; set; }
    }

    public enum CanFrameKind { Rx, Echo, Error }

    public readonly struct CanFrame
    {
        public uint Id { get; }
        public byte[] Data { get; }
        public CanFrameKind Kind { get; }
        public bool Extended { get; }
        public bool Remote { get; }
        public bool IsFd { get; }
        public bool BitRateSwitch { get; }
        public bool ErrorStateIndicator { get; }
        public bool Overflow { get; }
        public uint TimestampMicroseconds { get; }
        public static CanFrame Classic(uint id, byte[] data, bool extended = false);
        public static CanFrame Fd(uint id, byte[] data, bool extended = false, bool bitRateSwitch = true);
    }

    public sealed class GsCanException : Exception { }
}
```

- **Channel 生命周期**：随 `Device.Open` 创建，不单独 Open/Dispose。`Start` 才上总线。USB IN 是整机一条，`TryRead` 按 Channel 分路。
- **失败分层**：打开 / `Start` / 立刻失败的 `Send` → `GsCanException`。`TryRead` 超时 → `false`。总线状况不是异常。
- **Echo**：`Send` 立刻返回；Echo 只从 `TryRead` 出现。Echo ≠ 上了总线。DLL 强制 echo_id = 0，同一 Channel 按 FIFO 对 `Send`。不能按 id 认人。
- **Stop**：void、不抛。未对上的 `Send` 不再出 Echo。不合成取消帧。
- **Bus-off / BERR / Overflow**：只转发 `Kind = Error` 的帧（含 `CAN_ERR_BUSOFF`）。不区分 Hardware bus-off。无 `Channel.State`、无恢复 API。Overflow 是帧标志。无 BERR 开关、无事件。
- **第一版能力**：双 Channel、CAN FD（含 data bitrate）、硬件时间戳、Echo/Overflow/Error 帧、ListenOnly/Loopback/OneShot、BERR 作为 Error 帧。
- **热插拔**：无到达/离开事件。`List` 是当时快照。使用中断开 → 进行中的调用以 `GsCanException` 失败；不合成帧。
- **线程**：同一 `Channel` 上 `Send` 与 `TryRead` 可以跨线程。`Start` / `Stop` / `Dispose` 必须与收发互斥。同一 `Channel` 上两个 `TryRead` 不支持。`List` / `Open` 不保证多线程。
- **实现者须知（不进公共面）**：Schildkroet 头文件里 `GET_STATE` 的 BREQ 与现 gs_usb/FlintCAN-FD 不一致（12 vs 14）。v1 不调用、不暴露它。修不修这份自建动态库的 BREQ 不在本规格。

## Testing Decisions

- **唯一测试缝**：公共模块 `GsCan`（`Device` / `Channel` / `CanFrame` / `GsCanException`）。只测调用方能看见的行为，不测 P/Invoke、WinUSB、分路队列内部、FIFO 计数器实现。
- **不要**在 candle 动态库或 USB 控制请求上开第二道缝。假设备若存在，必须是同一公共界面上的适配器，且本规格不要求实现假设备。
- **好测试**：给定 `Start`/`Send`/`TryRead`/`Stop` 序列，断言帧的 `Kind`、FIFO Echo 数量、Stop 后无残留 Echo、超时返回 `false`、配置失败抛 `GsCanException`。不断言 native 文件名或 BREQ 编号。
- **验收（必须）**：实机 FlintCAN-FD 走通工作外观——双通道 CAN FD 收发、Echo 按 FIFO 出现、`Stop` 后无残留 Echo。
- **本规格不要求**：模拟 Device、对照 python-can / TSMaster。那些留给实现会话。
- **Prior art**：仓库目前没有测试。新测试以本缝为唯一入口。

## Out of Scope

- 实现会话里的编码与 NuGet 发布操作细节之外的「再设计」
- 托管 WinUSB / 自写 gs_usb 协议栈
- Linux SocketCAN 后端
- CAN 分析仪 GUI / 上位机应用
- CANopen / ISO-TP / UDS / DBC
- 固件与硬件
- ZLG / Kvaser 等非 gs_usb 适配器
- IDENTIFY、TDC、软件端接、硬件滤波
- GET_STATE / `Channel.State`、BUS_OFF_RECOVERY
- 自选 echo_id、BERR 模式开关
- 事件 / `Channel<T>` / `IObservable`
- 原始 bit timing
- 底层 candle 句柄与 native 路径配置
- 与 python-can / TSMaster 行为对齐
- 模拟设备、对照 python-can
- 第二套 TFM（`net10.0` 等）

## Further Notes

- 决策索引：[map.md](map.md)。
- 设备侧语义以 FlintCAN-FD `CONTEXT.md` 为准：TX echo 不是上了总线；Channel stop 丢未完成 Host TX 且不产生 echo。
- 第一等设备是 FlintCAN-FD；其他标准 gs_usb 设备保持同一公共面可用，不特化私有协议。
- 抛掷型外观草稿在 git 分支 `prototype/public-api`，不是本仓库 main 上的实现。
