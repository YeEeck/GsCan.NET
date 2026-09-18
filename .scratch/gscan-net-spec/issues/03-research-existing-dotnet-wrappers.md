# 03 research：现有 candle/gs_usb 的 .NET 封装

Type: research
Status: resolved
Blocked by: （无）

## Question

.NET 生态里是否已经有可直接采用、或值得当规格对照的 candle.dll / gs_usb 封装？结论要能回答「从零写规格」还是「在某份现成封装上现代化」。

核实：

1. **NuGet / GitHub**：关键词 candle、gs_usb、gsusb、CANable、candleLight 的 C# / .NET 库。每个候选写：仓库、许可证、目标框架（是否 netstandard2.0）、最后提交、CAN FD / 多通道、是否绑死某一份 native DLL。
2. **Schildkroet 自己的封装**：若 Schildkroet 除了 DLL 还有 C# 包装，单独成节，评估能否作为本库起点。
3. **非 .NET 但同 DLL 的绑定**（python-can usb2can / ctypes 等）：只作为 API 面对照，不作为依赖。
4. **成熟度判断**：对每个候选给「可当运行时依赖 / 可当对照不可依赖 / 不可用」，并说明理由（许可证、TFM、FD、维护状态）。

## 背景

目的地是现代 .NET 库规格，不是「找到一个包就收工」。即使存在可用库，也只是复议「是否从零设计公共界面」的事实输入。

## 产出

`research/existing-dotnet-wrappers.md`：候选表 + 逐项结论，一手来源（NuGet 页、仓库 README/csproj、LICENSE）。

## Answer

无有可采纳的 .NET 运行时依赖：NuGet 上无 candle/gs_usb 主机库；唯一正经的托管封装 [elliotwoods/Candle.NET](https://github.com/elliotwoods/Candle.NET)（netstandard2.0、C# MIT）停更于 2021、无 CAN FD、DllImport 钉死 HubertD 系 `Candle.dll`，只配当对照；[CypressControls/CandleLightNet](https://github.com/CypressControls/CandleLightNet) 为 2019 的 net472 C++/CLR + LGPL，不可用。Schildkroet **没有** C# 包装（CANgaroo 是 C++/Qt，其原生 candle.h 才有 FD）。python-can / jxltom/gs_usb / chemicstry/candle_driver 仅作 API 面对照。规格应**从零设计公共界面**，封装 Schildkroet 的 candle.dll。

详情：[`research/existing-dotnet-wrappers.md`](../../../research/existing-dotnet-wrappers.md)
