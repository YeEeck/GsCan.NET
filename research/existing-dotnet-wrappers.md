# Existing .NET / C# wrappers for candle.dll / gs_usb

Question: should GsCan.NET adopt an existing managed wrapper, or only use candidates as foils while designing the public interface from scratch?

Searched NuGet (`gsusb`, `gs_usb`, `gs-usb`, `canable`, `candlelight`, `CandleApi`, `usb2can`, `Candle.NET`) and GitHub (C# / candle.dll / CandleLightNet / CANable / gs_usb). Primary sources: NuGet query API, GitHub repo metadata, LICENSE / csproj / headers / README in-tree.

## Summary table

| Candidate | Kind | License | TFM | Last meaningful commit | CAN FD | Multi-channel | Native DLL pinning | Verdict |
|---|---|---|---|---|---|---|---|---|
| [elliotwoods/Candle.NET](https://github.com/elliotwoods/Candle.NET) | Pure C# P/Invoke + OO | C#: MIT; bundled native `Candle/`: LGPL-3.0 | **netstandard2.0** | 2021-01-12 | **No** | Yes (`candle_channel_count`) | `DllImport("Candle.dll")`; ships modified HubertD/cangaroo CandleAPI sources | **compare-only** |
| [CypressControls/CandleLightNet](https://github.com/CypressControls/CandleLightNet) | C++/CLR mixed-mode | LGPL-3.0 | **.NET Framework 4.7.2** (`TargetFrameworkVersion`) | 2019-07-11 | **No** (HubertD-era `data[8]`, no `CANDLE_MODE_FD`) | Native API has channel count; managed surface is thin | Embeds HubertD/`candle_dll` C sources into CLR DLL | **unusable** |
| [CypressControls/CypressRobotics.CAN](https://github.com/CypressControls/CypressRobotics.CAN) | Abstract CAN message types (submodule of CandleLightNet) | MIT | (C# helper, 2019) | 2019-07-11 | N/A | N/A | Not a candle/gs_usb host | **compare-only** (message model foil at most) |
| Schildkroet C# wrapper | — | — | — | — | — | — | — | **does not exist** (see §2) |
| NuGet package for candle/gs_usb host | — | — | — | — | — | — | — | **none found** |

Non-.NET (API-surface foils only — not dependencies):

| Binding | Stack | License | Notes | Verdict for GsCan.NET |
|---|---|---|---|---|
| [chemicstry/candle_driver](https://github.com/chemicstry/candle_driver) | Python extension wrapping HubertD candle API C sources | MIT (+ LGPL native under `src/candle_api/`) | Device/Channel, bitrate/timings, classic 8-byte frames | compare-only |
| [jxltom/gs_usb](https://github.com/jxltom/gs_usb) (+ python-can `gs_usb`) | Pure Python via pyusb / WinUSB WCID — **not** candle.dll | MIT | `GS_CAN_MODE_FD` left commented/`#` in constants | compare-only |
| python-can `usb2can` | ctypes → **`usb2can.dll`** (8devices CANAL) | LGPL-3.0 (python-can) | **Not** candle.dll; different vendor DLL | compare-only / do not confuse with candle |

**Bottom line:** there is no adoptable runtime .NET dependency. Design the public interface from scratch. Candle.NET is the only serious managed foil (P/Invoke + Device/Channel). Schildkroet publishes no C# wrapper; their CANgaroo fork’s **native** candle API (with FD) is the DLL surface ticket 01 must pin, not a managed starting point.

---

## 1. NuGet / GitHub .NET candidates

### 1.1 elliotwoods/Candle.NET — compare-only

- **Repo:** https://github.com/elliotwoods/Candle.NET  
- **Stars / activity:** small; last source commit **2021-01-12** (“Improved waiting on Device thread”). Forks since then are packaging/devcontainer noise (e.g. duane-chan ahead only on `.devcontainer` / CMake layout), not FD work.  
- **License:** root [`LICENSE`](https://github.com/elliotwoods/Candle.NET/blob/main/LICENSE) is **MIT** (Elliot Woods, 2020). Bundled native tree [`Candle/LICENSE`](https://github.com/elliotwoods/Candle.NET/blob/main/Candle/LICENSE) is **LGPL-3.0**. GitHub’s SPDX badge may say GPL-3.0 because LGPL text incorporates GPL; treat C# and native licenses separately.  
- **TFM:** [`Candle.NET/Candle.NET.csproj`](https://github.com/elliotwoods/Candle.NET/blob/main/Candle.NET/Candle.NET.csproj) → `<TargetFramework>netstandard2.0</TargetFramework>` (also `Platforms` x86/x64).  
- **NuGet:** **this** project is not published. NuGet id `Candle.NET` is an unrelated SFML 2D lighting library (`sirdx/Candle.NET`). Queries for `gsusb` / `gs_usb` / `canable` / `candlelight` returned **0** host-library hits.  
- **Native pinning:** every export uses `[DllImport("Candle.dll", CallingConvention = CallingConvention.Cdecl)]` in [`NativeFunctions.cs`](https://github.com/elliotwoods/Candle.NET/blob/main/Candle.NET/NativeFunctions.cs). README requires copying built `candle.dll` next to the managed assembly. Native sources are a modified HubertD/cangaroo CandleAPIDriver (credited in README), **not** Schildkroet’s FD-extended fork.  
- **Multi-channel:** yes — `candle_channel_count`, `Device.Channels` dictionary, per-channel `Start`/`Send`/`Receive`.  
- **CAN FD:** **no.** Evidence from primary sources:  
  - `candle_mode_t` has NORMAL / LISTEN_ONLY / LOOP_BACK / TRIPLE_SAMPLE / ONE_SHOT / HW_TIMESTAMP only — **no** `CANDLE_MODE_FD`.  
  - `candle_frame_t.data` is `[MarshalAs(..., SizeConst = 8)]`.  
  - No `candle_channel_set_data_timing` / `candle_fd_frame_*` imports.  
- **Managed shape (foil value):** background RX thread, non-blocking `Channel.Send` / `Receive` via `BlockingCollection`, `Device.ListDevices()` / `Open` / `Close`, bitrate helper + raw timing. Useful as a foil for P/Invoke marshalling and a Device/Channel split — not as a dependency (stale, classic-CAN-only, wrong native lineage for FlintCAN-FD).

**Verdict: compare-only.** Do not take as runtime dependency. netstandard2.0 + MIT C# are fine on paper; maintenance, no FD, and HubertD-era DLL pin disqualify adoption.

### 1.2 CypressControls/CandleLightNet — unusable

- **Repo:** https://github.com/CypressControls/CandleLightNet  
- **README (primary):** “uses C++/CLR to wrap the [candle_dll](https://github.com/HubertD/candle_dll)” for .NET languages; license “LGPL V3” because of candle_dll.  
- **Last commit:** 2019-07-11 (initial + README/license).  
- **TFM:** [`CandleLightNet.vcxproj`](https://github.com/CypressControls/CandleLightNet/blob/master/src/CandleLightNet.vcxproj) → `TargetFrameworkVersion` **v4.7.2**, `CLRSupport` true, `ConfigurationType` DynamicLibrary. **Not** netstandard2.0; Windows-only mixed-mode assembly.  
- **CAN FD:** embedded [`src/candle.h`](https://github.com/CypressControls/CandleLightNet/blob/master/src/candle.h) matches classic HubertD API (`uint8_t data[8]`, modes without FD).  
- **Managed API:** thin `CandleDevice` ref class (`open`, `sendMessage`, `CANMessageReceived` event) depending on submodule `CypressRobotics.CAN`.  
- **NuGet:** none.

**Verdict: unusable** for GsCan.NET (wrong TFM, CLR mixed-mode, LGPL viral surface, abandoned, no FD). At most a historical foil that CLR wrapping was tried and abandoned.

### 1.3 CypressControls/CypressRobotics.CAN — compare-only (peripheral)

- Submodule of CandleLightNet; MIT; last push 2019-07-11. Abstract CAN message types for adapters — **not** a candle/gs_usb host. Foil only if comparing message DTOs.

### 1.4 Other NuGet / GitHub noise (rejected)

| Hit | Why rejected |
|---|---|
| NuGet `Candle.NET` (SFML lighting) | Unrelated product name |
| Peak / Ixxat / Oakrey.Peak.Can.Driver NuGet packages | Different hardware stacks (PCAN etc.), not gs_usb/candle |
| industREAL.CAN.CANGW | Own UDP/COBS gateway protocol, not candle.dll |
| Schildkroet/Candle, Candle2 | **GRBL CNC** Qt apps — name collision only |
| sirdx/Candle.NET and other “Candle*” finance/graphics repos | Unrelated |

---

## 2. Schildkroet’s own C# wrapper

**None found.**

Schildkroet public repos relevant to CAN/gs_usb (GitHub user listing + language/tree scan):

| Repo | Language | Role | C# files |
|---|---|---|---|
| [Schildkroet/CANgaroo](https://github.com/Schildkroet/CANgaroo) | C++/C (Qt) | Fork of cangaroo analyzer; **embeds** `src/driver/CandleApiDriver/api/` (candle.c/h) | **0** |
| [Schildkroet/gs_usb_device_driver](https://github.com/Schildkroet/gs_usb_device_driver) | C | TinyUSB **device-side** gs_usb class driver | **0** |
| Schildkroet/Candle, Candle2 | C++/Qt | GRBL controllers — not CAN | N/A |

CANgaroo application license is **GPL-2.0**; the embedded candle API directory carries **LGPL-3.0**. Releases ship Windows Qt zips — not a redistributable managed library or documented standalone `candle.dll` NuGet.

**FD note (native only, for foil / ticket-01 cross-link):** Schildkroet CANgaroo’s [`candle.h`](https://github.com/Schildkroet/CANgaroo/blob/master/src/driver/CandleApiDriver/api/candle.h) **does** expose CAN FD (`CANDLE_FEATURE_FD`, `CANDLE_MODE_FD`, `candle_fd_frame_t` with `data[64]`, `candle_channel_set_data_timing`, `candle_fd_frame_send` / `read`). That strengthens “wrap Schildkroet’s native DLL” as the GsCan.NET plan — it does **not** give a C# starting point.

**Could it be the starting point for GsCan.NET?** **No.** There is no managed wrapper to fork. At best, P/Invoke signatures should be derived from Schildkroet’s FD-capable headers (after ticket 01 pins the exact DLL identity), while the **public** C# API is designed fresh.

---

## 3. Non-.NET bindings (API-surface comparison only)

### 3.1 chemicstry/candle_driver

- https://github.com/chemicstry/candle_driver — MIT wrapper; embeds HubertD-lineage candle API (LGPL) under `src/candle_api/`.  
- Last meaningful activity ~2022. Classic CAN only (`data[8]`, no FD mode in header).  
- Surface (from `example.py`): `list_devices()` → `device.open()` → `device.channel(0)` → `set_bitrate` / `set_timings` → `start` → `write` / `read` → `stop` / `close`; exposes `channel_count`, path, timestamp.  
- Closest behavioral cousin to Candle.NET’s Device/Channel model; confirms that pattern is common for this DLL family.

### 3.2 jxltom/gs_usb (+ python-can `interface="gs_usb"`)

- https://github.com/jxltom/gs_usb — MIT; setup.py `license="MIT"`.  
- Talks gs_usb over **pyusb** (usbfs / WinUSB WCID), **not** via candle.dll.  
- `GS_CAN_MODE_FD` appears **commented out** in `gs_usb/constants.py` — FD not implemented as a first-class host feature here.  
- python-can docs: `can.Bus(interface="gs_usb", channel=..., bitrate=...)`. Useful for host-behavior alignment discussions (enumerate, bitrate, frame flags), not for P/Invoke design.

### 3.3 python-can `usb2can`

- Loads **`usb2can.dll`** via `ctypes.windll.LoadLibrary` and the CANAL API (`CanalOpen` / `CanalSend` / …) — see [`usb2canabstractionlayer.py`](https://github.com/hardbyte/python-can/blob/main/can/interfaces/usb2can/usb2canabstractionlayer.py) and [docs](https://github.com/hardbyte/python-can/blob/main/doc/interfaces/usb2can.rst).  
- **Does not wrap candle.dll.** Do not treat as a candle binding foil; at most a “Windows vendor DLL + ctypes” pattern reference.

---

## 4. Verdicts (maturity)

| Candidate | Verdict | Reasons |
|---|---|---|
| elliotwoods/Candle.NET | **compare-only** | netstandard2.0 + MIT C# OK; **no FD**; stale since 2021; pinned to HubertD-era `Candle.dll` (not Schildkroet FD API); unpublished on NuGet; LGPL native still a redistribution concern if vendoring their DLL build |
| CypressControls/CandleLightNet | **unusable** | net472 C++/CLR only; LGPL-3.0; abandoned 2019; no FD; mixed-mode packaging unfit for a modern netstandard2.0 library |
| CypressRobotics.CAN | **compare-only** | Not a host driver; stale message helpers |
| Schildkroet C# | **unusable / N/A** | No C# wrapper exists; cannot be a managed starting point |
| Any NuGet candle/gs_usb host | **unusable / N/A** | No package found |
| chemicstry / jxltom / python-can | **compare-only** | Wrong language / wrong transport for a candle.dll P/Invoke library; useful for Device/Channel and bus-behavior foils only |

### Spec implication

GsCan.NET should **design the public interface from scratch** (modern netstandard2.0 host over Schildkroet’s candle.dll). Existing managed code is foil material only: Candle.NET for P/Invoke + Device/Channel shape; non-.NET bindings for behavioral comparison; Schildkroet’s **native** FD headers for what the DLL must expose — not for copying a C# façade.
