using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;

namespace Horizon.Orleans.Grains;

/// <summary>
/// 属性表行 → 五行来源贡献的服务端唯一映射。
/// <para>
/// CombatGrain（攻击侧）与 CharacterGrain（受击裁决侧）共用本映射，保证两端读到的是同一份
/// <c>Game_HunduShijie_CharacterAttribute</c> 行口径；聚合规则一律委托
/// <see cref="CombatProfile"/>，不在这里做任何求和或取高。
/// </para>
/// </summary>
public static class CombatAttributeProfiles
{
    /// <summary>
    /// 把一行属性表折算成"角色基础属性"来源的五行三行。
    /// 行号约定：1..5 = 金木水火土；<c>*_attack</c>=攻击力、<c>*_resistance</c>=防御力、
    /// <c>*_penetration</c>=破防值。null 行（表未迁移/无记录）退化为全零，不改变既有结算。
    /// </summary>
    public static CombatContribution FromAttributeRow(CharacterAttributeEntity? row)
    {
        if (row == null)
            return CombatContribution.FromVectors(CombatSourceType.BaseAttribute,
                ElementStatVector.Zero, ElementStatVector.Zero, ElementStatVector.Zero);

        return CombatContribution.FromVectors(CombatSourceType.BaseAttribute,
            new ElementStatVector(row.MetalAttack, row.WoodAttack, row.WaterAttack, row.FireAttack, row.EarthAttack),
            new ElementStatVector(row.MetalResistance, row.WoodResistance, row.WaterResistance,
                row.FireResistance, row.EarthResistance),
            new ElementStatVector(row.MetalPenetration, row.WoodPenetration, row.WaterPenetration,
                row.FirePenetration, row.EarthPenetration));
    }

    /// <summary>
    /// 合成一份权威画像 = 基础属性行（求和/取高后）+ 加点派生三行 + 通用攻防基值。
    /// </summary>
    /// <param name="allocation">服务端权威加点分配</param>
    /// <param name="row">属性表行（可空）</param>
    /// <param name="baseDefense">通用防御基值（非五行层）</param>
    /// <param name="baseAttack">通用攻击基值（非五行层）</param>
    /// <param name="element">自身命行（0 = 无属性）</param>
    /// <param name="extraAffinity">加点之外的五行属性值来源（穿戴装备/宝石的亲和），一起进兜底地板</param>
    /// <param name="extraSources">装备 / 技能 / Buff 等后续来源，逐个追加后统一按口径聚合</param>
    public static CombatProfile Build(
        AttributeAllocationData? allocation,
        CharacterAttributeEntity? row,
        float baseDefense = 0f,
        float baseAttack = 0f,
        int element = 0,
        ElementStatVector extraAffinity = default,
        params CombatContribution[] extraSources)
    {
        var sources = new CombatContribution[(extraSources?.Length ?? 0) + 1];
        sources[0] = FromAttributeRow(row);
        if (extraSources != null)
        {
            for (int i = 0; i < extraSources.Length; i++) sources[i + 1] = extraSources[i];
        }

        return CombatProfile.FromAllocation(allocation, baseDefense, baseAttack, element, extraAffinity, sources);
    }
}
