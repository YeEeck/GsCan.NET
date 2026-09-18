# Schildkroet candle.dll — identity and API inventory

Primary-source research for ticket `01-research-candle-dll`. Claims below cite the owning repo/file. Secondary blogs are not used as evidence.

## 1. Identity

### What “Schildkroet’s candle.dll” actually is

| Fact | Source |
|---|---|
| The named author on GitHub is user **Schildkroet**. The CAN-related work is repo **`Schildkroet/CANgaroo`** (Qt host tool fork of Hubert Denkmair’s cangaroo, with CAN FD). | https://github.com/Schildkroet/CANgaroo ; README “Credits” / “Forked from Hubert Denkmair / cangaroo” |
| The Windows gs_usb host C API lives at **`src/driver/CandleApiDriver/api/`** (`candle.h`, `candle.c`, `candle_defs.h`, `candle_ctrl_req.*`, `candle_types.h`). | CANgaroo tree; `CandleApiDriver.pri` lists those sources |
| Header copyright line: `Copyright (C) 2016 Hubert Denkmair` and `Copyright (C) 2026 Schildkroet`. | [`candle.h`](https://raw.githubusercontent.com/Schildkroet/CANgaroo/master/src/driver/CandleApiDriver/api/candle.h) |
| CANgaroo **compiles `api/candle.c` into the application**; it does **not** ship a separate prebuilt `candle.dll` in GitHub Releases (latest release assets are only `CANgaroo_x64.zip` / `CANgaroo_x86.zip` app zips). The `.pri` still has a **commented** link line `#win32:LIBS += -L$$PWD/api/ -lcandle_api`, replaced by `SOURCES += $$PWD/api/candle.c`. | `CandleApiDriver.pri`; https://api.github.com/repos/Schildkroet/CANgaroo/releases/latest |
| In Schildkroet’s `candle.h`, `#define DLL` is empty (no `__declspec(dllexport)` / `dllimport`). Symbols are ordinary `bool __stdcall` / etc. when built into CANgaroo. | Schildkroet `candle.h` |

**Naming caveat (do not substitute silently):** there is no published artifact literally named `candle.dll` under Schildkroet’s GitHub account. What the ticket points at is **Schildkroet’s fork of the Candle/candle Windows C API sources** (with FD extensions), historically designed as a DLL API (`__stdcall`, former `candle_api` link). If GsCan.NET needs a real `.dll` binary, it must be **built from that `api/` tree** (or an equivalent packaging of it)—not downloaded as an official Schildkroet release asset today.

### Relationship graph

| Party | Relationship | Source |
|---|---|---|
| **Hubert Denkmair** | Original author of the Windows Candle API and `candle_dll` CMake project; also author of candleLight firmware / cangaroo. Schildkroet’s header still carries his 2016 copyright. | `HubertD/candle_dll`; Schildkroet `candle.h` |
| **`HubertD/candle_dll`** | Standalone CMake project that builds a **shared** library target `candle_api` (Windows output name `candle_api.dll`), linked against **WinUSB** (`target_link_libraries(candle_api … winusb)`). Header uses `CANDLE_API` export macros when `CANDLE_API_LIBRARY` is defined. **No CAN FD / data-bitrate / FD frame APIs** in that header. **No GitHub Releases.** | https://github.com/HubertD/candle_dll `src/CMakeLists.txt`, `src/candle.h`; releases API empty |
| **`candle-usb/candle_api`** | **Does not exist** as a public repo (HTTP 404). Org `candle-usb` currently publishes **`candleLight_fw`** (firmware), not a Windows host DLL. | https://api.github.com/repos/candle-usb/candle_api → 404; https://api.github.com/orgs/candle-usb/repos |
| **candleLight (firmware)** | Device-side gs_usb firmware (STM32). Distinct from the Windows host C API. CONTEXT.md already locks: candleLight ≠ candle.dll. | `candle-usb/candleLight_fw` |
| **8devices USB2CAN** | Commercial USB–CAN product; python-can’s `usb2can` interface loads **`usb2can.dll`** (CANAL API), **not** candle.dll / CandleApi. Separate lineage from Schildkroet/HubertD Candle API. (A different open board named usb2can appears in candleLight_fw supported hardware—roboterclubaachen—not the 8devices CANAL stack.) | python-can usb2can interface; candleLight_fw README hardware list |
| **python-can “loads candle.dll” lead** | **Not confirmed** for current python-can usb2can (uses `usb2can.dll`). Other third-party .NET projects (e.g. elliotwoods/Candle.NET) wrap a DLL named `candle.dll` built from older Cangaroo CandleApiDriver sources—not Schildkroet’s FD fork. | Ticket lead vs. python-can usb2can sources; elliotwoods/Candle.NET README |

## 2. License

| Artifact | License | Source |
|---|---|---|
| Schildkroet `api/` tree (`candle.h` / `candle.c` / …) | **LGPL-3.0-or-later** (`SPDX-License-Identifier: LGPL-3.0-or-later` in `candle.h`; full text in `api/LICENSE`) | Schildkroet `candle.h`, `api/LICENSE` |
| Same `LICENSE` blob as HubertD | Git blob SHA `cf8eac157816812e0806c53a0ddc6ff90dbf1c8b` is identical for HubertD/candle_dll `LICENSE` and Schildkroet `api/LICENSE` | GitHub Contents API `sha` fields |
| CANgaroo application (Qt UI) | **GPL-2.0** (root `LICENSE`) — applies to the app, not a substitute for the `api/` LGPL notice | Schildkroet/CANgaroo `LICENSE` |

### Binary redistribution / NuGet (facts from LGPL-3 text, not legal advice)

LGPL-3.0 allows conveying a Combined Work under terms of your choice if you meet §4 conditions (prominent notices, include GPL-3 + LGPL-3 texts, and either convey Minimal Corresponding Source or use a suitable shared-library mechanism so the user can relink/replace the LGPL portion).

Practical reading for a NuGet that **ships a separate `candle*.dll` built from the `api/` sources** and dynamically P/Invokes it: that matches the shared-library pattern LGPL is designed for, provided notices + license texts + corresponding source (or Equivalent Access Offer) are included. **Static** absorption of the LGPL objects into a closed binary without those accommodations is the harder path.

Schildkroet does not publish a prebuilt DLL today, so any NuGet native asset would be **your build** of LGPL-covered sources, still under those obligations.

## 3. Artifacts

| Question | Finding | Source |
|---|---|---|
| Official prebuilt `candle.dll` from Schildkroet? | **No.** Releases ship CANgaroo app zips only. API is source-compiled into the app. | Releases API; `CandleApiDriver.pri` |
| HubertD prebuilt? | **No** GitHub Releases on `HubertD/candle_dll`. CMake produces shared `candle_api` → Windows name **`candle_api.dll`** (not `candle.dll`). | HubertD releases empty; `CMakeLists.txt` |
| Win32 / x64? | CANgaroo releases provide **both** `CANgaroo_x86.zip` and `CANgaroo_x64.zip` (app, not bare DLL). HubertD CMake does not hard-code arch—build follows the toolchain. | Releases; CMakeLists |
| USB backend | **WinUSB** (`winusb.h` / `WinUsb_*` in `candle_defs.h`; HubertD links `winusb`). **Not libusb** in this Windows API. | Schildkroet `candle_defs.h`; HubertD CMakeLists |
| Header path (Schildkroet) | `src/driver/CandleApiDriver/api/candle.h` (+ `candle_defs.h`, `candle_types.h`, `candle_ctrl_req.h`) | CANgaroo tree |
| Header path (HubertD standalone) | `src/candle.h` in `HubertD/candle_dll` | HubertD tree |

## 4. Exported API list (from Schildkroet `candle.h`)

Calling convention: `__stdcall`. Return type for most ops: `bool` (false → inspect `candle_dev_last_error`). Opaque handles: `candle_list_handle`, `candle_handle`. Types: `candle_err_t`, `candle_frame_t`, `candle_capability_t`, `candle_state_t`, `candle_mode_t`, `candle_bitrate_t` / timing fields via `candle_bittiming_t`.

### Enumerate / open / close / channel count

```c
bool __stdcall candle_list_scan(candle_list_handle *list);
bool __stdcall candle_list_free(candle_list_handle list);
bool __stdcall candle_list_length(candle_list_handle list, uint8_t *len);

bool __stdcall candle_dev_get(candle_list_handle list, uint8_t dev_num, candle_handle *hdev);
bool __stdcall candle_dev_get_state(candle_handle hdev, candle_devstate_t *state);
bool __stdcall candle_dev_get_path(candle_handle hdev, wchar_t *path);  /* length CANDLE_DEV_PATH_SIZE = 255+1 */
bool __stdcall candle_dev_open(candle_handle hdev);
bool __stdcall candle_dev_get_timestamp_us(candle_handle hdev, uint32_t *timestamp_us);
bool __stdcall candle_dev_close(candle_handle hdev);
bool __stdcall candle_dev_free(candle_handle hdev);

bool __stdcall candle_channel_count(candle_handle hdev, uint8_t *num_channels);
```

### Capabilities / timing / start-stop / mode

```c
bool __stdcall candle_channel_get_capabilities(candle_handle hdev, uint8_t ch, candle_capability_t *cap);
bool __stdcall candle_channel_set_timing(candle_handle hdev, uint8_t ch, candle_bittiming_t *data);
bool __stdcall candle_channel_set_bitrate(candle_handle hdev, uint8_t ch, uint32_t bitrate);
bool __stdcall candle_channel_set_data_timing(candle_handle hdev, uint8_t ch, candle_bittiming_t *data);   /* FD data phase — Schildkroet extension vs HubertD */
bool __stdcall candle_channel_set_data_bitrate(candle_handle hdev, uint8_t ch, uint32_t bitrate);       /* FD data phase — Schildkroet extension */
bool __stdcall candle_channel_get_state(candle_handle hdev, uint8_t ch, candle_state_t *state);
bool __stdcall candle_channel_start(candle_handle hdev, uint8_t ch, uint32_t flags);  /* candle_mode_t bits */
bool __stdcall candle_channel_stop(candle_handle hdev, uint8_t ch);
```

### Send / receive / frame accessors / timestamps / errors

```c
bool __stdcall candle_frame_send(candle_handle hdev, uint8_t ch, candle_frame_t *frame);
bool __stdcall candle_frame_read(candle_handle hdev, candle_frame_t *frame, uint32_t timeout_ms);

bool __stdcall candle_fd_frame_send(candle_handle hdev, uint8_t ch, candle_fd_frame_t *frame);  /* Schildkroet FD */
bool __stdcall candle_fd_frame_read(candle_handle hdev, candle_fd_frame_t *frame, uint32_t timeout_ms);

uint8_t*      __stdcall candle_frame_data(candle_frame_t *frame);
uint8_t       __stdcall candle_frame_dlc(candle_frame_t *frame);
uint32_t      __stdcall candle_frame_id(candle_frame_t *frame);
candle_frametype_t __stdcall candle_frame_type(candle_frame_t *frame);
uint32_t      __stdcall candle_frame_timestamp_us(candle_frame_t *frame);

candle_err_t  __stdcall candle_dev_last_error(candle_handle hdev);
```

Source for every signature above: Schildkroet [`candle.h`](https://raw.githubusercontent.com/Schildkroet/CANgaroo/master/src/driver/CandleApiDriver/api/candle.h).

### Mode / feature bit enums (header; used with start flags / capability structs)

- `candle_mode_t`: `NORMAL`, `LISTEN_ONLY`, `LOOP_BACK`, `TRIPLE_SAMPLE`, `ONE_SHOT`, `HW_TIMESTAMP`, **`FD` (0x80000000)** (FD bit is Schildkroet addition vs HubertD header).
- `candle_feature_t` capability bits include: `LISTEN_MODE`, `LOOP_BACK`, `TRIPLE_SAMPLE`, `ONE_SHOT`, `HW_TIMESTAMP`, **`IDENTIFY`**, **`TERMINATION`**, **`GET_STATE`**, **`FD`**.

## 5. Capability boundary

| Capability | In Schildkroet header? | Detail |
|---|---|---|
| **CAN FD** | **Yes** | `CANDLE_FEATURE_FD`, `CANDLE_MODE_FD`, `candle_fd_frame_t`, `candle_channel_set_data_timing` / `set_data_bitrate`, `candle_fd_frame_send` / `candle_fd_frame_read`. Absent from HubertD `candle_dll` header. |
| **Multi-channel** | **Yes** | `candle_channel_count`; channel index on timing/start/stop/send APIs. |
| **Hardware timestamp** | **Yes (mode + feature + accessors)** | `CANDLE_FEATURE_HW_TIMESTAMP`, `CANDLE_MODE_HW_TIMESTAMP`, `candle_frame_timestamp_us`, `candle_dev_get_timestamp_us`. |
| **IDENTIFY** | **Feature bit only — header has no export** | `CANDLE_FEATURE_IDENTIFY` exists. `candle_ctrl_req.h` documents BREQ 7 IDENTIFY as “not used here”. No `candle_*identify*` function. |
| **Bus termination** | **Feature bit only — header has no export** | `CANDLE_FEATURE_TERMINATION`. Ctrl BREQ 11 `SET_TERMINATION` marked “not used here”. No set/get termination export. |
| **TDC** | **Header has no export** | No TDC symbol in `candle.h` / `candle_ctrl_req.h` / driver usage searched. |
| **Acceptance filters** | **Header has no export** | No filter configure/read API in `candle.h`. |
| **State query** | **Yes** | `candle_channel_get_state(hdev, ch, candle_state_t*)`; `CANDLE_FEATURE_GET_STATE`. Also `candle_dev_get_state` for device avail/inuse. |

Internal note (not an export): `candle_ctrl_req.c` implements host control transfers used by the above APIs (bit timing, device mode, bus usage / state, etc.); IDENTIFY and SET_TERMINATION are explicitly unused in that layer.

## 6. HubertD vs Schildkroet API delta (summary)

Schildkroet adds relative to `HubertD/candle_dll` `candle.h`:

- `CANDLE_MODE_FD`, `CANDLE_FEATURE_FD` (+ IDENTIFY / TERMINATION / GET_STATE feature bits already partially present as enums in HubertD)
- `candle_channel_set_data_timing`, `candle_channel_set_data_bitrate`
- `candle_fd_frame_send`, `candle_fd_frame_read`, `candle_fd_frame_t` / FD frame enums
- Copyright year line for Schildkroet; `#define DLL` emptied (HubertD uses `CANDLE_API` dllexport macros)

HubertD alone provides the **ready CMake SHARED library** packaging (`candle_api.dll` + WinUSB link) without FD.

## 7. Bottom line for GsCan.NET

- **Identity to pin:** Windows host C API = Schildkroet’s `CANgaroo` `CandleApiDriver/api` tree (LGPL-3.0-or-later, WinUSB, Hubert Denkmair origin + Schildkroet FD work)—**not** `candle-usb/candle_api` (missing), **not** 8devices/`usb2can.dll`, **not** candleLight firmware.
- **Binary named `candle.dll`:** not published by Schildkroet; must be built (and export macros restored if a real DLL is required).
- **FlintCAN-FD needs (dual channel, CAN FD, HW timestamp):** present as header exports / mode bits in Schildkroet’s API. IDENTIFY / termination / TDC / filters: **no header exports**.
