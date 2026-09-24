# 03: 按路 Start / Stop

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 每路 Channel 可独立 Start / Stop。仲裁 bitrate 只能选 native 认的档，默认 500k。FD 默认关；勾上后数据段只能选 1M / 2M / 4M，默认 2M。ListenOnly、Loopback 一等且默认关。OneShot 在「更多」里且默认关。Start 失败时状态栏给出 `GsCanException` 说明，该路仍显示未在跑。Stop 不报错。

**Blocked by:** 02 列出、打开、关闭 Device

**Status:** ready-for-agent

- [x] 一路 Start 不影响另一路仍停着或仍在跑
- [x] 仲裁下拉仅为 10k / 20k / 50k / 83.333k / 100k / 125k / 250k / 500k / 800k / 1M，默认 500k
- [x] FD 默认关；打开 FD 后数据段为 1M / 2M / 4M，默认 2M
- [x] ListenOnly、Loopback 默认关且好看见；OneShot 默认关且在「更多」
- [x] 没有 IDENTIFY、端接、硬件滤波、`Channel.State` 控件
- [x] Start 失败：状态栏有说明，该路未在跑；Stop 不把关机当错误
- [x] 会话面测试覆盖默认值与「Open 后未 Start」；实机可 SKIP

## Comments

- Implemented on `feat/gscan-view-03-channel-start-stop` (`725000a`), merged to `feat/gscan-view-v1`. Classic Start leaves DataBitrate null. Hardware Start/Stop SKIP when no device.
