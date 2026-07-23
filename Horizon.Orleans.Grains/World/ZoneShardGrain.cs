using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Horizon.Game.Core.Persistence;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Interface.World;
using Horizon.Orleans.Interface;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// <see cref="IZoneShardGrain"/> 的最小可用实现（P2-b + P6-b observer 连线）。<br/>
/// 内部委托给 <see cref="ZoneShardAoi"/>；本 grain 目前是瞬态的——订阅关系是"在线玩家状态"，
/// 断线时 <see cref="IPlayerSessionGrain"/> 会显式调用 <see cref="RemoveSessionAsync"/>。
/// </summary>
public class ZoneShardGrain : Grain, IZoneShardGrain
{
    private readonly ILogger<ZoneShardGrain> _logger;
    private readonly ZoneShardAoi _aoi = new();
    private readonly Dictionary<Guid, IZoneShardFanoutObserver> _fanoutObservers = new();
    // Task 12：observer 快照缓存，避免每次广播 ToArray 分配。订阅/退订时置 null 失效。
    private KeyValuePair<Guid, IZoneShardFanoutObserver>[]? _observerSnapshot;
    private int _interactionSyncObserverFailures;
    private readonly MovementValidator _movementValidator;

    // fanout 无观察者告警限频（每 10 秒最多一次）
    private DateTime _lastNoObserverWarnUtc = DateTime.MinValue;
    private static readonly TimeSpan NoObserverWarnInterval = TimeSpan.FromSeconds(10);

    // Tick 周期状态字段
    private double _lastTickTime;
    private readonly Dictionary<ulong, SimulatedEntity> _simulatedEntities = new();
    private long _tickCount;
    private int _processedInputs;
    private int _correctionsIssued;
    private IDisposable? _tickTimer;
    private TimeSpan _tickInterval = TimeSpan.FromSeconds(1.0 / 60.0);

    // Task 19：上次 TickAsync 执行耗时（毫秒），通过 Interlocked 读写
    private long _lastTickDurationMs;

    // Task 10：TickAsync 复用缓冲，避免每次 tick 分配 List/Dictionary
    private readonly List<EntityDelta> _deltaBuffer = new();
    private readonly List<CorrectionPacket> _correctionBuffer = new();
    private readonly Dictionary<ulong, EntityDelta> _baselineDictBuffer = new();
    private readonly List<EntityDelta> _changedDeltasBuffer = new();
    private readonly EntityDelta[] _singleDeltaArray = new EntityDelta[1];  // Task 11 用

    // Task 11：BroadcastSnapshotAsync 按 chunk 分组聚合 delta 的复用缓冲
    private readonly Dictionary<ulong, List<EntityDelta>> _deltaByChunkBuffer = new();

    // Task 13：fire-and-forget 广播竞态保护（0=空闲，1=进行中）
    private int _broadcastInProgress;
    private long _broadcastStartTimestamp; // Stopwatch 时间戳，用于超时保护
    private DateTime _lastBroadcastSkipWarnUtc = DateTime.MinValue;
    private static readonly TimeSpan BroadcastSkipWarnInterval = TimeSpan.FromSeconds(10);
    /// <summary>广播超时保护阈值：超过此时间 _broadcastInProgress 仍为 1 则强制重置，防止永久阻塞。</summary>
    private static readonly long BroadcastTimeoutTicks = Stopwatch.Frequency * 5; // 5 秒

    // 修复：新实体注册后强制下一次广播为全量快照，确保新加入的玩家立即收到所有实体状态。
    private bool _forceFullSnapshotNextTick;

    // P8-8.5：交互槽状态持久化，用于校验 Start/End/Stolen 状态转换的合法性。
    private readonly Dictionary<(long interactableId, int slotIdx), (long interactorId, byte stateBits)> _interactionSlots = new();

    // 交互状态位标志常量（与客户端 InteractionSyncPacket.StateBits 对齐）。
    private const byte InteractionStateStart  = 0x01;
    private const byte InteractionStateEnd    = 0x02;
    private const byte InteractionStateStolen = 0x04;

    // Task C.4：场景对象状态表（key: objectId），由 HandleSceneObjectInteract 维护。
    private readonly Dictionary<ulong, SceneObjectStateData> _sceneObjectStates = new();

    // Task C.5：场景对象持久化存储（可选，为 null 时跳过持久化；测试环境可不注入）。
    private readonly ISceneObjectPersistenceStore? _sceneObjectPersistence;
    private IDisposable? _sceneObjectSaveTimer;
    private static readonly TimeSpan SceneObjectSaveInterval = TimeSpan.FromSeconds(30);

    // Task D.3：Snapshot 增量压缩 — 上一快照副本与全量快照间隔控制。
    // _lastSnapshot 始终保存“完整状态”（非增量），作为下次增量比对的 baseline；
    // 每 FullSnapshotIntervalTicks（60 tick = 1 秒 @ 60Hz）强制下发一次全量快照，防止 baseline 漂移。
    private SnapshotPacket? _lastSnapshot;
    private long _lastFullSnapshotTick;
    private const long FullSnapshotIntervalTicks = 60;

    // 移动公式固定步长（秒），与 MovementFormula / MovementValidator 保持一致。
    private const float MovementFormulaStepDt = 1f / 60f;

    // ===== 实体租约机制 =====
    // 实体租约时长：网关每 20 秒续约一次，90 秒阈值允许 4 次续约失败（网络抖动容错）。
    // 超过此时间未续约的实体被视为孤儿实体（网关崩溃/断线未清理），自动注销并广播 Despawn。
    private static readonly TimeSpan EntityLeaseDuration = TimeSpan.FromSeconds(90);
    // 孤儿实体检测间隔：每 10 秒扫描一次，避免每 tick 扫描的开销。
    private static readonly TimeSpan OrphanCheckInterval = TimeSpan.FromSeconds(10);
    // 修复 BUG：实体 Despawn 广播失败（无 fanout observer）时，孤儿清理会重试，但若 fanout
    // 订阅永久无法恢复，实体将永久残留。此常量定义最大重试次数（10 次 × 10 秒 = 100 秒），
    // 超过后强制从 _simulatedEntities 中移除实体，即使无法广播 Despawn delta。
    // 这是兜底机制，确保任何情况下实体都不会永久残留。
    private const int MaxFailedDespawnAttempts = 10;
    // 追踪实体 Despawn 广播失败的次数（entityId → 失败次数），由 UnregisterEntityAsync 和
    // CleanupOrphanEntitiesAsync 维护。超过 MaxFailedDespawnAttempts 后强制移除。
    private readonly Dictionary<ulong, int> _entityFailedDespawnAttempts = new();
    private IDisposable? _orphanCheckTimer;
    private DateTime _lastOrphanCheckUtc = DateTime.MinValue;

    // 诊断：前 5 次 BroadcastSnapshotAsync 调用无条件输出详情，定位 fanout 断点
    private int _broadcastDiagCount;

    // 诊断：前 5 次 TickAsync 调用无条件输出实体表状态，定位"实体未注册"问题
    private int _tickDiagCount;

    public ZoneShardGrain(ILogger<ZoneShardGrain> logger, ISceneObjectPersistenceStore? sceneObjectPersistence = null)
    {
        _logger = logger;
        _sceneObjectPersistence = sceneObjectPersistence;
        _movementValidator = new MovementValidator(new MovementValidator.Options
        {
            PositionEpsilon = MovementValidator.DefaultPositionEpsilon,
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            TickDtSeconds = 1f / 60f,
        });
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "ZoneShard {ShardId}: OnActivateAsync 被调用，当前 SessionCount={SessionCount}, ChunkCount={ChunkCount}",
            this.GetPrimaryKeyLong(), _aoi.SessionCount, _aoi.ChunkCount);

        _tickTimer = RegisterTimer(
            async _ =>
            {
                try
                {
                    await TickAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ZoneShard {ShardId}: 定时器 TickAsync 异常。", this.GetPrimaryKeyLong());
                }
            },
            null,
            _tickInterval,
            _tickInterval);

        // 实体租约检测定时器：每 10 秒扫描清理孤儿实体（网关崩溃/断线未清理的残留实体）。
        // 这是兜底机制，确保任何情况下孤儿实体都不会永久残留。
        _orphanCheckTimer = RegisterTimer(
            async _ =>
            {
                try
                {
                    await CleanupOrphanEntitiesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ZoneShard {ShardId}: 孤儿实体检测定时器异常。", this.GetPrimaryKeyLong());
                }
            },
            null,
            OrphanCheckInterval,
            OrphanCheckInterval);

        // Task C.5.4：激活时从持久化层加载场景对象状态。
        if (_sceneObjectPersistence is not null)
        {
            try
            {
                var shardKey = this.GetPrimaryKeyLong();
                var loaded = await _sceneObjectPersistence.LoadWorldStateAsync(shardKey);
                if (loaded is not null)
                {
                    foreach (var kv in loaded)
                    {
                        _sceneObjectStates[kv.Key] = kv.Value;
                    }
                    _logger.LogInformation(
                        "ZoneShard {ShardId}: 从持久化层加载 {Count} 个场景对象状态。",
                        shardKey, _sceneObjectStates.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ZoneShard {ShardId}: 加载场景对象状态失败，将以空状态启动。",
                    this.GetPrimaryKeyLong());
            }

            // Task C.5.5：定时落盘（30s）
            _sceneObjectSaveTimer = RegisterTimer(
                async _ =>
                {
                    try
                    {
                        await SaveSceneObjectStatesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "ZoneShard {ShardId}: 定时落盘场景对象状态异常。",
                            this.GetPrimaryKeyLong());
                    }
                },
                null,
                SceneObjectSaveInterval,
                SceneObjectSaveInterval);
        }

        await base.OnActivateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _tickTimer?.Dispose();
        _tickTimer = null;

        _orphanCheckTimer?.Dispose();
        _orphanCheckTimer = null;

        // Task C.5.5：停用前落盘一次，保证状态不丢失
        _sceneObjectSaveTimer?.Dispose();
        _sceneObjectSaveTimer = null;

        if (_sceneObjectPersistence is not null && _sceneObjectStates.Count > 0)
        {
            try
            {
                await SaveSceneObjectStatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ZoneShard {ShardId}: 停用前落盘场景对象状态失败。",
                    this.GetPrimaryKeyLong());
            }
        }

        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <summary>
    /// Task C.5.5：批量落盘当前所有场景对象状态到持久化层。
    /// </summary>
    private async Task SaveSceneObjectStatesAsync()
    {
        if (_sceneObjectPersistence is null || _sceneObjectStates.Count == 0) return;

        var shardKey = this.GetPrimaryKeyLong();
        await _sceneObjectPersistence.SaveWorldStateAsync(shardKey, _sceneObjectStates.Values.ToList());
    }

    /// <inheritdoc />
    public Task<int> SubscribeSessionAsync(long sessionId, ulong[] mortonKeys)
    {
        var added = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _aoi.Subscribe(sessionId, mortonKeys);
        _logger.LogInformation(
            "ZoneShard {ShardId}: SubscribeSessionAsync sessionId={SessionId}, added={Added}, SessionCount={SessionCount}, ChunkCount={ChunkCount}",
            this.GetPrimaryKeyLong(), sessionId, added, _aoi.SessionCount, _aoi.ChunkCount);
        return Task.FromResult(added);
    }

    /// <inheritdoc />
    public Task<int> UnsubscribeSessionAsync(long sessionId, ulong[] mortonKeys)
    {
        var removed = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _aoi.Unsubscribe(sessionId, mortonKeys);
        _logger.LogInformation(
            "ZoneShard {ShardId}: UnsubscribeSessionAsync sessionId={SessionId}, removed={Removed}, SessionCount={SessionCount}, ChunkCount={ChunkCount}",
            this.GetPrimaryKeyLong(), sessionId, removed, _aoi.SessionCount, _aoi.ChunkCount);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> RemoveSessionAsync(long sessionId)
    {
        // 诊断日志（Despawn 丢失 BUG）：记录 RemoveSession 调用前的订阅者列表，
        // 便于确认是否在 B 还在线时错误移除了 B 的 session。
        var subscribersBeforeRemove = _aoi.GetAllSubscribers();
        var removed = _aoi.RemoveSession(sessionId);
        _logger.LogWarning(
            "ZoneShard {ShardId}: [RemoveSession诊断] sessionId={SessionId}, removed={Removed}, " +
            "SessionCount={SessionCount}, ChunkCount={ChunkCount}, " +
            "SubscribersBeforeRemove=[{Before}], SubscribersAfterRemove=[{After}]",
            this.GetPrimaryKeyLong(), sessionId, removed, _aoi.SessionCount, _aoi.ChunkCount,
            string.Join(",", subscribersBeforeRemove), string.Join(",", _aoi.GetAllSubscribers()));
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public async Task<FanOutResult[]> BroadcastChunkDiffsAsync(WorldChunkDiffPacket[] diffs)
    {
        if (diffs is null || diffs.Length == 0)
            return Array.Empty<FanOutResult>();

        var targets = new (ulong, int)[diffs.Length];
        for (int i = 0; i < diffs.Length; i++)
        {
            targets[i] = (diffs[i].ChunkMortonKey, i);
        }
        var fanout = _aoi.FanOut(targets);

        // 构造 session → diff 下标的返回值（旧调用方仍可按此自行广播）。
        var result = new FanOutResult[fanout.Count];
        int idx = 0;
        foreach (var kv in fanout)
        {
            result[idx++] = new FanOutResult(kv.Key, kv.Value.ToArray());
        }

        // P6-b：把每个 diff 连同受众列表推给所有已注册 gateway 观察者。
        if (_fanoutObservers.Count > 0)
        {
            await NotifyFanoutObserversAsync(diffs).ConfigureAwait(false);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<long[]> GetSubscribersAsync(ulong mortonKey)
    {
        var subs = _aoi.GetSubscribers(mortonKey);
        return Task.FromResult(subs.ToArray());
    }

    /// <inheritdoc />
    public Task<(int SessionCount, int ChunkCount)> GetStatsAsync()
        => Task.FromResult((_aoi.SessionCount, _aoi.ChunkCount));

    /// <inheritdoc />
    public Task<ZoneShardLoadMetrics> GetLoadMetricsAsync()
    {
        var metrics = new ZoneShardLoadMetrics
        {
            EntityCount = _simulatedEntities.Count,
            SessionCount = _aoi.SessionCount,
            ChunkCount = _aoi.ChunkCount,
            LastTickDurationMs = Interlocked.Read(ref _lastTickDurationMs),
            PendingInputsCount = _simulatedEntities.Values.Sum(e => e.PendingInputs.Count),
            TickCount = _tickCount,
        };
        return Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public Task SubscribeFanoutAsync(Guid subscriptionId, IZoneShardFanoutObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var isNewObserver = !_fanoutObservers.ContainsKey(subscriptionId);
        _fanoutObservers[subscriptionId] = observer;
        _observerSnapshot = null; // Task 12：失效 observer 快照缓存

        // 修复 visibility BUG（核心根因）：新 Gateway 订阅 fanout 时，必须触发全量快照广播。
        // 场景：Gateway-2 在 Player A 进入后才订阅 fanout，此时 _forceFullSnapshotNextTick 已被
        // Player A 的 RegisterEntityAsync 消耗（或即将被消耗）。若无此修复，下一次 tick 产生的
        // 增量快照不包含静止实体（Player A 未移动），Gateway-2 永远收不到 Player A 的 Spawn/Update，
        // 导致 Gateway-2 上的玩家看不到 Player A。
        // 修复：新 observer 加入时强制下一次 tick 广播全量快照，确保该 observer 收到所有已存在实体
        // 的完整状态（BaselineTick=0，客户端可直接应用）。
        if (isNewObserver && _simulatedEntities.Count > 0)
        {
            _forceFullSnapshotNextTick = true;

            // 修复（即时推送）：不等待下一次 tick，立即向新 observer 推送当前所有实体的全量 Spawn。
            // 根因：_forceFullSnapshotNextTick 仅在下一次 TickAsync 时生效（最多 1/60 秒后），
            // 但若 TickAsync 的 BroadcastSnapshotAsync 因 _broadcastInProgress 竞态被跳过，
            // 或 observer 引用在 Orleans 回调链路中失效，新 observer 可能永远收不到首帧快照。
            // 即时推送绕过 tick 周期，确保新 observer 订阅后立即收到完整状态。
            _ = PushImmediateFullSnapshotToObserver(observer, subscriptionId);
        }

        _logger.LogInformation(
            "ZoneShard {ShardId}: fanout 订阅成功。SubscriptionId={SubscriptionId}, 当前订阅者数={Count}, IsNew={IsNew}",
            this.GetPrimaryKeyLong(), subscriptionId, _fanoutObservers.Count, isNewObserver);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 立即向新订阅的 observer 推送当前所有实体的全量 Spawn delta。
    /// 绕过 TickAsync 周期，确保新 observer 订阅后立即收到完整世界状态。
    /// 失败时仅记录日志，不影响主流程（下一次 tick 的 _forceFullSnapshotNextTick 仍会重试）。
    /// </summary>
    private async Task PushImmediateFullSnapshotToObserver(IZoneShardFanoutObserver observer, Guid subscriptionId)
    {
        try
        {
            if (_simulatedEntities.Count == 0) return;

            var allSubscribers = _aoi.GetAllSubscribers();
            if (allSubscribers.Count == 0) return;

            var deltas = new List<EntityDelta>(_simulatedEntities.Count);
            foreach (var kv in _simulatedEntities)
            {
                var e = kv.Value;
                deltas.Add(new EntityDelta
                {
                    EntityId = kv.Key,
                    Kind = EntityDeltaKind.Spawn,
                    Identity = new NetworkIdentityAuthComponent
                    {
                        NetworkId = kv.Key,
                        EntityType = 0,
                        OwnerId = kv.Key,
                    },
                    Transform = new AuthTransformComponent
                    {
                        X = e.X,
                        Y = e.Z,   // ECS Z → Flax Y
                        Z = e.Y,   // ECS Y → Flax Z
                        Pitch = 0f,
                        Yaw = e.Yaw,
                        Roll = 0f,
                    },
                });
            }

            var diff = new WorldChunkDiffPacket
            {
                ChunkMortonKey = 0,
                DiffSeqStart = _tickCount,
                DiffSeqEnd = _tickCount,
                Payload = MemoryPack.MemoryPackSerializer.Serialize(deltas.ToArray()),
                PayloadType = WorldChunkDiffPayloadType.EntityDelta,
            };

            await observer.OnChunkDiffAsync(diff, allSubscribers).ConfigureAwait(false);

            _logger.LogInformation(
                "ZoneShard {ShardId}: 即时全量快照已推送到新 observer。SubscriptionId={SubscriptionId}, EntityCount={EntityCount}, SubscriberCount={SubscriberCount}",
                this.GetPrimaryKeyLong(), subscriptionId, deltas.Count, allSubscribers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ZoneShard {ShardId}: 即时全量快照推送到新 observer 失败（下一次 tick 将重试）。SubscriptionId={SubscriptionId}",
                this.GetPrimaryKeyLong(), subscriptionId);
        }
    }

    /// <inheritdoc />
    public Task UnsubscribeFanoutAsync(Guid subscriptionId)
    {
        _fanoutObservers.Remove(subscriptionId);
        _observerSnapshot = null; // Task 12：失效 observer 快照缓存
        _logger.LogInformation(
            "ZoneShard {ShardId}: fanout 取消订阅。SubscriptionId={SubscriptionId}, 当前订阅者数={Count}",
            this.GetPrimaryKeyLong(), subscriptionId, _fanoutObservers.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Task 12：获取 observer 快照（惰性创建并缓存，订阅/退订时失效）。
    /// 替代每次广播都 _fanoutObservers.ToArray() 的热路径分配。
    /// </summary>
    private KeyValuePair<Guid, IZoneShardFanoutObserver>[] GetObserversSnapshot()
    {
        var snapshot = _observerSnapshot;
        if (snapshot is not null) return snapshot;
        snapshot = _fanoutObservers.ToArray();
        _observerSnapshot = snapshot;
        return snapshot;
    }

    /// <summary>
    /// 对本批 diff 逐条计算订阅者，并异步推送给所有 fanout 观察者；
    /// 单个观察者异常不会影响其他观察者或主流程（与 <c>IMUserGrain.NotifyGatewayObserversAsync</c> 一致）。
    /// </summary>
    private async Task NotifyFanoutObserversAsync(WorldChunkDiffPacket[] diffs)
    {
        // 先按 mortonKey 查订阅者，避免在 observer 循环中重复查询。
        for (int i = 0; i < diffs.Length; i++)
        {
            var diff = diffs[i];
            var subs = _aoi.GetSubscribers(diff.ChunkMortonKey);
            if (subs.Count == 0) continue;

            var observers = GetObserversSnapshot();
            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(diff, subs).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "ZoneShard {ShardId}: 推送 fanout 到观察者 {SubscriptionId} 失败（已吞；不影响其他观察者）。",
                        this.GetPrimaryKeyLong(), subscriptionId);
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<int> TickAsync(double tickTime)
    {
        // Task 19：记录 tick 开始时间戳，用于测量执行耗时
        var tickStartTimestamp = Stopwatch.GetTimestamp();

        var processedEntityCount = 0;
        // Task 10.2：复用字段级缓冲，避免每次 tick 分配 List
        var deltas = _deltaBuffer; deltas.Clear();
        var corrections = _correctionBuffer; corrections.Clear();

        foreach (var kv in _simulatedEntities)
        {
            var entityId = kv.Key;
            var entity = kv.Value;

            // 失效点 #2 修复：静止实体（PendingInputs 为空）不再 continue 跳过。
            // 改为：有输入时跑权威校验+移动；无输入时仍产当前位置的 Update delta，
            // 保证其他客户端能持续看到该实体（否则其他玩家看不到静止角色）。

            var startPos = new WorldPosition(entity.X, entity.Y, entity.Z);
            var startYaw = entity.Yaw;

            float latestYaw = entity.Yaw;
            if (entity.PendingInputs.Count > 0)
            {
                var lastInput = entity.PendingInputs[entity.PendingInputs.Count - 1];
                latestYaw = lastInput.LookYaw;
            }
            entity.Yaw = latestYaw;

            if (entity.PendingInputs.Count > 0)
            {
                // Task 10.3：复用 per-entity 输入缓冲，避免每次 tick ToArray 分配
                entity.PendingInputBuffer ??= new InputPacket[entity.PendingInputs.Count];
                if (entity.PendingInputBuffer.Length < entity.PendingInputs.Count)
                {
                    entity.PendingInputBuffer = new InputPacket[entity.PendingInputs.Count];
                }
                entity.PendingInputs.CopyTo(entity.PendingInputBuffer, 0);
                ReadOnlySpan<InputPacket> inputs = new(entity.PendingInputBuffer, 0, entity.PendingInputs.Count);

                var clientEnd = new WorldPosition(entity.ReportedEndX, entity.ReportedEndY, entity.ReportedEndZ);

                var validationResult = _movementValidator.Validate(
                    entityId,
                    startPos,
                    entity.Vz,
                    inputs,
                    clientEnd,
                    _tickCount);

                if (validationResult.NeedsCorrection)
                {
                    corrections.Add(validationResult.Correction!);
                    entity.X = validationResult.AuthoritativeEnd.X;
                    entity.Y = validationResult.AuthoritativeEnd.Y;
                    entity.Z = validationResult.AuthoritativeEnd.Z;
                    entity.Vz = validationResult.AuthoritativeVz;

                    _correctionsIssued++;
                }
                else
                {
                    entity.X = validationResult.AuthoritativeEnd.X;
                    entity.Y = validationResult.AuthoritativeEnd.Y;
                    entity.Z = validationResult.AuthoritativeEnd.Z;
                    entity.Vz = validationResult.AuthoritativeVz;
                }

                entity.PendingInputs.Clear();
                entity.LastSyncTick = _tickCount;
                entity.HadInputThisTick = true;

                const float groundedEpsilon = 0.001f;
                var prevZ = startPos.Z;
                var curZ = validationResult.AuthoritativeEnd.Z;
                var isGrounded = MathF.Abs(curZ - prevZ) < groundedEpsilon && validationResult.AuthoritativeVz <= 0f;
                entity.IsGrounded = isGrounded;
                entity.JumpCount = isGrounded ? 0 : entity.JumpCount;

                _processedInputs += inputs.Length;
                processedEntityCount++;
            }

            // 计算本 tick 实际水平速度（米/秒），用于 MovementStateAuthComponent。
            // 注意：使用 FixedDtSeconds（1/60）与 MovementFormula 一致。
            // 无输入的静止实体速度为 0。
            var velX = (entity.X - startPos.X) / MovementFormulaStepDt;
            var velY = (entity.Y - startPos.Y) / MovementFormulaStepDt;

            // 根据速度推导移动模式：区分 Jump（上升）与 Fall（下降）。
            // 修复 #15：原实现仅根据 IsGrounded 分 Fall/非坠落，无法区分跳跃上升与自由落体阶段，
            // 客户端动画状态机无法据此切换 Jump→Fall 过渡。
            var speedSq = velX * velX + velY * velY;
            MovementMode derivedMovementMode;
            if (!entity.IsGrounded)
            {
                derivedMovementMode = entity.Vz > 0f ? MovementMode.Jump : MovementMode.Fall;
            }
            else if (speedSq > 0.1f)
            {
                derivedMovementMode = MovementMode.Run;
            }
            else
            {
                derivedMovementMode = MovementMode.Walk;
            }

            bool hasPositionChanged = MathF.Abs(entity.X - startPos.X) > 0.001f
                || MathF.Abs(entity.Y - startPos.Y) > 0.001f
                || MathF.Abs(entity.Z - startPos.Z) > 0.001f;
            bool hasYawChanged = MathF.Abs(entity.Yaw - startYaw) > 0.001f;
            bool hasMovementChanged = derivedMovementMode != entity.PrevMovementMode
                || entity.IsGrounded != entity.PrevIsGrounded
                || MathF.Abs(velX - entity.PrevVelocityXZ_X) > 0.01f
                || MathF.Abs(velY - entity.PrevVelocityXZ_Y) > 0.01f;

            bool hasInput = entity.HadInputThisTick;
            bool hasPendingAnimation = entity.PendingAnimationEvents is not null && entity.PendingAnimationEvents.Count > 0;
            bool updateHeartbeatDue = (_tickCount - entity.LastUpdateBroadcastTick) >= 60;

            bool shouldBroadcast = hasInput || hasPositionChanged || hasYawChanged || hasMovementChanged
                || hasPendingAnimation || updateHeartbeatDue;

            // 修复：全量快照模式下（新玩家加入/定期全量）包含所有实体，
            // 而非仅 shouldBroadcast 的实体。原实现在全量快照时仍按 shouldBroadcast 过滤，
            // 导致静止实体（heartbeat 未到期）不出现在全量快照中，新玩家看不到它们。
            bool includeInSnapshot = shouldBroadcast || _forceFullSnapshotNextTick;

            if (includeInSnapshot)
            {
                var delta = new EntityDelta
                {
                    EntityId = entityId,
                    Kind = EntityDeltaKind.Update,
                    Identity = new NetworkIdentityAuthComponent
                    {
                        NetworkId = entityId,
                        EntityType = 0,
                        OwnerId = entityId,
                    },
                    Transform = new AuthTransformComponent
                    {
                        X = entity.X,
                        Y = entity.Z,
                        Z = entity.Y,
                        Pitch = 0f,
                        Yaw = entity.Yaw,
                        Roll = 0f,
                        ServerTick = _tickCount,
                    },
                };

                delta.MovementState = new MovementStateAuthComponent
                {
                    MovementMode = derivedMovementMode,
                    VelocityXZ_X = velX,
                    VelocityXZ_Y = velY,
                    IsGrounded = entity.IsGrounded,
                    ServerTick = _tickCount,
                };

                bool attributeChanged = entity.Mana != entity.PrevMana
                    || entity.Level != entity.PrevLevel
                    || entity.Exp != entity.PrevExp
                    || entity.Stamina != entity.PrevStamina
                    || entity.Hp != entity.PrevHp
                    || entity.EntityStateBits != entity.PrevEntityStateBits;

                bool attributeHeartbeatDue = (_tickCount - entity.LastAttributeHeartbeatTick) >= 60;

                if (attributeChanged || attributeHeartbeatDue)
                {
                    delta.State = new EntityStateAuthComponent
                    {
                        Health = entity.Hp,
                        MaxHealth = entity.MaxHp > 0 ? entity.MaxHp : entity.Hp,
                        StateBits = entity.EntityStateBits,
                        Mana = entity.Mana,
                        MaxMana = entity.MaxMana,
                        Level = entity.Level,
                        Exp = entity.Exp,
                        Stamina = entity.Stamina,
                        MaxStamina = entity.MaxStamina,
                    };
                    entity.LastAttributeHeartbeatTick = _tickCount;

                    entity.PrevMana = entity.Mana;
                    entity.PrevLevel = entity.Level;
                    entity.PrevExp = entity.Exp;
                    entity.PrevStamina = entity.Stamina;
                    entity.PrevHp = entity.Hp;
                    entity.PrevEntityStateBits = entity.EntityStateBits;
                }

                if (hasPendingAnimation)
                {
                    var animEvt = entity.PendingAnimationEvents!.Dequeue();
                    animEvt.ServerTick = _tickCount;
                    delta.AnimationState = animEvt;
                }

                entity.LastUpdateBroadcastTick = _tickCount;
                deltas.Add(delta);
            }

            entity.PrevMovementMode = derivedMovementMode;
            entity.PrevIsGrounded = entity.IsGrounded;
            entity.PrevVelocityXZ_X = velX;
            entity.PrevVelocityXZ_Y = velY;
            entity.HadInputThisTick = false;

            _simulatedEntities[entityId] = entity;
        }

        var completedCasts = new List<(ulong EntityId, int SkillId, ulong TargetId, long ElapsedTicks)>();
        foreach (var kv in _simulatedEntities)
        {
            var entity = kv.Value;
            if (!entity.IsCasting)
                continue;

            var elapsed = _tickCount - entity.CastStartTime;
            if (elapsed >= 30)
            {
                completedCasts.Add((kv.Key, entity.CastSkillId, entity.CastTargetId, elapsed));
            }
        }

        // 诊断：前 5 次 TickAsync 输出实体表状态，定位"实体未注册导致 deltas 为空"问题
        _tickDiagCount++;
        if (_tickDiagCount <= 5)
        {
            _logger.LogWarning(
                "[ZoneShardGrain 诊断#{N}] TickAsync：Tick={Tick}, Entities={EntityCount}, Deltas={DeltaCount}, PendingInputs={PendingInputs}, Sessions={SessionCount}, Observers={ObserverCount}",
                _tickDiagCount, _tickCount, _simulatedEntities.Count, deltas.Count, _simulatedEntities.Values.Sum(e => e.PendingInputs.Count), _aoi.SessionCount, _fanoutObservers.Count);
        }

        if (deltas.Count > 0)
        {
            // Task D.3：snapshot 始终保存完整状态（所有实体的 delta），作为增量比对的 baseline。
            var snapshot = new SnapshotPacket
            {
                ServerTick = _tickCount,
                BaselineTick = 0,
                Deltas = deltas.ToArray(),
            };

            // Task D.3.1/D.3.4：判定全量/增量模式。
            // 首次 tick 或距上次全量快照超过 FullSnapshotIntervalTicks（60 tick = 1 秒）时强制全量。
            // 修复：新实体注册后也强制全量，确保新玩家立即收到所有实体状态。
            SnapshotPacket toSend;
            bool isFullSnapshot = false;
            if (_lastSnapshot == null || (_tickCount - _lastFullSnapshotTick) >= FullSnapshotIntervalTicks || _forceFullSnapshotNextTick)
            {
                // 全量快照：BaselineTick=0，客户端直接应用
                snapshot.BaselineTick = 0;
                toSend = snapshot;
                _lastFullSnapshotTick = _tickCount;
                _forceFullSnapshotNextTick = false;
                isFullSnapshot = true;
            }
            else
            {
                // Task D.3.2：增量快照 — 仅含与 baseline 不同的 EntityDelta
                toSend = BuildDeltaSnapshot(_lastSnapshot, deltas);
            }

            // Task 13：通过 Interlocked.CompareExchange 保证不与上次广播并发
            // 修复：增加超时保护，防止 _broadcastInProgress 因 Orleans observer 调用挂起而永久卡在 1，
            // 导致后续所有广播被永久跳过（"角色无法看到彼此的移动"根因之一）。
            if (Interlocked.CompareExchange(ref _broadcastInProgress, 1, 0) == 0)
            {
                _broadcastStartTimestamp = Stopwatch.GetTimestamp();
                _ = BroadcastSnapshotAsync(toSend, corrections, isFullSnapshot).ContinueWith(
                    _ => Interlocked.Exchange(ref _broadcastInProgress, 0),
                    TaskScheduler.Default);
            }
            else
            {
                // 超时保护：如果上次广播超过 5 秒仍未完成，强制重置标志位，恢复广播能力。
                var elapsed = Stopwatch.GetTimestamp() - _broadcastStartTimestamp;
                if (elapsed > BroadcastTimeoutTicks)
                {
                    _logger.LogError(
                        "ZoneShard {ShardId}: 广播超时保护触发（{ElapsedMs}ms），强制重置 _broadcastInProgress。Tick={Tick}",
                        this.GetPrimaryKeyLong(), elapsed * 1000 / Stopwatch.Frequency, _tickCount);
                    Interlocked.Exchange(ref _broadcastInProgress, 0);
                    // 立即重试本次广播
                    if (Interlocked.CompareExchange(ref _broadcastInProgress, 1, 0) == 0)
                    {
                        _broadcastStartTimestamp = Stopwatch.GetTimestamp();
                        _ = BroadcastSnapshotAsync(toSend, corrections, isFullSnapshot).ContinueWith(
                            _ => Interlocked.Exchange(ref _broadcastInProgress, 0),
                            TaskScheduler.Default);
                    }
                }
                else
                {
                    // 上次广播未完成（未超时）：跳过本次广播并输出限频告警
                    var now = DateTime.UtcNow;
                    if (now - _lastBroadcastSkipWarnUtc >= BroadcastSkipWarnInterval)
                    {
                        _lastBroadcastSkipWarnUtc = now;
                        _logger.LogWarning(
                            "ZoneShard {ShardId}: tick 跳过广播（上次未完成）。Tick={Tick}, Entities={EntityCount}",
                            this.GetPrimaryKeyLong(), _tickCount, _simulatedEntities.Count);
                    }
                }
            }
            // _lastSnapshot 始终保存完整状态（非增量），供下次增量比对（无论是否广播都更新）
            _lastSnapshot = snapshot;
        }

        _tickCount++;
        _lastTickTime = tickTime;

        // Task 19：记录本次 tick 执行耗时（毫秒）
        var elapsedTicks = Stopwatch.GetTimestamp() - tickStartTimestamp;
        Interlocked.Exchange(ref _lastTickDurationMs, elapsedTicks * 1000 / Stopwatch.Frequency);

        return Task.FromResult(processedEntityCount);
    }

    /// <inheritdoc />
    public async Task RegisterEntityAsync(ulong entityId, float initialX, float initialY, float initialZ, float maxSpeed = 6f)
    {
        // Flax 使用 Y-up（Y=上下, Z=前后），ECS/MovementFormula 使用 Z-up（Y=前后, Z=上下）。
        // 在此转换为 ECS Z-up 坐标，保证 MovementFormula 重力/水平移动运算正确。
    var ecsX = initialX;   // 左右（不变）
    var ecsY = initialZ;   // 前后（Flax Z → ECS Y）
    var ecsZ = initialY;   // 上下（Flax Y → ECS Z）

        _simulatedEntities[entityId] = new SimulatedEntity
        {
            X = ecsX,
            Y = ecsY,
            Z = ecsZ,
            Vz = 0f,
            Yaw = 0f,
            MaxSpeed = maxSpeed,
            PendingInputs = new List<InputPacket>(),
            LastSyncTick = 0,
            LeaseExpiry = DateTime.UtcNow + EntityLeaseDuration,
        };

        _logger.LogInformation(
            "ZoneShard {ShardId}: 注册实体 {EntityId} 初始位置 ECS({X:F2},{Y:F2},{Z:F2}) ← Flax({FX:F2},{FY:F2},{FZ:F2})。",
            this.GetPrimaryKeyLong(), entityId, ecsX, ecsY, ecsZ, initialX, initialY, initialZ);

        // 修复：新实体注册后强制下一次 TickAsync 广播全量快照，
        // 确保新加入的玩家能立即收到所有已存在实体的完整状态（而非仅收到增量 delta）。
        _forceFullSnapshotNextTick = true;

        // 失效点 #1 修复：注册后立即广播 Spawn delta，让所有已在线玩家看到新玩家。
        // 同时给新玩家补发当前所有已存在实体的 Spawn，让其看到已在场的其他玩家。
        try
        {
            await BroadcastEntityLifecycleAsync(entityId, ecsX, ecsY, ecsZ, EntityDeltaKind.Spawn, includeNewSession: true)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ZoneShard {ShardId}: 注册实体 {EntityId} 后广播 Spawn 失败。",
                this.GetPrimaryKeyLong(), entityId);
        }
    }

    /// <inheritdoc />
    public async Task EnterWorldAsync(
        long sessionId,
        ulong entityId,
        float initialX,
        float initialY,
        float initialZ,
        ulong[] initialInterestChunks,
        float maxSpeed = 6f)
    {
        ArgumentNullException.ThrowIfNull(initialInterestChunks);

        // 幂等性修复：若实体已存在（重连场景），先清理旧实体和旧 AOI 订阅，避免：
        // 1. 旧实体的 AOI chunk 订阅泄露（sessionToChunks 累积不清理）
        // 2. 旧实体未广播 Despawn，其他客户端看到角色"瞬移"而非平滑过渡
        // 3. 旧实体的 PendingInputs/Hp/Mana 等状态丢失
        if (_simulatedEntities.ContainsKey(entityId))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: EnterWorldAsync 实体 {EntityId} 已存在（重连场景），先清理旧实体和 AOI 订阅。",
                this.GetPrimaryKeyLong(), entityId);
            await UnregisterEntityAsync(entityId).ConfigureAwait(false);
            _aoi.RemoveSession(sessionId);
        }

        var added = _aoi.Subscribe(sessionId, initialInterestChunks);
        // 诊断日志（Despawn 丢失 BUG）：记录 B 进入游戏时订阅的 chunk 列表和当前所有订阅者，
        // 便于确认 B 的 AOI 订阅是否正确建立，以及 A 是否在订阅者列表中。
        _logger.LogWarning(
            "ZoneShard {ShardId}: [EnterWorld诊断] sessionId={SessionId}, entityId={EntityId}, " +
            "initialSubscriptions={Added}, ChunkCount={ChunkCount}, InitialChunks=[{Chunks}], " +
            "AllSubscribersAfterSubscribe=[{AllSubs}], TotalSubscriberCount={SubCount}",
            this.GetPrimaryKeyLong(), sessionId, entityId, added, initialInterestChunks.Length,
            string.Join(",", initialInterestChunks.Take(10)),
            string.Join(",", _aoi.GetAllSubscribers()), _aoi.SessionCount);

        // RegisterEntityAsync 内部已做 Flax→ECS 坐标转换（Y/Z 互换），
        // 此处直接传入 Flax Y-up 原始坐标（X=左右, Y=上下, Z=前后）即可。
        // 注意：不要在此处额外做 Y/Z 互换，否则会与 RegisterEntityAsync 内部转换叠加，
        // 导致双重转换（坐标恢复为 Flax 顺序），重力施加在错误轴上，实体快速漂移，
        // Despawn 广播的 chunk key 与 AOI 订阅不匹配，其他客户端无法收到 Despawn delta。
        await RegisterEntityAsync(entityId, initialX, initialY, initialZ, maxSpeed).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnregisterEntityAsync(ulong entityId)
    {
        float x = 0f, y = 0f, z = 0f;
        if (_simulatedEntities.TryGetValue(entityId, out var leaving))
        {
            x = leaving.X; y = leaving.Y; z = leaving.Z;

            // 修复 BUG：先广播 Despawn，再移除实体。
            // 原实现先 _simulatedEntities.Remove 再广播，当 _fanoutObservers 为空（订阅断开）时
            // BroadcastEntityLifecycleAsync 直接 return，Despawn delta 永久丢失，实体也已从字典移除，
            // 孤儿清理无法补救（实体已不在 _simulatedEntities 中），导致其他客户端看到离线角色永久残留。
            // 新实现：先广播，广播成功才移除；广播失败则保留实体并立即过期租约，让孤儿清理定时器
            // （10 秒间隔）在 fanout 订阅恢复后重试广播 Despawn。
            bool broadcastOk = false;
            try
            {
                broadcastOk = await BroadcastEntityLifecycleAsync(entityId, x, y, z, EntityDeltaKind.Despawn, includeNewSession: false)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ZoneShard {ShardId}: 注销实体 {EntityId} 时广播 Despawn 抛异常。",
                    this.GetPrimaryKeyLong(), entityId);
            }

            // 修复 BUG（角色离线应立即从服务端下线）：
            // 原实现在广播失败时保留实体并过期租约，等待孤儿清理定时器重试广播 Despawn。
            // 这导致角色实体在 _simulatedEntities 中残留最多 100 秒（10 次 × 10 秒），
            // 与"立即从服务端下线"的需求矛盾，且会引发以下问题：
            //   1) 实体残留期间，TickAsync 仍会为该实体生成 Update delta，其他客户端持续看到该角色
            //   2) AOI session 已被 RemoveSessionAsync 清理，但实体仍在 _simulatedEntities 中，状态不一致
            //   3) CharacterGrain.GoOfflineAsync 已被调用并持久化 IsOnline=false，但实体仍在 ZoneShard 中，
            //      外部观察"服务端已下线但实体仍在"，造成混乱
            // 新方案：无论广播是否成功，都立即从 _simulatedEntities 移除实体。
            //   - 广播成功：其他客户端立即收到 Despawn delta，角色模型被销毁
            //   - 广播失败（无 fanout observer 或无其他在线玩家）：服务端立即下线，
            //     其他客户端可能暂时看不到 Despawn delta，但客户端有超时清理机制兜底
            //     （SnapshotApplySystem 会在实体长时间无更新时自动清理）
            // 同时保留失败计数器用于诊断，但不再用于决定是否移除实体。
            _simulatedEntities.Remove(entityId);

            if (broadcastOk)
            {
                _entityFailedDespawnAttempts.Remove(entityId);
                _logger.LogInformation(
                    "ZoneShard {ShardId}: 注销实体 {EntityId}（Despawn 已广播）。",
                    this.GetPrimaryKeyLong(), entityId);
            }
            else
            {
                // 广播失败：记录错误日志，但实体已移除（立即从服务端下线）
                _entityFailedDespawnAttempts.TryGetValue(entityId, out var failedAttempts);
                failedAttempts++;
                _entityFailedDespawnAttempts[entityId] = failedAttempts;

                _logger.LogError(
                    "ZoneShard {ShardId}: 实体 {EntityId} Despawn 广播失败（累计 {Attempts} 次），" +
                    "实体已立即从服务端移除（其他客户端可能暂时残留角色模型，由客户端超时清理机制兜底）。",
                    this.GetPrimaryKeyLong(), entityId, failedAttempts);
            }
        }
        else
        {
            // 修复 BUG：实体不存在时记录警告，便于诊断 Despawn 被跳过的情况。
            // 原实现静默返回，无法诊断"Despawn 被调用但实体已不存在"的场景（如重复注销、孤儿清理竞态等）。
            _logger.LogWarning(
                "ZoneShard {ShardId}: 注销实体 {EntityId} 失败 — 实体不在模拟表中（可能从未注册或已被清理）。",
                this.GetPrimaryKeyLong(), entityId);
        }
    }

    /// <summary>
    /// 兜底调用 <see cref="ICharacterGrain.GoOfflineAsync"/> 重置角色在线状态。<br/>
    /// 修复 BUG（两周未解决的核心根因）：当 <c>RegisterCharacter</c> 从未被调用
    /// （<c>GetCharacterIdsByConnection</c> 返回空，<c>DespawnImmediatelyAsync</c> 不被触发）时，
    /// <c>GoOfflineAsync</c> 可能从未被调用，导致 <c>CharacterGrain</c> 持久化状态
    /// <c>IsOnline</c> 永久卡在 true。本方法在实体从 <c>_simulatedEntities</c> 移除时兜底调用，
    /// 确保在线状态最终被重置。GoOfflineAsync 是幂等的，重复调用安全。
    /// </summary>
    private async Task TryGoOfflineAsync(ulong entityId)
    {
        try
        {
            var characterGrain = GrainFactory.GetGrain<ICharacterGrain>((long)entityId);
            await characterGrain.GoOfflineAsync().ConfigureAwait(false);
            _logger.LogInformation(
                "ZoneShard {ShardId}: 兜底调用 GoOfflineAsync 成功，实体 {EntityId} 在线状态已重置。",
                this.GetPrimaryKeyLong(), entityId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ZoneShard {ShardId}: 兜底调用 GoOfflineAsync 失败，实体 {EntityId} 在线状态可能未重置。",
                this.GetPrimaryKeyLong(), entityId);
        }
    }

    /// <inheritdoc />
    public Task<int> RenewLeaseAsync(ulong[] entityIds)
    {
        if (entityIds is null || entityIds.Length == 0)
            return Task.FromResult(0);

        var newExpiry = DateTime.UtcNow + EntityLeaseDuration;
        var renewed = 0;
        foreach (var entityId in entityIds)
            {
                // 注意：SimulatedEntity 是 struct，必须通过索引器引用修改字段
                if (_simulatedEntities.TryGetValue(entityId, out var entity))
                {
                    entity.LeaseExpiry = newExpiry;
                    _simulatedEntities[entityId] = entity;  // struct 赋值回字典
                    // 修复 BUG：不在 RenewLeaseAsync 中重置 _entityFailedDespawnAttempts。
                    // 原实现在续约时重置失败计数，导致已离线角色的实体被续约后（因 _characterConnections
                    // 残留映射），Despawn 失败计数被清零，强制移除兜底永远无法触发，实体永久残留。
                    // 失败计数只在 UnregisterEntityAsync 中广播成功时重置（第 806 行），
                    // 因为只有广播成功才证明 Despawn 已完成，失败计数不再需要。
                    renewed++;
                }
            }

        if (renewed > 0)
        {
            _logger.LogDebug(
                "ZoneShard {ShardId}: 续约 {Renewed}/{Total} 个实体，新过期时间 {Expiry:O}",
                this.GetPrimaryKeyLong(), renewed, entityIds.Length, newExpiry);
        }

        return Task.FromResult(renewed);
    }

    /// <inheritdoc />
    public Task<ulong[]> GetRegisteredEntityIdsAsync()
    {
        if (_simulatedEntities.Count == 0)
            return Task.FromResult(Array.Empty<ulong>());

        var ids = new ulong[_simulatedEntities.Count];
        var i = 0;
        foreach (var key in _simulatedEntities.Keys)
            ids[i++] = key;
        return Task.FromResult(ids);
    }

    /// <summary>
    /// 孤儿实体检测：扫描所有实体的 <see cref="SimulatedEntity.LeaseExpiry"/>，
    /// 清理超过租约期未续约的实体（网关崩溃/断线未清理的残留实体）。<br/>
    /// 清理时调用 <see cref="UnregisterEntityAsync"/>（广播 Despawn delta）+ <see cref="RemoveSessionAsync"/>（清理 AOI 订阅）。<br/>
    /// 这是兜底机制，确保任何情况下孤儿实体都不会永久残留。
    /// </summary>
    private async Task CleanupOrphanEntitiesAsync()
    {
        if (_simulatedEntities.Count == 0) return;

        var now = DateTime.UtcNow;
        var orphans = new List<ulong>();

        foreach (var kv in _simulatedEntities)
        {
            if (kv.Value.LeaseExpiry < now)
            {
                orphans.Add(kv.Key);
            }
        }

        if (orphans.Count == 0) return;

        _logger.LogWarning(
            "ZoneShard {ShardId}: 检测到 {Count} 个孤儿实体（租约过期未续约），开始清理。实体列表: [{EntityIds}]",
            this.GetPrimaryKeyLong(), orphans.Count, string.Join(", ", orphans));

        foreach (var orphanId in orphans)
        {
            try
            {
                // UnregisterEntityAsync 会从 _simulatedEntities 移除并广播 Despawn delta
                await UnregisterEntityAsync(orphanId).ConfigureAwait(false);
                // RemoveSessionAsync 清理该实体在 AOI 中的订阅（sessionId = entityId = characterId）
                // 注意：orphanId 是 ulong，而 sessionId 是 long，需要强制转换
                _aoi.RemoveSession((long)orphanId);

                // 兜底（两周未解决的核心根因）：如果实体已被移除（广播成功或强制移除），
                // 调用 GoOfflineAsync 重置 CharacterGrain 持久化状态 IsOnline。
                // 这确保即使 DespawnImmediatelyAsync 从未被调用（RegisterCharacter 未建立映射，
                // GetCharacterIdsByConnection 返回空），孤儿清理也能重置 IsOnline，
                // 防止持久化状态永久卡在 true。GoOfflineAsync 是幂等的，重复调用安全。
                if (!_simulatedEntities.ContainsKey(orphanId))
                {
                    await TryGoOfflineAsync(orphanId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ZoneShard {ShardId}: 清理孤儿实体 {EntityId} 失败。",
                    this.GetPrimaryKeyLong(), orphanId);
            }
        }

        _logger.LogInformation(
            "ZoneShard {ShardId}: 孤儿实体清理完成，共清理 {Count} 个。",
            this.GetPrimaryKeyLong(), orphans.Count);
    }

    /// <summary>
    /// 广播单实体的 Spawn/Despawn/Update 生命周期 delta，并可选择性给触发方 session 补发
    /// 当前所有已存在实体的 Spawn（仅 Spawn 且 includeNewSession=true 时）。
    /// 复用 <see cref="NotifyFanoutObserversAsync"/> 的 observer 推送路径。
    /// </summary>
    /// <param name="entityId">触发实体 ID。</param>
    /// <param name="x">实体位置 X。</param>
    /// <param name="y">实体位置 Y。</param>
    /// <param name="z">实体位置 Z。</param>
    /// <param name="kind">delta 种类（Spawn/Despawn）。</param>
    /// <param name="includeNewSession">是否给触发方 session 补发全场已存在实体的 Spawn（仅 Spawn 时有意义）。</param>
    /// <returns>
    /// true 表示实体应从 _simulatedEntities 移除（服务端立即下线）：
    ///   - Despawn 场景：无论是否成功广播，都返回 true，确保角色立即从服务端下线
    ///   - Spawn 场景：仅当存在 fanout observer 时返回 true
    /// false 表示实体不应被移除（仅 Spawn 场景：无 fanout observer 时不创建实体）。
    /// </returns>
    private async Task<bool> BroadcastEntityLifecycleAsync(ulong entityId, float x, float y, float z, EntityDeltaKind kind, bool includeNewSession)
    {
        if (_fanoutObservers.Count == 0)
        {
            // 修复 BUG（角色离线应立即从服务端下线）：
            // 原实现在无 fanout observer 时返回 false，导致 Despawn 时实体被保留等待重试，
            // 角色在服务端残留。这与"立即从服务端下线"的需求矛盾。
            // 新方案：
            //   - Despawn：返回 true，让实体立即从 _simulatedEntities 移除（服务端立即下线）
            //     其他客户端可能暂时残留角色模型，由客户端超时清理机制兜底
            //   - Spawn：返回 false，不创建实体（Spawn 需要确认 fanout 订阅链路正常）
            if (kind == EntityDeltaKind.Despawn)
            {
                _logger.LogError(
                    "ZoneShard {ShardId}: Despawn delta 未广播（无 fanout observer）！实体 {EntityId} 仍立即从服务端移除（其他客户端可能暂时残留角色模型）。",
                    this.GetPrimaryKeyLong(), entityId);
                return true;
            }
            else
            {
                LogNoObserverWarn("BroadcastEntityLifecycle");
                return false;
            }
        }

        // 1) 给触发方 session 之外的所有订阅者广播触发实体的 Spawn/Despawn。
        var triggerEntityYaw = _simulatedEntities.TryGetValue(entityId, out var triggerEntity) ? triggerEntity.Yaw : 0f;
        var triggerDelta = BuildEntityDelta(entityId, x, y, z, kind, triggerEntityYaw);
        var triggerChunkKey = WorldCoord.ToChunkMortonKey(x, y, z);
        var triggerDiff = BuildChunkDiff(new[] { triggerDelta }, triggerChunkKey, _tickCount);

        // 先查 AOI 订阅者（按实体所在 chunk），再剔除触发方（仅 Spawn 时新玩家尚未完成自身初始化，
        // 自身的 Spawn delta 会由客户端 IsLocalPlayer 路径处理；Despawn 时触发方已离线，也应剔除）。
        var allSubs = _aoi.GetSubscribers(triggerDiff.ChunkMortonKey);
        var broadcastSessionIds = allSubs.Where(s => (ulong)s != entityId).Select(s => s).ToArray();
        if (broadcastSessionIds.Length > 0)
        {
            var observers = GetObserversSnapshot();
            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(triggerDiff, broadcastSessionIds).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "ZoneShard {ShardId}: 广播 {Kind} 到观察者 {SubscriptionId} 失败（已吞）。",
                        this.GetPrimaryKeyLong(), kind, subscriptionId);
                }
            }
        }

        // 1.5) 跨 chunk 可见性修复：Spawn 与 Despawn 均需走全广播。
        // Spawn：trigger chunk 订阅者只覆盖了新玩家附近的观察者，其他 chunk 内的已在线玩家
        //       不会订阅 trigger chunk，因而收不到新玩家的 Spawn 通知。
        // Despawn：离线玩家被注销时，其所在 chunk 可能已无其他订阅者（玩家断线后 AOI 订阅
        //         可能已被清理，或附近本就无其他玩家订阅该 chunk），导致 AOI 过滤后 broadcastSessionIds
        //         为空，Despawn delta 永远无法送达其他客户端，离线角色模型在其他客户端永久残留。
        //         因此 Despawn 必须使用 GetAllSubscribers 全广播而非 AOI 过滤。
        // 这里绕过 AOI chunk 过滤，向当前 World 内所有活跃 session（剔除触发方自身）再推一次 delta。
        // 与第 1 步可能产生重复推送，客户端 SnapshotApplySystem.HandleSpawn/HandleDespawn 已按 EntityId 做幂等处理。
        if (kind == EntityDeltaKind.Spawn || kind == EntityDeltaKind.Despawn)
        {
            var allSubscribersBeforeFilter = _aoi.GetAllSubscribers();
            var allOtherSessionIds = allSubscribersBeforeFilter
                .Where(s => (ulong)s != entityId)
                .ToArray();
            // 诊断日志（Despawn 丢失 BUG）：记录全广播的目标 session 列表，便于确认 B 是否在订阅列表中。
            // 如果 allOtherSessionIds 不包含 B 的 characterId，说明 B 的 AOI 订阅在 A 离线时已丢失。
            _logger.LogWarning(
                "ZoneShard {ShardId}: [Despawn诊断] {Kind} 全广播。EntityId={EntityId}, TriggerChunk={ChunkKey}, " +
                "AllSubscribers=[{AllSubs}], FilteredTargets=[{Targets}], ObserverCount={ObserverCount}",
                this.GetPrimaryKeyLong(), kind, entityId, triggerChunkKey,
                string.Join(",", allSubscribersBeforeFilter), string.Join(",", allOtherSessionIds),
                _fanoutObservers.Count);

            // 修复 BUG（角色离线应立即从服务端下线）：
            // 原实现在无其他在线玩家时返回 false，导致实体被保留等待重试，角色在服务端残留。
            // 这与"立即从服务端下线"的需求矛盾。
            // 新方案：无其他在线玩家时返回 true（不需要广播，但实体应立即移除）。
            //   - 当前无其他在线玩家 → 不需要广播 Despawn delta
            //   - 实体应立即从 _simulatedEntities 移除（由 UnregisterEntityAsync 处理）
            //   - 后续有其他玩家上线时，他们不会看到该离线角色（因为实体已移除，不会收到 Spawn delta）
            if (kind == EntityDeltaKind.Despawn && allOtherSessionIds.Length == 0)
            {
                _logger.LogInformation(
                    "ZoneShard {ShardId}: [Despawn] 全广播目标为空（当前无其他在线玩家），" +
                    "EntityId={EntityId} 实体将立即移除（无需广播 Despawn delta）。",
                    this.GetPrimaryKeyLong(), entityId);
                return true;
            }

            if (allOtherSessionIds.Length > 0)
            {
                var observers = GetObserversSnapshot();
                foreach (var (subscriptionId, observer) in observers)
                {
                    try
                    {
                        await observer.OnChunkDiffAsync(triggerDiff, allOtherSessionIds).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "ZoneShard {ShardId}: 跨 chunk 广播 {Kind} 到观察者 {SubscriptionId} 失败（已吞）。",
                            this.GetPrimaryKeyLong(), kind, subscriptionId);
                    }
                }
            }
            else
            {
                // 仅 Spawn 可能走到这里：当前无其他玩家，但新玩家自己的 Spawn 通过 includeNewSession 分支补发。
                _logger.LogWarning(
                    "ZoneShard {ShardId}: [Spawn诊断] Spawn 全广播目标为空。EntityId={EntityId}, " +
                    "AllSubscribersCount={AllSubsCount}。当前场景无其他玩家在线。",
                    this.GetPrimaryKeyLong(), entityId, allSubscribersBeforeFilter.Count);
            }
        }

        // 2) 仅 Spawn：给触发方 session 补发当前所有已存在实体的 Spawn（含触发方自己），
        // 让新玩家立即看到已在场的其他角色，并收到自己的 Spawn delta 以在客户端创建本地玩家实体
        // （SnapshotApplySystem.HandleSpawn 依赖 Identity.OwnerId == LocalPlayerOwnerId 标记 IsLocalPlayer）。
        // 原逻辑跳过触发方自己且要求 _simulatedEntities.Count > 1，导致新玩家永远收不到自己的 Spawn，
        // 客户端本地玩家实体无法创建，InputSendSystem 查询不到实体，上行 InputPacket 链路断裂。
        if (kind == EntityDeltaKind.Spawn && includeNewSession)
        {
            var existingDeltas = new List<EntityDelta>();
            foreach (var kv in _simulatedEntities)
            {
                var e = kv.Value;
                existingDeltas.Add(BuildEntityDelta(kv.Key, e.X, e.Y, e.Z, EntityDeltaKind.Spawn, e.Yaw));
            }

            if (existingDeltas.Count > 0)
            {
                // 补发全场 Spawn 仅发给新 session，不经过 AOI 过滤；使用触发实体所在 chunk 作为 mortonKey 占位。
                var initialDiff = BuildChunkDiff(existingDeltas.ToArray(), triggerChunkKey, _tickCount);
                var newSessionIds = new long[] { (long)entityId };
                var observers = GetObserversSnapshot();
                foreach (var (subscriptionId, observer) in observers)
                {
                    try
                    {
                        await observer.OnChunkDiffAsync(initialDiff, newSessionIds).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex,
                            "ZoneShard {ShardId}: 给新 session {EntityId} 补发全场 Spawn 到观察者 {SubscriptionId} 失败（已吞）。",
                            this.GetPrimaryKeyLong(), entityId, subscriptionId);
                    }
                }
            }
        }

        return true;
    }

    /// <summary>构造单实体 delta（Spawn/Despawn/Update），统一填充 Identity + Transform。</summary>
    /// <remarks>输入 x/y/z 为 ECS Z-up 坐标（Y=前后, Z=上下），输出 Transform 转换为 Flax Y-up（Y=上下, Z=前后）。</remarks>
    private static EntityDelta BuildEntityDelta(ulong entityId, float x, float y, float z, EntityDeltaKind kind, float yaw = 0f)
    {
        return new EntityDelta
        {
            EntityId = entityId,
            Kind = kind,
            Identity = new NetworkIdentityAuthComponent
            {
                NetworkId = entityId,
                EntityType = 0,
                OwnerId = entityId,
            },
            Transform = new AuthTransformComponent
            {
                X = x,       // 左右（不变）
                Y = z,       // 上下（ECS Z → Flax Y）
                Z = y,       // 前后（ECS Y → Flax Z）
                Pitch = 0f,
                Yaw = yaw,
                Roll = 0f,
            },
        };
    }

    /// <summary>把 EntityDelta[] 序列化为 WorldChunkDiffPacket，使用传入的 chunkMortonKey 做 AOI 过滤。</summary>
    /// <param name="deltas">要下发的实体增量数组。</param>
    /// <param name="chunkMortonKey">目标 chunk 的 Morton 键。</param>
    /// <param name="serverTick">当前服务端 tick（用于客户端 baseline 跟踪）。0 表示未知。</param>
    private static WorldChunkDiffPacket BuildChunkDiff(EntityDelta[] deltas, ulong chunkMortonKey, long serverTick = 0)
    {
        return new WorldChunkDiffPacket
        {
            ChunkMortonKey = chunkMortonKey,
            DiffSeqEnd = serverTick,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(deltas),
            PayloadCompressed = false,
            PayloadType = WorldChunkDiffPayloadType.EntityDelta,
        };
    }

    /// <summary>
    /// 查找实体所在 chunk 的 Morton 键（P8-8.2）。
    /// 根据 entityId 从 <see cref="_simulatedEntities"/> 查位置并换算为 Morton 键。
    /// 若实体不在模拟表中（例如静态可交互对象），返回 null，调用方应回退到广播给全部订阅者。
    /// </summary>
    /// <param name="entityId">实体 ID（long，兼容 InteractionSyncPacket.InteractableId 等 long 字段）。</param>
    /// <returns>Morton 键；实体不存在时返回 null。</returns>
    private ulong? GetChunkMortonKeyForEntity(long entityId)
    {
        if (_simulatedEntities.TryGetValue((ulong)entityId, out var entity))
        {
            return WorldCoord.ToChunkMortonKey(entity.X, entity.Y, entity.Z);
        }
        return null;
    }

    /// <inheritdoc />
    public Task SubmitInputAsync(ulong entityId, InputPacket input, float reportedEndX, float reportedEndY, float reportedEndZ)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 提交输入到未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return Task.CompletedTask;
        }

        entity.PendingInputs.Add(input);
        // 修复（坐标系不匹配）：客户端上报的 PredictedEndX/Y/Z 是 Flax Y-up 坐标，
        // 但 MovementValidator.Validate 的 clientEnd 参数期望 ECS Z-up 坐标。
        // 转换：ECS.X = Flax.X, ECS.Y = Flax.Z(前后), ECS.Z = Flax.Y(上下)
        entity.ReportedEndX = reportedEndX;
        entity.ReportedEndY = reportedEndZ;  // Flax Z (前后) → ECS Y
        entity.ReportedEndZ = reportedEndY;  // Flax Y (上下) → ECS Z
        _simulatedEntities[entityId] = entity;

        return Task.CompletedTask;
    }

    public Task SubmitSkillCastAsync(ulong entityId, int skillId, ulong targetId)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 提交技能施放到未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return Task.CompletedTask;
        }

        entity.IsCasting = true;
        entity.CastSkillId = skillId;
        entity.CastStartTime = _tickCount;
        entity.CastTargetId = targetId;
        _simulatedEntities[entityId] = entity;

        return Task.CompletedTask;
    }

    public async Task CompleteSkillCastAsync(ulong entityId, float damage, ulong targetId, bool isCritical)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 完成技能施放时未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return;
        }

        var skillId = entity.CastSkillId;

        entity.IsCasting = false;
        entity.CastSkillId = 0;
        entity.CastTargetId = 0;
        entity.CastStartTime = 0;
        _simulatedEntities[entityId] = entity;

        var events = new List<SyncEvent>();

        events.Add(new SyncEvent
        {
            Kind = SyncEventKind.SkillCast,
            SourceEntityId = entityId,
            TargetEntityId = targetId,
            IntValue = skillId,
            FloatValue = 0f,
        });

        if (damage > 0)
        {
            events.Add(new SyncEvent
            {
                Kind = SyncEventKind.Damage,
                SourceEntityId = entityId,
                TargetEntityId = targetId,
                IntValue = (int)damage,
                FloatValue = isCritical ? 1f : 0f,
            });
        }

        if (_simulatedEntities.ContainsKey(targetId) && damage > 0)
        {
            var targetEntity = _simulatedEntities[targetId];
            if (targetEntity.Hp > 0 && targetEntity.Hp - (int)damage <= 0)
            {
                events.Add(new SyncEvent
                {
                    Kind = SyncEventKind.Death,
                    SourceEntityId = entityId,
                    TargetEntityId = targetId,
                    IntValue = 0,
                    FloatValue = 0f,
                });
            }
        }

        var eventPacket = new EventPacket
        {
            ServerTick = _tickCount,
            Events = events.ToArray(),
        };

        await BroadcastEventAsync(eventPacket).ConfigureAwait(false);
    }

    /// <summary>
    /// 广播快照和校正包到已注册的 fanout 观察者。
    /// </summary>
    /// <param name="snapshot">要广播的快照包。</param>
    /// <param name="corrections">校正包列表。</param>
    /// <param name="bypassAoiFilter">全量快照时绕过 per-chunk AOI 过滤，直接广播到所有订阅者。
    /// 修复根因：新玩家加入时仅订阅了出生点附近的 chunk，全量快照中其他 chunk 的实体
    /// 被 per-chunk AOI 过滤静默丢弃，导致新玩家看不到远处的已有角色，已有角色也看不到新玩家
    /// （因为已有玩家的订阅集不一定覆盖新玩家所在 chunk）。全量快照绕过 AOI 确保双向可见。</param>
    private async Task BroadcastSnapshotAsync(SnapshotPacket snapshot, List<CorrectionPacket> corrections, bool bypassAoiFilter = false)
    {
        // 诊断：前 5 次调用无条件输出详情，定位 fanout 断点（_fanoutObservers 为空？AOI 无订阅者？）
        _broadcastDiagCount++;
        if (_broadcastDiagCount <= 5)
        {
            var firstDeltaChunkKey = 0UL;
            var firstDeltaEntityId = 0UL;
            var firstDeltaTransform = "null";
            if (snapshot.Deltas.Length > 0)
            {
                var d0 = snapshot.Deltas[0];
                firstDeltaEntityId = d0.EntityId;
                if (d0.Transform is { } t0)
                {
                    firstDeltaChunkKey = WorldCoord.ToChunkMortonKey(t0.X, t0.Y, t0.Z);
                    firstDeltaTransform = $"X={t0.X:F2},Y={t0.Y:F2},Z={t0.Z:F2}";
                }
            }
            _logger.LogWarning(
                "[ZoneShardGrain 诊断#{N}] BroadcastSnapshot 入口：Tick={Tick}, Deltas={DeltaCount}, Observers={ObserverCount}, Sessions={SessionCount}, Chunks={ChunkCount}, Entities={EntityCount}, FirstDeltaEntity={EntityId}, FirstDeltaChunkKey=0x{ChunkKey:X16}, FirstDeltaTransform={Transform}",
                _broadcastDiagCount, _tickCount, snapshot.Deltas.Length, _fanoutObservers.Count, _aoi.SessionCount, _aoi.ChunkCount, _simulatedEntities.Count, firstDeltaEntityId, firstDeltaChunkKey, firstDeltaTransform);
        }

        if (_fanoutObservers.Count == 0)
        {
            LogNoObserverWarn("BroadcastSnapshot");
            return;
        }

        var observers = GetObserversSnapshot();

        // 诊断：每 60 tick（约 1 秒）输出一次广播状态，确认链路联通
        var isDiagnosticTick = _tickCount % 60 == 0;
        if (isDiagnosticTick)
        {
            _logger.LogInformation(
                "ZoneShard {ShardId}: BroadcastSnapshot 诊断 — Deltas={DeltaCount}, Observers={ObserverCount}, Sessions={SessionCount}, Chunks={ChunkCount}, Entities={EntityCount}",
                this.GetPrimaryKeyLong(), snapshot.Deltas.Length, observers.Length, _aoi.SessionCount, _aoi.ChunkCount, _simulatedEntities.Count);
        }

        // Task 11：按 chunkKey 分组聚合 delta，每个 chunk 一次性序列化推送（N×M RPC → K×M RPC）
        _deltaByChunkBuffer.Clear();
        foreach (var delta in snapshot.Deltas)
        {
            ulong? chunkKey = null;
            if (delta.Transform is { } t)
            {
                // delta.Transform 是 AuthTransformComponent，使用 Flax Y-up 坐标系（X=左右, Y=上下, Z=前后）。
                // AOI 订阅侧（SyncPacketHandler.HandleHandshakeAsync）使用 ECS Z-up 坐标系（X=左右, Y=前后, Z=上下），
                // WorldCoord.ToChunkMortonKey 不做坐标系转换，因此这里必须把 Flax Y-up 还原为 ECS Z-up（Y/Z 互换），
                // 否则 Morton 编码与订阅侧不一致，GetSubscribers 始终返回空，Update delta 全部被静默丢弃，
                // 导致"远程角色 Spawn 能看到但移动/旋转/跳跃不同步"。
                chunkKey = WorldCoord.ToChunkMortonKey(t.X, t.Z, t.Y);
            }
            else
            {
                // Transform 缺失时回退查模拟实体表
                chunkKey = GetChunkMortonKeyForEntity((long)delta.EntityId);
            }

            var effectiveChunkKey = chunkKey ?? 0UL;
            if (!_deltaByChunkBuffer.TryGetValue(effectiveChunkKey, out var chunkDeltaList))
            {
                chunkDeltaList = new List<EntityDelta>();
                _deltaByChunkBuffer[effectiveChunkKey] = chunkDeltaList;
            }
            chunkDeltaList.Add(delta);
        }

        // 按每个 chunk 一次性序列化推送
        foreach (var kv in _deltaByChunkBuffer)
        {
            var effectiveChunkKey = kv.Key;
            var chunkDeltas = kv.Value;

            // 获取该 chunk 的所有订阅者（chunkKey==0 表示无法定位 chunk，回退广播到全部订阅者）
            // 修复：全量快照时绕过 per-chunk AOI 过滤，直接广播到所有订阅者。
            // 根因：新玩家仅订阅出生点附近 chunk，全量快照中其他 chunk 的实体被静默丢弃，
            // 导致新玩家看不到远处已有角色，已有角色也看不到新玩家（双向不可见）。
            IReadOnlyCollection<long> sessionIds;
            if (bypassAoiFilter)
            {
                sessionIds = _aoi.GetAllSubscribers();
            }
            else if (effectiveChunkKey != 0UL)
            {
                sessionIds = _aoi.GetSubscribers(effectiveChunkKey);
            }
            else
            {
                if (_broadcastDiagCount <= 5)
                {
                    _logger.LogWarning(
                        "[ZoneShardGrain 诊断#{N}] BroadcastSnapshot 回退广播到全部订阅者 — ChunkKey=0x{ChunkKey:X16}",
                        _broadcastDiagCount, effectiveChunkKey);
                }
                sessionIds = _aoi.GetAllSubscribers();
            }

            if (sessionIds.Count == 0)
            {
                // 诊断：前 5 次无条件输出，定位 AOI 无订阅者问题
                if (_broadcastDiagCount <= 5)
                {
                    _logger.LogWarning(
                        "[ZoneShardGrain 诊断#{N}] BroadcastSnapshot 跳过 chunk — ChunkKey=0x{ChunkKey:X16}, 无 AOI 订阅者（Sessions={SessionCount}, Chunks={ChunkCount}）",
                        _broadcastDiagCount, effectiveChunkKey, _aoi.SessionCount, _aoi.ChunkCount);
                }
                else if (isDiagnosticTick)
                {
                    _logger.LogWarning(
                        "ZoneShard {ShardId}: BroadcastSnapshot 跳过 chunk — ChunkKey=0x{ChunkKey:X16}, 无 AOI 订阅者",
                        this.GetPrimaryKeyLong(), effectiveChunkKey);
                }
                continue;
            }

            // 诊断：前 5 次输出推送详情
            if (_broadcastDiagCount <= 5)
            {
                _logger.LogWarning(
                    "[ZoneShardGrain 诊断#{N}] BroadcastSnapshot 推送 chunk — ChunkKey=0x{ChunkKey:X16}, DeltaCount={DeltaCount}, SessionCount={SessionCount}, ObserverCount={ObserverCount}",
                    _broadcastDiagCount, effectiveChunkKey, chunkDeltas.Count, sessionIds.Count, observers.Length);
            }

            // 一次性序列化该 chunk 的所有 delta
            var chunkDeltaArray = chunkDeltas.ToArray();
            var diff = new WorldChunkDiffPacket
            {
                ChunkMortonKey = effectiveChunkKey,
                DiffSeqStart = _tickCount,
                DiffSeqEnd = _tickCount,
                Payload = MemoryPack.MemoryPackSerializer.Serialize(chunkDeltaArray),
                PayloadType = WorldChunkDiffPayloadType.EntityDelta,
            };

            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(diff, sessionIds).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "ZoneShard {ShardId}: 推送 snapshot 到观察者 {SubscriptionId} 失败（已吞）。",
                        this.GetPrimaryKeyLong(), subscriptionId);
                }
            }
        }

        foreach (var correction in corrections)
        {
            // P8-8.2：根据校正目标实体查 chunk key
            var correctionChunkKey = GetChunkMortonKeyForEntity((long)correction.EntityId);
            IReadOnlyCollection<long> correctionSessionIds;
            if (correctionChunkKey.HasValue)
            {
                correctionSessionIds = _aoi.GetSubscribers(correctionChunkKey.Value);
            }
            else
            {
                _logger.LogWarning(
                    "ZoneShard {ShardId}: BroadcastSnapshot correction 无法定位实体 {EntityId} 的 chunk，回退广播到全部订阅者。",
                    this.GetPrimaryKeyLong(), correction.EntityId);
                correctionSessionIds = _aoi.GetAllSubscribers();
            }
            if (correctionSessionIds.Count == 0) continue;

            var eventPacket = new EventPacket
            {
                ServerTick = _tickCount,
                Events = new[]
                {
                    new SyncEvent
                    {
                        Kind = SyncEventKind.Correction,
                        SourceEntityId = correction.EntityId,
                        IntValue = (int)correction.Reason,
                        FloatValue = correction.DriftMeters,
                        Payload = MemoryPack.MemoryPackSerializer.Serialize(correction),
                    },
                },
            };

            var diff = new WorldChunkDiffPacket
            {
                ChunkMortonKey = correctionChunkKey ?? 0,
                DiffSeqStart = _tickCount,
                DiffSeqEnd = _tickCount,
                Payload = MemoryPack.MemoryPackSerializer.Serialize(eventPacket),
                PayloadType = WorldChunkDiffPayloadType.Event,
            };

            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(diff, correctionSessionIds).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(
                        ex,
                        "ZoneShard {ShardId}: 推送 correction 到观察者 {SubscriptionId} 失败（已吞）。",
                        this.GetPrimaryKeyLong(), subscriptionId);
                }
            }
        }
    }

    /// <summary>
    /// 限频输出"无 fanout 观察者"警告（每 10 秒最多一次），避免刷屏。
    /// </summary>
    /// <param name="operation">触发广播的操作名（如 BroadcastSnapshot / BroadcastEntityLifecycle）。</param>
    private void LogNoObserverWarn(string operation)
    {
        var now = DateTime.UtcNow;
        if (now - _lastNoObserverWarnUtc < NoObserverWarnInterval)
            return;

        _lastNoObserverWarnUtc = now;
        _logger.LogWarning(
            "[ZoneShardGrain] {Operation} skipped: no fanout observers subscribed (shard={ShardKey}, sessions={SessionCount}, chunks={ChunkCount}, entities={EntityCount})",
            operation,
            this.GetPrimaryKeyLong(),
            _aoi.SessionCount,
            _aoi.ChunkCount,
            _simulatedEntities.Count);
    }

    private async Task BroadcastEventAsync(EventPacket eventPacket)
    {
        if (_fanoutObservers.Count == 0)
            return;

        // P8-8.2：根据首个事件的 SourceEntityId 查 chunk key，修复硬编码 0 导致 AOI 失效。
        var sourceEntityId = eventPacket.Events.Length > 0
            ? (long)eventPacket.Events[0].SourceEntityId
            : 0;
        var chunkKey = GetChunkMortonKeyForEntity(sourceEntityId);

        IReadOnlyCollection<long> sessionIds;
        if (chunkKey.HasValue)
        {
            sessionIds = _aoi.GetSubscribers(chunkKey.Value);
        }
        else
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: BroadcastEvent 无法定位源实体 {EntityId} 的 chunk，回退广播到全部订阅者。",
                this.GetPrimaryKeyLong(), sourceEntityId);
            sessionIds = _aoi.GetAllSubscribers();
        }

        if (sessionIds.Count == 0) return;

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = chunkKey ?? 0,
            DiffSeqStart = _tickCount,
            DiffSeqEnd = _tickCount,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(eventPacket),
            PayloadType = WorldChunkDiffPayloadType.Event,
        };

        var observers = GetObserversSnapshot();
        foreach (var (subscriptionId, observer) in observers)
        {
            try
            {
                await observer.OnChunkDiffAsync(diff, sessionIds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "ZoneShard {ShardId}: 推送 event 到观察者 {SubscriptionId} 失败（已吞）。",
                    this.GetPrimaryKeyLong(), subscriptionId);
            }
        }
    }

    /// <inheritdoc />
    public async Task GenerateInteractionSync(int slotIdx, long interactableId, long interactorId, byte stateBits, long serverTick)
    {
        // P8-8.5：交互槽状态转换校验，防止重复占用 / 非法释放。
        var slotKey = (interactableId, slotIdx);

        if ((stateBits & InteractionStateStart) != 0)
        {
            // Start：校验槽位是否空闲
            if (_interactionSlots.TryGetValue(slotKey, out var existing)
                && existing.interactorId != interactorId
                && (existing.stateBits & (InteractionStateEnd | InteractionStateStolen)) == 0)
            {
                // 槽位已被其他交互者占用且未结束
                _logger.LogWarning(
                    "ZoneShard {ShardId}: 拒绝交互同步 — 槽位 ({InteractableId},{SlotIdx}) 已被 {ExistingInteractorId} 占用（stateBits=0x{ExistingState:X2}），当前请求者 {InteractorId}。",
                    this.GetPrimaryKeyLong(), interactableId, slotIdx, existing.interactorId, existing.stateBits, interactorId);
                return;
            }
            // 占用/更新槽位
            _interactionSlots[slotKey] = (interactorId, stateBits);
        }
        else if ((stateBits & (InteractionStateEnd | InteractionStateStolen)) != 0)
        {
            // End 或 Stolen：清理槽位
            if (_interactionSlots.TryGetValue(slotKey, out var existing))
            {
                if (existing.interactorId != interactorId && (stateBits & InteractionStateStolen) == 0)
                {
                    // 非抢占式结束但请求者与占用者不一致
                    _logger.LogWarning(
                        "ZoneShard {ShardId}: 交互结束请求者 {InteractorId} 与槽位 ({InteractableId},{SlotIdx}) 占用者 {ExistingInteractorId} 不一致。",
                        this.GetPrimaryKeyLong(), interactorId, interactableId, slotIdx, existing.interactorId);
                }
                _interactionSlots.Remove(slotKey);
            }
        }

        var packet = new InteractionSyncPacket
        {
            SlotIdx = slotIdx,
            InteractableId = interactableId,
            InteractorId = interactorId,
            StateBits = stateBits,
            ServerTick = serverTick != 0 ? serverTick : _tickCount,
        };

        await BroadcastInteractionSyncAsync(packet).ConfigureAwait(false);
    }

    /// <summary>
    /// Task B.5.4：触发动画 Montage 事件（开始/结束），写入待下发队列，由下次 TickAsync 下发。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="montageId">Montage 资源 ID（0 表示停止当前 Montage）。</param>
    /// <param name="animInstanceId">动画实例 ID。</param>
    /// <param name="playRate">播放速率。</param>
    /// <param name="isLooping">是否循环。</param>
    public Task TriggerMontageAsync(ulong entityId, uint montageId, uint animInstanceId, float playRate = 1f, bool isLooping = false)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: TriggerMontage 到未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return Task.CompletedTask;
        }

        entity.PendingAnimationEvents ??= new Queue<AnimationStateAuthComponent>();
        entity.PendingAnimationEvents.Enqueue(new AnimationStateAuthComponent
        {
            AnimMontageId = montageId,
            AnimInstanceId = animInstanceId,
            PlayRate = playRate,
            TimePosition = 0f,
            IsLooping = isLooping,
        });

        // 更新实体当前动画状态
        entity.AnimMontageId = montageId;
        entity.AnimInstanceId = animInstanceId;
        entity.PlayRate = playRate;
        entity.IsLooping = isLooping;

        _simulatedEntities[entityId] = entity;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Task B.5.3：更新角色扩展属性（Mana/Level/Exp/Stamina 等），变化将在下次 TickAsync 通过 EntityState 下发。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="mana">当前法力值（-1 表示不更新）。</param>
    /// <param name="maxMana">最大法力值（-1 表示不更新）。</param>
    /// <param name="level">等级（-1 表示不更新）。</param>
    /// <param name="exp">经验值（-1 表示不更新）。</param>
    /// <param name="stamina">当前体力值（-1 表示不更新）。</param>
    /// <param name="maxStamina">最大体力值（-1 表示不更新）。</param>
    /// <param name="hp">当前生命值（-1 表示不更新）。</param>
    /// <param name="maxHp">最大生命值（-1 表示不更新）。</param>
    /// <param name="stateBits">状态位掩码（uint.MaxValue 表示不更新）。</param>
    public Task UpdateCharacterAttributesAsync(
        ulong entityId,
        int mana = -1, int maxMana = -1,
        int level = -1, long exp = -1,
        int stamina = -1, int maxStamina = -1,
        int hp = -1, int maxHp = -1,
        uint stateBits = uint.MaxValue)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: UpdateCharacterAttributes 到未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return Task.CompletedTask;
        }

        if (mana >= 0) entity.Mana = mana;
        if (maxMana >= 0) entity.MaxMana = maxMana;
        if (level >= 0) entity.Level = level;
        if (exp >= 0) entity.Exp = exp;
        if (stamina >= 0) entity.Stamina = stamina;
        if (maxStamina >= 0) entity.MaxStamina = maxStamina;
        if (hp >= 0) entity.Hp = hp;
        if (maxHp >= 0) entity.MaxHp = maxHp;
        if (stateBits != uint.MaxValue) entity.EntityStateBits = stateBits;

        _simulatedEntities[entityId] = entity;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Task B.5.2：更新角色移动状态（移动模式/水平速度），由移动校验或技能击退等逻辑调用。
    /// </summary>
    /// <param name="entityId">目标实体 ID。</param>
    /// <param name="mode">移动模式。</param>
    /// <param name="velX">水平速度 X。</param>
    /// <param name="velY">水平速度 Y。</param>
    public Task UpdateMovementStateAsync(ulong entityId, MovementMode mode, float velX, float velY)
    {
        if (!_simulatedEntities.TryGetValue(entityId, out var entity))
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: UpdateMovementState 到未知实体 {EntityId}（已忽略）。",
                this.GetPrimaryKeyLong(), entityId);
            return Task.CompletedTask;
        }

        entity.MovementMode = mode;
        entity.VelocityXZ_X = velX;
        entity.VelocityXZ_Y = velY;
        _simulatedEntities[entityId] = entity;
        return Task.CompletedTask;
    }

    // ===========================================================================
    // Task C.4：场景对象状态管理
    // ===========================================================================

    /// <inheritdoc />
    public async Task<bool> HandleSceneObjectInteract(ulong interactorId, ulong objectId, uint intentBits)
    {
        // 校验：interactorId 非 0
        if (interactorId == 0)
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 场景对象交互校验失败 — interactorId 为 0。ObjectId={ObjectId}",
                this.GetPrimaryKeyLong(), objectId);
            return false;
        }

        if (objectId == 0)
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 场景对象交互校验失败 — objectId 为 0。InteractorId={InteractorId}",
                this.GetPrimaryKeyLong(), interactorId);
            return false;
        }

        // 查找或创建场景对象状态
        if (!_sceneObjectStates.TryGetValue(objectId, out var state))
        {
            // 未注册的对象：首次交互时创建默认状态（ObjectType=Chest, StateBits=0）
            state = new SceneObjectStateData
            {
                ObjectId = objectId,
                ShardKey = this.GetPrimaryKeyLong(),
                ObjectType = SceneObjectType.Chest,
                StateBits = 0,
                CooldownEndTick = 0,
                OwnerCharacterId = 0,
            };
            _sceneObjectStates[objectId] = state;
        }

        // Task C.4.2：校验冷却（对比 CooldownEndTick 与当前 tick）
        if (state.CooldownEndTick > _tickCount)
        {
            _logger.LogDebug(
                "ZoneShard {ShardId}: 场景对象 {ObjectId} 冷却中，剩余 {Remaining} tick。InteractorId={InteractorId}",
                this.GetPrimaryKeyLong(), objectId, state.CooldownEndTick - _tickCount, interactorId);
            return false;
        }

        // Task C.4.2：校验归属（OwnerCharacterId 为 0 或等于 interactorId）
        if (state.OwnerCharacterId != 0 && state.OwnerCharacterId != interactorId)
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 场景对象 {ObjectId} 归属不匹配 — 当前归属 {Owner}, 请求者 {Interactor}",
                this.GetPrimaryKeyLong(), objectId, state.OwnerCharacterId, interactorId);
            return false;
        }

        // Task C.4.2：校验状态合法性（intentBits 仅允许低 4 位，且不能同时设置互斥位）
        // 简单策略：intentBits 直接作为新的 StateBits，但需过滤非法位
        uint newStateBits = intentBits & SceneObjectStateBits.StateMask;
        if (newStateBits == 0)
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 场景对象 {ObjectId} 状态位为 0（无有效意图）。InteractorId={InteractorId}",
                this.GetPrimaryKeyLong(), objectId, interactorId);
            return false;
        }

        // 更新状态
        state.StateBits = newStateBits;
        state.OwnerCharacterId = interactorId;
        // 默认冷却 300 tick（约 5 秒 @ 60Hz）；宝箱开启/门激活等关键事件由调用方覆盖
        state.CooldownEndTick = _tickCount + 300;
        state.UpdatedAt = DateTime.UtcNow;
        _sceneObjectStates[objectId] = state;

        // 构造 SceneObjectSyncPacket 并广播
        var packet = new SceneObjectSyncPacket
        {
            ObjectId = objectId,
            StateBits = state.StateBits,
            CooldownEndTick = state.CooldownEndTick,
            OwnerCharacterId = state.OwnerCharacterId,
            HasTransform = false,
            ServerTick = _tickCount,
        };

        await BroadcastSceneObjectSyncAsync(packet, objectId).ConfigureAwait(false);

        // Task C.5.5：关键事件即时落盘（宝箱开启/任务门激活等状态位变化）
        if (_sceneObjectPersistence is not null &&
            (SceneObjectStateBits.HasOpened(newStateBits) || SceneObjectStateBits.HasActivated(newStateBits)))
        {
            _ = SaveSingleSceneObjectStateAsync(state);
        }

        _logger.LogInformation(
            "ZoneShard {ShardId}: 场景对象 {ObjectId} 交互成功。InteractorId={InteractorId}, StateBits=0x{StateBits:X2}",
            this.GetPrimaryKeyLong(), objectId, interactorId, newStateBits);

        return true;
    }

    /// <summary>
    /// Task C.5.5：单个场景对象状态即时落盘（fire-and-forget，异常不回传给调用方）。
    /// </summary>
    private async Task SaveSingleSceneObjectStateAsync(SceneObjectStateData state)
    {
        if (_sceneObjectPersistence is null) return;
        try
        {
            await _sceneObjectPersistence.SaveSingleAsync(state.ShardKey, state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ZoneShard {ShardId}: 场景对象 {ObjectId} 即时落盘失败。",
                state.ShardKey, state.ObjectId);
        }
    }

    // ===========================================================================
    // Task D.3：Snapshot 增量压缩
    // ===========================================================================

    /// <summary>
    /// Task D.3.2：构造增量快照（仅含与 baseline 不同的 EntityDelta）。
    /// <para>
    /// 遍历 currentDeltas，与 baseline.Deltas 按 EntityId 比对，
    /// 仅保留 Transform/State/MovementState/AnimationState 字段有变化的 EntityDelta。
    /// 设置 BaselineTick = baseline.ServerTick，客户端据此查找本地缓存的全量快照进行重建。
    /// </para>
    /// </summary>
    /// <param name="baseline">上一次的完整状态快照（_lastSnapshot，BaselineTick=0）。</param>
    /// <param name="currentDeltas">当前 tick 的全量 EntityDelta 列表。</param>
    /// <returns>增量快照（仅含变化实体）。</returns>
    internal SnapshotPacket BuildDeltaSnapshot(SnapshotPacket baseline, List<EntityDelta> currentDeltas)
    {
        // Task 10.4/10.5：复用字段级缓冲，避免每次增量快照分配 List/Dictionary
        var changedDeltas = _changedDeltasBuffer; changedDeltas.Clear();
        // 以 EntityId 为键构建 baseline 查找表
        var baselineDict = _baselineDictBuffer; baselineDict.Clear();
        foreach (var d in baseline.Deltas)
            baselineDict[d.EntityId] = d;

        foreach (var current in currentDeltas)
        {
            if (baselineDict.TryGetValue(current.EntityId, out var baseDelta))
            {
                // 仅当关键字段变化超过阈值时纳入增量
                if (EntityDeltaChanged(baseDelta, current))
                    changedDeltas.Add(current);
            }
            else
            {
                // baseline 中没有的实体（新 Spawn）必须包含
                changedDeltas.Add(current);
            }
        }

        return new SnapshotPacket
        {
            ServerTick = _tickCount,
            BaselineTick = baseline.ServerTick,
            Deltas = changedDeltas.ToArray(),
        };
    }

    /// <summary>
    /// Task D.3.2：比较两个 EntityDelta 的关键字段是否变化。
    /// 位置变化阈值 0.01f（避免浮点抖动频繁触发增量），
    /// 属性变化阈值 1（整数差异≥1 即认为变化），
    /// 动画状态事件驱动（任何变化都算）。
    /// </summary>
    /// <param name="baseline">基线 EntityDelta。</param>
    /// <param name="current">当前 EntityDelta。</param>
    /// <returns>true 表示有显著变化（应纳入增量）；false 表示可忽略。</returns>
    internal static bool EntityDeltaChanged(EntityDelta baseline, EntityDelta current)
    {
        // Transform 比对（位置/旋转，阈值 0.01）
        if (baseline.Transform is { } bt && current.Transform is { } ct)
        {
            if (MathF.Abs(bt.X - ct.X) > 0.01f ||
                MathF.Abs(bt.Y - ct.Y) > 0.01f ||
                MathF.Abs(bt.Z - ct.Z) > 0.01f ||
                MathF.Abs(bt.Pitch - ct.Pitch) > 0.01f ||
                MathF.Abs(bt.Yaw - ct.Yaw) > 0.01f ||
                MathF.Abs(bt.Roll - ct.Roll) > 0.01f)
                return true;
        }
        else if (baseline.Transform is null && current.Transform is not null)
        {
            return true; // Transform 从无到有
        }

        // State 比对（Health/Mana/Level 等，整数差异≥1 即变化）
        if (baseline.State is { } bs && current.State is { } cs)
        {
            if (bs.Health != cs.Health ||
                bs.MaxHealth != cs.MaxHealth ||
                bs.StateBits != cs.StateBits ||
                bs.Mana != cs.Mana ||
                bs.MaxMana != cs.MaxMana ||
                bs.Level != cs.Level ||
                bs.Exp != cs.Exp ||
                bs.Stamina != cs.Stamina ||
                bs.MaxStamina != cs.MaxStamina)
                return true;
        }
        else if (baseline.State is null && current.State is not null)
        {
            return true;
        }

        // MovementState 比对（移动模式/速度/落地标志）
        if (baseline.MovementState is { } bm && current.MovementState is { } cm)
        {
            if (bm.MovementMode != cm.MovementMode ||
                bm.VelocityXZ_X != cm.VelocityXZ_X ||
                bm.VelocityXZ_Y != cm.VelocityXZ_Y ||
                bm.IsGrounded != cm.IsGrounded)
                return true;
        }
        else if (baseline.MovementState is null && current.MovementState is not null)
        {
            return true;
        }

        // AnimationState 比对（事件驱动，任何字段变化都算）
        if (baseline.AnimationState is { } ba && current.AnimationState is { } ca)
        {
            if (ba.AnimMontageId != ca.AnimMontageId ||
                ba.AnimInstanceId != ca.AnimInstanceId ||
                ba.PlayRate != ca.PlayRate ||
                ba.IsLooping != ca.IsLooping)
                return true;
        }
        else if (baseline.AnimationState is null && current.AnimationState is not null)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Task C.4.3：广播场景对象状态同步包到 AOI 订阅者。
    /// 查询 objectId 所在 chunk 的 MortonKey（若对象在 _sceneObjectStates 中有 Transform 信息则用之，
    /// 否则回退广播到全部订阅者），经现有 fanout 观察者推送。
    /// </summary>
    private async Task BroadcastSceneObjectSyncAsync(SceneObjectSyncPacket packet, ulong objectId)
    {
        if (_fanoutObservers.Count == 0)
        {
            LogNoObserverWarn("BroadcastSceneObjectSync");
            return;
        }

        // 查询 objectId 的位置以确定 chunk key
        // 场景对象通常不在 _simulatedEntities 中（静态世界对象），此时回退广播到全部订阅者
        ulong? chunkKey = null;
        if (_sceneObjectStates.TryGetValue(objectId, out var objState))
        {
            // 若场景对象有 Transform 数据，使用其位置计算 chunk key
            if (objState.TransformX != 0f || objState.TransformY != 0f || objState.TransformZ != 0f)
            {
                chunkKey = WorldCoord.ToChunkMortonKey(objState.TransformX, objState.TransformY, objState.TransformZ);
            }
        }

        IReadOnlyCollection<long> sessionIds;
        if (chunkKey.HasValue)
        {
            sessionIds = _aoi.GetSubscribers(chunkKey.Value);
        }
        else
        {
            _logger.LogDebug(
                "ZoneShard {ShardId}: 场景对象 {ObjectId} 无 Transform 信息，回退广播到全部订阅者。",
                this.GetPrimaryKeyLong(), objectId);
            sessionIds = _aoi.GetAllSubscribers();
        }

        if (sessionIds.Count == 0) return;

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = chunkKey ?? 0,
            DiffSeqStart = _tickCount,
            DiffSeqEnd = _tickCount,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(packet),
            PayloadType = WorldChunkDiffPayloadType.SceneObjectSync, // Task C.4：场景对象专用载荷类型（客户端按 PayloadType 解码）
        };

        var observers = GetObserversSnapshot();
        foreach (var (subscriptionId, observer) in observers)
        {
            try
            {
                await observer.OnChunkDiffAsync(diff, sessionIds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "ZoneShard {ShardId}: 推送场景对象同步到观察者 {SubscriptionId} 失败（已吞）。",
                    this.GetPrimaryKeyLong(), subscriptionId);
            }
        }
    }

    /// <summary>
    /// Task C.4：注册场景对象到本分片（初始化时由上层调用，填充 _sceneObjectStates）。
    /// </summary>
    /// <param name="objectId">场景对象 ID。</param>
    /// <param name="objectType">对象类型。</param>
    /// <param name="initialStateBits">初始状态位。</param>
    /// <param name="transformX">初始位置 X（0 表示无 Transform）。</param>
    /// <param name="transformY">初始位置 Y。</param>
    /// <param name="transformZ">初始位置 Z。</param>
    public Task RegisterSceneObjectAsync(ulong objectId, SceneObjectType objectType, uint initialStateBits,
        float transformX = 0f, float transformY = 0f, float transformZ = 0f)
    {
        var state = new SceneObjectStateData
        {
            ObjectId = objectId,
            ShardKey = this.GetPrimaryKeyLong(),
            ObjectType = objectType,
            StateBits = initialStateBits,
            CooldownEndTick = 0,
            OwnerCharacterId = 0,
            TransformX = transformX,
            TransformY = transformY,
            TransformZ = transformZ,
            UpdatedAt = DateTime.UtcNow,
        };
        _sceneObjectStates[objectId] = state;

        _logger.LogInformation(
            "ZoneShard {ShardId}: 注册场景对象 {ObjectId} 类型={Type} 状态=0x{StateBits:X2}",
            this.GetPrimaryKeyLong(), objectId, objectType, initialStateBits);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 广播交互槽状态同步包到已注册的 fanout 观察者（阶段 5）。
    /// 参照 <see cref="BroadcastEventAsync"/> 模式：将 <see cref="InteractionSyncPacket"/> 序列化进
    /// <see cref="WorldChunkDiffPacket.Payload"/>，通过现有 <see cref="IZoneShardFanoutObserver.OnChunkDiffAsync"/>
    /// 推送给 AOI 兴趣集内的玩家，复用 AOI 订阅过滤机制。
    /// </summary>
    /// <param name="packet">待广播的交互槽状态同步包。</param>
    private async Task BroadcastInteractionSyncAsync(InteractionSyncPacket packet)
    {
        if (_fanoutObservers.Count == 0)
        {
            LogNoObserverWarn("BroadcastInteractionSync");
            return;
        }

        // P8-8.2：根据可交互对象的位置查 chunk key，修复硬编码 0 导致 AOI 失效。
        // 可交互对象可能不在 _simulatedEntities 中（静态世界对象），此时回退广播到全部订阅者。
        var chunkKey = GetChunkMortonKeyForEntity(packet.InteractableId);

        IReadOnlyCollection<long> sessionIds;
        if (chunkKey.HasValue)
        {
            sessionIds = _aoi.GetSubscribers(chunkKey.Value);
        }
        else
        {
            _logger.LogWarning(
                "ZoneShard {ShardId}: 交互对象 {InteractableId} 不在模拟实体表中，回退广播到全部订阅者。",
                this.GetPrimaryKeyLong(), packet.InteractableId);
            sessionIds = _aoi.GetAllSubscribers();
        }

        if (sessionIds.Count == 0) return;

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = chunkKey ?? 0,
            DiffSeqStart = _tickCount,
            DiffSeqEnd = _tickCount,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(packet),
            PayloadType = WorldChunkDiffPayloadType.InteractionSync,
        };

        var observers = GetObserversSnapshot();
        foreach (var (subscriptionId, observer) in observers)
        {
            try
            {
                await observer.OnChunkDiffAsync(diff, sessionIds).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _interactionSyncObserverFailures++;
                _logger.LogWarning(
                    ex,
                    "ZoneShard {ShardId}: 推送 interaction sync 到观察者 {SubscriptionId} 失败（已吞），累计失败次数={Failures}。",
                    this.GetPrimaryKeyLong(), subscriptionId, _interactionSyncObserverFailures);
            }
        }
    }

    /// <summary>
    /// tick 模拟期间追踪实体位置/速度的内部状态结构体。
    /// Task B.5：扩展移动状态/动画状态/扩展属性字段及上一 tick 变化检测追踪。
    /// </summary>
    internal struct SimulatedEntity
    {
        /// <summary>实体最后已知权威位置 X。</summary>
        public float X;
        /// <summary>实体最后已知权威位置 Y。</summary>
        public float Y;
        /// <summary>实体最后已知权威位置 Z。</summary>
        public float Z;
        /// <summary>实体 Z 方向速度。</summary>
        public float Vz;
        /// <summary>实体水平朝向（Yaw，弧度）。</summary>
        public float Yaw;
        /// <summary>实体最大水平速度（米/秒）。</summary>
        public float MaxSpeed;
        /// <summary>待处理的输入包队列。</summary>
        public List<InputPacket> PendingInputs;
        /// <summary>Task 10.3：PendingInputs 复用缓冲（懒初始化），避免每 tick ToArray 分配。</summary>
        public InputPacket[]? PendingInputBuffer;
        /// <summary>最后一次同步的服务器 tick。</summary>
        public long LastSyncTick;
        /// <summary>
        /// 实体租约过期时间（UTC）。<br/>
        /// 网关每 20 秒调用 <see cref="ZoneShardGrain.RenewLeaseAsync"/> 续约。<br/>
        /// 超过此时间未续约的实体将被视为孤儿实体（网关崩溃/断线未清理），
        /// 由 TickAsync 自动注销并广播 Despawn。
        /// </summary>
        public DateTime LeaseExpiry;
        /// <summary>客户端报告的终点 X。</summary>
        public float ReportedEndX;
        /// <summary>客户端报告的终点 Y。</summary>
        public float ReportedEndY;
        /// <summary>客户端报告的终点 Z。</summary>
        public float ReportedEndZ;
        public bool IsGrounded;
        public int JumpCount;
        public bool IsCasting;
        public int CastSkillId;
        public long CastStartTime;
        public ulong CastTargetId;
        public int Hp;

        // ===== Task B.5：移动状态字段（MovementStateAuthComponent） =====
        /// <summary>当前移动模式（Walk/Run/Jump/Fall/Swim/Crouch）。</summary>
        public MovementMode MovementMode;
        /// <summary>水平速度 X 分量（世界坐标系）。</summary>
        public float VelocityXZ_X;
        /// <summary>水平速度 Y 分量（世界坐标系）。</summary>
        public float VelocityXZ_Y;

        // ===== Task B.5：扩展属性字段（EntityStateAuthComponent 扩展） =====
        public int MaxHp;
        public int Mana;
        public int MaxMana;
        public int Level;
        public long Exp;
        public int Stamina;
        public int MaxStamina;
        public uint EntityStateBits;

        // ===== Task B.5：动画状态字段（AnimationStateAuthComponent） =====
        public uint AnimMontageId;
        public uint AnimInstanceId;
        public float PlayRate;
        public float TimePosition;
        public bool IsLooping;

        // ===== Task B.5：上一 tick 变化检测追踪 =====
        public MovementMode PrevMovementMode;
        public bool PrevIsGrounded;
        public float PrevVelocityXZ_X;
        public float PrevVelocityXZ_Y;
        public int PrevMana;
        public int PrevLevel;
        public long PrevExp;
        public int PrevStamina;
        public int PrevHp;
        public uint PrevEntityStateBits;

        /// <summary>上次属性心跳的服务器 tick（用于 1Hz 心跳判断）。</summary>
        public long LastAttributeHeartbeatTick;
        /// <summary>上次广播 Update delta 的服务器 tick（用于静止实体心跳广播）。</summary>
        public long LastUpdateBroadcastTick;
        /// <summary>本 tick 是否收到输入（用于广播决策，必须在 PendingInputs.Clear() 前设置）。</summary>
        public bool HadInputThisTick;

        /// <summary>待下发的动画事件队列（Montage 触发/结束）。null 表示无待下发事件。</summary>
        public Queue<AnimationStateAuthComponent>? PendingAnimationEvents;
    }
}
