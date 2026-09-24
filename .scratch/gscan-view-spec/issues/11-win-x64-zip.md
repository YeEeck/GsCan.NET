# 11: win-x64 zip

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** 第一版以 Windows x64 自包含 zip 分发：解压即可跑，exe 旁边是 `candle_api.dll`。不打 x86、不做安装器、不做单文件。不宣传跨平台。native 仍是可替换的动态库（LGPL）。

**Blocked by:** 09 拔掉、状态栏, 10 记住表单，不上总线

**Status:** ready-for-agent

- [x] 产出 win-x64 自包含 zip，不含安装器、不含单文件包
- [x] 解压后 exe 旁能加载 native，不必先装桌面运行时
- [x] 不附带 x86 作为第一版交付；介绍里不写 Linux / macOS
- [x] 不把 CANgaroo 那个 GPL 应用打进包

## Comments

- Implemented on `feat/gscan-view-11-win-x64-zip` (`9f912a9`), merged to `feat/gscan-view-v1`. `scripts/publish-view.ps1` publishes self-contained win-x64 zip with `candle_api.dll` beside the exe.
