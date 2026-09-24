# GsCan View 用 Avalonia，运行时只做 Windows

UI 用 Avalonia，方便以后若库有第二后端时少搬一次窗口。第一版运行时只做 Windows x64（`candle_api.dll` / WinUSB），自包含、exe 旁放 native DLL、zip 分发。不宣传跨平台，不打 x86，不做安装器，不做单文件（P/Invoke 自解压不可靠）。不选 WPF/WinUI：不是因为要在 Linux 上打开 Device，而是不想把 UI 锁死在 Win32 控件树上。
