# 04 grilling：程序集、NuGet 与核心类型命名

Type: grilling
Status: claimed
Blocked by: （无）

## Question

对外名字钉成什么？仓库已叫 GsCan.NET，但程序集、NuGet id、根命名空间、核心类型仍可能不一致。

要定的名字：

- 程序集 / NuGet 包 id（`GsCan` / `GsCan.NET` / `GsUsb` / 其他）
- 根命名空间
- 核心类型：Device、Channel、以及「一帧 CAN」的类型名（尚无术语）
- 是否在公共名里出现 `Candle`（暴露 native 血统）还是只出现 `GsUsb` / `GsCan`

本票只命名，不设计方法面。Device / Channel 已在 `CONTEXT.md`；若本票改名，同步改术语表。

## 产出

一组锁定的对外名字，写入本票 Answer，并更新 `CONTEXT.md` 里被改动的词条。
