using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.Combat;

namespace Horizon.Orleans.Grains.Combat;

/// <summary>
/// 战斗系统 Grain 实现（P1.4）。<br/>
/// 负责攻击判定（命中/闪避/暴击）、伤害计算（攻防公式）、死亡裁决。<br/>
/// Grain-per-Session 模型：每个战斗会话一个 Grain 实例，Orleans 单线程保证无锁。
/// </summary>
public sealed class CombatSystemGrain : Grain, ICombatSystemGrain
{
    private readonly ILogger<CombatSystemGrain> _logger;

    /// <summary>战斗会话中的实体状态表。</summary>
    private readonly Dictionary<ulong, CombatEntityState> _entities = new();

    /// <summary>会话统计。</summary>
    private long _totalDamageDealt;
    private int _totalActionsProcessed;
    private DateTime _sessionStartTime;
    private long _serverTick;

    /// <summary>随机数生成器（暴击/闪避判定）。</summary>
    private readonly Random _random = new();

    // --- 战斗数值常量（Phase 1 简化版，Phase 2 由数值表驱动） ---
    /// <summary>基础暴击倍率。</summary>
    private const float CritMultiplier = 1.5f;
    /// <summary>防御减伤系数：damage * (1 - defense / (defense + 100))。</summary>
    private const int DefenseScaleFactor = 100;
    /// <summary>攻速限制：两次攻击最小间隔（毫秒）。</summary>
    private const long MinAttackIntervalMs = 500;

    public CombatSystemGrain(ILogger<CombatSystemGrain> logger)
    {
        _logger = logger;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _sessionStartTime = DateTime.UtcNow;
        _serverTick = 0;
        _logger.LogInformation("CombatSystemGrain 激活。GrainId={GrainId}", this.GetPrimaryKeyLong());
        return base.OnActivateAsync(cancellationToken);
    }

    public Task<CombatVerdict> ProcessActionAsync(CombatActionRequest request)
    {
        _serverTick++;
        _totalActionsProcessed++;

        var verdict = new CombatVerdict { ServerTick = _serverTick };

        // 校验攻击者存在且存活
        if (!_entities.TryGetValue(request.AttackerId, out var attacker) || !attacker.IsAlive)
        {
            _logger.LogWarning("战斗动作无效：攻击者不存在或已死亡。AttackerId={AttackerId}", request.AttackerId);
            return Task.FromResult(verdict);
        }

        // 攻速校验（防外挂）
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs - attacker.LastActionTimeMs < MinAttackIntervalMs)
        {
            _logger.LogWarning("战斗动作被拒绝：攻速过快。AttackerId={AttackerId}, IntervalMs={IntervalMs}",
                request.AttackerId, nowMs - attacker.LastActionTimeMs);
            return Task.FromResult(verdict);
        }
        attacker.LastActionTimeMs = nowMs;

        // 治疗/道具（ActionKind=2）：对自身或目标施加治疗
        if (request.ActionKind == 2) // ItemUse
        {
            return Task.FromResult(ProcessHealing(request, attacker));
        }

        // 格挡/闪避（ActionKind=3）：设置防御状态
        if (request.ActionKind == 3) // Defend
        {
            attacker.IsDefending = true;
            return Task.FromResult(verdict);
        }

        // 校验目标存在且存活
        if (!_entities.TryGetValue(request.TargetId, out var target) || !target.IsAlive)
        {
            _logger.LogWarning("战斗动作无效：目标不存在或已死亡。TargetId={TargetId}", request.TargetId);
            return Task.FromResult(verdict);
        }

        // --- 命中判定 ---
        var dodgeRoll = (float)_random.NextDouble();
        var effectiveDodge = target.DodgeRate * (target.IsDefending ? 1.5f : 1.0f);
        if (dodgeRoll < effectiveDodge)
        {
            // 闪避
            verdict.IsHit = false;
            target.IsDefending = false;
            return Task.FromResult(verdict);
        }

        // --- 伤害计算 ---
        var baseDamage = Math.Max(1, attacker.Attack - target.Defense * DefenseScaleFactor / (target.Defense + DefenseScaleFactor));

        // 暴击判定
        var critRoll = (float)_random.NextDouble();
        var isCritical = critRoll < attacker.CritRate;
        if (isCritical)
        {
            baseDamage = (int)(baseDamage * CritMultiplier);
        }

        // 格挡减伤
        if (target.IsDefending)
        {
            baseDamage = (int)(baseDamage * 0.5f);
            target.IsDefending = false;
        }

        // 应用伤害
        target.CurrentHp -= baseDamage;
        _totalDamageDealt += baseDamage;

        verdict.IsHit = true;
        verdict.DamageAmount = baseDamage;
        verdict.IsCritical = isCritical;
        verdict.DamageType = 0; // Physical
        verdict.TargetRemainingHp = Math.Max(0, target.CurrentHp);
        verdict.TargetMaxHp = target.MaxHp;

        // 死亡裁决
        if (target.CurrentHp <= 0)
        {
            target.CurrentHp = 0;
            target.IsAlive = false;
            verdict.IsTargetDead = true;

            _logger.LogInformation(
                "实体击杀。AttackerId={AttackerId}, TargetId={TargetId}, Damage={Damage}, IsCrit={IsCrit}",
                request.AttackerId, request.TargetId, baseDamage, isCritical);
        }

        return Task.FromResult(verdict);
    }

    private CombatVerdict ProcessHealing(CombatActionRequest request, CombatEntityState healer)
    {
        var verdict = new CombatVerdict { ServerTick = _serverTick, DamageType = 4 }; // Healing

        // 简化：治疗量 = 攻击力 * 0.5（Phase 2 由道具/技能表驱动）
        var healAmount = Math.Max(1, healer.Attack / 2);
        var targetId = request.TargetId == 0 ? request.AttackerId : request.TargetId;

        if (_entities.TryGetValue(targetId, out var target) && target.IsAlive)
        {
            target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + healAmount);
            verdict.DamageAmount = -healAmount; // 负数表示治疗
            verdict.TargetRemainingHp = target.CurrentHp;
            verdict.TargetMaxHp = target.MaxHp;
            verdict.IsHit = true;
        }

        return verdict;
    }

    public Task<CombatEntityStatus> GetEntityStatusAsync(ulong entityId)
    {
        if (_entities.TryGetValue(entityId, out var state))
        {
            return Task.FromResult(new CombatEntityStatus
            {
                EntityId = entityId,
                CurrentHp = state.CurrentHp,
                MaxHp = state.MaxHp,
                IsAlive = state.IsAlive,
                LastDamageTick = state.LastDamageTick,
            });
        }

        return Task.FromResult(new CombatEntityStatus { EntityId = entityId, IsAlive = false });
    }

    public Task RegisterEntityAsync(CombatEntitySnapshot snapshot)
    {
        _entities[snapshot.EntityId] = new CombatEntityState
        {
            EntityId = snapshot.EntityId,
            CurrentHp = snapshot.CurrentHp,
            MaxHp = snapshot.MaxHp,
            Attack = snapshot.Attack,
            Defense = snapshot.Defense,
            CritRate = snapshot.CritRate,
            DodgeRate = snapshot.DodgeRate,
            IsPlayer = snapshot.IsPlayer,
            Level = snapshot.Level,
            IsAlive = snapshot.CurrentHp > 0,
        };

        _logger.LogDebug("实体注册到战斗会话。EntityId={EntityId}, Hp={Hp}/{MaxHp}, GrainId={GrainId}",
            snapshot.EntityId, snapshot.CurrentHp, snapshot.MaxHp, this.GetPrimaryKeyLong());

        return Task.CompletedTask;
    }

    public Task UnregisterEntityAsync(ulong entityId)
    {
        _entities.Remove(entityId);
        return Task.CompletedTask;
    }

    public Task<CombatSessionStats> GetSessionStatsAsync()
    {
        return Task.FromResult(new CombatSessionStats
        {
            ActiveEntityCount = _entities.Count(e => e.Value.IsAlive),
            TotalDamageDealt = _totalDamageDealt,
            TotalActionsProcessed = _totalActionsProcessed,
            SessionStartTime = _sessionStartTime,
        });
    }

    /// <summary>内部实体战斗状态。</summary>
    private sealed class CombatEntityState
    {
        public ulong EntityId { get; init; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; init; }
        public int Attack { get; init; }
        public int Defense { get; init; }
        public float CritRate { get; init; }
        public float DodgeRate { get; init; }
        public bool IsPlayer { get; init; }
        public int Level { get; init; }
        public bool IsAlive { get; set; }
        public bool IsDefending { get; set; }
        public long LastActionTimeMs { get; set; }
        public long LastDamageTick { get; set; }
    }
}
