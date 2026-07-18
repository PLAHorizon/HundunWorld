# HundunWorld Netcode 架构基线

> 本文档基于已落地代码逆向梳理，作为网络同步迁移 spec 的阶段 0 基线。
> 所有事实均与当前代码库对齐，后续迁移以本文档为参照。

## 1. 概述（混合 netcode 模型）

HundunWorld 客户端采用 **客户端预测 + 服务端权威 + 快照插值** 的混合 netcode 模型：

- **客户端预测（Client Prediction）**：本地玩家在 `LocalSimulationSystem` 中以固定时间步（默认 1/60 秒）先行模拟移动，不等服务器回包即可即时响应输入，降低操作延迟感。
- **服务端权威（Server Authority）**：所有移动结果由服务端 `ZoneShardGrain` 通过 `MovementValidator` 回放校验，发现偏差超过阈值时下发 `CorrectionPacket` 强制修正。
- **快照插值（Snapshot Interpolation）**：远程实体（非本地玩家）由 `InterpolationSystem` 在渲染前对服务器快照做线性插值，平滑网络抖动；本地玩家不参与插值，由预测路径直接驱动。

该混合模型在一条链路上同时满足"本地玩家低延迟"与"远程实体平滑一致"两个目标，是后续网络同步迁移的核心不变量。

技术栈基线（已落地版本）：

| 组件 | 版本 | 用途 |
| --- | --- | --- |
| Arch | 2.0.0-beta | 客户端 ECS 世界存储（chunk-based，**非 Flecs.NET**） |
| Arch.System | 1.1.0 | ECS 系统抽象与查询 |
| Orleans | 10.0.1 | 服务端 Actor 模型（Grain） |
| TouchSocket | 4.1.1 | TCP 网关网络层 |
| MemoryPack | — | 二进制序列化 |
| K4os.Compression.LZ4 | — | 快照压缩 |

## 2. 架构图（层级描述）

自上而下的数据流层级如下：

```
┌─────────────────────────────────────────────────────────────┐
│  UE5 C++ 层（AActor / Tick / Blueprint）                     │
│  └─ AWorldSyncActor（UClass, BlueprintCallable）            │
│     · BeginPlay → InitializeSync()                          │
│     · Tick(deltaSeconds) → TickSync()                       │
└──────────────────────────┬──────────────────────────────────┘
                           │ UnrealSharp 互操作（P/Invoke）
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  C# 托管层（ManagedHundunWorld）                             │
│  └─ ArchEcsRuntime（单例）                                   │
│     └─ ArchWorldHost                                        │
│        · World（Arch.Core）                                  │
│        · SystemRegistry（反射注册 [ArchSystem] 标注的系统）  │
│        · Tick(δt) → 按 SystemGroup 顺序串行执行              │
│                                                              │
│     7 个客户端 ECS 系统：                                    │
│     NetworkReceive → FixedUpdate → Update → Render → NetworkSend │
│     · NetworkReceive 组含 InteractionApplySystem            │
│                                                              │
│  └─ NetworkRuntime（单例）                                   │
│     · WorldState（位置缓冲 / 事件队列）                      │
│     · SyncInbox（收件箱）                                    │
│     · JitterBuffer（RTT 抖动自适应）                         │
│     · MovementPrediction / SkillPrediction                  │
└──────────────────────────┬──────────────────────────────────┘
                           │ TouchSocket TCP（SyncPacketCodec 帧编解码）
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  Gateway 层（Horizon.Game.Gateway）                          │
│  └─ GameNetworkServer（TcpService，监听 0.0.0.0:7789）       │
│     · HorizonMessageAdapter（定长头帧适配）                  │
│     · SyncPacketHandler（SyncPacket 路由）                   │
│  └─ GatewaySyncDispatcher                                   │
│     · 从 IZoneShardFanoutSource 拉取 FanoutEvent            │
│     · 按 AOI 兴趣集查 ISessionRegistry                       │
│     · 经 IClientPacketSink 下发到客户端 endpoint             │
└──────────────────────────┬──────────────────────────────────┘
                           │ Orleans Grain 调用
                           ▼
┌─────────────────────────────────────────────────────────────┐
│  服务端 Orleans Silo 层                                      │
│  └─ ZoneShardGrain（按 shardKey 分片）                       │
│     · ZoneShardAoi（AOI 兴趣集：mortonKey → sessionIds）     │
│     · MovementValidator（权威移动回放 + 反作弊）             │
│     · RegisterTimer(1/60s) → TickAsync()                    │
│     · IZoneShardFanoutObserver 推送（snapshot/event/diff）   │
│  └─ IPlayerSessionGrain（会话生命周期）                      │
└─────────────────────────────────────────────────────────────┘
```

关键边界：
- **UE5 ↔ C#**：通过 UnrealSharp 互操作，`AWorldSyncActor` 是唯一入口。
- **C# ↔ Gateway**：通过 TouchSocket TCP，帧格式见 `NETWORK_PROTOCOL.md`。
- **Gateway ↔ Orleans**：Gateway 作为 Orleans Client，通过 Grain 接口调用 + Observer 推送双向通信。

## 3. 客户端预测流程

本地玩家的预测链路在客户端 ECS 中闭环：

```
玩家输入
   │
   ▼
PlayerInputComponent（写入 MoveX/MoveY/LookYaw/LookPitch/InputBits）
   │
   ▼  FixedUpdate 组 / order 10
LocalSimulationSystem
   · 查询 [PlayerInputComponent + NetworkIdentityComponent(IsLocalPlayer) + PredictedTransformComponent]
   · CurrentClientTick++（仅本地玩家）
   · 调用 MovementFormula.Step() 推进 PredictedTransformComponent
   · 记录 pred.ClientTick = CurrentClientTick
   · 写入 InputHistoryBuffer（供回滚重播）
   │
   ▼  NetworkSend 组 / order 0
InputSendSystem
   · 查询同上三元组（IsLocalPlayer=true）
   · 打包 InputPacket{ ClientTick, InputBits, LookYaw, LookPitch, MoveX, MoveY, CharacterId }
   · 入队 InputSendQueue（网络层批量消费）
   │
   ▼  TCP 上行
服务端 ZoneShardGrain.SubmitInputAsync()
```

要点：
- `LocalSimulationSystem` 通过 `NetworkIdentityComponent.IsLocalPlayer` 判定本地玩家（而非实体 ID 比对），保证 `CurrentClientTick` 正确递增。
- 输入固定时间步为 `1/60` 秒（`FixedDtSeconds`），与服务器 `MovementValidator.Options.TickDtSeconds` 严格对齐，否则回放结果不一致。
- `InputPacket.CharacterId` 由客户端显式携带（v4 协议修复），服务端单例 Handler 不再依赖握手时缓存的实例字段。

## 4. 服务端权威与协调（Reconciliation）

服务端权威回放与客户端回滚修正构成闭环：

### 4.1 服务端权威回放

`ZoneShardGrain.TickAsync()` 每 1/60 秒由 Orleans Timer 触发：

1. 遍历 `_simulatedEntities`，对每个有 `PendingInputs` 的实体调用 `MovementValidator.Validate()`。
2. `MovementValidator` 从服务器权威起点出发，按 `ClientTick` 升序回放输入序列，得到权威终点。
3. 比对客户端自报终点与权威终点：
   - 偏差 ≤ `PositionEpsilon`（默认 0.5m）：接受客户端结果，更新权威位置。
   - 偏差 > `PositionEpsilon`：生成 `CorrectionPacket`，强制把权威位置写回实体。
4. 每个实体（含静止）每 tick 产 `EntityDelta.Update`，组装成 `SnapshotPacket` 广播。

### 4.2 客户端 Reconciliation

`ReconciliationSystem`（FixedUpdate 组 / order 20，在 `LocalSimulationSystem` 之后执行）：

1. **InputAck 处理**：从 `InputAckReceiveBuffer` 读取服务器 `LastProcessedClientTick`，从 `InputHistoryBuffer` 取出未确认输入，从上次确认位置重播，清理已确认输入。
2. **Correction 处理**：从 `CorrectionReceiveBuffer` 读取修正包，计算预测位置与权威位置的 3D 距离：
   - `drift > CorrectionThreshold`（默认 0.5m）：强制吸附 `PredictedTransformComponent` 到权威位置，置 `NeedsReconciliation = true`。
   - `drift ≤ 阈值`：忽略，避免微小抖动。

`InputAckPacket` 与 `SnapshotPacket` 解耦，可在两次快照之间高频下发以缩短 reconciliation 窗口。

## 5. 快照插值（远程实体）

远程实体（非本地玩家）走快照插值路径：

### 5.1 快照应用（SnapshotApplySystem）

`SnapshotApplySystem`（NetworkReceive 组 / order 10）消费 `SnapshotReceiveBuffer`：

- **Spawn**：创建 Arch 实体，添加 `NetworkIdentityComponent` + `AuthTransformComponent`；远程实体额外加 `InterpolatedTransformComponent`，本地玩家加 `PlayerInputComponent` + `PredictedTransformComponent`。
- **Update**：刷新 `AuthTransformComponent`；对远程实体同步刷新插值起点（`StartX/Y/Z` = 上一帧权威位置）与目标（`TargetX/Y/Z` = 新权威位置），`Alpha` 重置为 0。**本地玩家实体的变换更新被跳过**（本地预测优先）。
- **Despawn**：销毁 Arch 实体并清理映射表。

### 5.2 插值推进（InterpolationSystem）

`InterpolationSystem`（Render 组 / order 0）查询所有 `InterpolatedTransformComponent` 实体：

- `Alpha += dt * InterpolationSpeed`（默认 `1/0.1 = 10/s`，即约 100ms 追平目标）。
- `Alpha ≥ 1`：直接吸附到目标位置。
- 否则：`pos = Start + (Target - Start) * Alpha` 线性插值。

本地玩家不携带 `InterpolatedTransformComponent`，不受本系统影响。

### 5.3 蓝图层抖动缓冲

`AWorldSyncActor.TryGetInterpolatedEntityPosition(entityId, rttMs, ...)` 暴露给蓝图：

- `rttMs > 0`：记录到 `NetworkRuntime.JitterBuffer`，由抖动缓冲自适应计算推荐延迟。
- 否则：回退到 100ms 固定延迟。
- 最终调用 `WorldState.TryGetInterpolatedPosition(entityId, delayMs, ...)` 取插值位置。

## 6. AOI 兴趣集

AOI（Area of Interest）由 `ZoneShardGrain` 内的 `ZoneShardAoi` 实现：

### 6.1 订阅模型

- **订阅单位**：`sessionId`（实际为 `characterId`）订阅一组 `mortonKey`（ChunkCell 16m 的 Morton 编码）。
- **接口**：
  - `SubscribeSessionAsync(sessionId, mortonKeys[])`：玩家进入区域时订阅。
  - `UnsubscribeSessionAsync(sessionId, mortonKeys[])`：离开区域时取消。
  - `RemoveSessionAsync(sessionId)`：断线时清理该 session 全部订阅（由 `IPlayerSessionGrain` 触发）。
- **查询**：`GetSubscribers(mortonKey)` 返回订阅该 chunk 的所有 session。

### 6.2 Fanout 分发

`ZoneShardGrain` 在广播时按 `ChunkMortonKey` 查询订阅者，构造 `FanoutEvent{ Packet, TargetSessionIds }`，通过 `IZoneShardFanoutObserver` 推送到 Gateway。

Gateway 侧 `GatewaySyncDispatcher` 接管：

1. 从 `IZoneShardFanoutSource.TryDequeueAsync()` 拉取 `FanoutEvent`。
2. 遍历 `TargetSessionIds`，查 `ISessionRegistry.TryGetEndpoint(sessionId)`：
   - 命中：经 `IClientPacketSink.Send(endpoint, packet)` 下发。
   - 未命中（session 离线）：计入 `DroppedOfflineCount`，限频告警（每 10s 最多一次）。

### 6.3 实体生命周期广播

`ZoneShardGrain.RegisterEntityAsync()` / `UnregisterEntityAsync()` 触发 Spawn/Despawn 广播：

- 新实体注册时，除向已有订阅者广播自身 Spawn 外，还会给新 session 补发当前所有已存在实体的 Spawn，让新玩家立即看到在场角色。
- 广播通过 `WorldChunkDiffPacket` 承载（`ChunkMortonKey = 0`，与握手订阅一致）。

### 6.4 已知限制与适配层

**已知限制**：
- `WorldChunkDiffPacket` 信封重载承载多种包类型（EntityDelta/InteractionSyncPacket/EventPacket/CorrectionPacket），通过 `PayloadType` 字段区分内部类型（`WorldChunkDiffPayloadType` 枚举：EntityDelta=0/InteractionSync=1/Event=2/Correction=3）
- 交互同步的 `ChunkMortonKey` 由 `ZoneShardGrain.GetChunkMortonKeyForEntity(entityId)` 根据交互对象实际位置查询，不再硬编码为 0
- 实体生命周期广播（Spawn/Despawn）仍使用 `ChunkMortonKey = 0`（与握手订阅一致），不走 AOI 过滤

**GatewaySyncDispatcher 适配层**：
- `GatewaySyncDispatcher` 从 `IZoneShardFanoutSource.TryDequeueAsync()` 拉取 `FanoutEvent`
- observer→source 适配：Orleans 的 `IZoneShardFanoutObserver` 推送模式适配为 `IZoneShardFanoutSource` 拉取模式，解耦 Grain 调用与 Gateway 下发
- 按 `TargetSessionIds` 查 `ISessionRegistry.TryGetEndpoint(sessionId)`，经 `IClientPacketSink.Send(endpoint, packet)` 下发
- session 离线时计入 `DroppedOfflineCount`，限频告警（每 10s 最多一次）

## 7. 反作弊

反作弊核心是服务端 `MovementValidator`（不依赖 Orleans，可在 grain / 单测 / 反外挂扫描器共享）：

### 7.1 校验维度

| 维度 | 阈值 | 触发原因 |
| --- | --- | --- |
| 预测漂移 | `PositionEpsilon = 0.5m` | `CorrectionReason.PredictionDrift` |
| 硬性速度上限 | `HardSpeedCap = 20 m/s` | `CorrectionReason.SpeedHackSuspected` |
| 跳跃次数 | 普通跳跃 1 次 / 轻功 3 次 | `CorrectionReason.JumpCountExceeded` |
| 碰撞穿越 | （预留） | `CorrectionReason.CollisionOverride` |

### 7.2 校验流程

1. 服务器从权威起点 `(start, startVz)` 出发，按 `ClientTick` 升序回放输入序列。
2. 逐 tick 调用 `MovementFormula.Step()`，跟踪 `maxObservedSpeed`、`jumpCount`、`isGrounded`。
3. 回放得到权威终点后：
   - 计算客户端自报终点的平均速度 `clientDistance / totalDt`，超 `HardSpeedCap` 判速度外挂。
   - 计算权威终点与客户端终点的 3D 偏差 `drift`，超 `PositionEpsilon` 判预测漂移。
   - 跳跃次数超限判 `JumpCountExceeded`。
4. 任一条件命中即生成 `CorrectionPacket`，携带权威终点、偏差、原因。

### 7.3 修正下发

`CorrectionPacket` 不直接走 `SyncPacket` union，而是以 `EventPacket` 负载形式下发（`SyncEvent.Kind = Unknown`，`Payload` 内嵌序列化的 `CorrectionPacket`），走可靠 + 高优先级通道，不受 snapshot tick 限制。客户端 `ReconciliationSystem.ProcessCorrection()` 据此吸附。

## 8. ECS 系统流水线

### 8.1 SystemGroup 执行顺序

`ArchWorldHost.Tick(δt)` 按 `SystemGroup` 枚举值升序串行执行各组，同组内按 `IArchSystem.Order` 升序：

| 枚举值 | SystemGroup | 语义 | 已落地系统 |
| --- | --- | --- | --- |
| 0 | `NetworkReceive` | 从网络/IO 队列拉取数据写入 ECS | SnapshotApplySystem, EventApplySystem, InteractionApplySystem |
| 1 | `FixedUpdate` | 固定时间步模拟（物理、移动预测、回滚） | LocalSimulationSystem, ReconciliationSystem |
| 2 | `Update` | 逐帧逻辑（AI、技能、状态机） | （暂无） |
| 3 | `Render` | 渲染前同步（ECS → UE Actor / UI） | InterpolationSystem |
| 4 | `NetworkSend` | 把本地输入/状态打包发送到服务器 | InputSendSystem |

### 8.2 系统注册表

| 系统 | SystemGroup | Order | 职责 | 查询组件 |
| --- | --- | --- | --- | --- |
| `SnapshotApplySystem` | NetworkReceive | 10 | 消费 `SnapshotReceiveBuffer`，应用 Spawn/Update/Despawn | 维护 EntityId→Entity 映射 |
| `InteractionApplySystem` | NetworkReceive | 15 | 消费 `SyncPacketInbox.InteractionEvents`，Spawn/Update/Despawn 交互 Arch Entity，回调 `UnrealNarrativeBridge` | `InteractionSyncComponent` |
| `EventApplySystem` | NetworkReceive | 20 | 消费 `EventReceiveBuffer`，应用技能/伤害/死亡事件 | `NetworkIdentityComponent` |
| `LocalSimulationSystem` | FixedUpdate | 10 | 本地预测移动，递增 `CurrentClientTick` | `PlayerInputComponent + NetworkIdentityComponent + PredictedTransformComponent` |
| `ReconciliationSystem` | FixedUpdate | 20 | 处理 InputAck 重播 + Correction 吸附 | `PlayerInputComponent + PredictedTransformComponent + NetworkIdentityComponent` |
| `InterpolationSystem` | Render | 0 | 远程实体位置线性插值 | `InterpolatedTransformComponent` |
| `InputSendSystem` | NetworkSend | 0 | 打包本地玩家输入到 `InputSendQueue` | `PlayerInputComponent + NetworkIdentityComponent + PredictedTransformComponent` |

### 8.3 注册机制

- **属性标注**：`[ArchSystem(SystemGroup.X, order: N)]` 标注在系统类上。
- **反射注册**：`SystemRegistry.RegisterFromAssembly(host, assembly)` 扫描带该属性的 `IArchSystem` 实现，用无参构造函数实例化后 `host.AddSystem()`。
- **排序保证**：`ArchWorldHost.AddSystem()` 在同组桶内按 `Order` 升序排序。
- **线程约束**：`ArchWorldHost.Tick()` 必须在主线程调用（UE Tick 驱动），`IsTicking` 期间禁止 AddSystem/RemoveSystem，且 `Tick` 不可重入。

### 8.4 网络缓冲组件

系统间通过单例缓冲解耦网络层与 ECS 层：

| 缓冲 | 方向 | 消费系统 |
| --- | --- | --- |
| `SnapshotReceiveBuffer` | 网络 → ECS | SnapshotApplySystem |
| `EventReceiveBuffer` | 网络 → ECS | EventApplySystem |
| `SyncPacketInbox.InteractionEvents` | 网络 → ECS | InteractionApplySystem |
| `InputAckReceiveBuffer` | 网络 → ECS | ReconciliationSystem |
| `CorrectionReceiveBuffer` | 网络 → ECS | ReconciliationSystem |
| `InputHistoryBuffer` | ECS 内部 | LocalSimulationSystem 写 / ReconciliationSystem 读 |
| `InputSendQueue` | ECS → 网络 | InputSendSystem 写 / 网络层读 |

## 9. AWorldSyncActor 桥接机制

`AWorldSyncActor` 是 UE5 C++ 与 C# Arch ECS 之间的唯一桥接点。

### 9.1 生命周期

- **BeginPlay**：若 `AutoInitializeSync = true`（默认），调用 `InitializeSync()`；若 `AutoTickSync = true`，开启 Actor Tick（`ActorTickInterval = 0`）。
- **Tick(deltaSeconds)**：若 `AutoTickSync = true`，调用 `TickSync(deltaSeconds)`。
- **TickSync**：累加 `_timeSinceLastEcsTick`，达到 `EcsTickRate`（默认 0.05s = 50ms = 20Hz）后推进 `ArchEcsRuntime.Tick(δt)`，重置累加器。

### 9.2 后端选择

`InitializeSync()` 根据 `EcsBackendOptions.UseArchEcs` 二选一：

- **true（新路径）**：取 `ArchEcsRuntime.Instance` 单例，调用 `EnsureDefaultSystems(WorldState)` 装配默认网络系统。
- **false（回退路径）**：构造旧 `EcsWorld` + `NetworkSyncSystem`，便于灰度回滚。

`ArchEcsRuntime` 是单例宿主，封装 `ArchWorldHost`，不直接引用 UnrealSharp 类型，可在 CI 环境编译与单元测试。

### 9.3 蓝图可调用方法

`AWorldSyncActor` 通过 `[UFunction(FunctionFlags.BlueprintCallable)]` 暴露以下方法供蓝图消费：

| 方法 | 用途 |
| --- | --- |
| `InitializeSync()` | 手动初始化同步运行时 |
| `TickSync(float)` | 手动推进一帧 ECS |
| `TryGetNextSkillCast(out long, out int)` | 从事件队列取技能释放 |
| `TryGetNextDamage(out long, out long, out int, out bool)` | 取伤害事件 |
| `TryGetNextAttack(out long, out long)` | 取攻击事件 |
| `TryGetNextEntitySpawn(out long)` | 取实体生成事件 |
| `TryGetNextEntityDespawn(out long)` | 取实体销毁事件 |
| `TryGetInterpolatedEntityPosition(long, float rttMs, out float, out float, out float)` | 取插值位置（含 RTT 抖动自适应） |
| `PushLocalEntityPosition(long, float, float, float)` | 推送本地玩家位置到插值缓冲 |
| `GetSyncInboxSummary()` | 收件箱摘要字符串（AckTick/DiffSeq/Pending 等） |
| `TryGetLatestInputAck(out long, out long, out long)` | 最近一次服务器 input ACK |
| `GetDeveloperPanelSummary()` | 开发者面板综合摘要（Sync/Network/Prediction） |

### 9.4 关键属性

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `EcsTickRate` | 0.05 | ECS 推进周期（秒），20Hz |
| `AutoInitializeSync` | true | BeginPlay 自动初始化 |
| `AutoTickSync` | true | Actor Tick 自动推进 ECS |

## 10. 关键文件索引

### 客户端桥接与运行时

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\hundunworld\Script\ManagedHundunWorld\WorldSyncActor.cs` — `AWorldSyncActor` UE5 桥接
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\hundunworld\Script\ManagedHundunWorld\ECS\Arch\ArchEcsRuntime.cs` — 客户端 Arch ECS 单例宿主

### 客户端 ECS 核心

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Core\ArchWorldHost.cs` — ECS 世界宿主与 Tick 调度
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Core\SystemGroup.cs` — 系统分组枚举
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Core\IArchSystem.cs` — 系统接口
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Core\ArchSystemAttribute.cs` — 系统注册属性
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Core\SystemRegistry.cs` — 反射注册器

### 客户端 ECS 系统

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\SnapshotApplySystem.cs` — 快照应用（NetworkReceive / order 10）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\EventApplySystem.cs` — 事件应用（NetworkReceive / order 20）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\LocalSimulationSystem.cs` — 本地预测模拟（FixedUpdate / order 10）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\ReconciliationSystem.cs` — 回滚修正（FixedUpdate / order 20）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\InterpolationSystem.cs` — 远程实体插值（Render / order 0）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Systems\InputSendSystem.cs` — 输入打包发送（NetworkSend / order 0）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\hundunworld\Script\ManagedHundunWorld\ECS\Arch\Systems\InteractionApplySystem.cs` — 交互同步应用（NetworkReceive / order 15）

### 客户端网络缓冲

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\SnapshotReceiveBuffer.cs`
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\EventReceiveBuffer.cs`
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\InputAckReceiveBuffer.cs`
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\CorrectionReceiveBuffer.cs`
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\InputHistoryBuffer.cs`
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.ECS.Arch\Network\InputSendQueue.cs`

### 服务端

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Orleans.Grains\World\ZoneShardGrain.cs` — Zone Shard Grain（AOI + 权威模拟 + 反作弊）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\Server\GatewaySyncDispatcher.cs` — Gateway 侧 fanout 分派器
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\MovementValidator.cs` — 服务端移动校验器（反作弊）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\MovementFormula.cs` — 移动公式（客户端/服务端共享，保证确定性）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Core\Sim\CorrectionPacket.cs` — 位置修正包

### Gateway 网络层

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Gateway\Network\GameNetworkServer.cs` — TCP 网关服务器（监听 7789）
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Gateway\appsettings.json` — 网络端口配置（TcpPort = 7789）

### 协议（详见 NETWORK_PROTOCOL.md）

- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPackets.cs` — 同步包定义
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\SyncPacketCodec.cs` — 帧编解码器
- `c:\Works\GitHubProjects\HundunWorld_UE5\HundunWorld\Horizon.Game.Message\Sync\Components\SyncComponents.cs` — 同步组件定义

## 11. 交互同步流程（NarrativePro 桥接）

NarrativePro 的交互系统（InteractionComponent / InteractableComponent / PlayerInteractionComponent）已从 UE5 原生复制迁移至自定义 Arch ECS 同步层，走 TouchSocket + MemoryPack + Arch ECS 统一链路，避开 UE5 网络同步与 NarrativePro 原生网络同步。

### 11.1 上行链路（客户端 → 服务端）

客户端玩家触发交互时，不再调用 Server RPC，而是经桥接层上行：

1. UE5 侧 `UInteractionComponent` / `UPlayerInteractionComponent` 检测交互输入
2. 调用 `AWorldSyncActor.BridgeRequestInteract(interactableId, slotIdx)` UFunction
3. C# 侧 `UnrealNarrativeBridge.RequestInteract` 打包 `InteractionSyncPacket`，StateBits 高位置 `RequestStartFlag=0x80`
4. 经 `NetworkClient` TCP 上行到 Gateway
5. Gateway `SyncPacketHandler.RouteHandlerAsync` 识别 `InteractionSyncPacket` 分支，调用 `HandleInteractionUplinkAsync`
6. `HandleInteractionUplinkAsync` 解析 StateBits 高位区分意图类型（RequestStart/RequestStop），执行校验：
   - `interactorId` 非零
   - `interactableId` 非零
   - `slotIdx` 范围合法
   - 速率限制（每 interactorId 每秒最多 10 次）
   - 会话绑定校验（interactorId 与 characterId 一致）—— TODO
   - interactableId 存在性校验 —— TODO
7. 校验通过后转发到 `IZoneShardGrain`

### 11.2 服务端权威处理

`ZoneShardGrain` 维护交互槽位权威状态：

1. `_interactionSlots` 字典记录槽位占用（key: interactableId+slotIdx, value: interactorId+stateBits）
2. `GenerateInteractionSync` 前校验槽位空闲/归属：
   - Start 意图：目标槽位必须空闲
   - Stop 意图：目标槽位必须属于该 interactorId
3. 交互结束时清理槽位状态
4. 生成 `InteractionSyncPacket`（StateBits 下行状态位：Start=0x01/End=0x02/Stolen=0x04）
5. `BroadcastInteractionSyncAsync` 按 `ChunkMortonKey` 查询 AOI 订阅者，构造 `FanoutEvent` 推送到 Gateway
6. 交互同步包复用 `WorldChunkDiffPacket` 信封承载，通过 `PayloadType` 字段区分内部类型（EntityDelta=0/InteractionSync=1/Event=2/Correction=3）

### 11.3 下行链路（服务端 → 客户端）

1. Gateway `GatewaySyncDispatcher` 从 `IZoneShardFanoutSource` 拉取 `FanoutEvent`
2. 按 AOI 兴趣集查 `ISessionRegistry`，经 `IClientPacketSink` 下发到客户端
3. 客户端 `SyncPacketDispatcher` 识别 `InteractionSyncPacket`，路由到 `SyncPacketInbox.InteractionEvents` 队列
4. `InteractionApplySystem`（NetworkReceive 组 / order 15）消费队列：
   - **Spawn**：首次收到 interactableId 的包时创建 Arch Entity，添加 `InteractionSyncComponent`
   - **Update**：后续包更新 `InteractionSyncComponent` 字段
   - **Despawn**：StateBits 含 End(0x02) 或 Stolen(0x04) 时调用 `world.Destroy(entity)` + 字典清理
5. `InteractionApplySystem` 回调 `UnrealNarrativeBridge.NotifyInteractionStateChanged` / `NotifyInteractionEvent`
6. 桥接层通过 UnrealSharp 反射调用 UE5 侧 `HandleBridgeInteractionStateChanged` / `HandleBridgeInteractionEvent`

### 11.4 InteractionStateBits 编码约定

`InteractionStateBits`（位于 `Horizon.Game.Message\Sync\InteractionStateBits.cs`）是交互状态位编码的单一事实源：

| 位 | 常量 | 值 | 方向 | 说明 |
| --- | --- | --- | --- | --- |
| bit0 | `Start` | 0x01 | 下行 | 交互开始 |
| bit1 | `End` | 0x02 | 下行 | 交互结束 |
| bit2 | `Stolen` | 0x04 | 下行 | 交互被抢占 |
| bit7 | `RequestStartFlag` | 0x80 | 上行 | 请求开始交互 |
| bit6 | `RequestStopFlag` | 0x40 | 上行 | 请求停止交互 |

- 下行状态位（StateMask=0x07）与上行意图位（IntentMask=0xC0）占用不同 bit 区间
- 同一 `InteractionSyncPacket` 包类型复用为上行意图载体，服务端通过高位区分
- `InteractionApplySystem` 检测 `IsTerminal(stateBits)`（含 End/Stolen）触发 Despawn

### 11.5 NetworkId 注册表机制

UE5 侧 `UInteractionSubsystem` 维护 NetworkId ↔ UObject 映射注册表，替代 `GetUniqueID()` 占位实现：

- `InteractableNetworkIdRegistry`：TMap<long, UInteractableComponent*>
- `InteractorNetworkIdRegistry`：TMap<long, UInteractionComponent*>
- `GenerateStableNetworkId`：基于 Actor 名称 + 关卡名称的 CRC32 哈希，跨会话稳定
- `HandleBridgeInteractionStateChanged` 通过注册表解析 InteractableId/InteractorId 到 UE5 组件指针
- 支持跨交互者的状态变更（如其他玩家抢占同一交互槽）

### 11.6 交互事件路由

离散交互事件（InteractStart/InteractEnd/InteractStolen）通过 `EventPacket` 下发，但 `EventApplySystem` 不直接处理交互事件，而是在类注释中标注路由路径：

- `NetworkRuntime` 显式将 `InteractStart/End/Stolen` 事件路由到 `SyncPacketInbox.InteractionEvents` 队列
- `InteractionApplySystem.ProcessInteractionEvents` 构造 `InteractionEventPayload`（含 SourceEntityId/TargetEntityId）
- `InteractionEventNotification` 结构携带 `InteractableId`/`InteractorId` 完整载荷
- `WorldSyncActor.BridgeTryDequeueInteractionEvent` 正确解包 payloadArg1/payloadArg2（不再硬编码 0）
- C++ 侧 `HandleBridgeInteractionEvent` 的 `InteractStolen` 分支含槽位归属校验

## 12. 角色同步流程（阶段 B 扩展）

阶段 B 在原有 `AuthTransformComponent`（位置/旋转）与 `EntityStateAuthComponent`（HP/MaxHealth/StateBits）基础上引入三类新组件，覆盖 MMORPG 必需的移动模式、动画触发与完整角色属性。频率策略统一由 `CharacterSyncConfig` 静态类声明，`ZoneShardGrain` 在快照生成时按策略裁剪字段。

### 12.1 同步频率策略

`CharacterSyncConfig`（`Horizon.Game.Message\Sync\CharacterSyncConfig.cs`）是频率策略的单一事实源：

| 同步类型 | 频率 | 触发策略 | 承载组件 |
| --- | --- | --- | --- |
| 位置 / 旋转（Transform） | 20Hz（50ms） | 每 tick 下发，匹配客户端 100ms 插值窗口，留 50ms 抖动余量 | `AuthTransformComponent` |
| 移动状态（MovementState） | 10Hz（100ms）+ 变化触发 | 移动模式/落地标志变化时立即下发，否则 100ms 心跳 | `MovementStateAuthComponent` |
| 动画状态（AnimationState） | 事件驱动 | 仅 Montage 触发/结束事件下发；循环动画由客户端据 MovementState 自行驱动 | `AnimationStateAuthComponent` |
| 属性（EntityState 扩展字段） | 1Hz（1s）+ 变化触发 | Mana/Level/Exp/Stamina 等变化时立即下发，否则每秒强制下发保证最终一致性 | `EntityStateAuthComponent`（扩展） |

数值依据：
- 20Hz 位置 = 50ms 间隔，匹配客户端 100ms 插值延迟窗口，留 50ms 抖动余量。
- 10Hz 移动状态 = 100ms 间隔，足够驱动动画状态机过渡（Idle↔Run 混合时间通常 200ms）。
- 1Hz 属性心跳 = 1s 间隔，属性变化不频繁但需保证最终一致性（断线重连后 1s 内自愈）。

### 12.2 移动状态同步（MovementStateAuthComponent）

`MovementStateAuthComponent` 承担动画状态机驱动数据，与 `AuthTransformComponent` 配合：Transform 负责位置，本组件负责动画表现。

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `MovementMode` | `MovementMode`（byte 枚举） | Walk=0 / Run=1 / Jump=2 / Fall=3 / Swim=4 / Crouch=5 |
| `VelocityXZ_X` | float | 水平速度 X 分量（世界坐标系） |
| `VelocityXZ_Y` | float | 水平速度 Y 分量（UE5 中对应 Y 轴） |
| `IsGrounded` | bool | 是否接触地面（true=地面，false=空中） |
| `ServerTick` | long | 采样时的服务器 tick（用于插值排序） |

服务端 `ZoneShardGrain` 在快照生成时按 10Hz 心跳 + 变化触发写入本组件：
- 与上一 tick 比对 `MovementMode`/`IsGrounded`，变化时立即下发。
- 否则每 100ms 心跳一次完整 `MovementStateAuthComponent`。
- 客户端 `InterpolationSystem` 据此驱动动画混合（速度向量平滑过渡）。

### 12.3 动画状态同步（AnimationStateAuthComponent）

`AnimationStateAuthComponent` 仅同步触发型动画（Montage），循环动画不占用网络带宽：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `AnimMontageId` | uint | 动画 Montage 资源 ID（0 表示无 Montage 播放） |
| `AnimInstanceId` | uint | 动画实例 ID（区分同一 Montage 的不同播放实例/槽位） |
| `PlayRate` | float | 播放速率（1.0=正常速度） |
| `TimePosition` | float | 当前播放时间位置（秒） |
| `IsLooping` | bool | 是否循环播放 |
| `ServerTick` | long | 采样时的服务器 tick |

同步策略：
- **纯事件驱动**：仅 Montage 触发/结束事件时下发，无心跳。
- 循环动画（Idle/Run 循环）由客户端根据 `MovementStateAuthComponent` 自行驱动，不占用网络带宽。
- 客户端 `SnapshotApplySystem` 收到本组件后通过 UnrealSharp 反射调用 UE5 侧 `AnimationMontage` 播放接口。

### 12.4 属性同步（EntityStateAuthComponent 扩展）

`EntityStateAuthComponent` 在原有 `Health`/`MaxHealth`/`StateBits` 基础上扩展 6 个字段，覆盖 MMORPG 完整角色属性：

| 字段 | MemoryPackOrder | Orleans Id | 说明 |
| --- | --- | --- | --- |
| `Health` | 0 | 0 | 当前生命值（旧字段，保持不变） |
| `MaxHealth` | 1 | 1 | 最大生命值（旧字段，保持不变） |
| `StateBits` | 2 | 2 | 状态位掩码（Dead/Invincible/Stunned/Hidden/Frozen）（旧字段） |
| `Mana` | 3 | 3 | 当前法力值（**新增**） |
| `MaxMana` | 4 | 4 | 最大法力值（**新增**） |
| `Level` | 5 | 5 | 角色等级（**新增**） |
| `Exp` | 6 | 6 | 累计经验值（**新增**） |
| `Stamina` | 7 | 7 | 当前体力值（冲刺/闪避等消耗）（**新增**） |
| `MaxStamina` | 8 | 8 | 最大体力值（**新增**） |

> 旧字段 `Health`/`MaxHealth`/`StateBits` 的 `[MemoryPackOrder]`/`[Id]` 编号保持不变（0/1/2），新增字段从 3 起连续编号，确保向后兼容。MemoryPack 显式布局下旧客户端忽略未知字段，但 Orleans `[Id]` 编号需保持连续。

同步策略：
- **1Hz 心跳 + 变化触发**：Mana/Level/Exp/Stamina 等属性变化时立即下发，否则每秒强制下发一次完整属性保证一致性。
- 服务端 `ZoneShardGrain` 在快照生成时与上一 tick 比对属性字段，差异 ≥1（整数）即触发下发。
- 断线重连后 1s 内自愈（由 1Hz 心跳保证）。

### 12.5 服务端快照生成流程

`ZoneShardGrain.TickAsync()` 每 1/60 秒触发，按 `CharacterSyncConfig` 策略裁剪字段：

1. 遍历 `_simulatedEntities`，对每个角色实体按频率策略判定是否纳入本 tick 快照：
   - Transform：每 tick 必带（20Hz）。
   - MovementState：与上一 tick 比对，变化时纳入；否则每 100ms（6 tick）心跳一次。
   - AnimationState：仅 Montage 触发/结束事件时纳入。
   - EntityState 扩展字段：与上一 tick 比对，变化时纳入；否则每 1s（60 tick）心跳一次。
2. 组装 `EntityDelta`（含 `Transform`/`State`/`MovementState`/`AnimationState` 四个可选字段）。
3. 多个 `EntityDelta` 组装成 `SnapshotPacket` 广播。

### 12.6 客户端 ECS 应用流程

客户端 `SnapshotApplySystem`（NetworkReceive 组 / order 10）消费 `SnapshotReceiveBuffer`：

- **Spawn**：创建 Arch 实体，添加 `NetworkIdentityComponent` + `AuthTransformComponent`；远程实体额外加 `InterpolatedTransformComponent` + `MovementStateAuthComponent` + `AnimationStateAuthComponent` + `EntityStateAuthComponent`。
- **Update**：刷新对应组件；对远程实体的 `MovementStateAuthComponent` 同步刷新插值起点与目标，驱动动画状态机过渡。
- **AnimationState 事件**：通过 UnrealSharp 反射调用 UE5 侧 `AnimationMontage` 播放接口。
- **EntityState 属性**：刷新 `EntityStateAuthComponent`，触发 UI 层属性面板更新。

## 13. 场景对象同步流程（阶段 C 扩展）

阶段 C 引入场景对象（宝箱/开关/门/拉杆/传送门）的权威状态同步，与 `SnapshotPacket` 解耦走独立通道（`SceneObjectSyncPacket`），避免高频场景对象状态污染 baseline/delta 流。

### 13.1 协议层

`SceneObjectSyncPacket`（`SyncPacketKind.SceneObjectSync = 10`）承载场景对象状态权威快照：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `ObjectId` | ulong | 场景对象全局唯一 ID |
| `StateBits` | uint | 状态位掩码（Opened=0x01 / Activated=0x02 / Locked=0x04 / Reset=0x08） |
| `CooldownEndTick` | long | 冷却结束的服务器 tick（0 表示无冷却） |
| `OwnerCharacterId` | ulong | 当前归属角色 ID（0 表示无归属） |
| `HasTransform` | bool | 标记 TransformX/Y/Z 与 TransformPitch/Yaw/Roll 是否有效 |
| `TransformX/Y/Z` | float × 3 | 可选位置（仅可移动场景对象需要） |
| `TransformPitch/Yaw/Roll` | float × 3 | 可选旋转（弧度） |
| `ServerTick` | long | 本包对应的服务器 tick |

- `WorldChunkDiffPacket.PayloadType` 新增 `SceneObjectSync = 4`，可复用 `WorldChunkDiffPacket` 信封承载。
- `SyncPacket` 基类注册 `[MemoryPackUnion(9, typeof(SceneObjectSyncPacket))]`。

### 13.2 服务端权威处理

`ZoneShardGrain` 维护 `_sceneObjectStates` 字典（key: objectId, value: `SceneObjectStateData`）：

1. **首次交互创建默认状态**：未注册的对象首次交互时创建默认状态（ObjectType=Chest, StateBits=0）。
2. **`HandleSceneObjectInteract` 校验链**（顺序执行，任一失败返回 false）：
   - `interactorId` 非 0
   - `objectId` 非 0
   - 冷却校验：`state.CooldownEndTick > _tickCount` 时拒绝（默认冷却 300 tick ≈ 5 秒 @ 60Hz）
   - 归属校验：`OwnerCharacterId != 0 && OwnerCharacterId != interactorId` 时拒绝
   - 状态合法性：`intentBits & SceneObjectStateBits.StateMask` 不能为 0（无有效意图）
3. **更新状态**：写入新 StateBits、OwnerCharacterId、CooldownEndTick、UpdatedAt。
4. **构造 `SceneObjectSyncPacket`** 并调用 `BroadcastSceneObjectSyncAsync`。
5. **关键事件即时落盘**：宝箱开启（Opened）/ 任务门激活（Activated）等状态位变化时调用 `SaveSingleSceneObjectStateAsync`。

### 13.3 AOI 过滤广播

`BroadcastSceneObjectSyncAsync(packet, objectId)` 按场景对象位置过滤订阅者：

1. 查询 `_sceneObjectStates[objectId]` 的 Transform 数据。
2. 若有 Transform 数据（`TransformX/Y/Z` 非零），通过 `WorldCoord.ToChunkMortonKey(x, y, z)` 计算 chunk key，仅下发给该 chunk 的 AOI 订阅者。
3. 若无 Transform 数据（静态场景对象），回退广播到全部订阅者。
4. 经现有 `_fanoutObservers` 推送到 Gateway，由 `GatewaySyncDispatcher` 按 AOI 兴趣集查 `ISessionRegistry` 下发。

### 13.4 持久化层

`ISceneObjectPersistenceStore` 接口提供场景对象状态的持久化能力：

| 方法 | 用途 |
| --- | --- |
| `SaveWorldStateAsync(shardKey, states)` | 批量 upsert 场景对象状态（定时落盘） |
| `LoadWorldStateAsync(shardKey)` | 分片启动时加载全部状态 |
| `SaveSingleAsync(shardKey, state)` | 单对象即时落盘（关键事件） |

落盘策略：
- **定时落盘**：默认 30 秒一次，调用 `SaveWorldStateAsync` 批量 upsert 当前 shard 的全部场景对象状态。
- **即时落盘**：宝箱开启 / 任务门激活等关键事件触发 `SaveSingleAsync` 即时落盘，避免宕机丢失关键进度。
- **加载时机**：`ZoneShardGrain` 激活时调用 `LoadWorldStateAsync` 填充 `_sceneObjectStates` 字典。
- **实现**：`SqlServerSceneObjectPersistenceStore` 基于 ADO.NET 批量 upsert，建表脚本见 `scripts\sql\004_scene_object_state.sql`。
- **DI 注册**：在 `Horizon.Game.Gateway\Program.cs` 注册到 DI 容器，测试环境可不注入（`_sceneObjectPersistence` 为 null 时跳过持久化）。

### 13.5 客户端 ECS 应用流程

客户端 `SceneObjectApplySystem`（NetworkReceive 组）消费 `SyncPacketInbox.SceneObjectEvents` 队列：

- **Spawn**：首次收到 ObjectId 的包时创建 Arch Entity，添加 `SceneObjectStateAuthComponent` + 可选 `SceneObjectTransformComponent`。
- **Update**：后续包更新 `SceneObjectStateAuthComponent` 字段（StateBits/CooldownEndTick/OwnerCharacterId）。
- **Despawn**：StateBits 含 Reset(0x08) 时调用 `world.Destroy(entity)` + 字典清理。
- **UE5 桥接**：通过 `ISceneObjectNotifySink` 回调 UE5 侧通知场景对象状态变更，驱动宝箱开启动画 / 门开启 / 传送门特效等表现。

## 14. 带宽优化策略

MMORPG 工业标准阈值为 **每玩家 < 100kbps**。本系统通过三层策略协同达成该目标：增量编码、频率裁剪、带宽守门。

### 14.1 Snapshot 增量压缩

`ZoneShardGrain` 在快照生成时实现增量编码（Task D.3）：

1. **Baseline 维护**：`_lastSnapshot` 始终保存完整状态（所有实体的 EntityDelta），作为下次增量比对的 baseline。
2. **全量/增量判定**：
   - 首次 tick 或距上次全量快照超过 `FullSnapshotIntervalTicks`（60 tick = 1 秒）→ 强制全量（`BaselineTick=0`）。
   - 否则 → 增量（`BaselineTick = baseline.ServerTick`）。
3. **`BuildDeltaSnapshot`**：遍历 currentDeltas，与 baseline.Deltas 按 EntityId 比对，仅保留有变化的 EntityDelta。
4. **`EntityDeltaChanged` 阈值**：
   - Transform 位置/旋转：阈值 0.01f（避免浮点抖动频繁触发增量）。
   - State 属性（Health/Mana/Level 等）：整数差异 ≥1 即认为变化。
   - MovementState：移动模式/速度/落地标志任何变化都算。
   - AnimationState：事件驱动，任何字段变化都算。
5. **客户端重建**：`SnapshotApplySystem` 基于上一快照 + 增量 delta 重建完整状态；缺失 baseline 时请求重传。

> 增量压缩使平均 SnapshotPacket 体积从 ~240 字节（10 EntityDelta）降至 ~80 字节（仅变化字段），带宽降至约 1/3。

### 14.2 频率裁剪

`CharacterSyncConfig` 声明各同步类型的下发频率（见 §12.1）：

| 同步类型 | 频率 | 带宽贡献（理论估算） |
| --- | --- | --- |
| 位置（Transform） | 20Hz | 主要带宽来源 |
| 移动状态（MovementState） | 10Hz + 变化触发 | 中等 |
| 动画状态（AnimationState） | 事件驱动 | 极低（仅 Montage 触发） |
| 属性（EntityState 扩展） | 1Hz + 变化触发 | 极低 |

`ZoneShardGrain` 在快照生成时按策略裁剪字段：位置每 tick 必带，属性每秒一次，动画仅事件时携带。

### 14.3 带宽守门（GatewaySyncDispatcher.SessionBandwidthTracker）

`GatewaySyncDispatcher` 维护 per-session 带宽跟踪器（Task D.4），超阈值时限流：

| 配置项 | 默认值 | 说明 |
| --- | --- | --- |
| `BandwidthThresholdKbps` | 100.0 | 带宽阈值（kbps，MMORPG 工业标准） |
| `NormalSnapshotHz` | 20 | 正常快照频率 |
| `ThrottledSnapshotHz` | 10 | 限流快照频率（超阈值时降频） |
| `RecoverySeconds` | 3 | 连续 N 秒低于阈值后回升频率 |

工作机制：
1. **预估包字节数**：`EstimatePacketSizeBytes` 按包类型给出保守上界（如 SnapshotPacket = HeaderOverhead + 24 + deltaCount × 80）。
2. **1 秒滚动窗口计数**：`SessionBandwidthTracker.RecordBytes` 累计当前 1 秒窗口内的下发字节数。
3. **窗口滚动计算带宽**：`kbps = bytes * 8 / 1024 / seconds`。
4. **限流状态机**：
   - 超阈值 → 降到 `ThrottledSnapshotHz`（10Hz），告警一次（避免 spam）。
   - 连续 `RecoverySeconds`（3）秒低于阈值 → 回升到 `NormalSnapshotHz`（20Hz）。
5. **快照生成器查询**：`ZoneShardGrain` 通过 `GetSessionSnapshotHz(sessionId)` 查询当前频率，按 session 调整推送节奏。
6. **监控指标**：`GetBandwidthSnapshot()` 返回各 session 当前带宽（sessionId → kbps），用于监控面板 / Prometheus 导出。

## 15. 弱网降级策略

弱网环境（高延迟、丢包、抖动）下，系统通过三套机制协同保证体验：JitterBuffer 自适应、InputPacket 冗余重传、ReconnectResume 断线重连。

### 15.1 JitterBuffer 自适应（Task D.1）

`JitterBuffer`（`HundunWorld\Script\ManagedHundunWorld\Network\Sync\JitterBuffer.cs`）跟踪 RTT 历史样本，推算网络抖动量，为位置插值系统提供自适应延迟推荐值。

**RTT 采样与 EMA 平滑**：
- 滑动窗口保留最近 20 个 RTT 样本（`SampleWindowSize = 20`）。
- EMA 平滑系数 `EmaAlpha = 0.2`（新样本权重 20%）。
- EMA 公式：`emaRtt = (1-α) * emaRtt + α * rttMs`。
- 方差公式：`rttVariance = (1-α) * rttVariance + α * (rtt - emaRtt)^2`。

**自适应插值延迟窗口**（80-200ms）：
- 下限 `AdaptiveMinDelayMs = 80`（RTT 平稳时收敛到该值）。
- 上限 `AdaptiveMaxDelayMs = 200`（RTT 抖动大时不超过该值）。
- 公式：`Clamp(emaRtt * 1.5 + sqrt(rttVariance), 80, 200)`。

**`ComputeInterpolationDelayMs` 算法**：
- RTT 平稳（方差小）→ 收敛到 80ms，保证低延迟体验。
- RTT 抖动大（方差大）→ 不超过 200ms，保证平滑性。
- 尚未有 RTT 样本时返回 80ms（保守下限）。

**与原有 `ComputeRecommendedDelayMs` 的关系**：
- 原有算法：`Clamp(avgRtt/2 + 2.0 × σ, 30, 500)`（窗口标准差，范围 30-500ms）。
- 新算法（Task D.1）：基于 EMA 与方差，范围 80-200ms，更敏感于近期 RTT 变化。
- 两套算法并存，由调用方按场景选择。

### 15.2 InputPacket 冗余重传（Task D.2）

`InputSendSystem`（`Horizon.Game.ECS.Arch\Systems\InputSendSystem.cs`）维护未确认 input 环形缓冲，对抗 TCP 拥塞导致的批量延迟。

**客户端冗余重传**：
- **环形缓冲容量**：`PendingAcksCapacity = 64`（存储最近 64 个未确认 InputPacket 副本）。
- **触发阈值**：`RetransmitThreshold = 5`（当 `ClientTick - LastAckedClientTick > 5` 时触发）。
- **重传逻辑**：将环形缓冲中所有未确认 input 重新 enqueue 到 `InputSendQueue`，与当前 tick 的 input 一起发送。
- **ACK 推进**：网络层收到 `InputAckPacket` 后调用 `OnInputAck(lastProcessedClientTick)`，清理已确认的 input，推进 `_lastAckedClientTick`。
- **线程安全**：`_pendingLock` 保护环形缓冲与 `_lastAckedClientTick`，ECS 线程（Update）与网络 IO 线程（OnInputAck）并发访问。

**服务端去重**（`SyncPacketHandler.HandleInputAsync`）：
- **per-characterId 去重字典**：`_lastInputTickPerCharacter`（key=characterId, value=last seen ClientTick）。
- **去重逻辑**：若 `input.ClientTick <= lastAcceptedTick`，判定为重复/过期，直接返回最新 ack，不转发到 `ZoneShardGrain`（避免重复模拟）。
- **锁保护**：`_inputDedupLock` 保护字典，锁内仅做字典操作，锁外再 await grain 调用。
- **单例多连接安全**：`SyncPacketHandler` 为单例，多连接并发调用 `HandleInputAsync`，去重字典按 characterId 隔离。

### 15.3 ReconnectResume 断线重连（Task D.5）

`ReconnectResumePacket`（Kind=8）承载客户端断线前的版本向量，服务端据此决定恢复策略。

**三路 resume 边界**：
1. **`LastAppliedSnapshotTick`**：客户端最后已应用的 snapshot tick，服务端据此决定是否补发增量快照。
2. **`LastAppliedDiffSeq`**：客户端最后已应用的世界 diff 全局序号（跨 chunk 单调递增的 high-water mark），服务端据此决定是否补发世界 diff。
3. **`BaselineVersion` + `WorldPatchVersion`**：客户端本地世界版本（来自 .pak / GengDi），服务端据此决定是否需要启动器补丁。

**服务端恢复决策**（`PlayerSessionGrain.ResumeAsync` 返回 `ResumeDecision`）：

| 决策 | 触发条件 | 响应 |
| --- | --- | --- |
| `ResumeIncremental` | 版本向量兼容，仅落后少量 diff | `BuildIncrementalResume`：返回 `WorldPatchManifestPacket`，客户端继续走在线 diff |
| `RequireLauncherPatch` | baseline/worldPatch 版本不匹配 | `BuildLauncherPatchRequiredResponse`：要求客户端走启动器补丁 |
| `ResendFullChunks` | 落后 diff 过多，增量补流代价过高 | `BuildFullChunksResendResponse`：强制重发全部 chunk |
| `ForceReLogin` | 状态不可恢复 | `BuildForceReLoginResponse`：要求客户端重新走完整登录流程 |

**超时降级**：
- resume 失败（如服务端无法在合理时间内补齐 diff）→ 回退到全量快照重发。
- 客户端 5 秒内未收到 resume 响应 → 主动降级为全量快照请求（重新走 Handshake 流程）。
- 全量快照重发后客户端清空本地 diff 缓冲，从新 baseline 重新累计。