# 记住上次的表单，但不自动 Open / Start

启动时恢复 Device Path、每路 ChannelOptions、Display Filter、TxSlot。不自动打开设备，也不自动上总线。自动 Start 加上 ListenOnly 默认关，会在插错总线时 ACK。每次空白则和「轻量易用」打架。Open 仍不等于 Start；FD 默认关（经典 500k），Loopback 默认关。
