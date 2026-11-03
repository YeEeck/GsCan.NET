# CanBench — CAN 命令台

给 gs_usb 适配器做联调的小工具：一份可编辑的命令表，加上按到达顺序的 Trace 和按 ID 聚合的 Latest。

CanBench 建在 `GsCan` 库上：设备枚举、通道启停和收发都走 `Device` / `Channel` / `CanFrame`；
工具本身只管表格、备注、过滤和显示，因此仓库里不需要第二套 candle_api P/Invoke。

## 它解决什么

GsCan View 与 CanBench 的能力有重叠：两者都能导入导出 `.list`/CSV、给命令写备注，
也都有 Trace、Latest 和 ID 过滤。差别主要在形态：

| | GsCan View | CanBench |
|---|---|---|
| 界面 | Avalonia 查看器 | WinForms 单窗口命令台 |
| 命令表行数 | 最多 256 行 | 不限 |
| 无界面模式 | 无 | `--dump`：收一段时间，直接打印 Latest 聚合 |
| 直接发帧（gs_usb） | ✅ | ✅ |
| Trace / Latest | ✅ | ✅ |
| ID 过滤（范围 / 排除） | ✅ | ✅ |
| 导入导出 `.list` / `.csv` | ✅ | ✅ |
| 每条命令写备注 | ✅ | ✅ |

CanBench 更适合「拿一整条长测试序列，整表发、逐条看」；GsCan View 是更完整的查看器。

## 心跳帧不会堵满

ECU 这类设备会以极高频率狂发同一个 ID（实测 `0x01100001` 约 **348 µs 一帧**，
折合每秒近 3000 帧），按到达顺序看的 Trace 会被瞬间冲掉，什么也看不清。

**Latest 页**按 `通道 + 类型 + ID + 标志` 聚合，同一个 ID 只占一行，显示：

- 最新一帧的数据
- 累计**计数**
- 实测**周期**（自动 ps/µs/ms/s 选单位）

实测 3000ms 收到 5682 帧心跳，Latest 只有 1 行：

```
  CH  Kind   ID          标志 长度 数据                     计数    周期
  1   Rx     0x01100001  EXT     8 FF FF 03 BE 00 00 00 00    5682  348µs
```

聚合本身很快：单独测过 20 万帧耗时 195ms（约 1µs/帧）。
界面刷新按 250ms 节流，不会因为帧多而卡死。

### ID 过滤

Trace 页上方的「ID 过滤」框，语法：

| 写法 | 含义 |
|---|---|
| （空） | 全部显示 |
| `0x02000002` | 只看这个 ID |
| `0x100-0x1FF` 或 `0x100..0x1FF` | 只看这个范围 |
| `!0x01100001` | **排除**这个 ID（心跳帧最常用） |
| `!0x01100000-0x011000FF` | 排除一段 |
| `0x02000002 0x01000002 !0x01000002` | 混用：先取中再排除 |

多个条件用空格、逗号或分号分隔。ID 一律按**十六进制**解析。

实测把心跳帧排除后，7172 帧里只剩 CH0 的 2 行：

```
共收到 7172 帧，过滤后参与聚合 2 帧
  0   Echo   0x02000002  EXT     0                               1  —
  0   Rx     0x02000002  EXT     4 01 00 02 01                   1  —
```

过滤对 Trace 和 Latest **同时**生效，右上角会显示「已过滤 N」。

## 构建

```powershell
dotnet build tools/CanBench/CanBench.csproj -c Release
.\tools\CanBench\bin\Release\net10.0-windows\win-x64\CanBench.exe
```

`candle_api.dll` 由仓库里的 `src/GsCan/runtimes/win-x64/native/` 提供，构建时自动复制到
exe 旁；换机器不用单独带 DLL。

直接带一个 `.list` 启动也能打开（在仓库根目录执行）：

```powershell
.\tools\CanBench\bin\Release\net10.0-windows\win-x64\CanBench.exe .\tools\samples\commands.list
```

## 用起来

1. **刷新手选设备 → 打开**。设备被 GsCan View 占用时会明确提示 `SET_DEVICE_MODE`，关掉 View 即可。
2. **CH0 启动**（正常模式，能发能收）；**CH1 默认勾了「只听」**，只观察 ECU、绝不往总线上发东西。
3. 表格里逐行点「发送」；或勾「启用」并填「周期ms」让它周期发；「全部停止」一键停。
4. 下方 Trace 会同时显示 `Rx`（对方发的）、`Echo`（自己发的）、`Error`（并写明是「无节点应答」还是「BUS-OFF」等）。

### 表格列

| 列 | 说明 |
|---|---|
| 备注 / 作用 | 自由文本，会写进 `.list` 的 `szname` / CSV 的「备注」列 |
| CH | 0 或 1 |
| ID | 支持 `0x02000002` 或十进制；超过 29 位会被拒绝 |
| 扩展 / RTR | 复选框 |
| DLC | 只读，跟着数据自动变 |
| 数据 | 十六进制，`00 44 7A 00 00`、`00447A0000`、`0x00...` 都认 |
| 周期ms | 0 = 只发一次；>0 且勾「启用」才周期发 |
| 启用 | 周期发送开关 |

编辑错的格子会撤销并提示原因，不会写坏数据。

## 文件格式

`.list` 与 [`samples/commands.list`](../samples/commands.list) **完全同格式**，逐字节兼容：

```
[0..3]   ID，uint32 小端，bit31=1 表示扩展帧
[4..7]   DLC，uint32 小端
[8..71]  数据区，固定 64 字节
[72..75] 保留，恒 0
```

CSV 表头：`备注,CH,ID,扩展,RTR,DLC,数据,周期ms,启用`
