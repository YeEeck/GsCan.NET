# 11 grilling：剩余雾如何收口

Type: grilling
Status: resolved
Blocked by: （无）

## Question

原计划票已全部关闭。地图 Not yet specified 里还剩七块雾。本票决定每一块是写进规格、还是划出本图，避免暗雾。

要处置：

- 接收模型：事件 / `Channel<T>` / `IObservable` vs 只保留 `TryRead`
- 原始 bit timing vs 只保留整数 bitrate
- 是否暴露底层 candle 句柄
- 热插拔（到达事件 vs 使用中拔掉怎么表现）
- 与 python-can、TSMaster 行为对齐
- 测试策略写不写进规格
- 线程模型（`Send`/`TryRead` 能否跨线程）

## 产出

每块雾的归宿：规格条款，或地图 Out of scope。Not yet specified 在本票关闭时清空或只留仍无法写清的项。

## Answer

2026-09-19 grilling，Q1 A。

**写入规格的条款**

- 热插拔：无到达/离开事件；`List` 是当时快照。使用中断开 → 进行中的调用以 `GsCanException` 失败；不合成帧。
- 线程：同一 `Channel` 上 `Send` 与 `TryRead` 可以跨线程。`Start` / `Stop` / `Dispose` 必须与收发互斥。同一 `Channel` 上两个 `TryRead` 不支持。`List` / `Open` 不保证多线程。
- 验收面：实机 FlintCAN-FD 能走通工作外观（双通道 FD 收发、Echo、Stop 无残留 Echo）。

**本图范围外**

- 事件 / `Channel<T>` / `IObservable`（v1 只有 `TryRead`）
- 原始 bit timing
- 底层 candle 句柄
- 与 python-can / TSMaster 行为对齐
- 模拟设备、对照 python-can 等实现期测试手段

Not yet specified 清空。`CONTEXT.md` 无新术语。
