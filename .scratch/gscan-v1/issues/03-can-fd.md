# 03 — CAN FD

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** 同一路 Channel 能以仲裁 bitrate + `DataBitrate` 开 CAN FD，发送 `CanFrame.Fd`，读回的帧带 `IsFd` 和硬件 `TimestampMicroseconds`。

**Blocked by:** 02 — 单通道经典收发与 Echo

**Status:** ready-for-agent

- [ ] `Start` 时同时给出 `Bitrate` 与 `DataBitrate`，FlintCAN-FD 该路进入 CAN FD
- [ ] `Send(CanFrame.Fd(...))` 后 `TryRead` 能拿到 `IsFd = true` 的 Echo 和/或 Rx（Loopback 可接受）
- [ ] `BitRateSwitch` / `ErrorStateIndicator` 按发送时的值出现在读回帧上（设备支持的范围内）
- [ ] 读回帧的 `TimestampMicroseconds` 为设备硬件时间戳（非主机随便填的 0，除非设备不报）
- [ ] 不设 `DataBitrate` 时行为仍与 02 的经典通道一致
- [ ] 无 TDC、无原始 bit timing API
