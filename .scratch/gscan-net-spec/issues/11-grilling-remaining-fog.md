# 11 grilling：剩余雾如何收口

Type: grilling
Status: claimed
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
