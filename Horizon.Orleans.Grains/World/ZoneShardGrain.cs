using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Horizon.Game.Core.Sim;
using Horizon.Game.Core.World;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;
using Horizon.Orleans.Interface.World;

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
    private readonly MovementValidator _movementValidator;

    // Tick 周期状态字段
    private double _lastTickTime;
    private readonly Dictionary<ulong, SimulatedEntity> _simulatedEntities = new();
    private long _tickCount;
    private int _processedInputs;
    private int _correctionsIssued;
    private IDisposable? _tickTimer;
    private TimeSpan _tickInterval = TimeSpan.FromSeconds(1.0 / 60.0);

    public ZoneShardGrain(ILogger<ZoneShardGrain> logger)
    {
        _logger = logger;
        _movementValidator = new MovementValidator(new MovementValidator.Options
        {
            PositionEpsilon = MovementValidator.DefaultPositionEpsilon,
            HardSpeedCap = MovementValidator.DefaultHardSpeedCap,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
            TickDtSeconds = 1f / 60f,
        });
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
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
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> SubscribeSessionAsync(long sessionId, ulong[] mortonKeys)
    {
        var added = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _aoi.Subscribe(sessionId, mortonKeys);
        return Task.FromResult(added);
    }

    /// <inheritdoc />
    public Task<int> UnsubscribeSessionAsync(long sessionId, ulong[] mortonKeys)
    {
        var removed = mortonKeys is null || mortonKeys.Length == 0
            ? 0
            : _aoi.Unsubscribe(sessionId, mortonKeys);
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> RemoveSessionAsync(long sessionId)
    {
        var removed = _aoi.RemoveSession(sessionId);
        if (removed > 0)
        {
            _logger.LogDebug(
                "ZoneShard {ShardId}: 清理 session {SessionId} 的 {Count} 条订阅。",
                this.GetPrimaryKeyLong(), sessionId, removed);
        }
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
    public Task SubscribeFanoutAsync(Guid subscriptionId, IZoneShardFanoutObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        _fanoutObservers[subscriptionId] = observer;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeFanoutAsync(Guid subscriptionId)
    {
        _fanoutObservers.Remove(subscriptionId);
        return Task.CompletedTask;
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
            var sessionIds = subs.ToArray();

            var observers = _fanoutObservers.ToArray();
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
                        "ZoneShard {ShardId}: 推送 fanout 到观察者 {SubscriptionId} 失败（已吞；不影响其他观察者）。",
                        this.GetPrimaryKeyLong(), subscriptionId);
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<int> TickAsync(double tickTime)
    {
        var processedEntityCount = 0;
        var deltas = new List<EntityDelta>();
        var corrections = new List<CorrectionPacket>();

        foreach (var kv in _simulatedEntities)
        {
            var entityId = kv.Key;
            var entity = kv.Value;

            if (entity.PendingInputs.Count == 0)
                continue;

            var inputs = entity.PendingInputs.ToArray();

            var startPos = new WorldPosition(entity.X, entity.Y, entity.Z);
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

                deltas.Add(new EntityDelta
                {
                    EntityId = entityId,
                    Kind = EntityDeltaKind.Update,
                    Transform = new AuthTransformComponent
                    {
                        X = entity.X,
                        Y = entity.Y,
                        Z = entity.Z,
                        Pitch = 0f,
                        Yaw = 0f,
                        Roll = 0f,
                        ServerTick = _tickCount,
                    },
                });

                _correctionsIssued++;
            }
            else
            {
                entity.X = validationResult.AuthoritativeEnd.X;
                entity.Y = validationResult.AuthoritativeEnd.Y;
                entity.Z = validationResult.AuthoritativeEnd.Z;
                entity.Vz = validationResult.AuthoritativeVz;

                deltas.Add(new EntityDelta
                {
                    EntityId = entityId,
                    Kind = EntityDeltaKind.Update,
                    Transform = new AuthTransformComponent
                    {
                        X = entity.X,
                        Y = entity.Y,
                        Z = entity.Z,
                        Pitch = 0f,
                        Yaw = 0f,
                        Roll = 0f,
                        ServerTick = _tickCount,
                    },
                });
            }

            entity.PendingInputs.Clear();
            entity.LastSyncTick = _tickCount;

            const float groundedEpsilon = 0.001f;
            var prevZ = startPos.Z;
            var curZ = validationResult.AuthoritativeEnd.Z;
            var isGrounded = MathF.Abs(curZ - prevZ) < groundedEpsilon && validationResult.AuthoritativeVz <= 0f;
            entity.IsGrounded = isGrounded;
            entity.JumpCount = isGrounded ? 0 : entity.JumpCount;

            _simulatedEntities[entityId] = entity;

            _processedInputs += inputs.Length;
            processedEntityCount++;
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

        if (deltas.Count > 0)
        {
            var snapshot = new SnapshotPacket
            {
                ServerTick = _tickCount,
                BaselineTick = 0,
                Deltas = deltas.ToArray(),
            };

            _ = BroadcastSnapshotAsync(snapshot, corrections);
        }

        _tickCount++;
        _lastTickTime = tickTime;

        return Task.FromResult(processedEntityCount);
    }

    /// <inheritdoc />
    public Task RegisterEntityAsync(ulong entityId, float initialX, float initialY, float initialZ, float maxSpeed = 6f)
    {
        _simulatedEntities[entityId] = new SimulatedEntity
        {
            X = initialX,
            Y = initialY,
            Z = initialZ,
            Vz = 0f,
            MaxSpeed = maxSpeed,
            PendingInputs = new List<InputPacket>(),
            LastSyncTick = 0,
        };

        _logger.LogDebug(
            "ZoneShard {ShardId}: 注册实体 {EntityId} 初始位置 ({X:F2},{Y:F2},{Z:F2})。",
            this.GetPrimaryKeyLong(), entityId, initialX, initialY, initialZ);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnregisterEntityAsync(ulong entityId)
    {
        if (_simulatedEntities.Remove(entityId))
        {
            _logger.LogDebug(
                "ZoneShard {ShardId}: 注销实体 {EntityId}。",
                this.GetPrimaryKeyLong(), entityId);
        }

        return Task.CompletedTask;
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
        entity.ReportedEndX = reportedEndX;
        entity.ReportedEndY = reportedEndY;
        entity.ReportedEndZ = reportedEndZ;
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
    private async Task BroadcastSnapshotAsync(SnapshotPacket snapshot, List<CorrectionPacket> corrections)
    {
        if (_fanoutObservers.Count == 0)
            return;

        var observers = _fanoutObservers.ToArray();

        foreach (var delta in snapshot.Deltas)
        {
            var diff = new WorldChunkDiffPacket
            {
                ChunkMortonKey = 0,
                DiffSeqStart = _tickCount,
                DiffSeqEnd = _tickCount,
                Payload = MemoryPack.MemoryPackSerializer.Serialize(new EntityDelta[] { delta }),
            };

            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(diff, Array.Empty<long>()).ConfigureAwait(false);
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
            var eventPacket = new EventPacket
            {
                ServerTick = _tickCount,
                Events = new[]
                {
                    new SyncEvent
                    {
                        Kind = SyncEventKind.Unknown,
                        SourceEntityId = correction.EntityId,
                        IntValue = (int)correction.Reason,
                        FloatValue = correction.DriftMeters,
                        Payload = MemoryPack.MemoryPackSerializer.Serialize(correction),
                    },
                },
            };

            var diff = new WorldChunkDiffPacket
            {
                ChunkMortonKey = 0,
                DiffSeqStart = _tickCount,
                DiffSeqEnd = _tickCount,
                Payload = MemoryPack.MemoryPackSerializer.Serialize(eventPacket),
            };

            foreach (var (subscriptionId, observer) in observers)
            {
                try
                {
                    await observer.OnChunkDiffAsync(diff, Array.Empty<long>()).ConfigureAwait(false);
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

    private async Task BroadcastEventAsync(EventPacket eventPacket)
    {
        if (_fanoutObservers.Count == 0)
            return;

        var diff = new WorldChunkDiffPacket
        {
            ChunkMortonKey = 0,
            DiffSeqStart = _tickCount,
            DiffSeqEnd = _tickCount,
            Payload = MemoryPack.MemoryPackSerializer.Serialize(eventPacket),
        };

        var observers = _fanoutObservers.ToArray();
        foreach (var (subscriptionId, observer) in observers)
        {
            try
            {
                await observer.OnChunkDiffAsync(diff, Array.Empty<long>()).ConfigureAwait(false);
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

    /// <summary>
    /// tick 模拟期间追踪实体位置/速度的内部状态结构体。
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
        /// <summary>实体最大水平速度（米/秒）。</summary>
        public float MaxSpeed;
        /// <summary>待处理的输入包队列。</summary>
        public List<InputPacket> PendingInputs;
        /// <summary>最后一次同步的服务器 tick。</summary>
        public long LastSyncTick;
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
    }
}
