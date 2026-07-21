using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Game.Message.Sync;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.2 副本实例 Grain 实现。<br/>
/// 继承 <see cref="SpatialSimulationBase"/> 获得空间模拟能力，<br/>
/// 额外实现：玩家进出管理、副本阶段状态机、超时自动关闭、怪物生成。
/// </summary>
public sealed class InstanceGrain : SpatialSimulationBase, IInstanceGrain
{
    private InstanceConfig _config = null!;
    private InstancePhase _phase = InstancePhase.Waiting;
    private DateTime _createTime;
    private bool _isClosed;

    /// <summary>副本内玩家集合。</summary>
    private readonly HashSet<long> _players = new();

    /// <summary>副本内怪物集合。</summary>
    private readonly HashSet<ulong> _monsters = new();

    /// <summary>超时定时器。</summary>
    private IDisposable? _timeoutTimer;

    public InstanceGrain(ILogger<InstanceGrain> logger) : base(logger)
    {
    }

    protected override long GetSpaceId() => this.GetPrimaryKeyLong();

    public async Task InitializeAsync(InstanceConfig config)
    {
        _config = config;
        _createTime = DateTime.UtcNow;
        _phase = InstancePhase.Waiting;
        _isClosed = false;

        // 注册超时定时器
        var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        _timeoutTimer = this.RegisterGrainTimer(
            OnTimeout,
            new GrainTimerCreationOptions(timeout, timeout));

        Logger.LogInformation(
            "副本初始化。InstanceId={InstanceId}, Template={Template}, Name={Name}, Type={Type}, MaxPlayers={MaxPlayers}",
            GetSpaceId(), config.TemplateId, config.Name, config.Type, config.MaxPlayers);

        await Task.CompletedTask;
    }

    public async Task<bool> EnterAsync(long characterId, float spawnX, float spawnY, float spawnZ)
    {
        if (_isClosed)
        {
            Logger.LogWarning("副本已关闭，拒绝进入。InstanceId={InstanceId}, CharacterId={CharacterId}", GetSpaceId(), characterId);
            return false;
        }

        if (_players.Count >= _config.MaxPlayers)
        {
            Logger.LogWarning("副本已满。InstanceId={InstanceId}, CharacterId={CharacterId}", GetSpaceId(), characterId);
            return false;
        }

        _players.Add(characterId);

        // 注册实体到空间模拟
        await RegisterEntityAsync((ulong)characterId, spawnX, spawnY, spawnZ, 0f, 6f);

        // 首个玩家进入时切换到 InProgress
        if (_phase == InstancePhase.Waiting)
        {
            _phase = InstancePhase.InProgress;
        }

        Logger.LogInformation(
            "玩家进入副本。InstanceId={InstanceId}, CharacterId={CharacterId}, PlayerCount={PlayerCount}",
            GetSpaceId(), characterId, _players.Count);

        return true;
    }

    public async Task LeaveAsync(long characterId)
    {
        _players.Remove(characterId);
        await UnregisterEntityAsync((ulong)characterId);

        Logger.LogInformation(
            "玩家离开副本。InstanceId={InstanceId}, CharacterId={CharacterId}, RemainingPlayers={RemainingPlayers}",
            GetSpaceId(), characterId, _players.Count);

        // 全部玩家离开 → 关闭副本
        if (_players.Count == 0 && _phase != InstancePhase.Waiting)
        {
            await CloseAsync(InstanceCloseReason.AllPlayersLeft);
        }
    }

    public Task<InstanceState> GetStateAsync()
    {
        return Task.FromResult(new InstanceState
        {
            InstanceId = GetSpaceId(),
            TemplateId = _config?.TemplateId ?? 0,
            Name = _config?.Name ?? string.Empty,
            Type = _config?.Type ?? InstanceType.Solo,
            CurrentPlayers = _players.Count,
            MaxPlayers = _config?.MaxPlayers ?? 0,
            Phase = _phase,
            CreateTime = _createTime,
            PlayerIds = _players.ToArray(),
            MonsterCount = _monsters.Count,
            IsClosed = _isClosed,
        });
    }

    public Task CloseAsync(InstanceCloseReason reason)
    {
        if (_isClosed) return Task.CompletedTask;

        _isClosed = true;
        _phase = InstancePhase.Closed;
        _timeoutTimer?.Dispose();

        Logger.LogInformation(
            "副本关闭。InstanceId={InstanceId}, Reason={Reason}, Duration={Duration}s",
            GetSpaceId(), reason, (DateTime.UtcNow - _createTime).TotalSeconds);

        // TODO: 通知所有玩家副本关闭（传送回开放世界）
        // TODO: 通知 InstanceManagerGrain 回收实例
        return Task.CompletedTask;
    }

    public async Task SpawnMonsterAsync(ulong monsterId, int templateId, float x, float y, float z)
    {
        _monsters.Add(monsterId);
        await RegisterEntityAsync(monsterId, x, y, z, 0f, 4f);

        Logger.LogDebug(
            "副本生成怪物。InstanceId={InstanceId}, MonsterId={MonsterId}, Template={Template}",
            GetSpaceId(), monsterId, templateId);
    }

    public Task<CombatVerdictResult> ProcessCombatActionAsync(ulong attackerId, ulong targetId, byte actionKind, int skillId)
    {
        // 简化版战斗裁决（Phase 2 后续由 CombatSystemGrain 接管）
        var result = new CombatVerdictResult();

        if (!Entities.TryGetValue(attackerId, out var attacker) || !attacker.IsAlive)
            return Task.FromResult(result);

        if (!Entities.TryGetValue(targetId, out var target) || !target.IsAlive)
            return Task.FromResult(result);

        // 基础伤害 = 攻击力（简化）
        var damage = Math.Max(1, attacker.Level * 5);
        target.Hp -= damage;
        result.IsHit = true;
        result.DamageAmount = damage;
        result.TargetRemainingHp = Math.Max(0, target.Hp);
        result.TargetMaxHp = target.MaxHp > 0 ? target.MaxHp : 100;

        if (target.Hp <= 0)
        {
            target.IsAlive = false;
            result.IsTargetDead = true;
        }

        return Task.FromResult(result);
    }

    protected override Task BroadcastSnapshotAsync(EntityDelta[] deltas)
    {
        // 副本内广播：所有玩家收到所有 delta（副本空间小，无需 AOI 裁剪）
        // TODO: 通过 Gateway fanout 下发给副本内玩家
        return Task.CompletedTask;
    }

    protected override async Task OnEntityOrphanedAsync(ulong entityId)
    {
        // 玩家孤儿 → 视为离开
        var characterId = (long)entityId;
        if (_players.Contains(characterId))
        {
            await LeaveAsync(characterId);
        }
    }

    private Task OnTimeout(CancellationToken ct)
    {
        if (!_isClosed && _phase != InstancePhase.Completed)
        {
            Logger.LogWarning("副本超时关闭。InstanceId={InstanceId}", GetSpaceId());
            _ = CloseAsync(InstanceCloseReason.Timeout);
        }
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _timeoutTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}
