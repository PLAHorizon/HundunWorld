using System.Text.Json;
using System.Text.Json.Serialization;

namespace Horizon.PetCli;

/// <summary>资质区间（草稿侧，对应 <c>PetStatBandEntity</c>）。</summary>
public sealed class PetStatBandDraft
{
    public int AttackMin { get; set; }
    public int AttackMax { get; set; }
    public int DefenseMin { get; set; }
    public int DefenseMax { get; set; }
    public int StaminaMin { get; set; }
    public int StaminaMax { get; set; }
    public int ManaMin { get; set; }
    public int ManaMax { get; set; }
    public int SpeedMin { get; set; }
    public int SpeedMax { get; set; }
    public int GrowthMinBps { get; set; }
    public int GrowthMaxBps { get; set; }
    public int ComprehensionMax { get; set; }
    public int SpiritMax { get; set; }
    public bool SuperSpirit { get; set; }
    public int MutationTierMax { get; set; }

    /// <summary>本段的已知键（协议；插件侧 <c>PetStatBandModel</c> 必须逐字相同，由契约测试按文本对账）。</summary>
    public static readonly string[] KnownKeys =
    {
        "attackMin", "attackMax", "defenseMin", "defenseMax", "staminaMin", "staminaMax",
        "manaMin", "manaMax", "speedMin", "speedMax", "growthMinBps", "growthMaxBps",
        "comprehensionMax", "spiritMax", "superSpirit", "mutationTierMax",
    };
}

/// <summary>技能槽位规则（草稿侧，对应 <c>PetSkillRuleEntity</c>）。</summary>
public sealed class PetSkillRuleDraft
{
    public int BaseSlots { get; set; }
    public int AbsoluteMaxSlots { get; set; }
    public string SlotsByGrade { get; set; } = string.Empty;
    public int ComprehensionUnlockA { get; set; }
    public int ComprehensionUnlockB { get; set; }
    public int SlotGrowMaxLevel { get; set; }
    public int SlotGrowMaxExisting { get; set; }
    public int SlotGrowChanceBps { get; set; }
    public int OverwriteChanceBps { get; set; }
    public int MaxLockableSlots { get; set; }
    public int LearnedPoolMax { get; set; }
    public int EquippedMax { get; set; }
    public int InnerCoreSlots { get; set; }

    public static readonly string[] KnownKeys =
    {
        "baseSlots", "absoluteMaxSlots", "slotsByGrade", "comprehensionUnlockA", "comprehensionUnlockB",
        "slotGrowMaxLevel", "slotGrowMaxExisting", "slotGrowChanceBps", "overwriteChanceBps",
        "maxLockableSlots", "learnedPoolMax", "equippedMax", "innerCoreSlots",
    };
}

/// <summary>技能池条目（草稿侧，对应 <c>PetSkillPoolEntity</c>）。</summary>
public sealed class PetSkillEntryDraft
{
    public int SkillId { get; set; }
    /// <summary>0可学 1天生必带 2领悟候选。</summary>
    public byte Kind { get; set; }
    public int InnateSlot { get; set; } = -1;
    public int MinLevel { get; set; }
    public string Conflicts { get; set; } = string.Empty;
    public bool Advanced { get; set; }
    public int Seq { get; set; }

    public static readonly string[] KnownKeys =
    {
        "skillId", "kind", "innateSlot", "minLevel", "conflicts", "advanced", "seq",
    };
}

/// <summary>行为与跟随（草稿侧，对应 <c>PetBehaviorEntity</c>）。</summary>
public sealed class PetBehaviorDraft
{
    public byte IntellectTier { get; set; }
    /// <summary>五个能力位的三态覆盖：null = 跟档位。<b>不能是 0/1 布尔</b>——那会把"没配"当成"关掉"。</summary>
    public byte? FlagUseAcquire { get; set; }
    public byte? FlagUseAutoCast { get; set; }
    public byte? FlagUseEvade { get; set; }
    public byte? FlagUseGuard { get; set; }
    public byte? FlagUseHold { get; set; }
    public byte DefaultStance { get; set; }
    public byte DefaultCommand { get; set; }
    public string AllowedPersonalities { get; set; } = string.Empty;
    public int EpiphanyChanceBps { get; set; }
    public float FollowDistance { get; set; }
    public float LeashRadius { get; set; }
    public float TeleportBeyondRadius { get; set; }
    public int RepathIntervalTicks { get; set; }
    public float FollowSpeed { get; set; }
    public float ArriveEpsilon { get; set; }
    public int CorpseSeconds { get; set; }
    public int ReviveCastSeconds { get; set; }
    public int SummonCooldownSeconds { get; set; }
    public int FollowStartDelayMs { get; set; }

    /// <summary>初始四轴默契（草稿里给的是"出生值"）。</summary>
    public int InitialLoyalty { get; set; }
    public int InitialHappiness { get; set; }
    public int InitialLifespan { get; set; }
    public int InitialIntimacy { get; set; }

    public static readonly string[] KnownKeys =
    {
        "intellectTier", "flagUseAcquire", "flagUseAutoCast", "flagUseEvade", "flagUseGuard", "flagUseHold",
        "defaultStance", "defaultCommand", "allowedPersonalities", "epiphanyChanceBps",
        "followDistance", "leashRadius", "teleportBeyondRadius", "repathIntervalTicks", "followSpeed",
        "arriveEpsilon", "corpseSeconds", "reviveCastSeconds", "summonCooldownSeconds", "followStartDelayMs",
        "initialLoyalty", "initialHappiness", "initialLifespan", "initialIntimacy",
    };
}

/// <summary>装备规则（草稿侧，对应 <c>PetEquipRuleEntity</c>）。</summary>
public sealed class PetEquipRuleDraft
{
    public string AllowedSlots { get; set; } = string.Empty;
    public bool GemAllowed { get; set; }
    public int MaxStar { get; set; }
    public byte SetFamily { get; set; }
    public bool AppraisalEnabled { get; set; }
    public bool AwakeningEnabled { get; set; }
    public int AwakeningPieceCount { get; set; }
    public byte Ownership { get; set; }

    public static readonly string[] KnownKeys =
    {
        "allowedSlots", "gemAllowed", "maxStar", "setFamily", "appraisalEnabled",
        "awakeningEnabled", "awakeningPieceCount", "ownership",
    };
}

/// <summary>辅助（反哺）条目（草稿侧，对应 <c>PetAssistRuleEntity</c>）。</summary>
public sealed class PetAssistEntryDraft
{
    public int SkillId { get; set; }
    public byte AssistKind { get; set; }
    public bool RequiresBattle { get; set; } = true;
    public bool Possessable { get; set; } = true;
    public int CoefficientBps { get; set; }
    public int Seq { get; set; }

    public static readonly string[] KnownKeys =
    {
        "skillId", "assistKind", "requiresBattle", "possessable", "coefficientBps", "seq",
    };
}

/// <summary>合成/繁殖规则（草稿侧，对应 <c>PetMergeRuleEntity</c>）。</summary>
public sealed class PetMergeRuleDraft
{
    public bool FuseEnabled { get; set; }
    public bool BreedEnabled { get; set; }
    public int FuseMinLevel { get; set; }
    public int FuseSuccessBps { get; set; }
    public int FuseQualityMinBps { get; set; }
    public int FuseQualityMaxBps { get; set; }
    public int FuseGrowthMinBps { get; set; }
    public int FuseGrowthMaxBps { get; set; }
    public int FuseMaxInheritedSkills { get; set; }
    public bool FuseConsumesParents { get; set; } = true;
    public int BreedMaxLevelGap { get; set; }
    public int BreedRequiredHappiness { get; set; }
    public int BreedRequiredLifespan { get; set; }
    public int BreedOffspringCount { get; set; }
    public int BreedClaimWindowSeconds { get; set; }
    public int BreedCost { get; set; }

    public static readonly string[] KnownKeys =
    {
        "fuseEnabled", "breedEnabled", "fuseMinLevel", "fuseSuccessBps",
        "fuseQualityMinBps", "fuseQualityMaxBps", "fuseGrowthMinBps", "fuseGrowthMaxBps",
        "fuseMaxInheritedSkills", "fuseConsumesParents", "breedMaxLevelGap", "breedRequiredHappiness",
        "breedRequiredLifespan", "breedOffspringCount", "breedClaimWindowSeconds", "breedCost",
    };
}

/// <summary>临时变异模板条目（草稿侧，对应 <c>PetMutationRuleEntity</c>）。</summary>
public sealed class PetMutationEntryDraft
{
    public string MutationKey { get; set; } = string.Empty;
    public int DurationTicks { get; set; }
    public int QualityCapBps { get; set; }
    public string? ColorOverride { get; set; }
    public int ExtraSkillSlots { get; set; }
    public bool Enabled { get; set; } = true;

    public static readonly string[] KnownKeys =
    {
        "mutationKey", "durationTicks", "qualityCapBps", "colorOverride", "extraSkillSlots", "enabled",
    };
}

/// <summary>
/// 宠物物种草稿（一份草稿 = 九张模板侧表的组合体）。
/// <para>
/// <b>为什么草稿在 Redis 而不在 SQL</b>：未校验通过的半份进库会立刻被运行时装配器读走
/// （它只按 publish_state 过滤，读不懂"这是半成品"）。Redis 把"编辑期"与"运行期"物理分开，
/// <c>publish</c> 是那唯一的一次跨线写入。
/// </para>
/// <para>
/// <b>字段名是跨进程协议</b>：Flax 插件的脚本域引用不到本工程，插件里另存一份同名字段模型，
/// 两侧由 <c>PetEditorContractTests</c> 按**源文本**逐字对账。改这里就必须同时改那里，
/// 而漏改的那一侧不会编译失败——只会静默丢字段（STJ 默认不绑公开字段、未知键不报错）。
/// </para>
/// </summary>
public sealed class PetDraft
{
    public int SpeciesId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ModelKey { get; set; } = string.Empty;
    public string? PrefabPath { get; set; }
    public string? PrefabGuid { get; set; }
    public decimal ModelScale { get; set; } = 1.0m;
    public string StandState { get; set; } = "Idle";
    public string BaseColorKey { get; set; } = "base";
    public int CarryLevelMin { get; set; }
    public int CarryLevelMax { get; set; } = 175;
    public string AllowedOrigins { get; set; } = "0,1";
    public string AllowedGrowthTiers { get; set; } = "0,1,2";
    public int HatchSeconds { get; set; }
    public int HatchLevel { get; set; }
    public int OwnerLevelSlack { get; set; } = 10;
    public int PotentialPerLevel { get; set; } = 5;
    public bool ImmortalLifespan { get; set; }
    public int HappinessScale { get; set; } = 1000;
    public byte CaptureMode { get; set; } = 1;
    public int CaptureBaseBps { get; set; } = 3000;
    public int CaptureGuidanceSeconds { get; set; }
    public int MutationChanceBps { get; set; }
    public byte PublishState { get; set; }
    public int ContentVersion { get; set; } = 1;
    public string Remark { get; set; } = string.Empty;

    public PetStatBandDraft Stats { get; set; } = new();
    public PetSkillRuleDraft SkillRule { get; set; } = new();
    public List<PetSkillEntryDraft> SkillEntries { get; set; } = new();
    public PetBehaviorDraft Behavior { get; set; } = new();
    public PetEquipRuleDraft Equip { get; set; } = new();
    public List<PetAssistEntryDraft> AssistEntries { get; set; } = new();
    public PetMergeRuleDraft Merge { get; set; } = new();
    public List<PetMutationEntryDraft> Mutations { get; set; } = new();

    /// <summary>顶层已知键（协议自证用；未知键一律拒绝，不让 STJ 静默丢弃）。</summary>
    public static readonly string[] KnownTopLevelKeys =
    {
        "speciesId", "name", "modelKey", "prefabPath", "prefabGuid", "modelScale", "standState",
        "baseColorKey", "carryLevelMin", "carryLevelMax", "allowedOrigins", "allowedGrowthTiers",
        "hatchSeconds", "hatchLevel", "ownerLevelSlack", "potentialPerLevel", "immortalLifespan",
        "happinessScale", "captureMode", "captureBaseBps", "captureGuidanceSeconds", "mutationChanceBps",
        "publishState", "contentVersion", "remark",
        "stats", "skillRule", "skillEntries", "behavior", "equip", "assistEntries", "merge", "mutations",
    };

    /// <summary>所有子段的已知键集合（按段名索引，供深层未知键检查）。</summary>
    public static readonly Dictionary<string, string[]> KnownSectionKeys = new()
    {
        ["stats"] = PetStatBandDraft.KnownKeys,
        ["skillRule"] = PetSkillRuleDraft.KnownKeys,
        ["skillEntries"] = PetSkillEntryDraft.KnownKeys,
        ["behavior"] = PetBehaviorDraft.KnownKeys,
        ["equip"] = PetEquipRuleDraft.KnownKeys,
        ["assistEntries"] = PetAssistEntryDraft.KnownKeys,
        ["merge"] = PetMergeRuleDraft.KnownKeys,
        ["mutations"] = PetMutationEntryDraft.KnownKeys,
    };

    /// <summary>两侧共用的 JSON 约定：camelCase、不写 null、缩进。</summary>
    public static readonly JsonSerializerOptions Contract = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Contract);

    public static PetDraft? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PetDraft>(json, Contract);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 新建草稿：物种号由调用方给（<c>PetIdentityCore.NextSpeciesId</c>），
    /// 各段填入内核默认值——**不在这里重写一遍默认数**，否则默认值就有两份事实源。
    /// </summary>
    public static PetDraft CreateNew(int suggestedSpeciesId)
    {
        var slot = Horizon.Game.Sim.Pets.PetSlotRuleConfig.Default;
        var follow = Horizon.Game.Sim.Pets.PetFollowConfig.Default;
        var bond = Horizon.Game.Sim.Pets.PetBondValues.Default;
        var minQ = Horizon.Game.Sim.Pets.PetQualities.Base;
        var maxQ = new Horizon.Game.Sim.Pets.PetQualities
        {
            Attack = 1500, Defense = 1500, Stamina = 1500, Mana = 1500, Speed = 1500,
        };

        return new PetDraft
        {
            SpeciesId = suggestedSpeciesId,
            Name = "新宠物_" + suggestedSpeciesId,
            ModelKey = "pet_" + suggestedSpeciesId,
            ModelScale = 1.0m,
            StandState = "Idle",
            BaseColorKey = "base",
            CarryLevelMin = 0,
            CarryLevelMax = 60,
            OwnerLevelSlack = Horizon.Game.Sim.Pets.PetGrowthCore.DefaultOwnerLevelSlack,
            PotentialPerLevel = Horizon.Game.Sim.Pets.PetStatsCore.PotentialPointsPerLevel,
            HappinessScale = Horizon.Game.Sim.Pets.PetBondCore.HappinessMax,
            CaptureMode = (byte)Horizon.Game.Sim.Pets.PetCaptureMode.CombatWeaken,
            CaptureBaseBps = 3000,
            MutationChanceBps = 50,
            ContentVersion = 1,
            Stats = new PetStatBandDraft
            {
                AttackMin = minQ.Attack, AttackMax = maxQ.Attack,
                DefenseMin = minQ.Defense, DefenseMax = maxQ.Defense,
                StaminaMin = minQ.Stamina, StaminaMax = maxQ.Stamina,
                ManaMin = minQ.Mana, ManaMax = maxQ.Mana,
                SpeedMin = minQ.Speed, SpeedMax = maxQ.Speed,
                GrowthMinBps = Horizon.Game.Sim.Pets.PetStatsCore.GrowthBpsOf(Horizon.Game.Sim.Pets.PetGrowthTier.Ordinary),
                GrowthMaxBps = Horizon.Game.Sim.Pets.PetStatsCore.GrowthBpsOf(Horizon.Game.Sim.Pets.PetGrowthTier.Fine),
                ComprehensionMax = Horizon.Game.Sim.Pets.PetStatsCore.ComprehensionBonusBps.Length,
                SpiritMax = Horizon.Game.Sim.Pets.PetStatsCore.SpiritBonusNormalBps.Length,
                MutationTierMax = Horizon.Game.Sim.Pets.PetMutationCore.MaxMutationTier,
            },
            SkillRule = new PetSkillRuleDraft
            {
                BaseSlots = slot.BaseSlots,
                AbsoluteMaxSlots = slot.AbsoluteMaxSlots,
                SlotsByGrade = string.Empty,
                ComprehensionUnlockA = slot.ComprehensionUnlockA,
                ComprehensionUnlockB = slot.ComprehensionUnlockB,
                SlotGrowMaxLevel = slot.SlotGrowMaxBattleLevel,
                SlotGrowMaxExisting = slot.SlotGrowMaxExistingSkills,
                SlotGrowChanceBps = slot.SlotGrowChanceBps,
                OverwriteChanceBps = slot.OverwriteChanceBps,
                MaxLockableSlots = slot.MaxLockableSlots,
                LearnedPoolMax = slot.LearnedPoolMax,
                EquippedMax = slot.EquippedMax,
                InnerCoreSlots = 1,
            },
            Behavior = new PetBehaviorDraft
            {
                IntellectTier = (byte)Horizon.Game.Sim.Pets.PetIntellectTier.Trained,
                DefaultStance = (byte)Horizon.Game.Sim.Pets.PetStance.Aggressive,
                DefaultCommand = (byte)Horizon.Game.Sim.Pets.PetCommand.Follow,
                AllowedPersonalities = "0,3,4",
                EpiphanyChanceBps = 1000,
                FollowDistance = follow.FollowDistance,
                LeashRadius = follow.LeashRadius,
                TeleportBeyondRadius = follow.TeleportBeyondRadius,
                RepathIntervalTicks = follow.RepathIntervalTicks,
                FollowSpeed = follow.Speed,
                ArriveEpsilon = follow.ArriveEpsilon,
                CorpseSeconds = follow.CorpseSeconds,
                ReviveCastSeconds = follow.ReviveCastSeconds,
                SummonCooldownSeconds = follow.SummonCooldownSeconds,
                FollowStartDelayMs = follow.FollowStartDelayMs,
                InitialLoyalty = bond.Loyalty,
                InitialHappiness = bond.Happiness,
                InitialLifespan = bond.Lifespan,
                InitialIntimacy = bond.Intimacy,
            },
            Equip = new PetEquipRuleDraft
            {
                AllowedSlots = "0,1,2",
                GemAllowed = true,
                MaxStar = Horizon.Game.Sim.Pets.PetEquipmentCore.MaxStar,
                SetFamily = 1,
                AppraisalEnabled = true,
                AwakeningEnabled = false,
                AwakeningPieceCount = 3,
                Ownership = 1,
            },
            Merge = new PetMergeRuleDraft
            {
                FuseEnabled = true,
                BreedEnabled = true,
                FuseMinLevel = Horizon.Game.Sim.Pets.PetMergeCore.FuseMinLevel,
                FuseSuccessBps = Horizon.Game.Sim.Pets.PetMergeCore.FuseBaseSuccessBps,
                FuseQualityMinBps = Horizon.Game.Sim.Pets.PetMergeCore.FuseQualityMinBps,
                FuseQualityMaxBps = Horizon.Game.Sim.Pets.PetMergeCore.FuseQualityMaxBps,
                FuseGrowthMinBps = Horizon.Game.Sim.Pets.PetMergeCore.FuseGrowthMinBps,
                FuseGrowthMaxBps = Horizon.Game.Sim.Pets.PetMergeCore.FuseGrowthMaxBps,
                FuseMaxInheritedSkills = Horizon.Game.Sim.Pets.PetMergeCore.FuseMaxInheritedSkills,
                FuseConsumesParents = true,
                BreedMaxLevelGap = Horizon.Game.Sim.Pets.PetMergeCore.BreedMaxLevelGap,
                BreedRequiredHappiness = Horizon.Game.Sim.Pets.PetMergeCore.BreedRequiredHappiness,
                BreedRequiredLifespan = Horizon.Game.Sim.Pets.PetMergeCore.BreedRequiredLifespan,
                BreedOffspringCount = Horizon.Game.Sim.Pets.PetMergeCore.BreedOffspringCount,
                BreedClaimWindowSeconds = Horizon.Game.Sim.Pets.PetMergeCore.BreedClaimWindowSeconds,
                BreedCost = Horizon.Game.Sim.Pets.PetMergeCore.BreedGoldCost,
            },
        };
    }
}
