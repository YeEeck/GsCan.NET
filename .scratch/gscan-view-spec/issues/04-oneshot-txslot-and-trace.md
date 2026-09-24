# 04: 发一帧并看见 Trace

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** TxSlot 周期为 0 时能对指定 Channel `Send`。接收区以 Trace 按到达顺序显示帧：相对时间（该路本次 Start 起）、Channel、Kind（Rx / Echo / Error）、ID 与标志、长度、数据十六进制。Echo 不得写成「上了总线」。Overflow 在该行标出。Error 的 payload 只显示十六进制。暂停冻 Trace 画面，后台仍 TryRead，暂停丢弃计入状态。清空只清窗口。Trace 约 10 万行封顶，超出丢最旧。该路 ListenOnly 时不能发。贴底才自动滚屏。

**Blocked by:** 03 按路 Start / Stop

**Status:** ready-for-agent

- [ ] Loopback 下一次 Send 在 Trace 里能同时看到 Echo 和 Rx，Kind 为英文词条
- [ ] 任何文案都不把 Echo 说成 TX 成功 / 已上总线 / 发送成功
- [ ] 暂停期间新帧不进 Trace，泵仍在读，状态能看见暂停丢弃；清空不动总线
- [ ] Trace 超上限丢最旧；相对时间为本次 Start 后的毫秒
- [ ] ListenOnly 的路上 TxSlot 不能 Send
- [ ] 会话面可注入 CanFrame 验证顺序、Kind、暂停、上限，不要求实机
- [ ] 实机 FlintCAN-FD 走通 Loopback 发一帧（无设备则 SKIP）
