# GsCan View

基于 GsCan 的 Windows x64 桌面查看器，把 Device / Channel / CanFrame 呈现为一个窗口。

## 解压并运行

1. 解压 `GsCan.View-win-x64.zip`
2. 进入解压目录，运行 `GsCan.View.exe`

该包为自包含发布，无需预先安装 .NET 桌面运行时。`candle_api.dll` 必须与 exe 位于同一目录。

## Native 动态库

`candle_api.dll` 是采用 LGPL-3.0-or-later 的 Candle Windows API。本程序以动态链接方式使用它，你可以按许可证条款替换这份 DLL。来源见包内 `SOURCE.txt`，许可证文本见 `LGPL-3.0.txt` 与 `GPL-3.0.txt`。

CANgaroo 的 GPL 应用程序未包含在内。
