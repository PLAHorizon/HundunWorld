using System;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 模板声明的立场（NPC 对"玩家"这个整体的一视同仁的态度）。
/// <para>
/// <b>这一档与"阵营敌对关系"是两件事，本文档刻意先只做立场</b>：本工程的角色阵营列
/// <c>CharacterEntity.cs:99-101</c>（<c>0-中立 1-正派 2-邪派 3-隐世</c>）今天<b>没有任何服务端代码读写它</b>——
/// 建角时写死 <c>Faction = 0</c>（<c>GameUserGrain.cs:305</c>），客户端角色面板那一格直接显示 "-"
/// （<c>CharacterPanelPage.cs:2725</c> 注释自证"阵营…无数据来源"）。
/// 所以"按阵营算敌对关系"这条轴现在没有输入，硬做就会造出一个永远走默认分支的判据
/// （设计文档 §14 待读 + 决策 D13 已挂起，等用户给阵营口径）。
/// </para>
/// </summary>
public enum NpcStance : byte
{
    /// <summary>友好（绿名）：铁匠、守卫、平民。</summary>
    Friendly = 0,

    /// <summary>中立（黄名）：两侧都能打，打了谁就得罪谁。</summary>
    Neutral = 1,

    /// <summary>敌对（红名）：可击杀、给经验、会主动或应召攻击。</summary>
    Hostile = 2,

    /// <summary>立场未登记/越界。<b>不当成 Friendly 处理</b>——把坏数据兜成"友好"等于让一个敌对 NPC 变成打不了的哑巴。</summary>
    Unknown = 255,
}

/// <summary>名牌色档（只有三档：绿/黄/红。这是玩家可观测的粒度，不做"痛恨/同盟"那五档细分——细分需要阵营轴）。</summary>
public enum NpcNameColor : byte
{
    /// <summary>绿：友好。</summary>
    Green = 0,

    /// <summary>黄：中立。</summary>
    Yellow = 1,

    /// <summary>红：敌对。</summary>
    Red = 2,

    /// <summary>立场未知时的中性灰（客户端必须画得出来，不能回绿）。</summary>
    Gray = 3,
}

/// <summary>"能不能打"的拒绝分档。<b>每个不可打出口回同一组字段</b>（同族拒绝分支纪律）。</summary>
public enum NpcAttackReject : byte
{
    /// <summary>可以打。</summary>
    None = 0,

    /// <summary>友好 NPC：默认不可攻击（观测：打自己的铁匠只会引来守卫）。</summary>
    FriendlyNotAttackable = 1,

    /// <summary>立场数据坏（<see cref="NpcStance.Unknown"/> 或越界）。</summary>
    BadStance = 2,
}

/// <summary>一次反应求值的完整回执。</summary>
/// <param name="Color">名牌色。</param>
/// <param name="CanBeAttacked">玩家能不能对它出手。</param>
/// <param name="GrantsKillXp">击杀它给不给贡献经验。<b>这一位与 <see cref="CanBeAttacked"/> 分开</b>：
/// 允许"可击杀但不给经验"这种设计（剧情必杀 NPC），反之"能打但不给经验"也正是友好 NPC 被 GM 开放攻击时的形状。</param>
/// <param name="ProvokesGuard">被打/被杀是否会引来守卫（观测：WoW 打友方 NPC → 守卫冲过来）。</param>
/// <param name="Reject">不可攻击时的理由档（可攻击时为 <see cref="NpcAttackReject.None"/>）。</param>
public readonly struct NpcReactionVerdict(
    NpcNameColor color,
    bool canBeAttacked,
    bool grantsKillXp,
    bool provokesGuard,
    NpcAttackReject reject)
{
    public readonly NpcNameColor Color = color;
    public readonly bool CanBeAttacked = canBeAttacked;
    public readonly bool GrantsKillXp = grantsKillXp;
    public readonly bool ProvokesGuard = provokesGuard;
    public readonly NpcAttackReject Reject = reject;

    public readonly string Describe() =>
        $"color={(byte)Color} attackable={(CanBeAttacked ? 1 : 0)} grants_xp={(GrantsKillXp ? 1 : 0)} " +
        $"provokes_guard={(ProvokesGuard ? 1 : 0)} reject={(byte)Reject}";
}

/// <summary>
/// 反应轴：立场 → 名牌色 / 可否攻击 / 是否给经验 / 是否引来守卫（规格 §3）。
/// <para>
/// <b>与让分色是两条正交的轴</b>：怪物的名字颜色是 <c>(怪等级 − 我等级)</c> 的函数（<c>MonsterDifficultyCore</c>），
/// NPC 的名字颜色是<b>立场</b>的函数。客户端名牌要把两轴分开取：敌对 NPC 既要有红名（立场），
/// 又要在血量/等级评估上复用让分档（决定"打不打得动、给不给经验"）。
/// 把两轴混成一个颜色，就会出现"高等级友好铁匠被画成骷髅"这类荒诞表现。
/// </para>
/// </summary>
public static class NpcReactionCore
{
    /// <summary>判据版本。</summary>
    public const int Version = 1;

    /// <summary>立场是否已登记。</summary>
    public static bool IsStanceKnown(NpcStance stance) => stance is >= NpcStance.Friendly and <= NpcStance.Hostile;

    /// <summary>名牌色。</summary>
    public static NpcNameColor ColorOf(NpcStance stance) => stance switch
    {
        NpcStance.Friendly => NpcNameColor.Green,
        NpcStance.Neutral => NpcNameColor.Yellow,
        NpcStance.Hostile => NpcNameColor.Red,
        _ => NpcNameColor.Gray,
    };

    /// <summary>
    /// 全量求值。
    /// </summary>
    /// <param name="stance">模板声明的立场。</param>
    /// <param name="attackableOverride">显式覆盖（表里的 <c>can_be_attacked</c> 列）。
    /// <b>口径：−1 = 跟立场；0 = 强制不可打；1 = 强制可打</b>。三态必须存在，
    /// 否则"剧情里必须能杀的友好 NPC"与"永不可打的敌对靶子"都配不出来，
    /// 而只给 bool 的话默认值 false 会把所有没填的敌对 NPC 变成打不动的哑巴
    /// （记忆 [结构体默认 0 会撞成合法下标] 的同族形状）。</param>
    public static NpcReactionVerdict Of(NpcStance stance, int attackableOverride = -1)
    {
        if (!IsStanceKnown(stance))
        {
            // 坏数据：灰名、不可打、不给经验、不引守卫。全部按"什么都不发生"回，并带 BadStance 让 validate 数得出来。
            return new NpcReactionVerdict(NpcNameColor.Gray, false, false, false, NpcAttackReject.BadStance);
        }

        var byStance = stance != NpcStance.Friendly;
        var canAttack = attackableOverride switch
        {
            0 => false,
            1 => true,
            _ => byStance,
        };

        var reject = canAttack ? NpcAttackReject.None : NpcAttackReject.FriendlyNotAttackable;

        // 给经验：友好 NPC 一律 0（观测：杀友方无经验，只有荣誉惩罚/守卫反应）。
        var grantsXp = canAttack && stance != NpcStance.Friendly;

        // 引守卫：只有"打了不该打的"才引——友好 NPC 被打必定引；中立/敌对 NPC 本身就是战斗对象，不引。
        var provokesGuard = stance == NpcStance.Friendly;

        return new NpcReactionVerdict(ColorOf(stance), canAttack, grantsXp, provokesGuard, reject);
    }

    /// <summary>把三态覆盖取成表列值（CLI/装配共用，避免两处各写一遍 <c>-1/0/1</c> 的解释）。</summary>
    public static int NormalizeOverride(int? columnValue) => columnValue.HasValue ? (columnValue.Value == 0 ? 0 : 1) : -1;
}
