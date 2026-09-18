# 07 prototype：公共 API 外观草稿

Type: prototype
Status: claimed
Blocked by: 01, 02, 03, 06

## Question

调用方看到的模块界面长什么样？用一份可编译的 C# 草稿（抛掷型，不是实现）让人能反应「这样用对不对」。

草稿必须覆盖：

- 发现 Device、打开/释放（`IDisposable` 与否）
- 打开 Channel、启动/停止总线
- 发送、接收（具体接收模型可先选一个能跑的，不在本票锁死）
- CAN FD 帧与经典帧如何出现在类型上
- 多 Channel
- 时间戳落在哪
- 错误如何冒出来（可先用一种，细语义交给「08 grilling：错误模型与 TX echo / Bus-off 主机语义」）

约束：深模块——界面小、行为藏在后面；不要把 candle.dll 的 C 函数一一映成 public。对照「03 research：现有 candle/gs_usb 的 .NET 封装」，只借鉴不抄成依赖。类型名与「04 grilling：程序集、NuGet 与核心类型命名」不一致时，用 `CONTEXT.md` 已有的 Device / Channel，命名票未关也不要阻塞草稿。

本票是 HITL：草稿是给真人拍的，不替代拍板。

## 产出

抛掷型草稿（例如 `prototypes/public-api/` 下的 netstandard2.0 空壳项目），并在本票留下分支/路径指针。Answer 只记录「草稿提出了哪种界面」，不定稿。
