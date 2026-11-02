using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Monsters;

namespace Horizon.Game.Sim.Npcs;

/// <summary>
/// 击杀来源的分档（目标原话"死亡贡献经验（PVP 时或敌对时有效，随玩家等级变化反比例衰减）"的实现口径）。
/// <para>
/// <b>0 是 Unknown，不是任何一条合法来源</b>：<see cref="NpcKillCore.Evaluate"/> 对 Unknown 一律给 0 经验并回理由码，
/// 绝不去猜它是怪还是 NPC——"默认当怪物算"会让一次漏传直接变成凭空发经验。
/// </para>
/// </summary>
public enum NpcKillSource : byte
{
    /// <summary>未登记来源（含越界值）。</summary>
    Unknown = 0,

    /// <summary>敌对 NPC（红名）。给经验。</summary>
    HostileNpc = 1,

    /// <summary>中立 NPC（黄名）。给经验——观测：WoW 里黄名生物击杀同样给经验。</summary>
    NeutralNpc = 2,

    /// <summary>友好 NPC。<b>永不可攻击，也给 0 经验</b>，但仍要出现在回执里：
    /// "友方 NPC 被打死"这类事件需要一条明确的"零经验且理由是立场"的记录，而不是与"没配经验"同形。</summary>
    FriendlyNpc = 3,

    /// <summary>PVP：击杀敌对阵营玩家（Honorable Kill 对应物）。给经验，走<b>同一条让分曲线</b>，
    /// 只是把"目标等级"换成对方玩家等级。</summary>
    PvpPlayer = 4,

    /// <summary>怪物（不是 NPC）。列在这里是为了让分派表覆盖全部来源，
    /// 调用方从同一张表读"这一类给不给经验"，而不是在两侧各写一套 if。</summary>
    Monster = 5,
}

/// <summary>经验为零的理由分档。<b>四类零必须可分辨</b>（记忆 [同族拒绝分支要回同一套对账字段]）。</summary>
public enum NpcKillXpReject : byte
{
    /// <summary>给了经验。</summary>
    None = 0,

    /// <summary>来源不给经验（友好 NPC / Unknown）。</summary>
    SourceGrantsNone = 1,

    /// <summary>让分判成灰名 → 断供（与怪物侧同一个判据，见 <c>MonsterDifficultyCore.GrayLevel</c>）。</summary>
    GrayConcession = 2,

    /// <summary>参与者超出人数上限被截断（与怪物侧 <c>MaxParticipants</c> 同源）。</summary>
    TruncatedByParticipantCap = 3,

    /// <summary>等级输入不可用（非正）。<b>不把等级 0 当成"1 级"算</b>——0 只会来自漏填。</summary>
    BadLevel = 4,

    /// <summary>输出缓冲不够长（调用方缺陷，不是"没人该拿经验"）。
    /// 单列一档是为了让 <c>paid == 0</c> 不再有两种解释（记忆 [预测"不在图里"与"产出零"同形] 的同族坑）。</summary>
    XpBufferTooSmall = 5,
}

/// <summary>一次击杀结算的回执。</summary>
public readonly struct NpcKillVerdict(
    long xp,
    NpcKillSource source,
    NpcKillXpReject reason,
    int targetLevel,
    int observerLevel,
    MonsterDifficulty difficulty)
{
    public readonly long Xp = xp;
    public readonly NpcKillSource Source = source;
    public readonly NpcKillXpReject Reason = reason;
    public readonly int TargetLevel = targetLevel;
    public readonly int ObserverLevel = observerLevel;

    /// <summary>难度档（灰名与"配了零经验"在最终值上同形，所以必须把难度一起带回来）。</summary>
    public readonly MonsterDifficulty Difficulty = difficulty;

    /// <summary>这一类来源原则上给不给经验（与 <see cref="Xp"/> 分开：后者还受让分与参与人数影响）。</summary>
    public readonly bool SourceGrantsXp =>
        source is NpcKillSource.HostileNpc or NpcKillSource.NeutralNpc or NpcKillSource.PvpPlayer or NpcKillSource.Monster;

    public readonly string Describe() =>
        $"kill_src={(byte)Source} kill_xp={Xp} kill_reject={(byte)Reason} " +
        $"kill_tgt={TargetLevel} kill_obs={ObserverLevel} kill_difficulty={(byte)Difficulty}";
}

/// <summary>
/// NPC/PVP 击杀贡献经验的<b>分派层</b>（规格 §12）。
/// <para>
/// <b>本类不含任何经验公式</b>——曲线、让分、灰名断供、参与人加权全部在
/// <see cref="MonsterXpCore"/> / <see cref="MonsterDifficultyCore"/>，
/// 那两处已有独立暴力参照全等级组合对拍（设计文档 §4 的复用清单纪律）。
/// 这里只做一件事：按来源决定"喂不喂得进去"，并把零经验的<b>理由</b>如实报出来。
/// </para>
/// <para>
/// <b>为什么 PVP 也走同一条曲线</b>：目标原话是"随玩家等级变化反比例衰减"，WoW 的 Honorable Kill 观测
/// 正是"大号杀小号不给经验"——那条断供的形状与打灰名怪完全一致（差值超过零差系数即 0）。
/// 另写一条 PvP 曲线会让"为什么这次没经验"出现两个答案。
/// </para>
/// </summary>
public static class NpcKillCore
{
    /// <summary>判据版本（与 <see cref="MonsterXpCore.Version"/> 各自独立：这里改的是分派，不是曲线）。</summary>
    public const int Version = 1;

    /// <summary>这一类来源是否可能给经验（不看待等级差）。</summary>
    public static bool SourceGrantsXp(NpcKillSource source) =>
        source is NpcKillSource.HostileNpc or NpcKillSource.NeutralNpc or NpcKillSource.PvpPlayer or NpcKillSource.Monster;

    /// <summary>把立场翻成击杀来源（装配期的唯一翻译点，别让每个调用方各写一遍）。</summary>
    public static NpcKillSource SourceOf(NpcStance stance) => stance switch
    {
        NpcStance.Friendly => NpcKillSource.FriendlyNpc,
        NpcStance.Neutral => NpcKillSource.NeutralNpc,
        NpcStance.Hostile => NpcKillSource.HostileNpc,
        _ => NpcKillSource.Unknown,
    };

    /// <summary>
    /// PVP 击杀来源：<b>只有敌对阵营才算 <see cref="NpcKillSource.PvpPlayer"/></b>，
    /// 其余一律 <see cref="NpcKillSource.Unknown"/>（= 0 经验）并回阵营判定回执。
    /// <para>判据只有 <see cref="Horizon.Game.Sim.Factions.FactionCore.Evaluate"/> 一份；
    /// 这里只做"阵营敌对 → PVP 来源"的翻译，不另写敌对规则。</para>
    /// </summary>
    public static NpcKillSource SourceOfPvp(int attackerFaction, int defenderFaction, out Factions.FactionVerdict verdict)
    {
        verdict = Factions.FactionCore.Evaluate(attackerFaction, defenderFaction);
        return verdict.Hostile ? NpcKillSource.PvpPlayer : NpcKillSource.Unknown;
    }

    /// <summary>单人击杀回执（NPC/PVP 都走这里；PVP 时 <paramref name="targetLevel"/> 是对方玩家等级）。</summary>
    public static NpcKillVerdict Evaluate(
        NpcKillSource source,
        MonsterXpConfig cfg,
        int targetLevel,
        int observerLevel,
        MonsterRank rank = MonsterRank.Normal)
    {
        if (!SourceGrantsXp(source))
        {
            var badLevel = targetLevel <= 0 || observerLevel <= 0;
            return new NpcKillVerdict(0, source,
                badLevel ? NpcKillXpReject.BadLevel : NpcKillXpReject.SourceGrantsNone,
                targetLevel, observerLevel,
                MonsterDifficultyCore.Of(targetLevel, observerLevel, rank));
        }

        if (targetLevel <= 0 || observerLevel <= 0)
        {
            // 坏等级时 Difficulty 不保证有意义（MonsterDifficulty 没有"无"这一档），调用方一律先看 Reason。
            return new NpcKillVerdict(0, source, NpcKillXpReject.BadLevel, targetLevel, observerLevel,
                MonsterDifficulty.Gray);
        }

        var breakdown = MonsterXpCore.Decompose(cfg, targetLevel, observerLevel, rank);
        var reason = breakdown.Final > 0
            ? NpcKillXpReject.None
            : breakdown.Difficulty == MonsterDifficulty.Gray
                ? NpcKillXpReject.GrayConcession
                : NpcKillXpReject.None;

        return new NpcKillVerdict(breakdown.Final, source, reason, targetLevel, observerLevel, breakdown.Difficulty);
    }

    /// <summary>
    /// 参与者分配。<b>排序、截断、逐人计算全部委托 <see cref="MonsterXpCore"/></b>，
    /// 分派层只决定"这一类来源整组给不给"，绝不重新分配份额（重排一次就多一套判据）。
    /// </summary>
    /// <param name="participants">参与者数组。<b>本方法就地排序</b>（先按贡献时刻新→旧、同刻按角色 id 升序），
    /// 因为 <c>ComputeForParticipants</c> 的前置条件就是"已排序"；调用方若还要原顺序请自行留副本。</param>
    /// <param name="outXp">逐人经验输出缓冲，长度至少 <c>min(participants.Length, MaxParticipants)</c>。</param>
    /// <returns>实际获得经验的人数。<b>0 不区分"没人"与"缓冲不够长"</b>——那两种情况由 <paramref name="reason"/>
    /// 与 <paramref name="outXp"/> 长度对照出来，别只看返回值。</returns>
    public static int AllocateToParticipants(
        NpcKillSource source,
        MonsterXpConfig cfg,
        int targetLevel,
        MonsterRank rank,
        MonsterXpParticipant[] participants,
        Span<long> outXp,
        out NpcKillXpReject reason)
    {
        reason = NpcKillXpReject.None;

        if (!SourceGrantsXp(source))
        {
            reason = NpcKillXpReject.SourceGrantsNone;
            return 0;
        }

        if (targetLevel <= 0 || participants is null || participants.Length == 0)
        {
            reason = NpcKillXpReject.BadLevel;
            return 0;
        }

        // 先排序再截断（口径与怪物侧一致：谁排在后面谁被砍）。
        MonsterXpCore.SortParticipantsByPriority(participants.AsSpan());

        if (participants.Length > MonsterXpCore.MaxParticipants) reason = NpcKillXpReject.TruncatedByParticipantCap;

        var paid = MonsterXpCore.ComputeForParticipants(cfg, targetLevel, rank, participants.AsSpan(), outXp);

        // 缓冲不足时内核回 0，但那不是"没人该拿"——把这一档如实改成 BadLevel 之外的独立理由，避免与"零经验"同形。
        if (paid == 0 && reason == NpcKillXpReject.None
            && outXp.Length < Math.Min(participants.Length, MonsterXpCore.MaxParticipants))
        {
            reason = NpcKillXpReject.XpBufferTooSmall;
        }

        return paid;
    }
}
