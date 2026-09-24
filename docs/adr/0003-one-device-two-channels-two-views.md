# 一块 Device、两路 Channel、Trace 与 Latest 并存

GsCan View 一次只打开一块 Device。FlintCAN-FD 的两路 Channel 是对等的：各自 Start/Stop/bitrate/FD/ListenOnly，一条合并 Trace 带 Channel 列，发送槽带 Channel 字段。不把双路拆成两个窗口，也不一次只用一路。接收区可在 Trace（每一帧）和 Latest（按键覆盖）之间切换——只做一种，bring-up 或时序会缺一块。
