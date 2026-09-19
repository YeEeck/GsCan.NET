# 02 — 单通道经典收发与 Echo

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** 一路 Channel 用整数 `Bitrate` 启动后，调用方能 `Send` 经典 `CanFrame`，并用 `TryRead` 收到 Echo（FIFO，不是上了总线）以及 Loopback 下的 Rx。超时返回 `false`；配置失败抛 `GsCanException`。

**Blocked by:** 01 — 列出并打开 Device

**Status:** verified (hardware pending)

- [ ] `Start(ChannelOptions { Bitrate })` 后该路上总线；无效 bitrate / 未 Open 的操作抛 `GsCanException`
- [ ] `Send` 立刻返回；`TryRead` 在超时内未到帧返回 `false`，不抛
- [ ] Loopback 下 `Send(CanFrame.Classic(...))` 之后，同一 Channel 能按 FIFO 读到 `Kind = Echo` 的对应帧
- [x] Echo 不得被文档或 API 说成发送成功 / 上了总线
- [ ] Loopback 下还能读到 `Kind = Rx` 的回环帧（与 Echo 区分）
- [ ] `Stop` 后该路停止；本票不要求验证「未完成 Send 无 Echo」（见 05）
- [x] 只测公共 `Device` / `Channel` / `CanFrame` / `GsCanException`，不测 P/Invoke 内部

## Comments

- 2026-09-19 编排验证：`dotnet test GsCan.sln` 12 通过。本机无设备，Loopback Echo/Rx、Start 上总线、TryRead 超时、Stop 等实机项 SKIP，未勾。代码：`Channel.Start/Send/TryRead/Stop` 已接 `candle_*`；USB IN 在 `Device.TryReadForChannel` 按 `channel` 分路；Echo 用 `echo_id != 0xFFFFFFFF`（与 `candle_frame_type` 一致）；文档写 “Echo is not bus confirmation”。
- 01 的 “not started” 测试已改为：Open 后未 Start 时 Send/TryRead 抛 `GsCanException`。
