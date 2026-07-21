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
/// 怪物 Grain 实现（P1.4）。<br/>
/// AI 状态机：Idle → Patrol → Chase → Attack → ReturnToSpawn → Dead。<br/>
/// 仇恨表：按累计伤害排序，最高仇恨为目标。脱战条件：目标超出仇恨范围 1.5 倍或全部死亡。
/// </summary>
public sealed class MonsterGrain : Grain, IMonsterGrain
{
    private readonly ILogger<MonsterGrain> _logger;

    private MonsterConfig _config = null!;
    private int _currentHp;
    private bool _isAlive = true;
    private MonsterAiAction _currentAction = MonsterAiAction.Idle;
    private MonsterPosition _position = new();
    private ulong _currentTargetId;

    /// <summary>仇恨表：entityId → 累计仇恨值。</summary>
    private readonly Dictionary<ulong, int> _threatTable = new();

    /// <summary>巡逻路径当前索引。</summary>
    private int _patrolIndex;

    /// <summary>上次攻击时间（毫秒）。</summary>
    private long _lastAttackTimeMs;

    /// <summary>怪物攻击冷却（毫秒）。</summary>
    private const long AttackCooldownMs = 2000;

    /// <summary>脱战距离倍数（相对于 AggroRange）。</summary>
    private const float LeashMultiplier = 1.5f;

    public MonsterGrain(ILogger<MonsterGrain> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(MonsterConfig config)
    {
        _config = config;
        _currentHp = config.MaxHp;
        _isAlive = true;
        _position = new MonsterPosition
        {
            X = config.SpawnPoint.X,
            Y = config.SpawnPoint.Y,
            Z = config.SpawnPoint.Z,
            Yaw = config.SpawnPoint.Yaw,
        };
        _currentAction = MonsterAiAction.Idle;
        _patrolIndex = 0;
        _threatTable.Clear();
        _currentTargetId = 0;

        _logger.LogInformation(
            "怪物初始化。MonsterId={MonsterId}, Template={Template}, Name={Name}, Level={Level}",
            this.GetPrimaryKeyLong(), config.TemplateId, config.Name, config.Level);

        return Task.CompletedTask;
    }

    public Task<MonsterAiDecision> TickAsync(float deltaMs, MonsterPosition currentPosition, NearbyEntity[] nearbyEntities)
    {
        if (!_isAlive)
        {
            return Task.FromResult(new MonsterAiDecision { Action = MonsterAiAction.Dead });
        }

        _position = currentPosition;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 更新仇恨表：移除已死亡或超出脱战范围的实体
        PruneThreatTable(nearbyEntities);

        // 选择当前目标（仇恨最高者）
        _currentTargetId = SelectTarget();

        var decision = new MonsterAiDecision();

        if (_currentTargetId == 0)
        {
            // 无目标：巡逻或返回出生点
            if (IsFarFromSpawn())
            {
                _currentAction = MonsterAiAction.ReturnToSpawn;
                decision.Action = MonsterAiAction.ReturnToSpawn;
                decision.MoveTarget = _config.SpawnPoint;
            }
            else
            {
                _currentAction = MonsterAiAction.MoveTo;
                decision.Action = MonsterAiAction.MoveTo;
                decision.MoveTarget = GetNextPatrolPoint();
            }
            return Task.FromResult(decision);
        }

        // 有目标：计算距离
        var target = nearbyEntities.FirstOrDefault(e => e.EntityId == _currentTargetId);
        if (target is null)
        {
            _currentAction = MonsterAiAction.Idle;
            decision.Action = MonsterAiAction.Idle;
            return Task.FromResult(decision);
        }

        var distToTarget = Distance(_position, target);

        if (distToTarget <= _config.AttackRange)
        {
            // 在攻击范围内：攻击
            if (nowMs - _lastAttackTimeMs >= AttackCooldownMs)
            {
                _lastAttackTimeMs = nowMs;
                _currentAction = MonsterAiAction.Attack;
                decision.Action = MonsterAiAction.Attack;
                decision.AttackTargetId = _currentTargetId;
            }
            else
            {
                _currentAction = MonsterAiAction.Idle;
                decision.Action = MonsterAiAction.Idle;
            }
        }
        else
        {
            // 追击目标
            _currentAction = MonsterAiAction.MoveTo;
            decision.Action = MonsterAiAction.MoveTo;
            decision.MoveTarget = new MonsterPosition { X = target.X, Y = target.Y, Z = target.Z };
        }

        return Task.FromResult(decision);
    }

    public Task OnDamagedAsync(ulong attackerId, int damage, bool isCritical)
    {
        if (!_isAlive) return Task.CompletedTask;

        // 仇恨值 = 伤害值（暴击额外 +50%）
        var threat = isCritical ? (int)(damage * 1.5f) : damage;
        _threatTable.TryGetValue(attackerId, out var existing);
        _threatTable[attackerId] = existing + threat;

        // 首次受击：从巡逻切换到追击
        if (_currentAction == MonsterAiAction.Idle || _currentAction == MonsterAiAction.MoveTo)
        {
            _currentTargetId = attackerId;
            _currentAction = MonsterAiAction.MoveTo;
        }

        return Task.CompletedTask;
    }

    public Task OnDeathAsync(ulong killerId)
    {
        _isAlive = false;
        _currentHp = 0;
        _currentAction = MonsterAiAction.Dead;
        _currentTargetId = 0;
        _threatTable.Clear();

        _logger.LogInformation(
            "怪物死亡。MonsterId={MonsterId}, KillerId={KillerId}, Template={Template}",
            this.GetPrimaryKeyLong(), killerId, _config?.TemplateId);

        // TODO Phase 2：掉落系统（LootTableGrain）+ 经验分配
        return Task.CompletedTask;
    }

    public Task RespawnAsync()
    {
        _currentHp = _config?.MaxHp ?? 100;
        _isAlive = true;
        _currentAction = MonsterAiAction.Idle;
        _currentTargetId = 0;
        _threatTable.Clear();
        _patrolIndex = 0;

        if (_config is not null)
        {
            _position = new MonsterPosition
            {
                X = _config.SpawnPoint.X,
                Y = _config.SpawnPoint.Y,
                Z = _config.SpawnPoint.Z,
                Yaw = _config.SpawnPoint.Yaw,
            };
        }

        _logger.LogInformation("怪物复活。MonsterId={MonsterId}", this.GetPrimaryKeyLong());
        return Task.CompletedTask;
    }

    public Task<MonsterState> GetStateAsync()
    {
        return Task.FromResult(new MonsterState
        {
            MonsterId = (ulong)this.GetPrimaryKeyLong(),
            TemplateId = _config?.TemplateId ?? 0,
            Name = _config?.Name ?? string.Empty,
            CurrentHp = _currentHp,
            MaxHp = _config?.MaxHp ?? 0,
            IsAlive = _isAlive,
            CurrentAction = _currentAction,
            Position = _position,
            CurrentTargetId = _currentTargetId,
            Level = _config?.Level ?? 1,
        });
    }

    // --- 私有辅助方法 ---

    private void PruneThreatTable(NearbyEntity[] nearbyEntities)
    {
        if (_threatTable.Count == 0) return;

        var toRemove = new List<ulong>();
        foreach (var (entityId, _) in _threatTable)
        {
            var entity = nearbyEntities.FirstOrDefault(e => e.EntityId == entityId);
            if (entity is null || !entity.IsAlive)
            {
                toRemove.Add(entityId);
                continue;
            }

            // 超出脱战范围
            var dist = Distance(_position, entity);
            if (dist > _config.AggroRange * LeashMultiplier)
            {
                toRemove.Add(entityId);
            }
        }

        foreach (var id in toRemove)
        {
            _threatTable.Remove(id);
        }
    }

    private ulong SelectTarget()
    {
        if (_threatTable.Count == 0) return 0;

        // 返回仇恨值最高的实体
        return _threatTable.OrderByDescending(kv => kv.Value).First().Key;
    }

    private bool IsFarFromSpawn()
    {
        if (_config is null) return false;
        var dist = Distance(_position, new NearbyEntity
        {
            X = _config.SpawnPoint.X,
            Y = _config.SpawnPoint.Y,
            Z = _config.SpawnPoint.Z,
        });
        return dist > _config.AggroRange * 0.5f;
    }

    private MonsterPosition GetNextPatrolPoint()
    {
        if (_config.PatrolPoints.Length == 0)
        {
            return _config.SpawnPoint;
        }

        var point = _config.PatrolPoints[_patrolIndex % _config.PatrolPoints.Length];
        _patrolIndex++;
        return point;
    }

    private static float Distance(MonsterPosition a, NearbyEntity b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static float Distance(MonsterPosition a, MonsterPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
