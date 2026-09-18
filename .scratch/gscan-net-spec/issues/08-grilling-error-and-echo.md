# 08 grilling：错误模型与 TX echo / Bus-off 主机语义

Type: grilling
Status: open
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
