<#
.SYNOPSIS
    电阻模拟卡（地址 0x02）CAN 帧生成 / 反解。

.DESCRIPTION
    给个数值 + 命令，吐出能直接贴进 CanBench / GsCan View / .list 的帧；
    也能把收到的数据字节反解回数值。加 --json 可被其它脚本调用。

    帧 ID 位域（29 位扩展帧）:
        bit28..24 命令码 | bit23..16 辅助命令码 | bit15..8 预留 | bit7..0 设备地址

.EXAMPLE
    .\CanFrame.ps1 res 3000
        工作模式 3000Ω（高边）  →  0x01020002  00 45 3B 80 00

.EXAMPLE
    .\CanFrame.ps1 res 3k --low
        低边 3000Ω，数值支持 k 后缀

.EXAMPLE
    .\CanFrame.ps1 chan 0 1 4 255
        测试帧通道选择：高边 / 通道1 / 配置电阻4 / 电位器255

.EXAMPLE
    .\CanFrame.ps1 query 06
        查询输入 PWM 参数 → 0x02060002（DLC=0）

.EXAMPLE
    .\CanFrame.ps1 float 3000
        只看数值编码，附带 IEEE-754 拆位

.EXAMPLE
    .\CanFrame.ps1 decode 45 3B 80 00
        反解 → 3000

.EXAMPLE
    .\CanFrame.ps1 decode 00 45 3B 80 00
        5 字节时首字节当高低边，后 4 字节当 float

.EXAMPLE
    $f = .\CanFrame.ps1 res 3000 --json | ConvertFrom-Json
    $f.id; $f.data
        脚本调用：# JSON 走输出流，不含装饰文本
#>

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ArgList
)

$ErrorActionPreference = 'Stop'

# ============================================================ 数值编解码

function ConvertTo-BeFloat {
    param([double]$Value)
    $bytes = [BitConverter]::GetBytes([single]$Value)
    [Array]::Reverse($bytes)
    return , $bytes
}

function ConvertFrom-BeFloat {
    param([byte[]]$Bytes)
    if ($Bytes.Count -ne 4) { throw "float 需要正好 4 字节，收到 $($Bytes.Count) 字节" }
    $tmp = [byte[]]$Bytes.Clone()
    [Array]::Reverse($tmp)
    return [BitConverter]::ToSingle($tmp, 0)
}

# "3000" / "3k" / "3.3k" / "3000r" / "3000Ω"
function Parse-Number {
    param([string]$Text)
    $t = $Text.Trim().ToUpperInvariant() -replace '[ΩR]$', ''
    if ($t -match '^([0-9]*\.?[0-9]+)K$') { return [double]$Matches[1] * 1000 }
    if ($t -match '^-?[0-9]*\.?[0-9]+$') { return [double]$t }
    throw "看不懂的数值：$Text"
}

# "2" / "0x02"
function Parse-Int {
    param([string]$Text)
    $t = $Text.Trim()
    if ($t -match '^0[xX]([0-9A-Fa-f]+)$') { return [Convert]::ToInt64($Matches[1], 16) }
    if ($t -match '^[0-9]+$') { return [int64]$t }
    throw "看不懂的整数：$Text"
}

function Format-Hex {
    param([byte[]]$Bytes)
    return (($Bytes | ForEach-Object { $_.ToString('X2') }) -join ' ')
}

# ============================================================ 帧

function New-FrameId {
    param([int]$Cmd, [int]$Aux, [int]$Addr, [int]$Reserved = 0)

    if ($Cmd -lt 0 -or $Cmd -gt 0x1F) { throw ("命令码 {0} 超出 5 位（0x00~0x1F）" -f $Cmd) }
    if ($Aux -lt 0 -or $Aux -gt 0xFF) { throw ("辅助命令码 {0} 超出 8 位（0x00~0xFF）" -f $Aux) }
    if ($Addr -lt 0 -or $Addr -gt 0xFF) { throw "设备地址 $Addr 超出 8 位（0~255）" }

    return [uint32]((([uint32]$Cmd -shl 24)) -bor
                    (([uint32]$Aux -shl 16)) -bor
                    (([uint32]$Reserved -shl 8)) -bor
                    ([uint32]$Addr))
}

# 固件已知缺口，生成时就提醒，免得白等超时
function Get-FirmwareWarning {
    param([int]$Cmd, [int]$Aux)

    if ($Cmd -eq 0x02 -and $Aux -in 0x03, 0x04, 0x05, 0x06, 0x07, 0x0F) {
        return '固件该查询分支为空（protocol.c:491-514），发出去不会有任何应答，只会等到超时'
    }
    if ($Cmd -eq 0x01 -and $Aux -eq 0x05) {
        return '固件没有 0x01/05 分支，会落到默认错误分支回 01 02（无效参数）'
    }
    if ($Cmd -eq 0x10) {
        return '协议写广播用 0x10，但固件判的是 0x1F；0x10 会落到 default 回 01 01（无效命令码）'
    }
    if ($Cmd -eq 0x1F) {
        return '固件 CAN_CMD_BROADCAST 分支是空的，静默无应答'
    }
    if ($Cmd -in 0x03, 0x04) {
        return '固件参数设置 / 参数查询分支为空，静默无应答'
    }
    return $null
}

# 按协议量程划分，提示固件会选中哪个内部通道
function Get-ChannelHint {
    param([double]$Ohm)

    if ($Ohm -eq 0) { return '0Ω = 关闭所有通道 / 关输入继电器 / 回空闲模式' }
    if ($Ohm -ge 1 -and $Ohm -lt 10) { return '落在通道1 量程（1~9.99Ω）' }
    if ($Ohm -ge 10 -and $Ohm -lt 100) { return '落在通道2 量程（10~99.9Ω）' }
    if ($Ohm -ge 100 -and $Ohm -lt 1000) { return '落在通道3 量程（100~999Ω）' }
    if ($Ohm -ge 1000 -and $Ohm -le 3000) { return '落在通道4 量程（1000~3000Ω）' }
    return $null
}

function New-FrameResult {
    param(
        [uint32]$Id, [byte[]]$Data, [string]$Note,
        [int]$Cmd, [int]$Aux, [string]$Warning
    )

    if ($null -eq $Data) { $Data = [byte[]]@() }
    if ($Data.Count -gt 8) { throw "经典 CAN 数据最多 8 字节，当前 $($Data.Count) 字节" }

    $idHex = '0x' + $Id.ToString('X8')

    # .list 的 obj：ID(uint32 LE, bit31=扩展) + DLC(uint32 LE) + 64B 数据区 + 4B 0
    $obj = New-Object System.Collections.Generic.List[byte]
    $idBytes = [BitConverter]::GetBytes([uint32]($Id -bor 0x80000000))
    if (-not [BitConverter]::IsLittleEndian) { [Array]::Reverse($idBytes) }
    $obj.AddRange([byte[]]$idBytes)
    $dlcBytes = [BitConverter]::GetBytes([uint32]$Data.Count)
    if (-not [BitConverter]::IsLittleEndian) { [Array]::Reverse($dlcBytes) }
    $obj.AddRange([byte[]]$dlcBytes)
    $padded = New-Object byte[] 64
    [Array]::Copy($Data, $padded, $Data.Count)
    $obj.AddRange($padded)
    $obj.AddRange([byte[]]@(0, 0, 0, 0))

    return [pscustomobject]@{
        id      = $idHex
        idDec   = $Id
        cmd     = $Cmd
        aux     = $Aux
        addr    = ($Id -band 0xFF)
        dlc     = $Data.Count
        data    = (Format-Hex $Data)
        note    = $Note
        warning = $Warning
        csv     = ("0,{0},true,false,false,false,{1},{2},0,false" -f $idHex, $Data.Count, (Format-Hex $Data))
        listObj = ((Format-Hex $obj.ToArray()) -replace ' ', '')
    }
}

function Show-FrameResult {
    param([pscustomobject]$Frame, [switch]$WithList)

    Write-Host ''
    Write-Host ("  ID      {0}    cmd=0x{1:X2} aux=0x{2:X2} addr=0x{3:X2}   扩展帧" -f `
            $Frame.id, $Frame.cmd, $Frame.aux, $Frame.addr)
    Write-Host ("  DLC     {0}" -f $Frame.dlc)
    Write-Host ("  数据    {0}" -f $(if ($Frame.data) { $Frame.data } else { '（空）' }))
    if ($Frame.note) { Write-Host ("  说明    {0}" -f $Frame.note) }
    if ($Frame.warning) { Write-Host ("  提醒    {0}" -f $Frame.warning) -ForegroundColor Yellow }

    Write-Host ''
    Write-Host '  --- CSV 行（Channel,Id,Extended,Remote,Fd,BitRateSwitch,Length,DataHex,PeriodMs,Enabled）'
    Write-Host ("  {0}" -f $Frame.csv) -ForegroundColor DarkGray

    if ($WithList) {
        Write-Host ''
        Write-Host '  --- .list 的 obj 属性'
        Write-Host ("  {0}" -f $Frame.listObj) -ForegroundColor DarkGray
    }
    Write-Host ''
}

# ============================================================ 参数

if ($null -eq $ArgList -or $ArgList.Count -eq 0) {
    Write-Host @'
电阻模拟卡 CAN 帧工具

  用法:  .\CanFrame.ps1 <命令> [参数...] [选项...]

  命令:
    res <欧姆> [--low]              配置模拟电阻（工作模式）      0x01/0x02
    chan <高边0|1> <通道0-4> <配置电阻0-5> <电位器0-255>
                                    通道选择（测试模式）          0x01/0x01
    side <0高边|1低边>              高低边切换（测试模式）        0x01/0x03
    relay <0断开|1吸合>             输入继电器（测试模式）        0x01/0x04
    close                           关闭负载电阻，回空闲          0x01/0x00
    query <辅助码>                  查询命令，如 query 0 / query 06   0x02/xx
    float <数值>                    只把数值编成 float 大端 4 字节
    decode <字节...>                反解数据字节 → 数值

  选项:
    --low        低边（res 用）
    --addr <n>   设备地址，默认 2（固件硬编码）
    --list       额外打印 .list 的 obj 串
    --json       只输出 JSON，便于脚本调用

  数值支持: 3000 / 3k / 3.3k / 3000Ω

  例:
    .\CanFrame.ps1 res 3000
    .\CanFrame.ps1 res 3k --low
    .\CanFrame.ps1 query 06
    .\CanFrame.ps1 float 3000
    .\CanFrame.ps1 decode 45 3B 80 00
'@
    exit 0
}

$Command = $ArgList[0].ToLowerInvariant()

$Positional = New-Object System.Collections.ArrayList
$Options = @{}

$i = 1
while ($i -lt $ArgList.Count) {
    $arg = $ArgList[$i]

    if ($arg -like '--*' -or $arg -like '-*') {
        $name = $arg.TrimStart('-').ToLowerInvariant()

        if ($name -in 'low', 'high', 'list', 'json') {
            $Options[$name] = $true
        }
        elseif ($name -eq 'addr') {
            if ($i + 1 -ge $ArgList.Count) { throw '选项 --addr 后面要跟一个数字' }
            $Options['addr'] = [int](Parse-Int $ArgList[$i + 1])
            $i++
        }
        else {
            throw "未知选项：$arg"
        }
    }
    else {
        [void]$Positional.Add($arg)
    }

    $i++
}

$DeviceAddr = if ($Options.ContainsKey('addr')) { $Options['addr'] } else { 2 }
$JsonOut = $Options.ContainsKey('json')
$WithList = $Options.ContainsKey('list')

# ============================================================ 分派

try {

switch ($Command) {

    'res' {
        if ($Positional.Count -lt 1) { throw '用法: res <欧姆> [--low]' }

        $ohm = Parse-Number $Positional[0]
        if ($ohm -lt 0 -or $ohm -gt 3000) {
            Write-Host ("  {0}Ω 超出协议范围 0~3000Ω，固件侧会算不出档位" -f $ohm) -ForegroundColor Yellow
        }

        $isLow = $Options.ContainsKey('low')
        $mode = if ($isLow) { 1 } else { 0 }

        $data = New-Object System.Collections.Generic.List[byte]
        $data.Add([byte]$mode)
        $data.AddRange([byte[]](ConvertTo-BeFloat $ohm))

        $side = if ($isLow) { '低边' } else { '高边' }
        $hint = Get-ChannelHint $ohm
        $note = if ($hint) { "工作模式 {0} {1}Ω，{2}" -f $side, $ohm, $hint } else { "工作模式 {0} {1}Ω" -f $side, $ohm }

        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x01 -Aux 0x02 -Addr $DeviceAddr) `
            -Data $data.ToArray() -Note $note -Cmd 0x01 -Aux 0x02 `
            -Warning (Get-FirmwareWarning 0x01 0x02)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'chan' {
        if ($Positional.Count -lt 4) { throw '用法: chan <高边0|1> <通道0-4> <配置电阻0-5> <电位器0-255>' }

        $mode    = [int](Parse-Int $Positional[0])
        $channel = [int](Parse-Int $Positional[1])
        $cfgRes  = [int](Parse-Int $Positional[2])
        $wiper   = [int](Parse-Int $Positional[3])

        if ($mode -notin 0, 1) { throw '第 1 个参数只能是 0（高边）或 1（低边）' }
        if ($channel -lt 0 -or $channel -gt 4) { throw '通道只能是 0~4（0 = 全关）' }
        if ($cfgRes -lt 0 -or $cfgRes -gt 5) { throw '配置电阻只能是 0~5' }
        if ($wiper -lt 0 -or $wiper -gt 255) { throw '电位器档位只能是 0~255' }

        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x01 -Aux 0x01 -Addr $DeviceAddr) `
            -Data ([byte[]]@($mode, $channel, $cfgRes, $wiper)) `
            -Note ("通道选择 高/低边={0} 通道={1} 配置电阻={2} 电位器={3}" -f $mode, $channel, $cfgRes, $wiper) `
            -Cmd 0x01 -Aux 0x01 -Warning (Get-FirmwareWarning 0x01 0x01)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'side' {
        if ($Positional.Count -lt 1) { throw '用法: side <0高边|1低边>' }
        $mode = [int](Parse-Int $Positional[0])
        if ($mode -notin 0, 1) { throw '只能是 0（高边）或 1（低边）' }

        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x01 -Aux 0x03 -Addr $DeviceAddr) `
            -Data ([byte[]]@($mode)) `
            -Note $(if ($mode -eq 1) { '切换低边输入（仅空闲模式可发）' } else { '切换高边输入（仅空闲模式可发）' }) `
            -Cmd 0x01 -Aux 0x03 -Warning (Get-FirmwareWarning 0x01 0x03)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'relay' {
        if ($Positional.Count -lt 1) { throw '用法: relay <0断开|1吸合>' }
        $state = [int](Parse-Int $Positional[0])
        if ($state -notin 0, 1) { throw '只能是 0（断开）或 1（吸合）' }

        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x01 -Aux 0x04 -Addr $DeviceAddr) `
            -Data ([byte[]]@($state)) `
            -Note $(if ($state -eq 1) { '输入继电器吸合（仅空闲模式可发）' } else { '输入继电器断开（仅空闲模式可发）' }) `
            -Cmd 0x01 -Aux 0x04 -Warning (Get-FirmwareWarning 0x01 0x04)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'close' {
        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x01 -Aux 0x00 -Addr $DeviceAddr) `
            -Data ([byte[]]@()) -Note '关闭负载电阻，回空闲模式' `
            -Cmd 0x01 -Aux 0x00 -Warning (Get-FirmwareWarning 0x01 0x00)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'query' {
        if ($Positional.Count -lt 1) { throw '用法: query <辅助命令码>，如 query 0 / query 03 / query 0x0F' }

        $aux = [int](Parse-Int $Positional[0])
        if ($aux -lt 0 -or $aux -gt 0xFF) { throw '辅助命令码只能是 0x00~0xFF' }

        $names = @{
            0x00 = '查询版本'
            0x01 = '查询设备状态'
            0x02 = '查询初始化状态'
            0x03 = '查询输入电压/电流'
            0x04 = '查询当前工作电阻'
            0x05 = '查询设备温度'
            0x06 = '查询输入 PWM 参数'
            0x07 = '查询当前工作通道'
            0x0F = '查询全部工作信息'
        }
        $note = if ($names.ContainsKey($aux)) { $names[$aux] } else { '未知查询命令' }

        $frame = New-FrameResult -Id (New-FrameId -Cmd 0x02 -Aux $aux -Addr $DeviceAddr) `
            -Data ([byte[]]@()) -Note $note -Cmd 0x02 -Aux $aux `
            -Warning (Get-FirmwareWarning 0x02 $aux)

        if ($JsonOut) { $frame | ConvertTo-Json -Depth 5 -Compress }
        else { Show-FrameResult $frame -WithList:$WithList }
    }

    'float' {
        if ($Positional.Count -lt 1) { throw '用法: float <数值>' }

        $value = Parse-Number $Positional[0]
        $be = [byte[]](ConvertTo-BeFloat $value)
        $le = [byte[]]$be.Clone()
        [Array]::Reverse($le)

        $u = [BitConverter]::ToUInt32($le, 0)
        $sign = ($u -shr 31) -band 1
        $exp = ($u -shr 23) -band 0xFF
        $man = $u -band 0x7FFFFF

        if ($JsonOut) {
            [pscustomobject]@{
                value        = $value
                bigEndian    = (Format-Hex $be)
                littleEndian = (Format-Hex $le)
                uint32       = ('0x' + $u.ToString('X8'))
                sign         = $sign
                exponent     = $exp
                mantissa     = ('0x' + $man.ToString('X6'))
                readback     = (ConvertFrom-BeFloat $be)
            } | ConvertTo-Json -Depth 5 -Compress
            break
        }

        Write-Host ''
        Write-Host ("  数值        {0}" -f $value)
        Write-Host ("  大端(协议)  {0}    ← 发送时 4 个字节按这个顺序" -f (Format-Hex $be)) -ForegroundColor Green
        Write-Host ("  小端        {0}    ← x86 内存里的样子，别直接 memcpy" -f (Format-Hex $le))
        Write-Host ("  uint32      0x{0}" -f $u.ToString('X8'))
        Write-Host ("  回读验证    {0}" -f (ConvertFrom-BeFloat $be))

        Write-Host ''
        Write-Host ("  IEEE-754 拆位: S = {0}    E = {1} (0x{2:X2})    M = 0x{3:X6}" -f $sign, $exp, $exp, $man)
        if ($exp -eq 0) {
            Write-Host "    次正规数 / 0:  (-1)^S × 2^-126 × (M/2^23)"
        }
        else {
            Write-Host ("    公式: (-1)^S × 2^(E-127) × (1 + M/2^23)")
            Write-Host ("        = 2^{0} × (1 + {1}/8388608)" -f ($exp - 127), $man)
            Write-Host ("        = {0} × {1}" -f [math]::Pow(2, $exp - 127), (1 + $man / [math]::Pow(2, 23)))
        }
        Write-Host ''
    }

    'decode' {
        if ($Positional.Count -lt 1) { throw '用法: decode <字节...>，如 decode 45 3B 80 00' }

        # 允许 "453B8000" 连写
        $tokens = @()
        foreach ($p in $Positional) {
            $clean = $p -replace '[^0-9A-Fa-f]', ''
            if ($clean.Length -eq 0) { throw "字节为空：$p" }
            if ($clean.Length -gt 2 -and ($clean.Length % 2) -eq 0) {
                for ($j = 0; $j -lt $clean.Length; $j += 2) { $tokens += $clean.Substring($j, 2) }
            }
            else { $tokens += $clean }
        }

        $bytes = [byte[]]@($tokens | ForEach-Object { [Convert]::ToByte($_, 16) })

        if ($bytes.Count -eq 4) {
            $value = ConvertFrom-BeFloat $bytes
            if ($JsonOut) {
                [pscustomobject]@{
                    bytes = (Format-Hex $bytes); size = 4
                    side  = $null; value = $value
                } | ConvertTo-Json -Depth 5 -Compress
                break
            }
            Write-Host ''
            Write-Host ("  输入        {0}   （4 字节）" -f (Format-Hex $bytes))
            Write-Host ("  float 大端  {0}" -f $value) -ForegroundColor Green
            Write-Host ''
        }
        elseif ($bytes.Count -eq 5) {
            $side = if ($bytes[0] -eq 1) { '低边' } else { '高边' }
            $tail = [byte[]]$bytes[1..4]
            $value = ConvertFrom-BeFloat $tail
            if ($JsonOut) {
                [pscustomobject]@{
                    bytes = (Format-Hex $bytes); size = 5
                    side  = $side; value = $value
                } | ConvertTo-Json -Depth 5 -Compress
                break
            }
            Write-Host ''
            Write-Host ("  输入        {0}   （5 字节）" -f (Format-Hex $bytes))
            Write-Host ("  高/低边     {0}（{1}）" -f $bytes[0], $side)
            Write-Host ("  float 大端  {0}Ω" -f $value) -ForegroundColor Green
            Write-Host ''
        }
        else {
            throw "只支持 4 字节（float）或 5 字节（高低边 + float），收到 $($bytes.Count) 字节"
        }
    }

    default {
        throw "未知命令：$Command（不带参数跑一次看用法）"
    }
}

}
catch {
    # 命令行友好：只打一行，不输出 PowerShell 堆栈
    Write-Host ("错误: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
