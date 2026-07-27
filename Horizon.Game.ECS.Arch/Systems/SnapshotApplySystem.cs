using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.ECS.Arch.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// 快照应用系统：在 NetworkReceive 阶段消费 <see cref="SnapshotReceiveBuffer"/>，
/// 将服务器下发的快照增量（Spawn / Update / Despawn）写入 Arch 世界。
/// </summary>
/// <remarks>
/// 系统维护一个 EntityId → <see cref="Entity"/> 的字典映射，用于快速查找远程实体。
/// 对于本地玩家实体（通过 <see cref="LocalPlayerOwnerId"/> 匹配），跳过变换更新，
/// 以保证本地预测优先权。
/// </remarks>
[ArchSystem(SystemGroup.NetworkReceive, order: 10)]
public sealed class SnapshotApplySystem : ArchSystemBase
{
    /// <summary>EntityId 到 Arch Entity 的映射表。</summary>
    private readonly Dictionary<ulong, Entity> _entityIdToArchEntity = new();

    /// <summary>上一帧的 LocalPlayerOwnerId，用于检测变更。跨线程读写使用 Volatile 保证可见性。</summary>
    private ulong _previousOwnerId = 0;

    // Task D.3.3：客户端最近应用的全量快照（用于增量快照重建）。
    // 当收到 BaselineTick != 0 的增量快照时，基于本字段重建完整状态。
    // 由网络层在应用全量快照后调用 OnFullSnapshotApplied 更新。
    // 跨线程读写使用 Volatile 保证可见性。
    private static SnapshotPacket? _lastAppliedSnapshot;

    // Task 1：LocalPlayerOwnerId 的 backing 字段，跨线程读写使用 Volatile 保证可见性。
    private ulong _localPlayerOwnerId;

    /// <summary>实体 Spawn 事件参数。</summary>
    public readonly record struct EntitySpawnedEventArgs(ulong EntityId, Entity ArchEntity, bool IsLocalPlayer, float X, float Y, float Z);

    /// <summary>实体 Despawn 事件参数。</summary>
    public readonly record struct EntityDespawnedEventArgs(ulong EntityId);

    /// <summary>
    /// Task B.6.3：动画状态变更事件参数。
    /// 当服务器下发 <see cref="AnimationStateAuthComponent"/>（Montage 触发/结束）时触发，
    /// 供 UE5 侧订阅以调用 AnimationMontage 播放/停止。
    /// </summary>
    public readonly record struct AnimationStateChangedEventArgs(ulong EntityId, uint AnimMontageId, uint AnimInstanceId, float PlayRate, bool IsLooping, long ServerTick);

    /// <summary>实体 Spawn 通知（供 FlaxActorSyncSystem 等外部系统订阅）。</summary>
    public event Action<EntitySpawnedEventArgs>? EntitySpawned;

    /// <summary>实体 Despawn 通知（供 FlaxActorSyncSystem 等外部系统订阅）。</summary>
    public event Action<EntityDespawnedEventArgs>? EntityDespawned;

    /// <summary>
    /// Task B.6.3：动画状态变更通知（供 UE5 侧订阅以播放/停止 Montage）。
    /// 当 delta 携带 <see cref="AnimationStateAuthComponent"/> 时触发。
    /// </summary>
    public event Action<AnimationStateChangedEventArgs>? AnimationStateChanged;

    /// <summary>本地玩家归属 ID（0 表示未设置）。跨线程读写使用 Volatile 保证可见性。</summary>
    public ulong LocalPlayerOwnerId
    {
        get => Volatile.Read(ref _localPlayerOwnerId);
        set => Volatile.Write(ref _localPlayerOwnerId, value);
    }

    // 诊断统计：供 ArchEcsRuntime 读取并通过 UE5 日志系统输出。
    // Horizon.Game.ECS.Arch 项目不能直接引用 UnrealSharp 的 LogWorldSyncActor，
    // 因此本系统只暴露统计属性，由客户端项目的 ArchEcsRuntime 负责日志输出。
    private long _totalSnapshotsApplied;
    private long _totalDeltasApplied;
    private long _totalSpawnsApplied;
    private long _totalUpdatesApplied;
    private long _totalDespawnsApplied;
    private int _lastTickConsumed;
    private int _lastTickDeltas;
    private int _lastTickSpawns;
    private int _lastTickUpdates;
    private int _lastTickDespawns;
    private int _updateCallCount;

    // Task 4：本地玩家保护次数计数器（替代热路径 Console.WriteLine）。
    private long _localPlayerProtectionCount;

    // 诊断：HandleUpdate 调用计数，用于限频日志（前 5 次无条件输出，后续每 120 次输出一次）
    private long _handleUpdateCount;
    // 诊断：HandleUpdate 中实体不在字典（走 Spawn 回退）的次数
    private long _handleUpdateSpawnFallbackCount;
    // 诊断：HandleUpdate 中实体为远程玩家且有 InterpolatedTransformComponent 的次数
    private long _handleUpdateRemoteInterpCount;

    // Task 2：单帧消费上限溢出日志限频时间戳（Stopwatch.GetTimestamp() 单位）。
    private long _lastOverflowLogTime;

    // 孤儿实体清理：每 300 帧（约 5 秒 @60fps）扫描一次映射字典，
    // 移除 Arch World 中已不存活的实体引用，防止内存泄漏和字典膨胀。
    private int _orphanCleanupCounter;
    private const int OrphanCleanupInterval = 300;
    private readonly List<ulong> _orphanKeysToRemove = new();
    /// <summary>累计清理的孤儿实体映射数（诊断用）。</summary>
    public long OrphanMappingsCleaned { get; private set; }

    // ─── 远程实体超时清理（客户端兜底机制）───
    // 问题：服务端检测异常断线（进程崩溃/断网）需 19~65 秒（TCP KeepAlive 19s 或心跳超时 60s+5s），
    // 期间本地角色看到远程角色"卡住不动"，体验差。
    // 修复：客户端每秒扫描远程实体，若 TimeSinceLastSnapshot 超过阈值则主动清理。
    // 服务端全量快照每 1 秒发送一次（FullSnapshotIntervalTicks=60 @60Hz），
    // 正常在线角色的 TimeSinceLastSnapshot 不会超过 1~2 秒，10 秒阈值足够安全。
    // 误判风险极低：即使误清理，后续收到 Spawn delta 会重新创建实体。
    private int _staleEntityCleanupCounter;
    private const int StaleEntityCleanupInterval = 60; // 每 60 帧（约 1 秒 @60fps）扫描一次
    private readonly List<ulong> _staleEntityIds = new();
    /// <summary>
    /// 远程实体超时阈值（秒）。超过此时间未收到任何快照更新的远程实体将被客户端主动清理。
    /// 默认 60 秒：与服务端 IdleTimeoutSeconds（60s）匹配，确保只有在连接真正断开时才清理远程角色。
    /// 服务端全量快照间隔 1 秒，60 秒内至少应有 50+ 次全量快照到达。
    /// 设置为 60 秒以容忍网络抖动、服务端 tick 异常、fanout observer 重订阅、网关重连等场景，
    /// 避免误清理在线角色导致"莫名离线"。
    /// 修复（莫名离线）：原值 30 秒在 fanout observer 重订阅或网络分区场景下不够安全，
    /// 增大到 60 秒与连接空闲超时一致，确保只有在连接真正断开时才清理。
    /// </summary>
    public float StaleEntityTimeoutSeconds { get; set; } = 60f;
    /// <summary>累计因超时被清理的远程实体数（诊断用）。</summary>
    public long StaleEntitiesCleaned { get; private set; }

    // Task 3：增量合并缓冲复用（实例方法 Update 使用），避免每帧分配 Dictionary/数组。
    private readonly Dictionary<ulong, EntityDelta> _deltaMergeBuffer = new();
    private readonly List<EntityDelta> _deltaMergeList = new();

    // Task 3：增量合并缓冲复用（静态方法 TryRebuildFromDelta 使用）。
    private static readonly Dictionary<ulong, EntityDelta> _deltaMergeBufferStatic = new();
    private static readonly List<EntityDelta> _deltaMergeListStatic = new();

    /// <summary>累计已消费的快照包总数（从不重置）。</summary>
    public long TotalSnapshotsApplied => _totalSnapshotsApplied;
    /// <summary>累计已应用的 EntityDelta 总数（从不重置）。</summary>
    public long TotalDeltasApplied => _totalDeltasApplied;
    /// <summary>累计已应用的 Spawn delta 总数（从不重置）。</summary>
    public long TotalSpawnsApplied => _totalSpawnsApplied;
    /// <summary>累计已应用的 Update delta 总数（从不重置）。</summary>
    public long TotalUpdatesApplied => _totalUpdatesApplied;
    /// <summary>累计已应用的 Despawn delta 总数（从不重置）。</summary>
    public long TotalDespawnsApplied => _totalDespawnsApplied;
    /// <summary>上一帧消费的快照包数量。</summary>
    public int LastTickConsumed => _lastTickConsumed;
    /// <summary>上一帧应用的 EntityDelta 数量。</summary>
    public int LastTickDeltas => _lastTickDeltas;
    /// <summary>上一帧应用的 Spawn delta 数量。</summary>
    public int LastTickSpawns => _lastTickSpawns;
    /// <summary>上一帧应用的 Update delta 数量。</summary>
    public int LastTickUpdates => _lastTickUpdates;
    /// <summary>上一帧应用的 Despawn delta 数量。</summary>
    public int LastTickDespawns => _lastTickDespawns;
    /// <summary>Update 方法被调用的总次数。</summary>
    public int UpdateCallCount => _updateCallCount;

    /// <summary>Task 2：单帧最多消费的快照包数量，超出部分留待下一帧处理。默认 8。</summary>
    public int MaxSnapshotsPerFrame { get; set; } = 8;

    /// <summary>[Phase C6] 累计单帧快照消费溢出次数（供游戏层转发到 ClientSyncMetrics）。</summary>
    public long OverflowCount { get; private set; }

    /// <summary>Task 4：本地玩家保护（跳过 Despawn）累计次数。</summary>
    public long LocalPlayerProtectionCount => Interlocked.Read(ref _localPlayerProtectionCount);

    // ─── [Phase C4] 自适应插值延迟 ───

    /// <summary>是否启用自适应插值延迟（默认关闭，待充分测试后开启）。</summary>
    public static bool UseAdaptiveDelay { get; set; } = true;

    /// <summary>固定插值延迟（秒），当 UseAdaptiveDelay=false 时使用。默认 100ms。</summary>
    public static float FixedInterpolationDelaySeconds { get; set; } = 0.1f;

    private static float _adaptiveAvgInterval;
    private static float _adaptiveJitter;
    private static long _adaptiveLastArrivalTimestamp;
    private static readonly object _adaptiveLock = new();

    /// <summary>
    /// [Phase C4] 计算当前自适应插值延迟（秒）。
    /// 公式：targetDelay = avgInterval + 2 * jitter，clamp 到 [100ms, 300ms]。
    /// 修复（远程角色闪移）：原实现 clamp 到 [50ms, 200ms]，最小 50ms 导致 Alpha 在 3 帧内到达 1，
    /// 之后 dead reckoning 用过时速度推进位置，新快照到达时位置突变表现为闪移。
    /// 增大最小延迟到 100ms（6 帧完成插值），让插值有足够时间平滑过渡，减少 dead reckoning 的影响。
    /// </summary>
    public static float AdaptiveInterpolationDelaySeconds
    {
        get
        {
            if (!UseAdaptiveDelay)
                return FixedInterpolationDelaySeconds;

            float avg, jitter;
            lock (_adaptiveLock)
            {
                avg = _adaptiveAvgInterval;
                jitter = _adaptiveJitter;
            }

            if (avg <= 0f)
                return FixedInterpolationDelaySeconds;

            var target = avg + 2f * jitter;
            return Math.Clamp(target, 0.1f, 0.3f);
        }
    }

    /// <summary>[Phase C4] 记录快照到达时间，更新自适应延迟统计。</summary>
    public static void RecordSnapshotArrival()
    {
        var now = Stopwatch.GetTimestamp();
        lock (_adaptiveLock)
        {
            if (_adaptiveLastArrivalTimestamp > 0)
            {
                var intervalSec = (float)((now - _adaptiveLastArrivalTimestamp) / (double)Stopwatch.Frequency);
                _adaptiveAvgInterval = _adaptiveAvgInterval == 0f
                    ? intervalSec
                    : _adaptiveAvgInterval + 0.2f * (intervalSec - _adaptiveAvgInterval);
                var deviation = Math.Abs(intervalSec - _adaptiveAvgInterval);
                _adaptiveJitter = _adaptiveJitter == 0f
                    ? deviation
                    : _adaptiveJitter + 0.25f * (deviation - _adaptiveJitter);
            }
            _adaptiveLastArrivalTimestamp = now;
        }
    }

    /// <summary>
    /// Task D.3.3：供网络层调用，记录最近应用的全量快照。
    /// <para>
    /// 当服务端下发 BaselineTick=0 的全量快照并成功应用后，网络层应调用本方法更新缓存，
    /// 供后续 BaselineTick!=0 的增量快照重建使用。
    /// </para>
    /// </summary>
    /// <param name="snapshot">已应用的全量快照（BaselineTick 应为 0）。</param>
    public static void OnFullSnapshotApplied(SnapshotPacket snapshot)
    {
        Volatile.Write(ref _lastAppliedSnapshot, snapshot);
    }

    /// <summary>
    /// Task D.3.3：尝试从增量快照重建完整状态（不涉及 Arch World，供测试与网络层校验使用）。
    /// <para>
    /// 若 <paramref name="deltaSnapshot"/> 为全量快照（BaselineTick=0），直接返回并更新缓存。
    /// 若为增量快照（BaselineTick!=0），基于 _lastAppliedSnapshot 合并重建；
    /// 若 _lastAppliedSnapshot 为 null 或 ServerTick 不匹配 BaselineTick，返回 null（调用方应请求全量重传）。
    /// </para>
    /// </summary>
    /// <param name="deltaSnapshot">待应用的全量或增量快照。</param>
    /// <returns>重建后的全量快照；若 baseline 不匹配则返回 null。</returns>
    internal static SnapshotPacket? TryRebuildFromDelta(SnapshotPacket deltaSnapshot)
    {
        if (deltaSnapshot.BaselineTick == 0)
        {
            // 全量快照：直接缓存并返回
            Volatile.Write(ref _lastAppliedSnapshot, deltaSnapshot);
            return deltaSnapshot;
        }

        // 增量快照：需基于 _lastAppliedSnapshot 重建
        var baseline = Volatile.Read(ref _lastAppliedSnapshot);
        if (baseline == null || baseline.ServerTick != deltaSnapshot.BaselineTick)
        {
            // baseline 不匹配，无法重建 — 调用方应触发全量重传请求
            return null;
        }

        // 合并：以 baseline 的 Deltas 为基础，用 delta 中变化的 EntityDelta 按 EntityId 覆盖
        // Task 3：复用静态缓冲区，避免每次调用分配新 Dictionary/数组。
        _deltaMergeBufferStatic.Clear();
        foreach (var d in baseline.Deltas)
            _deltaMergeBufferStatic[d.EntityId] = d;
        foreach (var d in deltaSnapshot.Deltas)
            _deltaMergeBufferStatic[d.EntityId] = d;

        _deltaMergeListStatic.Clear();
        foreach (var d in _deltaMergeBufferStatic.Values)
            _deltaMergeListStatic.Add(d);

        var result = new SnapshotPacket
        {
            ServerTick = deltaSnapshot.ServerTick,
            BaselineTick = 0, // 重建后视为全量
            Deltas = _deltaMergeListStatic.ToArray(),
        };

        Volatile.Write(ref _lastAppliedSnapshot, result);
        return result;
    }

    /// <summary>
    /// Task D.3.3：重置客户端快照缓存（重连/握手场景使用，避免旧 baseline 污染新连接）。
    /// </summary>
    public static void ResetLastAppliedSnapshot()
    {
        Volatile.Write(ref _lastAppliedSnapshot, null);
    }

    /// <inheritdoc />
    public override void Update(World world, TimeSpan deltaTime)
    {
        // 检测 LocalPlayerOwnerId 变更，回溯更新已创建实体
        RetrospectivelyUpdateLocalPlayer(world);

        // 孤儿实体清理：定期扫描映射字典，移除已不存活的实体引用。
        // 防止因 Arch World 外部销毁实体（如场景切换、World.Clear）后字典残留导致内存泄漏。
        if (++_orphanCleanupCounter >= OrphanCleanupInterval)
        {
            _orphanCleanupCounter = 0;
            CleanupOrphanMappings(world);
        }

        // 远程实体超时清理（客户端兜底机制）：
        // 每 1 秒扫描一次，检测 TimeSinceLastSnapshot 超过阈值的远程实体并主动清理。
        // 解决"服务端异常断线检测需 19~65 秒，期间本地角色看到远程角色卡住不动"的问题。
        if (++_staleEntityCleanupCounter >= StaleEntityCleanupInterval)
        {
            _staleEntityCleanupCounter = 0;
            CleanupStaleEntities(world);
        }

        _updateCallCount++;
        var consumedThisTick = 0;
        var deltasThisTick = 0;
        var spawnsThisTick = 0;
        var updatesThisTick = 0;
        var despawnsThisTick = 0;

        while (consumedThisTick < MaxSnapshotsPerFrame && SnapshotReceiveBuffer.Instance.TryDequeue(out var snapshot))
        {
            consumedThisTick++;
            RecordSnapshotArrival(); // [Phase C4] 记录快照到达时间，更新自适应延迟统计
            deltasThisTick += snapshot.Deltas?.Length ?? 0;
            // Task D.3.3：增量快照重建。
            // BaselineTick=0 → 全量快照，直接应用并更新 _lastAppliedSnapshot。
            // BaselineTick!=0 → 增量快照，基于 _lastAppliedSnapshot 合并重建后应用。
            SnapshotPacket toApply;
            if (snapshot.BaselineTick == 0)
            {
                // 全量快照
                toApply = snapshot;
                Volatile.Write(ref _lastAppliedSnapshot, snapshot);
            }
            else
            {
                // 增量快照 — 基于 _lastAppliedSnapshot 重建
                var baseline = Volatile.Read(ref _lastAppliedSnapshot);
                if (baseline == null ||
                    baseline.ServerTick != snapshot.BaselineTick)
                {
                    // 修复：baseline 不匹配时不再静默跳过，而是直接应用收到的 delta（作为部分更新）。
                    // 原实现 `continue` 导致新加入玩家在等待全量快照期间（最多 1 秒）收不到任何更新，
                    // 表现为“远程角色冻结不动”。直接应用增量 delta 可确保移动/旋转/跳跃立即生效，
                    // 即使缺少 baseline 也不会比跳过更差（最坏情况是某些实体位置延迟到下一个全量快照才同步）。
                    toApply = snapshot;
                    Volatile.Write(ref _lastAppliedSnapshot, snapshot);
                }
                else
                {
                    // 增量快照处理。
                    //
                    // 修复（远程角色固定间隔刷新/闪退/闪现 — 增量快照重新处理 baseline delta）：
                    // 原实现将 baseline + delta 合并后处理所有 delta，导致每次增量快照都重新处理
                    // baseline 中的所有旧 delta。95 个未变化实体的旧位置被重新应用到插值目标，
                    // 角色被"拉回"到旧位置 → 闪现/闪退。只有全量快照（每秒一次）位置正确，
                    // 表现为"固定时间间隔被强制刷新，非刷新时段无状态变化"。
                    //
                    // 正确做法：增量快照只处理本次增量中的 delta，不重新处理 baseline delta。
                    // baseline 仅用于维护完整状态（供下次增量合并参考），不参与当前帧处理。
                    // 实体状态已在 ECS 世界中维护（通过之前的 Spawn/Update），无需重复处理。
                    toApply = snapshot; // 直接使用增量快照，只处理本次增量 delta

                    // 更新 baseline：合并增量 delta 到 baseline（用于下次增量合并）
                    _deltaMergeBuffer.Clear();
                    // 从 baseline 加载非 Despawn delta
                    foreach (var d in baseline.Deltas)
                    {
                        if (d.Kind != EntityDeltaKind.Despawn)
                            _deltaMergeBuffer[d.EntityId] = d;
                    }
                    // 用增量 delta 更新 baseline
                    foreach (var d in snapshot.Deltas)
                    {
                        if (d.Kind == EntityDeltaKind.Despawn)
                        {
                            // Despawn delta：从 baseline 中移除该实体
                            _deltaMergeBuffer.Remove(d.EntityId);
                        }
                        else
                        {
                            _deltaMergeBuffer[d.EntityId] = d;
                        }
                    }

                    // 更新 baseline：只保留非 Despawn delta
                    var newBaseline = new SnapshotPacket
                    {
                        ServerTick = snapshot.ServerTick,
                        BaselineTick = 0,
                        Deltas = _deltaMergeBuffer.Values.ToArray(),
                    };
                    Volatile.Write(ref _lastAppliedSnapshot, newBaseline);
                }
            }

            foreach (var delta in toApply.Deltas)
            {
                switch (delta.Kind)
                {
                    case EntityDeltaKind.Spawn:
                        spawnsThisTick++;
                        HandleSpawn(world, delta, toApply.ServerTick);
                        break;

                    case EntityDeltaKind.Update:
                        updatesThisTick++;
                        HandleUpdate(world, delta, toApply.ServerTick);
                        break;

                    case EntityDeltaKind.Despawn:
                        despawnsThisTick++;
                        HandleDespawn(world, delta);
                        break;
                }
            }
        }

        // Task 2：单帧消费上限溢出 — 队列仍有剩余时，限频（每秒一次）输出 Debug 日志。
        if (SnapshotReceiveBuffer.Instance.Count > 0)
        {
            OverflowCount++; // [Phase C6] 溢出计数，供游戏层转发到 ClientSyncMetrics
            var now = Stopwatch.GetTimestamp();
            if (now - _lastOverflowLogTime >= Stopwatch.Frequency)
            {
                _lastOverflowLogTime = now;
                Debug.WriteLine($"[SnapshotApplySystem] 单帧消费上限 {MaxSnapshotsPerFrame} 达到，剩余队列长度: {SnapshotReceiveBuffer.Instance.Count}");
            }
        }

        // 更新诊断统计（供 ArchEcsRuntime 读取并通过 UE5 日志系统输出）
        _totalSnapshotsApplied += consumedThisTick;
        _totalDeltasApplied += deltasThisTick;
        _totalSpawnsApplied += spawnsThisTick;
        _totalUpdatesApplied += updatesThisTick;
        _totalDespawnsApplied += despawnsThisTick;
        _lastTickConsumed = consumedThisTick;
        _lastTickDeltas = deltasThisTick;
        _lastTickSpawns = spawnsThisTick;
        _lastTickUpdates = updatesThisTick;
        _lastTickDespawns = despawnsThisTick;
    }

    /// <summary>
    /// 当 LocalPlayerOwnerId 从0变为非0时，回溯更新已创建实体的 IsLocalPlayer 标志和组件。
    /// 同时销毁因 HandleSpawn 竞态（LocalPlayerOwnerId 尚未设置时 Spawn 先到达）创建的重复本地玩家实体，
    /// 避免 InputSendSystem 每帧生成两个 InputPacket（一个正确、一个全零）导致角色几乎不动。
    /// </summary>
    private void RetrospectivelyUpdateLocalPlayer(World world)
    {
        var localPlayerOwnerId = LocalPlayerOwnerId;
        var previousOwnerId = Volatile.Read(ref _previousOwnerId);
        if (localPlayerOwnerId == 0 || previousOwnerId == localPlayerOwnerId)
            return;

        Volatile.Write(ref _previousOwnerId, localPlayerOwnerId);

        var query = new QueryDescription()
            .WithAll<NetworkIdentityComponent, AuthTransformComponent>();

        // 收集重复实体（在 Query 外销毁，避免迭代期间修改集合）
        var duplicatesToDestroy = new List<Entity>();
        var foundOriginal = false;

        world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId, ref AuthTransformComponent authTransform) =>
        {
            if (netId.EntityId != localPlayerOwnerId)
                return;

            if (netId.IsLocalPlayer)
            {
                // 已标记为本地玩家：第一个视为原始实体（CreateLocalPlayerEntity 创建），
                // 后续为重复实体（HandleSpawn 在 LocalPlayerOwnerId=0 时创建后被本方法上次调用转换）。
                if (!foundOriginal)
                {
                    foundOriginal = true;
                    return; // 保留原始实体
                }
                // 重复的本地玩家实体 → 标记销毁
                duplicatesToDestroy.Add(entity);
                return;
            }

            // IsLocalPlayer=false 的实体：如果已有原始实体，则为重复 → 销毁；否则转换为本地玩家。
            if (foundOriginal)
            {
                duplicatesToDestroy.Add(entity);
                return;
            }

            foundOriginal = true;
            netId.IsLocalPlayer = true;
            world.Set(entity, netId);

            // 移除 InterpolatedTransformComponent（如果存在）
            if (world.Has<InterpolatedTransformComponent>(entity))
            {
                world.Remove<InterpolatedTransformComponent>(entity);
            }

            // 添加 PlayerInputComponent + PredictedTransformComponent
            if (!world.Has<PlayerInputComponent>(entity))
            {
                var input = new PlayerInputComponent
                {
                    MoveX = 0f, MoveY = 0f, LookYaw = 0f, LookPitch = 0f, InputBits = 0,
                };
                world.Add(entity, input);
            }

            if (!world.Has<Components.PredictedTransformComponent>(entity))
            {
                // AuthTransformComponent 是 Y-up（Flax 坐标系：X=左右, Y=上下, Z=前后），
                // PredictedTransformComponent 供 MovementFormula 使用，必须为 Z-up（X=左右, Y=前后, Z=上下）。
                // 因此 Y/Z 交换：Y(前后) ← authTransform.Z, Z(上下) ← authTransform.Y
                var predicted = new Components.PredictedTransformComponent
                {
                    X = authTransform.X,
                    Y = authTransform.Z,
                    Z = authTransform.Y,
                    Pitch = authTransform.Pitch, Yaw = authTransform.Yaw,
                    ClientTick = 0,
                };
                world.Add(entity, predicted);
            }

            // 通知 FlaxActorSyncSystem 销毁可能已创建的远程玩家 Actor
            EntityDespawned?.Invoke(new EntityDespawnedEventArgs(netId.EntityId));
        });

        // 销毁重复实体（在 Query 外执行，避免迭代期间修改集合）
        foreach (var dup in duplicatesToDestroy)
        {
            if (world.IsAlive(dup))
            {
                world.Destroy(dup);
                _entityIdToArchEntity.Remove(localPlayerOwnerId);
            }
        }
    }

    /// <summary>
    /// 处理实体生成：创建新的 Arch 实体并添加网络身份、权威变换和插值变换组件。
    /// </summary>
    /// <remarks>
    /// 多玩家 AOI 场景下，服务器可能在 chunk 订阅切换、重连重传等情况下重复下发同一 EntityId 的 Spawn delta。
    /// 本方法在创建新实体前检查映射表，若该 EntityId 已存在则先销毁旧实体并通知外部系统释放对应资源，
    /// 避免旧实体残留导致泄露与 Actor 同步错乱。
    /// </remarks>
    private void HandleSpawn(World world, EntityDelta delta, long serverTick)
    {
        if (delta.Identity == null)
        {
            return;
        }

        // 防止重复 Spawn：若该 EntityId 已存在实体，根据实体类型决定处理方式。
        // 注意：本地玩家实体同样由 HandleSpawn 创建（参见 ZoneShardGrain 注释），
        // 因此不在此处按 EntityId == LocalPlayerOwnerId 跳过 Spawn，否则会破坏本地玩家创建链路。
        if (_entityIdToArchEntity.TryGetValue(delta.EntityId, out var existingEntity))
        {
            // 修复（远程角色闪移/闪现 — 重复 Spawn 销毁重建）：
            // 场景：PushImmediateFullSnapshotToObserver、网关重连、AOI 边界抖动等导致重复 Spawn。
            // 远程实体（携带 InterpolatedTransformComponent）：转为插值目标更新，不销毁重建。
            if (world.IsAlive(existingEntity) && world.Has<InterpolatedTransformComponent>(existingEntity))
            {
                ref var interp = ref world.Get<InterpolatedTransformComponent>(existingEntity);
                if (delta.Transform != null)
                {
                    var newTransform = delta.Transform.Value;
                    // Lerp 平滑追赶方案：只更新目标位置，与 HandleUpdate 保持一致。
                    interp.TargetX = newTransform.X;
                    interp.TargetY = newTransform.Y;
                    interp.TargetZ = newTransform.Z;
                    interp.TargetYaw = newTransform.Yaw;
                    interp.ServerTick = serverTick;
                    interp.TimeSinceLastSnapshot = 0f;
                    newTransform.ServerTick = serverTick;
                    world.Set(existingEntity, ref newTransform);
                }
                return; // 不销毁重建，保持平滑插值
            }

            // 修复（本地玩家重复 Spawn 保护）：
            // 本地玩家实体（携带 PredictedTransformComponent + IsLocalPlayer）：仅更新 AuthTransform，不销毁。
            if (world.IsAlive(existingEntity) && world.Has<Components.PredictedTransformComponent>(existingEntity))
            {
                ref var existNetId = ref world.Get<NetworkIdentityComponent>(existingEntity);
                if (existNetId.IsLocalPlayer)
                {
                    if (delta.Transform != null)
                    {
                        var auth = delta.Transform.Value;
                        auth.ServerTick = serverTick;
                        world.Set(existingEntity, ref auth);
                    }
                    return; // 不销毁重建，保留本地预测状态
                }
            }

            // 其他情况：销毁旧实体
            if (world.IsAlive(existingEntity))
            {
                world.Destroy(existingEntity);
            }
            _entityIdToArchEntity.Remove(delta.EntityId);
            EntityDespawned?.Invoke(new EntityDespawnedEventArgs(delta.EntityId));
        }

        // 修复（重复本地玩家实体根因）：
        // CreateLocalPlayerEntity（HundunWorldGame）在客户端先行创建本地玩家实体但不注册到本字典，
        // 服务端 BroadcastEntityLifecycleAsync step 2 补发全场 Spawn（含自身）到达后，
        // 本方法在字典中找不到该实体 → 创建第二个 IsLocalPlayer=true 实体。
        // 两个实体导致 InputSendSystem 每帧生成两个 InputPacket（一个正确、一个全零），
        // 服务端同 tick 应用后零输入覆盖正确输入 → 角色几乎不动 → 其他客户端看不到移动。
        // 修复：无条件检测 Arch World 中已存在的同 EntityId 本地玩家实体，收养（注册到字典）而非重复创建。
        // 原实现仅在 isLocalPlayerSpawn（LocalPlayerOwnerId != 0）时检查，但 fanout 路径可能比握手响应
        // 更快送达（Channel dispatch vs 直接 TCP），此时 LocalPlayerOwnerId 仍为 0，检查被跳过，
        // 导致重复实体。EntityId 全局唯一，仅本地玩家自身的 Spawn 才会匹配同 EntityId 的本地实体。
        {
            Entity foundLocal = default;
            var localQuery = new QueryDescription().WithAll<NetworkIdentityComponent, PlayerInputComponent, Components.PredictedTransformComponent>();
            world.Query(in localQuery, (Entity e, ref NetworkIdentityComponent nid) =>
            {
                if (nid.EntityId == delta.EntityId && nid.IsLocalPlayer)
                {
                    foundLocal = e;
                }
            });

            if (foundLocal != default && world.IsAlive(foundLocal))
            {
                // 收养已存在的本地玩家实体：注册到字典，保留其 ClientTick/位置/输入状态。
                _entityIdToArchEntity[delta.EntityId] = foundLocal;

                // 用服务端权威 Transform 更新 AuthTransformComponent（不覆盖 PredictedTransformComponent，
                // 保留客户端预测位置，避免回弹）。
                if (delta.Transform != null)
                {
                    var auth = delta.Transform.Value;
                    auth.ServerTick = serverTick;
                    world.Set(foundLocal, ref auth);
                }

                return;
            }
        }

        var archEntity = world.Create();

        var netId = new NetworkIdentityComponent
        {
            EntityId = delta.EntityId,
            IsLocalPlayer = delta.Identity.Value.OwnerId == LocalPlayerOwnerId && LocalPlayerOwnerId != 0,
        };

        var authTransform = delta.Transform != null
            ? delta.Transform.Value
            : new AuthTransformComponent
            {
                X = 0f, Y = 0f, Z = 0f,
                Pitch = 0f, Yaw = 0f, Roll = 0f,
                ServerTick = serverTick,
            };
        authTransform.ServerTick = serverTick;

        world.Add(archEntity, netId);
        world.Add(archEntity, authTransform);

        if (netId.IsLocalPlayer)
        {
            // 本地玩家：添加输入组件和预测变换组件，供 InputSendSystem / LocalSimulationSystem 使用
            var input = new PlayerInputComponent
            {
                MoveX = 0f,
                MoveY = 0f,
                LookYaw = 0f,
                LookPitch = 0f,
                InputBits = 0,
            };
            var predicted = new Components.PredictedTransformComponent
            {
                // AuthTransformComponent 是 Y-up（Flax 坐标系：X=左右, Y=上下, Z=前后）；
                // PredictedTransformComponent 供 MovementFormula 使用，必须为 Z-up（X=左右, Y=前后, Z=上下）。
                // 因此 Y/Z 交换：Y(前后) ← authTransform.Z, Z(上下) ← authTransform.Y
                X = authTransform.X,
                Y = authTransform.Z,
                Z = authTransform.Y,
                Pitch = authTransform.Pitch,
                Yaw = authTransform.Yaw,
                ClientTick = 0,
            };
            world.Add(archEntity, input);
            world.Add(archEntity, predicted);
        }
        else
        {
            var interp = new InterpolatedTransformComponent
            {
                X = authTransform.X,
                Y = authTransform.Y,
                Z = authTransform.Z,
                StartX = authTransform.X,
                StartY = authTransform.Y,
                StartZ = authTransform.Z,
                TargetX = authTransform.X,
                TargetY = authTransform.Y,
                TargetZ = authTransform.Z,
                Yaw = authTransform.Yaw,
                StartYaw = authTransform.Yaw,
                TargetYaw = authTransform.Yaw,
                Alpha = 1f,
                ServerTick = serverTick,
                ReceivedTick = 0,
            };
            world.Add(archEntity, interp);
        }

        _entityIdToArchEntity[delta.EntityId] = archEntity;
        EntitySpawned?.Invoke(new EntitySpawnedEventArgs(delta.EntityId, archEntity, netId.IsLocalPlayer, authTransform.X, authTransform.Y, authTransform.Z));
    }

    /// <summary>
    /// 处理实体更新：查找对应 Arch 实体，更新权威变换和插值目标。
    /// 本地玩家实体的变换更新被跳过（本地预测优先）。
    /// </summary>
    private void HandleUpdate(World world, EntityDelta delta, long serverTick)
    {
        var updateCount = Interlocked.Increment(ref _handleUpdateCount);

        if (!_entityIdToArchEntity.TryGetValue(delta.EntityId, out var archEntity))
        {
            // 容错：本地没该实体但 delta 带 Identity（服务端 TickAsync 始终带 Identity）时，
            // 自动当作 Spawn 处理。修复握手过程中 Spawn delta 被 Gateway 丢弃
            // （characterId→connection 映射在握手响应返回后才建立）导致的实体缺失。
            if (delta.Identity != null)
            {
                Interlocked.Increment(ref _handleUpdateSpawnFallbackCount);
                if (updateCount <= 5 || updateCount % 120 == 1)
                {
                    Debug.WriteLine($"[SnapshotApplySystem] HandleUpdate#{updateCount}: 实体 {delta.EntityId} 不在字典，走 Spawn 回退。Transform={(delta.Transform.HasValue ? $"X={delta.Transform.Value.X:F2},Y={delta.Transform.Value.Y:F2},Z={delta.Transform.Value.Z:F2}" : "null")}");
                }
                HandleSpawn(world, delta, serverTick);
            }
            return;
        }

        if (!world.IsAlive(archEntity))
        {
            _entityIdToArchEntity.Remove(delta.EntityId);
            return;
        }

        if (delta.Transform != null)
        {
            var newTransform = delta.Transform.Value;
            newTransform.ServerTick = serverTick;

            ref var netId = ref world.Get<NetworkIdentityComponent>(archEntity);

            if (netId.IsLocalPlayer)
            {
                // 本地玩家：仅写权威 Transform（供 ReconciliationSystem 比对与 LocalPlayerActorSyncSystem 兜底读取）。
                // 注意：原实现此处 `return;` 会导致后续 MovementState/State/AnimationState 应用被跳过，
                // 本地玩家永远收不到服务端权威的移动模式与 Montage 事件，本地动画状态机无法驱动。
                // 修复：删除 return;，让代码继续向下执行 MovementState/State/AnimationState 应用分支。
                // 后续 `if (world.Has<InterpolatedTransformComponent>(archEntity))` 判断天然会跳过本地玩家
                // （本地玩家无 InterpolatedTransformComponent），插值不会被错误更新。
                if (updateCount <= 5 || updateCount % 120 == 1)
                {
                    Debug.WriteLine($"[SnapshotApplySystem] HandleUpdate#{updateCount}: EntityId={delta.EntityId} 为本地玩家，仅写 AuthTransformComponent");
                }
                world.Set(archEntity, ref newTransform);
            }
            else if (world.Has<InterpolatedTransformComponent>(archEntity))
            {
                ref var interp = ref world.Get<InterpolatedTransformComponent>(archEntity);

                // 诊断日志：前 5 次无条件输出，后续每 120 次输出一次
                var interpCount = Interlocked.Increment(ref _handleUpdateRemoteInterpCount);
                if (interpCount <= 5 || interpCount % 120 == 1)
                {
                    Debug.WriteLine($"[SnapshotApplySystem] HandleUpdate#{updateCount} 远程实体插值更新: EntityId={delta.EntityId}, OldPos=({interp.X:F2},{interp.Y:F2},{interp.Z:F2}), NewTarget=({newTransform.X:F2},{newTransform.Y:F2},{newTransform.Z:F2}), Yaw={newTransform.Yaw:F2}, ServerTick={serverTick}");
                }

                // Lerp 平滑追赶方案：只更新目标位置，不设置 Start，不重置 Alpha。
                // InterpolationSystem 每帧执行 位置 += (目标 - 位置) * lerpFactor，
                // 角色以指数衰减速度追赶目标，稳态速度与服务端速度一致。
                //
                // 修复（远程角色闪移/移动不可见 — Alpha 插值加速运动问题）：
                // 原 Alpha 插值方案在快照到达频率（60Hz）高于插值完成时间（100ms）时，
                // 无论是否重置 Alpha 都有问题：
                // - 重置 Alpha → 角色永远无法到达目标，每帧只移动 17% 的距离
                // - 不重置 Alpha → 帧间移动距离从 17% 到 183% 速度变化，加速运动
                // Lerp 方案消除了这些问题，帧间移动距离变化温和（4倍范围内）。
                interp.TargetX = newTransform.X;
                interp.TargetY = newTransform.Y;
                interp.TargetZ = newTransform.Z;
                interp.TargetYaw = newTransform.Yaw;
                interp.ServerTick = serverTick;
                interp.TimeSinceLastSnapshot = 0f; // 重置快照计时

                world.Set(archEntity, ref newTransform);
            }
            else
            {
                // 防御性修复：远程实体缺少 InterpolatedTransformComponent 时主动补加，
                // 确保 FlaxActorSyncSystem（查询条件 WithAll<InterpolatedTransformComponent>）能同步此实体。
                // 这种情况可能发生在 HandleSpawn 时 LocalPlayerOwnerId 已设置但实体实际为远程玩家
                // （OwnerId 与 LocalPlayerOwnerId 的匹配时序问题），或重复 Spawn 竞态条件。
                // 补加后走与正常远程实体相同的插值更新路径，确保移动/旋转/跳跃可见。
                if (updateCount <= 10 || updateCount % 120 == 1)
                {
                    Debug.WriteLine($"[SnapshotApplySystem] HandleUpdate#{updateCount} 修复: EntityId={delta.EntityId} 非本地玩家但缺少 InterpolatedTransformComponent，已补加。IsLocalPlayer={netId.IsLocalPlayer}");
                }

                var recoveryInterp = new InterpolatedTransformComponent
                {
                    X = newTransform.X, Y = newTransform.Y, Z = newTransform.Z,
                    StartX = newTransform.X, StartY = newTransform.Y, StartZ = newTransform.Z,
                    TargetX = newTransform.X, TargetY = newTransform.Y, TargetZ = newTransform.Z,
                    Yaw = newTransform.Yaw, StartYaw = newTransform.Yaw, TargetYaw = newTransform.Yaw,
                    Alpha = 1f, ServerTick = serverTick, ReceivedTick = 0,
                    TimeSinceLastSnapshot = 0f, // [Phase C4]
                };
                world.Add(archEntity, recoveryInterp);

                // 补加后走与正常远程实体相同的插值更新路径
                ref var interp = ref world.Get<InterpolatedTransformComponent>(archEntity);
                interp.StartX = interp.X;
                interp.StartY = interp.Y;
                interp.StartZ = interp.Z;
                interp.TargetX = newTransform.X;
                interp.TargetY = newTransform.Y;
                interp.TargetZ = newTransform.Z;
                interp.StartYaw = interp.Yaw;
                interp.TargetYaw = newTransform.Yaw;
                interp.Alpha = 0f;
                interp.ServerTick = serverTick;
                interp.TimeSinceLastSnapshot = 0f; // [Phase C4]

                world.Set(archEntity, ref newTransform);
            }
        }

        if (delta.State != null)
        {
            // 扩展 EntityStateAuthComponent：包含 Health/MaxHealth/StateBits/Mana/MaxMana/Level/Exp/Stamina/MaxStamina。
            // 服务端通过变化触发 + 60 tick 心跳下发，客户端整体覆盖写入即可。
            var newState = delta.State.Value;
            if (world.Has<EntityStateAuthComponent>(archEntity))
            {
                world.Set(archEntity, ref newState);
            }
            else
            {
                world.Add(archEntity, newState);
            }
        }

        // Task B.6.1：应用移动状态（MovementMode + 水平速度 + 落地标志）。
        // 10Hz 心跳 + 变化触发；客户端写入组件供动画状态机与插值系统使用。
        if (delta.MovementState != null)
        {
            var newMovement = delta.MovementState.Value;
            newMovement.ServerTick = serverTick;
            if (world.Has<MovementStateAuthComponent>(archEntity))
            {
                world.Set(archEntity, ref newMovement);
            }
            else
            {
                world.Add(archEntity, newMovement);
            }
        }

        // Task B.6.1 + B.6.3：应用动画状态（Montage 触发/结束事件）并回调 UE5 侧。
        // 事件驱动同步：仅 Montage 触发/结束下发，循环动画由客户端根据 MovementState 自行驱动。
        if (delta.AnimationState != null)
        {
            var newAnim = delta.AnimationState.Value;
            newAnim.ServerTick = serverTick;
            if (world.Has<AnimationStateAuthComponent>(archEntity))
            {
                world.Set(archEntity, ref newAnim);
            }
            else
            {
                world.Add(archEntity, newAnim);
            }

            // 回调 UE5 侧播放/停止 Montage（AnimMontageId=0 表示停止）
            AnimationStateChanged?.Invoke(new AnimationStateChangedEventArgs(
                delta.EntityId,
                newAnim.AnimMontageId,
                newAnim.AnimInstanceId,
                newAnim.PlayRate,
                newAnim.IsLooping,
                newAnim.ServerTick));
        }
    }

    /// <summary>
    /// 处理实体销毁：从 Arch 世界中移除实体并清理映射表。
    /// </summary>
    /// <remarks>
    /// 本地玩家实体不会被 Despawn：本地玩家不会因 AOI 视野离开而被销毁，
    /// 防止在多玩家场景下因 chunk 订阅切换或 AOI 边界抖动导致本地玩家实体被误销毁。
    /// 服务端 ZoneShardGrain 设置 OwnerId == EntityId，因此用 EntityId == LocalPlayerOwnerId 判断等价于 OwnerId 比较。
    /// </remarks>
    private void HandleDespawn(World world, EntityDelta delta)
    {
        // 本地玩家保护：避免本地玩家因 AOI 离开视野被 Despawn。
        var localPlayerOwnerId = LocalPlayerOwnerId;
        if (localPlayerOwnerId != 0 && delta.EntityId == localPlayerOwnerId)
        {
            // Task 4：以诊断计数器替代热路径 Console.WriteLine。
            Interlocked.Increment(ref _localPlayerProtectionCount);
            return;
        }

        if (_entityIdToArchEntity.TryGetValue(delta.EntityId, out var archEntity))
        {
            if (world.IsAlive(archEntity))
            {
                world.Destroy(archEntity);
            }
            _entityIdToArchEntity.Remove(delta.EntityId);
            EntityDespawned?.Invoke(new EntityDespawnedEventArgs(delta.EntityId));
        }
        else
        {
            // ECS 字典中无此 EntityId（可能 Spawn delta 丢失或时序异常），
            // 但 FlaxActorSyncSystem 可能仍有对应 Actor 残留。
            // 仍触发 EntityDespawned 事件，让 FlaxActorSyncSystem 有机会清理残留 Actor。
            EntityDespawned?.Invoke(new EntityDespawnedEventArgs(delta.EntityId));
        }
    }

    /// <summary>
    /// 清理所有远程实体映射（断线/重连场景使用）。
    /// 注意：此方法不销毁 ECS 实体（ECS World 由外部管理），仅清理本系统的映射字典。
    /// </summary>
    public void ClearAllEntityMappings()
    {
        var count = _entityIdToArchEntity.Count;
        _entityIdToArchEntity.Clear();
        if (count > 0)
        {
            Debug.WriteLine($"[SnapshotApplySystem] 已清理所有实体映射: {count} 个");
        }
    }

    /// <summary>
    /// 孤儿实体清理：扫描映射字典，移除 Arch World 中已不存活的实体引用。
    /// 防止因外部销毁实体（场景切换、World.Clear、重复 Spawn 覆盖）后字典残留导致内存泄漏。
    /// 每 <see cref="OrphanCleanupInterval"/> 帧调用一次，均摊开销可忽略。
    /// </summary>
    private void CleanupOrphanMappings(World world)
    {
        _orphanKeysToRemove.Clear();
        foreach (var kvp in _entityIdToArchEntity)
        {
            if (!world.IsAlive(kvp.Value))
            {
                _orphanKeysToRemove.Add(kvp.Key);
            }
        }

        if (_orphanKeysToRemove.Count > 0)
        {
            foreach (var key in _orphanKeysToRemove)
            {
                _entityIdToArchEntity.Remove(key);
            }
            OrphanMappingsCleaned += _orphanKeysToRemove.Count;
            Debug.WriteLine($"[SnapshotApplySystem] 孤儿实体清理: 移除 {_orphanKeysToRemove.Count} 个已失效映射，字典剩余 {_entityIdToArchEntity.Count}");
        }
    }

    /// <summary>
    /// 远程实体超时清理（客户端兜底机制）。
    /// 扫描所有远程实体，若 <see cref="InterpolatedTransformComponent.TimeSinceLastSnapshot"/>
    /// 超过 <see cref="StaleEntityTimeoutSeconds"/>，则主动销毁实体并触发 <see cref="EntityDespawned"/> 事件。
    /// </summary>
    /// <remarks>
    /// <b>设计目的</b>：服务端检测异常断线（进程崩溃/断网）需要 19~65 秒
    /// （TCP KeepAlive 19s 或应用层心跳超时 60s + 5s 定时器间隔），
    /// 期间 Despawn delta 不会下发，本地角色看到远程角色"卡住不动"。
    /// 本机制在客户端侧独立判定：若远程实体超过阈值时间未收到任何快照更新，主动清理。
    /// <para>
    /// <b>安全性</b>：服务端全量快照每 1 秒发送一次（FullSnapshotIntervalTicks=60 @60Hz），
    /// 正常在线角色的 TimeSinceLastSnapshot 不会超过 1~2 秒。60 秒阈值意味着至少 50+ 次全量快照
    /// 未到达才触发清理，误判风险极低。即使误清理，后续收到 Spawn delta 会重新创建实体。
    /// </para>
    /// <para>
    /// <b>本地玩家保护</b>：本地玩家不携带 InterpolatedTransformComponent（使用 PredictedTransformComponent），
    /// 不会被本机制清理。
    /// </para>
    /// </remarks>
    private void CleanupStaleEntities(World world)
    {
        var timeout = StaleEntityTimeoutSeconds;
        _staleEntityIds.Clear();

        foreach (var kvp in _entityIdToArchEntity)
        {
            if (!world.IsAlive(kvp.Value))
                continue;

            // 仅检查携带 InterpolatedTransformComponent 的远程实体
            if (world.TryGet<InterpolatedTransformComponent>(kvp.Value, out var interp))
            {
                if (interp.TimeSinceLastSnapshot > timeout)
                {
                    _staleEntityIds.Add(kvp.Key);
                }
            }
        }

        if (_staleEntityIds.Count == 0)
            return;

        foreach (var entityId in _staleEntityIds)
        {
            // 复用 HandleDespawn 的销毁逻辑：销毁 ECS 实体 + 移除字典映射 + 触发 EntityDespawned 事件
            // 构造一个仅含 EntityId 的 EntityDelta（Kind=Despawn），其余字段对 HandleDespawn 无影响
            var delta = new EntityDelta { EntityId = entityId, Kind = EntityDeltaKind.Despawn };
            HandleDespawn(world, delta);
            StaleEntitiesCleaned++;
            Debug.WriteLine(
                $"[SnapshotApplySystem] 超时清理: 实体 {entityId} 超过 {timeout:F0} 秒未收到快照，" +
                $"判定为离线并主动清理（服务端 Despawn delta 可能尚未到达）");
        }
    }
}
