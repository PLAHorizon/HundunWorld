using System;
using MemoryPack;
using Orleans;

namespace Horizon.Game.Message.Combat;

/// <summary>
/// 五行维度向量：金 / 木 / 水 / 火 / 土 各一个数值。
/// <para>
/// 下标语义与全项目的元素编号一致：<c>0=无属性</c>、<c>1=金</c>、<c>2=木</c>、<c>3=水</c>、
/// <c>4=火</c>、<c>5=土</c>（见客户端 <c>Game.Character.Attributes.WuxingElement</c> 与
/// <c>CombatCalculator.GetWuxingMultiplier</c>）。<see cref="At"/> 对 0 与越界一律返回 0，
/// 所以"无属性攻击"天然不取任何一行。
/// </para>
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public partial struct ElementStatVector
{
    /// <summary>金（下标 1）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public float Metal;

    /// <summary>木（下标 2）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public float Wood;

    /// <summary>水（下标 3）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public float Water;

    /// <summary>火（下标 4）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public float Fire;

    /// <summary>土（下标 5）。</summary>
    [MemoryPackOrder(4)]
    [Id(4)]
    public float Earth;

    public static readonly ElementStatVector Zero = default;

    public ElementStatVector(float metal, float wood, float water, float fire, float earth)
    {
        Metal = metal; Wood = wood; Water = water; Fire = fire; Earth = earth;
    }

    /// <summary>按元素编号取该行；0（无属性）与越界返回 0。</summary>
    public readonly float At(int element) => element switch
    {
        1 => Metal,
        2 => Wood,
        3 => Water,
        4 => Fire,
        5 => Earth,
        _ => 0f,
    };

    /// <summary>五行合计（仅用于展示口径，不参与结算）。</summary>
    [MemoryPackIgnore]
    public readonly float Total => Metal + Wood + Water + Fire + Earth;

    /// <summary>五行中的最高值（"已高者计"在单来源向量上的取值）。</summary>
    [MemoryPackIgnore]
    public readonly float Max
        => Math.Max(Math.Max(Math.Max(Metal, Wood), Math.Max(Water, Fire)), Earth);

    /// <summary>逐行相加（攻击侧的聚合口径）。</summary>
    public static ElementStatVector operator +(ElementStatVector a, ElementStatVector b)
        => new(a.Metal + b.Metal, a.Wood + b.Wood, a.Water + b.Water, a.Fire + b.Fire, a.Earth + b.Earth);

    /// <summary>各行同乘一个系数（每点换算用）。</summary>
    public static ElementStatVector operator *(ElementStatVector v, float scale)
        => new(v.Metal * scale, v.Wood * scale, v.Water * scale, v.Fire * scale, v.Earth * scale);

    /// <summary>
    /// 在某一五行行上<b>累加</b>一个值（技能把自己的五行攻击力临时并进攻方该行）。
    /// 元素为 0 或越界时原样返回——无属性技能没有可加的行。
    /// </summary>
    public readonly ElementStatVector WithRowAdded(int element, float value) => element switch
    {
        1 => new ElementStatVector(Metal + value, Wood, Water, Fire, Earth),
        2 => new ElementStatVector(Metal, Wood + value, Water, Fire, Earth),
        3 => new ElementStatVector(Metal, Wood, Water + value, Fire, Earth),
        4 => new ElementStatVector(Metal, Wood, Water, Fire + value, Earth),
        5 => new ElementStatVector(Metal, Wood, Water, Fire, Earth + value),
        _ => this,
    };

    /// <summary>
    /// 在某一五行行上取<b>更高者</b>（技能破防与角色常驻破防比较，符合"破防已高者计"）。
    /// 负值不会压低已有行：行值下限始终是已聚合出来的那份。
    /// </summary>
    public readonly ElementStatVector WithRowMax(int element, float value) => element switch
    {
        1 => new ElementStatVector(Math.Max(Metal, value), Wood, Water, Fire, Earth),
        2 => new ElementStatVector(Metal, Math.Max(Wood, value), Water, Fire, Earth),
        3 => new ElementStatVector(Metal, Wood, Math.Max(Water, value), Fire, Earth),
        4 => new ElementStatVector(Metal, Wood, Water, Math.Max(Fire, value), Earth),
        5 => new ElementStatVector(Metal, Wood, Water, Fire, Math.Max(Earth, value)),
        _ => this,
    };
}

/// <summary>五行三行的一份来源出处。落库列、日志、飘字都按它标注，聚合层据此决定求和还是取高。</summary>
public enum CombatSourceType
{
    /// <summary>角色基础属性（属性表的 *_attack / *_resistance / *_penetration 列）。</summary>
    BaseAttribute = 0,
    /// <summary>属性加点。</summary>
    Allocation = 1,
    /// <summary>穿戴装备（含镶嵌宝石）。</summary>
    Equipment = 2,
    /// <summary>已学技能提供的常驻五行加成。</summary>
    Skill = 3,
    /// <summary>战斗内生效的 Buff / 减益。</summary>
    Buff = 4,
}

/// <summary>
/// 单一来源对五行三行的贡献。
/// <para>
/// 这是可序列化的一种形状：CombatGrain 把它缓存在参与者状态里，每击再追加实时 Buff 来源后
/// 重新聚合，因此"求和/取高"的口径只有一处实现（<c>Horizon.Game.Core.Combat.CombatProfile</c>），
/// 不会出现缓存画像与实时增益两套算法各算各的。
/// </para>
/// </summary>
[MemoryPackable(SerializeLayout.Explicit)]
[GenerateSerializer]
[Serializable]
public partial struct CombatContribution
{
    /// <summary>来源类别（<see cref="CombatSourceType"/>）。</summary>
    [MemoryPackOrder(0)]
    [Id(0)]
    public int Source;

    /// <summary>五行攻击力贡献（求和侧）。</summary>
    [MemoryPackOrder(1)]
    [Id(1)]
    public ElementStatVector Attack;

    /// <summary>五行防御力贡献（取高侧）。</summary>
    [MemoryPackOrder(2)]
    [Id(2)]
    public ElementStatVector Defense;

    /// <summary>五行破防值贡献（取高侧）。</summary>
    [MemoryPackOrder(3)]
    [Id(3)]
    public ElementStatVector Penetration;

    public static CombatContribution FromVectors(
        CombatSourceType source, ElementStatVector attack, ElementStatVector defense, ElementStatVector penetration)
        => new CombatContribution
        {
            Source = (int)source,
            Attack = attack,
            Defense = defense,
            Penetration = penetration,
        };

    /// <summary>只加攻击行（技能瞬时伤害最常走的形状）。</summary>
    public static CombatContribution AttackOnly(CombatSourceType source, ElementStatVector attack)
        => FromVectors(source, attack, ElementStatVector.Zero, ElementStatVector.Zero);
}
