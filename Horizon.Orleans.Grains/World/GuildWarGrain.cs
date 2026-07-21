using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Horizon.Orleans.Interface.World;

namespace Horizon.Orleans.Grains.World;

/// <summary>
/// P2.4 公会战 Grain 实现。<br/>
/// 管理公会战全流程：报名→匹配→战斗→结算。
/// </summary>
public sealed class GuildWarGrain : Grain, IGuildWarGrain
{
    private readonly ILogger<GuildWarGrain> _logger;
    private GuildWarConfig _config = null!;
    private GuildWarPhase _phase = GuildWarPhase.Registration;
    private DateTime _startTime;

    // 阵营信息
    private long _attackerGuildId;
    private string _attackerGuildName = string.Empty;
    private long _defenderGuildId;
    private string _defenderGuildName = string.Empty;

    // 积分
    private int _attackerScore;
    private int _defenderScore;

    // 参战玩家（characterId → guildId）
    private readonly Dictionary<long, long> _battlePlayers = new();

    // 击杀统计（characterId → kills）
    private readonly Dictionary<long, int> _killCounts = new();

    // 定时器
    private IDisposable? _durationTimer;

    public GuildWarGrain(ILogger<GuildWarGrain> logger)
    {
        _logger = logger;
    }

    public Task InitializeAsync(GuildWarConfig config)
    {
        _config = config;
        _phase = GuildWarPhase.Registration;
        _logger.LogInformation(
            "公会战初始化。WarId={WarId}, Name={Name}, MaxPlayers={MaxPlayers}, Duration={Duration}s",
            config.WarId, config.Name, config.MaxPlayersPerSide, config.DurationSeconds);
        return Task.CompletedTask;
    }

    public Task<GuildWarRegisterResult> RegisterGuildAsync(long guildId, string guildName)
    {
        if (_phase != GuildWarPhase.Registration)
            return Task.FromResult(new GuildWarRegisterResult { Success = false, ErrorMessage = "报名阶段已结束。" });

        // 第一个报名的公会为攻方
        if (_attackerGuildId == 0)
        {
            _attackerGuildId = guildId;
            _attackerGuildName = guildName;
            _logger.LogInformation("公会战攻方报名。WarId={WarId}, Guild={GuildId} ({GuildName})", _config.WarId, guildId, guildName);
            return Task.FromResult(new GuildWarRegisterResult { Success = true, AssignedSide = 0 });
        }

        // 第二个报名的公会为守方
        if (_defenderGuildId == 0)
        {
            if (guildId == _attackerGuildId)
                return Task.FromResult(new GuildWarRegisterResult { Success = false, ErrorMessage = "同一公会不能同时担任攻守双方。" });

            _defenderGuildId = guildId;
            _defenderGuildName = guildName;
            _logger.LogInformation("公会战守方报名。WarId={WarId}, Guild={GuildId} ({GuildName})", _config.WarId, guildId, guildName);

            // 双方就位，进入准备阶段
            _phase = GuildWarPhase.Preparation;
            return Task.FromResult(new GuildWarRegisterResult { Success = true, AssignedSide = 1 });
        }

        return Task.FromResult(new GuildWarRegisterResult { Success = false, ErrorMessage = "参战名额已满。" });
    }

    public Task<bool> JoinBattleAsync(long characterId, long guildId)
    {
        if (_phase != GuildWarPhase.Preparation && _phase != GuildWarPhase.InProgress)
            return Task.FromResult(false);

        if (guildId != _attackerGuildId && guildId != _defenderGuildId)
            return Task.FromResult(false);

        var sideCount = _battlePlayers.Count(kv => kv.Value == guildId);
        if (sideCount >= _config.MaxPlayersPerSide)
            return Task.FromResult(false);

        _battlePlayers[characterId] = guildId;

        // 首次进入时启动战斗计时
        if (_phase == GuildWarPhase.Preparation && _battlePlayers.Count >= 2)
        {
            StartBattle();
        }

        return Task.FromResult(true);
    }

    public Task LeaveBattleAsync(long characterId)
    {
        _battlePlayers.Remove(characterId);
        return Task.CompletedTask;
    }

    public Task RecordKillAsync(long killerId, long victimId)
    {
        if (_phase != GuildWarPhase.InProgress)
            return Task.CompletedTask;

        if (!_battlePlayers.TryGetValue(killerId, out var killerGuild))
            return Task.CompletedTask;

        if (!_battlePlayers.TryGetValue(victimId, out var victimGuild))
            return Task.CompletedTask;

        // 同阵营击杀不计分
        if (killerGuild == victimGuild)
            return Task.CompletedTask;

        // 击杀者阵营加分
        if (killerGuild == _attackerGuildId)
            _attackerScore++;
        else
            _defenderScore++;

        // 记录击杀数
        _killCounts[killerId] = _killCounts.GetValueOrDefault(killerId, 0) + 1;

        _logger.LogDebug(
            "公会战击杀。WarId={WarId}, Killer={Killer}, Victim={Victim}, Score={AttackerScore}:{DefenderScore}",
            _config.WarId, killerId, victimId, _attackerScore, _defenderScore);

        // 检查是否达到胜利条件
        if (_attackerScore >= _config.VictoryScore || _defenderScore >= _config.VictoryScore)
        {
            _ = EndWarAsync();
        }

        return Task.CompletedTask;
    }

    public Task<GuildWarState> GetStateAsync()
    {
        var elapsed = _phase >= GuildWarPhase.InProgress ? (DateTime.UtcNow - _startTime).TotalSeconds : 0;
        var remaining = Math.Max(0, _config.DurationSeconds - (float)elapsed);

        return Task.FromResult(new GuildWarState
        {
            WarId = _config?.WarId ?? 0,
            Phase = _phase,
            AttackerGuildId = _attackerGuildId,
            AttackerGuildName = _attackerGuildName,
            DefenderGuildId = _defenderGuildId,
            DefenderGuildName = _defenderGuildName,
            AttackerScore = _attackerScore,
            DefenderScore = _defenderScore,
            AttackerPlayerCount = _battlePlayers.Count(kv => kv.Value == _attackerGuildId),
            DefenderPlayerCount = _battlePlayers.Count(kv => kv.Value == _defenderGuildId),
            StartTime = _startTime,
            RemainingSeconds = remaining,
        });
    }

    public Task<GuildWarSettlement> EndWarAsync()
    {
        if (_phase == GuildWarPhase.Finished)
            return Task.FromResult(BuildSettlement());

        _phase = GuildWarPhase.Finished;
        _durationTimer?.Dispose();

        var settlement = BuildSettlement();

        _logger.LogInformation(
            "公会战结束。WarId={WarId}, Winner={WinnerGuild} ({WinnerName}), Score={AttackerScore}:{DefenderScore}, MVP={Mvp} (Kills={MvpKills})",
            _config.WarId, settlement.WinnerGuildId, settlement.WinnerGuildName,
            settlement.FinalAttackerScore, settlement.FinalDefenderScore,
            settlement.MvpCharacterId, settlement.MvpKills);

        // TODO: 发放奖励（公会贡献/个人荣誉/物品）
        return Task.FromResult(settlement);
    }

    // --- 内部方法 ---

    private void StartBattle()
    {
        _phase = GuildWarPhase.InProgress;
        _startTime = DateTime.UtcNow;

        _durationTimer = this.RegisterGrainTimer(
            OnDurationTimer,
            new GrainTimerCreationOptions(
                TimeSpan.FromSeconds(_config.DurationSeconds),
                TimeSpan.FromSeconds(_config.DurationSeconds)));

        _logger.LogInformation("公会战开始。WarId={WarId}", _config.WarId);
    }

    private Task OnDurationTimer(CancellationToken ct)
    {
        if (_phase == GuildWarPhase.InProgress)
        {
            _logger.LogInformation("公会战时间到。WarId={WarId}", _config.WarId);
            _ = EndWarAsync();
        }
        return Task.CompletedTask;
    }

    private GuildWarSettlement BuildSettlement()
    {
        var winnerGuildId = _attackerScore >= _defenderScore ? _attackerGuildId : _defenderGuildId;
        var winnerName = _attackerScore >= _defenderScore ? _attackerGuildName : _defenderGuildName;

        var mvp = _killCounts.OrderByDescending(kv => kv.Value).FirstOrDefault();

        return new GuildWarSettlement
        {
            WinnerGuildId = winnerGuildId,
            WinnerGuildName = winnerName,
            FinalAttackerScore = _attackerScore,
            FinalDefenderScore = _defenderScore,
            TotalKills = _killCounts.Values.Sum(),
            DurationSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds,
            MvpCharacterId = mvp.Key,
            MvpKills = mvp.Value,
        };
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _durationTimer?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }
}
