# 01: 按轴禁用命令并给出原因

Parent: [GsCan View — 命令可用性](../spec.md)

**What to build:** 会话面按 Device / Channel / Pause / Trace 是否有行计算命令可用性，窗口绑定禁用与中文原因。能预判的非法不再静默、不再用 LastError 顶替。Device Open 锁身份；Channel Running 锁该路选项；发送与使能同一把钥匙且使能不是预约；Channel 序号列表随 `ChannelCount`；保存 Log 仅 Trace 非空。

**Blocked by:** （无）

**Status:** ready-for-agent

- [x] Device 条：无选中不能打开；Open 期间下拉 / 刷新 / 打开禁用、关闭可用；Closed 时关闭禁用、刷新可用
- [x] 每路启动 / 停止对称；Running 时该路 `ChannelOptions` 禁用；两路独立
- [x] 发送与使能：Open ∧ 该路 Running ∧ 非只听 ∧ 序号合法；否则禁用且使能保持关；Stop 清使能；Start 不恢复；Pause 不改这把钥匙
- [x] 未 Open 时 Channel 序号为 0 和 1；Open 后为 `0 .. ChannelCount-1`；越界夹回 0
- [x] 暂停 / 清空始终可；保存 Log 仅 Trace 非空（关 Device 后有行仍可）
- [x] 命令按钮有规格里那句中文原因；只听不再靠点发送写 LastError；Start 失败 / 拔线仍走 LastError
- [x] 会话面测试覆盖上述轴；不测 Avalonia 控件树；无设备则不得失败
