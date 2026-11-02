using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>捕捉方式（四款游戏各不相同，必须都表达得出来）。</summary>
public enum PetCaptureMode : byte
{
    /// <summary>左键点即捕（天龙 §D1 [高]：无需战斗、无需陷阱）。</summary>
    DirectClick = 0,

    /// <summary>战斗残血后下"捕捉"指令（梦幻 §B1 [高]）。</summary>
    CombatWeaken = 1,

    /// <summary>引导驯服（WoW §A1 [高]：引导 20 秒，期间自己护甲 −100%，不要求残血）。</summary>
    TameGuidance = 2,

    /// <summary>宠物对战打到濒死后收服（WoW BP §A1；概率公式游戏未公开）。</summary>
    BattlePetSubdue = 3,

    /// <summary>不可捕捉（神兽/剧情怪/活动限定，§B1 [高]）。</summary>
    Uncatchable = 4,
}

/// <summary>捕捉被拒绝的原因码。</summary>
public enum PetCaptureBlockReason : byte
{
    /// <summary>可以捕捉。</summary>
    None = 0,

    /// <summary>该物种不可捕捉（神兽/剧情怪）。</summary>
    SpeciesUncatchable = 1,

    /// <summary>玩家宠物栏位已满（WoW §A1 明文要求"有空宠物栏位"）。</summary>
    NoFreeSlot = 2,

    /// <summary>引导被打断（WoW：失去目标/仇恨转移/受击，§A1）。</summary>
    GuidanceInterrupted = 3,

    /// <summary>目标血量还太高（梦幻口径：先打残）。</summary>
    TargetTooHealthy = 4,

    /// <summary>玩家等级低于该物种的携带等级段（§B1/§C1 的品级门槛）。</summary>
    PlayerLevelTooLow = 5,

    /// <summary>已有出战宠，占用中（WoW §A1：必须无出战宠物才能开始驯服）。</summary>
    ActivePetOccupiesSlot = 6,

    /// <summary>目标不是"野兽"类别（WoW §A1：只有标为 Beast 的可驯）。</summary>
    TargetNotBeast = 7,
}

/// <summary>一次捕捉的结果。</summary>
public readonly struct PetCaptureResult
{
    /// <summary>拒绝原因（<see cref="PetCaptureBlockReason.None"/> 才可能成功）。</summary>
    public readonly PetCaptureBlockReason Blocked;

    /// <summary>是否成功。<b>与 Blocked==None 不同</b>：前置都过了但概率未命中时 Blocked 为 None 而本字段为 false。</summary>
    public readonly bool Success;

    /// <summary>掷值（0–9999），留在结果里让"这次为什么没抓住"可复现。</summary>
    public readonly int Roll;

    /// <summary>本次实际概率（bps）。</summary>
    public readonly int ChanceBps;

    /// <summary>成功后生成的个体编号；失败为 0。</summary>
    public readonly long NewInstanceId;

    /// <summary>成功后个体的出身（含变异 roll）。</summary>
    public readonly PetOrigin Origin;

    /// <summary>成功后个体的形态（驯服/捕捉直接成体，或产出蛋）。</summary>
    public readonly PetGeneration Stage;

    /// <summary>成功个体的资质。</summary>
    public readonly PetQualities Qualities;

    /// <summary>成功个体的成长（bps）。</summary>
    public readonly int GrowthBps;

    /// <summary>个体编号解析失败的原因码；与 Blocked 分开，避免把"号段耗尽"报成"栏位满"。</summary>
    public readonly PetIdentityIssue IdentityIssue;

    public PetCaptureResult(PetCaptureBlockReason blocked, bool success, int roll, int chanceBps,
        long newInstanceId, PetOrigin origin, PetGeneration stage, in PetQualities qualities, int growthBps,
        PetIdentityIssue identityIssue)
    {
        Blocked = blocked;
        Success = success;
        Roll = roll;
        ChanceBps = chanceBps;
        NewInstanceId = newInstanceId;
        Origin = origin;
        Stage = stage;
        Qualities = qualities;
        GrowthBps = growthBps;
        IdentityIssue = identityIssue;
    }
}

/// <summary>个体编号生成遇到的问题。</summary>
public enum PetIdentityIssue : byte
{
    /// <summary>无问题。</summary>
    None = 0,

    /// <summary>号段耗尽（该物种已达 <see cref="PetIdentityCore.MaxInstancesPerSpecies"/> 只）。</summary>
    SequenceExhausted = 1,

    /// <summary>物种号非法。</summary>
    IllegalSpecies = 2,
}

/// <summary>捕捉输入的快照。</summary>
public readonly struct PetCaptureRequest
{
    /// <summary>目标物种模板。</summary>
    public readonly PetSpeciesTemplate Species;

    /// <summary>玩家等级。</summary>
    public readonly int PlayerLevel;

    /// <summary>玩家猎术（捕捉技能）等级 0–10。</summary>
    public readonly int CatchSkillLevel;

    /// <summary>目标当前血量比例（bps，0–10000）。</summary>
    public readonly int TargetHpBps;

    /// <summary>目标是否为"野兽"类别（WoW 判据，§A1）。</summary>
    public readonly bool TargetIsBeast;

    /// <summary>玩家当前是否有出战宠（WoW 要求无出战宠才能驯服，§A1）。</summary>
    public readonly bool HasActivePet;

    /// <summary>剩余宠物栏位。</summary>
    public readonly int FreeSlots;

    /// <summary>引导已进行的秒数。</summary>
    public readonly int GuidanceElapsedSeconds;

    /// <summary>引导是否被打断。</summary>
    public readonly bool GuidanceInterrupted;

    /// <summary>场景是否允许捕该物种（梦幻"场景匹配"，§B1）。</summary>
    public readonly bool ZoneAllowsCatch;

    /// <summary>已存在的同物种个体数（用于生成下一个序号，<b>不是用来去重</b>）。</summary>
    public readonly int ExistingInstancesOfSpecies;

    /// <summary>确定性种子。</summary>
    public readonly long Seed;

    public PetCaptureRequest(PetSpeciesTemplate species, int playerLevel, int catchSkillLevel, int targetHpBps,
        bool targetIsBeast, bool hasActivePet, int freeSlots, int guidanceElapsedSeconds,
        bool guidanceInterrupted, bool zoneAllowsCatch, int existingInstancesOfSpecies, long seed)
    {
        Species = species;
        PlayerLevel = playerLevel;
        CatchSkillLevel = catchSkillLevel;
        TargetHpBps = targetHpBps;
        TargetIsBeast = targetIsBeast;
        HasActivePet = hasActivePet;
        FreeSlots = freeSlots;
        GuidanceElapsedSeconds = guidanceElapsedSeconds;
        GuidanceInterrupted = guidanceInterrupted;
        ZoneAllowsCatch = zoneAllowsCatch;
        ExistingInstancesOfSpecies = existingInstancesOfSpecies;
        Seed = seed;
    }
}

/// <summary>
/// 捕捉的确定性纯函数（逆向规格 §A1/§B1/§C1/§D1）。
/// <para>
/// <b>这个模块是"宠物编号 + 重复副本"的出生地</b>：抓到的每一只都要立刻拿到一个永不复用的
/// <c>instance_id</c>，同物种第 2、第 3 只必须照样发得出号（决策口径：宠物被抓后永久存在）。
/// 所以这里的序号来自"该物种已存在多少个个体"，<b>而不是"该物种是否已存在"</b>——
/// 用后者判重的实现会让玩家抓不到第二只同名宠，而这正是四款游戏都不存在的行为。
/// </para>
/// <para>四种捕捉方式共用一条前置流水线，只在概率与"是否要求残血"上分叉。</para>
/// </summary>
public static class PetCaptureCore
{
    /// <summary>捕捉内核版本。</summary>
    public const int Version = 1;

    /// <summary>WoW 驯服引导时长（秒，§A1 [高]：20 秒，部分版本 10 秒）。</summary>
    public const int TameGuidanceSeconds = 20;

    /// <summary>驯服期间猎人的护甲惩罚（bps，−100% = 护甲清零，§A1 [高]）。给界面与战斗内核消费。</summary>
    public const int TamingArmorPenaltyBps = 10_000;

    /// <summary>梦幻口径：目标血量低于该比例才开始有可观成功率（§B1"打残血"）。</summary>
    public const int WeakenThresholdBps = 2_500;

    /// <summary>血越低成功率越高的放大上限（bps）。</summary>
    public const int MaxHpWeakenBonusBps = 4_000;

    /// <summary>猎术每级加成（bps）。</summary>
    public const int CatchSkillBonusPerLevelBps = 600;

    /// <summary>概率带（bps）：任何方式都不许出现 100%，否则"多次尝试必成"会让失败分支永不执行。</summary>
    public const int ChanceCeilingBps = 9_500;

    /// <summary>
    /// 前置校验（顺序即玩家看到的提示优先级）。
    /// <para>先判"这怪根本不能捕"，再判"你条件不够"，最后才判"栏位/出战宠占用"——
    /// 反过来会让玩家为了一个不可捕的神兽去清宠物栏。</para>
    /// </summary>
    public static PetCaptureBlockReason ValidatePreconditions(in PetCaptureRequest request)
    {
        var species = request.Species;
        if (species is null) return PetCaptureBlockReason.SpeciesUncatchable;
        if (species.CaptureMode == PetCaptureMode.Uncatchable) return PetCaptureBlockReason.SpeciesUncatchable;

        // 携带等级段门槛（梦幻 §B1 / 大话 §C1：只能捕与自己等级对应或更低品级）
        if (request.PlayerLevel < species.CarryLevelMin) return PetCaptureBlockReason.PlayerLevelTooLow;

        if (species.CaptureMode == PetCaptureMode.TameGuidance)
        {
            if (!request.TargetIsBeast) return PetCaptureBlockReason.TargetNotBeast;
            if (request.HasActivePet) return PetCaptureBlockReason.ActivePetOccupiesSlot;
            if (request.GuidanceInterrupted) return PetCaptureBlockReason.GuidanceInterrupted;
        }

        if (request.FreeSlots <= 0) return PetCaptureBlockReason.NoFreeSlot;
        if (!request.ZoneAllowsCatch) return PetCaptureBlockReason.SpeciesUncatchable;

        if (species.CaptureMode == PetCaptureMode.CombatWeaken
            && request.TargetHpBps > WeakenThresholdBps)
        {
            return PetCaptureBlockReason.TargetTooHealthy;
        }

        return PetCaptureBlockReason.None;
    }

    /// <summary>
    /// 计算本次成功率（bps）。
    /// <para>基准来自模板的 <c>CaptureBaseChanceBps</c>；残血给加成（梦幻口径），猎术等级给加成，
    /// 驯服方式（WoW）不看血量而是"引导走完就高概率"（§A1 明文"不要求残血"）。</para>
    /// </summary>
    public static int ChanceBps(in PetCaptureRequest request)
    {
        var species = request.Species;
        if (species is null) return 0;

        var chance = species.CaptureBaseChanceBps;
        if (chance < 0) chance = 0;

        switch (species.CaptureMode)
        {
            case PetCaptureMode.CombatWeaken:
            case PetCaptureMode.BattlePetSubdue:
                {
                    var hp = request.TargetHpBps;
                    if (hp < 0) hp = 0;
                    if (hp > 10_000) hp = 10_000;
                    // 血量越低加成越大：从 0（满血）线性到 MaxHpWeakenBonus（空血）。
                    var bonus = (long)(10_000 - hp) * MaxHpWeakenBonusBps / 10_000L;
                    chance += (int)bonus;
                    break;
                }
            case PetCaptureMode.TameGuidance:
                {
                    // 引导走完 = 高概率；没走完一律 0 并**立刻返回**（WoW 里引导没完根本不出结果）。
                    // 这里必须 early-return：往下走会被"猎术等级加成"再捡回来，
                    // 于是引导到一半的驯服也有了可观成功率。
                    var need = species.CaptureGuidanceSeconds > 0 ? species.CaptureGuidanceSeconds : TameGuidanceSeconds;
                    if (request.GuidanceElapsedSeconds < need) return 0;
                    chance = 9_000;
                    break;
                }
            case PetCaptureMode.DirectClick:
                // 瞬捕：概率只由等级与猎术决定（天龙左键即捕，§D1）
                break;
            case PetCaptureMode.Uncatchable:
                return 0;
        }

        var skill = request.CatchSkillLevel;
        if (skill < 0) skill = 0;
        if (skill > 10) skill = 10;
        chance += skill * CatchSkillBonusPerLevelBps;

        if (chance > ChanceCeilingBps) chance = ChanceCeilingBps;
        return chance;
    }

    /// <summary>
    /// 执行一次捕捉：校验 → 概率 → 生成个体。
    /// </summary>
    /// <remarks>
    /// 个体序号 = <c>ExistingInstancesOfSpecies + 1</c>，即"这只物种已经有 N 只，我是第 N+1 只"。
    /// <b>不做任何"同物种已存在就拒绝"的判据</b>：宠物永久存在，重复副本是常态而不是异常。
    /// 序号越界由 <see cref="PetIdentityCore.NewInstanceId"/> 的哨兵返回捕获，转成
    /// <see cref="PetIdentityIssue.SequenceExhausted"/>，绝不静默复用别人的号。
    /// </remarks>
    public static PetCaptureResult Attempt(in PetCaptureRequest request)
    {
        var blocked = ValidatePreconditions(in request);
        if (blocked != PetCaptureBlockReason.None)
            return Fail(blocked, 0, 0, PetIdentityIssue.None);

        var roll = PetSkillCore.Roll(request.Seed, 60_001);
        var chance = ChanceBps(in request);
        var species = request.Species;

        if (roll >= chance)
            return Fail(PetCaptureBlockReason.None, roll, chance, PetIdentityIssue.None);

        // 出身 roll：宝宝/野生由模板允许集合决定；变异独立 roll（§B1 的"极小概率"）。
        var origin = RollOrigin(species, request.Seed);
        var mutation = PetMutationCore.Roll(request.Seed, species.MutationChanceBps);
        if (mutation.Tier > 0) origin = PetOrigin.Mutant;

        var sequence = request.ExistingInstancesOfSpecies + 1;
        var instanceId = PetIdentityCore.NewInstanceId(species.SpeciesId, sequence);
        if (instanceId == PetIdentityCore.UnassignedInstanceId)
        {
            var issue = PetIdentityCore.IsSpeciesId(species.SpeciesId)
                ? PetIdentityIssue.SequenceExhausted
                : PetIdentityIssue.IllegalSpecies;
            return Fail(PetCaptureBlockReason.None, roll, chance, issue);
        }

        // 资质 roll：野生按"21 档"从上限往下分布（§B3），宝宝直接取上段。
        var qualities = RollQualities(species, origin, request.Seed);
        var growth = RollGrowth(species, origin, request.Seed);

        // 形态：模板带孵化时长 → 抓到的是蛋；否则直接成体（WoW/天龙档）。
        var stage = species.HatchSeconds > 0 ? PetGeneration.Egg : PetGeneration.Hatchling;

        return new PetCaptureResult(PetCaptureBlockReason.None, true, roll, chance, instanceId,
            origin, stage, qualities, growth, PetIdentityIssue.None);
    }

    /// <summary>出身 roll（宝宝优先，其次是模板声明的第一个出身）。</summary>
    public static PetOrigin RollOrigin(PetSpeciesTemplate species, long seed)
    {
        var allowed = species.AllowedOrigins;
        if (allowed is null || allowed.Length == 0) return PetOrigin.Wild;

        // "XX宝宝"捕获即宝宝（§B1）；普通怪捕获为野生。这里按允许集合是否为宝宝决定，
        // 不做随机出身：出身在四款游戏里都由"你抓的是什么怪"决定，而不是概率。
        for (var i = 0; i < allowed.Length; i++)
        {
            if (allowed[i] == PetOrigin.Baby) return PetOrigin.Baby;
        }
        return allowed[0];
    }

    /// <summary>
    /// 资质 roll。
    /// <param name="species">物种。</param>
    /// <param name="origin">出身。</param>
    /// <param name="seed">种子。</param>
    /// <remarks>
    /// 野生取"均值偏下"：梦幻野生的 21 档是从最高资质**往下**铺（§B3），所以野生 roll 落在
    /// [min, min + (max−min)×60%]；宝宝允许铺满整个区间。系数 60% 是本工程口径，
    /// "野生普遍差于宝宝"这个方向才是从资料读出来的。
    /// </remarks>
    public static PetQualities RollQualities(PetSpeciesTemplate species, PetOrigin origin, long seed)
    {
        var min = species.QualityMin;
        var max = species.QualityMax;
        var span = origin == PetOrigin.Wild ? 6_000 : 10_000;

        return new PetQualities
        {
            Attack = RollWithin(min.Attack, max.Attack, span, seed, 60_101),
            Defense = RollWithin(min.Defense, max.Defense, span, seed, 60_102),
            Stamina = RollWithin(min.Stamina, max.Stamina, span, seed, 60_103),
            Mana = RollWithin(min.Mana, max.Mana, span, seed, 60_104),
            Speed = RollWithin(min.Speed, max.Speed, span, seed, 60_105),
        };
    }

    /// <summary>成长 roll：从允许档位里等概率取一档；变异允许更高档（§B3"额外 8 档"体现在资质带上）。</summary>
    public static int RollGrowth(PetSpeciesTemplate species, PetOrigin origin, long seed)
    {
        var tiers = species.AllowedGrowthTiers;
        if (tiers is null || tiers.Length == 0) return PetStatsCore.GrowthScale;
        var pick = PetSkillCore.Roll(seed, 60_201) % tiers.Length;
        return PetStatsCore.GrowthBpsOf(tiers[pick]);
    }

    private static int RollWithin(int min, int max, int spanBps, long seed, int salt)
    {
        if (max <= min) return min < 0 ? 0 : min;
        var effectiveMax = (int)(min + (long)(max - min) * spanBps / 10_000L);
        if (effectiveMax > max) effectiveMax = max;
        if (effectiveMax <= min) return min;
        return min + PetSkillCore.Roll(seed, salt) % (effectiveMax - min + 1);
    }

    private static PetCaptureResult Fail(PetCaptureBlockReason blocked, int roll, int chance, PetIdentityIssue issue)
        => new(blocked, false, roll, chance, PetIdentityCore.UnassignedInstanceId, PetOrigin.Wild,
            PetGeneration.Egg, PetQualities.Base, PetStatsCore.GrowthScale, issue);
}
