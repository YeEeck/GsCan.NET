# 暂停只冻画面，不停 TryRead

Trace 暂停时后台仍对每路 Channel 做 TryRead。停泵会导致设备 Overflow，用户只是想看清一行。暂停期间的帧不进 Trace/Latest，状态栏计丢弃。Trace 有上限（约 10 万行）并丢掉最旧行。时间戳对人显示为本次 Start 后的相对毫秒，Log 仍写 `TimestampMicroseconds` 原值。
