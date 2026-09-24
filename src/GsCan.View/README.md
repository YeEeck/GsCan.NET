# GsCan View

消费 GsCan 的 Windows x64 桌面查看器。把 Device / Channel / CanFrame 变成一个窗口。

## 解压运行

1. 解压 `GsCan.View-win-x64.zip`
2. 打开解压目录，运行 `GsCan.View.exe`

包是自包含的，不必先装 .NET 桌面运行时。`candle_api.dll` 必须和 exe 放在同一目录。

## Native 动态库

`candle_api.dll` 是 LGPL-3.0-or-later 的 Candle Windows API。应用动态链接它，你可以按许可证替换这份 DLL。对应来源见包内 `SOURCE.txt`，许可证见 `LGPL-3.0.txt` 与 `GPL-3.0.txt`。

不附带 CANgaroo 那个 GPL 应用。
