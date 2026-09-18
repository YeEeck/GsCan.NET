# GsCan.NET

.NET Standard 2.0 主机库：封装 candle.dll，让托管代码接入 gs_usb。本文件只收术语，不收实现。

设备侧（固件）术语以 FlintCAN-FD 的 `CONTEXT.md` 为准，不在这里重复。

## Language

**GsCan.NET**:
本仓库交付的主机库本身。它不是 CAN 分析仪，也不是上位机应用。
_Avoid_: 上位机, 分析仪, 适配器工具

**gs_usb**:
USB 线协议，与 Linux 内核驱动同名。Device 经 WinUSB 与主机交换控制请求和 CAN 帧。
_Avoid_: candle 协议, WinUSB 协议, SocketCAN

**candle.dll**:
Windows 用户态 C API，经 WinUSB 说 gs_usb。本库只封装它，不另写 USB 栈。
_Avoid_: gs_usb.dll, 主机协议栈, candleLight（那是固件）

**Device**:
一块 USB 适配器，可含多路 Channel。
_Avoid_: Adapter, Interface, Dongle, 适配器（当实际指 Channel 时）

**Channel**:
Device 上的一路 CAN 控制器。
_Avoid_: Port, Interface, CAN（当实际指这一路时）

**FlintCAN-FD**:
双路隔离 CAN FD 的 gs_usb 设备；本库的第一等消费面。其他标准 gs_usb 设备（CANable、candleLight 等）保持兼容。
_Avoid_: candleLight（当实际指这块板时）
