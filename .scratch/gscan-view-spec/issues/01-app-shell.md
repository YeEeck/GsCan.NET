# 01: 窗口能跑起来

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 用户能启动 GsCan View：窗口标题是「GsCan View」，界面中文，单窗口骨架在（Device 条、Channel 区、接收区、TxSlot 区、状态栏可以是空的）。测试能构造会话面。还不碰 USB，也不上总线。

**Blocked by:** （无）

**Status:** ready-for-agent

- [x] 启动后窗口标题为「GsCan View」，不是 GsCan.NET，也不是 FlintCAN
- [x] 可见文字为中文；Echo / Trace / Latest / Kind 若出现则保持英文
- [x] 单窗口，不对接、没有第二窗口
- [x] 测试项目能构造会话面；无设备时测试通过（不要求实机）

## Comments

- Implemented on `feat/gscan-view-01-app-shell` (`6ecc81c`), merged to `feat/gscan-view-v1`. Session constructible with `FakeGsCanPort`; window title bound to `ViewSession.WindowTitle`. USB List/Open still later tickets.
