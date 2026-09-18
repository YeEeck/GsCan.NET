# 08 grilling：错误模型与 TX echo / Bus-off 主机语义

Type: grilling
Status: resolved
Blocked by: 01, 02, 06

## Question

主机库如何把设备侧已经钉死的语义交给调用方，而不把「上了总线」说成 TX echo？

必须拍板：

- **TX echo**：调用方怎么对上自己的 Host TX？echo 失败/丢失怎么表现？Channel stop 吞掉未完成 Host TX、不产生 echo——库是静默、异常，还是可观察取消？
- **Bus-off**：库是否区分 FlintCAN-FD 的 Hardware bus-off 与主机看见的 Bus-off？还是只转发错误帧？
- **OVERFLOW / BERR**：事件、特殊帧，还是错误类型？
- **失败模型**：异常 vs 返回值 vs 两者分层（配置用异常、热路径用返回值）

设备侧定义只引用 FlintCAN-FD `CONTEXT.md`，不要改写。若本票产生主机侧新术语（例如把 TX echo 命名成库里的某个类型），当场写入本仓库 `CONTEXT.md`。

## 产出

锁定的错误/echo 语义，作为规格条款；新术语写入 `CONTEXT.md`。

## Answer

2026-09-18 grilling，全部按推荐。规格条款：

1. **失败分层**：打开 / `Start` / 立刻失败的 `Send` → `GsCanException`。`TryRead` 超时返回 `false`。总线状况不是异常。
2. **Echo**：`Send` 立刻返回；Echo 只从 `TryRead` 出现。DLL 强制 `echo_id = 0`，同一 Channel 按 FIFO 对 `Send`。不能按 id 认人。Echo ≠ 上了总线。
3. **Stop**：void、不抛。未对上的 `Send` 不再出 Echo。不合成取消帧。若在乎完成，先把 Echo 收完再 Stop。
4. **Bus-off**：不区分 FlintCAN-FD 的 Hardware bus-off 与主机看见的 Bus-off。只转发 `Kind = Error` 的帧（含 `CAN_ERR_BUSOFF`）。无 `Channel.State`，无总线恢复 API。
5. **OVERFLOW / BERR**：`Overflow` 是帧标志；BERR 是 `Kind = Error` 的帧。无事件，无 BERR 开关。

设备侧定义仍只在 FlintCAN-FD `CONTEXT.md`。主机侧新术语 **Echo**、**Overflow** 已写入本仓库 [`CONTEXT.md`](../../../CONTEXT.md)。
