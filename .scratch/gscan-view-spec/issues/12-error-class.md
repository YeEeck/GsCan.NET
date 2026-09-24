# 12: ErrorClass

Parent: [GsCan View v1 规格](../spec.md)

**What to build:** Kind=Error 的行在 ID 列显示 ErrorClass，tooltip 给一句中文 bring-up 提示；LastError 在 Error 到达时更新；Latest 的 Error 键是 `(Channel, Kind, ErrorClass)`；Display Filter 的 Error 只认 Channel + Kind=Error；硬故障才停该路周期发送。库不解码。Log 仍写数值 Id 与 hex Data。

**Blocked by:** 04, 05, 06, 07, 09

**Status:** resolved

- [x] Error 的 ID 列是 ErrorClass，Data 仍是 hex，tooltip 是中文提示（CNT 时含 TEC/REC）
- [x] Latest：Stuff 与 Form 分键；同一 ACK 不因附加类位裂开
- [x] Display Filter：Error 忽略 ID / 标准/扩展 / 经典/FD / Remote
- [x] 硬故障停周期；Warning / Active / Restarted 不停
- [x] LastError 在暂停或过滤后仍更新；文本没变不刷；停周期拼进同一句
- [x] Log CSV 仍是数值 Id
- [x] 会话面注入帧即可验证，不要求实机

## Comments

- Implemented on `feat/gscan-view-v1`. View session classifies SocketCAN Error frames into ErrorClass; library unchanged. Tests: `tests/GsCan.View.Tests/ErrorClassTests.cs`. ADR-0010.
