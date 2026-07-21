using System;
using System.Threading.Tasks;
using Orleans;

namespace Horizon.Orleans.Interface.World;

/// <summary>
/// P2.4 公会战 Grain 契约。<br/>
/// Grain Primary Key = guildWarId（由 GuildWarManagerGrain 分配）。<br/>
/// 负责：报名/匹配/战场实例/积分结算/奖励发放。
/// </summary>
[global::Orleans.CodeGeneration.Version(1)]
public interface IGuildWarGrain : IGrainWithIntegerKey
{
    /// <summary>初始化公会战。</summary>
    Task InitializeAsync(GuildWarConfig config);

    /// <summary>公会报名参战。</summary>
    Task<GuildWarRegisterResult> RegisterGuildAsync(long guildId, string guildName);

    /// <summary>玩家加入战场。</summary>
    Task<bool> JoinBattleAsync(long characterId, long guildId);

    /// <summary>玩家离开战场。</summary>
    Task LeaveBattleAsync(long characterId);

    /// <summary>记录击杀。</summary>
    Task RecordKillAsync(long killerId, long victimId);

    /// <summary>获取当前战况。</summary>
    Task<GuildWarState> GetStateAsync();

    /// <summary>结束公会战并结算。</summary>
    Task<GuildWarSettlement> EndWarAsync();
}

/// <summary>公会战配置。</summary>
[GenerateSerializer]
public sealed class GuildWarConfig
{
    [Id(0)] public int WarId { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary>每方最大参战人数。</summary>
    [Id(2)] public int MaxPlayersPerSide { get; set; } = 10;
    /// <summary>战场持续时间（秒）。</summary>
    [Id(3)] public float DurationSeconds { get; set; } = 1800f;
    /// <summary>胜利所需积分。</summary>
    [Id(4)] public int VictoryScore { get; set; } = 100;
    /// <summary>战场实例 ID（InstanceGrain）。</summary>
    [Id(5)] public long BattlefieldInstanceId { get; set; }
}

/// <summary>公会战报名结果。</summary>
[GenerateSerializer]
public sealed class GuildWarRegisterResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ErrorMessage { get; set; } = string.Empty;
    /// <summary>分配的阵营（0=攻方, 1=守方）。</summary>
    [Id(2)] public int AssignedSide { get; set; }
}

/// <summary>公会战状态。</summary>
[GenerateSerializer]
public sealed class GuildWarState
{
    [Id(0)] public long WarId { get; set; }
    [Id(1)] public GuildWarPhase Phase { get; set; }
    [Id(2)] public long AttackerGuildId { get; set; }
    [Id(3)] public string AttackerGuildName { get; set; } = string.Empty;
    [Id(4)] public long DefenderGuildId { get; set; }
    [Id(5)] public string DefenderGuildName { get; set; } = string.Empty;
    [Id(6)] public int AttackerScore { get; set; }
    [Id(7)] public int DefenderScore { get; set; }
    [Id(8)] public int AttackerPlayerCount { get; set; }
    [Id(9)] public int DefenderPlayerCount { get; set; }
    [Id(10)] public DateTime StartTime { get; set; }
    [Id(11)] public float RemainingSeconds { get; set; }
}

/// <summary>公会战结算。</summary>
[GenerateSerializer]
public sealed class GuildWarSettlement
{
    [Id(0)] public long WinnerGuildId { get; set; }
    [Id(1)] public string WinnerGuildName { get; set; } = string.Empty;
    [Id(2)] public int FinalAttackerScore { get; set; }
    [Id(3)] public int FinalDefenderScore { get; set; }
    [Id(4)] public long TotalKills { get; set; }
    [Id(5)] public float DurationSeconds { get; set; }
    /// <summary>MVP 玩家 ID（击杀最多）。</summary>
    [Id(6)] public long MvpCharacterId { get; set; }
    [Id(7)] public int MvpKills { get; set; }
}

/// <summary>公会战阶段。</summary>
[GenerateSerializer]
public enum GuildWarPhase : byte
{
    /// <summary>报名中。</summary>
    Registration = 0,
    /// <summary>准备阶段（进入战场）。</summary>
    Preparation = 1,
    /// <summary>战斗进行中。</summary>
    InProgress = 2,
    /// <summary>已结束。</summary>
    Finished = 3,
}
