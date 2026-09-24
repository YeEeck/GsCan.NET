# 09: 拔掉、状态栏

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 使用中设备断开或调用失败：收发停、Device 关掉、周期发送停、状态栏出现 `GsCanException`、已在 Trace/Latest 里的帧保留、不自动重开、不合成帧。状态栏显示每路是否在跑、RX 计数（Kind=Rx）、TX 计数（Kind=Echo，含义是设备上结束的 Send）、Error、Overflow、暂停丢弃、最后一次错误。不写「上了总线」。

**Blocked by:** 04 发一帧并看见 Trace

**Status:** ready-for-agent

- [ ] 失败路径关闭 Device 后已有 Trace/Latest 仍在
- [ ] 不自动 Open / Start；用户须刷新并再打开
- [ ] TX 计数来自 Echo，文案不暗示已经上了总线
- [ ] RX / Error / Overflow / 暂停丢弃可在会话面用注入帧与暂停断言
- [ ] 实机拔线若不便测，允许 SKIP；不得在没插设备的机器上失败
