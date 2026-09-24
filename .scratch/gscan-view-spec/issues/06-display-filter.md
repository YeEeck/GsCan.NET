# 06: Display Filter

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** Display Filter 按 Channel、Kind、标准/扩展、经典/FD、Remote、单个十六进制 ID 或闭区间（如 `100-1FF`）决定 Trace（以及已有的 Latest）里看见哪些帧。关掉 Kind=Echo 即不再看见 Echo。只改窗口，不进设备，不是硬件滤波。无掩码、无数据匹配、无表达式语言。

**Blocked by:** 04 发一帧并看见 Trace

**Status:** ready-for-agent

- [ ] 勾选/ID 条件只影响看见什么；泵仍接收未显示的帧
- [ ] 可单独关掉 Kind=Echo；可只看一路 Channel
- [ ] ID 支持单个十六进制与闭区间；非法输入有说明、不崩溃
- [ ] 若 Latest 已存在，同一套 Display Filter 对两种看法生效
- [ ] 会话面测试覆盖显隐，不要求实机
