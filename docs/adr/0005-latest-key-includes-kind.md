# Latest 的键包含 Kind

Latest 与 Trace 看同一批 CanFrame，不是「只看总线上的 Rx」。键为 `(Channel, Kind, Id, Extended, Remote, IsFd)`。若不含 Kind，Loopback 下 Echo 与 Rx 会互相覆盖，Echo≠上总线这条语义在第二种看法里就丢了。Error 也占自己的一行，避免 Bus-off 被数据帧盖掉。
