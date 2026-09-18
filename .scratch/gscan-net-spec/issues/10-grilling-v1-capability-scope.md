# 10 grilling：第一版规格的 gs_usb 能力范围

Type: grilling
Status: open
Blocked by: 06

## Question

第一版规格承诺哪些 gs_usb 能力？「06 research：candle.dll 对 gs_usb 的能力缺口」会标出 DLL 做不到的项——那些项若仍要进第一版，等于改写目的地（去自写 USB），必须显式拒绝或改图。

逐项纳入 / 推迟（推迟的写进地图 Out of scope 或 Not yet specified，不要留成暗雾）：

- CAN FD
- 双 Channel
- 硬件时间戳
- IDENTIFY
- 软件端接
- TDC
- 硬件滤波
- GET_STATE / BERR reporting

已锁定、不可推迟：FlintCAN-FD 作为第一等设备所需的双通道、CAN FD、硬件时间戳——若 DLL 缺口证明做不到，本票要决定是缩小目的地还是改接入路径，而不是悄悄丢掉。

## 产出

第一版能力清单 + 明确推迟项；若缺口迫使改目的地，先改地图 Destination，再关票。
