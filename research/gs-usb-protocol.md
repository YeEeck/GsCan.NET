# gs_usb wire protocol and FlintCAN-FD host-visible subset

Question: what is on the gs_usb wire, and what does FlintCAN-FD actually let a host see?

This note is facts only — not a .NET API design.

## Protocol authority

| Fact | Source |
|------|--------|
| There is **no** separate `include/uapi/linux/can/gs_usb.h` (or `linux/gs_usb.h`) on mainline. Wire structs, BREQ enum, and feature bits live **inside** the driver. | Torvalds tree: `drivers/net/can/usb/gs_usb.c` (fetched from [git.kernel.org plain](https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/plain/drivers/net/can/usb/gs_usb.c)); `include/uapi/linux/can/gs_usb.h` → HTTP 404 |
| Snapshot used: Linux **7.3.0-rc3** (“Baby Opossum Posse”); tip of `gs_usb.c` content matching that tree. Latest dedicated `gs_usb.c` commit on master: `3392698d3c1db2b27b97ba025324e2b644f419f7` (2026-08-17, typo fix only). | `Makefile` VERSION/PATCHLEVEL/SUBLEVEL/EXTRAVERSION; [commit](https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git/commit/?id=3392698d3c1db2b27b97ba025324e2b644f419f7) |
| FlintCAN-FD firmware carries an **extended** protocol header (beyond mainline) used as the device-side contract. | `D:\Code\StmProject\flintcan-fd\firmware\include\gs_usb.h` |
| Host↔device bulk endpoints (FlintCAN / candleLight lineage): IN `0x81`, OUT `0x02`. | `D:\Code\StmProject\flintcan-fd\firmware\include\gs_usb.h` (`GSUSB_ENDPOINT_IN` / `OUT`) |

Byte order: original Geschwister Schneider USB2CAN negotiated host byte order via `GS_USB_BREQ_HOST_FORMAT` / `gs_host_config.byte_order`. candleLight-line firmware (including FlintCAN-FD) ignores that negotiation and always uses **little-endian**. Kernel comment + FlintCAN EP0 handler: `gs_usb.c` host_config comment; `usbd_gs_can.c` `GS_USB_BREQ_HOST_FORMAT` case is a no-op.

## Frame plane (`gs_host_frame` on bulk IN/OUT)

Layout (packed, 4-byte aligned header) from kernel `struct gs_host_frame` and FlintCAN `gs_usb.h`:

| Field | Size | Meaning | Source |
|-------|------|---------|--------|
| `echo_id` | u32 | Host-chosen TX correlation id on OUT; echoed on IN when that Host TX finishes. `0xffffffff` (`GS_HOST_FRAME_ECHO_ID_RX`) = bus RX / error / overflow notification, not a TX echo. | `gs_usb.c` (`GS_HOST_FRAME_ECHO_ID_RX`, RX callback); FlintCAN `gs_usb.h` |
| `can_id` | u32 LE | Same flag bits as Linux `linux/can.h`: EFF / RTR / ERR in high bits. Error frames use `CAN_ERR_*` classes in id + classic 8-byte payload. | `gs_usb.c` comment “same flags and masks as linux/can.h”; FlintCAN `gs_usb.h` |
| `can_dlc` | u8 | Classic DLC or CAN FD DLC code | both |
| `channel` | u8 | Channel index on multi-channel devices | both |
| `flags` | u8 | See flag table | both |
| `reserved` | u8 | Padding / reserved | both |
| payload union | classic 8 / classic+ts / FD 64 / FD+ts (+ quirk variants in kernel) | Data + optional `timestamp_us` (µs) when HW timestamp feature is on | both |

### Frame flags

| Bit | Name | Role | Source |
|-----|------|------|--------|
| 0 | `GS_CAN_FLAG_OVERFLOW` | Device lost RX; host treats as RX overflow error. May accompany a normal RX or stand alone (`echo_id=RX`, dlc 0). | `gs_usb.c` RX path; FlintCAN `can_send_overflow_frame` |
| 1 | `GS_CAN_FLAG_FD` | CAN FD frame (64-byte data path) | both |
| 2 | `GS_CAN_FLAG_BRS` | Bit rate switch (FD) | both |
| 3 | `GS_CAN_FLAG_ESI` | Error state indicator (FD) | both |

Classic vs FD sizing (kernel RX minimum length logic):

- Classic RX without HW ts: header + data length (RTR → no data).
- Classic with HW ts: full `classic_can_ts` size (data[8] + `timestamp_us`).
- FD RX without HW ts: header + FD data length; with HW ts: full `canfd_ts`.
- FlintCAN-FD always builds with `CONFIG_CANFD` → host frame size is `canfd_ts` (`usbd_gs_can.h` `GS_HOST_FRAME_SIZE`).

Error frames: classic layout, `can_id & CAN_ERR_FLAG`, `can_dlc = 8`, payload carries SocketCAN error detail bytes. Kernel updates controller state from them; FlintCAN emits them when `GS_CAN_FEATURE_BERR_REPORTING` is enabled on the channel (`can_common.c`).

## Control plane (USB class/vendor BREQ)

`wValue`: `1` for device-wide `HOST_FORMAT` / `DEVICE_CONFIG`; otherwise **channel index**. FlintCAN validates channel for all other BREQs (`usbd_gs_can.c`).

### Mainline kernel BREQ list (`enum gs_usb_breq` in `gs_usb.c`)

| # | Name | Direction (typical) | Notes | In mainline driver? |
|---|------|---------------------|-------|---------------------|
| 0 | `HOST_FORMAT` | H→D | byte_order; candleLight/Flint no-op | yes |
| 1 | `BITTIMING` | H→D | classic / arbitration timing | yes |
| 2 | `MODE` | H→D | `RESET`/`START` + feature flags | yes |
| 3 | `BERR` | — | Enum only; **no** send/recv call sites in `gs_usb.c` | enum only |
| 4 | `BT_CONST` | D→H | feature bitmask + fclk + timing limits | yes |
| 5 | `DEVICE_CONFIG` | D→H | `icount` (= channels−1), sw/hw version | yes |
| 6 | `TIMESTAMP` | D→H | device µs timestamp (kernel uses for HW ts clock) | yes |
| 7 | `IDENTIFY` | H→D | blink channel LED | yes (if feature) |
| 8 | `GET_USER_ID` / quirk alias `QUIRK_CANTACT_PRO_DATA_BITTIMING` | — | USER_ID not generally used; CANtact Pro reuses #8 for data bittiming | quirk |
| 9 | `SET_USER_ID` | — | not used by mainline path | enum |
| 10 | `DATA_BITTIMING` | H→D | FD data-phase timing | yes (FD) |
| 11 | `BT_CONST_EXT` | D→H | + data-phase bt const | yes (FD) |
| 12 | `SET_TERMINATION` | H→D | | yes (if feature) |
| 13 | `GET_TERMINATION` | D→H | | yes (if feature) |
| 14 | `GET_STATE` | D→H | `state`, `rxerr`, `txerr` | yes (if feature) |

### Feature bits (advertise in `BT_CONST` / `BT_CONST_EXT`; request in `MODE.flags`)

Mainline mask: `GS_CAN_FEATURE_MASK = GENMASK(13, 0)`.

| Bit | Name | Mainline | Role |
|-----|------|----------|------|
| 0 | `LISTEN_ONLY` | yes | mode |
| 1 | `LOOP_BACK` | yes | mode |
| 2 | `TRIPLE_SAMPLE` | yes | mode |
| 3 | `ONE_SHOT` | yes | mode |
| 4 | `HW_TIMESTAMP` | yes | mode; appends `timestamp_us` |
| 5 | `IDENTIFY` | yes | capability (BREQ 7) |
| 6 | `USER_ID` | yes (bit); BREQ unused in practice | |
| 7 | `PAD_PKTS_TO_MAX_PKT_SIZE` | yes | mode |
| 8 | `FD` | yes | capability + mode |
| 9 | `REQ_USB_QUIRK_LPC546XX` | yes | padding quirk |
| 10 | `BT_CONST_EXT` | yes | capability |
| 11 | `TERMINATION` | yes | capability |
| 12 | `BERR_REPORTING` | yes | mode: error frames on bulk IN |
| 13 | `GET_STATE` | yes | capability |

FlintCAN / candleLight **extensions** (in firmware `gs_usb.h`, **outside** mainline `FEATURE_MASK`):

| Bit | Name | Extra BREQs |
|-----|------|-------------|
| 14–15 / ELM | `ELM_PROTOCOL` and Elmue-specific aliases | `ELM_*` 20–27 |
| 16 | `FILTER` | `SET_FILTER` / `GET_FILTER` (15/16) |
| 17 | `TDC` | `GET_TDC_CONST` / `SET_TDC` / `GET_TDC` (17–19) |
| 18 | `BUS_OFF_RECOVERY` | `BUS_OFF_RECOVERY` = 32 |

## FlintCAN-FD host-visible subset

Board: `BOARD_flintcan_fd` — dual FDCAN, `CONFIG_CANFD=1`, **no** `CONFIG_TERMINATION`, **no** `CONFIG_CAN_FILTER`, `CAN_CLOCK_SPEED=48e6`, `NUM_CAN_CHANNEL=2`. Sources: `D:\Code\StmProject\flintcan-fd\firmware\include\config.h`; device config `icount = NUM_CAN_CHANNEL - 1` → **1** (two channels) in `usbd_gs_can.c`; features from `D:\Code\StmProject\flintcan-fd\firmware\src\can\m_can.c` `CAN_btconst` / `CAN_btconst_ext`.

### Capability / BREQ matrix (this board)

| Item | On FlintCAN-FD? | Evidence |
|------|-----------------|----------|
| Dual channel | **yes** (`icount=1`) | `config.h` `NUM_CAN_CHANNEL 2`; `USBD_GS_CAN_dconf` |
| CAN FD + data bittiming + `BT_CONST_EXT` | **yes** | `CONFIG_CANFD`; `m_can.c` `FD \| BT_CONST_EXT`; BREQs handled in `usbd_gs_can.c` |
| HW timestamp | **yes** (advertised; frames use `*_ts`) | `m_can.c` feature; `GS_HOST_FRAME_SIZE` → `canfd_ts` |
| Identify | **yes** | feature + `GS_USB_BREQ_IDENTIFY` LED sequence |
| Listen-only / loopback / one-shot / pad-to-max | **yes** | `m_can.c` feature bits; applied in `can_drv_enable` |
| Triple sample | **no** (not advertised on M_CAN path) | `m_can.c` feature list omits it |
| BERR reporting (error frames on IN) | **yes** | `GS_CAN_FEATURE_BERR_REPORTING` in `m_can.c`; gated in `can_common.c` |
| `GS_USB_BREQ_BERR` control request | **no** (enum present, EP0 `default` → fail) | FlintCAN `gs_usb.h` enum; `usbd_gs_can.c` switch has no case |
| GET_STATE | **yes** | feature + BREQ handler |
| TDC (const/set/get) | **yes** | `CONFIG_CANFD` → `GS_CAN_FEATURE_TDC`; BREQs 17–19 |
| BUS_OFF_RECOVERY BREQ (32) | **yes** | feature + handler → `can_schedule_bus_off_recovery(..., 0)` |
| Termination get/set | **no** | comment “Manual DIP… do NOT define CONFIG_TERMINATION”; BREQs rejected |
| HW filter get/set | **no** | no `CONFIG_CAN_FILTER` on this board; BREQs rejected |
| USER_ID get/set | **no** | header marks not implemented; no EP0 cases |
| ELM_* BREQs / features | **no** (header placeholders only) | enum 20–31; not in EP0 switch |
| LPC546XX USB quirk / CANtact Pro quirk | **no** | not in `m_can.c` feature word |

### Host-must-understand semantics (FlintCAN-FD glossary — quoted, not redesigned)

From `D:\Code\StmProject\flintcan-fd\CONTEXT.md` (do **not** copy these definitions into GsCan.NET `CONTEXT.md`):

- **TX echo**: “USB IN 上带回主机原来那个 `echo_id` 的 `gs_host_frame`。它表示设备已经结束这一帧 Host TX，**不是「上了总线」**。”
- **Channel stop**: “主机把通道关掉。**尚未结束的 Host TX 丢弃，不产生 TX echo**。” Firmware: `can_disable` purges from-host / to-host lists and splices `list_tx_echo` back to the frame pool with no IN echo (`can_common.c`).
- **Hardware bus-off**: “Armed 之后控制器新进入不能上总线。从这一刻起不再把 Queued Host TX 交给控制器，也不再发 BERR….”
- **Bus-off** (host-visible): “主机看见的通道状态：In-flight Host TX 已全部 TX echo、控制器已 `Stop`、软件状态为 `BUS_OFF`、并已发出 `CAN_ERR_BUSOFF`。”
- **Bus-off recovery**: firmware schedules recovery 100 ms after Bus-off commit; host `GS_USB_BREQ_BUS_OFF_RECOVERY` only advances that schedule (`CONTEXT.md`; `CAN_BUS_OFF_RESTART_DELAY_MS` in `can_common.c`).

## Summary gist

Mainline gs_usb is defined only in `drivers/net/can/usb/gs_usb.c` (Linux 7.3-rc3 / tip `3392698d…`): bulk `gs_host_frame` (classic/FD ± HW ts, echo_id, OVERFLOW, SocketCAN error frames) plus BREQs 0–14 / features 0–13. FlintCAN-FD exposes dual-channel CAN FD with HW timestamp, identify, BERR frames, GET_STATE, TDC, and bus-off recovery; it does not expose switchable termination, filters, USER_ID, ELM, or `BREQ_BERR`. Host must treat TX echo as “Host TX finished on device,” not on-bus success, and expect silent drop of unfinished Host TX on channel stop.
