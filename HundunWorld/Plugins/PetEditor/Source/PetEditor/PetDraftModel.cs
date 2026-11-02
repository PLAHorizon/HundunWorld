using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PetEditor
{
    /// <summary>
    /// 宠物草稿的插件侧模型（与 <c>Horizon.PetCli.PetDraft</c> 是同一份 JSON 协议的两个宿主）。
    /// <para>
    /// <b>为什么这里要另存一份而不是引用 CLI 的程序集</b>：Flax 脚本域引不到仓库里的 .NET 工程
    /// （引擎的编译图与解决方案是两套），所以字段名协议在两侧各有一份字面副本。
    /// 这不是可以省掉的重复——<c>PetEditorContractTests</c> 会按**源文本**把两侧的键集合逐字比对，
    /// 漂一个字母就当场红。
    /// </para>
    /// <para>
    /// <b>必须用属性而不是公共字段</b>：<c>System.Text.Json</c> 默认不绑定公开字段
    /// （<c>IncludeFields=false</c>）。写成字段的话反序列化"成功"但全是初值，
    /// 而两侧同为初值时自校验反而全绿——这是最难查的那种静默失效。
    /// </para>
    /// </summary>
    public sealed class PetDraftModel
    {
        public int SpeciesId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ModelKey { get; set; } = string.Empty;
        public string PrefabPath { get; set; }
        public string PrefabGuid { get; set; }
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

        public PetStatBandModel Stats { get; set; } = new PetStatBandModel();
        public PetSkillRuleModel SkillRule { get; set; } = new PetSkillRuleModel();
        public List<PetSkillEntryModel> SkillEntries { get; set; } = new List<PetSkillEntryModel>();
        public PetBehaviorModel Behavior { get; set; } = new PetBehaviorModel();
        public PetEquipRuleModel Equip { get; set; } = new PetEquipRuleModel();
        public List<PetAssistEntryModel> AssistEntries { get; set; } = new List<PetAssistEntryModel>();
        public PetMergeRuleModel Merge { get; set; } = new PetMergeRuleModel();
        public List<PetMutationEntryModel> Mutations { get; set; } = new List<PetMutationEntryModel>();

        /// <summary>顶层已知键。<b>与 CLI 的 KnownTopLevelKeys 逐字相同</b>（契约测试按文本对账）。</summary>
        public static readonly string[] KnownTopLevelKeys =
        {
            "speciesId", "name", "modelKey", "prefabPath", "prefabGuid", "modelScale", "standState",
            "baseColorKey", "carryLevelMin", "carryLevelMax", "allowedOrigins", "allowedGrowthTiers",
            "hatchSeconds", "hatchLevel", "ownerLevelSlack", "potentialPerLevel", "immortalLifespan",
            "happinessScale", "captureMode", "captureBaseBps", "captureGuidanceSeconds", "mutationChanceBps",
            "publishState", "contentVersion", "remark",
            "stats", "skillRule", "skillEntries", "behavior", "equip", "assistEntries", "merge", "mutations",
        };

        /// <summary>子段键集合（按段名索引）。</summary>
        public static readonly Dictionary<string, string[]> KnownSectionKeys = new Dictionary<string, string[]>
        {
            ["stats"] = PetStatBandModel.KnownKeys,
            ["skillRule"] = PetSkillRuleModel.KnownKeys,
            ["skillEntries"] = PetSkillEntryModel.KnownKeys,
            ["behavior"] = PetBehaviorModel.KnownKeys,
            ["equip"] = PetEquipRuleModel.KnownKeys,
            ["assistEntries"] = PetAssistEntryModel.KnownKeys,
            ["merge"] = PetMergeRuleModel.KnownKeys,
            ["mutations"] = PetMutationEntryModel.KnownKeys,
        };

        /// <summary>
        /// 两侧共用的 JSON 约定：camelCase、不写 null、缩进。
        /// <para>刻意<b>不设 Encoder</b>：Flax 脚本域里没有 <c>System.Text.Encodings.Web</c>
        /// （离线 csc 闸门会当场报 CS0012）。默认转义器会把中文名写成 <c>\uXXXX</c>，
        /// 解析回来是同一个字符串，所以这不影响协议，只影响肉眼看 JSON 的舒适度。</para>
        /// </summary>
        public static readonly JsonSerializerOptions Contract = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Contract);

        public static PetDraftModel FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<PetDraftModel>(json, Contract);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>新建草稿：各段填本工程口径的默认值。<b>数字在这里是字面量副本</b>，
        /// 与内核默认的逐条对照由 <see cref="PetPreviewMath"/> 与契约测试负责。</summary>
        public static PetDraftModel CreateNew(int suggestedSpeciesId)
        {
            return new PetDraftModel
            {
                SpeciesId = suggestedSpeciesId,
                Name = "新宠物_" + suggestedSpeciesId,
                ModelKey = "pet_" + suggestedSpeciesId,
                ModelScale = 1.0m,
                StandState = "Idle",
                BaseColorKey = "base",
                CarryLevelMin = 0,
                CarryLevelMax = 60,
                OwnerLevelSlack = 10,
                PotentialPerLevel = 5,
                HappinessScale = 1000,
                CaptureMode = 1,
                CaptureBaseBps = 3000,
                MutationChanceBps = 50,
                ContentVersion = 1,
                Stats = new PetStatBandModel(),
                SkillRule = new PetSkillRuleModel(),
                Behavior = new PetBehaviorModel(),
                Equip = new PetEquipRuleModel(),
                Merge = new PetMergeRuleModel(),
            };
        }
    }

    /// <summary>资质区间段（对应 <c>PetStatBandDraft</c>）。</summary>
    public sealed class PetStatBandModel
    {
        public int AttackMin { get; set; } = 1000;
        public int AttackMax { get; set; } = 1500;
        public int DefenseMin { get; set; } = 1000;
        public int DefenseMax { get; set; } = 1500;
        public int StaminaMin { get; set; } = 1000;
        public int StaminaMax { get; set; } = 1500;
        public int ManaMin { get; set; } = 1000;
        public int ManaMax { get; set; } = 1500;
        public int SpeedMin { get; set; } = 1000;
        public int SpeedMax { get; set; } = 1500;
        public int GrowthMinBps { get; set; } = 10000;
        public int GrowthMaxBps { get; set; } = 12000;
        public int ComprehensionMax { get; set; } = 10;
        public int SpiritMax { get; set; } = 10;
        public bool SuperSpirit { get; set; }
        public int MutationTierMax { get; set; } = 8;

        public static readonly string[] KnownKeys =
        {
            "attackMin", "attackMax", "defenseMin", "defenseMax", "staminaMin", "staminaMax",
            "manaMin", "manaMax", "speedMin", "speedMax", "growthMinBps", "growthMaxBps",
            "comprehensionMax", "spiritMax", "superSpirit", "mutationTierMax",
        };
    }

    /// <summary>技能槽规则段（对应 <c>PetSkillRuleDraft</c>）。</summary>
    public sealed class PetSkillRuleModel
    {
        public int BaseSlots { get; set; } = 4;
        public int AbsoluteMaxSlots { get; set; } = 8;
        public string SlotsByGrade { get; set; } = string.Empty;
        public int ComprehensionUnlockA { get; set; } = 4;
        public int ComprehensionUnlockB { get; set; } = 7;
        public int SlotGrowMaxLevel { get; set; } = 125;
        public int SlotGrowMaxExisting { get; set; } = 3;
        public int SlotGrowChanceBps { get; set; } = 1500;
        public int OverwriteChanceBps { get; set; } = 7000;
        public int MaxLockableSlots { get; set; } = 2;
        public int LearnedPoolMax { get; set; } = 6;
        public int EquippedMax { get; set; } = 3;
        public int InnerCoreSlots { get; set; } = 1;

        public static readonly string[] KnownKeys =
        {
            "baseSlots", "absoluteMaxSlots", "slotsByGrade", "comprehensionUnlockA", "comprehensionUnlockB",
            "slotGrowMaxLevel", "slotGrowMaxExisting", "slotGrowChanceBps", "overwriteChanceBps",
            "maxLockableSlots", "learnedPoolMax", "equippedMax", "innerCoreSlots",
        };
    }

    /// <summary>技能条目（对应 <c>PetSkillEntryDraft</c>）。</summary>
    public sealed class PetSkillEntryModel
    {
        public int SkillId { get; set; }
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

    /// <summary>行为与跟随段（对应 <c>PetBehaviorDraft</c>）。</summary>
    public sealed class PetBehaviorModel
    {
        public byte IntellectTier { get; set; } = 2;
        public byte? FlagUseAcquire { get; set; }
        public byte? FlagUseAutoCast { get; set; }
        public byte? FlagUseEvade { get; set; }
        public byte? FlagUseGuard { get; set; }
        public byte? FlagUseHold { get; set; }
        public byte DefaultStance { get; set; } = 3;
        public byte DefaultCommand { get; set; } = 1;
        public string AllowedPersonalities { get; set; } = "0,3,4";
        public int EpiphanyChanceBps { get; set; } = 1000;
        public float FollowDistance { get; set; } = 3f;
        public float LeashRadius { get; set; } = 12f;
        public float TeleportBeyondRadius { get; set; } = 40f;
        public int RepathIntervalTicks { get; set; } = 10;
        public float FollowSpeed { get; set; } = 5.5f;
        public float ArriveEpsilon { get; set; } = 0.6f;
        public int CorpseSeconds { get; set; }
        public int ReviveCastSeconds { get; set; } = 4;
        public int SummonCooldownSeconds { get; set; }
        public int FollowStartDelayMs { get; set; } = 300;
        public int InitialLoyalty { get; set; } = 100;
        public int InitialHappiness { get; set; } = 700;
        public int InitialLifespan { get; set; } = 100;
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

    /// <summary>装备规则段（对应 <c>PetEquipRuleDraft</c>）。</summary>
    public sealed class PetEquipRuleModel
    {
        public string AllowedSlots { get; set; } = "0,1,2";
        public bool GemAllowed { get; set; } = true;
        public int MaxStar { get; set; } = 7;
        public byte SetFamily { get; set; } = 1;
        public bool AppraisalEnabled { get; set; } = true;
        public bool AwakeningEnabled { get; set; }
        public int AwakeningPieceCount { get; set; } = 3;
        public byte Ownership { get; set; } = 1;

        public static readonly string[] KnownKeys =
        {
            "allowedSlots", "gemAllowed", "maxStar", "setFamily", "appraisalEnabled",
            "awakeningEnabled", "awakeningPieceCount", "ownership",
        };
    }

    /// <summary>辅助条目（对应 <c>PetAssistEntryDraft</c>）。</summary>
    public sealed class PetAssistEntryModel
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

    /// <summary>合成/繁殖段（对应 <c>PetMergeRuleDraft</c>）。</summary>
    public sealed class PetMergeRuleModel
    {
        public bool FuseEnabled { get; set; } = true;
        public bool BreedEnabled { get; set; } = true;
        public int FuseMinLevel { get; set; } = 30;
        public int FuseSuccessBps { get; set; } = 6000;
        public int FuseQualityMinBps { get; set; } = 7000;
        public int FuseQualityMaxBps { get; set; } = 11000;
        public int FuseGrowthMinBps { get; set; } = 9600;
        public int FuseGrowthMaxBps { get; set; } = 10200;
        public int FuseMaxInheritedSkills { get; set; } = 8;
        public bool FuseConsumesParents { get; set; } = true;
        public int BreedMaxLevelGap { get; set; } = 5;
        public int BreedRequiredHappiness { get; set; } = 100;
        public int BreedRequiredLifespan { get; set; } = 1000;
        public int BreedOffspringCount { get; set; } = 2;
        public int BreedClaimWindowSeconds { get; set; } = 172800;
        public int BreedCost { get; set; } = 2;

        public static readonly string[] KnownKeys =
        {
            "fuseEnabled", "breedEnabled", "fuseMinLevel", "fuseSuccessBps",
            "fuseQualityMinBps", "fuseQualityMaxBps", "fuseGrowthMinBps", "fuseGrowthMaxBps",
            "fuseMaxInheritedSkills", "fuseConsumesParents", "breedMaxLevelGap", "breedRequiredHappiness",
            "breedRequiredLifespan", "breedOffspringCount", "breedClaimWindowSeconds", "breedCost",
        };
    }

    /// <summary>临时变异条目（对应 <c>PetMutationEntryDraft</c>）。</summary>
    public sealed class PetMutationEntryModel
    {
        public string MutationKey { get; set; } = string.Empty;
        public int DurationTicks { get; set; } = 36000;
        public int QualityCapBps { get; set; } = 10000;
        public string ColorOverride { get; set; }
        public int ExtraSkillSlots { get; set; }
        public bool Enabled { get; set; } = true;

        public static readonly string[] KnownKeys =
        {
            "mutationKey", "durationTicks", "qualityCapBps", "colorOverride", "extraSkillSlots", "enabled",
        };
    }
}
