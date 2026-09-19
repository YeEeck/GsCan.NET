# 03 — CAN FD

Parent: [GsCan.NET v1 规格](../../gscan-net-spec/spec.md)

**What to build:** 同一路 Channel 能以仲裁 bitrate + `DataBitrate` 开 CAN FD，发送 `CanFrame.Fd`，读回的帧带 `IsFd` 和硬件 `TimestampMicroseconds`。

**Blocked by:** 02 — 单通道经典收发与 Echo

**Status:** verified (hardware pending)

- [ ] `Start` 时同时给出 `Bitrate` 与 `DataBitrate`，FlintCAN-FD 该路进入 CAN FD
- [ ] `Send(CanFrame.Fd(...))` 后 `TryRead` 能拿到 `IsFd = true` 的 Echo 和/或 Rx（Loopback 可接受）
- [ ] `BitRateSwitch` / `ErrorStateIndicator` 按发送时的值出现在读回帧上（设备支持的范围内）
- [ ] 读回帧的 `TimestampMicroseconds` 为设备硬件时间戳（非主机随便填的 0，除非设备不报）
- [ ] 不设 `DataBitrate` 时行为仍与 02 的经典通道一致
- [x] 无 TDC、无原始 bit timing API

## Comments

- 2026-09-19 编排验证：`dotnet test GsCan.sln` 16 通过。本机无设备，FD 实机项 SKIP。代码：`DataBitrate` → 48 MHz `set_data_timing`（1M/2M）+ `CANDLE_MODE_FD`；`candle_fd_frame_send/read`；reader 已切到 FD 读（经典帧仍可解码）。无公共 TDC/bit-timing 类型。
- 无 `candle_channel_set_data_bitrate`，整数 DataBitrate 在托管侧换成 timing。
