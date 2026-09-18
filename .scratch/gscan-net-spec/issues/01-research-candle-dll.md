# 01 research：Schildkroet candle.dll 身份与 API 清单

Type: research
Status: resolved
Blocked by: （无）

## Question

Schildkroet 的 candle.dll 到底是哪份产物？规格要封装的就是这一份，必须先钉死身份和能力。

核实并列出：

1. **身份**：上游仓库 / 作者 / 与 `candle-usb/candle_api`、Hubert Denkmair candleLight、8devices USB2CAN 的关系。用户点名的是 Schildkroet 这一份，不要张冠李戴。
2. **许可证**：源码与二进制再分发条款（能否随 NuGet 附带 x86/x64 DLL）。
3. **产物形态**：官方是否提供预编译 `candle.dll`；Win32 / x64；依赖（WinUSB / libusb）；头文件位置。
4. **导出 API 清单**：设备枚举、打开/关闭、Channel 数量、启动/停止、bitrate / bit timing、发送、接收、时间戳、错误。每个函数给签名级事实（来自头文件，不要凭记忆）。
5. **能力边界**：是否支持 CAN FD、多 Channel、硬件时间戳、IDENTIFY、端接、TDC、滤波、状态查询。有则指出对应 API；无则明确写「头文件无此导出」。

## 背景

目的地已定：Windows 只封装这份 DLL，不自写 USB 栈；FlintCAN-FD 需要双通道 + CAN FD + 硬件时间戳。本票只出事实，不决定公共 API。

线索（待一手核实，不是结论）：`https://github.com/candle-usb/candle_api`；GitHub 用户 Schildkroet；python-can 的 usb2can 接口也曾加载 `candle.dll`。

## 产出

`research/candle-dll-identity.md`：每个调查项给结论，标注一手来源（仓库文件 / 头文件 / LICENSE / 发布资产）。

## Answer

Schildkroet 点名的 Windows 主机 C API 是 **`Schildkroet/CANgaroo`** 里的 `src/driver/CandleApiDriver/api/`（Hubert Denkmair 2016 起源 + Schildkroet 2026 CAN FD 扩展，**LGPL-3.0-or-later**，**WinUSB**）。它目前**静态编进 CANgaroo**，GitHub Releases **没有**独立预编译 `candle.dll`；`candle-usb/candle_api` 仓库不存在；8devices/`usb2can.dll` 与 python-can usb2can 是另一条线。头文件导出含多 Channel、CAN FD（data timing / fd_frame send·read）、硬件时间戳与 `candle_channel_get_state`；**IDENTIFY / 端接 / TDC / 滤波：头文件无此导出**。

完整签名清单与引用来源见 [`research/candle-dll-identity.md`](../../../research/candle-dll-identity.md)。
