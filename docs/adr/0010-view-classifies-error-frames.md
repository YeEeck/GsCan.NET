# View 对 Error 做 ErrorClass，库不解释

GsCan View 是查看器不是分析仪，v1 曾把 Error 的 payload 钉成只显示十六进制。实验室 bring-up 仍需要一眼看出 ACK / Bus-off。决定只在 View 把 SocketCAN 类位归成一个 **ErrorClass** 并给一句提示；GsCan 库继续只转发 `Kind=Error`。ID 列显示 ErrorClass（不再假装是 CAN ID），tooltip 是中文提示，Data 与 Log 仍是原值。Latest 的 Error 键改为 `(Channel, Kind, ErrorClass)`（ADR-0005 的例外）；Display Filter 的 Error 只认 Channel + Kind=Error。不解码 location / TRX，Bus-off 提示不写成必须 Stop 再 Start。
