# HundunWorld / Horizon MMORPG 网络同步代码调研报告

> 调研日期：2026-07-26
> 调研范围：Horizon.sln 全部服务端项目（Gateway / Orleans / ECS / Message / Core）与 Flax 客户端（HundunWorld/Source）
> 主题：MMORPG 网络同步——架构、状态同步、预测/回滚、可靠性、AOI、重连、性能与测试

---

## 一、总体架构

```
Flax 客户端 (HundunWorld, C#/FlaxEngine)
   │  TCP (TouchSocket TcpService) + 8 字节线路帧 (HorizonMessageAdapter)
   ▼
Horizon.Game.Gateway  ── Orleans Client ──►  Horizon.Orleans.Silo
   │  GameNetworkServer.cs                        │  ZoneShardGrain (世界分片/权威模拟)
   │  ConnectionManager (会话)                    │  ZoneShardAoi (chunk 订阅)
   │  GatewaySyncDispatcher (fanout 分发)         │  MovementValidator (权威回放校验)
   ▲                                              │
   └── IZoneShardFanoutObserver 推送 (WorldChunkDiffPacket)
```

- **传输层**：TouchSocket 4.x `TcpService`（纯 TCP，无 UDP/KCP/QUIC 通道）。关键文件：`Horizon.Game.Gateway/Network/GameNetworkServer.cs`（884 行）、`Horizon.Game.Gateway/Configuration/NetworkOptions.cs`。
- **序列化**：MemoryPack 为主（`HorizonMessagePacket`、`SyncFrameMessage`），可选 LZ4 压缩（`HorizonMessagePacket.cs` 引用 K4os.Compression.LZ4），Orleans 侧叠加 `[GenerateSerializer]`。
- **协议双层帧**：内层 `SyncPacketCodec`（6 字节同步帧头，`Horizon.Game.Message/Sync/SyncPacketCodec.cs`）→ 包装为 `SyncFrameMessage`（`Horizon.Game.Message/Network/SyncTransportMessages.cs`）→ 外层 `HorizonMessageAdapter.PackMessage` 8 字节线路帧。同步包明确 `compress: false`（延迟敏感）。

---

## 二、各维度实现现状

### 2.1 状态同步（快照 + 增量）——已实现，质量较高

| 维度 | 现状 | 证据 |
|---|---|---|
| 位置快照 | 20 Hz（50ms）每 tick 下发 | `Horizon.Game.Message/Sync/CharacterSyncConfig.cs` |
| 移动状态 | 10 Hz 心跳 + 变化触发 | 同上 |
| 动画状态 | 纯事件驱动（Montage 触发/结束） | 同上 + `ZoneShardGrain.TriggerMontageAsync` |
| 属性 | 1 Hz 心跳 + 变化触发 | 同上 |
| 全量快照兜底 | 每 60 tick 周期性全量 + 强制标志 | `Horizon.Orleans.Grains/World/ZoneShardGrain.cs` TickAsync（L524，`forceFullThisTick`） |
| 增量 diff | `WorldChunkDiffPacket`，DiffSeqStart/End 序号 | `ZoneShardGrain.cs` L430-437 |

设计文档级注释完整（频率数值依据、客户端 100ms 插值窗口匹配），且已有历史 bug 修复记录（静止实体不进全量快照问题，L519-523）。

### 2.2 AOI（兴趣区域）——已实现，基于 Morton chunk

- `Horizon.Game.Core/World/ZoneShardAoi.cs`：纯数据结构，`ChunkMortonKey → HashSet<sessionId>` 双向映射，Orleans 无依赖、可单测。
- 客户端跨 chunk 边界时上行 `SubscriptionUpdatePacket`（`Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs` 的 `PlayerChunkChanged` 事件）。
- 服务端 fanout 按 chunk 查订阅者（`ZoneShardGrain.cs` L487）。
- **缺口**：AOI 粒度是"订阅哪些 chunk"，无距离分级（近处高频/远处降频），也无实体级（非 chunk 级）剔除；所有订阅 chunk 内的实体 delta 同频率下发。

### 2.3 客户端预测 + 服务端权威（混合模式）——已实现，全链路

服务端权威侧：
- `ZoneShardGrain.SubmitInputAsync`（L1214）接收输入 + 上报终点 → `Horizon.Game.Core/Sim/MovementValidator.cs`（289 行）按 `MovementFormula` 回放 → 偏差 > 0.5m 下发 `CorrectionPacket`。
- 反作弊：硬性速度上限 200 m/s、加速度上限 50 m/s²、瞬移阈值 100m、跳跃次数限制（`MovementValidator.Options`）。
- 服务端无物理引擎，地面高度通过 `GroundHeightSampler` 委托注入（heightmap/chunk 几何），避免把客户端拉到地下。

客户端预测侧（`Horizon.Game.ECS.Arch`，Arch ECS）：
- `LocalSimulationSystem`：FixedUpdate 1/60s 本地预测，维护输入历史。
- `ReconciliationSystem`：FixedUpdate order 20，处理 InputAck（清理已确认输入 + 重播未确认输入）和 Correction（>0.5m 强制吸附）。
- `InterpolationSystem`：Render 阶段非玩家实体插值 + dead reckoning（速度向量推算），100ms 插值窗口。
- `InputSendSystem`：NetworkSend 阶段打包 InputPacket；**Task D.2 冗余重传**：64 容量环形缓冲，ACK 落后 >5 tick 时整包重发抗丢包。
- 缓冲区族：`InputHistoryBuffer`、`SnapshotReceiveBuffer`、`CorrectionReceiveBuffer`、`InputAckReceiveBuffer`、`EventReceiveBuffer`。

Flax 客户端接线：`HundunWorld/Source/Game/Network/NetworkSyncManager.cs`（Hybrid 模式：预测+校验+插值）、`SyncPacketMessageHandler.cs`、`ECSUpdateDriver.cs`。

**注意**：存在**两套并行实现**——ECS.Arch 的系统族是新主链路；`NetworkSyncManager` 里还有一套自己的 `predictionBuffer`/插值字段（Flax Script 组件）。两套逻辑并存是隐患（见问题清单 P2）。

### 2.4 可靠性与延迟处理

| 机制 | 现状 |
|---|---|
| RTT/抖动/丢包统计 | 客户端 `NetworkOptimizer`（RTT 样本 20 个、抖动方差、带宽统计），但**未见服务端↔客户端统一的 RTT 测量协议**；HeartbeatResponse 带 Latency 字段 |
| 输入冗余重传 | 已实现（InputSendSystem Task D.2） |
| 快照序号 | DiffSeqStart/End 有，客户端 `SnapshotReceiveBuffer` 排序 |
| 丢包检测 | 依赖 ACK 序号推断，无 NACK 协议 |
| 传输 | 纯 TCP——**无可靠 UDP / 多通道**；高频位置同步走 TCP 是 MMORPG 的常见争议点（队头阻塞会放大抖动） |
| 心跳 | 30s 客户端心跳 → Redis presence TTL 90s 刷新（`HeartbeatHandler.cs`）；网关另有 TCP KeepAlive 探测（需反射访问 Socket，`GameNetworkServer.cs` L260-292，脆弱点） |

### 2.5 断线重连——已实现，设计完整

- `Horizon.Game.Core/Sim/ReconnectFlow.cs`：纯函数编排器，`ReconnectResumePacket` → `PlayerSessionState.ApplyReconnect` 决策 → `ReconnectPlan`（Launcher 补丁 / manifest / chunk diff 补发 / 断连）。
- 支持按 diff 序号增量追平（`serverHeadDiffSeq` 对比）。
- `PlayerDespawnScheduler`、`CharacterPresenceMonitorHostedService` 处理掉线实体的延迟移除。

### 2.6 性能与规模化

已做：
- fanout 队列有界 Channel 8192 + DropOldest 反压（`GatewaySyncWiring.cs`）
- **一次编码多会话复用**（Task 15/16，`GameConnectionPacketSink.Encode/Send(wireBytes)`）
- observer 快照缓存（Task 12）、字段级缓冲复用（Task 10.x）、多 worker drain（Task 18）、lock-free 带宽统计（Task 17）
- **per-session 带宽自适应限流**：1s 滚动窗口，超 100 kbps 降快照 20→10 Hz，连续恢复后回升（`GatewaySyncDispatcher.SessionBandwidthTracker`）
- 带宽上限 100 kbps 对齐 MMORPG 工业标准

缺口：
- 快照 payload 用 MemoryPack 全量序列化实体数组，**无字段级 bitmask 增量、无量化（坐标 float32 → int16/fixed-point）、无 bitstream 压缩**；注释明示同步包不压缩
- 无跨网关分片负载均衡策略（ZoneShardGrain 固定主键）
- `Horizon.PerformanceTests` 存在但网络同步的端到端压测（N 客户端 × M 实体吞吐）未见系统化用例

### 2.7 测试覆盖

`Horizon.Game.Gateway.Tests` 中相关用例（40+ 文件中含）：
- `CharacterSyncConfigPolicyTests.cs`（频率策略）
- `InputRetransmitTests.cs`（冗余重传）
- `BandwidthBudgetTests.cs`（带宽限流）
- `GatewaySyncWiringTests.cs`（fanout 链路）
- `SyncPacketCodecBenchmarkTests.cs`（编码基准）
- `CrossHardwareConsistencyTests.cs`（跨硬件确定性——对预测/回滚一致性很关键）
- `InteractionSyncChainTests.cs`、`InteractionStateMachineTests.cs`

**缺口**：无端到端集成测试（真实 TCP 客户端 ↔ 网关 ↔ Silo 全链路）；无高丢包/高延迟网络仿真测试；无预测回滚的确定性回归测试（虽然有 CrossHardwareConsistency）。

---

## 三、问题清单（按优先级）

| # | 优先级 | 问题 | 证据 |
|---|---|---|---|
| P0 | 高 | **TCP 单通道承载全部高频位置同步**：队头阻塞在弱网（丢包 1-3%）下会让 20Hz 位置包排队，插值缓冲被抽干后出现"瞬移"。工业实践是位置/移动走 UDP（KCP/ENet/自定义可靠 UDP），指令类走 TCP。 | `GameNetworkServer.cs` 仅 `TcpService`；`NetworkOptions.cs` 无 UDP 配置 |
| P0 | 高 | **无统一 RTT 测量与服务器时钟同步**：客户端插值延迟固定 100ms（`NetworkSyncManager.InterpolationDelay`），不随 RTT/抖动自适应；预测回滚的 tick 对齐依赖客户端本地 tick 序号，未见服务端-客户端时钟偏差估计。 | `NetworkOptimizer` 只做统计未反馈到同步参数 |
| P1 | 中高 | **同步数据未量化/位打包**：位置 float32×3 + yaw float32，20Hz × N 实体直接 MemoryPack 序列化数组；带宽限流只是事后降频，未从源头减字节。 | `ZoneShardGrain.cs` L419-437 delta 结构；`GameConnectionPacketSink` `compress:false` |
| P1 | 中高 | **AOI 无距离分级降频**：chunk 内所有实体同频下发，远处实体浪费带宽；chunk 内也无实体数量上限（同屏爆炸时仅靠全局限流兜底）。 | `ZoneShardAoi.cs` 纯集合映射 |
| P2 | 中 | **客户端两套同步逻辑并存**（ECS.Arch 系统族 vs NetworkSyncManager 自有预测缓冲），职责边界不清，后续维护易双改漂移。 | `NetworkSyncManager.cs` vs `Horizon.Game.ECS.Arch/Systems/*` |
| P2 | 中 | **网关断连检测依赖反射取私有 Socket**（TouchSocket 内部字段），库升级即失效风险；Closed 事件不可靠已自带注释承认。 | `GameNetworkServer.cs` L146, L260-292 |
| P3 | 低 | 心跳 30s 间隔对 MMO 在线状态偏长，高安全场景的 5s 检活仍是 TODO。 | `HeartbeatHandler.cs` 注释 |
| P3 | 低 | 无网络仿真（clumsy/仿真延迟注入）的自动化测试，预测/回滚在 100ms+/3% 丢包下的表现无回归保障。 | 测试目录无相关用例 |

---

## 四、后续开发计划（网络同步专项）

### 阶段 A：弱网体验与协议（预计 3-4 周）

1. **A1 双通道传输（P0）**
   - 网关增加 UDP 通道（建议 KCP，C# 生态可用 kcp4sharp 或自研可靠 UDP），`SyncFrameMessage` 中的位置/移动类（PacketKind 区分）走 UDP，登录/交易/聊天保留 TCP。
   - 落点：`GameNetworkServer`（新增 UdpService 监听）、`GameConnection`（双 endpoint）、`GameConnectionPacketSink.Send`（按 PacketKind 路由）。
   - 验收：丢包 3% + 100ms RTT 仿真下，远端角色插值不抽干、无肉眼瞬移。

2. **A2 RTT 测量与自适应插值（P0）**
   - 在心跳/InputAck 中嵌入服务端时间戳回显，客户端 `NetworkOptimizer` 的 RTT 样本驱动 `InterpolationSystem` 的缓冲窗口（clamp 50ms–400ms）。
   - 服务端 tick 对齐：`SubmitInputAsync` 携带 clientTick + 客户端估算的 RTT offset，供 `MovementValidator` 做更公平的校验窗口。
   - 落点：`HeartbeatHandler`、`InputAckReceiveBuffer`、`InterpolationSystem.InterpolationSpeed` 动态化。

### 阶段 B：带宽与规模（预计 2-3 周）

3. **B1 同步数据量化（P1）**
   - `SyncPacketCodec` 增加 bitstream 模式：坐标量化到 int16（分米/厘米精度按 chunk 局部坐标系），yaw 量化 byte，字段变化 bitmask。
   - 预期：单实体位置 delta 从 ~16B 降到 ~7B，20Hz 千人同图带宽减半以上。
   - 落点：`Horizon.Game.Message/Sync/SyncPacketCodec.cs`、`WorldChunkDiffPacket.Payload` 编码侧。

4. **B2 AOI 距离分级（P1）**
   - `ZoneShardAoi` 增加环形分级：同 chunk 20Hz、相邻 chunk 10Hz、2 环外 2Hz/仅事件。
   - `ZoneShardGrain.TickAsync` 按订阅环级选择下发频率（复用 `SessionBandwidthTracker` 的限流框架）。
   - 加同屏实体上限（如 200），超限按距离裁剪。

### 阶段 C：健壮性与工程化（预计 2 周，可与 B 并行）

5. **C1 客户端同步逻辑归一（P2）**：废弃 `NetworkSyncManager` 内部自有预测/插值实现，统一为 ECS.Arch 系统族的薄适配层（只做 Flax Actor 写回）。
6. **C2 断连检测去反射（P2）**：用应用层心跳超时（如 90s 无包主动踢）替代反射读 Socket；TouchSocket Closed 事件仅作快路径。
7. **C3 测试补齐（P3）**：
   - 网络仿真集成测试：注入 100/200ms 延迟 + 1%/3% 丢包，断言预测回滚收敛、插值不抽干。
   - 端到端压测用例加入 `Horizon.PerformanceTests`：N 模拟客户端 × M 实体，统计 P99 下发延迟与带宽。
8. **C4 心跳分级（P3）**：落实 `HeartbeatHandler` 中 SecurityLevel TODO，交易/支付场景 5s 检活。

### 建议里程碑

- M1（A1+A2）：弱网可玩——位置同步抗丢包、插值自适应。
- M2（B1+B2）：规模达标——千人同图带宽 ≤ 目标值（如每客户端 ≤ 50 kbps 均值）。
- M3（C1-C4）：工程债清零——单一同步链路、仿真回归入 CI。
