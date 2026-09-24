# 10: 记住表单，不上总线

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 下次启动恢复上次的 Device Path、每路 ChannelOptions、Display Filter、TxSlot。恢复之后仍然不自动 Open、不自动 Start。插错总线时不会自己 ACK。

**Blocked by:** 06 Display Filter, 07 周期 TxSlot

**Status:** ready-for-agent

- [ ] 重启后 Path、bitrate / FD / ListenOnly / Loopback / OneShot、Display Filter、TxSlot 表与上次一致
- [ ] 启动后 Device 未打开、各路未 Start
- [ ] 会话面测试不要求实机：写入一组配置、新建会话后读回且未 Start
