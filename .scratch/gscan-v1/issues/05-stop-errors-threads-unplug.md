# 05 — Stop、总线状况、线程与断开

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** 规格里剩余的热路径语义一次做完：`Stop` 丢掉未完成 Send 且不产生 Echo；总线错误和 Overflow 走 `CanFrame`；`ListenOnly` / `OneShot` 可用；同一 Channel 上 `Send` 与 `TryRead` 可跨线程；使用中拔掉设备以 `GsCanException` 失败。无 State、无恢复、无事件。

**Blocked by:** 02 — 单通道经典收发与 Echo

**Status:** ready-for-agent

- [ ] `Stop` 为 void、不抛；未对上的 `Send` 之后不再出现 Echo；不合成取消帧
- [ ] 总线错误以 `Kind = Error` 的 `CanFrame` 出现（含 `CAN_ERR_BUSOFF` 这类错误帧）；不是异常
- [ ] Overflow 是某帧上的 `Overflow` 标志，不是独立类型或事件
- [ ] `ListenOnly` 与 `OneShot` 可在 `ChannelOptions` 里打开并实际作用于该路
- [ ] 同一 Channel：线程 A 阻塞 `TryRead`、线程 B `Send`，两者均可完成（不要求两个 `TryRead` 并发）
- [ ] `Start` / `Stop` / `Dispose` 与收发互斥（并发调用不得损坏设备句柄；允许抛 `GsCanException`）
- [ ] 使用中拔掉适配器：进行中的 `TryRead` / `Send` / `Start` 以 `GsCanException` 失败，不合成帧
- [ ] 无 `Channel.State`、无总线恢复、无到达/离开事件、无 BERR 开关
