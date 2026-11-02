using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Pets;
using Horizon.Model.GameModel;

namespace Horizon.Orleans.Grains.Pets;

/// <summary>
/// 一只宠物从库里读出来之后的运行期视图（面板与战斗共同消费的形状）。
/// <para>
/// 为什么视图与 <see cref="PetInstanceEntity"/> 不是一回事：实体是"列的镜像"，
/// 三列 JSON 还没解、资质还是裸资（没乘悟性/灵性/变异）。下游若各自去乘，
/// 就会出现"面板显示的资质和战斗用的资质不同"——而两处都不报错。
/// 所以换算只在 <see cref="PetRosterMapper"/> 发生一次。</para>
/// </summary>
public sealed class PetRosterEntry
{
    /// <summary>个体编号（物种×100000+序号）。</summary>
    public long InstanceId;

    /// <summary>物种编号。</summary>
    public int SpeciesId;

    /// <summary>昵称（仅展示，绝不参与身份判定）。</summary>
    public string Nickname = string.Empty;

    /// <summary>个体状态。</summary>
    public PetInstanceState State;

    /// <summary>等级。</summary>
    public int Level;

    /// <summary>裸资质（库里那一列）。</summary>
    public PetQualities BaseQualities;

    /// <summary>有效资质（已乘悟性/灵性/永久变异；面板与战斗都读这一份）。</summary>
    public PetQualities EffectiveQualities;

    /// <summary>生命上限（由 <see cref="EffectiveQualities"/> 与等级、加点投影而来）。</summary>
    public int PetHp;

    /// <summary>技能槽（解码成功时才有内容）。</summary>
    public PetSkillSlot[] Skills = Array.Empty<PetSkillSlot>();

    /// <summary>穿戴清单。</summary>
    public PetWornGear[] Gear = Array.Empty<PetWornGear>();

    /// <summary>临时变异（过期或空列时为 null）。</summary>
    public TimedPetMutation? TimedMutation;

    /// <summary>
    /// 这只宠身上发生过的"读坏了"（JSON 解析失败、编号与物种不符…）。
    /// <para>非 null 不等于这只宠作废：等级/资质照用，只有出问题的那一列按空处理。
    /// 但回执必须念出来，否则"我的技能怎么没了"无处可查。</para>
    /// </summary>
    public string? Issue;

    /// <summary>参与反哺的辅助技能 id（只取该物种在辅助规则表里登记过的）。</summary>
    public int[] AssistSkillIds = Array.Empty<int>();

    /// <summary>与 <see cref="AssistSkillIds"/> 同序的技能等级。</summary>
    public int[] AssistSkillLevels = Array.Empty<int>();

    /// <summary>出战位（每玩家同时只许一只，见 <see cref="PetAiCore.MaxBattlingPets"/>）。</summary>
    public bool IsBattling => State == PetInstanceState.Battling;

    /// <summary>附体位（给主人加成，不占出战位）。</summary>
    public bool IsPossessed => State == PetInstanceState.Possessed;

    /// <summary>是否参与对主人的反哺。</summary>
    public bool ContributesToOwner => IsBattling || IsPossessed;
}

/// <summary>一次名册装载的结果：条目 + 被挡下的计数 + 逐条问题。</summary>
public sealed class PetRosterResult
{
    /// <summary>可用的条目（已按个体编号升序，保证同一份库回同一份顺序）。</summary>
    public PetRosterEntry[] Entries = Array.Empty<PetRosterEntry>();

    /// <summary>整条作废的个体数（编号非法/越界）。</summary>
    public int Rejected;

    /// <summary>被挡下原因（第一手，用于回执念"为什么少了一只"）。</summary>
    public string FirstRejection = string.Empty;

    /// <summary>有问题的条目数（列解码失败等，条目仍在但那一列为空）。</summary>
    public int Degraded;

    /// <summary>参与反哺的宠物（内核入口要的形状）。</summary>
    public PetAssistCore.ActivePet[] ActivePets = Array.Empty<PetAssistCore.ActivePet>();
}

/// <summary>
/// 宠物个体表的读侧：<c>PetInstanceEntity</c> 行 → 内核运行期视图 → 反哺入参。
/// <para>
/// <b>纯函数、不碰 Orleans</b>：装载与换算分开，换算这段才能被单测直接喂假行验证。
/// 跨 Grain 编排放在 PetRosterGrain，那里只有取数。</para>
/// </summary>
public static class PetRosterMapper
{
    /// <summary>读侧版本（结构变更 +1，写进回执便于对账旧存档）。</summary>
    public const int Version = 1;

    /// <summary>
    /// 把某个主人的全部个体行折成名册。
    /// </summary>
    /// <param name="instances">个体行（未按 IsValid 过滤也可以，本函数会滤）。</param>
    /// <param name="assistRules">辅助规则行（全表或按物种取子集都行；本函数按 species_id 建索引）。</param>
    /// <param name="bands">资质区间行，只用来取 <c>super_spirit</c>（超级灵性影响灵性加成表选哪一张）。</param>
    public static PetRosterResult Build(
        IReadOnlyList<PetInstanceEntity>? instances,
        IReadOnlyList<PetAssistRuleEntity>? assistRules = null,
        IReadOnlyList<PetStatBandEntity>? bands = null)
    {
        var result = new PetRosterResult();
        if (instances is null || instances.Count == 0) return result;

        // 辅助技能域按物种建索引：技能是不是"辅主技"由规则表说了算，
        // 不在这里凭技能 id 区间猜（KindOf 对未知 id 会回落成 OwnerStrength，猜就会把普通攻击技算成借力）。
        var assistBySpecies = new Dictionary<int, HashSet<int>>();
        if (assistRules != null)
        {
            for (var i = 0; i < assistRules.Count; i++)
            {
                var rule = assistRules[i];
                if (rule == null || rule.SpeciesId <= 0 || rule.SkillId <= 0) continue;
                if (!assistBySpecies.TryGetValue(rule.SpeciesId, out var set))
                {
                    set = new HashSet<int>();
                    assistBySpecies[rule.SpeciesId] = set;
                }
                set.Add(rule.SkillId);
            }
        }

        var superSpiritBySpecies = new Dictionary<int, bool>();
        if (bands != null)
        {
            for (var i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                if (band == null || band.Id <= 0) continue;
                // 区间表的主键就是物种号（列名 species_id、访问器被 shadow 成 Id）。
                superSpiritBySpecies[band.Id] = band.SuperSpirit;
            }
        }

        var entries = new List<PetRosterEntry>(instances.Count);
        for (var i = 0; i < instances.Count; i++)
        {
            var row = instances[i];
            if (row == null) continue;
            if (!row.IsValid) continue;

            // 身份自证：个体编号必须能反解回它自己的 species_id 列。
            // 不一致只有两种来源——有人手填编号，或号段基数被改过。两种都必须整条挡下：
            // 让这样的行进名册，重复副本的对账（ReconcileDuplicates）就会长期数不清。
            if (!PetIdentityCore.TryParseInstanceId(row.Id, out var decodedSpecies, out _)
                || decodedSpecies != row.SpeciesId)
            {
                result.Rejected++;
                if (string.IsNullOrEmpty(result.FirstRejection))
                {
                    result.FirstRejection = $"个体 {row.Id} 的编号反解出物种 {decodedSpecies}，与 species_id={row.SpeciesId} 不符";
                }
                continue;
            }

            var entry = new PetRosterEntry
            {
                InstanceId = row.Id,
                SpeciesId = row.SpeciesId,
                Nickname = row.Nickname ?? string.Empty,
                State = (PetInstanceState)row.State,
                Level = row.Level,
                BaseQualities = new PetQualities
                {
                    Attack = row.AttackQuality,
                    Defense = row.DefenseQuality,
                    Stamina = row.StaminaQuality,
                    Mana = row.ManaQuality,
                    Speed = row.SpeedQuality,
                },
            };

            var mutation = PetMutationCore.FromTier(row.MutationTier, row.MutationColorKey ?? string.Empty);
            var superSpirit = superSpiritBySpecies.TryGetValue(row.SpeciesId, out var ss) && ss;

            // 换算只问权威出口 EffectiveQualities，不在这里逐维重抄一遍公式：
            // 抄一份就等于允许它与内核那天加"变异上限封顶"之类的新条款静默分叉。
            entry.EffectiveQualities = PetStatsCore.EffectiveQualities(entry.BaseQualities,
                row.ComprehensionLevel, row.SpiritLevel, superSpirit, mutation.QualityCapBps);

            var points = new PetAttributePoints
            {
                Strength = row.AllocStrength,
                Endurance = row.AllocEndurance,
                Constitution = row.AllocConstitution,
                Spirit = row.AllocSpirit,
                Agility = row.AllocAgility,
            };
            entry.PetHp = PetStatsCore.Compute(entry.EffectiveQualities, row.Level, row.GrowthBps, in points).Hp;

            if (PetInstanceCodec.TryDecodeSkillSlots(row.SkillSlotsJson, out var slots, out var slotError))
            {
                entry.Skills = slots;
            }
            else
            {
                entry.Issue = slotError;
                result.Degraded++;
            }

            if (PetInstanceCodec.TryDecodeWornGear(row.EquipmentJson, out var gear, out var gearError))
            {
                entry.Gear = gear;
            }
            else
            {
                entry.Issue = (entry.Issue == null ? "" : entry.Issue + "；") + gearError;
                result.Degraded++;
            }

            if (PetInstanceCodec.TryDecodeTimedMutation(row.TimedMutationJson, out var timed, out var timedPresent,
                    out var timedError))
            {
                entry.TimedMutation = timedPresent ? timed : null;
            }
            else
            {
                entry.Issue = (entry.Issue == null ? "" : entry.Issue + "；") + timedError;
                result.Degraded++;
            }

            if (assistBySpecies.TryGetValue(row.SpeciesId, out var domain))
            {
                var ids = new List<int>(4);
                var levels = new List<int>(4);
                for (var s = 0; s < entry.Skills.Length; s++)
                {
                    var slot = entry.Skills[s];
                    if (slot.SkillId <= 0 || !domain.Contains(slot.SkillId)) continue;
                    ids.Add(slot.SkillId);
                    levels.Add(slot.Level < 1 ? 1 : slot.Level);
                }
                entry.AssistSkillIds = ids.ToArray();
                entry.AssistSkillLevels = levels.ToArray();
            }

            entries.Add(entry);
        }

        // 顺序由数据决定而不是查询回来的行序：同主同名同种的多只宠必须每次回同一份清单，
        // 否则"附体位取前 3 只"这种截断会随驱动返回顺序变脸（怪物侧栽过同一条）。
        entries.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
        result.Entries = entries.ToArray();
        result.ActivePets = ToActivePets(entries);
        return result;
    }

    /// <summary>名册 → 内核反哺入参（只取出战 + 附体两种状态）。</summary>
    public static PetAssistCore.ActivePet[] ToActivePets(IReadOnlyList<PetRosterEntry>? entries)
    {
        if (entries is null || entries.Count == 0) return Array.Empty<PetAssistCore.ActivePet>();

        var list = new List<PetAssistCore.ActivePet>(4);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null || !e.ContributesToOwner) continue;
            list.Add(new PetAssistCore.ActivePet(
                e.InstanceId, e.Level, e.EffectiveQualities, e.PetHp,
                e.AssistSkillIds, e.AssistSkillLevels,
                // 高级辅主技在规则表里是"另一行技能 id"，本表没有 advanced 列，
                // 因此这里恒 false —— 想要高级版就去登记那个 id，而不是在这里猜。
                Array.Empty<bool>(),
                possessed: e.IsPossessed,
                inBattle: e.IsBattling));
        }
        return list.ToArray();
    }
}
