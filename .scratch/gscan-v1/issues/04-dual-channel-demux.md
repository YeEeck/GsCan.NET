# 04 — 双通道分路

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** FlintCAN-FD 两路 Channel 各自 `Start`。在 Channel 0 上 `Send` 的帧不会从 Channel 1 的 `TryRead` 出现。调用方不必看见 USB IN 是整机一条。

**Blocked by:** 02 — 单通道经典收发与 Echo

**Status:** verified (hardware pending)

- [ ] 两路均可独立 `Start` / `Stop`（bitrate 可以不同）
- [ ] Channel 0 `Send` 后，其 Echo（及 Loopback Rx）只出现在 Channel 0 的 `TryRead`
- [ ] Channel 1 在同一时段 `TryRead` 超时返回 `false`，不得串到 Channel 0 的帧
- [ ] 反之（在 1 发、在 0 读）同样隔离
- [x] 公共面仍是 `Channel.TryRead`，无「整机读再带通道号」的第二套 API

## Comments

- 2026-09-19 编排验证：`dotnet test GsCan.sln` 20 通过。生产代码未改（02 已做 per-channel queue demux）。本机无设备，隔离实机项 SKIP。公共面测试确认 `Device` 无整机读 API，唯一 `TryRead` 在 `Channel`。
