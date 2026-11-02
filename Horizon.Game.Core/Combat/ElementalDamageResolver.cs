using System;
using System.Collections.Generic;
using Horizon.Game.Message.Combat;

namespace Horizon.Game.Core.Combat;

/// <summary>
/// 五行伤害的唯一权威口径。
/// <para>
/// 三条聚合/结算规则（与策划口径一一对应）：
/// <list type="number">
///   <item><description><b>五行攻击力</b>：各来源（基础属性 / 加点 / 装备 / 技能 / Buff）<b>逐行求和</b>，
///   取攻方所用元素那一行作为伤害基数。</description></item>
///   <item><description><b>五行防御力</b>：各来源<b>取最高者</b>（不叠加），避免多来源把防御堆到无解。</description></item>
///   <item><description><b>五行破防值</b>：各来源<b>取最高者</b>，且<b>只抵消防御</b>——
///   破防不额外加伤；克制关系只决定"破防打到哪一行"。</description></item>
/// </list>
/// 减免沿用全项目已有的 <c>defense / (defense + 100)</c> 曲线，不另起一套。
/// </para>
/// </summary>
public static class ElementalDamageResolver
{
    /// <summary>防御曲线常数：100 点防御抵消 50%。</summary>
    public const float DefenseCurveConstant = 100f;

    /// <summary>相克伤害倍率（攻克守）。</summary>
    public const float AdvantageMultiplier = 1.25f;

    /// <summary>被克伤害倍率（攻被守克）。</summary>
    public const float DisadvantageMultiplier = 0.8f;

    // ── 聚合：攻方求和，防/破防取最高 ────────────────────────────────

    /// <summary>攻击侧聚合：各来源逐行相加。</summary>
    public static ElementStatVector SumSources(IEnumerable<ElementStatVector> sources)
    {
        var total = ElementStatVector.Zero;
        if (sources == null) return total;
        foreach (var s in sources) total += s;
        return total;
    }

    /// <summary>防御 / 破防侧聚合："已高者计"——逐行取各来源最大值。</summary>
    public static ElementStatVector MaxSources(IEnumerable<ElementStatVector> sources)
    {
        var best = ElementStatVector.Zero;
        if (sources == null) return best;
        foreach (var s in sources)
        {
            best.Metal = Math.Max(best.Metal, s.Metal);
            best.Wood = Math.Max(best.Wood, s.Wood);
            best.Water = Math.Max(best.Water, s.Water);
            best.Fire = Math.Max(best.Fire, s.Fire);
            best.Earth = Math.Max(best.Earth, s.Earth);
        }
        return best;
    }

    // ── 五行相克 ──────────────────────────────────────────────────

    /// <summary>
    /// 相克环：金克木、木克土、土克水、水克火、火克金。
    /// 与 <c>CombatCalculator.GetWuxingMultiplier</c> 的"相克"分支同一套关系，
    /// 那里"被克"分支含一处历史遗留的非标准判定，本类不复制该偏差。
    /// </summary>
    public static int Overcomes(int element) => element switch
    {
        1 => 2,   // 金克木
        2 => 5,   // 木克土
        5 => 3,   // 土克水
        3 => 4,   // 水克火
        4 => 1,   // 火克金
        _ => 0,
    };

    /// <summary>克制 <paramref name="element"/> 的那一行（谁克它）。无属性/未知返回 0。</summary>
    public static int OvercomeBy(int element) => element switch
    {
        1 => 4,   // 金被火克
        2 => 1,   // 木被金克
        3 => 5,   // 水被土克
        4 => 3,   // 火被水克
        5 => 2,   // 土被木克
        _ => 0,
    };

    /// <summary>
    /// 伤害倍率：攻方克守方 ×<see cref="AdvantageMultiplier"/>，守方克攻方 ×<see cref="DisadvantageMultiplier"/>，
    /// 其余（含任一方无属性）×1。
    /// </summary>
    public static float DamageMultiplier(int attackerElement, int defenderElement)
    {
        if (attackerElement == 0 || defenderElement == 0) return 1.0f;
        if (Overcomes(attackerElement) == defenderElement) return AdvantageMultiplier;
        if (Overcomes(defenderElement) == attackerElement) return DisadvantageMultiplier;
        return 1.0f;
    }

    // ── 结算 ──────────────────────────────────────────────────────

    /// <summary>一次五行结算的完整分解（用于日志、飘字与客户端权威回显）。</summary>
    public readonly struct ElementalDamage
    {
        /// <summary>攻方所用元素行号。</summary>
        public int AttackElement { get; init; }
        /// <summary>攻方该五行攻击（各来源求和后的该行）。</summary>
        public float Attack { get; init; }
        /// <summary>克制倍率。</summary>
        public float Multiplier { get; init; }
        /// <summary>克制前伤害。</summary>
        public float RawDamage { get; init; }
        /// <summary>守方有效防御行（该行各来源取最高）。</summary>
        public float DefenseRow { get; init; }
        /// <summary>攻方对该防御行的破防（各来源取最高）。</summary>
        public float Penetration { get; init; }
        /// <summary>抵免后的有效防御（不为负）。</summary>
        public float EffectiveDefense { get; init; }
        /// <summary>防御减免比例 0~1。</summary>
        public float DefenseReduction { get; init; }
        /// <summary>最终伤害（不为负）。</summary>
        public float FinalDamage { get; init; }
    }

    /// <summary>攻击侧结算结果（克制前/后的伤害基数，不含任何防御减免）。</summary>
    public readonly struct ElementalAttack
    {
        /// <summary>实际使用的攻击行（1~5；0 = 无属性）。</summary>
        public int AttackElement { get; init; }
        /// <summary>命中行的攻击力。</summary>
        public float Attack { get; init; }
        /// <summary>克制倍率。</summary>
        public float Multiplier { get; init; }
        /// <summary>克制后、防御减免前的伤害。</summary>
        public float RawDamage { get; init; }
    }

    /// <summary>
    /// 只做攻击侧结算——给"减免由下游裁决层负责"的调用点用（即 <c>CombatGrain.CalculateDamage</c>）。
    /// <para>
    ///  CombatGrain 算命中/暴击/格挡/克制，CharacterGrain 裁决防御与减伤，两层各持一半，
    ///  沿用既有"避免双重减免"的分层不变量。
    /// </para>
    /// </summary>
    /// <param name="attack">攻方五行攻击（各来源求和后的最终向量）</param>
    /// <param name="attackElement">攻方所用元素（0 = 无属性，退化为攻方五行最高行）</param>
    /// <param name="defenderElement">守方命行（只决定克制倍率）</param>
    public static ElementalAttack ResolveAttackSide(in ElementStatVector attack, int attackElement, int defenderElement)
    {
        // 无属性攻击没有"该行"，退化为攻方五行最高值，保证无属性技能仍然有伤害来源。
        float attackValue = attackElement == 0 ? attack.Max : attack.At(attackElement);
        float multiplier = DamageMultiplier(attackElement, defenderElement);
        return new ElementalAttack
        {
            AttackElement = attackElement,
            Attack = attackValue,
            Multiplier = multiplier,
            RawDamage = attackValue * multiplier,
        };
    }

    /// <summary>
    /// 完整结算：攻方攻击向量（已求和）× 克制倍率，再按守方防御向量（已取最高）与
    /// 攻方破防向量（已取最高）减免。
    /// </summary>
    /// <param name="attack">攻方五行攻击（各来源求和后的最终向量）</param>
    /// <param name="penetration">攻方五行破防（各来源取最高后的最终向量）</param>
    /// <param name="defense">守方五行防御（各来源取最高后的最终向量）</param>
    /// <param name="attackElement">攻方所用元素（1~5；0 = 无属性）</param>
    /// <param name="defenderElement">守方元素（用于克制倍率；0 = 无属性）</param>
    /// <param name="baseDefense">守方通用防御（非五行层），与命中行防御相加后一起被抵免</param>
    public static ElementalDamage Resolve(in ElementStatVector attack, in ElementStatVector penetration,
        in ElementStatVector defense, int attackElement, int defenderElement, float baseDefense = 0f)
    {
        var atk = ResolveAttackSide(attack, attackElement, defenderElement);
        float raw = atk.RawDamage;

        // 破防只作用于"实际承担防御的那一行"：同行对抗，破防来自克制该行的那一系。
        int defenseElement = attackElement == 0 ? HighestDefenseElement(defense) : attackElement;
        float mitigated = Mitigate(raw, baseDefense, defense, penetration, defenseElement);

        return new ElementalDamage
        {
            AttackElement = attackElement,
            Attack = atk.Attack,
            Multiplier = atk.Multiplier,
            RawDamage = raw,
            DefenseRow = baseDefense + defense.At(defenseElement),
            Penetration = penetration.At(OvercomeBy(defenseElement)),
            EffectiveDefense = EffectiveDefenseValue(defense, penetration, defenseElement, baseDefense),
            DefenseReduction = DefenseReductionValue(defense, penetration, defenseElement, baseDefense),
            FinalDamage = mitigated,
        };
    }

    /// <summary>
    /// 只做防御侧减免——给"伤害已经由上游算好、本层只负责裁决"的调用点用
    /// （即 <c>CharacterGrain.RequestHpChangeAsync</c> 的位置）。
    /// </summary>
    /// <param name="incomingDamage">上游算出的伤害（正数表示伤害量）</param>
    /// <param name="defense">守方五行防御（各来源取最高）</param>
    /// <param name="attackerPenetration">攻方五行破防（各来源取最高）</param>
    /// <param name="attackElement">攻方所用元素（0 = 无属性，按守方最高防御行处理）</param>
    public static float Mitigate(float incomingDamage, in ElementStatVector defense,
        in ElementStatVector attackerPenetration, int attackElement)
        => Mitigate(incomingDamage, 0f, defense, attackerPenetration, attackElement);

    /// <inheritdoc cref="Mitigate(float,in ElementStatVector,in ElementStatVector,int)"/>
    /// <param name="baseDefense">守方通用防御（非五行层）</param>
    public static float Mitigate(float incomingDamage, float baseDefense, in ElementStatVector defense,
        in ElementStatVector attackerPenetration, int attackElement)
    {
        if (incomingDamage <= 0f) return 0f;

        int element = attackElement == 0 ? HighestDefenseElement(defense) : attackElement;
        float eff = EffectiveDefenseValue(defense, attackerPenetration, element, baseDefense);
        float reduction = eff / (eff + DefenseCurveConstant);
        float result = incomingDamage * (1f - reduction);
        return result < 0f ? 0f : result;
    }

    private static float EffectiveDefenseValue(in ElementStatVector defense, in ElementStatVector penetration,
        int element, float baseDefense = 0f)
    {
        float row = baseDefense + defense.At(element);
        // 克制该防御行的那一系才提供破防（金系破防打木行），与"克制只影响破防目标"一致。
        float pen = penetration.At(OvercomeBy(element));
        float eff = row - pen;
        return eff < 0f ? 0f : eff;
    }

    private static float DefenseReductionValue(in ElementStatVector defense, in ElementStatVector penetration,
        int element, float baseDefense = 0f)
    {
        float eff = EffectiveDefenseValue(defense, penetration, element, baseDefense);
        return eff / (eff + DefenseCurveConstant);
    }

    /// <summary>守方防御最高的一行（无属性攻击时的对抗行）。</summary>
    public static int HighestDefenseElement(in ElementStatVector defense)
    {
        int best = 0;
        float bestV = 0f;
        for (int e = 1; e <= 5; e++)
        {
            float v = defense.At(e);
            if (v > bestV) { bestV = v; best = e; }
        }
        return best;
    }
}
