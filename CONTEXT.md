# GsCan.NET

.NET Standard 2.0 主机库：封装 candle.dll，让托管代码接入 gs_usb。本文件只收术语，不收实现。

设备侧（固件）术语以 FlintCAN-FD 的 `CONTEXT.md` 为准，不在这里重复。

## Language

**GsCan.NET**:
本仓库与产品标题。它是主机库，不是 CAN 分析仪，也不是上位机应用。
_Avoid_: 上位机, 分析仪, 适配器工具, GsCan（当实际指仓库名或产品标题时）

**GsCan**:
公共代码身份：NuGet 包 id、程序集 `GsCan.dll`、根命名空间。
_Avoid_: GsUsb, GsCan.NET（当实际指包 id 或命名空间时）, Candle, Candle.NET

**gs_usb**:
USB 线协议，与 Linux 内核驱动同名。Device 经 WinUSB 与主机交换控制请求和 CAN 帧。
_Avoid_: candle 协议, WinUSB 协议, SocketCAN

**candle.dll**:
Windows 用户态 C API，经 WinUSB 说 gs_usb。本库只封装它，不另写 USB 栈。
_Avoid_: gs_usb.dll, 主机协议栈, candleLight（那是固件）

**Device**:
一块 USB 适配器，可含多路 Channel。公共类型名即 `Device`。
_Avoid_: Adapter, Interface, Dongle, CandleDevice, GsDevice, 适配器（当实际指 Channel 时）

**Channel**:
Device 上的一路 CAN 控制器。公共类型名即 `Channel`。
_Avoid_: Port, Interface, CAN, CanChannel, CandleChannel, GsChannel（当实际指这一路时）

**CanFrame**:
主机看见的一帧 CAN。经典、FD、Echo、错误帧都是它的种类，不是另一套类型名。
_Avoid_: CanMessage, Message, Packet, GsFrame, GsHostFrame, CandleFrame

**Echo**:
一次 `Send` 在设备上结束时，`TryRead` 拿到的那种 `CanFrame`。不是上了总线。同一 Channel 上按 FIFO 对 `Send`；`Stop` 之后未对上的 `Send` 不会再出 Echo。
_Avoid_: TX 成功, 上了总线, 发送成功, TX echo（当实际指这个库的种类名时）

**Overflow**:
某帧上的标志：设备丢掉了 RX。不是独立类型，也不是异常。
_Avoid_: 溢出事件, 溢出异常

**FlintCAN-FD**:
双路隔离 CAN FD 的 gs_usb 设备；本库的第一等消费面。其他标准 gs_usb 设备（CANable、candleLight 等）保持兼容。
_Avoid_: candleLight（当实际指这块板时）
