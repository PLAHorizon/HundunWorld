using System.Globalization;
using Horizon.Entities;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;

namespace Horizon.PetCli;

/// <summary>一次写入的结果（与 MonsterCli 的 StoreResult 同形，编辑器解析代码因此可以复用）。</summary>
public sealed class PetStoreResult
{
    public bool Ok { get; set; }
    public int SpeciesId { get; set; }
    public int ContentVersion { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();

    /// <summary>本次实际落库的行数。<b>每个写方都要自带状态位</b>：只回 ok=true 说不清"写了几张表"。</summary>
    public Dictionary<string, int> WrittenRows { get; set; } = new();
}

/// <summary>列表行摘要。</summary>
public sealed class PetListRow
{
    public int SpeciesId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ModelKey { get; set; } = string.Empty;
    public int CarryLevelMin { get; set; }
    public int CarryLevelMax { get; set; }
    public int RarityTier { get; set; }
    public int PublishState { get; set; }
    public int ContentVersion { get; set; }
    public int SkillEntryCount { get; set; }
    public int AssistEntryCount { get; set; }
    public int MutationEntryCount { get; set; }
    public int InstanceCount { get; set; }
}

/// <summary>
/// 宠物表的数据访问；同时是校验器的引用对账实现（<see cref="IPetReferenceIndex"/>）。
/// <para>
/// <b>写顺序 = 引用完整性顺序</b>：模板 → 资质区间 → 槽位规则 → 技能池 → 行为 → 装备 → 辅助 → 合成 → 变异。
/// 反过来写会让"技能池指向一个还不存在的物种"这一刻真实存在，而运行时只读已发布行，
/// 于是这个物种整条技能链为空且没有任何日志。
/// </para>
/// <para>
/// <b>主键属性名是 <c>Id</c> 而不是 <c>SpeciesId</c></b>：实体沿用本项目惯例，用
/// <c>public new int Id</c> 覆盖基类主键并把它映射到列 <c>species_id</c>。
/// 1:1 从表按 <c>Id</c> 查；关联表（技能池/辅助/变异）上 <c>SpeciesId</c> 是一个真的外键列。
/// </para>
/// <para>
/// <b>号段列不自增</b>：<c>species_id</c> 与 <c>pet_instance_id</c> 都是算号制，
/// 由 <see cref="PetIdentityCore"/> 分配。Store 只写调用方给定的号，绝不"顺手让数据库生成"。
/// </para>
/// </summary>
public sealed class PetStore : IPetReferenceIndex, IDisposable
{
    private readonly GameEntityContext _db;

    public PetStore(string connectionString)
    {
        var options = new DbContextOptionsBuilder<GameEntityContext>()
            .UseSqlServer(connectionString)
            .Options;
        _db = new GameEntityContext(options);
    }

    /// <summary>主表是否可达（用来区分"没配宠物"与"库还没迁移"——这两件事在只读侧长得一模一样）。</summary>
    public bool TemplateTableReachable(out string error)
    {
        try
        {
            _ = _db.PetTemplates.Count();
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.GetBaseException().Message;
            return false;
        }
    }

    /// <summary>技能是否存在（跨域对账；技能表里查不到时只 warning，不拦保存）。</summary>
    public bool SkillExists(int skillId)
    {
        try
        {
            return _db.SkillTemplates.Any(s => s.Id == skillId);
        }
        catch (Exception)
        {
            // 查不到不等于不存在：技能表不可达时按"存在"处理，把这条对账降级为无信息，
            // 而不是让一次库连接失败去否决一份合法草稿。
            return true;
        }
    }

    // ---------------- 读 ----------------

    /// <summary>列表（含号段推断出的稀有度档，供编辑器分组显示）。</summary>
    public List<PetListRow> List()
    {
        var rows = new List<PetListRow>();
        var templates = _db.PetTemplates.AsNoTracking().OrderBy(t => t.Id).ToList();
        var skillCounts = GroupCount(_db.PetSkillPools.AsNoTracking().Select(p => p.SpeciesId));
        var assistCounts = GroupCount(_db.PetAssistRules.AsNoTracking().Select(p => p.SpeciesId));
        var mutationCounts = GroupCount(_db.PetMutationRules.AsNoTracking().Select(p => p.SpeciesId));
        var instanceCounts = GroupCount(_db.PetInstances.AsNoTracking().Select(p => p.SpeciesId));

        foreach (var t in templates)
        {
            rows.Add(new PetListRow
            {
                SpeciesId = t.Id,
                Name = t.Name,
                ModelKey = t.ModelKey,
                CarryLevelMin = t.CarryLevelMin,
                CarryLevelMax = t.CarryLevelMax,
                RarityTier = (int)PetIdentityCore.RarityTierOf(t.Id),
                PublishState = t.PublishState,
                ContentVersion = t.ContentVersion,
                SkillEntryCount = skillCounts.GetValueOrDefault(t.Id),
                AssistEntryCount = assistCounts.GetValueOrDefault(t.Id),
                MutationEntryCount = mutationCounts.GetValueOrDefault(t.Id),
                InstanceCount = instanceCounts.GetValueOrDefault(t.Id),
            });
        }
        return rows;
    }

    private static Dictionary<int, int> GroupCount(IQueryable<int> source)
        => source.AsEnumerable().GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>把一个物种的九张表读回成一份草稿（编辑器的"从库读取"按钮）。</summary>
    public PetDraft? GetDraft(int speciesId, out string error)
    {
        error = string.Empty;
        var t = _db.PetTemplates.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (t is null)
        {
            error = $"物种 {speciesId} 不存在";
            return null;
        }

        var draft = new PetDraft
        {
            SpeciesId = t.Id,
            Name = t.Name,
            ModelKey = t.ModelKey,
            PrefabPath = t.PrefabPath,
            PrefabGuid = t.PrefabGuid,
            ModelScale = t.ModelScale,
            StandState = t.StandState,
            BaseColorKey = t.BaseColorKey,
            CarryLevelMin = t.CarryLevelMin,
            CarryLevelMax = t.CarryLevelMax,
            AllowedOrigins = t.AllowedOrigins,
            AllowedGrowthTiers = t.AllowedGrowthTiers,
            HatchSeconds = t.HatchSeconds,
            HatchLevel = t.HatchLevel,
            OwnerLevelSlack = t.OwnerLevelSlack,
            PotentialPerLevel = t.PotentialPerLevel,
            ImmortalLifespan = t.ImmortalLifespan,
            HappinessScale = t.HappinessScale,
            CaptureMode = t.CaptureMode,
            CaptureBaseBps = t.CaptureBaseBps,
            CaptureGuidanceSeconds = t.CaptureGuidanceSeconds,
            MutationChanceBps = t.MutationChanceBps,
            PublishState = t.PublishState,
            ContentVersion = t.ContentVersion,
            Remark = t.Remark,
        };

        var band = _db.PetStatBands.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (band is not null)
        {
            draft.Stats = new PetStatBandDraft
            {
                AttackMin = band.AttackMin, AttackMax = band.AttackMax,
                DefenseMin = band.DefenseMin, DefenseMax = band.DefenseMax,
                StaminaMin = band.StaminaMin, StaminaMax = band.StaminaMax,
                ManaMin = band.ManaMin, ManaMax = band.ManaMax,
                SpeedMin = band.SpeedMin, SpeedMax = band.SpeedMax,
                GrowthMinBps = band.GrowthMinBps, GrowthMaxBps = band.GrowthMaxBps,
                ComprehensionMax = band.ComprehensionMax, SpiritMax = band.SpiritMax,
                SuperSpirit = band.SuperSpirit, MutationTierMax = band.MutationTierMax,
            };
        }

        var rule = _db.PetSkillRules.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (rule is not null)
        {
            draft.SkillRule = new PetSkillRuleDraft
            {
                BaseSlots = rule.BaseSlots, AbsoluteMaxSlots = rule.AbsoluteMaxSlots,
                SlotsByGrade = rule.SlotsByGrade,
                ComprehensionUnlockA = rule.ComprehensionUnlockA,
                ComprehensionUnlockB = rule.ComprehensionUnlockB,
                SlotGrowMaxLevel = rule.SlotGrowMaxLevel,
                SlotGrowMaxExisting = rule.SlotGrowMaxExisting,
                SlotGrowChanceBps = rule.SlotGrowChanceBps,
                OverwriteChanceBps = rule.OverwriteChanceBps,
                MaxLockableSlots = rule.MaxLockableSlots,
                LearnedPoolMax = rule.LearnedPoolMax,
                EquippedMax = rule.EquippedMax,
                InnerCoreSlots = rule.InnerCoreSlots,
            };
        }

        draft.SkillEntries = _db.PetSkillPools.AsNoTracking().Where(p => p.SpeciesId == speciesId)
            .OrderBy(p => p.Seq).Select(p => new PetSkillEntryDraft
            {
                SkillId = p.SkillId, Kind = p.Kind, InnateSlot = p.InnateSlot,
                MinLevel = p.MinLevel, Conflicts = p.Conflicts, Advanced = p.Advanced, Seq = p.Seq,
            }).ToList();

        var beh = _db.PetBehaviors.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (beh is not null)
        {
            draft.Behavior = new PetBehaviorDraft
            {
                IntellectTier = beh.IntellectTier,
                FlagUseAcquire = beh.FlagUseAcquire, FlagUseAutoCast = beh.FlagUseAutoCast,
                FlagUseEvade = beh.FlagUseEvade, FlagUseGuard = beh.FlagUseGuard, FlagUseHold = beh.FlagUseHold,
                DefaultStance = beh.DefaultStance, DefaultCommand = beh.DefaultCommand,
                AllowedPersonalities = beh.AllowedPersonalities, EpiphanyChanceBps = beh.EpiphanyChanceBps,
                FollowDistance = beh.FollowDistance, LeashRadius = beh.LeashRadius,
                TeleportBeyondRadius = beh.TeleportBeyondRadius, RepathIntervalTicks = beh.RepathIntervalTicks,
                FollowSpeed = beh.FollowSpeed, ArriveEpsilon = beh.ArriveEpsilon,
                CorpseSeconds = beh.CorpseSeconds, ReviveCastSeconds = beh.ReviveCastSeconds,
                SummonCooldownSeconds = beh.SummonCooldownSeconds, FollowStartDelayMs = beh.FollowStartDelayMs,
                InitialLoyalty = beh.InitialLoyalty, InitialHappiness = beh.InitialHappiness,
                InitialLifespan = beh.InitialLifespan, InitialIntimacy = beh.InitialIntimacy,
            };
        }

        var equip = _db.PetEquipRules.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (equip is not null)
        {
            draft.Equip = new PetEquipRuleDraft
            {
                AllowedSlots = equip.AllowedSlots, GemAllowed = equip.GemAllowed, MaxStar = equip.MaxStar,
                SetFamily = equip.SetFamily, AppraisalEnabled = equip.AppraisalEnabled,
                AwakeningEnabled = equip.AwakeningEnabled, AwakeningPieceCount = equip.AwakeningPieceCount,
                Ownership = equip.Ownership,
            };
        }

        draft.AssistEntries = _db.PetAssistRules.AsNoTracking().Where(p => p.SpeciesId == speciesId)
            .OrderBy(p => p.Seq).Select(p => new PetAssistEntryDraft
            {
                SkillId = p.SkillId, AssistKind = p.AssistKind, RequiresBattle = p.RequiresBattle,
                Possessable = p.Possessable, CoefficientBps = p.CoefficientBps, Seq = p.Seq,
            }).ToList();

        var merge = _db.PetMergeRules.AsNoTracking().FirstOrDefault(x => x.Id == speciesId);
        if (merge is not null)
        {
            draft.Merge = new PetMergeRuleDraft
            {
                FuseEnabled = merge.FuseEnabled, BreedEnabled = merge.BreedEnabled,
                FuseMinLevel = merge.FuseMinLevel, FuseSuccessBps = merge.FuseSuccessBps,
                FuseQualityMinBps = merge.FuseQualityMinBps, FuseQualityMaxBps = merge.FuseQualityMaxBps,
                FuseGrowthMinBps = merge.FuseGrowthMinBps, FuseGrowthMaxBps = merge.FuseGrowthMaxBps,
                FuseMaxInheritedSkills = merge.FuseMaxInheritedSkills,
                FuseConsumesParents = merge.FuseConsumesParents,
                BreedMaxLevelGap = merge.BreedMaxLevelGap, BreedRequiredHappiness = merge.BreedRequiredHappiness,
                BreedRequiredLifespan = merge.BreedRequiredLifespan, BreedOffspringCount = merge.BreedOffspringCount,
                BreedClaimWindowSeconds = merge.BreedClaimWindowSeconds, BreedCost = merge.BreedCost,
            };
        }

        draft.Mutations = _db.PetMutationRules.AsNoTracking()
            .Where(p => p.SpeciesId == speciesId || p.SpeciesId == 0)
            .Select(p => new PetMutationEntryDraft
            {
                MutationKey = p.MutationKey, DurationTicks = p.DurationTicks, QualityCapBps = p.QualityCapBps,
                ColorOverride = p.ColorOverride, ExtraSkillSlots = p.ExtraSkillSlots, Enabled = p.Enabled,
            }).ToList();

        return draft;
    }

    // ---------------- 写 ----------------

    /// <summary>
    /// 保存草稿到九张表（upsert）。
    /// <para><b>新建时才采用草稿里的 publishState</b>：更新路径上沿用旧值，
    /// 否则"存个草稿"会顺手把物种发布上线（编辑期与运行期之间那条线只能由 publish 动词跨）。</para>
    /// </summary>
    public PetStoreResult Save(PetDraft draft)
    {
        var result = new PetStoreResult
        {
            SpeciesId = draft.SpeciesId,
            ContentVersion = draft.ContentVersion,
        };

        if (!PetIdentityCore.IsSpeciesId(draft.SpeciesId))
        {
            result.Message = $"宠物编号 {draft.SpeciesId} 不在号段内，拒绝写入";
            return result;
        }

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var row = _db.PetTemplates.FirstOrDefault(x => x.Id == draft.SpeciesId);
            var isNew = row is null;
            if (isNew)
            {
                row = new PetTemplateEntity { Id = draft.SpeciesId };
                _db.PetTemplates.Add(row);
            }

            row.Name = draft.Name;
            row.ModelKey = draft.ModelKey;
            row.PrefabPath = draft.PrefabPath;
            row.PrefabGuid = draft.PrefabGuid;
            row.ModelScale = draft.ModelScale;
            row.StandState = draft.StandState;
            row.BaseColorKey = draft.BaseColorKey;
            row.CarryLevelMin = draft.CarryLevelMin;
            row.CarryLevelMax = draft.CarryLevelMax;
            row.AllowedOrigins = draft.AllowedOrigins;
            row.AllowedGrowthTiers = draft.AllowedGrowthTiers;
            row.HatchSeconds = draft.HatchSeconds;
            row.HatchLevel = draft.HatchLevel;
            row.OwnerLevelSlack = draft.OwnerLevelSlack;
            row.PotentialPerLevel = draft.PotentialPerLevel;
            row.ImmortalLifespan = draft.ImmortalLifespan;
            row.HappinessScale = draft.HappinessScale;
            row.CaptureMode = draft.CaptureMode;
            row.CaptureBaseBps = draft.CaptureBaseBps;
            row.CaptureGuidanceSeconds = draft.CaptureGuidanceSeconds;
            row.MutationChanceBps = draft.MutationChanceBps;
            row.ContentVersion = draft.ContentVersion;
            row.Remark = draft.Remark;
            if (isNew) row.PublishState = (byte)Math.Clamp((int)draft.PublishState, 0, 2);
            result.WrittenRows["template"] = 1;

            var band = Upsert(_db.PetStatBands, draft.SpeciesId);
            band.AttackMin = draft.Stats.AttackMin; band.AttackMax = draft.Stats.AttackMax;
            band.DefenseMin = draft.Stats.DefenseMin; band.DefenseMax = draft.Stats.DefenseMax;
            band.StaminaMin = draft.Stats.StaminaMin; band.StaminaMax = draft.Stats.StaminaMax;
            band.ManaMin = draft.Stats.ManaMin; band.ManaMax = draft.Stats.ManaMax;
            band.SpeedMin = draft.Stats.SpeedMin; band.SpeedMax = draft.Stats.SpeedMax;
            band.GrowthMinBps = draft.Stats.GrowthMinBps; band.GrowthMaxBps = draft.Stats.GrowthMaxBps;
            band.ComprehensionMax = draft.Stats.ComprehensionMax; band.SpiritMax = draft.Stats.SpiritMax;
            band.SuperSpirit = draft.Stats.SuperSpirit; band.MutationTierMax = draft.Stats.MutationTierMax;
            result.WrittenRows["statBand"] = 1;

            var skillRule = Upsert(_db.PetSkillRules, draft.SpeciesId);
            skillRule.BaseSlots = draft.SkillRule.BaseSlots;
            skillRule.AbsoluteMaxSlots = draft.SkillRule.AbsoluteMaxSlots;
            skillRule.SlotsByGrade = draft.SkillRule.SlotsByGrade;
            skillRule.ComprehensionUnlockA = draft.SkillRule.ComprehensionUnlockA;
            skillRule.ComprehensionUnlockB = draft.SkillRule.ComprehensionUnlockB;
            skillRule.SlotGrowMaxLevel = draft.SkillRule.SlotGrowMaxLevel;
            skillRule.SlotGrowMaxExisting = draft.SkillRule.SlotGrowMaxExisting;
            skillRule.SlotGrowChanceBps = draft.SkillRule.SlotGrowChanceBps;
            skillRule.OverwriteChanceBps = draft.SkillRule.OverwriteChanceBps;
            skillRule.MaxLockableSlots = draft.SkillRule.MaxLockableSlots;
            skillRule.LearnedPoolMax = draft.SkillRule.LearnedPoolMax;
            skillRule.EquippedMax = draft.SkillRule.EquippedMax;
            skillRule.InnerCoreSlots = draft.SkillRule.InnerCoreSlots;
            result.WrittenRows["skillRule"] = 1;

            // 集合类表：先删本物种的旧行再写新行。留着旧行会让"删掉一个技能"看起来失败。
            var oldPools = _db.PetSkillPools.Where(p => p.SpeciesId == draft.SpeciesId).ToList();
            _db.PetSkillPools.RemoveRange(oldPools);
            var poolSeq = 0;
            foreach (var entry in draft.SkillEntries)
            {
                poolSeq++;
                _db.PetSkillPools.Add(new PetSkillPoolEntity
                {
                    SpeciesId = draft.SpeciesId,
                    SkillId = entry.SkillId,
                    Kind = entry.Kind,
                    InnateSlot = entry.InnateSlot,
                    MinLevel = entry.MinLevel,
                    Conflicts = entry.Conflicts,
                    Advanced = entry.Advanced,
                    Seq = entry.Seq > 0 ? entry.Seq : poolSeq,
                });
            }
            result.WrittenRows["skillPool"] = draft.SkillEntries.Count;

            var beh = Upsert(_db.PetBehaviors, draft.SpeciesId);
            beh.IntellectTier = draft.Behavior.IntellectTier;
            beh.FlagUseAcquire = draft.Behavior.FlagUseAcquire;
            beh.FlagUseAutoCast = draft.Behavior.FlagUseAutoCast;
            beh.FlagUseEvade = draft.Behavior.FlagUseEvade;
            beh.FlagUseGuard = draft.Behavior.FlagUseGuard;
            beh.FlagUseHold = draft.Behavior.FlagUseHold;
            beh.DefaultStance = draft.Behavior.DefaultStance;
            beh.DefaultCommand = draft.Behavior.DefaultCommand;
            beh.AllowedPersonalities = draft.Behavior.AllowedPersonalities;
            beh.EpiphanyChanceBps = draft.Behavior.EpiphanyChanceBps;
            beh.FollowDistance = draft.Behavior.FollowDistance;
            beh.LeashRadius = draft.Behavior.LeashRadius;
            beh.TeleportBeyondRadius = draft.Behavior.TeleportBeyondRadius;
            beh.RepathIntervalTicks = draft.Behavior.RepathIntervalTicks;
            beh.FollowSpeed = draft.Behavior.FollowSpeed;
            beh.ArriveEpsilon = draft.Behavior.ArriveEpsilon;
            beh.CorpseSeconds = draft.Behavior.CorpseSeconds;
            beh.ReviveCastSeconds = draft.Behavior.ReviveCastSeconds;
            beh.SummonCooldownSeconds = draft.Behavior.SummonCooldownSeconds;
            beh.FollowStartDelayMs = draft.Behavior.FollowStartDelayMs;
            beh.InitialLoyalty = draft.Behavior.InitialLoyalty;
            beh.InitialHappiness = draft.Behavior.InitialHappiness;
            beh.InitialLifespan = draft.Behavior.InitialLifespan;
            beh.InitialIntimacy = draft.Behavior.InitialIntimacy;
            result.WrittenRows["behavior"] = 1;

            var equip = Upsert(_db.PetEquipRules, draft.SpeciesId);
            equip.AllowedSlots = draft.Equip.AllowedSlots;
            equip.GemAllowed = draft.Equip.GemAllowed;
            equip.MaxStar = draft.Equip.MaxStar;
            equip.SetFamily = draft.Equip.SetFamily;
            equip.AppraisalEnabled = draft.Equip.AppraisalEnabled;
            equip.AwakeningEnabled = draft.Equip.AwakeningEnabled;
            equip.AwakeningPieceCount = draft.Equip.AwakeningPieceCount;
            equip.Ownership = draft.Equip.Ownership;
            result.WrittenRows["equipRule"] = 1;

            var oldAssists = _db.PetAssistRules.Where(p => p.SpeciesId == draft.SpeciesId).ToList();
            _db.PetAssistRules.RemoveRange(oldAssists);
            var assistSeq = 0;
            foreach (var entry in draft.AssistEntries)
            {
                assistSeq++;
                _db.PetAssistRules.Add(new PetAssistRuleEntity
                {
                    SpeciesId = draft.SpeciesId,
                    SkillId = entry.SkillId,
                    AssistKind = entry.AssistKind,
                    RequiresBattle = entry.RequiresBattle,
                    Possessable = entry.Possessable,
                    CoefficientBps = entry.CoefficientBps,
                    Seq = entry.Seq > 0 ? entry.Seq : assistSeq,
                });
            }
            result.WrittenRows["assistRule"] = draft.AssistEntries.Count;

            var merge = Upsert(_db.PetMergeRules, draft.SpeciesId);
            merge.FuseEnabled = draft.Merge.FuseEnabled;
            merge.BreedEnabled = draft.Merge.BreedEnabled;
            merge.FuseMinLevel = draft.Merge.FuseMinLevel;
            merge.FuseSuccessBps = draft.Merge.FuseSuccessBps;
            merge.FuseQualityMinBps = draft.Merge.FuseQualityMinBps;
            merge.FuseQualityMaxBps = draft.Merge.FuseQualityMaxBps;
            merge.FuseGrowthMinBps = draft.Merge.FuseGrowthMinBps;
            merge.FuseGrowthMaxBps = draft.Merge.FuseGrowthMaxBps;
            merge.FuseMaxInheritedSkills = draft.Merge.FuseMaxInheritedSkills;
            merge.FuseConsumesParents = draft.Merge.FuseConsumesParents;
            merge.BreedMaxLevelGap = draft.Merge.BreedMaxLevelGap;
            merge.BreedRequiredHappiness = draft.Merge.BreedRequiredHappiness;
            merge.BreedRequiredLifespan = draft.Merge.BreedRequiredLifespan;
            merge.BreedOffspringCount = draft.Merge.BreedOffspringCount;
            merge.BreedClaimWindowSeconds = draft.Merge.BreedClaimWindowSeconds;
            merge.BreedCost = draft.Merge.BreedCost;
            result.WrittenRows["mergeRule"] = 1;

            var oldMutations = _db.PetMutationRules.Where(p => p.SpeciesId == draft.SpeciesId).ToList();
            _db.PetMutationRules.RemoveRange(oldMutations);
            foreach (var entry in draft.Mutations)
            {
                _db.PetMutationRules.Add(new PetMutationRuleEntity
                {
                    SpeciesId = draft.SpeciesId,
                    MutationKey = entry.MutationKey,
                    DurationTicks = entry.DurationTicks,
                    QualityCapBps = entry.QualityCapBps,
                    ColorOverride = entry.ColorOverride,
                    ExtraSkillSlots = entry.ExtraSkillSlots,
                    Enabled = entry.Enabled,
                });
            }
            result.WrittenRows["mutationRule"] = draft.Mutations.Count;

            _db.SaveChanges();
            tx.Commit();

            result.Ok = true;
            result.Message = $"物种 {draft.SpeciesId} 已保存（{(isNew ? "新建" : "更新")}）";
            return result;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            result.Ok = false;
            result.Message = "保存失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    /// <summary>发布/停用/退回草稿。</summary>
    public PetStoreResult SetPublishState(int speciesId, int publishState)
    {
        var result = new PetStoreResult { SpeciesId = speciesId };
        var row = _db.PetTemplates.FirstOrDefault(x => x.Id == speciesId);
        if (row is null)
        {
            result.Message = $"物种 {speciesId} 不存在";
            return result;
        }
        row.PublishState = (byte)Math.Clamp(publishState, 0, 2);
        _db.SaveChanges();
        result.Ok = true;
        result.ContentVersion = row.ContentVersion;
        result.WrittenRows["template"] = 1;
        result.Message = publishState switch
        {
            1 => $"物种 {speciesId} 已发布",
            2 => $"物种 {speciesId} 已停用",
            _ => $"物种 {speciesId} 退回草稿",
        };
        return result;
    }

    /// <summary>
    /// 该物种当前已用到的最大序号（个体号 = 物种×100000+序号）。
    /// <para>按"已用最大 + 1"而不是 COUNT()+1：放生不回收编号，删过的行会让 COUNT 小于历史最大，
    /// 于是新宠会撞上旧个体的编号——两个玩家的两只宠共用一个身份，这是内核最不该发生的一类错。</para>
    /// </summary>
    public int NextInstanceSequence(int speciesId)
    {
        var maxSeq = 0;
        foreach (var id in _db.PetInstances.AsNoTracking().Where(p => p.SpeciesId == speciesId).Select(p => p.Id))
        {
            if (PetIdentityCore.TryParseInstanceId(id, out var decoded, out var seq) && decoded == speciesId && seq > maxSeq)
                maxSeq = seq;
        }
        return maxSeq + 1;
    }

    /// <summary>写入一只新个体（捕捉成功或 GM 发放），并留一条事件流水。</summary>
    public PetStoreResult AddInstance(PetInstanceEntity row, byte eventKind, string note)
    {
        var result = new PetStoreResult { SpeciesId = row.SpeciesId };

        if (!PetIdentityCore.TryParseInstanceId(row.Id, out var decodedSpecies, out var sequence))
        {
            result.Message = $"个体编号 {row.Id} 不合法（号段或序号越界），拒绝写入";
            return result;
        }
        if (decodedSpecies != row.SpeciesId)
        {
            result.Message = $"个体编号 {row.Id} 反解出物种 {decodedSpecies}，与 species_id={row.SpeciesId} 不符，拒绝写入";
            return result;
        }
        if (_db.PetInstances.Any(p => p.Id == row.Id))
        {
            result.Message = $"个体编号 {row.Id} 已存在，拒绝覆盖（编号永不复用）";
            return result;
        }
        if (_db.PetTemplates.FirstOrDefault(t => t.Id == row.SpeciesId) is null)
        {
            result.Message = $"物种 {row.SpeciesId} 在库里没有模板行，拒绝写入个体";
            return result;
        }

        using var tx = _db.Database.BeginTransaction();
        try
        {
            _db.PetInstances.Add(row);
            _db.PetInstanceEvents.Add(new PetInstanceEventEntity
            {
                PetInstanceId = row.Id,
                EventKind = eventKind,
                OccurredAt = DateTime.Now,
                // 用序列化器而不是手拼转义：note 里带引号时手拼会产出非法 JSON，
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { note }),
                IsValid = true,
            });
            var changed = _db.SaveChanges();
            tx.Commit();

            // 回执的副作用要跟库对账：写没写进去是查出来的，不是算出来的。
            var back = _db.PetInstances.AsNoTracking().FirstOrDefault(p => p.Id == row.Id);
            result.Ok = back != null;
            result.WrittenRows["instance"] = back != null ? 1 : 0;
            result.WrittenRows["event"] = changed - (back != null ? 1 : 0);
            result.ContentVersion = row.ContentVersion;
            result.Message = back != null
                ? $"个体 {row.Id}（物种 {row.SpeciesId} 第 {sequence} 只）已落库并回读到"
                : $"提交后回读不到个体 {row.Id}——写入未生效，请查触发器/约束";
            return result;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            result.Message = "写入失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    /// <summary>删除模板及其八张从表。<b>个体实例不在删除范围内</b>：宠物被抓后永久存在。</summary>
    public PetStoreResult Delete(int speciesId)
    {
        var result = new PetStoreResult { SpeciesId = speciesId };
        var row = _db.PetTemplates.FirstOrDefault(x => x.Id == speciesId);
        if (row is null)
        {
            result.Message = $"物种 {speciesId} 不存在";
            return result;
        }

        var instances = _db.PetInstances.Count(p => p.SpeciesId == speciesId);
        if (instances > 0)
        {
            // 拦下来而不是级联删：删掉模板会让玩家手里那 instances 只宠从此查不到物种。
            result.Message = $"物种 {speciesId} 仍有 {instances} 只个体存在，拒绝删除模板"
                             + "（宠物被抓后永久存在；请先停用 publish_state=2）";
            return result;
        }

        using var tx = _db.Database.BeginTransaction();
        try
        {
            _db.PetTemplates.Remove(row);
            _db.PetStatBands.RemoveRange(_db.PetStatBands.Where(p => p.Id == speciesId));
            _db.PetSkillRules.RemoveRange(_db.PetSkillRules.Where(p => p.Id == speciesId));
            _db.PetSkillPools.RemoveRange(_db.PetSkillPools.Where(p => p.SpeciesId == speciesId));
            _db.PetBehaviors.RemoveRange(_db.PetBehaviors.Where(p => p.Id == speciesId));
            _db.PetEquipRules.RemoveRange(_db.PetEquipRules.Where(p => p.Id == speciesId));
            _db.PetAssistRules.RemoveRange(_db.PetAssistRules.Where(p => p.SpeciesId == speciesId));
            _db.PetMergeRules.RemoveRange(_db.PetMergeRules.Where(p => p.Id == speciesId));
            _db.PetMutationRules.RemoveRange(_db.PetMutationRules.Where(p => p.SpeciesId == speciesId));
            _db.SaveChanges();
            tx.Commit();
            result.Ok = true;
            result.Message = $"物种 {speciesId} 及其九张从表已删除";
            result.WrittenRows["template"] = 1;
            return result;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            result.Message = "删除失败：" + ex.GetBaseException().Message;
            return result;
        }
    }

    // ---------------- 内容装配 / 外观导出 ----------------

    /// <summary>把已发布物种装配成运行时内容包（DS/客户端读端与 <c>PetContent.Install</c> 的输入）。</summary>
    public PetContentBundle BuildPublishedBundle(out int publishedCount)
    {
        var bundle = new PetContentBundle();
        publishedCount = 0;
        foreach (var row in List().Where(r => r.PublishState == 1))
        {
            var draft = GetDraft(row.SpeciesId, out _);
            if (draft is null) continue;
            bundle.Species[draft.SpeciesId] = ToTemplate(draft);
            publishedCount++;
        }
        bundle.ContentRevision = publishedCount > 0 ? 2 : 1;
        return bundle;
    }

    /// <summary>草稿 → 运行时模板（内核类型；不在这里重算任何数值）。</summary>
    public static PetSpeciesTemplate ToTemplate(PetDraft d)
    {
        var grade = ParseCsv(d.SkillRule.SlotsByGrade);
        return new PetSpeciesTemplate
        {
            SpeciesId = d.SpeciesId,
            Name = d.Name,
            ModelKey = d.ModelKey,
            PrefabPath = d.PrefabPath ?? string.Empty,
            ModelScale = (float)d.ModelScale,
            StandState = d.StandState,
            BaseColorKey = d.BaseColorKey,
            CarryLevelMin = d.CarryLevelMin,
            CarryLevelMax = d.CarryLevelMax,
            AllowedOrigins = ParseCsv(d.AllowedOrigins).Select(v => (PetOrigin)v).ToArray(),
            QualityMin = new PetQualities
            {
                Attack = d.Stats.AttackMin, Defense = d.Stats.DefenseMin, Stamina = d.Stats.StaminaMin,
                Mana = d.Stats.ManaMin, Speed = d.Stats.SpeedMin,
            },
            QualityMax = new PetQualities
            {
                Attack = d.Stats.AttackMax, Defense = d.Stats.DefenseMax, Stamina = d.Stats.StaminaMax,
                Mana = d.Stats.ManaMax, Speed = d.Stats.SpeedMax,
            },
            AllowedGrowthTiers = ParseCsv(d.AllowedGrowthTiers).Select(v => (PetGrowthTier)v).ToArray(),
            HatchSeconds = d.HatchSeconds,
            HatchLevel = d.HatchLevel,
            OwnerLevelSlack = d.OwnerLevelSlack,
            SlotRule = new PetSlotRuleConfig(
                d.SkillRule.BaseSlots, d.SkillRule.AbsoluteMaxSlots,
                grade.Count > 0 ? grade.ToArray() : null,
                d.SkillRule.ComprehensionUnlockA, d.SkillRule.ComprehensionUnlockB,
                d.SkillRule.SlotGrowMaxLevel, d.SkillRule.SlotGrowMaxExisting,
                d.SkillRule.SlotGrowChanceBps, d.SkillRule.OverwriteChanceBps,
                d.SkillRule.MaxLockableSlots, d.SkillRule.LearnedPoolMax, d.SkillRule.EquippedMax),
            InnateSkillIds = d.SkillEntries.Where(x => x.Kind == 1).Select(x => x.SkillId).ToArray(),
            LearnableSkillIds = d.SkillEntries.Where(x => x.Kind == 0).Select(x => x.SkillId).ToArray(),
            EpiphanySkillIds = d.SkillEntries.Where(x => x.Kind == 2).Select(x => x.SkillId).ToArray(),
            EpiphanyChanceBps = d.Behavior.EpiphanyChanceBps,
            IntellectTier = (PetIntellectTier)Math.Min((int)d.Behavior.IntellectTier, (int)PetIntellectTier.Master),
            AiFlagOverrides = new int?[]
            {
                d.Behavior.FlagUseAcquire, d.Behavior.FlagUseAutoCast, d.Behavior.FlagUseEvade,
                d.Behavior.FlagUseGuard, d.Behavior.FlagUseHold,
            },
            DefaultStance = (PetStance)Math.Min((int)d.Behavior.DefaultStance, (int)PetStance.Aggressive),
            DefaultCommand = (PetCommand)Math.Min((int)d.Behavior.DefaultCommand, (int)PetCommand.Retreat),
            AllowedPersonalities = ParseCsv(d.Behavior.AllowedPersonalities).Select(v => (PetPersonality)v).ToArray(),
            InitialBond = new PetBondValues
            {
                Loyalty = d.Behavior.InitialLoyalty, Happiness = d.Behavior.InitialHappiness,
                Lifespan = d.Behavior.InitialLifespan, Intimacy = d.Behavior.InitialIntimacy,
            },
            ImmortalLifespan = d.ImmortalLifespan,
            HappinessScale = d.HappinessScale,
            EquipSlots = ParseCsv(d.Equip.AllowedSlots).Select(v => (PetEquipSlot)v).ToArray(),
            AssistSkillIds = d.AssistEntries.Select(x => x.SkillId).ToArray(),
            MutationChanceBps = d.MutationChanceBps,
            FuseMinLevel = d.Merge.FuseMinLevel,
            Follow = new PetFollowConfig(
                d.Behavior.FollowDistance, d.Behavior.LeashRadius, d.Behavior.TeleportBeyondRadius,
                d.Behavior.RepathIntervalTicks, d.Behavior.FollowSpeed, d.Behavior.ArriveEpsilon,
                d.Behavior.CorpseSeconds, d.Behavior.ReviveCastSeconds, d.Behavior.SummonCooldownSeconds,
                d.Behavior.FollowStartDelayMs),
            CaptureMode = (PetCaptureMode)Math.Min((int)d.CaptureMode, (int)PetCaptureMode.Uncatchable),
            CaptureBaseChanceBps = d.CaptureBaseBps,
            CaptureGuidanceSeconds = d.CaptureGuidanceSeconds,
            Published = d.PublishState == 1,
            ContentVersion = d.ContentVersion,
            Remark = d.Remark,
        };
    }

    private static List<int> ParseCsv(string csv)
    {
        var list = new List<int>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var token in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) list.Add(v);
        }
        return list;
    }

    /// <summary>
    /// 按主键取行；不存在则新建并 Add。<b>只用于 1:1 从表</b>（代理主键的表走删重建）。
    /// <para>用 <c>Find</c> 而不是自己比对主键列：EF 已经知道主键是哪一列，
    /// 手比对很容易在"主键属性被 new 覆盖过"的实体上比到基类那个同名属性，
    /// 结果拿到一份永远插不进去的副本。</para>
    /// </summary>
    private TEntity Upsert<TEntity>(DbSet<TEntity> set, int key) where TEntity : class, new()
    {
        var existing = set.Find(key);
        if (existing is not null) return existing;

        var created = new TEntity();
        var prop = typeof(TEntity).GetProperty("Id");
        if (prop is not null && prop.CanWrite) prop.SetValue(created, key);
        set.Add(created);
        return created;
    }

    public void Dispose() => _db.Dispose();
}
