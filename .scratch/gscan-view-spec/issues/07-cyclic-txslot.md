# 07: 周期 TxSlot

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 一张约 16 行的 TxSlot 表：Channel、ID、Extended、Remote、FD、BRS、数据、周期毫秒、使能。周期 > 0 且使能则按周期 `Send`。该路 Stop 或改为 ListenOnly 后，该路上的周期发送停掉且不得再发。没有 ESI 列。

**Blocked by:** 04 发一帧并看见 Trace

**Status:** ready-for-agent

- [ ] 约 16 个 TxSlot；周期 0 仍是发一次，周期 > 0 为周期发送
- [ ] 无 ESI 字段
- [ ] Channel Stop 后该路周期发送停止；再 Start 不会自动恢复使能脉冲，除非用户再次使能（实现须在验收里表现得可预期：Stop 即停）
- [ ] 该路 ListenOnly 时该路 TxSlot 不能发、已在跑的周期停掉
- [ ] 会话面可用假时钟验证周期次数与 Stop 后不再 Send，不要求实机
