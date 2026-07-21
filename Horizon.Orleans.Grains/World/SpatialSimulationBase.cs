using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Game.Core.Sim;
using Horizon.Game.Message.Sync;
using Horizon.Game.Message.Sync.Components;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.2 空间模拟抽象基类：ZoneShard 和 Instance 共享的核心模拟逻辑。<br/>
/// 提供：实体注册/注销、Tick 循环驱动、移动权威回放、快照生成、租约管理。<br/>
/// 派生类只需实现 AOI 策略和扇出方式。
/// </summary>
/// <remarks>
/// 设计原则：
/// <list type="bullet">
///   <item>ZoneShardGrain 当前保持独立实现（2277 行），后续版本渐进迁移到本基类。</item>
///   <item>InstanceGrain 直接继承本基类，获得完整的空间模拟能力。</item>
///   <item>本基类不依赖任何 AOI/订阅逻辑（由派生类实现）。</item>
/// </list>
/// </remarks>
public abstract class SpatialSimulationBase : Grain
{
    protected readonly ILogger Logger;
    protected readonly MovementValidator MovementValidator;

    /// <summary>模拟实体表（entityId → 状态）。</summary>
    protected readonly Dictionary<ulong, SimulatedEntityState> Entities = new();

    /// <summary>当前 tick 计数。</summary>
    protected long TickCount;

    /// <summary>Tick 定时器。</summary>
    private IDisposable? _tickTimer;

    /// <summary>Tick 间隔（默认 60Hz）。</summary>
    protected virtual TimeSpan TickInterval => TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>实体租约超时（默认 90 秒）。</summary>
    protected virtual TimeSpan LeaseTimeout => TimeSpan.FromSeconds(90);

    /// <summary>孤儿检测间隔（默认 10 秒）。</summary>
    protected virtual TimeSpan OrphanCheckInterval => TimeSpan.FromSeconds(10);

    // 复用缓冲
    private readonly List<EntityDelta> _deltaBuffer = new();

    protected SpatialSimulationBase(ILogger logger)
    {
        Logger = logger;
        MovementValidator = new MovementValidator();
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _tickTimer = this.RegisterGrainTimer(
            OnTickTimer,
            new GrainTimerCreationOptions(TickInterval, TickInterval));
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _tickTimer?.Dispose();
        _tickTimer = null;
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    // --- 实体生命周期 ---

    /// <summary>
    /// 注册实体到本空间。
    /// </summary>
    protected virtual Task RegisterEntityAsync(ulong entityId, float x, float y, float z, float yaw, float maxSpeed)
    {
        Entities[entityId] = new SimulatedEntityState
        {
            EntityId = entityId,
            X = x,
            Y = y,
            Z = z,
            Yaw = yaw,
            MaxSpeed = maxSpeed,
            LeaseExpiry = DateTime.UtcNow + LeaseTimeout,
            IsAlive = true,
        };

        Logger.LogDebug("实体注册。EntityId={EntityId}, SpaceId={SpaceId}", entityId, GetSpaceId());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注销实体。
    /// </summary>
    protected virtual Task UnregisterEntityAsync(ulong entityId)
    {
        Entities.Remove(entityId);
        Logger.LogDebug("实体注销。EntityId={EntityId}, SpaceId={SpaceId}", entityId, GetSpaceId());
        return Task.CompletedTask;
    }

    /// <summary>
    /// 续约实体租约。
    /// </summary>
    public Task RenewLeaseAsync(ulong entityId)
    {
        if (Entities.TryGetValue(entityId, out var entity))
        {
            entity.LeaseExpiry = DateTime.UtcNow + LeaseTimeout;
        }
        return Task.CompletedTask;
    }

    // --- Tick 循环 ---

    private async Task OnTickTimer(CancellationToken ct)
    {
        try
        {
            await TickAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Tick 异常。SpaceId={SpaceId}", GetSpaceId());
        }
    }

    /// <summary>
    /// 核心 Tick：驱动所有实体的移动回放 + 孤儿检测 + 快照广播。
    /// </summary>
    protected virtual async Task TickAsync()
    {
        TickCount++;
        var now = DateTime.UtcNow;

        // 孤儿检测
        if (TickCount % (long)(OrphanCheckInterval / TickInterval) == 0)
        {
            await DetectOrphansAsync(now);
        }

        // 移动回放
        _deltaBuffer.Clear();
        foreach (var (entityId, entity) in Entities)
        {
            if (!entity.IsAlive) continue;

            // 处理待处理输入
            if (entity.PendingInputs.Count > 0)
            {
                ProcessEntityInputs(entity);
                entity.PendingInputs.Clear();
            }

            // 生成 delta（如果有变化）
            if (entity.HasChanged)
            {
                _deltaBuffer.Add(CreateEntityDelta(entity));
                entity.ResetChangeTracking();
            }
        }

        // 广播快照（由派生类决定 AOI 策略）
        if (_deltaBuffer.Count > 0)
        {
            await BroadcastSnapshotAsync(_deltaBuffer.ToArray());
        }
    }

    /// <summary>
    /// 处理单个实体的输入队列（移动权威回放）。
    /// </summary>
    private void ProcessEntityInputs(SimulatedEntityState entity)
    {
        foreach (var input in entity.PendingInputs)
        {
            // 使用 MovementFormula 进行权威回放
            // Step(x, y, z, vz, moveX, moveY, jumpImpulse, dt, maxSpeed) → (X, Y, Z, Vz)
            var maxSpeed = entity.MaxSpeed > 0 ? entity.MaxSpeed : input.MaxSpeed;
            var jumpImpulse = 0f; // TODO: 从 InputBits 提取跳跃标志
            var (nx, ny, nz, nvz) = MovementFormula.Step(
                entity.X, entity.Y, entity.Z,
                entity.Vz,
                input.MoveX, input.MoveY,
                jumpImpulse,
                (float)(TickInterval.TotalSeconds),
                maxSpeed);

            entity.X = nx;
            entity.Y = ny;
            entity.Z = nz;
            entity.Vz = nvz;
            entity.Yaw = input.LookYaw;
            entity.MarkChanged();
        }
    }

    /// <summary>
    /// 孤儿检测：清理超时未续约的实体。
    /// </summary>
    private async Task DetectOrphansAsync(DateTime now)
    {
        var orphans = Entities
            .Where(kv => kv.Value.LeaseExpiry < now)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var entityId in orphans)
        {
            Logger.LogWarning("孤儿实体清理。EntityId={EntityId}, SpaceId={SpaceId}", entityId, GetSpaceId());
            await UnregisterEntityAsync(entityId);
            await OnEntityOrphanedAsync(entityId);
        }
    }

    // --- 输入处理 ---

    /// <summary>
    /// 接收客户端输入包。
    /// </summary>
    public Task AcceptInputAsync(ulong entityId, InputPacket input)
    {
        if (Entities.TryGetValue(entityId, out var entity))
        {
            entity.PendingInputs.Add(input);
        }
        return Task.CompletedTask;
    }

    // --- 派生类必须实现 ---

    /// <summary>获取空间标识（用于日志）。</summary>
    protected abstract long GetSpaceId();

    /// <summary>广播快照给 AOI 内的订阅者（由派生类实现 AOI 策略）。</summary>
    protected abstract Task BroadcastSnapshotAsync(EntityDelta[] deltas);

    /// <summary>实体成为孤儿时的回调（派生类可通知外部系统）。</summary>
    protected virtual Task OnEntityOrphanedAsync(ulong entityId) => Task.CompletedTask;

    // --- 辅助方法 ---

    private static EntityDelta CreateEntityDelta(SimulatedEntityState entity)
    {
        return new EntityDelta
        {
            EntityId = entity.EntityId,
            Kind = EntityDeltaKind.Update,
            Transform = new AuthTransformComponent
            {
                X = entity.X,
                Y = entity.Y,
                Z = entity.Z,
                Yaw = entity.Yaw,
            },
            State = new EntityStateAuthComponent
            {
                Health = entity.Hp,
                MaxHealth = entity.MaxHp,
                Level = entity.Level,
                StateBits = entity.EntityStateBits,
            },
        };
    }

    /// <summary>
    /// 模拟实体状态（空间模拟基类使用的轻量版本）。
    /// </summary>
    protected sealed class SimulatedEntityState
    {
        public ulong EntityId { get; init; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Vz { get; set; }
        public float Yaw { get; set; }
        public float MaxSpeed { get; set; }
        public bool IsGrounded { get; set; } = true;
        public bool IsAlive { get; set; } = true;
        public DateTime LeaseExpiry { get; set; }

        // RPG 广播缓存（权威来源为 CharacterGrain）
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Level { get; set; }
        public uint EntityStateBits { get; set; }

        // 输入队列
        public List<InputPacket> PendingInputs { get; } = new();

        // 变化追踪
        private bool _hasChanged;
        public bool HasChanged => _hasChanged;
        public void MarkChanged() => _hasChanged = true;
        public void ResetChangeTracking() => _hasChanged = false;
    }
}
