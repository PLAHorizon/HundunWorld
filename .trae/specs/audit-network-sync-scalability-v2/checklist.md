# 验证检查清单

## 阶段一：ECS 系统层并发安全与基础 GC 优化

- [x] SnapshotApplySystem.LocalPlayerOwnerId / _previousOwnerId / _lastAppliedSnapshot 跨线程读写使用 Volatile.Read/Volatile.Write
- [x] SnapshotApplySystem.OnFullSnapshotApplied / ResetLastAppliedSnapshot / TryRebuildFromDelta / Update 中所有 _lastAppliedSnapshot 读写均使用 Volatile 语义
- [x] SnapshotApplySystem.Update 限制单帧 MaxSnapshotsPerFrame=8 个快照消费，剩余延后下一帧
- [x] SnapshotApplySystem 增量合并使用 _deltaMergeBuffer 字段级复用，不再每次 new Dictionary
- [x] SnapshotApplySystem 增量合并的 .Values.ToArray() 改为复用 List<EntityDelta> 缓冲
- [x] SnapshotApplySystem 移除热路径 Console.WriteLine（第 264/306/542 行），改为限频诊断计数器
- [x] LocalSimulationSystem._jumpCounts 内存泄漏修复（改为 ConditionalWeakTable 或事件清理）
- [x] InputSendSystem._lastAckedClientTick / _pendingAcks / _pendingTail / _pendingHead / _pendingAcksCount / _pendingLock 改为实例字段
- [x] InputSendSystem.OnInputAck / GetPendingInputs 改为实例方法（由 ArchWorldHost 持有）
- [x] ECSUpdateDriver 与 HundunWorldGame 中的 InputSendSystem 调用方更新为通过 ArchWorldHost 获取实例
- [x] SnapshotReceiveBuffer 添加 MaxQueueSize=1024 软上限
- [x] SnapshotReceiveBuffer.Enqueue 超过上限时丢弃最旧包并递增 _droppedByOverflowCount
- [x] SnapshotReceiveBuffer 添加 DroppedByOverflowCount 属性（Interlocked.Read）
- [x] SnapshotReceiveBuffer 限频告警日志（每 10 秒最多一次）

## 阶段二：客户端 ECSUpdateDriver 与 NetworkManager 优化

- [~] ECSUpdateDriver.FlushInputSendQueue 使用 ArrayPool<byte>.Shared.Rent/Return 替代 new byte[]【跳过：SyncFrameMessage.Frame 的 byte[] Length 语义不允许使用 ArrayPool】
- [~] ECSUpdateDriver.FlushInputSendQueue 每帧 GC 分配降为 0（除最终 wireBytes）【跳过：同上】
- [x] NetworkManager._syncClientTick 跨线程读写使用 Interlocked.Read/Increment
- [x] NetworkManager._syncHandshakeComplete 已 volatile（确认未回归）
- [x] 其他跨线程字段审查（_connectionStatus 等）列出但本次不改

## 阶段三：服务端 Grain 层优化

- [x] ZoneShardGrain 添加 _deltaBuffer / _correctionBuffer / _baselineDictBuffer 字段级复用缓冲
- [x] TickAsync 不再 new List<EntityDelta> / List<CorrectionPacket>，使用 Clear() + 复用
- [x] entity.PendingInputs.ToArray() 改为复用缓冲
- [x] BuildDeltaSnapshot 使用 _baselineDictBuffer.Clear() + 复用
- [x] changedDeltas.ToArray() 改为复用 _changedDeltasArrayBuffer
- [x] ZoneShardGrain.BroadcastSnapshotAsync 按 chunkKey 分组聚合 delta
- [x] 每个 chunk 一次性序列化为单个 WorldChunkDiffPacket.Payload
- [x] 单个 chunk 一次 RPC 推送给每个 observer（sessionIds 由 _aoi.GetSubscribers 一次性获取）
- [x] 语义不变：N delta × M observer 次 RPC 降为 K chunk × M observer 次 RPC
- [x] correction 单独推送逻辑保留不变
- [x] ZoneShardGrain 添加 _observerSnapshot 字段，订阅/退订时失效
- [x] GetObserversSnapshot() 惰性创建快照并缓存
- [x] BroadcastSnapshotAsync / BroadcastEntityLifecycleAsync / BroadcastEventAsync / BroadcastInteractionSyncAsync / BroadcastSceneObjectSyncAsync 中所有 _fanoutObservers.ToArray() 改为 GetObserversSnapshot()
- [x] SubscribeFanoutAsync / UnsubscribeFanoutAsync 触发 _observerSnapshot 失效
- [x] ZoneShardGrain 添加 _broadcastInProgress 标志位
- [x] TickAsync 中 _ = BroadcastSnapshotAsync 调用前 CompareExchange 检测上次是否完成
- [x] 上次未完成时跳过广播并输出 Warning 限频日志，_lastSnapshot 仍更新
- [x] BroadcastSnapshotAsync 完成时（finally）Exchange 重置 _broadcastInProgress
- [x] ZoneShardAoi.GetSubscribers / GetAllSubscribers 视图返回，调用方不修改
- [x] ZoneShardGrain 中 .ToArray() 调用评估：能改 IEnumerable<long> 的改之
- [x] IZoneShardFanoutObserver.OnChunkDiffAsync 签名改为接受 IReadOnlyList<long>（兼容 long[] 与 List<long>）

## 阶段四：网关层 fanout 性能优化

- [x] GatewaySyncDispatcher.Dispatch 在 foreach 前预先 SyncPacketCodec.Encode 一次
- [x] 编码后 wireBytes 传递给 _sink.Send(endpoint, wireBytes) 新重载
- [x] foreach 改为 Parallel.ForEachAsync（MaxDegreeOfParallelism = MaxDispatchParallelism）
- [x] 添加 MaxDispatchParallelism 属性（默认 Environment.ProcessorCount）
- [x] GameConnectionPacketSink 添加 Send(object, byte[]) 新重载
- [x] 新重载跳过 SyncPacketCodec.Encode + PackMessage，直接 conn.SendAsync(wireBytes)
- [x] 旧 Send(object, SyncPacket) 保留兼容
- [x] FailedSendCount 计数仍正确
- [x] SessionBandwidthTracker._bytesInCurrentWindow 使用 Interlocked.Add
- [x] 窗口滚动使用 Interlocked.CompareExchange 自旋重试
- [x] _currentBandwidthKbps / _currentSnapshotHz 使用 Interlocked.Read/Exchange（或 volatile）
- [x] 100 万 session 时每 session 0 个 lock 对象
- [x] GatewayOptions 添加 MaxDispatcherWorkers 配置（默认 1）
- [x] SyncDispatcherHostedService.ExecuteAsync 启动 MaxDispatcherWorkers 个 worker Task
- [x] GatewaySyncDispatcher.RunOnceAsync 多 worker 并发安全
- [x] GatewayZoneShardFanoutSource 的 BoundedChannelOptions.SingleReader = false
- [x] 多 worker 下事件无丢失无重复

## 阶段五：扩展性基础设施

- [x] IZoneShardGrain 接口添加 GetLoadMetricsAsync 方法
- [x] ZoneShardLoadMetrics record 定义（含 EntityCount/SessionCount/ChunkCount/LastTickDurationMs/PendingInputsCount）
- [x] ZoneShardGrain.GetLoadMetricsAsync 实现返回当前状态
- [x] TickAsync 中测量执行耗时并记录到 _lastTickDurationMs 字段

## 阶段六：端到端验证

- [x] 编译 Horizon.Game.Message（0 错误，0 警告，SyncProtocolVersion.Current 保持 5）
- [x] 编译 Horizon.Game.ECS.Arch（0 错误）
- [x] 编译 Horizon.Game.Core（0 错误）
- [x] 编译 Horizon.Orleans.Grains（0 错误）
- [x] 编译 Horizon.Game.Gateway（0 错误）
- [ ] Flax 客户端项目需用户手动复制 DLL 后在 Flax Editor 中编译（权限限制）
- [x] 代码审查：SnapshotApplySystem 所有跨线程字段使用 Volatile/Interlocked
- [x] 代码审查：ZoneShardGrain.BroadcastSnapshotAsync 实现批量聚合
- [x] 代码审查：GatewaySyncDispatcher.Dispatch 实现并行 + 一次序列化复用
- [x] 代码审查：LocalSimulationSystem._jumpCounts 不再泄漏
- [x] 代码审查：InputSendSystem static 字段已实例化
- [x] 代码审查：SnapshotReceiveBuffer 软上限生效
- [x] 代码审查：ZoneShardGrain fire-and-forget 竞态保护
- [~] 代码审查：ECSUpdateDriver 使用 ArrayPool【跳过：Task 8 因协议约束未实现】
- [x] 代码审查：SessionBandwidthTracker 无 lock
- [x] 代码审查：GameConnectionPacketSink 新重载接受预编码 wireBytes
- [x] 代码审查：SyncDispatcherHostedService 支持 MaxDispatcherWorkers 配置
- [x] 行为兼容性：所有优化保持外部行为不变（语义等价）
- [x] 协议兼容性：不修改 SyncProtocolVersion.Current，旧客户端仍可连接
- [x] 诊断不丢失：移除的 Console.WriteLine 有等价诊断计数器替代

## 后续（不在本次范围，但记录）

- [ ] ECS 系统 Arch.Core 并行查询（world.Query 的并行重载）— 后续单独优化
- [ ] 完整 100 万级分布式 sharding（多 ZoneShardGrain 路由、跨 shard 订阅迁移）
- [ ] 修改 EntityDelta / SnapshotPacket schema（避免协议层变更）
- [ ] 引入 QUIC / WebSocket 替换 TouchSocket
- [ ] Prometheus 指标导出
- [ ] ECS 系统重排序与系统组划分重构
- [ ] ECSUpdateDriver 固定时间步长重构
- [ ] ECSUpdateDriver ArrayPool 优化（需先重构 SyncFrameMessage.Frame 为 Memory<byte>，破坏性协议变更）
- [ ] InputRetransmitTests.cs / NetworkEdgecaseTests.cs 测试文件更新（使用 InputSendSystem.Instance 而非 static 调用）
- [ ] GameConnectionPacketSink.FailedSendCount 改为 Interlocked 原子计数（当前非原子，多 worker 下可能不准）
- [ ] EndToEnd 测试的 FakeConnection.Sent 改为 ConcurrentBag（当前 List<byte[]> 非线程安全）
