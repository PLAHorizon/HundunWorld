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

    // Task 2：单帧消费上限溢出日志限频时间戳（Stopwatch.GetTimestamp() 单位）。
    private long _lastOverflowLogTime;

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

    /// <summary>Task 4：本地玩家保护（跳过 Despawn）累计次数。</summary>
    public long LocalPlayerProtectionCount => Interlocked.Read(ref _localPlayerProtectionCount);

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

        _updateCallCount++;
        var consumedThisTick = 0;
        var deltasThisTick = 0;
        var spawnsThisTick = 0;
        var updatesThisTick = 0;
        var despawnsThisTick = 0;

        while (consumedThisTick < MaxSnapshotsPerFrame && SnapshotReceiveBuffer.Instance.TryDequeue(out var snapshot))
        {
            consumedThisTick++;
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
                    // baseline 不匹配：跳过本次应用（网络层应触发全量重传）
                    continue;
                }

                // 合并 baseline 与 delta：以 baseline 为基础，用 delta 中变化的 EntityDelta 覆盖
                // Task 3：复用实例缓冲区，避免每帧分配新 Dictionary/数组。
                _deltaMergeBuffer.Clear();
                foreach (var d in baseline.Deltas)
                    _deltaMergeBuffer[d.EntityId] = d;
                foreach (var d in snapshot.Deltas)
                    _deltaMergeBuffer[d.EntityId] = d;

                _deltaMergeList.Clear();
                foreach (var d in _deltaMergeBuffer.Values)
                    _deltaMergeList.Add(d);

                toApply = new SnapshotPacket
                {
                    ServerTick = snapshot.ServerTick,
                    BaselineTick = 0, // 重建后视为全量
                    Deltas = _deltaMergeList.ToArray(),
                };

                Volatile.Write(ref _lastAppliedSnapshot, toApply);
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

        world.Query(in query, (Entity entity, ref NetworkIdentityComponent netId, ref AuthTransformComponent authTransform) =>
        {
            if (netId.IsLocalPlayer)
                return; // 已经标记为本地玩家，跳过

            if (netId.EntityId == localPlayerOwnerId)
            {
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
            }
        });
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

        // 防止重复 Spawn：若该 EntityId 已存在实体，先销毁旧实体（避免泄露）。
        // 注意：本地玩家实体同样由 HandleSpawn 创建（参见 ZoneShardGrain 注释），
        // 因此不在此处按 EntityId == LocalPlayerOwnerId 跳过 Spawn，否则会破坏本地玩家创建链路。
        if (_entityIdToArchEntity.TryGetValue(delta.EntityId, out var existingEntity))
        {
            if (world.IsAlive(existingEntity))
            {
                world.Destroy(existingEntity);
            }
            _entityIdToArchEntity.Remove(delta.EntityId);
            // 通知外部系统（如 FlaxActorSyncSystem）销毁可能已创建的 Actor 等资源
            EntityDespawned?.Invoke(new EntityDespawnedEventArgs(delta.EntityId));
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
        if (!_entityIdToArchEntity.TryGetValue(delta.EntityId, out var archEntity))
        {
            // 容错：本地没该实体但 delta 带 Identity（服务端 TickAsync 始终带 Identity）时，
            // 自动当作 Spawn 处理。修复握手过程中 Spawn delta 被 Gateway 丢弃
            // （characterId→connection 映射在握手响应返回后才建立）导致的实体缺失。
            if (delta.Identity != null)
            {
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
                world.Set(archEntity, ref newTransform);
                return;
            }

            if (world.Has<InterpolatedTransformComponent>(archEntity))
            {
                ref var oldAuth = ref world.Get<AuthTransformComponent>(archEntity);
                ref var interp = ref world.Get<InterpolatedTransformComponent>(archEntity);

                // 失效点 #4 修复：刷新插值起点 StartX/Y/Z 为上一帧的权威位置。
                // 否则 InterpolationSystem（读 Start + (Target-Start)*t）会始终从
                // 实体出生点插值，导致远程角色每帧瞬移回出生位置。
                interp.StartX = oldAuth.X;
                interp.StartY = oldAuth.Y;
                interp.StartZ = oldAuth.Z;
                interp.X = oldAuth.X;
                interp.Y = oldAuth.Y;
                interp.Z = oldAuth.Z;
                interp.TargetX = newTransform.X;
                interp.TargetY = newTransform.Y;
                interp.TargetZ = newTransform.Z;
                interp.Alpha = 0f;
                interp.ServerTick = serverTick;
            }

            world.Set(archEntity, ref newTransform);
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
            Console.WriteLine($"[SnapshotApplySystem] 已清理所有实体映射: {count} 个");
        }
    }
}
