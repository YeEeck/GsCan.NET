# 06 research：candle.dll 对 gs_usb 的能力缺口

Type: research
Status: open
Blocked by: 01, 02

## Question

把「01 research：Schildkroet candle.dll 身份与 API 清单」的 DLL 导出面，对着「02 research：gs_usb 线协议与 FlintCAN-FD 主机可见子集」的协议/设备面，列出缺口。

对 FlintCAN-FD 用到的每一项主机可见能力，判定：

- candle.dll **已暴露**（给出函数名）
- 协议/设备有，**DLL 没有**（封装层无法通过这份 DLL 提供，除非改目的地去自写 USB）
- DLL 有，FlintCAN-FD **不用**（可进库也可藏）

重点覆盖：CAN FD、双 Channel、硬件时间戳、TX echo、OVERFLOW、IDENTIFY、TDC、端接、滤波、GET_STATE、BERR、Bus-off 相关。

本票不决定第一版做哪些——那是「10 grilling：第一版规格的 gs_usb 能力范围」。本票只出缺口表。

## 产出

`research/candle-vs-gsusb-gap.md`：能力 ×（协议 / FlintCAN-FD / candle.dll）对照表。
