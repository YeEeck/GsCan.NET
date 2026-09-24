# 02: 列出、打开、关闭 Device

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 顶栏展示当前 `Device.List` 快照（Path 与 Channel 数）。刷新再 List 一次。一次只打开一块 Device；打开后按 `ChannelCount` 出现对应数量的 Channel 条（此时仍不能 Start）。关闭后可再打开。无设备时列表为空、不报错。

**Blocked by:** 01 窗口能跑起来

**Status:** ready-for-agent

- [x] 刷新后的列表是当时快照，不会自己因插拔更新
- [x] 打开一块 Device 后不能同时再开另一块；Channel 条数量等于该 Device 的 Channel 数
- [x] Open 之后各路 Channel 仍停着（Open ≠ Start）
- [x] 关闭 Device 后顶栏回到未打开；同一项可以再开
- [x] 无设备时列表为空，不抛给用户看不懂的崩溃
- [x] 会话面测试可用假列表 / 假 Open 失败，不要求实机

## Comments

- Implemented on `feat/gscan-view-02-list-open-close` (`242501a`), merged to `feat/gscan-view-v1`. Fake port injects list/Open failure. Real `GsCanPort` wraps `Device.List`/`Open`. Start is still ticket 03.
