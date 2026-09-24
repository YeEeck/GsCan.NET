# 05: Latest

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 接收区可在 Trace 与 Latest 之间切换。Latest 按键覆盖：键为 `(Channel, Kind, Id, Extended, Remote, IsFd)`，每个键只留最新一帧并计数。BRS / ESI / Overflow / 数据随最新帧更新、不进键。Loopback 下同一 Send 的 Echo 与 Rx 各占一行；Error 不被数据帧盖掉。

**Blocked by:** 04 发一帧并看见 Trace

**Status:** ready-for-agent

- [ ] 可切换 Trace | Latest，不是两个窗口
- [ ] 键含 Kind：Echo 与 Rx 不会互相覆盖
- [ ] Error 与同 ID 的 Rx 是不同行
- [ ] 同一键再次到达时计数增加，数据与 BRS / ESI / Overflow 取最新
- [ ] 会话面注入帧即可验证键与计数，不要求实机
