# 05 grilling：仅 netstandard2.0 还是多目标

Type: grilling
Status: open
Blocked by: （无）

## Question

编译目标怎么切？目的地已经要求 **.NET Standard 2.0** 能用，但「现代化」是否意味着额外打一个现代 TFM（net8/net10），在那个 TFM 上用 `LibraryImport`、`Span<T>` 等？

选项至少包括：

- **只** `netstandard2.0`：一份 API，P/Invoke 走 `DllImport`
- `netstandard2.0` + 一个现代 TFM：同一公共界面，现代 TFM 可换实现细节
- 放弃 netstandard2.0，只打现代 TFM（这会改写目的地，必须显式确认）

约束：调用方包括 .NET Framework 4.6.1+ 与现代 .NET 时，netstandard2.0 是刚需；若没有 Framework 调用方，多目标的代价要说清楚。

## 产出

锁定的 TFM 列表，以及「公共界面是否允许只在现代 TFM 出现的成员」。
