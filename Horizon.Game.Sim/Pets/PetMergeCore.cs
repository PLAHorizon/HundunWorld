using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>多亲本产出模式。</summary>
public enum PetMergeMode : byte
{
    /// <summary>合成/炼妖（梦幻 §B9）：消耗双亲，产出一只新个体。</summary>
    Fuse = 0,

    /// <summary>合体/繁殖（天龙 §D9）：保留双亲，冷却后产出二代，需限时领取。</summary>
    Breed = 1,
}

/// <summary>参与合成/繁殖的亲本状态快照（只读输入，内核不回写）。</summary>
public readonly struct PetParentSnapshot
{
    /// <summary>实例编号。</summary>
    public readonly long InstanceId;

    /// <summary>物种号。</summary>
    public readonly int SpeciesId;

    /// <summary>等级。</summary>
    public readonly int Level;

    /// <summary>出身。</summary>
    public readonly PetOrigin Origin;

    /// <summary>0 = 雄，1 = 雌，2 = 未知/无性别（繁殖要求异性，§D9）。</summary>
    public readonly int Gender;

    /// <summary>资质（裸资，未含加成）。</summary>
    public readonly PetQualities Qualities;

    /// <summary>成长（bps）。</summary>
    public readonly int GrowthBps;

    /// <summary>当前槽内技能 id（用于继承抽样）。</summary>
    public readonly int[] SkillIds;

    /// <summary>快乐（繁殖要求 100，§D9）。</summary>
    public readonly int Happiness;

    /// <summary>寿命（繁殖要求 ≥1000，§D9）。</summary>
    public readonly int Lifespan;

    /// <summary>已繁殖次数（决定婚配等级，§D9）。</summary>
    public readonly int BreedingCount;

    /// <summary>是否出战中（繁殖要求非出战，§D9）。</summary>
    public readonly bool IsBattling;

    /// <summary>配偶实例编号；0 = 无配偶。</summary>
    public readonly long SpouseInstanceId;

    /// <summary>是否顶过书（梦幻社区口径：顶书胚子的炼妖成功率约为原生 3 倍，§B9 [中]）。</summary>
    public readonly bool HasRebookedSkill;

    public PetParentSnapshot(long instanceId, int speciesId, int level, PetOrigin origin, int gender,
        in PetQualities qualities, int growthBps, int[] skillIds, int happiness, int lifespan,
        int breedingCount, bool isBattling, long spouseInstanceId, bool hasRebookedSkill)
    {
        InstanceId = instanceId;
        SpeciesId = speciesId;
        Level = level;
        Origin = origin;
        Gender = gender;
        Qualities = qualities;
        GrowthBps = growthBps;
        SkillIds = skillIds;
        Happiness = happiness;
        Lifespan = lifespan;
        BreedingCount = breedingCount;
        IsBattling = isBattling;
        SpouseInstanceId = spouseInstanceId;
        HasRebookedSkill = hasRebookedSkill;
    }
}

/// <summary>合成/繁殖被拦截的原因码——九条前置必须能逐条报出，否则玩家只看到"点不动"。</summary>
public enum PetMergeBlockReason : byte
{
    /// <summary>条件齐备。</summary>
    None = 0,

    /// <summary>亲本数量不足（炼妖与繁殖都要两只）。</summary>
    NotEnoughParents = 1,

    /// <summary>等级未达线（炼妖需 ≥30 级 §B9；繁殖需达婚配等级 §D9）。</summary>
    LevelTooLow = 2,

    /// <summary>物种不同型（繁殖要求"同类型"，§D9）。</summary>
    SpeciesMismatch = 3,

    /// <summary>性别不符（繁殖要求异性，§D9）。</summary>
    GenderMismatch = 4,

    /// <summary>等级差超过 5（§D9；双方都达婚配级时此条豁免）。</summary>
    LevelGapTooWide = 5,

    /// <summary>配偶状态冲突（已有配偶且不是彼此，§D9）。</summary>
    SpouseConflict = 6,

    /// <summary>出身不符（繁殖要求皆宝宝、非变异/非二代/非成年，§D9）。</summary>
    OriginNotAllowed = 7,

    /// <summary>快乐/寿命未达线（繁殖要求快乐 100 且寿命 ≥1000，§D9）。</summary>
    BondTooLow = 8,

    /// <summary>出战中不可参与繁殖（§D9）。</summary>
    CurrentlyBattling = 9,

    /// <summary>变异个体不可炼妖（梦幻 §B1）也不可繁殖（天龙 §D9）。</summary>
    MutantExcluded = 10,

    /// <summary>队伍/货币前置未满足（队长携带 ≥2 两黄金，§D9）。</summary>
    PartyOrCostMissing = 11,
}

/// <summary>合成/繁殖的产物。</summary>
public readonly struct PetMergeResult
{
    /// <summary>被拦截原因（<see cref="PetMergeBlockReason.None"/> 才有产物）。</summary>
    public readonly PetMergeBlockReason Blocked;

    /// <summary>产物资质。</summary>
    public readonly PetQualities Qualities;

    /// <summary>产物成长（bps）。</summary>
    public readonly int GrowthBps;

    /// <summary>产物继承到的技能 id。</summary>
    public readonly int[] InheritedSkillIds;

    /// <summary>产物出身（炼妖产物 = <see cref="PetOrigin.Baby"/> 语义上的"回炉胚子"；繁殖产物 = 二代）。</summary>
    public readonly PetOrigin OffspringOrigin;

    /// <summary>产物形态（炼妖重置为幼体；繁殖给幼体/二代）。</summary>
    public readonly PetGeneration OffspringStage;

    /// <summary>产出只数（炼妖 1 只；繁殖通常 2 只，§D9）。</summary>
    public readonly int OffspringCount;

    /// <summary>领取时限（秒）；繁殖产物 48 小时不领取则回归自然（§D9），炼妖即时得到则为 0。</summary>
    public readonly int ClaimWindowSeconds;

    /// <summary>每一项资质实际用的随机带宽（bps），留在结果里是为了"这只为什么是这个资质"可复现。</summary>
    public readonly int QualityBandBps;

    /// <summary>成长实际用的带宽（bps）。</summary>
    public readonly int GrowthBandBps;

    /// <summary>失败时**消耗了什么**——必须显式说明，因为"失败但吃了材料"和"失败没吃材料"在玩家看来是两回事。</summary>
    public readonly bool ParentsConsumed;

    public PetMergeResult(PetMergeBlockReason blocked, in PetQualities qualities, int growthBps,
        int[] inheritedSkillIds, PetOrigin offspringOrigin, PetGeneration offspringStage, int offspringCount,
        int claimWindowSeconds, int qualityBandBps, int growthBandBps, bool parentsConsumed)
    {
        Blocked = blocked;
        Qualities = qualities;
        GrowthBps = growthBps;
        InheritedSkillIds = inheritedSkillIds;
        OffspringOrigin = offspringOrigin;
        OffspringStage = offspringStage;
        OffspringCount = offspringCount;
        ClaimWindowSeconds = claimWindowSeconds;
        QualityBandBps = qualityBandBps;
        GrowthBandBps = growthBandBps;
        ParentsConsumed = parentsConsumed;
    }

    /// <summary>是否成功。<b>与 <see cref="Ok"/> 不同</b>：炼妖"前置全过但概率未命中"时 Blocked 仍为 None
    /// 而产出为 0 只——那种情况必须报 false，否则结算界面会说"合成成功"而玩家手里少了两只宠。</summary>
    public readonly bool Success => Blocked == PetMergeBlockReason.None && OffspringCount > 0;

    /// <summary>是否成功。</summary>
    public readonly bool Ok => Blocked == PetMergeBlockReason.None;
}

/// <summary>
/// 合成（炼妖）与合体（繁殖）的确定性纯函数（逆向规格 §B9/§C9/§D9）。
/// <para>
/// <b>两种模式共用"多亲本 → 新个体"骨架，但必须分开校验</b>：炼妖只看等级和数量（梦幻 §B9），
/// 繁殖看九条前置（天龙 §D9）。把它们并成一个 Validate 会让炼妖也要求"异性"，
/// 而梦幻的炼妖根本不检查性别——这是能观察到的错误行为。
/// </para>
/// <para>
/// <b>继承带宽用定点数存进结果</b>（<see cref="PetMergeResult.QualityBandBps"/>）：
/// 资质 =(A+B)/2 × [70%,110%] 是社区拟合公式（§B9 [低]），把它写死在函数里当"事实"是不诚实的；
/// 让它成为结果字段，编辑器和策划就能在实测后逐档改而不改代码。</para>
/// </summary>
public static class PetMergeCore
{
    /// <summary>合成内核版本。</summary>
    public const int Version = 1;

    /// <summary>炼妖最低等级（梦幻 §B9：2 只 ≥30 级）。</summary>
    public const int FuseMinLevel = 30;

    /// <summary>资质继承带的下界（bps，拟合公式 70%）。</summary>
    public const int FuseQualityMinBps = 7_000;

    /// <summary>资质继承带的上界（bps，拟合公式 110%）。</summary>
    public const int FuseQualityMaxBps = 11_000;

    /// <summary>成长继承带（bps，拟合公式 96%–102%）。</summary>
    public const int FuseGrowthMinBps = 9_600;
    public const int FuseGrowthMaxBps = 10_200;

    /// <summary>炼妖技能继承上限（梦幻 §B9：5+5+2 最高常合出 8 技能）。</summary>
    public const int FuseMaxInheritedSkills = 8;

    /// <summary>顶书胚子的成功率倍率（bps，§B9 [中]："约为原生宠的 3 倍"）。</summary>
    public const int RebookedSuccessMultiplierBps = 30_000;

    /// <summary>炼妖基准成功率（bps）。资料未给绝对数字，取 60% 作可调初值。</summary>
    public const int FuseBaseSuccessBps = 6_000;

    /// <summary>繁殖最低婚配等级（§D9 首档）。</summary>
    public const int MarriageMinLevel = 30;

    /// <summary>繁殖双方的最大等级差（§D9：≤5）。</summary>
    public const int BreedMaxLevelGap = 5;

    /// <summary>繁殖要求的快乐（§D9：=100）。这里按 0–100 刻度写；若模板把快乐统一到 0–1000，由调用方换算后再比较。</summary>
    public const int BreedRequiredHappiness = 100;

    /// <summary>繁殖要求的寿命下限（§D9：≥1000）。</summary>
    public const int BreedRequiredLifespan = 1_000;

    /// <summary>繁殖通常产出 2 只二代（§D9："一对材料 → 通常 2 只"）。</summary>
    public const int BreedOffspringCount = 2;

    /// <summary>繁殖产物领取时限：48 小时（§D9 [高]）。</summary>
    public const int BreedClaimWindowSeconds = 48 * 3600;

    /// <summary>队长需携带的黄金门槛（§D9：≥2 两）；用"两"为单位的整数。</summary>
    public const int BreedGoldCost = 2;

    /// <summary>
    /// 繁殖前置校验（九条，逐条返回原因）。
    /// </summary>
    /// <param name="a">亲本 A。</param>
    /// <param name="b">亲本 B。</param>
    /// <param name="partyGoldAvailable">队长携带黄金（两）。</param>
    public static PetMergeBlockReason ValidateBreed(in PetParentSnapshot a, in PetParentSnapshot b, int partyGoldAvailable)
    {
        if (a.InstanceId == 0 || b.InstanceId == 0) return PetMergeBlockReason.NotEnoughParents;

        // ⑥ 变异不可繁殖（§D9）——必须排在"皆为宝宝"之前：Mutant 同时不满足两条判据，
        // 但只有 MutantExcluded 才是玩家看得懂的原因（"变异宝宝不能配种"，而不是含糊的"出身不符"）。
        if (a.Origin == PetOrigin.Mutant || b.Origin == PetOrigin.Mutant) return PetMergeBlockReason.MutantExcluded;

        // ② 皆为宝宝（同时也排除了成年与二代）
        if (a.Origin != PetOrigin.Baby || b.Origin != PetOrigin.Baby) return PetMergeBlockReason.OriginNotAllowed;

        // ① 同类型
        if (a.SpeciesId != b.SpeciesId) return PetMergeBlockReason.SpeciesMismatch;

        // ③ 异性
        if (a.Gender == b.Gender || a.Gender == 2 || b.Gender == 2) return PetMergeBlockReason.GenderMismatch;

        // ⑦ 非出战（放在等级之前：出战中的宠连面板都进不去，报"等级不够"会误导）
        if (a.IsBattling || b.IsBattling) return PetMergeBlockReason.CurrentlyBattling;

        // ④ 等级差 ≤5，或双方均达婚配级（豁免）
        var marriageA = MarriageLevelFor(a.BreedingCount);
        var marriageB = MarriageLevelFor(b.BreedingCount);
        var bothMarried = a.Level >= marriageA && b.Level >= marriageB;
        if (!bothMarried)
        {
            var gap = a.Level - b.Level;
            if (gap < 0) gap = -gap;
            if (gap > BreedMaxLevelGap) return PetMergeBlockReason.LevelGapTooWide;
        }

        // ④'/⑦ 婚配等级本身要达标
        if (a.Level < marriageA || b.Level < marriageB) return PetMergeBlockReason.LevelTooLow;

        // ⑤ 无配偶或互为配偶
        if (a.SpouseInstanceId != 0 && a.SpouseInstanceId != b.InstanceId) return PetMergeBlockReason.SpouseConflict;
        if (b.SpouseInstanceId != 0 && b.SpouseInstanceId != a.InstanceId) return PetMergeBlockReason.SpouseConflict;

        // ⑦ 快乐 100 且寿命 ≥1000
        if (a.Happiness < BreedRequiredHappiness || b.Happiness < BreedRequiredHappiness) return PetMergeBlockReason.BondTooLow;
        if (a.Lifespan < BreedRequiredLifespan || b.Lifespan < BreedRequiredLifespan) return PetMergeBlockReason.BondTooLow;

        // ⑨ 队长携带 ≥2 两黄金
        if (partyGoldAvailable < BreedGoldCost) return PetMergeBlockReason.PartyOrCostMissing;

        return PetMergeBlockReason.None;
    }

    /// <summary>婚配等级（§D9：从未繁殖 30、1 次 50、2 次 70、3 次 90，每次 +20）。超出取末档 +20 继续。</summary>
    public static int MarriageLevelFor(int breedingCount)
    {
        var table = PetGrowthCore.MarriageLevelByBreedingCount;
        if (breedingCount < 0) breedingCount = 0;
        if (breedingCount < table.Length) return table[breedingCount];
        return table[^1] + (breedingCount - table.Length + 1) * 20;
    }

    /// <summary>
    /// 炼妖（合成）。
    /// <para>概率命中才产出；未命中时<b>材料照吃</b>（梦幻的失败表现就是"胚子没了"），
    /// 所以 <see cref="PetMergeResult.ParentsConsumed"/> 在失败时也必须为 true——
    /// 把它写成 false 会让结算界面说"未消耗"，而玩家手里那两只确实不见了。</para>
    /// </summary>
    /// <param name="a">亲本 A。</param>
    /// <param name="b">亲本 B。</param>
    /// <param name="seed">确定性种子。</param>
    public static PetMergeResult Fuse(in PetParentSnapshot a, in PetParentSnapshot b, long seed)
    {
        if (a.InstanceId == 0 || b.InstanceId == 0)
            return Fail(PetMergeBlockReason.NotEnoughParents, consumed: false);
        if (a.Level < FuseMinLevel || b.Level < FuseMinLevel)
            return Fail(PetMergeBlockReason.LevelTooLow, consumed: false);
        // 变异不可用于炼妖（梦幻 §B1 [高]）
        if (a.Origin == PetOrigin.Mutant || b.Origin == PetOrigin.Mutant)
            return Fail(PetMergeBlockReason.MutantExcluded, consumed: false);

        var roll = PetSkillCore.Roll(seed, 40_001);
        var successBps = FuseBaseSuccessBps;
        // 顶书过的胚子成功率 ×3（§B9 [中]）。判据是"任一方顶过书"，不是"双方都顶过"——
        // 社区经验说的是胚子质量，一只顶过就已经改变了这次炼妖的性质。
        if (a.HasRebookedSkill || b.HasRebookedSkill)
        {
            successBps = (int)((long)successBps * RebookedSuccessMultiplierBps / 10_000L);
            if (successBps > 9_500) successBps = 9_500; // 不给 100%：满概率会让"失败吃材料"这条分支永不触发
        }

        var meanQ = PetQualities.Mean(a.Qualities, b.Qualities);
        var meanGrowth = (a.GrowthBps + b.GrowthBps) / 2;
        var qBand = FuseQualityMinBps + roll % (FuseQualityMaxBps - FuseQualityMinBps + 1);
        var gBand = FuseGrowthMinBps + PetSkillCore.Roll(seed, 40_002) % (FuseGrowthMaxBps - FuseGrowthMinBps + 1);

        if (roll >= successBps)
        {
            // 失败：产物字段全零，但 ParentsConsumed=true 且保留带宽数字，供日志复盘。
            // 真实玩家的观察就是"胚子没了、什么也没得到"，所以这里**不能**报 consumed=false。
            return new PetMergeResult(PetMergeBlockReason.None, PetQualities.Base, 0,
                Array.Empty<int>(), PetOrigin.Baby, PetGeneration.Hatchling, 0, 0, qBand, gBand, true);
        }

        var qualities = new PetQualities
        {
            Attack = (int)((long)meanQ.Attack * qBand / 10_000L),
            Defense = (int)((long)meanQ.Defense * qBand / 10_000L),
            Stamina = (int)((long)meanQ.Stamina * qBand / 10_000L),
            Mana = (int)((long)meanQ.Mana * qBand / 10_000L),
            Speed = (int)((long)meanQ.Speed * qBand / 10_000L),
        };
        var growth = (int)((long)meanGrowth * gBand / 10_000L);
        if (growth < PetStatsCore.GrowthMinBps) growth = PetStatsCore.GrowthMinBps;
        if (growth > PetStatsCore.GrowthMaxBps) growth = PetStatsCore.GrowthMaxBps;

        var inherited = InheritSkills(a.SkillIds, b.SkillIds, seed, FuseMaxInheritedSkills);

        return new PetMergeResult(PetMergeBlockReason.None, qualities, growth, inherited,
            PetOrigin.Baby, PetGeneration.Hatchling, 1, 0, qBand, gBand, true);
    }

    /// <summary>
    /// 繁殖。前置通过才掷产出；产物数 2、领取时限 48 小时（§D9）。
    /// <para>繁殖<b>不产生变异</b>（§D9 [高]），所以产物 Origin 恒为 <see cref="PetOrigin.SecondGeneration"/>。</para>
    /// </summary>
    /// <param name="a">亲本 A。</param>
    /// <param name="b">亲本 B。</param>
    /// <param name="partyGoldAvailable">队长黄金。</param>
    /// <param name="seed">种子。</param>
    /// <param name="hasTimedMutationA">A 是否处于临时变异态（参与 <see cref="ValidateBreed"/> 之外的补充判定）。</param>
    /// <param name="hasTimedMutationB">B 同上。</param>
    public static PetMergeResult Breed(in PetParentSnapshot a, in PetParentSnapshot b, int partyGoldAvailable,
        long seed, bool hasTimedMutationA = false, bool hasTimedMutationB = false)
    {
        var blocked = ValidateBreed(a, b, partyGoldAvailable);
        if (blocked != PetMergeBlockReason.None) return Fail(blocked, consumed: false);
        if (hasTimedMutationA || hasTimedMutationB) return Fail(PetMergeBlockReason.MutantExcluded, consumed: false);

        // 材料宠成长值影响二代成长值（§D9 [高]）；资质取均值但不放宽带——
        // 繁殖的确定性是天龙玩家可观察到的事实（同两只父母多次繁殖资质接近），不像炼妖那样重随。
        var meanQ = PetQualities.Mean(a.Qualities, b.Qualities);
        var meanGrowth = (a.GrowthBps + b.GrowthBps) / 2;

        // 二代相对均值有 ±3% 的小幅浮动（本工程口径，用来避免"完全相同的两只二代"）。
        var jitter = 9_700 + PetSkillCore.Roll(seed, 40_003) % 600;
        var qualities = new PetQualities
        {
            Attack = (int)((long)meanQ.Attack * jitter / 10_000L),
            Defense = (int)((long)meanQ.Defense * jitter / 10_000L),
            Stamina = (int)((long)meanQ.Stamina * jitter / 10_000L),
            Mana = (int)((long)meanQ.Mana * jitter / 10_000L),
            Speed = (int)((long)meanQ.Speed * jitter / 10_000L),
        };

        // 技能继承：繁殖继承"父母会的东西里的一部分"，不突破物种格数上限。
        var inherited = InheritSkills(a.SkillIds, b.SkillIds, seed, FuseMaxInheritedSkills);

        return new PetMergeResult(PetMergeBlockReason.None, qualities, meanGrowth, inherited,
            PetOrigin.SecondGeneration, PetGeneration.Hatchling, BreedOffspringCount,
            BreedClaimWindowSeconds, jitter, jitter, false);
    }

    /// <summary>
    /// 技能继承：取双亲技能并集后抽样。
    /// <para>从并集而不是"各取一半"抽：梦幻玩家可见的结果就是"父母都会的技可能出现在孩子身上"，
    /// 而"各取一半"会让 A 的全部技能在 B 有很多技能时一条都传不下去。</para>
    /// </summary>
    public static int[] InheritSkills(int[] a, int[] b, long seed, int maxCount)
    {
        var union = new System.Collections.Generic.List<int>(16);
        CollectUnique(a, union);
        CollectUnique(b, union);
        if (union.Count == 0) return Array.Empty<int>();

        // 抽样数量 = min(上限, 并集大小)，但**不保证取满**：真实玩家观察到的是"8 格常合出 6–8 技"。
        // 这里用 [55%, 100%] 的带决定取几个，让"合出满技能"成为小概率而不是必然。
        var band = 5_500 + PetSkillCore.Roll(seed, 40_004) % 4_500;
        var take = (int)((long)union.Count * band / 10_000L);
        if (take > maxCount) take = maxCount;
        if (take > union.Count) take = union.Count;
        if (take <= 0) return Array.Empty<int>();

        var picked = new System.Collections.Generic.HashSet<int>();
        var result = new int[take];
        var written = 0;
        var guard = 0;
        while (written < take && guard < take * 8)
        {
            guard++;
            var idx = PetSkillCore.Roll(seed, 40_100 + written) % union.Count;
            if (picked.Add(union[idx])) result[written++] = union[idx];
        }
        // 兜底：哈希撞完仍不足时顺序补齐，保证"要 take 个就真拿到 take 个"。
        for (var i = 0; written < take && i < union.Count; i++)
        {
            if (picked.Add(union[i])) result[written++] = union[i];
        }
        if (written == take) return result;
        var trimmed = new int[written];
        Array.Copy(result, trimmed, written);
        return trimmed;
    }

    private static void CollectUnique(int[] ids, System.Collections.Generic.List<int> into)
    {
        if (ids is null) return;
        for (var i = 0; i < ids.Length; i++)
        {
            if (ids[i] <= 0) continue;
            if (!into.Contains(ids[i])) into.Add(ids[i]);
        }
    }

    private static PetMergeResult Fail(PetMergeBlockReason reason, bool consumed) => new(
        reason, PetQualities.Base, 0, Array.Empty<int>(), PetOrigin.Baby, PetGeneration.Hatchling,
        0, 0, FuseQualityMinBps, FuseGrowthMinBps, consumed);
}
