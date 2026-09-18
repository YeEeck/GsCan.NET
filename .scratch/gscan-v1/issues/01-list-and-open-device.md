# 01 — 列出并打开 Device

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** 调用方装上 `GsCan` 就能看见插着的 gs_usb 适配器：FlintCAN-FD 以一个 Device、两路 Channel 出现；`Open` 之后可以 `Dispose`。Native 动态库随包到达输出目录，公共面不出现 Candle 名。

**Blocked by:** None — can start immediately

**Status:** ready-for-agent

- [ ] `Device.List` 在插着 FlintCAN-FD 时返回至少一项，`ChannelCount` 为 2，并带可用来 `Open` 的路径
- [ ] `Device.Open` 给出两路 `Channel`（下标 0 和 1），尚未 `Start`
- [ ] `Dispose`（含 using）释放设备；再 `Open` 同一项仍可用
- [ ] 无设备时 `List` 返回空集合，不抛
- [ ] 单一 NuGet/程序集 `GsCan`、`netstandard2.0`；native 为自建动态库（x86/x64），RID + 旁路拷贝使 `DllImport` 能加载
- [ ] 包内有 LGPL/GPL 文本及对应 `api/` 源码获取方式；不附带 CANgaroo 应用
- [ ] 公共类型与命名空间不出现 Candle
