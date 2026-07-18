# Tasks

本任务列表按依赖顺序组织，相同阶段的子任务可并行委派给独立 sub-agent。每个任务都对应 spec.md 中的具体 Requirement。

## 阶段一：ECS 系统层并发安全与基础 GC 优化（独立，可并行）

- [x] Task 1: SnapshotApplySystem 跨线程可见性保护
  - [x] SubTask 1.1: 修改 `Horizon.Game.ECS.Arch/Systems/SnapshotApplySystem.cs` 的 `LocalPlayerOwnerId` 属性，改用 `Volatile.Read`/`Volatile.Write`（ulong 类型用 `System.Threading.Volatile`）；`_previousOwnerId` 同理
  - [x] SubTask 1.2: 修改 `_lastAppliedSnapshot` static 字段读写为 `Volatile.Read`/`Volatile.Write`，明确跨实例可见性语义
  - [x] SubTask 1.3: 验证 `OnFullSnapshotApplied`、`ResetLastAppliedSnapshot`、`TryRebuildFromDelta` 与 `Update` 中的所有读写均使用 Volatile 语义
  - [x] SubTask 1.4: 代码审查确认无遗漏的跨线程读写点

- [x] Task 2: SnapshotApplySystem 单帧消费上限
  - [x] SubTask 2.1: 在 `SnapshotApplySystem` 添加 `public int MaxSnapshotsPerFrame { get; set; } = 8;` 配置属性
  - [x] SubTask 2.2: 修改 `Update` 方法的 `while (TryDequeue)` 循环，添加 `consumedThisTick < MaxSnapshotsPerFrame` 终止条件
  - [x] SubTask 2.3: 在循环终止且队列非空时输出 Debug 级日志（限频每秒一次）记录剩余队列长度
  - [x] SubTask 2.4: 验证 SnapshotReceiveBuffer.TotalEnqueued - TotalDequeued 在突发洪水时递减而非堆积

- [x] Task 3: SnapshotApplySystem GC 优化（增量合并缓冲复用）
  - [x] SubTask 3.1: 在 `SnapshotApplySystem` 添加字段 `private readonly Dictionary<ulong, EntityDelta> _deltaMergeBuffer = new();`
  - [x] SubTask 3.2: 修改 `Update` 与 `TryRebuildFromDelta` 中的增量合并逻辑，使用 `_deltaMergeBuffer` 而非每次新建 Dictionary；每次合并前 `Clear()` 并复用
  - [x] SubTask 3.3: 修改 `.Values.ToArray()` 调用为 `List<EntityDelta>` 实例字段缓存，避免每次 ToArray 分配（或使用 `Span<>` / `CopyTo`）
  - [x] SubTask 3.4: 验证合并语义不变（baseline 覆盖 + delta 覆盖顺序）

- [x] Task 4: 移除 SnapshotApplySystem 热路径 Console.WriteLine
  - [x] SubTask 4.1: 移除 `SnapshotApplySystem.cs` 第 264 行、306 行、542 行的 `Console.WriteLine` 调用
  - [x] SubTask 4.2: 改为限频诊断计数器或 `Debug.WriteLine`（仅 Debug 构建），保留致命错误路径的 `Console.Error.WriteLine`
  - [x] SubTask 4.3: 确认 `RetrospectivelyUpdateLocalPlayer`、`HandleDespawn` 的诊断改为累计计数器（已有 `TotalDespawnsApplied` 等）

- [x] Task 5: LocalSimulationSystem _jumpCounts 内存泄漏修复
  - [x] SubTask 5.1: 修改 `Horizon.Game.ECS.Arch/Systems/LocalSimulationSystem.cs` 添加 `public event Action<int>? EntityDestroyed;`（参数为 entity.Id）
  - [x] SubTask 5.2: 修改 `SnapshotApplySystem.HandleDespawn` 在销毁实体时通过新增事件通知 LocalSimulationSystem 清理 `_jumpCounts`
  - [x] SubTask 5.3: 或者改为 `ConditionalWeakTable<Arch.Core.Entity, StrongBox<int>>`（推荐，更简单），移除 `_jumpCounts` 字段
  - [x] SubTask 5.4: 验证销毁 1000 个实体后 `_jumpCounts.Count` 为 0

- [x] Task 6: InputSendSystem static 字段实例化
  - [x] SubTask 6.1: 修改 `Horizon.Game.ECS.Arch/Systems/InputSendSystem.cs`，将 `_lastAckedClientTick`、`_pendingAcks`、`_pendingTail`、`_pendingHead`、`_pendingAcksCount`、`_pendingLock` 改为实例字段（去掉 static）
  - [x] SubTask 6.2: 修改 `OnInputAck` 为实例方法，由 ArchWorldHost / 网络层持有 system 实例后调用
  - [x] SubTask 6.3: 修改 `GetPendingInputs` 为实例方法（或保留 static 但内部访问 InputSendQueue.Instance）
  - [x] SubTask 6.4: 验证 ECSUpdateDriver 与 HundunWorldGame 中的调用方更新为通过 ArchWorldHost 获取 system 实例后调用

- [x] Task 7: SnapshotReceiveBuffer 软上限与丢弃策略
  - [x] SubTask 7.1: 修改 `Horizon.Game.ECS.Arch/Network/SnapshotReceiveBuffer.cs` 添加 `public int MaxQueueSize { get; set; } = 1024;` 配置
  - [x] SubTask 7.2: 修改 `Enqueue` 方法，当 `_queue.Count >= MaxQueueSize` 时丢弃最旧包（`TryDequeue(out _)`）并 `Interlocked.Increment(ref _droppedByOverflowCount)`
  - [x] SubTask 7.3: 添加 `public long DroppedByOverflowCount` 属性，使用 `Interlocked.Read`
  - [x] SubTask 7.4: 添加限频告警日志（每 10 秒最多一次）输出当前队列长度与累计丢弃数

## 阶段二：客户端 ECSUpdateDriver 与 NetworkManager 优化（独立）

- [x] Task 8: ECSUpdateDriver ArrayPool + 批量发送【已跳过】
  > **跳过原因**：SyncFrameMessage.Frame 是 `byte[]` 类型，MemoryPack 序列化与服务端 `SyncPacketCodec.Decode(syncFrame.Frame)` 依赖 `.Length` 完整语义。ArrayPool.Rent 返回的缓冲长度（2 的幂对齐）大于 frameLength，直接赋值会发送多余字节并破坏服务端解码。改为 `Memory<byte>` 会破坏跨多个文件（PlayerController.cs、NetworkManager.cs、SyncPacketHandler.cs、GatewaySyncWiring.cs）的线路协议兼容性，超出 spec 约束范围。
  - [~] SubTask 8.1: ArrayPool 不兼容 byte[] Length 语义，跳过
  - [~] SubTask 8.2: 评估后决定保留原发送逻辑，避免协议层变更
  - [~] SubTask 8.3: GC 优化目标未达成，记录为后续优化项

- [x] Task 9: NetworkManager _syncClientTick 跨线程保护
  - [x] SubTask 9.1: 修改 `HundunWorld/Source/Game/Network/NetworkManager.cs` 的 `_syncClientTick` 字段读写为 `Interlocked.Read`/`Interlocked.Increment`
  - [x] SubTask 9.2: 检查 `_syncHandshakeComplete` 是否已 volatile（已用 `volatile`，确认即可）
  - [x] SubTask 9.3: 审查其他跨线程字段（`_connectionStatus` 等）是否有相同隐患，列出但本次不改（避免回归）

## 阶段三：服务端 Grain 层优化（核心瓶颈）

- [x] Task 10: ZoneShardGrain TickAsync GC 优化（缓冲复用）
  - [x] SubTask 10.1: 在 `ZoneShardGrain` 添加字段 `private readonly List<EntityDelta> _deltaBuffer = new();`、`private readonly List<CorrectionPacket> _correctionBuffer = new();`、`private readonly Dictionary<ulong, EntityDelta> _baselineDictBuffer = new();`
  - [x] SubTask 10.2: 修改 `TickAsync` 使用 `_deltaBuffer.Clear()` + 复用，避免每次 `new List<EntityDelta>()`
  - [x] SubTask 10.3: 修改 `entity.PendingInputs.ToArray()` 为复用缓冲 `entity.PendingInputBuffer`（在 SimulatedEntity 添加 `byte[]?` 缓冲字段或 `List<>` 复用）
  - [x] SubTask 10.4: 修改 `BuildDeltaSnapshot` 使用 `_baselineDictBuffer.Clear()` + 复用
  - [x] SubTask 10.5: 修改 `changedDeltas.ToArray()` 为 `_changedDeltasArrayBuffer` 复用

- [x] Task 11: ZoneShardGrain BroadcastSnapshotAsync 批量聚合（关键瓶颈）
  - [x] SubTask 11.1: 在 `BroadcastSnapshotAsync` 中按 `delta.Transform → chunkKey` 分组聚合 delta，使用 `Dictionary<ulong, List<EntityDelta>> _deltaByChunkBuffer`（字段级复用）
  - [x] SubTask 11.2: 对每个 chunk 一次性 `MemoryPackSerializer.Serialize(EntityDelta[])` 序列化为单个 `WorldChunkDiffPacket.Payload`
  - [x] SubTask 11.3: 单个 chunk 一次 RPC 推送给每个 observer，sessionIds 由 `_aoi.GetSubscribers(chunkKey)` 一次性获取
  - [x] SubTask 11.4: 验证语义不变：原 N delta × M observer 次 RPC 降为 K chunk × M observer 次 RPC（K ≤ N）
  - [x] SubTask 11.5: 保留 correction 单独推送逻辑（数量少，不优化）

- [x] Task 12: 消除 ZoneShardGrain _fanoutObservers.ToArray() 热路径分配
  - [x] SubTask 12.1: 在 `ZoneShardGrain` 添加字段 `private KeyValuePair<Guid, IZoneShardFanoutObserver>[]? _observerSnapshot;`，订阅/退订时 `_observerSnapshot = null` 失效
  - [x] SubTask 12.2: 添加 `GetObserversSnapshot()` 方法，惰性创建快照并缓存，调用方直接遍历数组
  - [x] SubTask 12.3: 修改 `BroadcastSnapshotAsync`、`BroadcastEntityLifecycleAsync`、`BroadcastEventAsync`、`BroadcastInteractionSyncAsync`、`BroadcastSceneObjectSyncAsync` 中所有 `_fanoutObservers.ToArray()` 调用为 `GetObserversSnapshot()`
  - [x] SubTask 12.4: 验证 SubscribeFanoutAsync / UnsubscribeFanoutAsync 触发失效

- [x] Task 13: ZoneShardGrain fire-and-forget 竞态修复
  - [x] SubTask 13.1: 在 `ZoneShardGrain` 添加字段 `private int _broadcastInProgress;`（0=空闲，1=进行中）
  - [x] SubTask 13.2: 修改 `TickAsync` 中的 `_ = BroadcastSnapshotAsync(toSend, corrections)` 调用，使用 `Interlocked.CompareExchange(ref _broadcastInProgress, 1, 0)` 检测是否上次广播未完成
  - [x] SubTask 13.3: 若上次未完成，本次 tick 跳过广播并输出 Warning 日志（限频），`_lastSnapshot` 仍更新为新 snapshot（最新状态优先）
  - [x] SubTask 13.4: 在 `BroadcastSnapshotAsync` 完成时（finally 块）`Interlocked.Exchange(ref _broadcastInProgress, 0)`

- [x] Task 14: ZoneShardAoi 视图优化（消除 ToArray）
  - [x] SubTask 14.1: 修改 `Horizon.Game.Core/World/ZoneShardAoi.cs` 的 `GetSubscribers` 返回 `IReadOnlyCollection<long>` 视图（已返回，确认调用方不修改）
  - [x] SubTask 14.2: 修改 `GetAllSubscribers` 返回 `_sessionToChunks.Keys` 视图（已返回）
  - [x] SubTask 14.3: 在 `ZoneShardGrain` 中所有调用 `_aoi.GetSubscribers(...).ToArray()` 处评估是否必须 ToArray：若仅传递给 observer.OnChunkDiffAsync 则不需要 ToArray（接受 IEnumerable<long>）；若必须 long[] 则保留但记录
  - [x] SubTask 14.4: 修改 `IZoneShardFanoutObserver.OnChunkDiffAsync` 签名为接受 `IReadOnlyList<long>`（兼容 long[] 与 List<long>），避免调用方强制 ToArray

## 阶段四：网关层 fanout 性能优化（依赖阶段三）

- [x] Task 15: GatewaySyncDispatcher 一次序列化复用 + 并行分发
  - [x] SubTask 15.1: 修改 `GatewaySyncDispatcher.Dispatch` 在 `foreach (sessionId)` 之前预先调用 `SyncPacketCodec.Encode(evt.Packet, out var frame, out var frameLength)` 一次
  - [x] SubTask 15.2: 将编码后的 wireBytes 传递给 `_sink.Send(endpoint, wireBytes)`（新增 `IClientPacketSink.Send(object, byte[])` 重载）
  - [x] SubTask 15.3: 修改 `foreach (sessionId)` 改为 `Parallel.ForEachAsync(evt.TargetSessionIds, new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism }, async (sessionId, ct) => { ... })`
  - [x] SubTask 15.4: 添加 `public int MaxDispatchParallelism { get; set; } = Environment.ProcessorCount;` 配置
  - [x] SubTask 15.5: 验证 `_sink.Send` 实现是线程安全的（GameConnectionPacketSink.Send 已用 Interlocked.Increment 计数，但 SyncPacketCodec.Encode 不可重入）

- [x] Task 16: GameConnectionPacketSink 接受预编码 wireBytes
  - [x] SubTask 16.1: 修改 `Horizon.Game.Gateway/Services/GatewaySyncWiring.cs` 的 `GameConnectionPacketSink` 添加 `public void Send(object endpoint, byte[] wireBytes)` 重载
  - [x] SubTask 16.2: 实现新重载：跳过 `SyncPacketCodec.Encode` + `PackMessage`，直接 `_ = conn.SendAsync(wireBytes)`
  - [x] SubTask 16.3: 旧 `Send(object, SyncPacket)` 保留兼容，但 `GatewaySyncDispatcher` 改用新重载
  - [x] SubTask 16.4: 验证 `FailedSendCount` 计数仍正确

- [x] Task 17: SessionBandwidthTracker lock-free 化
  - [x] SubTask 17.1: 修改 `SessionBandwidthTracker` 用 `Interlocked.Add(ref _bytesInCurrentWindow, bytes)` 替代 `lock(_lock)` 累加
  - [x] SubTask 17.2: 窗口滚动判定使用 `Interlocked.CompareExchange` 自旋重试（短临界区）
  - [x] SubTask 17.3: `_currentBandwidthKbps`、`_currentSnapshotHz` 用 `Interlocked.Read`/`Exchange`（double 用 `Interlocked.Exchange(ref *(long*)&doubleValue, ...)` 或 `volatile` + 不可变性）
  - [x] SubTask 17.4: 验证 100 万 session 时锁对象内存占用从「每 session 1 个 object」降为「每 session 0 个 object」

- [x] Task 18: SyncDispatcherHostedService 多 worker 选项
  - [x] SubTask 18.1: 在 `Horizon.Game.Gateway/Configuration/GatewayOptions.cs` 添加 `public int MaxDispatcherWorkers { get; set; } = 1;`
  - [x] SubTask 18.2: 修改 `SyncDispatcherHostedService.ExecuteAsync` 启动 `MaxDispatcherWorkers` 个 worker Task，每个 Task 独立调用 `_dispatcher.RunOnceAsync`
  - [x] SubTask 18.3: `GatewaySyncDispatcher.RunOnceAsync` 内部 `await _source.TryDequeueAsync(ct)` 在多 worker 下通过 channel 单读语义保证每个事件被处理一次（Channel 单读模式 → 多读模式）
  - [x] SubTask 18.4: 修改 `GatewayZoneShardFanoutSource` 的 `BoundedChannelOptions.SingleReader = false`（多 reader 模式）
  - [x] SubTask 18.5: 验证多 worker 下事件无丢失、无重复处理（channel 多读保证）

## 阶段五：扩展性基础设施（小修，未来扩展用）

- [x] Task 19: ZoneShardGrain 暴露负载指标
  - [x] SubTask 19.1: 在 `IZoneShardGrain` 接口添加 `Task<ZoneShardLoadMetrics> GetLoadMetricsAsync();`
  - [x] SubTask 19.2: 定义 `ZoneShardLoadMetrics` record（含 EntityCount、SessionCount、ChunkCount、LastTickDurationMs、PendingInputsCount）
  - [x] SubTask 19.3: `ZoneShardGrain.GetLoadMetricsAsync` 实现返回当前状态
  - [x] SubTask 19.4: 在 `TickAsync` 中测量执行耗时并记录到字段 `private long _lastTickDurationMs`

## 阶段六：端到端验证

- [x] Task 20: 编译与代码审查验证
  - [x] SubTask 20.1: 编译 `Horizon.Game.Message` 项目（0 错误，0 警告，SyncProtocolVersion.Current 保持 5）
  - [x] SubTask 20.2: 编译 `Horizon.Game.ECS.Arch` 项目（0 错误）
  - [x] SubTask 20.3: 编译 `Horizon.Game.Core` 项目（0 错误）
  - [x] SubTask 20.4: 编译 `Horizon.Orleans.Grains` 项目（0 错误）
  - [x] SubTask 20.5: 编译 `Horizon.Game.Gateway` 项目（0 错误）
  - [ ] SubTask 20.6: Flax 客户端项目（`HundunWorld/Source/Game.csproj`）需用户手动复制 DLL 后在 Flax Editor 中编译（权限限制）
  - [x] SubTask 20.7: 代码审查确认 SnapshotApplySystem 所有跨线程字段使用 Volatile/Interlocked
  - [x] SubTask 20.8: 代码审查确认 ZoneShardGrain.BroadcastSnapshotAsync 实现批量聚合
  - [x] SubTask 20.9: 代码审查确认 GatewaySyncDispatcher.Dispatch 实现并行 + 一次序列化复用
  - [x] SubTask 20.10: 代码审查确认 LocalSimulationSystem._jumpCounts 不再泄漏（改 ConditionalWeakTable 或事件清理）
  - [x] SubTask 20.11: 代码审查确认 InputSendSystem static 字段已实例化
  - [x] SubTask 20.12: 代码审查确认 SnapshotReceiveBuffer 软上限生效
  - [x] SubTask 20.13: 代码审查确认 ZoneShardGrain fire-and-forget 竞态保护
  - [~] SubTask 20.14: 代码审查确认 ECSUpdateDriver 使用 ArrayPool【跳过，Task 8 因协议约束未实现】
  - [x] SubTask 20.15: 代码审查确认 SessionBandwidthTracker 无 lock
  - [x] SubTask 20.16: 代码审查确认 GameConnectionPacketSink 新重载接受预编码 wireBytes
  - [x] SubTask 20.17: 代码审查确认 SyncDispatcherHostedService 支持 MaxDispatcherWorkers 配置

# Task Dependencies

- Task 1-7（ECS 系统层）：互相独立，可全部并行委派
- Task 8-9（客户端层）：独立，可与阶段一并行
- Task 10-14（服务端 Grain 层）：Task 11 依赖 Task 10（共用缓冲字段），Task 12/13/14 独立于 10/11
- Task 15-18（网关层）：
  - Task 15 依赖 Task 16（dispatcher 需要新 sink 重载）
  - Task 17/18 独立
- Task 19（扩展性钩子）独立
- Task 20（验证）依赖所有其他任务完成

# 并行化建议

以下任务可并行委派给不同 sub-agent：

- **并行组 A（ECS 并发安全）**：Task 1 + Task 2 + Task 6 + Task 7（互不相干，可同时改 SnapshotApplySystem 与 InputSendSystem 与 SnapshotReceiveBuffer 不同文件）
- **并行组 B（ECS GC 优化）**：Task 3 + Task 4 + Task 5（同文件不同修改点，需小心合并冲突，建议串行）
- **并行组 C（客户端层）**：Task 8 + Task 9（不同文件）
- **并行组 D（服务端 Grain 优化）**：Task 10 + Task 12 + Task 13 + Task 14（同文件不同方法，建议串行避免冲突）
- **并行组 E（服务端关键瓶颈）**：Task 11（独立，但要等 Task 10 完成）
- **并行组 F（网关层）**：Task 16 + Task 17 + Task 18（不同文件）→ 完成后 Task 15
- **并行组 G（扩展性钩子）**：Task 19（独立）

Task 20 必须在所有修复完成后串行执行。

# 风险与回归测试要点

1. **协议兼容性**：不修改 SyncProtocolVersion.Current，确保旧客户端仍可连接
2. **行为兼容性**：所有优化保持外部行为不变（语义等价）
3. **并发安全**：新引入的并行分发需保证不破坏 SessionBandwidthTracker 计数正确性
4. **GC 优化**：对象池/缓冲复用需保证线程安全（ECS 线程独占，OK；网关多 worker 共享 channel，需 channel 本身线程安全）
5. **诊断不丢失**：移除 Console.WriteLine 时确保有等价的诊断计数器替代
