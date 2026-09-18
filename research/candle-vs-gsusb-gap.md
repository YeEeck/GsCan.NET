# candle.dll vs gs_usb / FlintCAN-FD — capability gap table

Facts only. Does **not** decide v1 scope (ticket 10).

Sources:

| Layer | Primary source |
|-------|----------------|
| Mainline gs_usb | Linux `drivers/net/can/usb/gs_usb.c` (inventory in [`gs-usb-protocol.md`](gs-usb-protocol.md); Linux 7.3-rc3 / tip `3392698d…`) |
| FlintCAN-FD | `D:\Code\StmProject\flintcan-fd\firmware\include\gs_usb.h`, `config.h` (`BOARD_flintcan_fd`), `firmware\src\can\m_can.c`; host semantics in FlintCAN `CONTEXT.md` (quoted via ticket 02) |
| Schildkroet candle API | `Schildkroet/CANgaroo` `src/driver/CandleApiDriver/api/` — `candle.h`, `candle.c`, `candle_ctrl_req.h`, `candle_ctrl_req.c` (fetched for this ticket); inventory also in [`candle-dll-identity.md`](candle-dll-identity.md) |

Classification legend (per FlintCAN-FD **host-visible** capability unless noted):

| Tag | Meaning |
|-----|---------|
| **DLL exposed** | Schildkroet header exports a callable / typed surface that covers it (function name or documented struct/flag the host can use after `candle_*_read`) |
| **DLL no export** | Protocol and/or FlintCAN-FD has it; wrapping this DLL cannot provide it without rewriting USB (out of destination) |
| **DLL has / Flint unused** | DLL surface exists; this FlintCAN-FD board does not advertise or implement it |

---

## Capability × mainline / FlintCAN-FD / candle.dll

| Capability | Mainline gs_usb | FlintCAN-FD (this board) | Schildkroet candle API | Gap class |
|------------|-----------------|--------------------------|------------------------|-----------|
| **CAN FD** | Yes — feature/mode `FD`, `DATA_BITTIMING`, `BT_CONST_EXT`, FD frame layouts | **Yes** — `CONFIG_CANFD`; features `FD \| BT_CONST_EXT`; BREQs handled | **DLL exposed** — `CANDLE_FEATURE_FD` / `CANDLE_MODE_FD` (0x0100); `candle_channel_set_data_timing`; `candle_fd_frame_send` / `candle_fd_frame_read` (+ FD accessors). No `candle_channel_set_data_bitrate` in current header. No host API to **read** `BT_CONST_EXT` (ctrl layer never issues that BREQ). | DLL covers FD TX/RX + data timing set; extended BT-const **read** is DLL no-export |
| **Dual Channel** | Yes — `DEVICE_CONFIG.icount`, `channel` on frames / BREQs | **Yes** — `NUM_CAN_CHANNEL 2` → `icount=1` | **DLL exposed** — `candle_channel_count`; channel index on timing / start / stop / send | DLL exposed |
| **Hardware timestamp** | Yes — feature/mode `HW_TIMESTAMP`; `timestamp_us` on frames; BREQ `TIMESTAMP` | **Yes** — advertised; host frames use `canfd_ts` size | **DLL exposed** — `CANDLE_FEATURE_HW_TIMESTAMP` / `CANDLE_MODE_HW_TIMESTAMP`; `candle_channel_start` **forces** HW-ts mode bit when capability set; `candle_frame_timestamp_us` / `candle_fd_frame_timestamp_us`; `candle_dev_get_timestamp_us` | DLL exposed |
| **TX echo** | Yes — bulk IN returns Host TX with same `echo_id` (≠ `0xffffffff`) | **Yes** — same wire; semantics: echo = Host TX **finished on device**, not “on bus” (FlintCAN `CONTEXT.md`) | **DLL exposed (partial)** — `candle_frame_type` / `candle_fd_frame_type` → `CANDLE_FRAMETYPE_ECHO` when `echo_id != 0xFFFFFFFF`; `echo_id` field on `candle_frame_t` / `candle_fd_frame_t`. **But** `candle_frame_send` / `candle_fd_frame_send` **overwrite** `echo_id = 0` before OUT — host cannot choose correlation ids through this DLL | Echo **presence** exposed; **host-chosen echo_id** = DLL no-export (forced 0) |
| **OVERFLOW** | Yes — `GS_CAN_FLAG_OVERFLOW` on bulk IN | **Yes** — `can_send_overflow_frame` path | **DLL exposed** — `CANDLE_FRAME_FLAG_OVERFLOW` (0x01) on frame `flags`. No dedicated `candle_frame_is_overflow` helper; host reads `flags`. Enum `CANDLE_FRAMETYPE_TIMESTAMP_OVFL` exists but `candle_frame_type` **never returns it** (only ECHO / ERROR / RECEIVE) | DLL exposed via flag; TIMESTAMP_OVFL enum unused |
| **IDENTIFY** | Yes — feature + BREQ 7 | **Yes** — feature + LED BREQ | Feature bit `CANDLE_FEATURE_IDENTIFY` only. Ctrl comment: BREQ 7 “not used here”. **No** `candle_*identify*` export | **DLL no export** |
| **TDC** | No (outside mainline `FEATURE_MASK`) | **Yes** — `GS_CAN_FEATURE_TDC`; BREQs 17–19 | No TDC symbol / BREQ / export in `candle.h` / `candle_ctrl_req.*` | **DLL no export** (FlintCAN uses it) |
| **Termination** | Yes — feature + BREQ 12/13 | **No** — DIP only; `CONFIG_TERMINATION` not defined; BREQs rejected | Feature bit `CANDLE_FEATURE_TERMINATION` only. Ctrl comment: SET_TERMINATION “not used here”. **No** set/get termination export | Protocol/mainline has it; Flint unused; **DLL no export**. Tag: **DLL has feature bit / Flint unused** + no control API |
| **Filters** | No (Flint/candleLight extension) | **No** — no `CONFIG_CAN_FILTER`; BREQs rejected | **No** filter configure/read export | Neither Flint board nor DLL; extension-only on other boards |
| **GET_STATE** | Yes — feature + BREQ **14** (`state`, `rxerr`, `txerr`) | **Yes** — feature + handler | **DLL exposed** — `candle_channel_get_state` → `candle_can_state_t` (includes `CANDLE_STATE_BUS_OFF`). **Wire caveat:** Schildkroet `candle_ctrl_req.c` defines `CANDLE_BREQ_GET_STATE = 12`, which is mainline/Flint **`SET_TERMINATION`**, not `GET_STATE` (14). DLL also does not surface `rxerr`/`txerr` to the caller (only `state`) | Export exists; BREQ number **mismatches** current protocol (interop risk vs FlintCAN-FD). `rxerr`/`txerr` = DLL no-export |
| **BERR reporting** | Yes — mode/feature `BERR_REPORTING`; SocketCAN error frames on bulk IN. `BREQ_BERR` enum-only in kernel | **Yes** — feature advertised; error frames gated on mode bit in `can_common.c`. `BREQ_BERR` not handled | Feature `CANDLE_FEATURE_BERR_REPORTING` comment: “Unsupported, always enabled”. **No** `CANDLE_MODE_BERR_*` bit. Error frames readable as `CANDLE_FRAMETYPE_ERROR` (`can_id & CANDLE_ID_ERR`) | Passiveive error-frame RX **DLL exposed**; **mode toggle** for BERR_REPORTING = **DLL no export**. `BREQ_BERR` unused by all three |
| **Bus-off observation** | Via error frames (`CAN_ERR_BUSOFF`) + `GET_STATE` → `BUS_OFF` | **Yes** — host-visible Bus-off per FlintCAN glossary (echo drain + Stop + `BUS_OFF` + `CAN_ERR_BUSOFF`) | State enum includes `CANDLE_STATE_BUS_OFF`; error frames via `CANDLE_FRAMETYPE_ERROR` | Observation path **DLL exposed** (subject to GET_STATE BREQ caveat above) |
| **BUS_OFF_RECOVERY** | No (Flint extension, BREQ 32 / feature bit 18) | **Yes** — feature + BREQ → `can_schedule_bus_off_recovery` | No export / no BREQ 32 in ctrl layer | **DLL no export** (FlintCAN uses it) |
| **Channel stop drops unfinished Host TX without echo** | Channel MODE `RESET` stops channel; device-side echo policy is implementation-defined | **Yes** — documented: stop discards unfinished Host TX, **no** TX echo (`can_disable` / FlintCAN `CONTEXT.md`) | **DLL exposed** control: `candle_channel_stop` → MODE RESET. Silent-drop-without-echo is **device** behavior the host will observe; DLL neither documents nor compensates it | Stop API exposed; no-echo-on-drop is protocol/device fact (not a DLL feature to enable) |

---

## Extra rows (useful, not in the mandatory list)

| Capability | Mainline | FlintCAN-FD | candle.dll | Gap class |
|------------|----------|-------------|------------|-----------|
| Listen-only / loopback / one-shot | Yes | Yes (advertised; applied in `can_drv_enable`) | **DLL exposed** — `CANDLE_MODE_*` flags to `candle_channel_start` | DLL exposed |
| Triple sample | Yes | **No** (omitted on M_CAN feature word) | Mode/feature bits present | **DLL has / Flint unused** |
| Pad-to-max-packet | Yes | Yes (advertised) | Mode/feature bits; start flags pass-through | DLL exposed (if host sets flag) |
| USER_ID | Feature bit; BREQs unused in practice | **No** | Feature bit only; no export | Unused by Flint; DLL no export |
| Classic send/recv | Yes | Yes (classic still on wire; FD build uses larger URB size) | **DLL exposed** — `candle_frame_send` / `candle_frame_read` | DLL exposed |

---

## Summary by gap class (FlintCAN-FD–relevant)

### DLL exposed (wrapping can reach)

- CAN FD TX/RX + data-phase timing set; dual channel; HW timestamp; channel start/stop; listen/loopback/one-shot; classic + FD frame I/O
- TX echo **as a frame type** (always with `echo_id==0` from this DLL’s send path)
- OVERFLOW via `CANDLE_FRAME_FLAG_OVERFLOW`
- Error frames (`CANDLE_FRAMETYPE_ERROR`) including bus-off class bits when device sends them
- `candle_channel_get_state` (API surface; see BREQ caveat)

### Protocol/device has it — DLL has no export (cannot provide by wrapping alone)

- **IDENTIFY** (FlintCAN yes)
- **TDC** get/set/const (FlintCAN yes)
- **BUS_OFF_RECOVERY** BREQ (FlintCAN yes)
- **Host-chosen `echo_id`** (wire allows; DLL forces 0)
- **BERR_REPORTING mode toggle** (FlintCAN gates on it; DLL “always enabled” / no mode bit)
- **Termination** get/set (mainline yes; FlintCAN-FD board no)
- **Filters** get/set (extension; FlintCAN-FD board no)
- **`BT_CONST_EXT` read** and **`rxerr`/`txerr`** from GET_STATE payload

### DLL has / FlintCAN-FD does not use

- Triple-sample mode/feature
- Termination **feature bit** (and mainline termination BREQs) — board uses manual DIP
- Filter APIs — N/A on both this board and this DLL
- `CANDLE_FRAMETYPE_TIMESTAMP_OVFL` enum member (unused by `candle_frame_type`)

### Interop footnote (not a missing symbol)

Schildkroet `CANDLE_BREQ_GET_STATE = 12` vs mainline/Flint `GS_USB_BREQ_GET_STATE = 14` (12 = `SET_TERMINATION`). A NuGet that P/Invokes this DLL unchanged may mis-talk GET_STATE to FlintCAN-FD even though the C export exists. Fixing that is a candle-source / packaging concern, not a GsCan.NET USB rewrite — but it is still a factual gap for “wrap this DLL as-is.”

---

## Bottom line

Against FlintCAN-FD’s host-visible set, Schildkroet candle.dll covers the **data path** (dual-channel CAN FD, HW timestamps, frame I/O, echo-as-type, overflow flag, error frames, channel start/stop) and **partial** control (bit timing, data timing, mode flags, state query export). Hard **DLL no-export** gaps for this device are **IDENTIFY**, **TDC**, **BUS_OFF_RECOVERY**, host-chosen **echo_id**, and **BERR_REPORTING** mode control; termination/filters are absent from the DLL and also unused on this board. GET_STATE is exported but its ctrl BREQ numbering disagrees with current gs_usb/FlintCAN.
