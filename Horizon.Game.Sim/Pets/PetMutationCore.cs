using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>变异层的作用范围。</summary>
public enum PetMutationLayer : byte
{
    /// <summary>永久变异（出身的一部分，随个体一起存档，不可回退）。</summary>
    Permanent = 0,

    /// <summary>临时变异（带有效期的状态层，到期整体回退，<b>不污染持久身份</b>）。</summary>
    Temporary = 1,
}

/// <summary>永久变异形态。</summary>
public readonly struct PetMutation
{
    /// <summary>变异档位（梦幻"额外 8 档"§B3；天龙二代随机 8–9 种变异等级 §D8）。</summary>
    public readonly int Tier;

    /// <summary>资质上限放大（bps；10000 = 不放大）。梦幻观测带 1.01–1.08（§B1 [高]）。</summary>
    public readonly int QualityCapBps;

    /// <summary>颜色键（外观层消费；<b>不当身份用</b>）。</summary>
    public readonly string ColorKey;

    /// <summary>是否可用于炼妖（变异宝宝不可，§B1 [高]）。</summary>
    public readonly bool CanFuse;

    /// <summary>是否可作繁殖材料（变异不可繁殖，§D9 [高]）。</summary>
    public readonly bool CanBreed;

    /// <summary>寿命上限放大（bps）。变异宝宝"寿命更长"（§B1）。</summary>
    public readonly int LifespanCapBps;

    public PetMutation(int tier, int qualityCapBps, string colorKey, bool canFuse, bool canBreed, int lifespanCapBps)
    {
        Tier = tier;
        QualityCapBps = qualityCapBps < PetStatsCore.GrowthScale ? PetStatsCore.GrowthScale : qualityCapBps;
        ColorKey = colorKey;
        CanFuse = canFuse;
        CanBreed = canBreed;
        LifespanCapBps = lifespanCapBps < PetStatsCore.GrowthScale ? PetStatsCore.GrowthScale : lifespanCapBps;
    }

    /// <summary>无变异。</summary>
    public static PetMutation None => new(0, PetStatsCore.GrowthScale, null, true, true, PetStatsCore.GrowthScale);
}

/// <summary>临时变异状态（染色/幻化/事件变体/限时鎏金）。</summary>
public readonly struct TimedPetMutation
{
    /// <summary>模板 key（指向物种表里的一行临时变异定义）。</summary>
    public readonly string Key;

    /// <summary>生效起始 tick。</summary>
    public readonly long StartTick;

    /// <summary>失效 tick；<b>0 表示从未生效</b>，不能当成"第 0 tick 开始"。</summary>
    public readonly long ExpireTick;

    /// <summary>资质上限放大（bps）。</summary>
    public readonly int QualityCapBps;

    /// <summary>颜色覆盖（显示层）。</summary>
    public readonly string ColorOverride;

    /// <summary>临时追加的技能格数（"限时多一格"是染色类道具常见形态，但资料未证实，默认 0）。</summary>
    public readonly int ExtraSkillSlots;

    /// <summary>层级；本结构只该出现 <see cref="PetMutationLayer.Temporary"/>。</summary>
    public readonly PetMutationLayer Layer;

    public TimedPetMutation(string key, long startTick, long expireTick, int qualityCapBps,
        string colorOverride, int extraSkillSlots, PetMutationLayer layer)
    {
        Key = key;
        StartTick = startTick;
        ExpireTick = expireTick;
        QualityCapBps = qualityCapBps < 1 ? 1 : qualityCapBps;
        ColorOverride = colorOverride;
        ExtraSkillSlots = extraSkillSlots < 0 ? 0 : extraSkillSlots;
        Layer = layer;
    }
}

/// <summary>变异合成结果：给内核其余部分消费的最终带宽。</summary>
public readonly struct PetMutationComposite
{
    /// <summary>资质上限放大（bps），已含永久 × 临时。</summary>
    public readonly int QualityCapBps;

    /// <summary>技能格追加数（只来自当前有效的临时层）。</summary>
    public readonly int ExtraSkillSlots;

    /// <summary>显示色键；null = 用物种基色。</summary>
    public readonly string ColorKey;

    /// <summary>当前是否有生效中的临时层。</summary>
    public readonly bool TemporaryActive;

    /// <summary>临时层剩余 tick；0 = 无。</summary>
    public readonly long RemainingTicks;

    /// <summary>是否允许炼妖（永久变异会关掉它，§B1）。</summary>
    public readonly bool CanFuse;

    /// <summary>是否允许作繁殖材料。</summary>
    public readonly bool CanBreed;

    /// <summary>寿命上限放大（bps）。</summary>
    public readonly int LifespanCapBps;

    public PetMutationComposite(int qualityCapBps, int extraSkillSlots, string colorKey, bool temporaryActive,
        long remainingTicks, bool canFuse, bool canBreed, int lifespanCapBps)
    {
        QualityCapBps = qualityCapBps;
        ExtraSkillSlots = extraSkillSlots;
        ColorKey = colorKey;
        TemporaryActive = temporaryActive;
        RemainingTicks = remainingTicks;
        CanFuse = canFuse;
        CanBreed = canBreed;
        LifespanCapBps = lifespanCapBps;
    }
}

/// <summary>
/// 变异与临时变异的确定性纯函数（逆向规格 §B1/§B8/§C10/§D8/§A10）。
/// <para>
/// <b>两层分开算是本模块的全部要点</b>（决策 D7）：永久变异是"这只宠出身如此"，进身份记录；
/// 临时变异是"这只宠现在这样"，到期整体回退。合并成一层的话，一次染色过期就会连带把
/// 玩家的变异宝宝洗成普通种——这是不可逆的数据损失，而不是显示问题。
/// </para>
/// </summary>
public static class PetMutationCore
{
    /// <summary>变异内核版本。</summary>
    public const int Version = 1;

    /// <summary>变异档位上限（梦幻"额外 8 档"§B3；天龙 8–9 档 §D8）。取 9 覆盖两家观察。</summary>
    public const int MaxMutationTier = 9;

    /// <summary>野生/普通种的基础档数（梦幻"平均 21 档"，§B3）。</summary>
    public const int BaseQualitySteps = PetStatsCore.WildQualitySteps;

    /// <summary>
    /// 每档变异给资质上限的增量（bps）。
    /// <para>梦幻给的是总带 1.01–1.08（§B1）而不是逐档，所以 8 档均分：每档约 +100bps，
    /// 满档 +800bps。天龙的"8–9 种变异等级"未给数字，复用同一族增量。</para>
    /// </summary>
    public const int PerTierQualityBps = 100;

    /// <summary>变异宝宝寿命上限放大（bps）；资料只说"寿命更长"未给数字，取 +20%。</summary>
    public const int MutantLifespanCapBps = 12_000;

    /// <summary>临时变异默认时长（tick）；工程 tick 按 10Hz 计，1 小时 = 36000 tick。</summary>
    public const int TemporaryDurationTicks = 36_000;

    /// <summary>把变异档展开成 <see cref="PetMutation"/>。</summary>
    /// <param name="tier">1–<see cref="MaxMutationTier"/>；0 或越界一律按 <see cref="PetMutation.None"/> 处理。</param>
    /// <param name="colorKey">外观色键。</param>
    public static PetMutation FromTier(int tier, string colorKey)
    {
        if (tier <= 0) return PetMutation.None;
        if (tier > MaxMutationTier) tier = MaxMutationTier;
        return new PetMutation(
            tier,
            PetStatsCore.GrowthScale + tier * PerTierQualityBps,
            colorKey,
            canFuse: false,   // 变异宝宝不可用于炼妖（§B1）
            canBreed: false,  // 变异不可繁殖（§D9）
            MutantLifespanCapBps);
    }

    /// <summary>roll 一个变异档（捕捉/洗炼时调用）。概率表可被模板覆盖。</summary>
    /// <param name="seed">确定性种子。</param>
    /// <param name="chanceBps">发生变异的概率（bps）；梦幻"极小概率"（§B1），取 50bps = 0.5% 作可调初值。</param>
    /// <param name="tierSpread">档位分布宽度（1 = 恒定 1 档）。</param>
    public static PetMutation Roll(long seed, int chanceBps = 50, int tierSpread = MaxMutationTier)
    {
        if (chanceBps <= 0) return PetMutation.None;
        if (PetSkillCore.Roll(seed, 50_001) >= chanceBps) return PetMutation.None;

        var width = tierSpread < 1 ? 1 : (tierSpread > MaxMutationTier ? MaxMutationTier : tierSpread);
        var tier = 1 + PetSkillCore.Roll(seed, 50_002) % width;
        return FromTier(tier, "mutant_t" + tier);
    }

    /// <summary>开启一个临时变异层。</summary>
    public static TimedPetMutation StartTemporary(string key, long currentTick, int durationTicks,
        int qualityCapBps, string colorOverride, int extraSkillSlots)
    {
        var dur = durationTicks <= 0 ? TemporaryDurationTicks : durationTicks;
        var cap = qualityCapBps < PetStatsCore.GrowthScale ? PetStatsCore.GrowthScale : qualityCapBps;
        return new TimedPetMutation(key, currentTick, currentTick + dur, cap, colorOverride,
            extraSkillSlots, PetMutationLayer.Temporary);
    }

    /// <summary>
    /// 临时层此刻是否有效。<b>只有 <see cref="TimedPetMutation.ExpireTick"/> ≤ 0 才代表"从未生效"</b>——
    /// StartTick 为 0 是一个完全合法的开始时刻（开局上的 buff），拿它当哨兵会把这类层一律判成未生效。
    /// </summary>
    public static bool IsTemporaryActive(in TimedPetMutation mutation, long currentTick)
    {
        if (mutation.ExpireTick <= 0) return false;
        return currentTick >= mutation.StartTick && currentTick < mutation.ExpireTick;
    }

    /// <summary>
    /// 回退临时层：返回"永久层仍在、临时层清零"的组合格。
    /// <para><b>到期必须整体回退而不是逐项减</b>：逐项减会在"中途改过永久档"时算错；
    /// 重算永久层再叠加零临时，结果只取决于当前永久状态（幂等，重复调用安全）。</para>
    /// </summary>
    public static PetMutationComposite ExpireTemporary(in PetMutation permanent)
    {
        return Compose(permanent, default, 0L);
    }

    /// <summary>
    /// 合成永久 + 临时两层，得到其余内核消费的最终带宽。
    /// </summary>
    /// <param name="permanent">永久变异（出身）。</param>
    /// <param name="temporary">临时变异（可能未生效）。</param>
    /// <param name="currentTick">当前 tick。</param>
    /// <remarks>
    /// 两层的资质放大<b>相乘</b>而不是相加：相加会让"满变异 + 临时染色"只比单有其一好一点点，
    /// 而玩家观察到的是"临时状态确实又抬了一截"。相乘的溢出由 <see cref="QualityCapCeilingBps"/> 兜住。
    /// </remarks>
    public static PetMutationComposite Compose(in PetMutation permanent, in TimedPetMutation temporary, long currentTick)
    {
        var tempActive = IsTemporaryActive(in temporary, currentTick);

        var permCap = permanent.QualityCapBps <= 0 ? PetStatsCore.GrowthScale : permanent.QualityCapBps;
        var cap = permCap;
        if (tempActive)
        {
            cap = (int)((long)permCap * temporary.QualityCapBps / 10_000L);
            if (cap > QualityCapCeilingBps) cap = QualityCapCeilingBps;
        }

        var slots = tempActive ? temporary.ExtraSkillSlots : 0;
        var color = tempActive && !string.IsNullOrEmpty(temporary.ColorOverride)
            ? temporary.ColorOverride
            : permanent.ColorKey;

        var remaining = tempActive ? temporary.ExpireTick - currentTick : 0L;

        return new PetMutationComposite(
            cap,
            slots,
            color,
            tempActive,
            remaining,
            permanent.CanFuse,
            permanent.CanBreed,
            permanent.LifespanCapBps <= 0 ? PetStatsCore.GrowthScale : permanent.LifespanCapBps);
    }

    /// <summary>两层相乘后的封顶（防止"永久满档 × 临时满档"把资质上限推到不可控的高度）。</summary>
    public const int QualityCapCeilingBps = 15_000;

    /// <summary>把变异上限套到物种资质区间上（返回放大后的上界表）。</summary>
    public static PetQualities ApplyCapToBand(in PetQualities speciesMax, int qualityCapBps)
    {
        if (qualityCapBps <= PetStatsCore.GrowthScale) return speciesMax;
        return speciesMax.ScaledBy(qualityCapBps);
    }
}
