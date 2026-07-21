using System;
using System.Collections.Generic;
using Arch.Core;
using Horizon.Game.ECS.Arch.Components;
using Horizon.Game.ECS.Arch.Core;
using Horizon.Game.Message.Sync;

namespace Horizon.Game.ECS.Arch.Systems;

/// <summary>
/// P1.4 战斗特效系统：处理服务端下发的 <see cref="DamagePacket"/> 和 <see cref="DeathPacket"/>，
/// 驱动伤害数字飘字、受击动画、技能特效、死亡表现。
/// <para>
/// 在 <see cref="SystemGroup.Render"/> 阶段执行（渲染前同步），
/// 从 <see cref="CombatEffectBuffer"/> 中取出本帧待处理的战斗事件并应用到 ECS 组件/Flax Actor。
/// </para>
/// </summary>
[ArchSystem(SystemGroup.Render, order: 5)]
public sealed class CombatEffectSystem : ArchSystemBase
{
    /// <summary>当前实例（供网络层静态推送事件）。</summary>
    public static CombatEffectSystem? Instance { get; private set; }

    /// <summary>待处理的伤害事件队列（线程安全：网络线程写入，主线程消费）。</summary>
    private readonly Queue<DamagePacket> _damageQueue = new();

    /// <summary>待处理的死亡事件队列。</summary>
    private readonly Queue<DeathPacket> _deathQueue = new();

    /// <summary>队列锁（网络线程 vs 主线程）。</summary>
    private readonly object _queueLock = new();

    /// <summary>每帧最大处理事件数（防止突发大量事件导致卡顿）。</summary>
    private const int MaxEventsPerFrame = 32;

    /// <summary>伤害数字飘字持续时间（秒）。</summary>
    private const float DamageNumberDuration = 1.2f;

    /// <summary>受击闪烁持续时间（秒）。</summary>
    private const float HitFlashDuration = 0.15f;

    public override void Initialize(World world)
    {
        Instance = this;
    }

    public override void Update(World world, TimeSpan deltaTime)
    {
        // 从队列中取出本帧待处理事件
        DamagePacket[] damages;
        DeathPacket[] deaths;

        lock (_queueLock)
        {
            var damageCount = Math.Min(_damageQueue.Count, MaxEventsPerFrame);
            var deathCount = Math.Min(_deathQueue.Count, MaxEventsPerFrame);

            damages = new DamagePacket[damageCount];
            deaths = new DeathPacket[deathCount];

            for (var i = 0; i < damageCount; i++)
                damages[i] = _damageQueue.Dequeue();
            for (var i = 0; i < deathCount; i++)
                deaths[i] = _deathQueue.Dequeue();
        }

        // 处理伤害事件
        foreach (var damage in damages)
        {
            ProcessDamageEvent(world, damage);
        }

        // 处理死亡事件
        foreach (var death in deaths)
        {
            ProcessDeathEvent(world, death);
        }
    }

    /// <summary>
    /// 网络层收到 DamagePacket 后调用（线程安全）。
    /// </summary>
    public void EnqueueDamage(DamagePacket packet)
    {
        lock (_queueLock)
        {
            _damageQueue.Enqueue(packet);
        }
    }

    /// <summary>
    /// 网络层收到 DeathPacket 后调用（线程安全）。
    /// </summary>
    public void EnqueueDeath(DeathPacket packet)
    {
        lock (_queueLock)
        {
            _deathQueue.Enqueue(packet);
        }
    }

    private void ProcessDamageEvent(World world, DamagePacket damage)
    {
        // 查找目标实体
        var targetEntity = FindEntityByNetworkId(world, damage.TargetId);
        if (targetEntity is null) return;

        // 1. 更新 HP 组件（若有 CombatStateComponent）
        // TODO Phase 2：添加 CombatStateComponent 存储 HP/MaxHP/Buff

        // 2. 触发受击反馈
        // - 伤害数字飘字（Flax UI：WorldSpaceLabel）
        // - 受击闪烁（Material 参数）
        // - 屏幕震动（本地玩家受击时）
        var isLocalPlayerTarget = IsLocalPlayer(world, targetEntity.Value);

        // 记录事件供 Flax 侧读取（通过静态事件总线）
        CombatEffectEvents.RaiseDamageNumber(new DamageNumberEvent
        {
            TargetEntityId = damage.TargetId,
            DamageAmount = damage.DamageAmount,
            IsCritical = damage.IsCritical,
            DamageType = damage.DamageType,
            Duration = DamageNumberDuration,
            IsLocalPlayer = isLocalPlayerTarget,
        });

        // 受击动画触发
        CombatEffectEvents.RaiseHitReaction(new HitReactionEvent
        {
            TargetEntityId = damage.TargetId,
            AttackerId = damage.AttackerId,
            Duration = HitFlashDuration,
            IsCritical = damage.IsCritical,
        });
    }

    private void ProcessDeathEvent(World world, DeathPacket death)
    {
        var targetEntity = FindEntityByNetworkId(world, death.EntityId);
        if (targetEntity is null) return;

        // 触发死亡表现
        CombatEffectEvents.RaiseDeath(new DeathEvent
        {
            EntityId = death.EntityId,
            KillerId = death.KillerId,
            DeathType = (byte)death.DeathType,
            RespawnDelaySeconds = death.RespawnDelaySeconds,
        });

        // TODO Phase 2：
        // - 播放死亡动画（Ragdoll / 动画状态机）
        // - 禁用碰撞体
        // - 隐藏/淡出模型
        // - 本地玩家死亡：显示复活 UI + 倒计时
    }

    private static Entity? FindEntityByNetworkId(World world, ulong networkId)
    {
        Entity? result = null;
        var query = new QueryDescription().WithAll<NetworkIdentityComponent>();
        world.Query(query, (Entity entity, ref NetworkIdentityComponent identity) =>
        {
            if (identity.EntityId == networkId)
            {
                result = entity;
            }
        });
        return result;
    }

    private static bool IsLocalPlayer(World world, Entity entity)
    {
        if (!world.Has<NetworkIdentityComponent>(entity)) return false;
        ref var identity = ref world.Get<NetworkIdentityComponent>(entity);
        return identity.IsLocalPlayer;
    }

    public override void Dispose(World world)
    {
        if (Instance == this) Instance = null;
    }
}

// ---------------------------------------------------------------------------
// 战斗特效事件总线（ECS → Flax 渲染层的桥接）。
// ---------------------------------------------------------------------------

/// <summary>
/// 战斗特效事件总线：ECS 系统通过此静态类向 Flax 渲染层推送表现事件。
/// Flax 侧订阅这些事件来播放粒子特效/伤害数字/动画。
/// </summary>
public static class CombatEffectEvents
{
    public static event Action<DamageNumberEvent>? OnDamageNumber;
    public static event Action<HitReactionEvent>? OnHitReaction;
    public static event Action<DeathEvent>? OnDeath;

    public static void RaiseDamageNumber(DamageNumberEvent e) => OnDamageNumber?.Invoke(e);
    public static void RaiseHitReaction(HitReactionEvent e) => OnHitReaction?.Invoke(e);
    public static void RaiseDeath(DeathEvent e) => OnDeath?.Invoke(e);
}

/// <summary>伤害数字飘字事件。</summary>
public readonly record struct DamageNumberEvent
{
    public ulong TargetEntityId { get; init; }
    public int DamageAmount { get; init; }
    public bool IsCritical { get; init; }
    public byte DamageType { get; init; }
    public float Duration { get; init; }
    public bool IsLocalPlayer { get; init; }
}

/// <summary>受击反馈事件。</summary>
public readonly record struct HitReactionEvent
{
    public ulong TargetEntityId { get; init; }
    public ulong AttackerId { get; init; }
    public float Duration { get; init; }
    public bool IsCritical { get; init; }
}

/// <summary>死亡事件。</summary>
public readonly record struct DeathEvent
{
    public ulong EntityId { get; init; }
    public ulong KillerId { get; init; }
    public byte DeathType { get; init; }
    public float RespawnDelaySeconds { get; init; }
}
