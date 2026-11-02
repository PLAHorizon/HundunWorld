using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;

namespace Horizon.Game.Core.Combat;

/// <summary>
/// 一份战斗属性的权威聚合结果：把"角色基础属性 / 加点 / 装备 / 技能 / Buff"五类来源的
/// 五行三行按 <see cref="ElementalDamageResolver"/> 的口径合成一份可直接结算的画像。
/// <para>
/// 聚合口径（服务端唯一事实源，客户端只做预览镜像）：
/// <list type="number">
///   <item><description>五行攻击力：各来源<b>逐行求和</b>（<see cref="Attack"/>）。</description></item>
///   <item><description>五行防御力：各来源<b>取最高</b>（<see cref="Defense"/>）。</description></item>
///   <item><description>五行破防值：各来源<b>取最高</b>（<see cref="Penetration"/>）。</description></item>
///   <item><description>五行属性值（亲和）兜底：按每点换算取半额，再与上面聚出的三行<b>逐行取较高</b>。</description></item>
/// </list>
/// 通用防御（<c>CharacterInfo.Defense</c> / <c>CharacterEntity.Defense</c>）是独立于五行之外的
/// 一层，结算时与命中行防御<b>相加</b>后再被破防抵免——"已高者计"只作用于五行防御的各来源之间。
/// </para>
/// </summary>
public sealed class CombatProfile
{
    /// <summary>五行攻击（各来源求和）。</summary>
    public ElementStatVector Attack { get; init; }

    /// <summary>五行防御（各来源取最高，不含通用防御）。</summary>
    public ElementStatVector Defense { get; init; }

    /// <summary>五行破防（各来源取最高）。</summary>
    public ElementStatVector Penetration { get; init; }

    /// <summary>通用防御（非五行层），与命中行防御相加后一起参与破防抵免。</summary>
    public float BaseDefense { get; init; }

    /// <summary>通用攻击（非五行层），沿用 <c>CombatCalculator.CalculateBaseDamage</c> 的物理口径。</summary>
    public float BaseAttack { get; init; }

    /// <summary>
    /// 该角色的五行属性值（亲和）向量：加点×10 + 装备/宝石亲和。
    /// <para>
    /// 它本身不是一条来源，而是<b>兜底地板</b>的输入：三行按
    /// <see cref="AttributeDerivation.DeriveAffinityFallback"/> 半额换算后与聚合结果逐行取较高。
    /// 缓存的是它而不是兜底后的三行，因为 Buff 每击都要重新聚合，地板必须一起参与。
    /// </para>
    /// </summary>
    public ElementStatVector Affinity { get; init; }

    /// <summary>自身命行（1..5 = 金木水火土，0 = 无属性），用于克制倍率。</summary>
    public int Element { get; init; }

    /// <summary>加点派生的标量加成（生命/攻击/防御/内力/暴击…）。</summary>
    public AttributeDerivation.DerivedBonuses Bonuses { get; init; }

    /// <summary>
    /// 参与本次聚合的来源清单（可缓存进 Grain 状态）。
    /// Buff 会随战斗实时增减，所以缓存的是<b>来源</b>而不是聚合结果——
    /// 每击用 <see cref="Aggregate"/> 把缓存来源加上实时 Buff 来源重新聚合成三行。
    /// </summary>
    public List<CombatContribution> Sources { get; init; } = new();

    public static readonly CombatProfile Empty = new();

    /// <summary>
    /// 把若干来源按权威口径聚合成五行三行（攻=逐行求和，防与破防=逐行取最高）。
    /// 全项目只有这一处实现聚合规则；<see cref="Build"/> 与结算热路径都走它。
    /// <para>
    /// 这个重载<b>不含五行属性值兜底</b>，只给纯来源聚合（<see cref="CombatContribution"/> 清单本身
    /// 不携带亲和，聚合出什么就是什么）。带兜底的口径一律走
    /// <see cref="Aggregate(IEnumerable{CombatContribution}, ElementStatVector)"/>。
    /// </para>
    /// </summary>
    public static AttributeDerivation.ElementVectors Aggregate(IEnumerable<CombatContribution> sources)
    {
        var list = sources as IReadOnlyCollection<CombatContribution> ?? (sources?.ToList() ?? new List<CombatContribution>());

        return new AttributeDerivation.ElementVectors
        {
            Attack = ElementalDamageResolver.SumSources(list.Select(c => c.Attack)),
            Defense = ElementalDamageResolver.MaxSources(list.Select(c => c.Defense)),
            Penetration = ElementalDamageResolver.MaxSources(list.Select(c => c.Penetration)),
        };
    }

    /// <summary>
    /// 聚合来源后再并入五行属性值兜底：<b>逐行取较高</b>，兜底本身按
    /// <see cref="AttributeDerivation.AffinityFallbackRatio"/> 半额换算。
    /// <para>
    /// 取较高而不是相加是关键：加点已经全额进过三行，若把由同一批点数折算出的半额亲和再加一遍，
    /// 等于给加点角色凭空多付五成五行伤害。
    /// </para>
    /// </summary>
    public static AttributeDerivation.ElementVectors Aggregate(
        IEnumerable<CombatContribution> sources, ElementStatVector affinity)
        => AttributeDerivation.MaxVectors(Aggregate(sources), AttributeDerivation.DeriveAffinityFallback(affinity));

    /// <summary>
    /// 按口径合成画像。传 null 或空来源时得到全零画像（行为等同于"未分配、无装备"）。
    /// </summary>
    /// <param name="contributions">五类来源的贡献，顺序无关</param>
    /// <param name="baseDefense">通用防御基值（角色表 defense 列 + 加点防御派生）</param>
    /// <param name="baseAttack">通用攻击基值（角色表 attack_power 列 + 加点攻击派生）</param>
    /// <param name="element">自身命行</param>
    /// <param name="bonuses">加点标量派生</param>
    /// <param name="affinity">五行属性值（亲和）向量，用于三行兜底地板</param>
    public static CombatProfile Build(
        IEnumerable<CombatContribution> contributions,
        float baseDefense = 0f,
        float baseAttack = 0f,
        int element = 0,
        AttributeDerivation.DerivedBonuses bonuses = default,
        ElementStatVector affinity = default)
    {
        var materialized = new List<CombatContribution>();
        if (contributions != null) materialized.AddRange(contributions);
        var vectors = Aggregate(materialized, affinity);

        return new CombatProfile
        {
            Attack = vectors.Attack,
            Defense = vectors.Defense,
            Penetration = vectors.Penetration,
            BaseDefense = baseDefense,
            BaseAttack = baseAttack,
            Element = element,
            Bonuses = bonuses,
            Affinity = affinity,
            Sources = materialized,
        };
    }

    /// <summary>
    /// 由加点分配构造画像（服务端目前唯一有持久数据的来源），并追加其余来源。
    /// <para>
    /// 装备/技能/Buff 接入时把它们各自折算成 <see cref="CombatContribution"/> 追加进
    /// <paramref name="extraSources"/>，不要在调用方自行求和——那会绕过"已高者计"口径。
    /// </para>
    /// </summary>
    /// <param name="extraAffinity">不属于加点的五行属性值（装备/宝石亲和），与加点折算的亲和合并后一起兜底</param>
    public static CombatProfile FromAllocation(
        AttributeAllocationData? allocation,
        float baseDefense = 0f,
        float baseAttack = 0f,
        int element = 0,
        ElementStatVector extraAffinity = default,
        params CombatContribution[] extraSources)
    {
        var bonuses = AttributeDerivation.Derive(allocation);
        var vectors = AttributeDerivation.DeriveElementVectors(allocation);
        var contributions = new List<CombatContribution>(extraSources?.Length + 1 ?? 1)
        {
            CombatSourceType.Allocation.AsContribution(vectors),
        };
        if (extraSources != null) contributions.AddRange(extraSources);

        return Build(
            contributions,
            baseDefense + bonuses.DefenseBonus,
            baseAttack + bonuses.AttackBonus,
            element,
            bonuses,
            AttributeDerivation.AffinityFromAllocation(allocation) + extraAffinity);
    }
}

/// <summary>五行三行的来源类别与贡献结构本体在协议层（<see cref="CombatContribution"/>），
/// 这里只提供 <see cref="AttributeDerivation.ElementVectors"/> 的形状转换便利方法。</summary>
public static class CombatContributionExtensions
{
    public static CombatContribution AsContribution(
        this CombatSourceType source, AttributeDerivation.ElementVectors vectors)
        => CombatContribution.FromVectors(source, vectors.Attack, vectors.Defense, vectors.Penetration);
}
