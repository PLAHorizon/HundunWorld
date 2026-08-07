# 角色系统深度优化 — 验证检查清单

> 修复完成后，按此清单逐步验证。每步通过后打 ✅，失败则查看对应日志。

## 前置条件

- [ ] 停止运行中的 Silo 进程
- [ ] 停止运行中的 Gateway 进程
- [ ] 执行 `dotnet build Horizon.sln` 确认 0 错误
- [ ] 启动 Silo
- [ ] 启动 Gateway
- [ ] 确认 Silo 和 Gateway 日志无启动错误

## 第一阶段：单客户端验证

- [ ] 启动 1 个游戏客户端
- [ ] 登录并进入游戏
- [ ] 搜索日志 `[NetDiag] 数据接收` — 确认每 10 秒出现（心跳正常）
- [ ] 搜索日志 `[Broadcast] Delta推送成功` — 确认快照广播正常
- [ ] 搜索日志 `[Dispatch] 分发完成` — 确认 `TotalMs` < 5ms
- [ ] 确认无 `空闲超时` 日志
- [ ] 确认无 `DespawnImmediatelyAsync` 日志
- [ ] 确认无 `BroadcastSnapshotAsync 发生未捕获异常` 日志
- [ ] 确认无 `CodecNotFoundException` 日志
- [ ] 确认无 `MemoryPackSerializationException` 日志
- [ ] 确认角色移动流畅，无卡顿

## 第二阶段：多客户端验证（3-5 个）

- [ ] 启动 3 个游戏客户端，全部登录进入游戏
- [ ] 确认所有客户端能看到彼此的角色
- [ ] 移动其中一个角色，其他客户端能看到平滑移动
- [ ] 搜索日志 `[NetDiag] 连接状态汇总` — 确认所有连接 `idle` < 15 秒
- [ ] 搜索日志 `[FanoutBackpressure]` — 确认无告警（队列使用率 < 80%）
- [ ] 增加到 5 个客户端
- [ ] 确认所有 5 个客户端同步正常
- [ ] 确认无客户端被误判离线

## 第三阶段：长时间运行验证（30 分钟+）

- [ ] 5 个客户端保持连接 30 分钟
- [ ] 其中 2 个客户端持续移动，3 个静止
- [ ] 每 5 分钟检查一次日志：
  - [ ] 无 `空闲超时` 日志
  - [ ] 无 `DespawnImmediatelyAsync` 日志
  - [ ] 无 `Packet dropped: session offline` 日志
  - [ ] `[Dispatch] 分发完成` 的 `TotalMs` 持续 < 5ms
- [ ] 30 分钟后所有 5 个客户端仍在线
- [ ] 远程角色无异常消失
- [ ] 本地角色移动无卡顿

## 第四阶段：断线重连验证

- [ ] 5 个客户端在线运行中
- [ ] 手动断开 1 个客户端的网络（关闭客户端或拔网线）
- [ ] 等待 10 秒后重新连接
- [ ] 搜索日志 `CancelDespawn` — 确认重连时取消了 Despawn
- [ ] 确认重连后角色仍在线（其他客户端未看到角色消失）
- [ ] 确认重连后角色位置同步正常
- [ ] 搜索日志 `EnterWorldAsync 实体.*已存在（重连场景）` — 确认跳过了 Despawn+Spawn

## 第五阶段：延迟测量

- [ ] 5 个客户端同时移动
- [ ] 观察远程角色移动的平滑度
- [ ] 确认远程角色移动延迟 < 200ms（肉眼观察无明显滞后）
- [ ] 搜索日志 `[Dispatch] 包编码完成` — 确认 `EncodeMs` < 2ms
- [ ] 搜索日志 `[Dispatch] 分发完成` — 确认 `TotalMs` < 5ms

## 日志关键字速查

| 关键字 | 含义 | 预期 |
|--------|------|------|
| `[NetDiag] 数据接收` | 客户端数据到达 | 每 10 秒出现 |
| `[NetDiag] 连接状态汇总` | 连接空闲状态 | 每 30 秒出现，idle < 15s |
| `[Broadcast] Delta推送成功` | 快照广播成功 | 每 60 tick 或前 5 次 |
| `[Broadcast] CorrectionPacket序列化成功` | Correction 序列化 | 修复前崩溃，修复后成功 |
| `[Dispatch] 包编码完成` | 数据包编码 | EncodeMs < 2ms |
| `[Dispatch] 分发完成` | 数据包分发 | TotalMs < 5ms |
| `[FanoutBackpressure]` | 队列反压告警 | 不应出现 |
| `空闲超时` | 连接被误判离线 | **不应出现** |
| `DespawnImmediatelyAsync` | 强制 Despawn | **不应出现**（正常只有 ScheduleDespawn） |
| `CodecNotFoundException` | Orleans 序列化失败 | **不应出现** |
| `MemoryPackSerializationException` | MemoryPack 序列化失败 | **不应出现** |
| `Packet dropped: session offline` | 包被丢弃 | **不应出现** |

## 修复内容摘要

| 修复项 | 影响 |
|--------|------|
| MemoryPack.Generator 引用 | 修复 CorrectionPacket 序列化异常 |
| GetAllSubscribers ToArray() | 修复 Orleans 深拷贝失败 |
| 心跳 15s + 超时 45s | 容差从 10s → 30s |
| 心跳失败 5s 重试 | 避免单次失败触发超时 |
| 宽限期保护 | CharacterPresenceMonitor 不绕过 ScheduleDespawn |
| ArchWorldHost.Tick 零分配 | 消除每帧 5 次数组分配 |
| OnPlayerChunkChanged 零分配 | 消除 3-4MB HashSet 分配 |
| UpdateSessionPositionAsync 零分配 | 消除 3-4MB HashSet 分配（服务端） |
| 位置驱动订阅 | 24 字节 vs 416KB RPC |
| 20Hz 降频广播 | RPC 次数降 3 倍 |
| 全链路诊断日志 | 延迟瓶颈定位 |
