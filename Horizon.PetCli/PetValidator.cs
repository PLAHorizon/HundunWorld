using System.Globalization;
using Horizon.Game.Sim.Pets;

namespace Horizon.PetCli;

/// <summary>校验结果（结构化，带错误路径）。</summary>
public sealed class PetValidationResult
{
    public bool Ok => Errors.Count == 0;
    public List<string> Errors { get; } = new();
    public List<string> ErrorPaths { get; } = new();
    public List<string> Warnings { get; } = new();

    /// <summary>记录一条错误。<b>路径必须与消息同写</b>：只有消息时，编辑器无法把红点落到具体控件上。</summary>
    public void Error(string path, string message)
    {
        Errors.Add(message);
        ErrorPaths.Add(path);
    }

    public void Warn(string message) => Warnings.Add(message);
}

/// <summary>
/// 宠物草稿的唯一校验器（只在 CLI 一侧存在；插件不复制一份，否则两侧迟早分叉）。
/// <para>
/// <b>校验的层次</b>：① 号段与身份的不变量（越界 = 整条编号契约失效，必须是 error）；
/// ② 区间方向（min&gt;max 会让 roll 静默返回 min，看起来像"这只最差"）；
/// ③ 跨段一致性（技能池引用了未登记的技能、辅助条目引用了不存在的技能等）；
/// ④ 表现层可用性（部位集合为空却启用了镶嵌、档位与能力位冲突等，报 warning）。
/// </para>
/// </summary>
public static class PetValidator
{
    public const int MaxSkillEntries = 32;
    public const int MaxAssistEntries = 16;
    public const int MaxMutationEntries = 8;
    public const int MaxLockablePerSpecies = 4;
    public const int MaxLevel = PetStatsCore.MaxPetLevel;
    public const int MaxBasisPoints = 10_000;

    /// <summary>技能 id 的合法域（引用五行技能树：800000 + 技能id；宠物不另立编号）。</summary>
    public const int SkillIdDomainMin = 800_000;

    public static PetValidationResult Validate(PetDraft draft, IPetReferenceIndex? references = null)
    {
        var r = new PetValidationResult();
        if (draft is null)
        {
            r.Error("draft", "草稿为 null");
            return r;
        }

        // ---------- ① 号段与身份 ----------
        if (!PetIdentityCore.IsSpeciesId(draft.SpeciesId))
        {
            r.Error("speciesId", $"宠物编号 {draft.SpeciesId} 不在号段 "
                                 + $"{PetIdentityCore.MinSpeciesId}-{PetIdentityCore.MaxSpeciesId} 内"
                                 + "（号段即稀有度判据，越界会让 RarityTierOf 返回 Unknown 而不报错）");
        }
        else
        {
            var tier = PetIdentityCore.RarityTierOf(draft.SpeciesId);
            // 神兽段必须不可捕捉：这是"野外不出现、仅活动产出"的数据表达（逆向 §B1）
            if (tier == PetRarityTier.Divine && draft.CaptureMode != (byte)PetCaptureMode.Uncatchable)
                r.Error("captureMode", "神兽号段（650000-699999）的捕捉方式必须是不许捕（4），否则野外可刷");
            if (tier == PetRarityTier.Mutant && draft.MutationChanceBps != 0)
                r.Error("mutationChanceBps", "变异号段（600000-649999）不该再 roll 变异（它本身就是变异种）");
        }

        if (string.IsNullOrWhiteSpace(draft.Name))
            r.Error("name", "名称不得为空（空名会让客户端回退成占位串）");
        if (string.IsNullOrWhiteSpace(draft.ModelKey))
            r.Error("modelKey", "模型键不得为空（外观链按它分流 prefab）");

        if (draft.CarryLevelMin < 0 || draft.CarryLevelMax > MaxLevel)
            r.Error("carryLevelMax", $"携带等级段必须落在 [0,{MaxLevel}]");
        if (draft.CarryLevelMin > draft.CarryLevelMax)
            r.Error("carryLevelMin", "携带等级下界大于上界（roll 会静默返回下界）");

        if (draft.OwnerLevelSlack < 0 || draft.OwnerLevelSlack > 50)
            r.Error("ownerLevelSlack", "允许超出主人的级数应在 [0,50]（0=WoW 档，10=天龙档）");
        if (draft.PotentialPerLevel < 0 || draft.PotentialPerLevel > 20)
            r.Error("potentialPerLevel", "每级潜能点应在 [0,20]");
        if (draft.HappinessScale is not (100 or 1000))
            r.Error("happinessScale", "快乐刻度只支持 100 或 1000（门槛判据按刻度换算）");

        if (draft.HatchSeconds < 0)
            r.Error("hatchSeconds", "孵化秒数不得为负（0 = 捕捉即得，用 0 表达而不是负数）");
        if (draft.HatchLevel < 0 || draft.HatchLevel > MaxLevel)
            r.Error("hatchLevel", $"出壳等级应在 [0,{MaxLevel}]");

        if (draft.CaptureMode > (byte)PetCaptureMode.Uncatchable)
            r.Error("captureMode", "捕捉方式取值必须在 0-4");
        if (draft.CaptureBaseBps < 0 || draft.CaptureBaseBps > MaxBasisPoints)
            r.Error("captureBaseBps", "捕捉基准概率必须在 [0,10000]");
        if (draft.CaptureMode == (byte)PetCaptureMode.TameGuidance && draft.CaptureGuidanceSeconds <= 0)
            r.Warn("驯服方式（captureMode=2）未给引导秒数，内核会回退到默认 20 秒");

        if (draft.MutationChanceBps < 0 || draft.MutationChanceBps > MaxBasisPoints)
            r.Error("mutationChanceBps", "变异概率必须在 [0,10000]");

        // ---------- ② 资质区间方向 ----------
        var s = draft.Stats;
        CheckBand(r, "stats.attack", s.AttackMin, s.AttackMax, PetStatsCore.MinQuality, PetStatsCore.MaxQuality);
        CheckBand(r, "stats.defense", s.DefenseMin, s.DefenseMax, PetStatsCore.MinQuality, PetStatsCore.MaxQuality);
        CheckBand(r, "stats.stamina", s.StaminaMin, s.StaminaMax, PetStatsCore.MinQuality, PetStatsCore.MaxQuality);
        CheckBand(r, "stats.mana", s.ManaMin, s.ManaMax, PetStatsCore.MinQuality, PetStatsCore.MaxQuality);
        CheckBand(r, "stats.speed", s.SpeedMin, s.SpeedMax, PetStatsCore.MinQuality, PetStatsCore.MaxQuality);

        if (s.GrowthMinBps > s.GrowthMaxBps)
            r.Error("stats.growth", "成长下界大于上界（Refine 会把上界钳成下界，看起来像\"成长全是最低档\"）");
        if (s.GrowthMinBps < PetStatsCore.GrowthMinBps || s.GrowthMaxBps > PetStatsCore.GrowthMaxBps)
            r.Error("stats.growth", $"成长必须落在 [{PetStatsCore.GrowthMinBps},{PetStatsCore.GrowthMaxBps}] 万分比");
        // 关系模式要求右侧是编译期常量，而表长是运行时值——这里只能用普通比较。
        if (s.ComprehensionMax < 0 || s.ComprehensionMax > PetStatsCore.ComprehensionBonusBps.Length)
            r.Error("stats.comprehensionMax", $"悟性上限应在 [0,{PetStatsCore.ComprehensionBonusBps.Length}]");
        if (s.SpiritMax < 0 || s.SpiritMax > PetStatsCore.SpiritBonusNormalBps.Length)
            r.Error("stats.spiritMax", $"灵性上限应在 [0,{PetStatsCore.SpiritBonusNormalBps.Length}]");
        if (s.MutationTierMax is < 0 or > PetMutationCore.MaxMutationTier)
            r.Error("stats.mutationTierMax", $"变异最高档应在 [0,{PetMutationCore.MaxMutationTier}]");

        // ---------- ③ 槽位规则与内核的一致性 ----------
        var rule = draft.SkillRule;
        if (rule.BaseSlots < 1)
            r.Error("skillRule.baseSlots", "基础技能格数至少 1");
        if (rule.AbsoluteMaxSlots < rule.BaseSlots)
            r.Error("skillRule.absoluteMaxSlots", "绝对上限不得小于基础格数（内核会把上限钳回基础值，等于白配）");

        var gradeTable = ParseCsvInts(rule.SlotsByGrade);
        if (gradeTable.Count > 0)
        {
            foreach (var g in gradeTable)
            {
                if (g < 1 || g > rule.AbsoluteMaxSlots)
                {
                    r.Error("skillRule.slotsByGrade",
                        $"品级格数 {g} 越出 [1,{rule.AbsoluteMaxSlots}]（会被静默钳位，编辑器里看着像生效了）");
                }
            }
        }

        // 与内核算出的格数对账：这一步让"配了却不生效"当场现形
        var computedMax = PetSkillCore.MaxSlotsFor(ToSlotRule(draft), gradeTable.Count, s.ComprehensionMax);
        if (computedMax > rule.AbsoluteMaxSlots)
            r.Error("skillRule.absoluteMaxSlots",
                $"基础{rule.BaseSlots}+品级+悟性算到 {computedMax} 格，超过绝对上限 {rule.AbsoluteMaxSlots}");

        if (rule.SlotGrowChanceBps is < 0 or > MaxBasisPoints)
            r.Error("skillRule.slotGrowChanceBps", "顶书开格概率必须在 [0,10000]");
        if (rule.OverwriteChanceBps is < 0 or > MaxBasisPoints)
            r.Error("skillRule.overwriteChanceBps", "打书顶替概率必须在 [0,10000]");
        if (rule.MaxLockableSlots > MaxLockablePerSpecies)
            r.Error("skillRule.maxLockableSlots", $"可锁定格数不得超过 {MaxLockablePerSpecies}");
        if (rule.EquippedMax > rule.LearnedPoolMax)
            r.Error("skillRule.equippedMax", "同时装配数不得大于技能池上限（会装配不出，界面显示为空）");
        if (rule.InnerCoreSlots is < 0 or > 6)
            r.Error("skillRule.innerCoreSlots", "内丹格数应在 [0,6]（梦幻按参战等级给 1-6 格）");

        // ---------- ④ 技能池 ----------
        if (draft.SkillEntries.Count > MaxSkillEntries)
            r.Error("skillEntries", $"技能条目最多 {MaxSkillEntries} 条，当前 {draft.SkillEntries.Count}");

        var innateSeen = new HashSet<(int slot, int skill)>();
        var skillIds = new HashSet<int>();
        foreach (var (entry, index) in draft.SkillEntries.Select((e, i) => (e, i)))
        {
            var path = $"skillEntries[{index}]";
            if (entry.SkillId < SkillIdDomainMin)
                r.Error(path + ".skillId", $"技能编号 {entry.SkillId} 不在技能域（应为 {SkillIdDomainMin}+技能id）");
            if (!skillIds.Add(entry.SkillId))
                r.Warn($"{path}: 技能 {entry.SkillId} 在本物种里登记了多次（不同 kind 允许，但通常意味着重复）");
            if (entry.Kind > 2)
                r.Error(path + ".kind", "技能种类只能是 0可学/1天生/2领悟候选");
            if (entry.MinLevel < 0 || entry.MinLevel > MaxLevel)
                r.Error(path + ".minLevel", $"最低学习等级应在 [0,{MaxLevel}]");
            if (entry.Kind == 1)
            {
                if (entry.InnateSlot < 0)
                    r.Error(path + ".innateSlot", "天生技必须指定所在槽位（否则\"恢复到第 1 格\"无从执行）");
                if (!innateSeen.Add((entry.InnateSlot, entry.SkillId)))
                    r.Error(path + ".innateSlot", $"槽位 {entry.InnateSlot} 上有重复的天生技登记");
            }
            else if (entry.InnateSlot != -1)
                r.Warn($"{path}: 非天生技的 innateSlot 应为 -1，当前 {entry.InnateSlot}（内核会忽略它）");

            foreach (var conflictId in ParseCsvInts(entry.Conflicts))
            {
                if (conflictId == entry.SkillId)
                    r.Error(path + ".conflicts", "互斥表不能包含自己（会让这本书永远顶不掉任何技能）");
            }
        }

        // ---------- ⑤ 行为与跟随 ----------
        var b = draft.Behavior;
        if (b.IntellectTier > (byte)PetIntellectTier.Master)
            r.Error("behavior.intellectTier", $"智能档位必须在 [0,{(byte)PetIntellectTier.Master}]");
        if (b.DefaultStance > (byte)PetStance.Aggressive)
            r.Error("behavior.defaultStance", "默认姿态必须在 [0,3]");
        if (b.DefaultCommand > (byte)PetCommand.Retreat)
            r.Error("behavior.defaultCommand", "默认指令必须在 [0,4]");
        foreach (var (name, value) in new[]
                 {
                     ("flagUseAcquire", b.FlagUseAcquire), ("flagUseAutoCast", b.FlagUseAutoCast),
                     ("flagUseEvade", b.FlagUseEvade), ("flagUseGuard", b.FlagUseGuard),
                     ("flagUseHold", b.FlagUseHold),
                 })
        {
            if (value is > 1)
                r.Error("behavior." + name, "能力位覆盖只能是 0/1 或留空（留空 = 跟档位默认）");
        }
        if (b.EpiphanyChanceBps is < 0 or > MaxBasisPoints)
            r.Error("behavior.epiphanyChanceBps", "领悟率必须在 [0,10000]");

        if (b.FollowDistance <= 0f)
            r.Error("behavior.followDistance", "跟随距离必须 > 0（0 会让宠物叠在主人身上）");
        if (b.LeashRadius < b.FollowDistance)
            r.Error("behavior.leashRadius", "leash 半径不得小于跟随距离");
        if (b.TeleportBeyondRadius < b.LeashRadius)
            r.Error("behavior.teleportBeyondRadius", "硬传送半径不得小于 leash（否则一出门就传送，跟随形同虚设）");
        if (b.RepathIntervalTicks < 1)
            r.Error("behavior.repathIntervalTicks", "重算间隔至少 1 tick（0 会让每 tick 重寻路）");
        if (b.FollowSpeed <= 0f)
            r.Error("behavior.followSpeed", "跟随移速必须 > 0（内核会回退默认值，但配 0 说明有人想关掉跟随）");
        if (b.ArriveEpsilon <= 0f)
            r.Error("behavior.arriveEpsilon", "到点判定必须 > 0");
        if (b.CorpseSeconds < 0 || b.ReviveCastSeconds < 0 || b.SummonCooldownSeconds < 0)
            r.Error("behavior.corpseSeconds", "尸体/复活/召唤的秒数不得为负（不留尸体请用 0 表达）");
        if (b.FollowStartDelayMs < 0)
            r.Error("behavior.followStartDelayMs", "起步延迟不得为负");

        if (b.InitialLoyalty is < 0 or > PetBondCore.LoyaltyMax)
            r.Error("behavior.initialLoyalty", $"初始忠诚应在 [0,{PetBondCore.LoyaltyMax}]");
        if (b.InitialHappiness is < 0 or > PetBondCore.HappinessMax)
            r.Error("behavior.initialHappiness", $"初始快乐应在 [0,{PetBondCore.HappinessMax}]（刻度按 happinessScale 换算）");
        if (b.InitialLifespan < 0)
            r.Error("behavior.initialLifespan", "初始寿命不得为负");
        if (b.InitialLifespan > 0 && b.InitialLifespan < PetBondCore.LifespanBlockThreshold && !draft.ImmortalLifespan)
        {
            r.Error("behavior.initialLifespan",
                $"初始寿命 {b.InitialLifespan} 低于出战门槛 {PetBondCore.LifespanBlockThreshold} "
                + "且未标记永生 → 这只宠刚出生就不能出战（要出生即可用请给 ≥ 门槛或勾永生）");
        }
        if (b.InitialHappiness > 0 && b.InitialHappiness < PetBondCore.HappinessBlockThreshold
            && draft.HappinessScale == PetBondCore.HappinessMax)
        {
            r.Warn($"初始快乐 {b.InitialHappiness} 低于出战门槛 {PetBondCore.HappinessBlockThreshold}（千分制），出生即红脸");
        }

        // ---------- ⑥ 装备 ----------
        var e = draft.Equip;
        var slots = ParseCsvInts(e.AllowedSlots).Select(v => (PetEquipSlot)v).ToList();
        foreach (var slot in slots)
        {
            if (!Enum.IsDefined(typeof(PetEquipSlot), slot))
                r.Error("equip.allowedSlots", $"未知装备部位 {(int)slot}");
        }
        if (slots.Count == 0 && e.GemAllowed)
            r.Error("equip.gemAllowed", "部位集合为空（该物种没有装备系统）却启用了灵石镶嵌 → 镶嵌永远不生效");
        if (e.MaxStar is < 1 or > PetEquipmentCore.MaxStar)
            r.Error("equip.maxStar", $"最高星级应在 [1,{PetEquipmentCore.MaxStar}]");
        if (e.SetFamily > 2)
            r.Error("equip.setFamily", "套装族只能是 0无/1按性格/2按部位");
        if (e.AwakeningEnabled && slots.Count < e.AwakeningPieceCount)
            r.Error("equip.awakeningEnabled",
                $"觉醒需要 {e.AwakeningPieceCount} 件达标，而本物种只开放 {slots.Count} 个部位");
        if (e.Ownership > 1)
            r.Error("equip.ownership", "装备归属只能是 0绑宠/1绑角色");

        // ---------- ⑦ 辅助 ----------
        if (draft.AssistEntries.Count > MaxAssistEntries)
            r.Error("assistEntries", $"辅助条目最多 {MaxAssistEntries} 条，当前 {draft.AssistEntries.Count}");
        foreach (var (entry, index) in draft.AssistEntries.Select((a, i) => (a, i)))
        {
            var path = $"assistEntries[{index}]";
            if (entry.SkillId < SkillIdDomainMin)
                r.Error(path + ".skillId", $"辅助技能编号 {entry.SkillId} 不在技能域");
            if (entry.AssistKind > (byte)PetAssistKind.OwnerLeechFromDamage)
                r.Error(path + ".assistKind", $"辅助类型必须在 [0,{(byte)PetAssistKind.OwnerLeechFromDamage}]");
            if (entry.CoefficientBps < 0 || entry.CoefficientBps > 100_000)
                r.Error(path + ".coefficientBps", "系数覆盖应在 [0,100000] 万分比");
        }

        // ---------- ⑧ 合成/繁殖 ----------
        var m = draft.Merge;
        if (m.FuseMinLevel < 1 || m.FuseMinLevel > MaxLevel)
            r.Error("merge.fuseMinLevel", $"炼妖最低等级应在 [1,{MaxLevel}]");
        if (m.FuseSuccessBps is < 0 or > PetCaptureCore.ChanceCeilingBps)
            r.Error("merge.fuseSuccessBps",
                $"炼妖成功率应在 [0,{PetCaptureCore.ChanceCeilingBps}]（不给 100%：满概率会让\"失败吃材料\"那条分支永不执行）");
        CheckBand(r, "merge.fuseQualityBand", m.FuseQualityMinBps, m.FuseQualityMaxBps, 1, 100_000);
        CheckBand(r, "merge.fuseGrowthBand", m.FuseGrowthMinBps, m.FuseGrowthMaxBps, 1, 100_000);
        if (m.FuseMaxInheritedSkills is < 0 or > 16)
            r.Error("merge.fuseMaxInheritedSkills", "继承技能上限应在 [0,16]");
        if (m.BreedMaxLevelGap < 0)
            r.Error("merge.breedMaxLevelGap", "繁殖等级差上限不得为负");
        if (m.BreedRequiredHappiness < 0)
            r.Error("merge.breedRequiredHappiness", "繁殖要求的快乐不得为负");
        if (m.BreedRequiredLifespan < 0)
            r.Error("merge.breedRequiredLifespan", "繁殖要求的寿命不得为负");
        if (m.BreedOffspringCount is < 0 or > 10)
            r.Error("merge.breedOffspringCount", "产仔数应在 [0,10]");
        if (m.BreedClaimWindowSeconds < 0)
            r.Error("merge.breedClaimWindowSeconds", "领取时限不得为负（无期限请用 0）");

        // 号段与"可否炼妖/繁殖"的交叉：变异号段本身就该关掉
        if (PetIdentityCore.IsSpeciesId(draft.SpeciesId)
            && PetIdentityCore.RarityTierOf(draft.SpeciesId) == PetRarityTier.Mutant
            && (m.FuseEnabled || m.BreedEnabled))
        {
            r.Warn("变异号段的物种默认应关掉炼妖与繁殖（梦幻：变异宝宝不可炼妖；天龙：变异不可繁殖）");
        }

        // ---------- ⑨ 临时变异 ----------
        if (draft.Mutations.Count > MaxMutationEntries)
            r.Error("mutations", $"临时变异条目最多 {MaxMutationEntries} 条，当前 {draft.Mutations.Count}");
        foreach (var (entry, index) in draft.Mutations.Select((x, i) => (x, i)))
        {
            var path = $"mutations[{index}]";
            if (string.IsNullOrWhiteSpace(entry.MutationKey))
                r.Error(path + ".mutationKey", "临时变异必须有键名（运行时按它查模板）");
            if (entry.DurationTicks <= 0 && entry.Enabled)
                r.Error(path + ".durationTicks", "启用中的临时变异时长必须 > 0（0 会让它一挂上就到期）");
            if (entry.QualityCapBps < 10_000)
                r.Error(path + ".qualityCapBps", "资质上限放大不得小于 10000（小于 1 倍是\"削弱\"，本层不做削弱）");
            if (entry.QualityCapBps > 30_000)
                r.Error(path + ".qualityCapBps", "资质上限放大不得超过 30000（两层相乘另有封顶）");
            if (entry.ExtraSkillSlots is < 0 or > 4)
                r.Error(path + ".extraSkillSlots", "临时追加技能格应在 [0,4]");
        }

        // ---------- ⑩ 与参考索引的对账（技能是否真的存在） ----------
        if (references is not null)
        {
            var toCheck = new List<int>();
            foreach (var entry in draft.SkillEntries) toCheck.Add(entry.SkillId);
            foreach (var entry in draft.AssistEntries) toCheck.Add(entry.SkillId);

            foreach (var skillId in toCheck)
            {
                if (skillId >= SkillIdDomainMin && !references.SkillExists(skillId))
                    r.Warn($"技能 {skillId} 在技能库里查不到（运行时会被忽略而编辑器仍显示它）");
            }
        }

        return r;
    }

    /// <summary>把草稿的槽位段展开成内核的 <see cref="PetSlotRuleConfig"/>（校验要用同一份算法对账）。</summary>
    public static PetSlotRuleConfig ToSlotRule(PetDraft draft)
    {
        var grade = ParseCsvInts(draft.SkillRule.SlotsByGrade);
        return new PetSlotRuleConfig(
            draft.SkillRule.BaseSlots,
            draft.SkillRule.AbsoluteMaxSlots,
            grade.Count > 0 ? grade.ToArray() : null,
            draft.SkillRule.ComprehensionUnlockA,
            draft.SkillRule.ComprehensionUnlockB,
            draft.SkillRule.SlotGrowMaxLevel,
            draft.SkillRule.SlotGrowMaxExisting,
            draft.SkillRule.SlotGrowChanceBps,
            draft.SkillRule.OverwriteChanceBps,
            draft.SkillRule.MaxLockableSlots,
            draft.SkillRule.LearnedPoolMax,
            draft.SkillRule.EquippedMax);
    }

    private static void CheckBand(PetValidationResult r, string path, int min, int max, int lo, int hi)
    {
        if (min > max)
            r.Error(path, $"{path} 的下界 {min} 大于上界 {max}（roll 会静默返回下界）");
        if (min < lo || max > hi)
            r.Error(path, $"{path} 越出 [{lo},{hi}]：下界 {min}、上界 {max}");
    }

    private static List<int> ParseCsvInts(string csv)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Add(v);
        }
        return list;
    }

    /// <summary>参考索引（技能是否存在等）；由 CLI 用库/目录实现，测试里给假实现。</summary>
    public static IEnumerable<int> ParseCsvOnlyValues(string csv) => ParseCsvInts(csv);
}

/// <summary>跨域引用索引（校验时用来回答"这个技能 id 真的存在吗"）。</summary>
public interface IPetReferenceIndex
{
    bool SkillExists(int skillId);
}
