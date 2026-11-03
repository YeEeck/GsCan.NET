# samples

两份 `.list` 命令表样例（UTF-8）：

- `commands.list` —— 42 条命令（原先的 `测试命令帧_261020.list`）。
- `bench.list` —— 32 条手工整理的联调序列（原先 CanBench 的 `t.list`），适合在 CanBench 里逐条发。

格式：每条 `<tagSendUint>` 的 `obj` 是 76 字节 —— ID（uint32 小端，bit31=1 为扩展帧）、
DLC（uint32 小端）、64 字节数据区、4 字节保留。同一份内容也可以用 CanBench 导出成 CSV
（列：备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用）。

`CanBenchTest` 会读取本目录所有 `*.list`，并与 `commands.list` 做逐字节往返比对。
