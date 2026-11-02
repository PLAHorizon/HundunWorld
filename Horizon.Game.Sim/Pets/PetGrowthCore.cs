using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 形态阶段。
/// <para>四款游戏的"阶段"名目不同（梦幻 野生/宝宝/进阶、大话 点化/化形/飞升、天龙 宝宝/成年/二代/幻化、
/// WoW BP 品质），但玩家看到的都是"这只宠换了一次形态，属性重算、外观改变"。这里统一成四阶段，
/// 各家的名字由 <see cref="PetGrowthCore.StageDisplayName"/> 做显示层映射。</para>
/// </summary>
public enum PetGeneration : byte
{
    /// <summary>蛋/未孵化。</summary>
    Egg = 0,

    /// <summary>幼体（梦幻"宝宝"0 级、天龙"宝宝"）。</summary>
    Hatchling = 1,

    /// <summary>成体（天龙"成年"可作提悟材料，§D1）。</summary>
    Adult = 2,

    /// <summary>觉醒体（梦幻进阶 / 大话觉醒+点化 / 天龙幻化后提灵性）。</summary>
    Awakened = 3,
}

/// <summary>个体出身——决定资质分布与能否参与炼妖/繁殖。</summary>
public enum PetOrigin : byte
{
    /// <summary>野生（直接捕捉的普通怪；梦幻"平均 21 档"资质分布，§B3）。</summary>
    Wild = 0,

    /// <summary>宝宝（0 级、每级 5 潜能点，优于野生，§B1）。</summary>
    Baby = 1,

    /// <summary>成年（可作提悟/吞噬材料，天龙 §D1）。</summary>
    Mature = 2,

    /// <summary>变异（资质上限 ×1.01–1.08、独有色、<b>不可用于炼妖</b>，§B1）。</summary>
    Mutant = 3,

    /// <summary>二代（繁殖产物，§D9；<b>变异不可繁殖</b>，二代也不可再作为繁殖材料）。</summary>
    SecondGeneration = 4,

    /// <summary>神兽（野外不出现、不可捕、出战不掉寿命，§B1/§B6）。</summary>
    Divine = 5,
}

/// <summary>洗炼/重随的结果，必须带"保底是否命中"这一项。</summary>
public readonly struct PetRefineResult
{
    /// <summary>重随后的资质。</summary>
    public readonly PetQualities Qualities;

    /// <summary>重随后的成长率（bps）。</summary>
    public readonly int GrowthBps;

    /// <summary>本次是否触发了"保底必出满技能"（梦幻 §B1：同种达一定次数必出满技能）。</summary>
    public readonly bool GuaranteeHit;

    /// <summary>本次是否把个体洗成了幼体（金柳露"把野生洗成 0 级宝宝"，§B1）。</summary>
    public readonly bool ResetToBaby;

    public PetRefineResult(PetQualities qualities, int growthBps, bool guaranteeHit, bool resetToBaby)
    {
        Qualities = qualities;
        GrowthBps = growthBps;
        GuaranteeHit = guaranteeHit;
        ResetToBaby = resetToBaby;
    }
}

/// <summary>
/// 孵化、升级、蜕变与洗炼的确定性纯函数（逆向规格 §A1/§B1/§B3/§C1/§D3/§D8）。
/// <para>
/// <b>WoW 与国产三家的孵化差异用同一个函数表达</b>：WoW 没有孵化（§A1），但把
/// <c>hatchSeconds</c> 设成 0 就是"捕捉即得"，不需要两条代码路径。这是选参数而不是选分支的理由——
/// 两条路径只有一条会被测试覆盖到。
/// </para>
/// <para>
/// <b>等级上限受主人约束</b>（WoW 宠 ≤ 猎人级 §A3；天龙 宠 ≤ 主人 +10 §D3）表现为"经验条封顶"，
/// 所以 <see cref="LevelCapFor"/> 必须在加经验之前被调用，加完之后调只会得到"已经超了"的事后判断。
/// </para>
/// </summary>
public static class PetGrowthCore
{
    /// <summary>成长内核版本。</summary>
    public const int Version = 1;

    /// <summary>幼体起始等级（梦幻宝宝 0 级，§B1）。</summary>
    public const int BabyStartLevel = 0;

    /// <summary>宠物等级上限。</summary>
    public const int MaxLevel = PetStatsCore.MaxPetLevel;

    /// <summary>主人等级上限（与 <c>MonsterDifficultyCore.MaxPlayerLevel</c> 同值，由对账测试钉住）。</summary>
    public const int MaxOwnerLevel = 300;

    /// <summary>默认允许超出主人的级数（天龙 10，§D3；WoW 0，§A3）。</summary>
    public const int DefaultOwnerLevelSlack = 10;

    /// <summary>
    /// 天龙的"婚配等级"阶梯：从未繁殖 30、已繁殖 1 次 50、2 次 70、3 次 90（每次 +20，§D9 [高]）。
    /// <para>数组长度即"最多繁殖 4 次"这一硬约束的可见形式；超出取末项后判"等级不够"，
    /// 而不是无限 +20——资料只给到 3 次。</para>
    /// </summary>
    public static readonly int[] MarriageLevelByBreedingCount = { 30, 50, 70, 90 };

    /// <summary>
    /// 提悟性失败时的"不回退档"（天龙 §D8：悟性 4/7/9 时失败不回退）——
    /// 这条规则决定了"玩家敢不敢往上冲"，必须原样保留而不是简化成"失败即退"。
    /// </summary>
    public static readonly int[] ComprehensionSafeLevels = { 4, 7, 9 };

    /// <summary>
    /// 融合度提升成功率表（bps，索引 0 = 提升到 1 级）。天龙 §D8 [高]：90/75/60/45/30/30/30/15/10/20%。
    /// <para>末两档 10%→20% 的反转是原文如此，不要"顺手修正"成单调递减。</para>
    /// </summary>
    public static readonly int[] FusionSuccessBps =
        { 9_000, 7_500, 6_000, 4_500, 3_000, 3_000, 3_000, 1_500, 1_000, 2_000 };

    /// <summary>提悟性基准成功率（bps）；等级越高越难（天龙 §D8 只给了趋势未给逐档数字，线性递减为本工程拟合）。</summary>
    public const int ComprehensionBaseSuccessBps = 8_000;

    /// <summary>每提升 1 级悟性，成功率下降的 bps。</summary>
    public const int ComprehensionSuccessDecayBps = 700;

    /// <summary>失败时悟性回落的级数（天龙"失败会降悟性"，取 1 级；在 <see cref="ComprehensionSafeLevels"/> 上不适用）。</summary>
    public const int ComprehensionFailPenalty = 1;

    /// <summary>洗炼保底次数（梦幻 §B1"同种达一定次数必出满技能"；具体次数资料未公开，取 30 作可调初值）。</summary>
    public const int RefineGuaranteeAttempts = 30;

    /// <summary>成长率提升道具（炼兽珍经）每年每宠可用次数（梦幻 §B3：每只每年 5 本）。</summary>
    public const int GrowthBookUsesPerYear = 5;

    /// <summary>
    /// 成长率提升的边际递减（梦幻 §B3 [高]："成长越高收益越小"，例：&lt;1.234 时 +0.005，达 1.285 时 +0.001）。
    /// <para>用两段折线表达"递减"这个形状，而不是给一个恒定 +50——原文给的就是两个不同区间的不同收益，
    /// 单值实现会让满成长宠变成无脑刷。</para>
    /// </summary>
    /// <param name="currentGrowthBps">当前成长。</param>
    /// <returns>本次可提升的 bps。</returns>
    public static int GrowthBookGainBps(int currentGrowthBps)
    {
        if (currentGrowthBps < 12_340) return 50;   // <1.234 → +0.005
        if (currentGrowthBps < 12_850) return 25;   // 中间段取两已知点的中值
        if (currentGrowthBps < 13_500) return 10;
        return 1;                                    // ≥1.285 → +0.001（原文给的第二点）
    }

    /// <summary>孵化所需秒数（<c>0</c> = WoW 档"捕捉即得"）。模板给定时长与出壳等级。</summary>
    public static int HatchSecondsFor(int hatchSeconds) => hatchSeconds < 0 ? 0 : hatchSeconds;

    /// <summary>孵化进度推进：返回新进度与是否完成。进度用秒计，钳到 [0, total]。</summary>
    public static (int ProgressSeconds, bool Hatched) AdvanceHatch(int progressSeconds, int totalSeconds, int dtSeconds)
    {
        if (totalSeconds <= 0) return (0, true);
        var next = progressSeconds + dtSeconds;
        if (next < 0) next = 0;
        if (next >= totalSeconds) return (totalSeconds, true);
        return (next, false);
    }

    /// <summary>等级上限 = min(物种上限, 主人等级 + 余量)；主人未定（0）时只受物种上限约束。</summary>
    public static int LevelCapFor(int ownerLevel, int speciesMaxLevel, int ownerLevelSlack = DefaultOwnerLevelSlack)
    {
        var species = speciesMaxLevel < 1 ? 1 : (speciesMaxLevel > MaxLevel ? MaxLevel : speciesMaxLevel);
        if (ownerLevel <= 0) return species;
        var byOwner = ownerLevel + ownerLevelSlack;
        if (byOwner > MaxLevel) byOwner = MaxLevel;
        return species < byOwner ? species : byOwner;
    }

    /// <summary>
    /// 宠物经验曲线：升到 <paramref name="nextLevel"/> 所需累计经验。
    /// <para>二次型（level² × 系数）——四款游戏都没公开过宠物流水经验，
    /// 但都表现为"前期快、后期慢"，取与怪物经验同族的形状以免两套曲线互相打架。</para>
    /// </summary>
    public static int XpToNextLevel(int nextLevel)
    {
        if (nextLevel < 1) return 0;
        if (nextLevel > MaxLevel) return int.MaxValue;
        var l = (long)nextLevel * nextLevel;
        return (int)(15L + l * 3L / 2L);
    }

    /// <summary>
    /// 宠物获得的主人经验比例（WoW §A3 [中]：宠物获得你经验值的 10%，且同级后不再得经验）。
    /// </summary>
    /// <param name="ownerGainedXp">主人本次获得的经验。</param>
    /// <param name="petLevel">宠物等级。</param>
    /// <param name="ownerLevel">主人等级。</param>
    /// <returns>宠物本次应得经验；<paramref name="petLevel"/> 已达 <paramref name="ownerLevel"/> 时返回 0。</returns>
    public static int ShareOwnerXp(long ownerGainedXp, int petLevel, int ownerLevel)
    {
        if (ownerGainedXp <= 0) return 0;
        if (petLevel >= ownerLevel) return 0; // "同级时不再得经验，需轮换练"（§A3）
        return (int)(ownerGainedXp / 10L);
    }

    /// <summary>
    /// 加经验并结算升级；返回新等级、新剩余经验与本次获得的潜能点。
    /// <para>封顶处理：到达 <paramref name="levelCap"/> 后经验**不再累计**（表现为经验条停止），
    /// 而不是把溢出的经验堆成一个"解封后一次性升满"的暗池——后者会让玩家在升主人级时看到
    /// 宠物连升 10 级，而四款游戏都不是这样。</para>
    /// </summary>
    public static (int Level, int CurrentXp, int GainedPotential) AddXp(int level, int currentXp, long xp,
        int levelCap, int potentialPerLevel = PetStatsCore.PotentialPointsPerLevel)
    {
        var lv = level < 0 ? 0 : level;
        var xpLeft = currentXp + (xp > int.MaxValue ? int.MaxValue : (int)xp);

        var gained = 0;
        while (lv < levelCap)
        {
            var need = XpToNextLevel(lv + 1);
            if (need <= 0 || xpLeft < need) break;
            xpLeft -= need;
            lv++;
            gained += potentialPerLevel < 0 ? 0 : potentialPerLevel;
        }

        if (lv >= levelCap && xpLeft > 0) xpLeft = 0; // 封顶后停止累计（见上）
        if (xpLeft < 0) xpLeft = 0;
        return (lv, xpLeft, gained);
    }

    /// <summary>
    /// 蜕变（进阶/觉醒/点化/幻化）前置校验。
    /// <para>返回原因码而不是布尔：蜕变是多条件合取（等级 + 亲密/忠诚 + 材料），
    /// 玩家必须知道差的是哪一项，否则"点不动"和"点了没反应"在界面上同形。</para>
    /// </summary>
    public static PetMetamorphosisBlockReason CheckMetamorphosis(PetGeneration from, int petLevel,
        int requiredLevel, int loyalty, int requiredLoyalty, int materialsAvailable, int materialsRequired)
    {
        if (from >= PetGeneration.Awakened) return PetMetamorphosisBlockReason.AlreadyApex;
        if (petLevel < requiredLevel) return PetMetamorphosisBlockReason.LevelTooLow;
        if (loyalty < requiredLoyalty) return PetMetamorphosisBlockReason.LoyaltyTooLow;
        if (materialsAvailable < materialsRequired) return PetMetamorphosisBlockReason.MaterialShortage;
        return PetMetamorphosisBlockReason.None;
    }

    /// <summary>蜕变后的新阶段（一次一跳；觉醒体不能再往上）。</summary>
    public static PetGeneration NextStage(PetGeneration from) => from switch
    {
        PetGeneration.Egg => PetGeneration.Hatchling,
        PetGeneration.Hatchling => PetGeneration.Adult,
        PetGeneration.Adult => PetGeneration.Awakened,
        _ => from,
    };

    /// <summary>
    /// 洗炼（金柳露/净瓶玉露/龙之骨）——重随资质与成长。
    /// <para>梦幻 §B1：把野生洗成 0 级宝宝并重随资质/成长/技能；大话 §C3：洗练重置初值+成长，
    /// <b>但不动龙骨数量/等级/修炼/亲密/技能</b>。这两条"重置范围"的差别是真实的玩家可见差异，
    /// 所以用 <paramref name="resetToBaby"/> 一个参数分开表达，而不是两套函数。</para>
    /// </summary>
    /// <param name="min">物种资质区间下界。</param>
    /// <param name="max">物种资质区间上界。</param>
    /// <param name="growthMinBps">成长区间下界。</param>
    /// <param name="growthMaxBps">成长区间上界。</param>
    /// <param name="attemptsBefore">本次之前已洗次数（用于保底）。</param>
    /// <param name="seed">确定性种子。</param>
    /// <param name="resetToBaby">true = 一并降回 0 级宝宝（梦幻口径）。</param>
    public static PetRefineResult Refine(in PetQualities min, in PetQualities max, int growthMinBps,
        int growthMaxBps, int attemptsBefore, long seed, bool resetToBaby)
    {
        // 五维各自独立 roll：梦幻的资质是"每项单独一档"（§B3 的 21 档指单项），
        // 一起 roll 会让"五维同档"这种实际罕见的形态成为默认。
        var q = new PetQualities
        {
            Attack = RollInRange(min.Attack, max.Attack, seed, 1),
            Defense = RollInRange(min.Defense, max.Defense, seed, 2),
            Stamina = RollInRange(min.Stamina, max.Stamina, seed, 3),
            Mana = RollInRange(min.Mana, max.Mana, seed, 4),
            Speed = RollInRange(min.Speed, max.Speed, seed, 5),
        };

        var lo = growthMinBps < PetStatsCore.GrowthMinBps ? PetStatsCore.GrowthMinBps : growthMinBps;
        var hi = growthMaxBps > PetStatsCore.GrowthMaxBps ? PetStatsCore.GrowthMaxBps : growthMaxBps;
        if (hi < lo) hi = lo;
        var growth = (int)(lo + (long)(hi - lo) * PetSkillCore.Roll(seed, 6) / 10_000L);

        // 保底：连续洗够次数后必给"上界全套"，避免玩家无限沉没（梦幻 §B1 的保底语义）。
        var guarantee = attemptsBefore + 1 >= RefineGuaranteeAttempts;
        if (guarantee)
        {
            q = max;
            growth = hi;
        }

        return new PetRefineResult(q, growth, guarantee, resetToBaby);
    }

    /// <summary>提悟性一次尝试（天龙 §D8：失败降 1 级，但 4/7/9 级不回退）。</summary>
    /// <param name="currentLevel">当前悟性 0–10。</param>
    /// <param name="seed">种子。</param>
    /// <returns>新悟性等级与是否失败。</returns>
    public static (int NewLevel, bool Failed) TryComprehension(int currentLevel, long seed)
    {
        var cur = currentLevel < 0 ? 0 : (currentLevel > PetStatsCore.ComprehensionBonusBps.Length ? PetStatsCore.ComprehensionBonusBps.Length : currentLevel);
        if (cur >= PetStatsCore.ComprehensionBonusBps.Length) return (cur, false); // 已满

        var successBps = ComprehensionBaseSuccessBps - cur * ComprehensionSuccessDecayBps;
        if (successBps < 500) successBps = 500;

        var roll = PetSkillCore.Roll(seed, 20_001);
        if (roll < successBps) return (cur + 1, false);

        // 失败：只有"安全档"不回退。判据用的是**提升前的等级**，不是失败后的目标等级。
        if (IsSafeComprehensionLevel(cur)) return (cur, true);
        var down = cur - ComprehensionFailPenalty;
        return (down < 0 ? 0 : down, true);
    }

    /// <summary>该悟性等级失败是否不回退。</summary>
    public static bool IsSafeComprehensionLevel(int level)
    {
        for (var i = 0; i < ComprehensionSafeLevels.Length; i++)
        {
            if (ComprehensionSafeLevels[i] == level) return true;
        }
        return false;
    }

    /// <summary>
    /// 提融合度一次尝试（天龙 §D8：成功率逐档给定，4/7/9 失败不回退——复用悟性的安全档判据）。
    /// </summary>
    /// <param name="currentLevel">当前融合度 0–10。</param>
    /// <param name="seed">种子。</param>
    /// <param name="comprehensionLevel">悟性——资料明说"融合度受悟性/灵性限制"（§D8）。</param>
    /// <param name="spiritLevel">灵性。</param>
    /// <returns>新融合度与是否失败；受前置限制而未掷骰时返回 (cur, false) 且 <paramref name="blockedByPrerequisite"/> 置位。</returns>
    public static (int NewLevel, bool Failed) TryFusion(int currentLevel, long seed, int comprehensionLevel,
        int spiritLevel, out bool blockedByPrerequisite)
    {
        blockedByPrerequisite = false;
        var cur = currentLevel < 0 ? 0 : (currentLevel > FusionSuccessBps.Length ? FusionSuccessBps.Length : currentLevel);
        if (cur >= FusionSuccessBps.Length) { blockedByPrerequisite = false; return (cur, false); }

        // 前置：目标融合度不得高于 悟性与灵性中的较低者 + 4。系数是本工程给的形状，
        // 约束本身（"受悟性/灵性限制"）来自天龙 §D8。
        var gate = Math.Min(comprehensionLevel, spiritLevel) + 4;
        if (cur + 1 > gate)
        {
            blockedByPrerequisite = true;
            return (cur, false);
        }

        var roll = PetSkillCore.Roll(seed, 20_002);
        if (roll < FusionSuccessBps[cur]) return (cur + 1, false);
        if (IsSafeComprehensionLevel(cur)) return (cur, true);
        var down = cur - 1;
        return (down < 0 ? 0 : down, true);
    }

    /// <summary>形态阶段的显示名（仅用于界面；<b>绝不参与任何判等</b>）。</summary>
    public static string StageDisplayName(PetGeneration stage, string family) => (stage, family) switch
    {
        (PetGeneration.Egg, _) => "蛋",
        (PetGeneration.Hatchling, "xyq") => "宝宝",
        (PetGeneration.Hatchling, "tlbb") => "宝宝",
        (PetGeneration.Hatchling, _) => "幼体",
        (PetGeneration.Adult, "tlbb") => "成年",
        (PetGeneration.Adult, _) => "成体",
        (PetGeneration.Awakened, "xy2") => "点化",
        (PetGeneration.Awakened, "tlbb") => "幻化",
        (PetGeneration.Awakened, "xyq") => "进阶",
        (PetGeneration.Awakened, _) => "觉醒体",
    };

    private static int RollInRange(int lo, int hi, long seed, int salt)
    {
        if (hi <= lo) return lo < 0 ? 0 : lo;
        return lo + PetSkillCore.Roll(seed, salt) % (hi - lo + 1);
    }
}

/// <summary>蜕变被拦截的原因码（界面据此写"差在哪一项"）。</summary>
public enum PetMetamorphosisBlockReason : byte
{
    /// <summary>可以蜕变。</summary>
    None = 0,

    /// <summary>等级不足。</summary>
    LevelTooLow = 1,

    /// <summary>忠诚/亲密不足。</summary>
    LoyaltyTooLow = 2,

    /// <summary>材料不足。</summary>
    MaterialShortage = 3,

    /// <summary>已是最高形态。</summary>
    AlreadyApex = 4,
}
