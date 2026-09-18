# 02 research：gs_usb 线协议与 FlintCAN-FD 主机可见子集

Type: research
Status: resolved
Blocked by: （无）

## Question

gs_usb 线上有什么、FlintCAN-FD 实际让主机看见什么？这份清单是后续「DLL 缺口」和「第一版规格范围」的输入，不是 API 设计。

核实并列出：

1. **线协议权威来源**：Linux 内核 `drivers/net/can/usb/gs_usb.c` 与 `include/uapi/linux/gs_usb.h`（或等价 uapi 头）里的 BREQ、feature 位、`gs_host_frame` 布局。版本/提交要写清。
2. **帧与控制面**：经典 CAN vs CAN FD 帧布局、echo_id、OVERFLOW、错误帧、硬件时间戳、多 Channel 字段。
3. **FlintCAN-FD 主机可见子集**：对照 `D:\Code\StmProject\flintcan-fd\firmware\include\gs_usb.h`、`usbd_gs_can.c`、以及 `D:\Code\StmProject\flintcan-fd\CONTEXT.md`。标明该板实现了哪些 BREQ/feature（FD、双通道、HW timestamp、IDENTIFY、TDC、端接、滤波、GET_STATE、BERR 等），哪些只在通用协议里存在。
4. **主机必须理解的语义**（只列事实，不设计 .NET 类型）：TX echo 不是「上了总线」；Channel stop 丢弃未完成 Host TX 且不产生 echo；Bus-off 与 Hardware bus-off 的区别。引用 FlintCAN-FD 术语表，不要改写定义。

## 背景

FlintCAN-FD 是第一等设备。设备侧术语已经钉在 FlintCAN-FD 的 `CONTEXT.md`，本票不要把那些定义抄进 GsCan.NET 的 `CONTEXT.md`。

## 产出

`research/gs-usb-protocol.md`：协议能力表 + FlintCAN-FD 子集表，每条标注一手来源（内核文件路径+版本，或本地固件头文件路径）。

## Answer

Mainline gs_usb 没有独立 uapi 头，线协议就在 Linux 7.3-rc3 的 `drivers/net/can/usb/gs_usb.c`（tip `3392698d…`）：bulk `gs_host_frame`（经典/FD ± HW ts、`echo_id`、OVERFLOW、SocketCAN 错误帧）+ BREQ 0–14 / feature 0–13。FlintCAN-FD 主机可见：双通道 CAN FD、HW timestamp、IDENTIFY、BERR 帧、GET_STATE、TDC、BUS_OFF_RECOVERY；无开关端接、滤波、USER_ID、ELM、`BREQ_BERR`。TX echo ≠ 上总线；Channel stop 丢未完成 Host TX 且无 echo。

完整表与引用：[`research/gs-usb-protocol.md`](../../../research/gs-usb-protocol.md)。
