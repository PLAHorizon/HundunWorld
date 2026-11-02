using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;
using Horizon.QuestCli;

namespace Horizon.PetCli;

/// <summary>
/// 宠物物种的命令行入口：宠物编辑器（Flax 插件）以"短生命周期子进程"的方式调用它，
/// 由它把<b>校验</b>与<b>写库</b>这两件只在服务端一侧才说得清的事做完，并代跑内核的确定性算法。
/// <para>
/// 输出恒为一段 JSON（UTF-8），字段固定 <c>ok/verb/message/data/warnings</c>；退出码 0 成功、非 0 失败。
/// 与 <c>Horizon.MonsterCli</c> 逐字同构，编辑器的解析代码因此可以照搬。
/// </para>
/// <para>
/// <b>preview-* 一律调内核而不是在这儿再算一遍</b>：编辑器里显示的"这只宠升到 50 级多少血"
/// 必须与战斗服算出的那个数字是同一个函数出来的。两份实现迟早分叉，而分叉的表现是
/// "策划照着编辑器调平衡，线上数值却不是那个数"。
/// </para>
/// </summary>
public static class Program
{
    private static readonly JsonSerializerOptions OutputOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static TextWriter _stdout = Console.Out;
    private static TextReader _stdin = Console.In;

    /// <summary>
    /// 显式接管两条标准流：悄悄退回控制台码页会让中文名字被读坏，
    /// 而"库写进去了但名字是乱码"是最难查的那种坏法。
    /// </summary>
    private static void InitializeStreams()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        _stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
        _stdin = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: true);
    }

    public static int Main(string[] args)
    {
        var verb = (args.Length > 0 ? args[0] : "help").Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();

        try
        {
            try
            {
                InitializeStreams();
            }
            catch (Exception streamFailure)
            {
                Console.Error.WriteLine("标准流接管失败：" + streamFailure.Message);
            }

            return verb switch
            {
                "help" or "--help" or "-h" => Run(verb, true,
                    "宠物物种 CLI：catalog | list | get <id> | validate - | save - | publish <id> | retire <id> | delete <id> | "
                    + "draft save|list|get|delete | preview-stats --species S --level L [--growth B] [--sweep] | "
                    + "preview-learn --species S --skill K --seed N [--owned id,id] | "
                    + "preview-merge --mode fuse|breed --a <id> --b <id> --seed N | "
                    + "preview-bonus --species S --level L [--possessed] [--ownerAttack N --ownerHp N --ownerDefense N --ownerHit N] | preview-capture --species S --seed N | "
                    + "preview-bond --loyalty L --happiness H --lifespan P | "
                    + "export-content --out <path> | export-visuals --out <path> | instance list [--species S] | "
                    + "instance add --species S --owner <角色id> [--level L] [--seed N] [--state owned|battling|possessed|following|stable] [--skill 技能id:等级] [--force]", null),

                "catalog" => RunCatalog(),
                "list" => RunList(),
                "get" => RunGet(rest),
                "validate" => RunValidate(rest),
                "save" => RunSave(rest),
                "publish" => RunSetState(rest, 1),
                "retire" => RunSetState(rest, 2),
                "delete" => RunDelete(rest),
                "draft" => RunDraft(rest),
                "preview-stats" => RunPreviewStats(rest),
                "preview-learn" => RunPreviewLearn(rest),
                "preview-merge" => RunPreviewMerge(rest),
                "preview-bonus" => RunPreviewBonus(rest),
                "preview-capture" => RunPreviewCapture(rest),
                "preview-bond" => RunPreviewBond(rest),
                "export-content" => RunExportContent(rest),
                "export-visuals" => RunExportVisuals(rest),
                "instance" => RunInstance(rest),
                _ => Run(verb, false, "未知动词：" + verb + "（跑 help 看用法）", null),
            };
        }
        catch (Exception ex)
        {
            // 上报必须带 errorType/errorStack：只回 ex.Message 的话，同一个神秘异常会被猜三次。
            return Run(verb, false, "执行失败：" + ex.GetBaseException().Message, new Dictionary<string, object?>
            {
                ["errorType"] = ex.GetType().FullName,
                ["errorStack"] = ex.ToString(),
            });
        }
    }

    // ---------------- 无库依赖的动词（catalog / preview-*） ----------------

    /// <summary>号段、内核默认值与硬编码回落目录：编辑器启动时先调这个，不需要连库。</summary>
    private static int RunCatalog()
    {
        var segments = new List<Dictionary<string, object?>>
        {
            Segment("普通", PetRarityTier.Common, 500_000, 599_999),
            Segment("变异/稀有", PetRarityTier.Mutant, 600_000, 649_999),
            Segment("神兽", PetRarityTier.Divine, 650_000, 699_999),
            Segment("活动限定", PetRarityTier.Event, 700_000, 799_999),
        };

        var slot = PetSlotRuleConfig.Default;
        var follow = PetFollowConfig.Default;
        var bond = PetBondValues.Default;

        var data = new Dictionary<string, object?>
        {
            ["segments"] = segments,
            ["identity"] = new Dictionary<string, object?>
            {
                ["minSpeciesId"] = PetIdentityCore.MinSpeciesId,
                ["maxSpeciesId"] = PetIdentityCore.MaxSpeciesId,
                ["sequenceBase"] = PetIdentityCore.InstanceSequenceBase,
                ["maxInstancesPerSpecies"] = PetIdentityCore.MaxInstancesPerSpecies,
                ["version"] = PetIdentityCore.Version,
            },
            ["kernelDefaults"] = new Dictionary<string, object?>
            {
                ["maxPetLevel"] = PetStatsCore.MaxPetLevel,
                ["potentialPerLevel"] = PetStatsCore.PotentialPointsPerLevel,
                ["growthMinBps"] = PetStatsCore.GrowthMinBps,
                ["growthMaxBps"] = PetStatsCore.GrowthMaxBps,
                ["comprehensionBonusBps"] = PetStatsCore.ComprehensionBonusBps,
                ["spiritBonusNormalBps"] = PetStatsCore.SpiritBonusNormalBps,
                ["spiritBonusSuperBps"] = PetStatsCore.SpiritBonusSuperBps,
                ["fusionSuccessBps"] = PetGrowthCore.FusionSuccessBps,
                ["comprehensionSafeLevels"] = PetGrowthCore.ComprehensionSafeLevels,
                ["marriageLevels"] = PetGrowthCore.MarriageLevelByBreedingCount,
                ["happinessGreenAt"] = PetBondCore.HappinessGreenAt,
                ["happinessYellowAt"] = PetBondCore.HappinessYellowAt,
                ["damageMultBps"] = new[] { PetBondCore.DamageMultRedBps, PetBondCore.DamageMultYellowBps, PetBondCore.DamageMultGreenBps },
                ["loyaltyRefuseThreshold"] = PetBondCore.LoyaltyRefuseThreshold,
                ["lifespanBlockThreshold"] = PetBondCore.LifespanBlockThreshold,
                ["happinessBlockThreshold"] = PetBondCore.HappinessBlockThreshold,
                ["baseSlots"] = slot.BaseSlots,
                ["absoluteMaxSlots"] = slot.AbsoluteMaxSlots,
                ["innerCoreSlotsByBattleLevel"] = new[] { 1, 35, 75, 105, 145, 175 }.Select(PetSkillCore.InnerCoreSlotsFor).ToArray(),
                ["followDistance"] = follow.FollowDistance,
                ["leashRadius"] = follow.LeashRadius,
                ["teleportBeyondRadius"] = follow.TeleportBeyondRadius,
                ["maxBattlingPets"] = PetAiCore.MaxBattlingPets,
                ["maxPossessedSlots"] = PetAiCore.MaxPossessedSlots,
                ["possessUnlockOwnerLevel"] = PetAiCore.PossessionUnlockOwnerLevel,
                ["initialBond"] = new Dictionary<string, object?>
                {
                    ["loyalty"] = bond.Loyalty, ["happiness"] = bond.Happiness,
                    ["lifespan"] = bond.Lifespan, ["intimacy"] = bond.Intimacy,
                },
            },
            ["fallbackCatalog"] = PetSpeciesCatalog.Rows.Select(r => new Dictionary<string, object?>
            {
                ["speciesId"] = r.SpeciesId,
                ["name"] = r.Name,
                ["carryMin"] = r.CarryMin,
                ["modelKey"] = r.ModelKey,
                ["origin"] = (int)r.PrimaryOrigin,
                ["rarityTier"] = (int)PetIdentityCore.RarityTierOf(r.SpeciesId),
            }).ToArray(),
            ["limits"] = new Dictionary<string, object?>
            {
                ["maxSkillEntries"] = PetValidator.MaxSkillEntries,
                ["maxAssistEntries"] = PetValidator.MaxAssistEntries,
                ["maxMutationEntries"] = PetValidator.MaxMutationEntries,
                ["skillIdDomainMin"] = PetValidator.SkillIdDomainMin,
            },
        };

        return Run("catalog", true, "号段与内核默认值（不依赖数据库）", data);
    }

    private static Dictionary<string, object?> Segment(string label, PetRarityTier tier, int lo, int hi)
        => new()
        {
            ["label"] = label,
            ["tier"] = (int)tier,
            ["min"] = lo,
            ["max"] = hi,
        };

    private static int RunPreviewStats(string[] args)
    {
        if (!TryGetOpt(args, "level", out var level)) return Run("preview-stats", false, "缺 --level", null);
        var speciesId = TryGetOpt(args, "species", out var sid) ? sid : 0;
        var growth = TryGetOpt(args, "growth", out var g) ? g : PetStatsCore.GrowthBpsOf(PetGrowthTier.Fine);
        var comprehension = TryGetOpt(args, "comprehension", out var comp) ? comp : 0;
        var spirit = TryGetOpt(args, "spirit", out var spi) ? spi : 0;
        var superSpirit = args.Any(a => string.Equals(a, "--superSpirit", StringComparison.OrdinalIgnoreCase));

        var q = new PetQualities
        {
            Attack = TryGetOpt(args, "atk", out var atk) ? atk : 1200,
            Defense = TryGetOpt(args, "def", out var def) ? def : 1200,
            Stamina = TryGetOpt(args, "sta", out var sta) ? sta : 1200,
            Mana = TryGetOpt(args, "mana", out var mana) ? mana : 1200,
            Speed = TryGetOpt(args, "spd", out var spd) ? spd : 1200,
        };
        var points = new PetAttributePoints
        {
            Strength = TryGetOpt(args, "str", out var str) ? str : 0,
            Endurance = TryGetOpt(args, "end", out var end) ? end : 0,
            Constitution = TryGetOpt(args, "con", out var con) ? con : 0,
            Spirit = TryGetOpt(args, "spiPts", out var spiPts) ? spiPts : 0,
            Agility = TryGetOpt(args, "agi", out var agi) ? agi : 0,
        };

        var effective = PetStatsCore.EffectiveQualities(q, comprehension, spirit, superSpirit);
        var single = PetStatsCore.Compute(effective, level, growth, points);

        var data = new Dictionary<string, object?>
        {
            ["speciesId"] = speciesId,
            ["level"] = level,
            ["growthBps"] = growth,
            ["effectiveQualities"] = new Dictionary<string, object?>
            {
                ["attack"] = effective.Attack, ["defense"] = effective.Defense,
                ["stamina"] = effective.Stamina, ["mana"] = effective.Mana, ["speed"] = effective.Speed,
            },
            ["stats"] = Describe(single),
            ["coreVersion"] = PetStatsCore.Version,
        };

        if (args.Any(a => string.Equals(a, "--sweep", StringComparison.OrdinalIgnoreCase)))
        {
            // 扫全等级：编辑器的成长曲线视图用这条，而不是自己按公式画
            var sweep = new List<Dictionary<string, object?>>(PetStatsCore.MaxPetLevel);
            for (var lv = 1; lv <= PetStatsCore.MaxPetLevel; lv++)
            {
                sweep.Add(new Dictionary<string, object?>
                {
                    ["level"] = lv,
                    ["potentialTotal"] = lv * PetStatsCore.PotentialPointsPerLevel,
                    ["stats"] = Describe(PetStatsCore.Compute(effective, lv, growth, points)),
                });
            }
            data["sweep"] = sweep;
        }

        return Run("preview-stats", true, "属性试算完成", data);
    }

    private static Dictionary<string, object?> Describe(PetCombatStats s) => new()
    {
        ["hp"] = s.Hp, ["mp"] = s.Mp, ["attack"] = s.Attack,
        ["defense"] = s.Defense, ["speed"] = s.Speed, ["spiritPower"] = s.SpiritPower,
    };

    private static int RunPreviewLearn(string[] args)
    {
        if (!TryGetOpt(args, "skill", out var skillId)) return Run("preview-learn", false, "缺 --skill", null);
        var seed = TryGetOpt(args, "seed", out var s0) ? s0 : 1;
        var level = TryGetOpt(args, "level", out var lv) ? lv : 50;
        var comprehension = TryGetOpt(args, "comprehension", out var comp) ? comp : 0;
        var owned = ParseIdList(TryGetOptString(args, "owned"));
        var conflicts = ParseIdList(TryGetOptString(args, "conflicts"));

        // 用内核默认槽规则搭出当前槽表：owned 的第 0 格按天生技处理（与梦幻"洗炼必出"一致）
        var rule = PetSlotRuleConfig.Default;
        var cap = PetSkillCore.MaxSlotsFor(rule, 0, comprehension);
        var slots = new PetSkillSlot[cap];
        for (var i = 0; i < cap; i++)
        {
            slots[i] = i < owned.Length
                ? new PetSkillSlot
                {
                    SkillId = owned[i],
                    Kind = i == 0 ? PetSkillSlotKind.Innate : PetSkillSlotKind.Learned,
                    Level = 1,
                }
                : new PetSkillSlot();
        }

        var cfg = new PetSkillLearnConfig(slots, cap, level, comprehension);
        var (after, result) = PetSkillCore.LearnSkill(rule, cfg, skillId, 1, conflicts, seed);

        var data = new Dictionary<string, object?>
        {
            ["outcome"] = result.Outcome.ToString(),
            ["slotIndex"] = result.SlotIndex,
            ["replacedSkillId"] = result.ReplacedSkillId,
            ["roll"] = result.Roll,
            ["openCap"] = cap,
            ["slotsAfter"] = after.Select(s => new Dictionary<string, object?>
            {
                ["skillId"] = s.SkillId, ["kind"] = s.Kind.ToString(), ["locked"] = s.Locked,
            }).ToArray(),
            ["coreVersion"] = PetSkillCore.Version,
        };
        return Run("preview-learn", true, "打书试算完成", data);
    }

    private static int RunPreviewMerge(string[] args)
    {
        var mode = (TryGetOptString(args, "mode") ?? "fuse").ToLowerInvariant();
        if (!TryGetOpt(args, "a", out var aId) || !TryGetOpt(args, "b", out var bId))
            return Run("preview-merge", false, "需要 --a 与 --b 两个物种号", null);
        var seed = TryGetOpt(args, "seed", out var seedValue) ? seedValue : 1;
        var level = TryGetOpt(args, "level", out var lv) ? lv : 40;

        var q = new PetQualities { Attack = 1200, Defense = 1200, Stamina = 1200, Mana = 1200, Speed = 1200 };
        var skills = new[] { 800_001, 800_002, 800_003 };
        var parentA = new PetParentSnapshot(PetIdentityCore.NewInstanceId(aId, 1), aId, level, PetOrigin.Baby, 0,
            q, 11_000, skills, 100, 2_000, 0, false, 0, false);
        var parentB = new PetParentSnapshot(PetIdentityCore.NewInstanceId(bId, 1), bId, level, PetOrigin.Baby, 1,
            q, 11_000, new[] { 800_003, 800_004 }, 100, 2_000, 0, false, 0,
            string.Equals(TryGetOptString(args, "rebooked"), "1", StringComparison.Ordinal));

        var result = mode == "breed" ? PetMergeCore.Breed(parentA, parentB, 2, seed) : PetMergeCore.Fuse(parentA, parentB, seed);

        var data = new Dictionary<string, object?>
        {
            ["mode"] = mode,
            ["blocked"] = result.Blocked.ToString(),
            ["success"] = result.Success,
            ["offspringCount"] = result.OffspringCount,
            ["claimWindowSeconds"] = result.ClaimWindowSeconds,
            ["parentsConsumed"] = result.ParentsConsumed,
            ["qualityBandBps"] = result.QualityBandBps,
            ["growthBandBps"] = result.GrowthBandBps,
            ["qualities"] = new Dictionary<string, object?>
            {
                ["attack"] = result.Qualities.Attack, ["defense"] = result.Qualities.Defense,
                ["stamina"] = result.Qualities.Stamina, ["mana"] = result.Qualities.Mana,
                ["speed"] = result.Qualities.Speed,
            },
            ["growthBps"] = result.GrowthBps,
            ["inheritedSkillIds"] = result.InheritedSkillIds,
            ["offspringOrigin"] = result.OffspringOrigin.ToString(),
            ["coreVersion"] = PetMergeCore.Version,
        };
        return Run("preview-merge", result.Blocked == PetMergeBlockReason.None,
            result.Success ? "产出成功" : $"未产出（{result.Blocked}）", data);
    }

    private static int RunPreviewBonus(string[] args)
    {
        if (!TryGetOpt(args, "level", out var level)) return Run("preview-bonus", false, "缺 --level", null);
        var speciesId = TryGetOpt(args, "species", out var sid) ? sid : 0;
        var possessed = args.Any(a => string.Equals(a, "--possessed", StringComparison.OrdinalIgnoreCase));
        var assistIds = ParseIdList(TryGetOptString(args, "skills"));
        var levelsStr = ParseIdList(TryGetOptString(args, "skillLevels"));

        var q = new PetQualities
        {
            Attack = TryGetOpt(args, "atk", out var atk) ? atk : 1200,
            Defense = TryGetOpt(args, "def", out var def) ? def : 1200,
            Stamina = TryGetOpt(args, "sta", out var sta) ? sta : 1200,
            Mana = TryGetOpt(args, "mana", out var mana) ? mana : 1200,
            Speed = TryGetOpt(args, "spd", out var spd) ? spd : 1200,
        };
        var petHp = TryGetOpt(args, "hp", out var hp) ? hp : 1_000;

        var bonus = PetAssistCore.ComputeOwnerBonus(level, q, petHp,
            assistIds.Length > 0 ? assistIds : Array.Empty<int>(),
            levelsStr.Length > 0 ? levelsStr : Array.Empty<int>(),
            null, PetIdentityCore.NewInstanceId(speciesId, 1), true, possessed);

        // 主人自己的基础值：给了就把加成叠上去并单列"其中宠物贡献多少"，
        // 面板与 DS 都消费同一份结果，避免"编辑器说 +500 攻击、线上却按另一种换算算"。
        var hasBase = TryGetOpt(args, "ownerAttack", out _) || TryGetOpt(args, "ownerHp", out _)
                      || TryGetOpt(args, "ownerDefense", out _) || TryGetOpt(args, "ownerHit", out _);
        Dictionary<string, object?>? finalStats = null;
        if (hasBase)
        {
            var baseStats = new PetAssistCore.OwnerBaseStats
            {
                Attack = TryGetOpt(args, "ownerAttack", out var oa) ? oa : 0,
                Hp = TryGetOpt(args, "ownerHp", out var oh) ? oh : 0,
                Defense = TryGetOpt(args, "ownerDefense", out var od) ? od : 0,
                Hit = TryGetOpt(args, "ownerHit", out var ohi) ? ohi : 0,
                ManaRegen = TryGetOpt(args, "ownerManaRegen", out var omr) ? omr : 0,
                BaseCritTakenBps = TryGetOpt(args, "ownerCritTakenBps", out var oct) ? oct : 0,
            };
            var final = PetAssistCore.ApplyToOwnerStats(bonus, baseStats);
            finalStats = new Dictionary<string, object?>
            {
                ["attack"] = final.Attack, ["hp"] = final.Hp, ["defense"] = final.Defense,
                ["hit"] = final.Hit, ["manaRegen"] = final.ManaRegen,
                ["critTakenBps"] = final.CritTakenBps,
                ["fromPetsAttack"] = final.FromPetsAttack, ["fromPetsHp"] = final.FromPetsHp,
                ["fromPetsDefense"] = final.FromPetsDefense, ["fromPetsHit"] = final.FromPetsHit,
            };
        }

        var data = new Dictionary<string, object?>
        {
            ["speciesId"] = speciesId,
            ["level"] = level,
            ["possessed"] = possessed,
            ["ownerBonus"] = new Dictionary<string, object?>
            {
                ["strength"] = bonus.Strength, ["spirit"] = bonus.Spirit, ["vitality"] = bonus.Vitality,
                ["agility"] = bonus.Agility, ["steadiness"] = bonus.Steadiness,
                ["hpFlat"] = bonus.HpFlat, ["manaRegen"] = bonus.ManaRegen,
                ["critTankChanceBps"] = bonus.CritTankChanceBps, ["damageLeechBps"] = bonus.DamageLeechBps,
            },
            ["finalOwnerStats"] = finalStats,
            // 来源清单必须一起回：只有合计数字的话，界面无法回答"这 300 力量是谁给的"
            ["sources"] = bonus.Sources?.Select(x => new Dictionary<string, object?>
            {
                ["kind"] = x.Kind.ToString(), ["skillId"] = x.SkillId,
                ["instanceId"] = x.FromInstanceId, ["label"] = x.AttributeLabel,
                ["value"] = x.Value, ["isBasisPoints"] = x.IsBasisPoints,
            }).ToArray(),
            ["coreVersion"] = PetAssistCore.Version,
        };
        return Run("preview-bonus", true, "反哺加成试算完成", data);
    }

    private static int RunPreviewCapture(string[] args)
    {
        if (!TryGetOpt(args, "species", out var speciesId)) return Run("preview-capture", false, "缺 --species", null);
        var seed = TryGetOpt(args, "seed", out var s0) ? s0 : 1;
        var playerLevel = TryGetOpt(args, "player", out var pl) ? pl : 50;
        var skill = TryGetOpt(args, "catchSkill", out var cs) ? cs : 0;
        var hp = TryGetOpt(args, "targetHpBps", out var th) ? th : 1_000;
        var existing = TryGetOpt(args, "existing", out var ex) ? ex : 0;
        var freeSlots = TryGetOpt(args, "freeSlots", out var fs) ? fs : 3;

        PetContent.ResetToHardcoded();
        if (!PetContent.TryGetTemplate(speciesId, out var template))
            return Run("preview-capture", false, $"回落目录里没有物种 {speciesId}（先用编辑器建一个或用 022 种子）", null);

        var request = new PetCaptureRequest(template, playerLevel, skill, hp, true, false, freeSlots,
            template.CaptureGuidanceSeconds, false, true, existing, seed);
        var result = PetCaptureCore.Attempt(request);

        var data = new Dictionary<string, object?>
        {
            ["blocked"] = result.Blocked.ToString(),
            ["success"] = result.Success,
            ["roll"] = result.Roll,
            ["chanceBps"] = result.ChanceBps,
            ["instanceId"] = result.NewInstanceId,
            ["origin"] = result.Origin.ToString(),
            ["stage"] = result.Stage.ToString(),
            ["identityIssue"] = result.IdentityIssue.ToString(),
            ["growthBps"] = result.GrowthBps,
            ["coreVersion"] = PetCaptureCore.Version,
        };
        return Run("preview-capture", result.Blocked == PetCaptureBlockReason.None,
            result.Success ? $"捕捉成功，个体编号 {result.NewInstanceId}" : $"未成功（{result.Blocked}）", data);
    }

    private static int RunPreviewBond(string[] args)
    {
        var values = new PetBondValues
        {
            Loyalty = TryGetOpt(args, "loyalty", out var lo) ? lo : 100,
            Happiness = TryGetOpt(args, "happiness", out var ha) ? ha : 700,
            Lifespan = TryGetOpt(args, "lifespan", out var lf) ? lf : 100,
            Intimacy = TryGetOpt(args, "intimacy", out var it) ? it : 0,
        };
        var petLevel = TryGetOpt(args, "level", out var lv) ? lv : 50;
        var ownerLevel = TryGetOpt(args, "owner", out var ol) ? ol : 50;

        var block = PetBondCore.CheckDeployable(values, petLevel, ownerLevel);
        var mood = PetBondCore.MoodOf(values.Happiness);

        // 逐事件跑一遍：编辑器要的是"这件事会发生什么"，不是让策划去读矩阵
        var events = new List<Dictionary<string, object?>>();
        foreach (PetBondEvent evt in Enum.GetValues<PetBondEvent>())
        {
            var after = PetBondCore.ApplyEvent(values, evt);
            events.Add(new Dictionary<string, object?>
            {
                ["event"] = evt.ToString(),
                ["loyalty"] = after.Loyalty, ["happiness"] = after.Happiness,
                ["lifespan"] = after.Lifespan, ["intimacy"] = after.Intimacy,
            });
        }

        var data = new Dictionary<string, object?>
        {
            ["deployable"] = block == PetBondBlockReason.None,
            ["blockReason"] = block.ToString(),
            ["mood"] = mood.ToString(),
            ["damageMultiplierBps"] = PetBondCore.DamageMultiplierBps(mood),
            ["refusalChanceBps"] = PetBondCore.RefusalChanceBps(values.Loyalty),
            ["intimacyTriggerBonusBps"] = PetBondCore.IntimacyTriggerBonusBps(values.Intimacy),
            ["events"] = events,
            ["coreVersion"] = PetBondCore.Version,
        };
        return Run("preview-bond", true, "默契试算完成", data);
    }

    // ---------------- 需要连库的动词 ----------------

    private static int RunList()
    {
        if (!TryOpenStore(out var store, out var error)) return Run("list", false, error, null);
        using (store)
        {
            var rows = store!.List();
            return Run("list", true, $"共 {rows.Count} 个物种", new Dictionary<string, object?>
            {
                ["rows"] = rows,
                ["count"] = rows.Count,
            });
        }
    }

    private static int RunGet(string[] rest)
    {
        if (!TryParseInt(rest, out var speciesId)) return Run("get", false, "缺物种号", null);
        if (!TryOpenStore(out var store, out var error)) return Run("get", false, error, null);
        using (store)
        {
            var draft = store!.GetDraft(speciesId, out var getError);
            if (draft is null) return Run("get", false, getError, null);
            return Run("get", true, $"物种 {speciesId}", new Dictionary<string, object?>
            {
                ["draft"] = draft,
                ["draftJson"] = draft.ToJson(),
            });
        }
    }

    private static int RunValidate(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("validate", false, readError, null);
        IPetReferenceIndex? references = null;
        string? openError = null;
        if (!TryOpenStore(out var store, out openError)) store = null;
        try
        {
            references = store;
            var result = PetValidator.Validate(draft!, references);
            return Run("validate", result.Ok,
                result.Ok ? "校验通过" : $"{result.Errors.Count} 条错误",
                new Dictionary<string, object?>
                {
                    ["errors"] = result.Errors,
                    ["errorPaths"] = result.ErrorPaths,
                    ["warnings"] = result.Warnings,
                    ["errorCount"] = result.Errors.Count,
                },
                result.Warnings);
        }
        finally
        {
            store?.Dispose();
            if (store is null && openError is not null)
            {
                // 连不上库只影响跨域对账（技能是否存在），不影响结构校验：
                // 把这条信息以 warning 形式带回，而不是让它伪装成"校验通过"。
                Console.Error.WriteLine("参考索引不可用，跨域对账已跳过：" + openError);
            }
        }
    }

    private static int RunSave(string[] rest)
    {
        if (!ReadDraft(rest, out var draft, out var readError)) return Run("save", false, readError, null);
        var validation = PetValidator.Validate(draft!);
        if (!validation.Ok)
        {
            return Run("save", false, $"校验未通过（{validation.Errors.Count} 条），拒绝写库",
                new Dictionary<string, object?> { ["errors"] = validation.Errors, ["errorPaths"] = validation.ErrorPaths });
        }
        if (!TryOpenStore(out var store, out var error)) return Run("save", false, error, null);
        using (store)
        {
            var result = store!.Save(draft!);
            return Run("save", result.Ok, result.Message,
                new Dictionary<string, object?>
                {
                    ["speciesId"] = result.SpeciesId,
                    ["contentVersion"] = result.ContentVersion,
                    ["writtenRows"] = result.WrittenRows,
                },
                result.Warnings);
        }
    }

    private static int RunSetState(string[] rest, int publishState)
    {
        var verb = publishState == 1 ? "publish" : "retire";
        if (!TryParseInt(rest, out var speciesId)) return Run(verb, false, "缺物种号", null);
        if (!TryOpenStore(out var store, out var error)) return Run(verb, false, error, null);
        using (store)
        {
            var result = store!.SetPublishState(speciesId, publishState);
            return Run(verb, result.Ok, result.Message,
                new Dictionary<string, object?> { ["writtenRows"] = result.WrittenRows });
        }
    }

    private static int RunDelete(string[] rest)
    {
        if (!TryParseInt(rest, out var speciesId)) return Run("delete", false, "缺物种号", null);
        if (!TryOpenStore(out var store, out var error)) return Run("delete", false, error, null);
        using (store)
        {
            var result = store!.Delete(speciesId);
            return Run("delete", result.Ok, result.Message,
                new Dictionary<string, object?> { ["writtenRows"] = result.WrittenRows });
        }
    }

    private static int RunDraft(string[] rest)
    {
        var sub = (rest.Length > 0 ? rest[0] : "list").ToLowerInvariant();
        if (!PetDraftStore.TryConnect(out var drafts, out var source, out var error))
            return Run("draft " + sub, false, error, null);

        using (drafts)
        {
            switch (sub)
            {
                case "save":
                {
                    if (!ReadDraft(rest.Skip(1).ToArray(), out var draft, out var readError))
                        return Run("draft save", false, readError, null);
                    drafts!.Save(draft!);
                    return Run("draft save", true, $"草稿已写入 Redis（{source}）",
                        new Dictionary<string, object?> { ["speciesId"] = draft!.SpeciesId });
                }
                case "list":
                {
                    var list = drafts!.List();
                    return Run("draft list", true, $"共 {list.Count} 份草稿",
                        new Dictionary<string, object?> { ["drafts"] = list, ["count"] = list.Count });
                }
                case "get":
                {
                    if (!TryParseInt(rest.Skip(1).ToArray(), out var speciesId))
                        return Run("draft get", false, "缺物种号", null);
                    var body = drafts!.GetRaw(speciesId);
                    if (body is null) return Run("draft get", false, $"没有物种 {speciesId} 的草稿", null);
                    return Run("draft get", true, $"物种 {speciesId} 的草稿",
                        new Dictionary<string, object?> { ["draftJson"] = body });
                }
                case "delete":
                {
                    if (!TryParseInt(rest.Skip(1).ToArray(), out var speciesId))
                        return Run("draft delete", false, "缺物种号", null);
                    var removed = drafts!.Delete(speciesId);
                    return Run("draft delete", removed, removed ? "草稿已删除" : "没有这份草稿", null);
                }
                default:
                    return Run("draft " + sub, false, "draft 只支持 save|list|get|delete", null);
            }
        }
    }

    private static int RunExportContent(string[] rest)
    {
        var path = TryGetOptString(rest, "out");
        if (string.IsNullOrWhiteSpace(path)) return Run("export-content", false, "缺 --out <path>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("export-content", false, error, null);
        using (store)
        {
            var bundle = store!.BuildPublishedBundle(out var published);
            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["contentRevision"] = bundle.ContentRevision,
                ["exportedAt"] = DateTimeOffset.UtcNow.ToString("O"),
                ["speciesCount"] = bundle.Species.Count,
                ["publishedCount"] = published,
                ["species"] = bundle.Species.Values.OrderBy(t => t.SpeciesId).ToList(),
            }, OutputOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return Run("export-content", published > 0,
                published > 0 ? $"导出 {published} 个已发布物种" : "没有任何已发布物种可导出",
                new Dictionary<string, object?> { ["path"] = path, ["count"] = published });
        }
    }

    private static int RunExportVisuals(string[] rest)
    {
        var path = TryGetOptString(rest, "out");
        if (string.IsNullOrWhiteSpace(path)) return Run("export-visuals", false, "缺 --out <path>", null);
        if (!TryOpenStore(out var store, out var error)) return Run("export-visuals", false, error, null);
        using (store)
        {
            var rows = store!.List().Where(r => r.PublishState == 1).ToList();
            var visuals = new List<Dictionary<string, object?>>();
            var problems = new List<string>();
            foreach (var row in rows)
            {
                var draft = store!.GetDraft(row.SpeciesId, out var getError);
                if (draft is null)
                {
                    problems.Add($"{row.SpeciesId}: 读草稿失败 {getError}");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(draft.PrefabPath))
                {
                    // 没有 prefab 路径 = 只登记了意图、美术还没进包。如实报 problem，
                    // 而不是导出一条空路径让客户端去加载一个不存在的资产。
                    problems.Add($"{row.SpeciesId} ({row.Name}): 没有 prefab 路径，未导出");
                    continue;
                }
                visuals.Add(new Dictionary<string, object?>
                {
                    ["modelKey"] = draft.ModelKey,
                    ["prefabPath"] = draft.PrefabPath,
                    ["scale"] = draft.ModelScale,
                    ["standState"] = draft.StandState,
                });
            }

            var json = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["clientKeys"] = ClientKeys,
                ["count"] = visuals.Count,
                ["visuals"] = visuals,
                ["problems"] = problems,
            }, OutputOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return Run("export-visuals", true,
                $"导出 {visuals.Count} 条外观、{problems.Count} 条问题",
                new Dictionary<string, object?> { ["path"] = path, ["count"] = visuals.Count, ["problems"] = problems });
        }
    }

    /// <summary>
    /// 客户端读端认的字面量键名。<b>这是跨进程协议</b>：客户端的宠物外观表按这些名字取值，
    /// 改一个字母会让整条外观链静默失效（读不到就用占位 prefab，而"占位"看起来像"还没美术"）。
    /// </summary>
    public static readonly string[] ClientKeys = { "visuals", "modelKey", "prefabPath", "scale", "standState" };

    private static int RunInstance(string[] rest)
    {
        var sub = (rest.Length > 0 ? rest[0] : "list").ToLowerInvariant();
        if (sub == "add") return RunInstanceAdd(rest.Skip(1).ToArray());
        if (sub != "list") return Run("instance " + sub, false, "instance 支持 list | add", null);
        if (!TryOpenStore(out var store, out var error)) return Run("instance list", false, error, null);
        using (store)
        {
            // 个体表已由 PetRosterGrain 接入（角色面板的反哺通路）。
            // 这里恒空仍是**如实状态**而不是链路故障，所以照常回 ok=true 并带 explanation，
            // 避免调用方把空列表当成"读法坏了"。
            var rows = store!.List().Select(r => new Dictionary<string, object?>
            {
                ["speciesId"] = r.SpeciesId,
                ["instanceCount"] = r.InstanceCount,
            }).ToList();
            return Run("instance list", true,
                // 计数是查出来的；文案不再断言"写入口没接"，只解释 0 意味着什么。
                "按物种汇总的个体数（某物种为 0 = 还没有人往里写过个体，用 instance add 发放或等捕捉落库接线）",
                new Dictionary<string, object?> { ["rows"] = rows });
        }
    }

    /// <summary>
    /// instance add —— 把一只新个体写进个体表（捕捉成功的落库，或 GM 发放）。
    /// <para>编号一律由内核算：序号取"该物种已用最大 + 1"，所以放生或删除过的号不会被复用。
    /// 资质、成长、出身都走内核 roll，CLI 不自己发明数字。</para>
    /// </summary>
    private static int RunInstanceAdd(string[] args)
    {
        const string verbName = "instance add";
        if (!TryGetOpt(args, "species", out var speciesId)) return Run(verbName, false, "缺 --species", null);
        if (!TryGetOptLong(args, "owner", out var ownerCharacterId) || ownerCharacterId <= 0)
            return Run(verbName, false, "缺 --owner（角色 id，必须为正整数）", null);

        var seed = TryGetOpt(args, "seed", out var sd) ? sd : 1;
        var level = TryGetOpt(args, "level", out var lv) ? lv : 1;
        var force = HasFlag(args, "force");
        var stateText = TryGetOptText(args, "state", out var st) ? st : "owned";
        if (!Enum.TryParse<PetInstanceState>(stateText, true, out var state))
        {
            if (!byte.TryParse(stateText, out var stateNum))
                return Run(verbName, false, "--state 既不是名字也不是数字：" + stateText, null);
            state = (PetInstanceState)stateNum;
        }

        if (!TryOpenStore(out var store, out var error)) return Run(verbName, false, error, null);
        using (store)
        {
            PetContent.ResetToHardcoded();
            if (!PetContent.TryGetTemplate(speciesId, out var template) || template is null)
                return Run(verbName, false,
                    $"回落目录里没有物种 {speciesId}；个体必须由模板 roll 出资质，不能凭空造", null);

            // 序号来源是库里的历史最大值，而不是 COUNT()：删过的行不会把号段缩回去。
            var nextSequence = store!.NextInstanceSequence(speciesId);
            var request = new PetCaptureRequest(template, level, 0, 1_000, true, false, 3,
                template.CaptureGuidanceSeconds, false, true, nextSequence - 1, seed);
            var capture = PetCaptureCore.Attempt(request);

            if (capture.Blocked != PetCaptureBlockReason.None)
                return Run(verbName, false, $"前置被挡：{capture.Blocked}",
                    new Dictionary<string, object?> { ["blocked"] = capture.Blocked.ToString() });

            if (!capture.Success && !force)
            {
                return Run(verbName, false,
                    $"本次没抓住（roll={capture.Roll} 概率={capture.ChanceBps}bps）；GM 发放请加 --force",
                    new Dictionary<string, object?>
                    {
                        ["roll"] = capture.Roll,
                        ["chanceBps"] = capture.ChanceBps,
                        ["instanceId"] = 0L,
                    });
            }

            var instanceId = capture.NewInstanceId;
            if (instanceId == PetIdentityCore.UnassignedInstanceId)
                return Run(verbName, false, $"个体编号分配失败（{capture.IdentityIssue}）", null);

            var bond = PetBondValues.Default;
            var slots = ParseSkillSlots(args);
            var row = new PetInstanceEntity
            {
                Id = instanceId,
                SpeciesId = speciesId,
                OwnerCharacterId = ownerCharacterId,
                Nickname = template.Name,
                Generation = (byte)capture.Stage,
                Origin = (byte)capture.Origin,
                State = (byte)state,
                Level = level,
                Loyalty = bond.Loyalty,
                Happiness = bond.Happiness,
                Lifespan = bond.Lifespan,
                Intimacy = bond.Intimacy,
                AttackQuality = capture.Qualities.Attack,
                DefenseQuality = capture.Qualities.Defense,
                StaminaQuality = capture.Qualities.Stamina,
                ManaQuality = capture.Qualities.Mana,
                SpeedQuality = capture.Qualities.Speed,
                GrowthBps = capture.GrowthBps,
                SkillSlotsJson = PetInstanceCodec.EncodeSkillSlots(slots),
                EquipmentJson = PetInstanceCodec.EncodeWornGear(Array.Empty<PetWornGear>()),
                MutationTier = (int)capture.Origin == (int)PetOrigin.Mutant ? 1 : 0,
                DuplicateFingerprint = PetIdentityCore.DuplicateFingerprint(
                    speciesId, (int)capture.Stage, PetIdentityCore.HashOf(capture.Qualities), capture.GrowthBps),
                CaptureSource = force ? "cli instance add --force" : "cli instance add",
                CapturedAt = DateTime.Now,
                IsValid = true,
            };

            var result = store.AddInstance(row, 0, force ? "GM 发放" : "捕捉成功");
            var data = new Dictionary<string, object?>
            {
                ["instanceId"] = instanceId,
                ["sequence"] = nextSequence,
                ["ownerCharacterId"] = ownerCharacterId,
                ["origin"] = capture.Origin.ToString(),
                ["stage"] = capture.Stage.ToString(),
                ["state"] = state.ToString(),
                ["roll"] = capture.Roll,
                ["chanceBps"] = capture.ChanceBps,
                ["forced"] = force,
                ["skillSlots"] = slots.Length,
                ["writtenRows"] = result.WrittenRows,
            };
            return Run(verbName, result.Ok, result.Message, data);
        }
    }

    /// <summary>把重复的 --skill 技能id:等级 落成技能槽（缺省空表）。</summary>
    private static PetSkillSlot[] ParseSkillSlots(string[] args)
    {
        var list = new List<PetSkillSlot>();
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--skill", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= args.Length) continue;
            var parts = args[i + 1].Split(':');
            if (!int.TryParse(parts[0], out var skillId) || skillId <= 0) continue;
            var level = parts.Length > 1 && int.TryParse(parts[1], out var lv) ? lv : 1;
            list.Add(new PetSkillSlot { SkillId = skillId, Kind = PetSkillSlotKind.Learned, Level = level });
        }
        return list.ToArray();
    }

    /// <summary>长整数选项（角色 id / 个体号都超 int）；两种写法都认。</summary>
    private static bool TryGetOptLong(string[] args, string name, out long value)
    {
        value = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            return long.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return hit != null && long.TryParse(hit.Substring(prefix.Length),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>文本选项（--state battling）；同样认 --state=battling 这种写法。</summary>
    private static bool TryGetOptText(string[] args, string name, out string value)
    {
        value = string.Empty;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            value = args[i + 1];
            return true;
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hit == null) return false;
        value = hit.Substring(prefix.Length);
        return true;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, "--" + name, StringComparison.OrdinalIgnoreCase));

    // ---------------- 助手 ----------------

    private static bool TryOpenStore(out PetStore? store, out string error)
    {
        var conn = ConnectionConfig.Resolve(out var hint);
        if (string.IsNullOrWhiteSpace(conn))
        {
            store = null;
            error = "数据库连接串不可用。" + hint;
            return false;
        }
        store = new PetStore(conn!);
        if (!store.TemplateTableReachable(out var tableError))
        {
            error = $"宠物表不可达（迁移脚本 scripts/sql/021_pet_editor_tables.sql 是否已执行？）：{tableError}";
            store.Dispose();
            store = null;
            return false;
        }
        error = string.Empty;
        return true;
    }

    /// <summary>从 stdin（"-"）或文件读草稿；顺带做<b>键名自证</b>：未知键说明两侧漂移，而 STJ 会静默丢弃它。</summary>
    private static bool ReadDraft(string[] rest, out PetDraft? draft, out string error)
    {
        draft = null;
        error = string.Empty;
        var source = rest.Length > 0 ? rest[0] : "-";
        string json;
        try
        {
            json = source == "-" ? _stdin.ReadToEnd() : File.ReadAllText(source);
        }
        catch (Exception ex)
        {
            error = "读取草稿失败：" + ex.GetBaseException().Message;
            return false;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "草稿为空（stdin 没有内容）。";
            return false;
        }

        var unknownKeys = FindUnknownKeys(json);
        if (unknownKeys.Count > 0)
        {
            error = "草稿含未知键，将被静默丢弃：" + string.Join(", ", unknownKeys)
                    + "（请对齐 PetDraft.KnownTopLevelKeys / KnownSectionKeys；两侧字段名是协议）";
            return false;
        }

        draft = PetDraft.FromJson(json);
        if (draft == null)
        {
            error = "JSON 反序列化失败";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 找出顶层与子段里对不上已知键集合的键名。
    /// <para>一次真实缺陷模式：字段名漂一个字母，STJ 不报错、值全是初值，
    /// 而"两侧同为 0"让自校验反而全绿。</para>
    /// </summary>
    private static List<string> FindUnknownKeys(string json)
    {
        var unknown = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return unknown;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!PetDraft.KnownTopLevelKeys.Contains(prop.Name))
                {
                    unknown.Add(prop.Name);
                    continue;
                }
                if (!PetDraft.KnownSectionKeys.TryGetValue(prop.Name, out var allowed)) continue;

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    CollectUnknown(prop.Value, allowed, prop.Name, unknown);
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        CollectUnknown(item, allowed, prop.Name + "[]", unknown);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 交给反序列化那一步去报"JSON 坏了"，这里不重复报。
        }
        return unknown;
    }

    private static void CollectUnknown(JsonElement element, string[] allowed, string prefix, List<string> unknown)
    {
        if (element.ValueKind != JsonValueKind.Object) return;
        foreach (var child in element.EnumerateObject())
        {
            if (!allowed.Contains(child.Name)) unknown.Add(prefix + "." + child.Name);
        }
    }

    private static int[] ParseIdList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return Array.Empty<int>();
        var list = new List<int>();
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Add(v);
        }
        return list.ToArray();
    }

    private static bool TryParseInt(string[] args, out int value)
    {
        foreach (var a in args)
        {
            var token = a.StartsWith("--") ? a.Substring(2) : a;
            var candidate = token.Contains('=') ? token.Split('=', 2)[1] : token;
            if (int.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        }
        value = 0;
        return false;
    }

    private static bool TryGetOpt(string[] args, string name, out int value)
    {
        value = 0;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (hit != null) return int.TryParse(hit.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        return false;
    }

    private static string? TryGetOptString(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--" + name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }
        var prefix = "--" + name + "=";
        var hit = args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return hit != null ? hit.Substring(prefix.Length) : null;
    }

    private static int Run(string verb, bool ok, string message, object? data = null, List<string>? warnings = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["ok"] = ok,
            ["verb"] = verb,
            ["message"] = message,
            ["data"] = data,
            ["warnings"] = warnings != null && warnings.Count > 0 ? warnings : null,
        };
        _stdout.Write(JsonSerializer.Serialize(payload, OutputOptions));
        _stdout.Write("\n");
        return ok ? 0 : 1;
    }
}
