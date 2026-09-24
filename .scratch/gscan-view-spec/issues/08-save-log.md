# 08: 存 Log

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 用户能把当前 Trace 存成 CSV。列含 `TimestampMicroseconds` 原值、Channel、Kind、ID、标志、数据。第一版不能打开一份 Log 来看。写出的是 Trace 缓冲里的帧（暂停丢弃的本来就不在）。Display Filter 不另外决定磁盘上写哪些行。

**Blocked by:** 04 发一帧并看见 Trace

**Status:** ready-for-agent

- [ ] 能把当前 Trace 存为 CSV，含原始微秒时间戳
- [ ] 没有「打开 Log」入口
- [ ] 不是 ASC / BLF / trc
- [ ] 会话面测试：注入若干帧再导出，文件内容可断言，不要求实机
