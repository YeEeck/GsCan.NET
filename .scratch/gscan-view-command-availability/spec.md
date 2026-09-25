# GsCan View — 命令可用性

Status: ready-for-agent

术语以仓库根 `CONTEXT.md` 为准。库的语义（Echo、Start/Stop、ListenOnly、`Send`）不在本规格改写。父规格：`.scratch/gscan-view-spec/spec.md`。决策：`docs/adr/0011`；并遵守 `0003`（两路对等）、`0006`（Pause 只冻画面）、`0008`（记住表单但不上总线）、`0009`（TxSlot 是动态表）、`0010`（硬故障才停周期发送）。

## Problem Statement

窗口上绝大多数按钮始终可点。Device 已 Open 时「刷新」什么都不做，「打开」才写 LastError；Channel 未 Start 时「发送」静默失败，只听时却又骂一句；正在跑的 Channel 仍能改仲裁和只听，但选项要到下次 Start 才进库——只听勾选还会和硬件脱节。用户分不清「现在不能做」和「设备报错了」。

## Solution

可用性按正交轴计算，没有全局 Mode：Device `Closed` | `Open`（拔线塌成 `Closed` + LastError）、每路 Channel `Stopped` | `Running`、画面 `Live` | `Paused`、Trace 空 | 有行。会话能预判不合法的命令禁用，并给一句中文原因；LastError 只留给端口失败。已 Open 的 Device 锁身份；已 Running 的 Channel 锁该路 `ChannelOptions`。发送和使能同一把钥匙。Pause / 清空始终可做。

## User Stories

1. As a GsCan View 用户, I want 可用性按 Device / Channel / Pause / Trace 是否有行分别算, so that 一路在跑时另一路仍能改选项，Pause 也不会把发送灰掉。
2. As a GsCan View 用户, I want 不要一个全局 Mode（空闲 / 在线 / 暂停 / 死了）, so that 不会和两路对等、Pause 只冻画面打架。
3. As a 主机用户, I want 会话已经知道不合法的命令是灰的, so that 我不必点下去才发现不行。
4. As a 主机用户, I want 可见按钮不要静默什么都不做, so that 「刷新」亮着却 List 一次都没有这种事不再发生。
5. As a 主机用户, I want Start 失败、拔线、`Send` 抛 `GsCanException` 仍然只走 LastError, so that 状态栏继续表示「打到设备才知道的失败」，不当「你不该点这个」的说明书。
6. As a 主机用户, I want 灰掉的命令按钮带一句中文原因, so that 发送离通道条很远时我仍知道是未启动还是只听。
7. As a 主机用户, I want ChannelOptions 锁死、BRS 随 FD、数据随 Remote 这种同排能看懂的灰不加原因, so that tooltip 不会刷屏。
8. As a 主机用户, I want hover 灰按钮不要改写 LastError, so that 端口失败不会被「通道未启动」盖掉。
9. As a 主机用户, I want 未选 Device 时「打开」禁用, so that 点下去不会无声 return。
10. As a 主机用户, I want 已选 Device 且未 Open 时「打开」可用, so that 常用路径一步能上。
11. As a 主机用户, I want Device 已 Open 时下拉、刷新、打开都禁用, so that 我不会以为能换一块或再 List 一次，也不会改掉下次要恢复的 Path。
12. As a 主机用户, I want Device 已 Open 时只有「关闭」能点, so that 换设备的路径是先关。
13. As a 主机用户, I want 未 Open 时「关闭」禁用, so that 它不会装成还能关一次。
14. As a 主机用户, I want 未 Open 时「刷新」可用且会 List, so that 刚插上的设备能出现。
15. As a 主机用户, I want 关掉 Device 之后下拉 / 刷新 / 打开（若仍有选中）恢复可点, so that 拔线或手关之后我能再开。
16. As a 单路 gs_usb 用户, I want Open 之后 Channel 条仍只有一路, so that 不会出现点不动的第二路（父规格故事 9，本规格不改）。
17. As a 主机用户, I want 某路 Stopped 时「启动」可用、「停止」禁用, so that 两颗并排按钮始终一亮一灰。
18. As a 主机用户, I want 某路 Running 时「启动」禁用、「停止」可用, so that 我不会对已在跑的路再点启动。
19. As a FlintCAN-FD 用户, I want 两路的启停和选项锁互相独立, so that CH0 在跑时 CH1 仍能改bitrate 并 Start。
20. As a 主机用户, I want 某路 Running 时该路仲裁、FD、数据段、只听、环回、OneShot 都禁用, so that 表单不会假装已经改了总线。
21. As a 主机用户, I want 改速率 / FD / 只听 / 环回 / OneShot 必须先 Stop 再改再 Start, so that 选项只在 `Start` 时交给库的语义不被 UI 撒谎。
22. As a 主机用户, I want Stop 之后该路选项立刻可改, so that unwind 是可逆的。
23. As a 主机用户, I want Start 失败时该路仍 Stopped、启动仍可用、LastError 有说明, so that 失败不是「按钮灰了但没上总线」。
24. As a 发送用户, I want Device 未 Open 时「发送」和使能都禁用, so that 不会点下去什么都不发生。
25. As a 发送用户, I want 目标 Channel 未 Running 时「发送」和使能都禁用, so that 未上总线发不出帧。
26. As a 发送用户, I want 目标 Channel 以只听 Start 时「发送」和使能都禁用, so that 不再靠点发送才看到「只听通道不能发送。」
27. As a 发送用户, I want 目标 Channel Running 且非只听时「发送」和使能可用, so that 实验室能发一次也能武装周期。
28. As a 发送用户, I want 发送和使能共用同一把钥匙, so that 能点发送的时候才能勾使能，不会出现只能武装不能发。
29. As a 发送用户, I want 使能不是预约：Channel 还不能发时勾不上、保持未武装, so that 停着勾上再 Start 不会自己嘀嘀嘀。
30. As a 发送用户, I want 该路 Stop 之后使能被清掉且禁用, so that 再 Start 不会自动恢复周期发送（父规格 issue 07）。
31. As a 发送用户, I want 要周期发送时先 Start 再勾使能, so that 和 Open ≠ Start 同一方向。
32. As a 发送用户, I want Pause 时只要该路仍能发，「发送」和使能仍可用, so that 冻画面不等于停发送。
33. As a 发送用户, I want 硬故障 ErrorClass 仍只把该路使能清掉, so that 不新开 Bus-off 锁轴；通道若仍 Running 且非只听，我可以再勾使能。
34. As a 发送用户, I want TxSlot 的 ID / 标志 / DLC / 数据 / 周期 / Channel 随时可编（BRS 仍随 FD、数据仍随 Remote）, so that 周期武装着也能改下一个字节。
35. As a 发送用户, I want TxSlot 的 FD 不跟目标 Channel 当时是不是 FD 绑死, so that 未 Open / 未 Start 时仍能预先填一帧 FD。
36. As a 发送用户, I want 在经典 Running 的路上发 FD 若端口失败则走 LastError, so that 这是设备失败，不是会话先替库拒绝。
37. As a 发送用户, I want 未 Open 时 TxSlot 的 Channel 仍是 0 和 1, so that 记住表单不必先插设备。
38. As a 单路 gs_usb 用户, I want Open 之后 TxSlot 的 Channel 下拉只有 0, so that 选不到发不出去的 CH1。
39. As a 单路 gs_usb 用户, I want Open 时若某行 Channel 越界则夹回 0, so that 不会留着一行永远发不出去。
40. As a FlintCAN-FD 用户, I want Open 两路之后 TxSlot 的 Channel 仍是 0 和 1, so that 双路发送表不变。
41. As a 发送用户, I want 关掉 Device 之后 Channel 下拉回到 0 和 1, so that 未 Open 的表单规则恢复。
42. As a 发送用户, I want 添加仍在 16 行时禁用、删除仍在只剩 1 行时禁用, so that 行数规则不变。
43. As a 接收用户, I want 暂停始终可勾, so that 没帧、没 Device 时也能先冻住，之后 Start 的帧不进缓冲。
44. As a 接收用户, I want 清空始终可点, so that 空窗口上点清空仍是清空。
45. As a 主机用户, I want Trace 为空时「保存 Log」禁用, so that 不会弹出一次对话框去写空表。
46. As a 主机用户, I want Trace 有行时即使 Device 已关（含拔线）也能保存 Log, so that USB 线松了不等于跟踪带不走。
47. As a 接收用户, I want Display Filter 的勾选和 ID 始终可改, so that 过滤仍只改看见什么。
48. As a 接收用户, I want 未 Open 时 Filter 仍有通道 0 和 1, so that 表单能记住。
49. As a 单路 gs_usb 用户, I want Open 之后 Filter 只出现通道 0, so that 不会对着一个永远匹配不到的通道 1。
50. As a FlintCAN-FD 用户, I want Open 两路之后 Filter 仍是通道 0 和 1, so that 双路过滤不变。
51. As a 主机用户, I want 拔线之后可用性按 Closed 算、LastError 仍在、已有 Trace/Latest 仍在, so that 不会对着死句柄点启动。
52. As a 实现者, I want 可用性和原因都在窗口绑定的会话面上, so that 测试不断言控件树，XAML 只绑 `IsEnabled` 和 `ToolTip`。

## Implementation Decisions

- **仍是同一会话面**：不新模块，不改 GsCan 公共面。窗口继续薄绑定。可用性是会话状态，不是 Avalonia 自己猜，也不是全局 Mode 枚举。
- **预判禁用，端口失败才 LastError。** 方法里原有的守卫可以留作第二道闸，但可见命令必须先灰掉。原先「打开已 Open → LastError」「只听发送 → LastError」「未 Start 发送 → 静默」里，能预判的那几条改为禁用；Start 失败 / 拔线 / native `Send` 失败仍写 LastError。
- **Device 身份锁：** `IsDeviceOpen` 时禁用下拉、刷新、打开，只留关闭。`Closed` 时禁用关闭；打开仅当有选中的 Device。刷新仅 `Closed` 时可用。
- **Channel 选项锁：** 该路 `Running` 时禁用该路仲裁、FD、数据段、只听、环回、OneShot。「启动」仅 Stopped，「停止」仅 Running。另一路不受影响。没有自动 Stop+Start。
- **发送钥匙：** 某一行可发送当且仅当 Device Open、该行 Channel 序号存在、该路 Running、该路非只听。同一谓词门控「发送」和使能。使能在不能发时必须为关，且不能被打开（不是预约）。Stop 仍清使能。Pause 不参与此谓词。
- **Channel 序号列表：** 未 Open 为 `{0, 1}`。Open 之后为 `0 .. ChannelCount-1`。Display Filter 的通道勾选和 TxSlot 的 Channel 下拉共用这份列表。越界的 TxSlot.Channel 夹回 0。Close 之后回到 `{0, 1}`。
- **TxSlot 编辑与 FD：** 行内字段始终可编（BRS 仍要求 FD，数据仍要求非 Remote）。TxSlot 的 FD 不跟目标 Channel 的 FD 勾选绑定。添加 / 删除仍只跟 1..16。
- **画面操作：** 暂停、清空始终可用。「保存 Log」仅当 Trace 非空。
- **硬故障：** 仍按 ADR-0010 清该路使能。不因 ErrorClass 禁用使能；通道若仍可发送，用户可再武装。
- **禁用原因（命令按钮，中文）：**
  - 打开，无选中：「未选择设备」
  - 打开 / 刷新 / 下拉，已 Open：「请先关闭当前 Device」
  - 关闭，未 Open：「未打开 Device」
  - 启动，已 Running：「通道已在运行」
  - 停止，已 Stopped：「通道未启动」
  - 发送 / 使能，未 Open：「未打开 Device」
  - 发送 / 使能，通道未 Running 或序号不存在：「通道未启动」
  - 发送 / 使能，只听：「只听通道不能发送」
  - 保存 Log，Trace 空：「Trace 为空」
  - 添加，已 16 行：沿用「最多 16 行」
- **拔线：** 仍关 Device、停周期、保留 Trace/Latest、不自动重开。可用性按 Closed 重算。

## Testing Decisions

- **唯一测试缝：** 仍是 GsCan View 会话面（窗口所绑定的命令与状态）。本规格只是给同一层补上可用性标志和禁用原因。不要新端口，不要测 Avalonia 控件树 / `IsEnabled` 像素，不要进 GsCan 库或 candle。
- **断言外部行为：** Open / Close / Start / Stop / 只听 / Pause / 清空 / 有无 Trace 行 / 1 路 vs 2 路 Open 之后，会话上的「能否打开、刷新、关闭、启动、停止、改选项、发送、使能、保存 Log」以及对应中文原因。越界 Channel 夹回 0。Stop 后使能保持关且不能武装；Start 不自动恢复使能。Pause 不改变发送钥匙。只听时发送不可用且不写 LastError。Start 失败则该路仍可启动且 LastError 有说明。
- **不要断言：** 控件名、样式、XAML 绑定路径、调度器类型、是否用 `ICommand`。
- **无实机：** `FakeGsCanPort` 注入 List/Open/Start/只听/`GsCanException`、注入帧以产生非空 Trace。本规格不要求新的硬件验收；拔线路径用现有关 Device + LastError 的会话行为覆盖可用性重算即可。
- **Prior art：** `GsCan.View.Tests` 的 `ViewSessionTests`、`ChannelStartStopTests`、`TraceAndTxSlotTests`、`StatusAndUnplugTests`。同一 xUnit 风格；没插设备的机器上不得失败。
- **好测试：** 未选中时不能打开；Open 之后不能刷新 / 打开、能关闭；CH0 Running 时 CH0 不能改选项、CH1 能；只听 Running 时该行不能发送、LastError 不被这条路径写入；停着不能把使能打开，Start 之后使能仍关；1 路 Open 后 Channel 列表只有 0，记忆里的 1 被夹回；空 Trace 不能保存，灌入一帧后能保存，Close 之后仍能保存；Pause 时发送钥匙不变。

## Out of Scope

- 改 GsCan 公共面、补 `Channel.State`、Bus-off 期间锁死使能
- 改选项时自动 Stop+Start
- 把 TxSlot 的 FD 绑到 Channel 的 FD
- 确认对话框、快捷键、把禁用做成隐藏（Channel 条随 Open 显隐、非法 Channel 序号从列表拿掉——这两项不是「整颗按钮藏起来」）
- 主题、禁用态皮肤、第二窗口
- 把 Echo 说成发送成功；自动 Open / 自动 Start
- 重做 Display Filter 语言、离线打开 Log

## Further Notes

- 父规格 `.scratch/gscan-view-spec/spec.md` 的故事 9 / 11 / 37–40 / 48–49 / 52–55 / 56 仍然有效；本规格补的是这些命令在何时可点。
- ADR-0011 是本规格的政策；ADR-0006 禁止把 Pause 做成第三种 Channel 状态；ADR-0008 要求未 Open 时表单仍可编（因此 TxSlot / Filter 在 Closed 时不锁编辑，只锁发送钥匙）。
