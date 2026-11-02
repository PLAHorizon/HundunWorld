using System;

namespace Horizon.Game.Sim.Factions;

/// <summary>
/// 玩家阵营。<b>0 不是"中立可被攻击"，而是"未入阵营"</b>：
/// 未入阵营的角色与任何人都不敌对（记忆 [结构体默认 0 会撞成合法下标] 的同族——
/// 把 0 当成一个可参战的阵营，会让所有未选阵营的玩家开局就互相能打）。
/// </summary>
public enum FactionAlignment : int
{
    /// <summary>未入阵营 / 中立。</summary>
    Unaligned = 0,

    /// <summary>正派。</summary>
    Righteous = 1,

    /// <summary>邪派。</summary>
    Wicked = 2,
}

/// <summary>敌对判定为零时的理由。<b>四类零必须可分辨</b>，否则"打不了"只有一个答案。</summary>
public enum FactionReject : byte
{
    /// <summary>敌对（不是零）。</summary>
    None = 0,

    /// <summary>同阵营：不打。</summary>
    SameAlignment = 1,

    /// <summary>任一方未入阵营：不打。</summary>
    UnalignedInvolved = 2,

    /// <summary>阵营值不在登记表内（越界脏数据）：不打，且要能看出是数据问题而不是"同阵营"。</summary>
    UnknownFaction = 3,
}

/// <summary>一次阵营敌对判定的完整回执。</summary>
public readonly struct FactionVerdict(
    bool hostile,
    FactionAlignment attacker,
    FactionAlignment defender,
    FactionReject reason)
{
    public readonly bool Hostile = hostile;
    public readonly FactionAlignment Attacker = attacker;
    public readonly FactionAlignment Defender = defender;
    public readonly FactionReject Reason = reason;

    public readonly string Describe() =>
        $"faction_hostile={(Hostile ? 1 : 0)} faction_a={(int)Attacker} faction_d={(int)Defender} " +
        $"faction_reject={(byte)Reason}";
}

/// <summary>
/// 阵营敌对矩阵（D13 的落地口径：<b>复用 <c>CharacterEntity.faction</c>，只有一条规则</b>——
/// 正派 ↔ 邪派 互敌，其余（未入阵营、同阵营）一律不敌对）。
/// <para>
/// <b>为什么只有一条规则</b>：WoW 的阵营关系是一张 N×N 表，但本工程只有"正/邪/未入"三档，
/// 把规则写成表只会让"未入阵营"在表里被误读成一个可参战的阵营。
/// 将来要加"门派之间敌对"时，改这里的 <see cref="Evaluate"/> 并补测试，别在调用方另写 if。
/// </para>
/// <para>
/// <b>越界值不当同阵营</b>：脏数据（例如 7）回 <see cref="FactionReject.UnknownFaction"/> 并拒绝参战，
/// 而不是静默归到 0——静默归零会让"数据配错"与"这人没入阵营"长得一模一样。
/// </para>
/// </summary>
public static class FactionCore
{
    /// <summary>判据版本：改 <see cref="Evaluate"/> 的语义时要递增。</summary>
    public const int Version = 1;

    /// <summary>值是否在登记表内。</summary>
    public static bool IsKnown(int faction) =>
        faction == (int)FactionAlignment.Unaligned
        || faction == (int)FactionAlignment.Righteous
        || faction == (int)FactionAlignment.Wicked;

    /// <summary>敌对判定（唯一入口）。</summary>
    public static FactionVerdict Evaluate(int attackerFaction, int defenderFaction)
    {
        var attackerKnown = IsKnown(attackerFaction);
        var defenderKnown = IsKnown(defenderFaction);

        var attacker = attackerKnown ? (FactionAlignment)attackerFaction : FactionAlignment.Unaligned;
        var defender = defenderKnown ? (FactionAlignment)defenderFaction : FactionAlignment.Unaligned;

        if (!attackerKnown || !defenderKnown)
            return new FactionVerdict(false, attacker, defender, FactionReject.UnknownFaction);

        if (attacker == FactionAlignment.Unaligned || defender == FactionAlignment.Unaligned)
            return new FactionVerdict(false, attacker, defender, FactionReject.UnalignedInvolved);

        if (attacker == defender)
            return new FactionVerdict(false, attacker, defender, FactionReject.SameAlignment);

        // 只剩"正 ↔ 邪"这一种组合。
        return new FactionVerdict(true, attacker, defender, FactionReject.None);
    }

    /// <summary>便捷判定（只关心结果时用）。</summary>
    public static bool AreHostile(int attackerFaction, int defenderFaction) =>
        Evaluate(attackerFaction, defenderFaction).Hostile;
}
