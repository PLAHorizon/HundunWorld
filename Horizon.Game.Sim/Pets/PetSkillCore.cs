using System;

namespace Horizon.Game.Sim.Pets;

/// <summary>技能槽的形态。</summary>
public enum PetSkillSlotKind : byte
{
    /// <summary>空槽。</summary>
    Empty = 0,

    /// <summary>天生必带技（每宠固定、洗炼必出，梦幻 §B4；不可被顶替，大话 §C4）。</summary>
    Innate = 1,

    /// <summary>打书习得。</summary>
    Learned = 2,

    /// <summary>领悟习得（大话 §C4：参战后概率自动获得，含终极技）。</summary>
    Epiphany = 3,

    /// <summary>被玩家锁定，打书时不许覆盖（大话"锁定技"，§C4）。</summary>
    Locked = 4,
}

/// <summary>打书结果的原因码——界面要据此写"发生了什么"，不能只报成败。</summary>
public enum PetSkillLearnOutcome : byte
{
    /// <summary>写入空槽成功。</summary>
    Added = 0,

    /// <summary>覆盖了一个非锁定、非天生槽（顶书）。</summary>
    Replaced = 1,

    /// <summary>覆盖时被冲突规则判定互斥，新技生效、旧技失效（梦幻 §B4）。</summary>
    ReplacedByConflict = 2,

    /// <summary>顶书开格成功：技能格 +1 并写入（梦幻 §B4 的"顶书"而非"打书"）。</summary>
    SlotGrown = 3,

    /// <summary>拒绝：槽满且所有槽都是天生/锁定，无处可顶。</summary>
    RejectedAllProtected = 4,

    /// <summary>拒绝：已在槽中（重复学习不消耗，避免玩家以为"顶掉了自己的技"）。</summary>
    RejectedAlreadyKnown = 5,

    /// <summary>拒绝：等级段/形态不允许学这一类（天龙的群攻书需对应形态）。</summary>
    RejectedLevelGate = 6,

    /// <summary>拒绝：参数非法（槽表为空、技能 id 非法）。</summary>
    RejectedInvalid = 7,
}

/// <summary>一个技能槽的完整状态。</summary>
public struct PetSkillSlot
{
    /// <summary>技能 id；<b>0 是"空槽"</b>，与 <see cref="PetSkillSlotKind.Empty"/> 配对使用，二者必须一致。</summary>
    public int SkillId;

    /// <summary>形态。</summary>
    public PetSkillSlotKind Kind;

    /// <summary>技能等级 1–10（影响天龙"按等级概率释放"与伤害系数）。</summary>
    public int Level;

    /// <summary>是否锁定（锁定的槽打书不可覆盖）。与 <see cref="Kind"/> 的 Locked 位表达同一件事时以本字段为准。</summary>
    public bool Locked;
}

/// <summary>打书/领悟的输入配置。</summary>
public readonly struct PetSkillLearnConfig
{
    /// <summary>当前槽表（长度 = 当前已开放的技能格数）。</summary>
    public readonly PetSkillSlot[] Slots;

    /// <summary>槽位上限（由 <see cref="PetSkillCore.MaxSlotsFor"/> 得出，可能大于已开放格数）。</summary>
    public readonly int MaxSlots;

    /// <summary>宠物参战等级。</summary>
    public readonly int PetLevel;

    /// <summary>悟性等级 0–10（天龙开格判据）。</summary>
    public readonly int ComprehensionLevel;

    public PetSkillLearnConfig(PetSkillSlot[] slots, int maxSlots, int petLevel, int comprehensionLevel)
    {
        Slots = slots ?? Array.Empty<PetSkillSlot>();
        MaxSlots = maxSlots;
        PetLevel = petLevel;
        ComprehensionLevel = comprehensionLevel;
    }
}

/// <summary>打书结果。</summary>
public readonly struct PetSkillLearnResult
{
    /// <summary>原因码。</summary>
    public readonly PetSkillLearnOutcome Outcome;

    /// <summary>被写入的槽下标；拒绝时为 −1。</summary>
    public readonly int SlotIndex;

    /// <summary>被顶掉的技能 id；未顶掉为 0。</summary>
    public readonly int ReplacedSkillId;

    /// <summary>本次实际使用的概率掷值（0–9999），留在结果里是为了让"为什么这只被顶了"可复现。</summary>
    public readonly int Roll;

    public PetSkillLearnResult(PetSkillLearnOutcome outcome, int slotIndex, int replacedSkillId, int roll)
    {
        Outcome = outcome;
        SlotIndex = slotIndex;
        ReplacedSkillId = replacedSkillId;
        Roll = roll;
    }
}

/// <summary>
/// 技能槽位规则配置（每张表都对应一个可被编辑器逐格填的面板）。
/// <para>四款游戏的开格判据完全不同（梦幻按物种上限 + 顶书条件、大话按召唤兽品级、天龙按悟性、
/// WoW BP 是"池 6 槽 3"），所以这里<b>不把任何一种写成硬编码</b>，而是给出一组默认值 +
/// 三态覆盖：null = 用默认，非 null = 用模板值。照抄 <c>MonsterAiFlags.Resolve</c> 的做法。</para>
/// </summary>
public readonly struct PetSlotRuleConfig
{
    /// <summary>基础技能格数。</summary>
    public readonly int BaseSlots;

    /// <summary>绝对上限（再怎么开格也不超过它）。</summary>
    public readonly int AbsoluteMaxSlots;

    /// <summary>按品级追加的格数表（大话口径：普通/高级/特殊/神兽/珍惜）。</summary>
    public readonly int[] SlotsByGrade;

    /// <summary>悟性达到该等级时 +1 格（天龙 §D4：4 级与 7 级各开 1 格）。</summary>
    public readonly int ComprehensionUnlockA;
    public readonly int ComprehensionUnlockB;

    /// <summary>顶书开格的等级门（梦幻 §B4：参战等级 ≤125）。</summary>
    public readonly int SlotGrowMaxBattleLevel;

    /// <summary>顶书开格的已有技能数门（梦幻 §B4：已有技能 ≤3）。</summary>
    public readonly int SlotGrowMaxExistingSkills;

    /// <summary>顶书开格概率（bps）。资料只说"有一定概率"，取 15% 作可调初值。</summary>
    public readonly int SlotGrowChanceBps;

    /// <summary>满槽时打书"顶替已有技能"的概率（bps）。大话 §C4 明说"有空格也可能顶替"。</summary>
    public readonly int OverwriteChanceBps;

    /// <summary>允许锁定的格数（大话 §C4：解锁 8 格可锁 2 技）。</summary>
    public readonly int MaxLockableSlots;

    /// <summary>技能池（learned）上限——WoW BP "最多学 6 个"（§A4）。</summary>
    public readonly int LearnedPoolMax;

    /// <summary>可同时装配数——WoW BP "同时装配 3 个"（§A4）。</summary>
    public readonly int EquippedMax;

    /// <summary>默认规则：梦幻普通种形状（4 格起步、125 级前可顶书开格、上限 8）。</summary>
    public static PetSlotRuleConfig Default => new(
        baseSlots: 4, absoluteMaxSlots: 8, slotsByGrade: null,
        comprehensionUnlockA: 4, comprehensionUnlockB: 7,
        slotGrowMaxBattleLevel: 125, slotGrowMaxExistingSkills: 3,
        slotGrowChanceBps: 1_500, overwriteChanceBps: 7_000,
        maxLockableSlots: 2, learnedPoolMax: 6, equippedMax: 3);

    public PetSlotRuleConfig(int baseSlots, int absoluteMaxSlots, int[] slotsByGrade,
        int comprehensionUnlockA, int comprehensionUnlockB, int slotGrowMaxBattleLevel,
        int slotGrowMaxExistingSkills, int slotGrowChanceBps, int overwriteChanceBps,
        int maxLockableSlots, int learnedPoolMax, int equippedMax)
    {
        BaseSlots = baseSlots < 1 ? 1 : baseSlots;
        AbsoluteMaxSlots = absoluteMaxSlots < BaseSlots ? BaseSlots : absoluteMaxSlots;
        SlotsByGrade = slotsByGrade;
        ComprehensionUnlockA = comprehensionUnlockA;
        ComprehensionUnlockB = comprehensionUnlockB;
        SlotGrowMaxBattleLevel = slotGrowMaxBattleLevel;
        SlotGrowMaxExistingSkills = slotGrowMaxExistingSkills;
        SlotGrowChanceBps = slotGrowChanceBps;
        OverwriteChanceBps = overwriteChanceBps;
        MaxLockableSlots = maxLockableSlots < 0 ? 0 : maxLockableSlots;
        LearnedPoolMax = learnedPoolMax < 1 ? 1 : learnedPoolMax;
        EquippedMax = equippedMax < 1 ? 1 : equippedMax;
    }
}

/// <summary>
/// 技能学习与打书的确定性纯函数（逆向规格 §A4 / §B4 / §C4 / §D4）。
/// <para>
/// <b>两级分离（决策 D4）</b>：技能池 learned 是"这只会哪些"，装配槽 slots 是"格子里现在是哪些"。
/// 四款游戏里只有 WoW BP 有这种分离（学 6 装 3），国产三家是"槽里有什么就是什么"。
/// 保留分离不是为了好看：梦幻的"遗忘天生技后恢复回第 1 格、把已学技顶掉"（§C4）
/// 只有在"池 ⊇ 槽"的模型里才是可表达的。
/// </para>
/// <para>
/// <b>所有随机走确定性哈希（seed + salt），绝不用 <see cref="Random"/>/GUID</b>：
/// "同 seed 同结果"是打书能进金轨迹、能被单测、能被编辑器预览复现的前提。
/// </para>
/// </summary>
public static class PetSkillCore
{
    /// <summary>技能内核版本。</summary>
    public const int Version = 1;

    /// <summary>技能等级上限（天龙"技能等级影响释放概率与系数"，§D4）。</summary>
    public const int MaxSkillLevel = 10;

    /// <summary>单次战斗最多装配的主动技数（WoW BP 口径，§A4）。</summary>
    public const int MaxEquippedActive = 3;

    /// <summary>
    /// 当前开放的技能格数 = min(绝对上限, 基础 + 品级追加 + 悟性追加)。
    /// <para>三家的判据在这里合成一条公式，但每一项都可由模板置 0 来关掉：
    /// 想要纯梦幻形状就把 <c>SlotsByGrade</c> 置空且悟性门槛设到 11（永不满足）。</para>
    /// </summary>
    public static int MaxSlotsFor(in PetSlotRuleConfig rule, int grade, int comprehensionLevel)
    {
        var slots = rule.BaseSlots;

        if (rule.SlotsByGrade is { Length: > 0 })
        {
            var idx = grade < 0 ? 0 : (grade >= rule.SlotsByGrade.Length ? rule.SlotsByGrade.Length - 1 : grade);
            // 品级表给的是"该品级的总格数"，不是增量（大话：普通 4 / 高级 6 / 特殊 8 / 神兽 11 / 珍惜 13）。
            slots = rule.SlotsByGrade[idx];
        }

        if (rule.ComprehensionUnlockA > 0 && comprehensionLevel >= rule.ComprehensionUnlockA) slots++;
        if (rule.ComprehensionUnlockB > 0 && comprehensionLevel >= rule.ComprehensionUnlockB) slots++;

        if (slots < 1) slots = 1;
        if (slots > rule.AbsoluteMaxSlots) slots = rule.AbsoluteMaxSlots;
        return slots;
    }

    /// <summary>已占用（非空）槽数。</summary>
    public static int CountOccupied(PetSkillSlot[] slots)
    {
        if (slots is null) return 0;
        var n = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SkillId != 0) n++;
        }
        return n;
    }

    /// <summary>
    /// 打书（用技能书写入槽位）。<b>不修改入参数组</b>：返回新数组与结果，调用方决定要不要落盘。
    /// </summary>
    /// <param name="rule">槽位规则。</param>
    /// <param name="config">当前槽表与等级/悟性。</param>
    /// <param name="skillId">要学的技能 id（引用五行技能树同一 id 域，id = 800000 + 技能id）。</param>
    /// <param name="skillLevel">新书的技能等级。</param>
    /// <param name="conflictsWith">该技能的互斥技 id 列表；命中则旧技直接消失（梦幻 §B4 冲突）。</param>
    /// <param name="seed">确定性随机种子。</param>
    /// <param name="salt">同一 seed 下区分不同次掷值的盐。</param>
    /// <remarks>
    /// 流程按四款游戏的实际优先级排：
    /// ① 已在本宠槽中 → 拒绝（不做"消耗一本书结果什么都没变"的事）；
    /// ② 有空槽 → 直接入槽（这是玩家的期望默认）；
    /// ③ 无空槽但已开放格数 &lt; 上限且满足顶书门 → 按概率**开一格**（梦幻"顶书"）；
    /// ④ 否则按概率覆盖一个可覆盖槽（大话"大概率顶替"）；
    /// ⑤ 全部槽天生/锁定 → 拒绝。
    /// 每一步都把掷值带回结果，为了"这次为什么顶了我的连击"可复现。
    /// </remarks>
    public static (PetSkillSlot[] Slots, PetSkillLearnResult Result) LearnSkill(in PetSlotRuleConfig rule,
        in PetSkillLearnConfig config, int skillId, int skillLevel, int[] conflictsWith, long seed, int salt = 0)
    {
        var slots = config.Slots is null ? Array.Empty<PetSkillSlot>() : (PetSkillSlot[])config.Slots.Clone();
        var roll = Roll(seed, salt);

        if (skillId <= 0 || slots.Length == 0)
            return (slots, new PetSkillLearnResult(PetSkillLearnOutcome.RejectedInvalid, -1, 0, roll));

        // ① 已在槽中
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SkillId == skillId)
                return (slots, new PetSkillLearnResult(PetSkillLearnOutcome.RejectedAlreadyKnown, -1, 0, roll));
        }

        // ② 空槽优先
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SkillId != 0) continue;
            slots[i] = new PetSkillSlot
            {
                SkillId = skillId,
                Kind = PetSkillSlotKind.Learned,
                Level = ClampLevel(skillLevel),
                Locked = false,
            };
            return (slots, new PetSkillLearnResult(PetSkillLearnOutcome.Added, i, 0, roll));
        }

        var occupied = CountOccupied(slots);
        var openCap = MaxSlotsFor(rule, 0, config.ComprehensionLevel);

        // ③ 顶书开格：格数未达上限，且满足梦幻的两道门（等级 ≤125、已有 ≤3）
        if (slots.Length < openCap
            && config.PetLevel <= rule.SlotGrowMaxBattleLevel
            && occupied <= rule.SlotGrowMaxExistingSkills
            && roll < rule.SlotGrowChanceBps)
        {
            var grown = new PetSkillSlot[slots.Length + 1];
            Array.Copy(slots, grown, slots.Length);
            grown[slots.Length] = new PetSkillSlot
            {
                SkillId = skillId,
                Kind = PetSkillSlotKind.Learned,
                Level = ClampLevel(skillLevel),
                Locked = false,
            };
            return (grown, new PetSkillLearnResult(PetSkillLearnOutcome.SlotGrown, slots.Length, 0, roll));
        }

        // ④ 覆盖：先挑冲突项（互斥的旧技优先被清掉），再挑可覆盖槽
        var conflictIndex = -1;
        if (conflictsWith is { Length: > 0 })
        {
            for (var i = 0; i < slots.Length; i++)
            {
                for (var c = 0; c < conflictsWith.Length; c++)
                {
                    if (slots[i].SkillId == conflictsWith[c] && IsOverwritable(slots[i])) { conflictIndex = i; break; }
                }
                if (conflictIndex >= 0) break;
            }
        }

        if (roll < rule.OverwriteChanceBps || conflictIndex >= 0)
        {
            var target = conflictIndex;
            if (target < 0)
            {
                // 可覆盖槽里选哪一个也由掷值决定，而不是"第一个"：写死第一个会让每次打书
                // 都顶掉同一个槽，玩家一眼就能看出规律，而四款游戏都不是这样。
                target = PickOverwritableIndex(slots, seed, salt + 1);
                if (target < 0)
                    return (slots, new PetSkillLearnResult(PetSkillLearnOutcome.RejectedAllProtected, -1, 0, roll));
            }

            var replaced = slots[target].SkillId;
            slots[target] = new PetSkillSlot
            {
                SkillId = skillId,
                Kind = PetSkillSlotKind.Learned,
                Level = ClampLevel(skillLevel),
                Locked = false,
            };
            var outcome = conflictIndex >= 0 ? PetSkillLearnOutcome.ReplacedByConflict : PetSkillLearnOutcome.Replaced;
            return (slots, new PetSkillLearnResult(outcome, target, replaced, roll));
        }

        // ⑤ 概率未命中且无空槽：书白吃了但什么都没发生——这条出口必须存在，
        // 它是梦幻/大话"打书失败技能不变"的真实表现，界面据此提示"失败，技能未变动"。
        return (slots, new PetSkillLearnResult(PetSkillLearnOutcome.RejectedAllProtected, -1, 0, roll));
    }

    /// <summary>
    /// 领悟掷骰（大话 §C4：携带参战、有空技能栏则每场后概率获得一个随机技能，满栏不领悟；不满意可与 NPC 对话取消）。
    /// </summary>
    /// <param name="slots">当前槽表。</param>
    /// <param name="candidateSkillIds">本次可领悟的技能池。</param>
    /// <param name="chanceBps">领悟率（bps）。</param>
    /// <param name="seed">种子。</param>
    /// <param name="pickedSkillId">命中的候选技能 id；未命中为 0。</param>
    /// <returns>true = 发生领悟。满栏一律 false（不领悟也不覆盖——这是"领悟"和"打书"的关键差别）。</returns>
    public static bool TryEpiphany(PetSkillSlot[] slots, int[] candidateSkillIds, int chanceBps, long seed,
        out int pickedSkillId)
    {
        pickedSkillId = 0;
        if (slots is null || slots.Length == 0) return false;
        if (candidateSkillIds is null || candidateSkillIds.Length == 0) return false;

        // 满栏不领悟（大话 §C4 明文）。
        var firstEmpty = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SkillId == 0) { firstEmpty = i; break; }
        }
        if (firstEmpty < 0) return false;

        var roll = Roll(seed, 7_001);
        if (roll >= chanceBps) return false;

        // 候选里跳过已在槽中的，避免"领悟到一个自己已经会的技能"这种空转结果。
        var pickIdx = Roll(seed, 7_002) % candidateSkillIds.Length;
        for (var step = 0; step < candidateSkillIds.Length; step++)
        {
            var idx = (pickIdx + step) % candidateSkillIds.Length;
            var candidate = candidateSkillIds[idx];
            if (candidate <= 0) continue;

            var known = false;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].SkillId == candidate) { known = true; break; }
            }
            if (known) continue;

            pickedSkillId = candidate;
            slots[firstEmpty] = new PetSkillSlot
            {
                SkillId = candidate,
                Kind = PetSkillSlotKind.Epiphany,
                Level = 1,
                Locked = false,
            };
            return true;
        }
        return false;
    }

    /// <summary>
    /// 天生技遗忘/恢复（大话 §C4：恢复后回到第 1 格、把已学技顶掉）。
    /// <para>这条规则反直觉到必须原样实现：玩家以为"恢复"是加回来，实际是把第 1 格占了。</para>
    /// </summary>
    /// <param name="slots">原地修改（唯一一处允许就地改口的函数，因为它表达的是"位置"语义而非"值替换"）。</param>
    /// <param name="innateSkillId">要恢复的天生技。</param>
    /// <returns>被顶掉的技能 id；0 = 没人被顶。</returns>
    public static int RestoreInnateSkill(PetSkillSlot[] slots, int innateSkillId)
    {
        if (slots is null || slots.Length == 0 || innateSkillId <= 0) return 0;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i].SkillId == innateSkillId) return 0; // 已在，不重复占位
        }

        var displaced = slots[0].SkillId;
        slots[0] = new PetSkillSlot
        {
            SkillId = innateSkillId,
            Kind = PetSkillSlotKind.Innate,
            Level = 1,
            Locked = false,
        };
        return displaced;
    }

    /// <summary>设置/取消锁定；返回实际生效与否与原因（超出可锁格数要大声拒绝，不是静默忽略）。</summary>
    public static bool TrySetLock(PetSkillSlot[] slots, int slotIndex, bool locked, in PetSlotRuleConfig rule,
        out string reason)
    {
        reason = string.Empty;
        if (slots is null || slotIndex < 0 || slotIndex >= slots.Length)
        {
            reason = "槽位下标非法";
            return false;
        }
        if (slots[slotIndex].SkillId == 0)
        {
            reason = "空槽不能锁定";
            return false;
        }
        if (slots[slotIndex].Kind == PetSkillSlotKind.Innate)
        {
            // 大话 §C4：天生技与神兽技栏不可锁定（因为它本来就不可被顶，锁是多余的承诺）。
            reason = "天生技不可锁定";
            return false;
        }

        if (locked)
        {
            var already = 0;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Locked) already++;
            }
            if (already >= rule.MaxLockableSlots)
            {
                reason = $"最多可锁定 {rule.MaxLockableSlots} 个技能";
                return false;
            }
        }

        var slot = slots[slotIndex];
        slot.Locked = locked;
        slot.Kind = locked && slot.Kind == PetSkillSlotKind.Learned ? PetSkillSlotKind.Locked : slot.Kind;
        slots[slotIndex] = slot;
        return true;
    }

    /// <summary>内丹格数表（梦幻 §B4 [高]：按参战等级给格数，格子数不是玩家选的）。</summary>
    /// <param name="battleLevel">参战等级。</param>
    /// <returns>1–6 格。</returns>
    public static int InnerCoreSlotsFor(int battleLevel)
    {
        if (battleLevel <= 0) return 1;
        if (battleLevel < 35) return 1;
        if (battleLevel < 75) return 2;
        if (battleLevel < 105) return 3;
        if (battleLevel < 145) return 4;
        if (battleLevel < 175) return 5;
        return 6;
    }

    /// <summary>内丹层数上限（梦幻 §B4：初始 1 层、最高 5 层，靠领悟升层）。</summary>
    public const int InnerCoreMaxLayer = 5;

    /// <summary>
    /// 按技能等级决定自动释放概率（天龙 §D5 [高]：战斗中按技能等级概率自动释放）。
    /// <para>线性到 10 级封顶 60%——不给 100%，因为"必发"会让自动战斗退化成一个固定循环，
    /// 而天龙玩家观察到的是"高等级兽决更常放，但仍会放不出来"。</para>
    /// </summary>
    public static int AutoCastChanceBps(int skillLevel)
    {
        var lv = ClampLevel(skillLevel);
        return 1_500 + lv * 450; // 1 级 1950，10 级 6000
    }

    /// <summary>槽是否可被覆盖（天生与锁定不可）。</summary>
    private static bool IsOverwritable(in PetSkillSlot slot)
        => slot.SkillId != 0 && !slot.Locked && slot.Kind != PetSkillSlotKind.Innate;

    /// <summary>用掷值在可覆盖槽里选一个；返回 −1 表示无处可顶。</summary>
    private static int PickOverwritableIndex(PetSkillSlot[] slots, long seed, int salt)
    {
        var candidates = new int[slots.Length];
        var n = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            if (IsOverwritable(slots[i])) candidates[n++] = i;
        }
        if (n == 0) return -1;
        return candidates[Roll(seed, salt) % n];
    }

    private static int ClampLevel(int level)
    {
        if (level < 1) return 1;
        if (level > MaxSkillLevel) return MaxSkillLevel;
        return level;
    }

    /// <summary>
    /// 确定性掷值 0–9999（splitmix64 的截断版）。
    /// <para>自己写而不是 <c>Random(seed)</c>：<c>Random</c> 的实现细节属于运行时版本，
    /// 换 SDK 会改变序列，而打书结果必须跨版本逐位一致。</para>
    /// </summary>
    public static int Roll(long seed, int salt)
    {
        unchecked
        {
            var z = (ulong)seed + 0x9E3779B97F4A7C15UL + (ulong)salt * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z % 10_000UL);
        }
    }
}
