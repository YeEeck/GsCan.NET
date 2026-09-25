# 能预判的非法就禁用；已交给库的配置必须先 unwind

GsCan View 曾把「现在不能做」混成三种手法：灰掉、LastError、静默 return。决定：会话已经能判定不合法的命令一律禁用，LastError 只留给打到端口才知道的失败（Start 失败、拔线、`Send` 抛 `GsCanException`）；可见按钮禁止静默 no-op。已生效的硬件配置也按 unwind 锁死：Device 已 Open 则锁身份（下拉 / 刷新 / 打开），该路 Channel 已 Running 则锁该路 `ChannelOptions`（改速率 / FD / 只听必须先 Stop）。使能不是预约——Channel 还不能发时勾不上，Stop 仍清掉它。Pause 不参与这套锁。
