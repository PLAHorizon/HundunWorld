# HundunWorld 网络同步性能基线

> 本文档定义 MMORPG 网络同步系统的压测方法学、带宽测量结果、性能优化策略与容量规划建议。
> 目标：每玩家平均带宽 < 100kbps（MMORPG 工业标准阈值），端到端延迟（input→snapshot round trip）p99 < 100ms。
> 阶段 F 文档：与 `NETCODE.md`（架构基线）/ `NETWORK_PROTOCOL.md`（协议规范）互补，专注性能与容量。

## 1. 压测方法学

### 1.1 测试环境

| 组件 | 版本 | 用途 |
| --- | --- | --- |
| .NET | 10.0 | 运行时（服务端 + 客户端托管层） |
| Orleans | 10.0.1 | 服务端 Actor 模型（Grain） |
| TouchSocket | 4.1.1 | TCP 网关网络层 |
| MemoryPack | — | 二进制序列化 |
| K4os.Compression.LZ4 | — | 快照压缩 |
| Arch | 2.0.0-beta | 客户端 ECS 世界存储 |

部署形态：
- **单 shard 压测**：1 个 `ZoneShardGrain` + 1 个 `GameNetworkServer`（TCP 7789）+ N 个模拟客户端（同机或局域网）。
- **集群压测**：M 个 shard，每 shard 独立 AOI + 状态，按 shardKey 分片。

### 1.2 压测工具

| 工具 | 位置 | 用途 |
| --- | --- | --- |
| `NetworkLoadHarness` | `Horizon.Game.Core\LoadTest\NetworkLoadHarness.cs`（Task E.1，待落地） | 100+ 并发玩家端到端压测：TCP 连接 + 真实 `SyncPacketCodec` 编解码 + InputPacket 上行 + SnapshotPacket 接收 |
| `WeakNetworkSimulator` | `Horizon.Game.Core\LoadTest\WeakNetworkSimulator.cs`（Task E.2，待落地） | 弱网仿真：注入延迟（50/200/500ms）、丢包（1%/5%/10%）、抖动、连接中断 |
| `SyncLoadHarness` | `Horizon.Game.Core\LoadTest\SyncLoadHarness.cs`（已存在） | 单 shard 纯逻辑压测（1024 session），无真实 TCP，验证 Grain 层吞吐 |

> **注**：`NetworkLoadHarness` 与 `WeakNetworkSimulator` 由 Task E.1 / E.2 落地，本文档带宽数据为理论估算值，实际值待 Task E.5 实测后填充。

### 1.3 指标定义

| 指标 | 定义 | 采集方式 |
| --- | --- | --- |
| 带宽（kbps/玩家） | 单 session 1 秒滚动窗口平均下发字节数 × 8 / 1024 | `GatewaySyncDispatcher.SessionBandwidthTracker.CurrentBandwidthKbps` |
| 端到端延迟（input→snapshot round trip） | 客户端 InputPacket 发送 → 收到对应 ServerTick 的 SnapshotPacket 的时间差 | 客户端 `NetworkRuntime` 采样 |
| 吞吐（packets/s） | 单 session 每秒收发的 SyncPacket 数 | `GatewaySyncDispatcher.DeliveredPacketCount` 增量 |
| 丢包恢复时间（ms） | 客户端检测到缺包 → 收到重传/全量快照恢复的时间 | 客户端 `SnapshotApplySystem` 缺包请求时间戳 |
| RTT（ms） | 客户端 → 服务端 → 客户端的网络往返延迟 | `JitterBuffer.RecordRtt` 采样 |
| 抖动（ms） | RTT 样本标准差 | `JitterBuffer.Jitter` |
| EMA RTT（ms） | RTT 的指数移动平均（α=0.2） | `JitterBuffer.EmaRttMs` |

p50 / p99 / p999 分位数通过 `NetworkLoadHarness` 采集全量样本后离线计算。

## 2. 带宽测量结果

> **状态：理论估算（待 Task E.5 `NetworkLoadHarness` 实测填充）**
> 以下数据基于协议字段大小与频率策略的理论估算，实际值受 EntityDelta 数量、AOI 密度、增量压缩命中率影响。

### 2.1 单包体积估算

| 包类型 | 体积估算 | 说明 |
| --- | --- | --- |
| `SnapshotPacket`（全量，10 EntityDelta） | ~240 字节 | 每个 EntityDelta ~24 字节（Identity + Transform + State 保守估计） + 帧头开销 |
| `SnapshotPacket`（增量，仅变化字段） | ~80 字节 | 仅含变化 EntityDelta，平均 3-4 个 × 24 字节 |
| `InputPacket` | ~40 字节 | ClientTick + InputBits + LookYaw/Pitch + MoveX/Y + CharacterId + 帧头 |
| `InputAckPacket` | ~24 字节 | LastProcessedClientTick + ServerTick + EchoClientTick + 帧头 |
| `InteractionSyncPacket` | ~40 字节 | SlotIdx + InteractableId + InteractorId + StateBits + ServerTick + 帧头 |
| `SceneObjectSyncPacket`（无 Transform） | ~48 字节 | ObjectId + StateBits + CooldownEndTick + OwnerCharacterId + HasTransform + ServerTick + 帧头 |
| `SceneObjectSyncPacket`（含 Transform） | ~80 字节 | 上述 + 6 × float Transform 字段 |

> 帧头开销 = `FrameHeaderSize`（6B）+ SyncPacket 基类（Kind + ProtocolVersion）+ MemoryPack union 判别标签 ≈ 16B。

### 2.2 单玩家带宽估算（100 玩家压测场景）

假设场景：100 玩家同 shard，AOI 兴趣集平均 20 个远程实体，角色持续移动。

| 数据流 | 频率 | 单包体积 | 带宽 |
| --- | --- | --- | --- |
| `SnapshotPacket`（全量，20Hz） | 20/s | ~240 字节 | 4.8 KB/s = 38.4 kbps |
| `SnapshotPacket`（增量压缩后） | 20/s | ~80 字节 | 1.6 KB/s = 12.8 kbps |
| `InputPacket`（60Hz 上行） | 60/s | ~40 字节 | 2.4 KB/s = 19.2 kbps |
| `InputAckPacket`（60Hz 下行） | 60/s | ~24 字节 | 1.4 KB/s = 11.5 kbps |
| `InteractionSyncPacket`（偶发） | ~1/s | ~40 字节 | 0.04 KB/s = 0.3 kbps |
| `SceneObjectSyncPacket`（偶发） | ~1/s | ~48 字节 | 0.05 KB/s = 0.4 kbps |

**带宽汇总**：

| 场景 | 下行带宽 | 上行带宽 | 合计 | 目标 |
| --- | --- | --- | --- | --- |
| 全量快照（无增量压缩） | 38.4 + 11.5 + 0.7 = 50.6 kbps | 19.2 kbps | **69.8 kbps** | < 100 kbps ✓ |
| 增量压缩后（典型场景） | 12.8 + 11.5 + 0.7 = 25.0 kbps | 19.2 kbps | **44.2 kbps** | < 100 kbps ✓ |

> **结论**：典型场景（增量压缩生效）下单玩家带宽 ≈ 44.2 kbps，远低于 100kbps 目标。即使全量快照（无增量压缩）也仅 69.8 kbps，仍在预算内。
>
> **增量压缩收益**：SnapshotPacket 带宽从 38.4 kbps 降至 12.8 kbps，降幅 66.7%。

### 2.3 100 玩家压测带宽汇总（理论估算）

| 场景 | 单玩家平均 | 100 玩家合计 | 网卡负载（100Mbps） |
| --- | --- | --- | --- |
| 全量快照 | 69.8 kbps | 6.98 Mbps | 7.0% |
| 增量压缩 | 44.2 kbps | 4.42 Mbps | 4.4% |

> 100 玩家场景下，即使全量快照也仅占 100Mbps 网卡的 7%，容量瓶颈在 CPU（Grain 调度）而非带宽。

### 2.4 待实测填充项

以下数据待 Task E.5 `NetworkLoadHarness` 100 玩家压测后填充：

| 指标 | 理论估算 | 实测值（待填充） |
| --- | --- | --- |
| 单玩家平均带宽 | 44.2 kbps（增量压缩） | _待 E.5.1 填充_ |
| 单玩家峰值带宽 | ~80 kbps（AOI 密集 + 全量快照） | _待 E.5.1 填充_ |
| 端到端延迟 p50 | ~50ms | _待 E.5.1 填充_ |
| 端到端延迟 p99 | ~100ms | _待 E.5.1 填充_ |
| 吞吐（packets/s/玩家） | ~140（20 snapshot + 60 input + 60 ack） | _待 E.5.1 填充_ |
| 丢包恢复时间 | ~200ms（5 tick × 33ms + 重传 RTT） | _待 E.5.2 填充_ |
| 弱网下带宽（200ms 延迟 + 5% 丢包） | ~50 kbps（冗余重传增加上行） | _待 E.5.2 填充_ |

## 3. 性能优化策略

本系统通过四层策略协同降低带宽与延迟：增量编码、频率裁剪、限流、JitterBuffer 自适应。

### 3.1 增量编码（Task D.3）

`ZoneShardGrain.BuildDeltaSnapshot` 实现 SnapshotPacket 增量压缩：

- **Baseline 维护**：`_lastSnapshot` 始终保存完整状态，作为下次增量比对基准。
- **强制全量**：每 `FullSnapshotIntervalTicks`（60 tick = 1 秒）强制下发一次全量快照（`BaselineTick=0`），避免增量误差累积。
- **变化检测阈值**（`EntityDeltaChanged`）：
  - Transform 位置/旋转：阈值 0.01f（避免浮点抖动频繁触发增量）。
  - State 属性（Health/Mana/Level 等）：整数差异 ≥1 即认为变化。
  - MovementState：移动模式/速度/落地标志任何变化都算。
  - AnimationState：事件驱动，任何字段变化都算。
- **客户端重建**：`SnapshotApplySystem` 基于上一快照 + 增量 delta 重建完整状态；缺失 baseline 时请求重传。

**收益**：平均 SnapshotPacket 体积从 ~240 字节（10 EntityDelta）降至 ~80 字节（仅变化字段），带宽降幅 66.7%。

### 3.2 频率裁剪（CharacterSyncConfig）

`CharacterSyncConfig`（`Horizon.Game.Message\Sync\CharacterSyncConfig.cs`）声明各同步类型的下发频率：

| 同步类型 | 频率 | 触发策略 | 带宽贡献 |
| --- | --- | --- | --- |
| 位置（Transform） | 20Hz（50ms） | 每 tick 下发 | 主要带宽来源 |
| 移动状态（MovementState） | 10Hz（100ms）+ 变化触发 | 移动模式/落地标志变化时立即下发 | 中等 |
| 动画状态（AnimationState） | 事件驱动 | 仅 Montage 触发/结束事件下发 | 极低 |
| 属性（EntityState 扩展） | 1Hz（1s）+ 变化触发 | Mana/Level/Exp/Stamina 变化时立即下发 | 极低 |

`ZoneShardGrain` 在快照生成时按策略裁剪字段：位置每 tick 必带，移动状态每 100ms 心跳，属性每秒一次，动画仅事件时携带。

### 3.3 限流（GatewaySyncDispatcher.SessionBandwidthTracker）

`GatewaySyncDispatcher` 维护 per-session 带宽跟踪器，超阈值时降频：

| 配置 | 默认值 | 说明 |
| --- | --- | --- |
| `BandwidthThresholdKbps` | 100.0 | 带宽阈值（kbps） |
| `NormalSnapshotHz` | 20 | 正常快照频率 |
| `ThrottledSnapshotHz` | 10 | 限流快照频率（超阈值时降频） |
| `RecoverySeconds` | 3 | 连续 N 秒低于阈值后回升频率 |

工作机制：
1. `EstimatePacketSizeBytes` 按包类型预估字节数（如 SnapshotPacket = 16 + 24 + deltaCount × 80）。
2. `SessionBandwidthTracker.RecordBytes` 在 1 秒滚动窗口内累计下发字节数。
3. 窗口滚动时计算 `kbps = bytes * 8 / 1024 / seconds`。
4. 超阈值 → 降到 10Hz，告警一次；连续 3 秒低于阈值 → 回升到 20Hz。
5. `ZoneShardGrain` 通过 `GetSessionSnapshotHz(sessionId)` 查询当前频率，按 session 调整推送节奏。
6. `GetBandwidthSnapshot()` 返回各 session 带宽，用于监控面板 / Prometheus 导出。

### 3.4 JitterBuffer 自适应（Task D.1）

`JitterBuffer` 基于 RTT 的 EMA 与方差，动态调整插值延迟窗口：

- **EMA 平滑**：α=0.2，公式 `emaRtt = (1-α) * emaRtt + α * rttMs`。
- **方差跟踪**：`rttVariance = (1-α) * rttVariance + α * (rtt - emaRtt)^2`。
- **自适应延迟窗口**：80-200ms，公式 `Clamp(emaRtt * 1.5 + sqrt(rttVariance), 80, 200)`。
- RTT 平稳（方差小）→ 收敛到 80ms，保证低延迟体验。
- RTT 抖动大（方差大）→ 不超过 200ms，保证平滑性。

**与位置插值的协同**：
- `InterpolationSystem` 使用 `ComputeInterpolationDelayMs()` 返回的延迟作为插值窗口。
- 弱网下延迟窗口自动扩大，避免实体位置跳变；网络恢复后自动收敛到 80ms。

## 4. 容量规划建议

### 4.1 单 shard 容量

基于 `SyncLoadHarness` 1024 session 纯逻辑压测基准（已存在）：

| 维度 | 容量 | 瓶颈 |
| --- | --- | --- |
| 并发会话数 | 1000 sessions | CPU（Grain 调度） |
| AOI 订阅密度 | ~50 实体/玩家 | 内存（_simulatedEntities 字典） |
| 快照生成吞吐 | 1000 × 20Hz = 20000 snapshots/s | CPU（BuildDeltaSnapshot 比对） |
| 带宽吞吐 | 1000 × 44.2 kbps = 44.2 Mbps | 网卡（100Mbps 网卡支持 ~1000 玩家） |

> 单 shard 1000 会话基准来自 `SyncLoadHarness` 1k 会话纯逻辑压测。实际端到端容量受 TCP 连接数、序列化 CPU 开销、AOI 兴趣集大小影响，建议生产环境预留 20% 余量，即单 shard 800 玩家。

### 4.2 集群扩展性

| 维度 | 扩展性 | 说明 |
| --- | --- | --- |
| shard 数量 | 线性扩展 | 每 shard 独立 AOI + 状态，按 shardKey 分片，无跨 shard 状态共享 |
| 玩家总量 | 线性扩展 | 总玩家数 = shard 数 × 单 shard 容量 |
| 跨 shard 交互 | 走 Gateway 路由 | 跨 shard 交互（如交易、跨区域移动）经 Gateway 转发，不直接走 Grain 间调用 |

**扩展建议**：
- 按地理区域分 shard（如东大陆 / 西大陆各 1 shard）。
- 单 shard 玩家数超过 800 时考虑分裂（split shard）。
- 跨 shard 移动采用"先在新 shard 注册 → 通知旧 shard Despawn → 客户端切 shard"三步流程。

### 4.3 带宽预算

基于 100kbps/玩家目标：

| 网卡规格 | 支持玩家数 | 建议部署 |
| --- | --- | --- |
| 100 Mbps | ~1000 玩家 | 单 shard 单机部署 |
| 1 Gbps | ~10000 玩家 | 多 shard 单机或小集群 |
| 10 Gbps | ~100000 玩家 | 多 shard 集群，每节点 1-2 Gbps |

**带宽预算分配**（单玩家 100kbps）：
- SnapshotPacket（下行）：~50 kbps（含增量压缩）
- InputPacket（上行）：~20 kbps
- InputAckPacket（下行）：~12 kbps
- 其他（InteractionSync/SceneObjectSync/Event）：~18 kbps
- 冗余重传预留：~10 kbps（弱网下上行增加）

### 4.4 CPU 与内存规划

| 组件 | CPU 占比 | 内存占用 |
| --- | --- | --- |
| `ZoneShardGrain.TickAsync` | 主要 CPU 消耗（移动校验 + 快照生成） | ~100MB / 1000 实体（_simulatedEntities + _sceneObjectStates） |
| `GatewaySyncDispatcher` | 中等（fanout 分派 + 带宽计数） | ~50MB / 1000 session（_bandwidthTrackers） |
| `SyncPacketCodec` | 中等（MemoryPack 序列化 + LZ4 压缩） | ~20MB（ArrayPool 缓冲） |
| `JitterBuffer` | 极低（EMA 计算） | ~2KB / session（20 RTT 样本） |

**CPU 扩展建议**：
- 单 shard 1000 玩家场景下 `ZoneShardGrain.TickAsync` 是主要 CPU 瓶颈。
- 优化方向：并行化 `MovementValidator.Validate`（按 entity 分片）、SIMD 化 `EntityDeltaChanged` 比对。
- 内存优化：`_simulatedEntities` 与 `_sceneObjectStates` 使用 `ArrayPool<T>` 减少 GC 压力。

## 5. 监控指标

### 5.1 Prometheus 指标导出

`GatewaySyncDispatcher` 暴露以下指标供 Prometheus 采集：

| 指标 | 类型 | 说明 |
| --- | --- | --- |
| `sync_packets_dispatched_total` | Counter | 累计下发的 SyncPacket 数 |
| `sync_packets_dropped_offline_total` | Counter | 因 session 离线丢弃的包数 |
| `sync_dispatch_failed_total` | Counter | 分派失败的包数 |
| `sync_session_bandwidth_kbps` | Gauge（per session） | 单 session 当前带宽（kbps） |
| `sync_session_snapshot_hz` | Gauge（per session） | 单 session 当前快照频率（Hz） |
| `sync_jitter_buffer_ema_rtt_ms` | Gauge（per session） | JitterBuffer EMA RTT（ms） |
| `sync_jitter_buffer_interpolation_delay_ms` | Gauge（per session） | 自适应插值延迟（ms） |

### 5.2 告警阈值建议

| 指标 | 告警阈值 | 说明 |
| --- | --- | --- |
| `sync_session_bandwidth_kbps` | > 90 kbps（持续 10s） | 接近 100kbps 上限，即将触发限流 |
| `sync_session_snapshot_hz` | == 10 Hz（持续 30s） | 长期限流未恢复，可能网络拥塞 |
| `sync_packets_dropped_offline_total` 增速 | > 10/s | 大量 session 离线，可能服务器异常 |
| `sync_jitter_buffer_ema_rtt_ms` | > 300ms | 网络延迟过高，影响体验 |
| `sync_jitter_buffer_interpolation_delay_ms` | == 200ms（持续 10s） | 自适应延迟达上限，网络抖动严重 |

## 6. 关键文件索引

### 压测工具

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\LoadTest\SyncLoadHarness.cs` — 单 shard 纯逻辑压测（1024 session，已存在）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\LoadTest\NetworkLoadHarness.cs` — 端到端网络压测（100+ 玩家，Task E.1 待落地）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\LoadTest\WeakNetworkSimulator.cs` — 弱网仿真（Task E.2 待落地）

### 性能优化实现

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\Server\GatewaySyncDispatcher.cs` — per-session 带宽跟踪器 + 限流状态机
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Orleans.Grains\World\ZoneShardGrain.cs` — `BuildDeltaSnapshot` 增量压缩 + `EntityDeltaChanged` 阈值
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\HundunWorld\Script\ManagedHundunWorld\Network\Sync\JitterBuffer.cs` — RTT EMA + 自适应插值延迟
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\CharacterSyncConfig.cs` — 频率裁剪策略
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\InputSendSystem.cs` — InputPacket 冗余重传

### 协议（详见 NETWORK_PROTOCOL.md）

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPackets.cs` — 同步包定义
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPacketCodec.cs` — 帧编解码器
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\Components\SyncComponents.cs` — 同步组件定义
