# 网络同步能力再次审计与健壮性提升（迈向100万级玩家）Spec

## Why

上一轮 `audit-enter-game-network-sync` 与 `fix-move-sync-and-despawn` / `fix-network-sync-visibility` 已修复进入游戏与基础可见性问题，但在面向 100 万级玩家同服同步的扩展性目标下，当前网络同步链路在 ECS 系统、服务端 Grain、网关 fanout、客户端消费等关键路径仍存在严重瓶颈、并发安全隐患与 GC 压力问题。本次审计在系统读代码层面定位了约 20+ 处具体缺陷与扩展性限制，需通过本规范进行结构化修复，以显著降低单 tick CPU/GC 开销、消除并发竞态、为后续水平扩展（多 Shard、多 Gateway）奠定基础。

## What Changes

### A. ECS 系统层修复（客户端 + 共享层）

- **修复 SnapshotApplySystem 跨线程可见性隐患**：`LocalPlayerOwnerId`、`_previousOwnerId`、`_lastAppliedSnapshot`、`_entityIdToArchEntity` 等字段跨 ECS/网络线程访问未使用 `Volatile`/`Interlocked`，导致重连/握手切换线程时可能读到旧值（上一轮 spec 标称修复但代码实测未修复）
- **修复 SnapshotApplySystem 单帧消费无上限**：`while (TryDequeue)` 在服务器突发洪水时会一帧处理过多快照导致掉帧，需限制每帧最大处理包数（默认 8）并将剩余延后下一帧
- **优化 SnapshotApplySystem 增量合并 GC 热点**：`Dictionary<ulong, EntityDelta>` 每帧重建 + `.Values.ToArray()` 改为对象池/可复用缓冲，减少 GC 分配
- **移除热路径 Console.WriteLine**：SnapshotApplySystem 中 `Console.WriteLine` 改为限频诊断计数器（已有 `TotalSnapshotsApplied` 等），仅保留错误级别日志
- **修复 ECSUpdateDriver InputPacket 发送 GC**：每包 `new byte[frameLength]` + `Buffer.BlockCopy` 改为 `ArrayPool<byte>.Shared` 租用 + 批量合并发送（一帧内多包合并为单 TCP 帧）
- **修复 InputSendSystem static 字段跨实例污染**：`_lastAckedClientTick`、`_pendingAcks`、`_pendingLock` 等 static 字段在多 ArchHost 实例（编辑器+游戏）下共享，改为实例字段
- **修复 LocalSimulationSystem _jumpCounts 内存泄漏**：已销毁实体的 entry 不会被清理，长时间运行会无限增长，需在实体销毁时清理
- **优化 InterpolationSystem 查询缓存**：`world.TryGet<MovementStateAuthComponent>` 每帧每实体重复查询，改用联合查询 `WithAll<InterpolatedTransformComponent, MovementStateAuthComponent>`（可选）或保留但记录到诊断

### B. 服务端 Grain 层修复

- **优化 ZoneShardGrain TickAsync GC 热点**：每 tick `new List<EntityDelta>()` + `new List<CorrectionPacket>()` + `entity.PendingInputs.ToArray()` 改为复用缓冲（字段级 `List<>` + `Clear()`）
- **优化 ZoneShardGrain BroadcastSnapshotAsync 关键瓶颈**：当前实现为「每个 delta 独立序列化 + 独立 RPC 推送」，N delta × M observer 次序列化与 await。改为：按 chunkKey 分组聚合 delta → 一次性序列化为单个 `WorldChunkDiffPacket.Payload` → 单次推送包含多 delta
- **消除 `_fanoutObservers.ToArray()` 热路径分配**：每次广播都复制 observer 列表，改为不可变快照引用（仅在订阅/退订时替换）
- **修复 fire-and-forget 竞态**：`_ = BroadcastSnapshotAsync(toSend, corrections)` 后立刻 `_lastSnapshot = snapshot`，下次 tick 可能与上次广播重叠。需通过 `Interlocked` 标志或 Task 链化保证串行
- **优化 BuildDeltaSnapshot GC**：`Dictionary<ulong, EntityDelta> baselineDict` 每次新建，改为字段级复用 + `Clear()`
- **优化 ZoneShardAoi FanOut 与 GetAllSubscribers GC**：返回 `IReadOnlyCollection<>` 视图避免 ToArray，调用方按需迭代
- **修复 SimulatedEntity struct 拷贝语义陷阱**：`_simulatedEntities[entityId] = entity` 在 struct 修改后整体回写，但 `entity.PendingInputs` 是 `List<InputPacket>` 引用类型，多次结构体拷贝会共享同一 List（实际行为正确但语义混淆），需明确注释或改为 class

### C. 网关层 fanout 性能修复

- **优化 GatewaySyncDispatcher 串行发送瓶颈**：当前 `foreach (sessionId in evt.TargetSessionIds) { _sink.Send(endpoint, evt.Packet); }` 串行处理 100 万 session，改为并行分批（Partitioner / Parallel.ForEachAsync）+ 每包一次序列化复用（不要每 session 重编码）
- **修复 GameConnectionPacketSink 每包每 session 重复序列化**：当前 `Send(endpoint, packet)` 内部每次都 `SyncPacketCodec.Encode` + `new byte[]`，改为：dispatcher 层先一次性编码 wireBytes，sink 层只做 `conn.SendAsync(wireBytes)`
- **优化 SessionBandwidthTracker 锁竞争**：每个 session 一把 lock，100 万 session 时锁对象本身就吃内存。改为 `Interlocked` 操作 + 单 long 计数器（窗口内字节数）
- **修复 SyncDispatcherHostedService 单线程瓶颈**：单 dispatcher 单线程串行 `RunOnceAsync`，改为多 worker 并行消费同一 channel（`Channel<T>` 本身支持单读多写或多读单写）

### D. 协议层小修

- **保持协议兼容性**：本次不修改 SyncProtocolVersion.Current（保持 v5），仅做实现层优化
- **修复 EntityDelta 序列化冗余**：`Nullable<>` 字段在 MemoryPack 下仍有额外开销，但本次不修改 schema，仅注释说明

### E. 客户端层小修

- **修复 NetworkManager `_syncClientTick` 跨线程读写**：使用 `Interlocked.Read`/`Interlocked.Increment`
- **修复 SnapshotReceiveBuffer 无界队列**：增加软上限（默认 1024），溢出时丢弃最旧包并记录告警，防止 OOM
- **优化 InputSendSystem.GetPendingInputs GC**：`new List<InputPacket>` 改为复用列表或直接返回 IEnumerable 避免 ToList

### F. 扩展性基础设施（不直接实现 100 万级，但打基础）

- **在 ZoneShardGrain 暴露 sharding 钩子**：增加 `GetLoadMetricsAsync()` 返回当前 entity/session count，供未来路由器做负载均衡决策
- **在 SyncDispatcherHostedService 暴露多 dispatcher 选项**：通过 `GatewayOptions.MaxDispatcherWorkers` 配置（默认 1，可调高）
- **在协议层预留 batched snapshot 包**：本次不实现，但在 spec.md 标注后续可扩展 `SnapshotBatchPacket`

## Impact

### Affected specs

- `audit-enter-game-network-sync`：本次不改其修复内容，但 _lastAppliedSnapshot 的并发保护会影响其重连重置流程
- `fix-move-sync-and-despawn`：Despawn 全量广播路径在本次优化后保持语义不变
- `fix-network-sync-visibility`：fanout 链路优化不改变外部行为

### Affected code

- `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs`（并发保护 + 单帧消费上限 + GC 优化）
- `Horizon.Game.ECS.Arch/Systems/InputSendSystem.cs`（实例化字段 + GC 优化）
- `Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs`（_jumpCounts 清理）
- `Horizon.Game.ECS.Arch/Systems/InterpolationSystem.cs`（查询优化）
- `Horizon.Game.ECS.Arch/Network/SnapshotReceiveBuffer.cs`（软上限 + 丢弃策略）
- `HundunWorld/Source/Game/ECSUpdateDriver.cs`（ArrayPool + 批量发送）
- `Horizon.Orleans.Grains/World/ZoneShardGrain.cs`（GC 优化 + 批量序列化 + 竞态修复）
- `Horizon.Game.Core/World/ZoneShardAoi.cs`（FanOut 视图优化）
- `Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs`（并行 + 一次序列化复用）
- `Horizon.Game.Core/Sim/Server/GatewaySyncDispatcher.cs`（SessionBandwidthTracker lock-free）
- `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs`（GameConnectionPacketSink 接受预编码 bytes）
- `Horizon.Game.Gateway/Configuration/GatewayOptions.cs`（新增 MaxDispatcherWorkers）
- `HundunWorld/Source/Game/Network/NetworkManager.cs`（_syncClientTick Interlocked）

## ADDED Requirements

### Requirement: ECS 系统跨线程可见性保护

The system SHALL 为 `SnapshotApplySystem.LocalPlayerOwnerId`、`_previousOwnerId` 提供跨线程可见性保护，使用 `Volatile.Read`/`Volatile.Write`（ulong 类型）或 `Interlocked`（long 类型）语义，避免 ECS 线程与网络线程之间的旧值读取。

#### Scenario: 重连握手后 ECS 线程读到最新 LocalPlayerOwnerId

- **WHEN** 网络线程在握手回调中调用 `SnapshotApplySystem.LocalPlayerOwnerId = newValue`
- **THEN** ECS 线程在下一帧 Update 中通过 Volatile.Read 读到 newValue，不会因 CPU 缓存读到旧值 0
- **AND** RetrospectivelyUpdateLocalPlayer 不会因旧值跳过本地玩家标记

### Requirement: SnapshotApplySystem 单帧消费上限

The system SHALL 限制 `SnapshotApplySystem.Update` 单帧最多消费 `MaxSnapshotsPerFrame`（默认 8）个快照包，剩余包延后下一帧处理，避免突发洪水时单帧卡顿。

#### Scenario: 服务器突发 50 个快照包

- **WHEN** 网络线程一帧内入队 50 个 SnapshotPacket
- **THEN** SnapshotApplySystem 单帧只消费 8 个，剩余 42 个保留在 SnapshotReceiveBuffer 中
- **AND** 下一帧继续消费 8 个，约 7 帧处理完
- **AND** 每帧 Update 耗时保持稳定，不会因 50 个快照同时处理导致掉帧

### Requirement: ZoneShardGrain 批量序列化与推送

The system SHALL 在 `BroadcastSnapshotAsync` 中按 chunkKey 分组聚合多个 EntityDelta，每个 chunk 一次性序列化为单个 `WorldChunkDiffPacket.Payload`，单次推送包含多 delta，将 N delta × M observer 次 RPC 降为「按 chunk 分组的 K 次 RPC」（K ≤ chunk 数）。

#### Scenario: 100 实体分布在 5 个 chunk

- **WHEN** TickAsync 产生 100 个 EntityDelta，分布在 5 个 chunk（每个 20 delta）
- **THEN** BroadcastSnapshotAsync 按 chunk 分组聚合为 5 个 WorldChunkDiffPacket
- **AND** 每个 chunk 一次序列化 + 一次 RPC 推送给每个 observer
- **AND** 总 RPC 次数从 100 × M 降为 5 × M

### Requirement: GatewaySyncDispatcher 并行分发

The system SHALL 在 `Dispatch` 中对 `evt.TargetSessionIds` 进行并行分批处理，使用 `Parallel.ForEachAsync` 或 `Partitioner`，默认 `MaxDegreeOfParallelism = Environment.ProcessorCount`，可通过 `GatewayOptions.MaxDispatchParallelism` 配置。

#### Scenario: 单包目标 100 万 session

- **WHEN** 一个 fanout 事件目标 sessionIds 长度为 100 万
- **THEN** dispatcher 按 8 路并行（默认）分批发送
- **AND** 单包只编码一次 wireBytes，所有 session 共用
- **AND** 总耗时约为串行的 1/8

### Requirement: SnapshotReceiveBuffer 软上限与丢弃策略

The system SHALL 为 `SnapshotReceiveBuffer` 设置软上限 `MaxQueueSize`（默认 1024），超过上限时丢弃最旧包并递增 `DroppedByOverflowCount`，每 10 秒最多输出一次告警日志。

#### Scenario: 服务器洪水时客户端处理不及

- **WHEN** 队列长度超过 1024
- **THEN** 新入队的包挤掉最旧包（或拒绝入队，视实现）
- **AND** `DroppedByOverflowCount` 递增
- **AND** 日志输出限频告警，包含当前队列长度与累计丢弃数

### Requirement: ECSUpdateDriver 批量合并发送

The system SHALL 在 `FlushInputSendQueue` 中合并本帧所有 InputPacket 为单个 TCP 帧发送（或单批次顺序发送），使用 `ArrayPool<byte>.Shared` 租用编码缓冲，避免每包 `new byte[]` + `Buffer.BlockCopy` 的 GC 压力。

#### Scenario: 一帧产生 5 个 InputPacket

- **WHEN** FlushInputSendQueue 取出 5 个 InputPacket
- **THEN** 通过 ArrayPool 租用 5 次编码缓冲（每次用完归还）
- **AND** 5 个 SyncFrameMessage 顺序发送，但不创建新 byte[]（除最终 wireBytes）
- **AND** 累计 GC 分配从 5 个 byte[] 降为 0（除网络层 wireBytes）

### Requirement: ZoneShardGrain fire-and-forget 竞态修复

The system SHALL 保证 `TickAsync` 触发的 `BroadcastSnapshotAsync` 与下一次 TickAsync 不会并发执行，通过 `Interlocked.CompareExchange` 标志位或 `Task.ContinueWith` 链化保证串行。

#### Scenario: 上次广播未完成时下一次 tick 触发

- **WHEN** 上一次 BroadcastSnapshotAsync 仍在 await observer.OnChunkDiffAsync
- **THEN** 本次 TickAsync 跳过广播（或排队等待）
- **AND** `_lastSnapshot` 只在广播完成后更新
- **AND** 不会出现「新 snapshot 用旧 baseline 比对」的竞态

## MODIFIED Requirements

### Requirement: LocalSimulationSystem _jumpCounts 清理

`LocalSimulationSystem._jumpCounts` 字典会在实体销毁后保留 entry，造成内存泄漏。修改为：在 `SnapshotApplySystem.HandleDespawn` 销毁实体时通过事件通知 `LocalSimulationSystem` 清理对应 `entity.Id` 的 entry；或改为 `ConditionalWeakTable<Entity, int>` 自动随实体回收。

### Requirement: InputSendSystem 字段实例化

`InputSendSystem` 的 `_lastAckedClientTick`、`_pendingAcks`、`_pendingTail`、`_pendingHead`、`_pendingAcksCount`、`_pendingLock` 改为实例字段（去掉 static），由 ArchWorldHost 创建实例时初始化，避免多 ArchHost 共享污染。

### Requirement: GameConnectionPacketSink 接受预编码 wireBytes

新增 `IClientPacketSink.Send(object endpoint, byte[] wireBytes)` 重载（或修改现有签名），由 `GatewaySyncDispatcher.Dispatch` 在并行分批前一次性编码 wireBytes，所有 session 复用同一份字节数组。

## REMOVED Requirements

### Requirement: 移除 SnapshotApplySystem 热路径 Console.WriteLine

**Reason**: `Console.WriteLine` 是同步阻塞 IO，在 60Hz tick 上每帧调用会引入数毫秒延迟，且当前诊断计数器（`TotalSnapshotsApplied` 等）已足够。

**Migration**: 仅保留 `Console.Error.WriteLine` 用于致命错误；其余改为限频日志或诊断计数器。

## 非目标（明确不在本次范围）

- 不实现完整的 100 万级玩家分布式 sharding（多 ZoneShardGrain 路由、跨 shard 订阅迁移）
- 不修改 SyncProtocolVersion.Current（保持 v5，避免客户端兼容性破坏）
- 不修改 EntityDelta / SnapshotPacket schema（避免协议层变更）
- 不引入 QUIC / WebSocket 替换 TouchSocket
- 不实现 ECS 系统 Arch.Core 并行查询（`world.Query` 的并行重载）—— 后续单独优化
- 不引入 Prometheus 指标导出（已有诊断计数器，后续接入）
- 不做 ECS 系统重排序与系统组划分重构
- 不做 ECSUpdateDriver 的固定时间步长重构（保持与 Flax 主线程 deltaTime 一致）
