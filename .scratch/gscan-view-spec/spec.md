# GsCan View v1 — 轻量 CAN 查看器

Status: ready-for-agent

术语以仓库根 `CONTEXT.md` 为准。库的语义（Echo、Overflow、Start/Stop、`TryRead`）不在本规格改写。决策索引：`docs/adr/0001`–`0010`。

## Problem Statement

GsCan 已经能在 Windows 上打开 gs_usb Device、对每路 Channel 收发 CanFrame，但调用方仍是写代码的人。FlintCAN-FD（以及兼容的 CANable / candleLight）用户需要一个窗口：选设备、上总线、看见帧、发出去、把跟踪留下来。他们不要分析仪，也不要再包一层协议栈。

## Solution

交付 **GsCan View**：消费 GsCan 的单窗口桌面查看器。一块 Device、两路 Channel 对等；接收区可在 Trace 与 Latest 之间切换；Display Filter 只改看见什么；TxSlot 表负责单帧和周期发送；Log 把 Trace 存成 CSV（只写不打开）。界面中文，Echo / Trace / Latest / Kind 等词条保持英文。运行时只做 Windows x64。

## User Stories

1. As a FlintCAN-FD 用户, I want 打开 GsCan View 就看到一块窗口, so that 不必先写程序才能碰总线。
2. As a GsCan View 用户, I want 窗口标题是「GsCan View」, so that 不会把它当成库或当成 FlintCAN 私有工具。
3. As a GsCan View 用户, I want 界面是中文, so that 按钮和状态我能读懂。
4. As a GsCan View 用户, I want Echo、Trace、Latest、Kind 这些词保持英文, so that 和库的术语表对得上，不会被译成「发送成功」。
5. As a 主机用户, I want 顶栏用友好名称列出当前 `Device.List` 的快照（gs_usb、VID:PID、实例、Channel 数；完整 Path 在提示里）, so that 我能认出插着的那块适配器，而不必读 WinUSB 路径。
6. As a 主机用户, I want 启动窗口和点开顶部设备下拉时各 List 一次，刷新按钮仍可用, so that 刚插上的设备能出现；我明白列表仍不是热插拔事件。
7. As a 主机用户, I want 一次只打开一块 Device, so that 不会同时占着多块适配器。
8. As a FlintCAN-FD 用户, I want 打开之后看到两路 Channel 条, so that CAN1 和 CAN2 都在同一窗口里。
9. As a 单路 gs_usb 用户, I want 打开之后只看到一路 Channel 条, so that 不会出现点不动的第二路。
10. As a 主机用户, I want 打开 Device 之后 Channel 仍停着, so that Open 不等于上总线。
11. As a 主机用户, I want 对某一路单独 Start / Stop, so that 另一路可以仍停着或仍在跑。
12. As a 主机用户, I want 每路自己的仲裁 bitrate 下拉, so that 两路可以不同速率。
13. As a 主机用户, I want bitrate 只能选设备认的那几档（10k、20k、50k、83.333k、100k、125k、250k、500k、800k、1M）, so that Start 不会因为随手填的整数失败。
14. As a 主机用户, I want 仲裁默认 500k, so that 实验室最常见的速率不用改。
15. As a 主机用户, I want FD 默认关, so that 插上经典设备不会以 FD 模式 Start。
16. As a FlintCAN-FD 用户, I want 勾上 FD 之后出现数据段 bitrate, so that 我能开 CAN FD。
17. As a FlintCAN-FD 用户, I want 数据段只能选 1M / 2M / 4M，默认 2M, so that 不会选到 48MHz 时钟除不尽的速率。
18. As a 主机用户, I want ListenOnly 是一等控件且默认关, so that 实验室能发帧，同时我仍能一眼看到自己会不会 ACK。
19. As a 主机用户, I want Loopback 是一等控件且默认关, so that 自测时能开，接真总线时不会默默环回。
20. As a 主机用户, I want OneShot 在「更多」里且默认关, so that 需要禁止自动重传时找得到，日常不挡路。
21. As a 主机用户, I want Start 失败时状态栏出现 `GsCanException` 的说明、Channel 仍显示未在跑, so that 我知道没上总线。
22. As a 主机用户, I want Stop 不报错, so that 关机不是错误路径。
23. As a 接收用户, I want 接收区能在 Trace 和 Latest 之间切换, so that 既能看时序又能看每个键的最新一帧。
24. As a 接收用户, I want Trace 按到达顺序列出每一帧, so that 我能看见先后。
25. As a 接收用户, I want Trace / Latest 的列覆盖 CanFrame 对人有意义的字段：相对时间、Channel、Kind、ID、Extended、Remote、FD、BRS、ESI、Overflow、长度、数据十六进制, so that 库能看见的标志窗口里都能看见。
26. As a 接收用户, I want Kind 列写 Rx / Echo / Error, so that 我不会把 Echo 当成已经上了总线。
27. As a 接收用户, I want Overflow 在该行上标出来, so that 设备丢 RX 时不是一声不响。
28. As a 接收用户, I want Error 的数据仍只显示十六进制, so that payload 原值还在，窗口也不把 Data 列变成分析仪。
28a. As a 接收用户, I want Error 的 ID 列显示 ErrorClass（ACK / Stuff / Bus-off 等）, so that 我不必把 `040` 当成 CAN ID。
28b. As a 接收用户, I want 把鼠标放到 Error 的 ID 上看到一句中文 bring-up 提示（CNT 时含 TEC/REC）, so that 常见故障有提示，但不是物理诊断。
28c. As a 主机用户, I want Error 到达时状态栏 LastError 更新为「CHn ErrorClass：提示」（暂停或 Display Filter 藏掉该行也写；文本没变则不写）, so that 不必翻 Trace 才知道总线出了什么事。
28d. As a 发送用户, I want 只有硬故障的 ErrorClass 才停该路周期发送（Bus-off / ACK / Stuff / Form / Bit0 / Bit1 / CRC / Error-passive / Unknown）, so that Warning / Active / Restarted 不会把 TxSlot 掐掉。
29. As a 接收用户, I want 相对时间是该 Channel 本次 Start 之后的毫秒, so that 人对齐时序时不必读原始微秒计数。
30. As a Latest 用户, I want Rx / Echo 的键是 `(Channel, Kind, Id, Extended, Remote, IsFd)`，Error 的键是 `(Channel, Kind, ErrorClass)`, so that Loopback 下 Echo 与 Rx 分家，Stuff 与 Form 也不挤在一行。
31. As a Latest 用户, I want 每个键只留最新一帧并显示计数, so that bring-up 时刷屏也能看清谁还在说话。
32. As a Latest 用户, I want BRS / ESI / Overflow / 数据随最新那一帧更新、不进键, so that 同一 ID 上标志变化不会裂成许多行。
33. As a 接收用户, I want Display Filter 能按 Channel、Kind、标准/扩展、经典/FD、Remote 勾选, so that 我能只看关心的那一类。
34. As a 接收用户, I want 关掉 Kind=Echo 就不再看见 Echo, so that 不必另造「隐藏 Echo」控件。
35. As a 接收用户, I want ID 能填单个十六进制或闭区间（如 `100-1FF`）, so that 常见的按 ID 看不必上表达式语言。
36. As a 接收用户, I want Display Filter 只改窗口里看见什么, so that 我明白 USB 上该来的帧仍会来，库没有硬件滤波。
36a. As a 接收用户, I want Error 只认 Channel 与 Kind=Error 勾选（忽略 ID 区间、标准/扩展、经典/FD、Remote）, so that 只看 FD 或填 `100-1FF` 时 Bus-off 不会被藏掉。
37. As a 接收用户, I want 暂停只冻 Trace/Latest 的画面, so that 我能看清一行。
38. As a 接收用户, I want 暂停时后台仍 TryRead, so that 设备不会因为我看一行就 Overflow。
39. As a 接收用户, I want 暂停期间新帧不进 Trace/Latest，状态栏计暂停丢弃, so that 我知道冻住时错过了多少。
40. As a 接收用户, I want 清空只清窗口里的 Trace 和 Latest, so that 总线和 Channel 的 Start 状态不受影响。
41. As a 接收用户, I want Trace 始终跟着最新一行（滚动条钉在底部）, so that 新帧一到就能看见；要看清一行请暂停。
42. As a 接收用户, I want Trace 大约 10 万行封顶、超出丢最旧, so that 满负载开一夜不会把内存吃光。
43. As a 发送用户, I want 一张可添加/删除的 TxSlot 表（默认 1 行，最多约 16 行）, so that 可以同时挂多路要发的帧，又不必对着空行。
44. As a 发送用户, I want 每个 TxSlot 有 Channel、ID、Extended、Remote、FD、BRS、DLC、数据、周期毫秒、使能, so that 经典/FD、单次/周期都能配，长度由 DLC 显式决定。
45. As a 发送用户, I want 周期为 0 表示只发一次, so that 点一下和嘀嘀嘀是同一张表。
46. As a 发送用户, I want TxSlot 上没有 ESI, so that 我不会以为自己在控制对端错误状态。
47. As a 发送用户, I want 使能周期发送时按周期 `Send`, so that 实验室能持续刺激总线。
48. As a 发送用户, I want 该路 Stop 之后该路上的周期发送停掉, so that Stop 丢掉未完成 Send、不再出 Echo 的语义不被周期发送踩破。
49. As a 发送用户, I want 该路 ListenOnly 时该路上的 TxSlot 不能发, so that 不会撞上库的「Cannot send on a listen-only channel」。
50. As a 发送用户, I want 发出去之后在 Trace 里看到 Echo（若 Display Filter 没关掉 Kind=Echo）, so that 我知道这次 Send 在设备上结束了，而不是以为已经上了总线。
51. As a Loopback 用户, I want 同一 Send 在 Trace 里能同时看到 Echo 和 Rx, so that 自测时能把两种 Kind 分开。
52. As a 主机用户, I want 使用中拔掉设备时收发停、Device 被关掉、状态栏出现错误, so that 我不会对着一个已死的句柄点 Start。
53. As a 主机用户, I want 拔掉之后已经进 Trace/Latest 的帧还在, so that USB 线松了不等于跟踪被清空。
54. As a 主机用户, I want 拔掉之后不自动重开, so that 设备再插回来由我刷新并打开。
55. As a 主机用户, I want 关闭 Device 时停掉所有路的周期发送并 Stop 已启动的 Channel, so that 句柄释放前热路径已经停。
56. As a 主机用户, I want 把当前 Trace 存成 CSV, so that 能用表格软件拆这些帧。
57. As a 主机用户, I want CSV 写出 `TimestampMicroseconds` 原值以及 Channel、Kind、ID、标志、数据, so that Log 能和设备时间戳对上，而不是只留下相对毫秒。
58. As a 主机用户, I want 第一版不能打开一份 Log 来看, so that 这个窗口不会变成离线查看器。
59. As a 主机用户, I want 下次启动时恢复上次的 Device Path、每路 ChannelOptions、Display Filter、TxSlot, so that 常用配置不用每次重填。
60. As a 主机用户, I want 恢复表单之后仍然不自动 Open、不自动 Start, so that 插错总线时不会自己 ACK。
61. As a 主机用户, I want 状态栏显示每路是否在跑、RX/TX/Error/Overflow 计数、暂停丢弃、最后一次 `GsCanException`, so that 不必从 Trace 里猜当前状态。
62. As a 主机用户, I want RX 计数按 Kind=Rx 的帧、TX 计数按 Kind=Echo 的帧, so that 状态栏不把 Echo 说成「已上总线的发送成功」。
63. As a 实现者, I want GsCan View 只消费 GsCan 的公共面, so that 不必改库、不必暴露 candle。
64. As a 实现者, I want 窗口只绑定会话状态、不直接拥有 `Device` 句柄的一生, so that 控件被拆掉时不会把 USB 扔在半空。
65. As a Windows x64 用户, I want 自包含 zip，exe 旁边就是 `candle_api.dll`, so that 不必先装桌面运行时，也不必跟单文件自解压搏斗。
66. As a 合规使用者, I want 应用动态链接那份 LGPL native, so that 替换 `candle_api.dll` 的路径还在。

## Implementation Decisions

- **一个新模块**：`GsCan.View`（程序集 / 根命名空间 / 窗口标题「GsCan View」）。寄宿本仓库，解决方案仍是现有那一份。不改 GsCan 的公共面，不把 GsCan.NET 改成上位机。
- **窗口是薄绑定**：Device 生命周期、每路 Channel 的 Start/Stop、TryRead 泵、Trace / Latest / Display Filter / TxSlot / Log / 配置记忆，都在会话层。XAML 只绑状态和命令。MVVM 工具任选，不构成本规格的决策。
- **目标框架**：现代桌面 TFM（与现有测试项目同代即可）。不是 `netstandard2.0`。RID 第一版只 `win-x64`。
- **UI**：Avalonia。运行时只做 Windows，不宣传跨平台。不选 WPF/WinUI。
- **分发**：自包含、exe 旁放 `candle_api.dll`、zip。不做 x86、不做安装器、不做单文件。
- **一次一块 Device**：打开第二块必须先关当前这块。Channel 条数量等于该 Device 的 `ChannelCount`。
- **Open ≠ Start**。Start 才上总线。默认：FD 关、ListenOnly 关、Loopback 关、OneShot 关、仲裁 500k；勾上 FD 后数据段默认 2M。Bitrate 下拉只给库 native 认的取值。
- **读泵**：每路已 Start 的 Channel 在后台 `TryRead`。遵守库的线程条款：同一 Channel 上不同时两个 `TryRead`；`Start` / `Stop` / `Dispose` 与收发互斥；`Send` 与 `TryRead` 可以跨线程。暂停冻的是 Trace/Latest 的画面，不是泵。
- **UI 线程**：只收批量快照，不为每一帧切一次调度。
- **Trace**：到达序、上限约 10 万行、超出丢最旧。相对时间按该 Channel 本次 Start 起算。清空只清窗口。
- **Latest**：Rx / Echo 键 `(Channel, Kind, Id, Extended, Remote, IsFd)`；Error 键 `(Channel, Kind, ErrorClass)`。计数按键累加；BRS/ESI/Overflow/数据/Error 提示取最新帧。
- **Display Filter**：Channel、Kind、标准/扩展、经典/FD、Remote、单个十六进制 ID 或闭区间。无掩码、无数据匹配、无表达式。Error 只认 Channel + Kind=Error。过滤发生在进窗口之前，不进设备。
- **TxSlot**：默认 1 行，可添加/删除，上限 16。列：Channel、ID、Extended、Remote、FD、BRS、DLC、数据、周期毫秒（0 = 一次）、使能。DLC 是 payload 字节数（经典 0–8；FD 另加 12/16/20/24/32/48/64），默认 8。无 ESI。该路 Stop 或 ListenOnly → 停该路周期发送且不得再 `Send`。从旧的 16 行空白表恢复时丢掉末尾空行。
- **Echo 文案**：任何地方都不写「TX 成功 / 已上总线 / 发送成功」。状态栏 TX 计数来自 Echo 帧数，含义是「设备上结束的 Send」，不是总线确认。
- **拔掉**：进行中的调用以 `GsCanException` 失败；关 Device；停周期发送；已有 Trace/Latest 保留；不合成帧；不自动重开。
- **Log**：只把当前 Trace 写成 CSV（含 `TimestampMicroseconds` 原值、Channel、Kind、ID、标志、数据）。不能打开。Display Filter 不决定写出哪些行——写出的是窗口里那份 Trace 缓冲（暂停丢弃的本来就不在缓冲里）。
- **配置记忆**：启动时恢复 Device Path、每路 ChannelOptions、Display Filter、TxSlot。不自动 Open、不自动 Start。
- **骨架**：单窗口自上而下：Device 条（启动与打开下拉会 List）；Channel 条（OneShot 在「更多」）；Display Filter 条；接收区（Trace | Latest、暂停/清空；Trace 始终钉底）；TxSlot 表（含 DLC）；状态栏。不对接、没有第二窗口。
- **ErrorClass**：只在 View 把 SocketCAN 类位归成一个主因。优先级：Bus-off > Restarted > ACK > Stuff > Form > Bit0 > Bit1 > CRC > Error-passive > Error-warning > Error-active > Bus error > Unknown。ID 列显示该英文词；tooltip 与 LastError 用中文提示；CNT 位置位时提示末尾加 TEC/REC。Data 与 Log 仍是原值。库不解释。不解码 location / TRX。周期发送只在硬故障类停下。详见 ADR-0010。
- **库能力缺口不在窗口里假装有**：IDENTIFY、软件端接、硬件滤波、`Channel.State`、总线恢复，都不做控件。
- **命令可用性**：会话能预判的非法就禁用（LastError 只留给端口失败）；Device Open 锁身份，Channel Running 锁该路 `ChannelOptions`；发送与使能同一把钥匙且使能不是预约。详见 `.scratch/gscan-view-command-availability/spec.md` 与 ADR-0011。

## Testing Decisions

- **唯一测试缝**：GsCan View 会话面——窗口所绑定的那一层命令与状态（Device 生命周期、Channel Start/Stop、Trace、Latest、Display Filter、TxSlot、暂停/清空/上限、Log、配置记忆）。只测外部行为。
- **不要**测 Avalonia 控件树、不要测像素、不要在 GsCan 库内部或 candle 上开第二道缝。库已有自己的规格和测试；本规格不重复 `Send`/`TryRead`/Echo FIFO 那些库条款，只测 View 怎么展现和指挥它们。
- **无实机**：会话面必须能在测试里注入 `CanFrame` 序列、模拟 List/Open/Start/`GsCanException`、模拟时钟上的周期 `Send`。不要求本规格实现一个假 Device 去骗库；假的是 View 会话背后的端口，测试只对会话说话。
- **好测试**：喂一组带 Kind 的帧，断言 Trace 顺序、Latest 键（Echo 与 Rx 分家、Error 按 ErrorClass 分键）、ErrorClass / 提示 / LastError、Display Filter 显隐（Error 忽略 ID 与帧格式勾选）、暂停期间帧不进缓冲且丢弃计数增加、上限丢最旧、ListenOnly/Stop 停掉该路 TxSlot、硬故障才停周期发送、TxSlot 可加减且上限 16、Log CSV 含原始微秒与数值 Id、恢复的配置不会自己 Start。不断言控件名、样式、调度器类型。
- **验收（必须，无设备则跳过，风格同现有 `GsCan.Tests`）**：实机 FlintCAN-FD 能从窗口 List/Open、两路独立 Start、Loopback 下看见 Echo 与 Rx、Stop 后周期发送停、拔线或关闭后状态栏报错且已有帧保留。
- **Prior art**：`GsCan.Tests`（xUnit、无设备打印 SKIP）。View 的测试项目同一风格；硬件相关同样可跳过，不得在没插设备的机器上失败。

## Out of Scope

- 改 GsCan 公共面、补 IDENTIFY / 端接 / 硬件滤波 / `Channel.State` / 原始 bit timing
- DBC、信号图、UDS / ISO-TP / J1939、脚本、回放、离线打开 Log
- 多块 Device 同时在线、多窗口 / 对接布局
- Display Filter 表达式、ID 掩码、数据字节匹配
- Vector ASC / BLF / PCAN `.trc`、Wireshark 导出
- Linux / macOS、win-x86、安装器、单文件 exe、跨平台宣传
- 自动 Open / 自动 Start
- 把 Echo 显示成「上了总线」
- SocketCAN location / TRX 解码、把 ErrorClass 做成库 API、总线负载曲线、主题/快捷键作为交付要求
- 实现期选哪个 MVVM 包、Fluent 皮肤

## Further Notes

- 库规格：`.scratch/gscan-net-spec/spec.md`。View 不得削弱其中 Echo、Stop、ListenOnly、无热插拔事件等条款。
- ADR：`docs/adr/0001` 不是分析仪；`0002` Avalonia + Win-x64 zip；`0003` 一块 Device 两路 Channel、Trace+Latest；`0004` CSV 只写；`0005` Latest 键含 Kind；`0006` 暂停不停泵；`0007` 工程身份 `GsCan.View`；`0008` 记住表单但不上总线；`0009` TxSlot 是动态列表不是邮箱表；`0010` View 对 Error 做 ErrorClass，库不解释；`0011` 能预判的非法就禁用，已生效配置必须先 unwind。
- 命令可用性规格：`.scratch/gscan-view-command-availability/spec.md`。
