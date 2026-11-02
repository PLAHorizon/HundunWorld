using System;
using System.Collections.Generic;
using System.Text.Json;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Enums;

namespace Horizon.Game.Core.Combat;

/// <summary>
/// 装备属性 JSON 契约的解析器：把 <c>ItemTemplateEntity.BaseAttributes</c> /
/// <c>RandomAttributes</c> 里的键值对折算成一份 <see cref="CombatContribution"/> 加一层通用攻防。
/// <para>
/// 键名直接复用 <see cref="AttributeType"/> 的成员名（大小写不敏感），因此不需要另立一套词表：
/// <c>"MetalAttack"</c>=金行攻击力、<c>"WoodDefense"</c>=木行防御力、<c>"EarthPenetration"</c>=土系破防，
/// 通用层用 <c>"Attack"</c> / <c>"Defense"</c> / <c>"HP"</c> / <c>"MP"</c>。
/// 未识别的键<b>忽略而不是乱摊</b>——把 <c>"CritRate"</c> 误算进攻击力比少算一条属性难查得多。
/// </para>
/// <para>
/// 之所以在代码里定义契约而不是猜库里的既有格式：全仓没有任何代码读写过这两列，
/// <c>ItemAttributeEntity</c> 也零读者，装备数值此前只存在于客户端硬编码表里。
/// </para>
/// </summary>
public static class ItemAttributeContract
{
    /// <summary>一件装备（模板或实例）折算出来的属性。</summary>
    public sealed class ParsedItemAttributes
    {
        /// <summary>五行三行贡献（Source = Equipment）。</summary>
        public CombatContribution Contribution;

        /// <summary>通用攻击（非五行层）。</summary>
        public float PhysicalAttack;

        /// <summary>通用防御（非五行层）。</summary>
        public float PhysicalDefense;

        /// <summary>生命上限加成。</summary>
        public float MaxHealth;

        /// <summary>内力上限加成。</summary>
        public float MaxEnergy;

        /// <summary>暴击率加成（比例制，0.001 = 0.1%）。</summary>
        public float CritRate;

        /// <summary>
        /// 五行属性值（亲和）：只喂兜底换算，不进三行本身。
        /// <para>
        /// 键为 <c>MetalAffinity</c> / <c>WoodAffinity</c> …（裸元素名 <c>Metal</c> 也认，
        /// 与客户端装备表 <c>WuxingBonus</c> 同源）。装备亲和此前对战斗零贡献——
        /// 它只出现在雷达图上，服务端根本读不到。
        /// </para>
        /// </summary>
        public ElementStatVector Affinity;

        /// <summary>已识别的键数；0 表示这件装备对战斗没有权威贡献。</summary>
        public int RecognizedKeyCount;
    }

    /// <summary>
    /// 解析一段属性 JSON。空串、坏 JSON、非对象都返回"无贡献"而不抛异常——
    /// 内容表里存在历史脏数据时，绝不能让一次穿戴把战斗结算拖垮。
    /// </summary>
    public static ParsedItemAttributes Parse(string json)
    {
        var result = new ParsedItemAttributes();
        if (string.IsNullOrWhiteSpace(json)) return result;

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

            var attack = ElementStatVector.Zero;
            var defense = ElementStatVector.Zero;
            var penetration = ElementStatVector.Zero;
            var affinity = ElementStatVector.Zero;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                float value;
                try
                {
                    value = property.Value.ValueKind == JsonValueKind.Number
                        ? property.Value.GetSingle()
                        : float.TryParse(property.Value.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0f;
                }
                catch (Exception)
                {
                    continue;   // 嵌套对象/数组等不属于平值属性，跳过
                }

                if (value == 0f) continue;
                if (!Enum.TryParse<AttributeType>(property.Name, ignoreCase: true, out var kind)
                    && !AttributeAliases.TryGetValue(property.Name.Trim().ToUpperInvariant(), out kind))
                {
                    continue;
                }

                if (TryMapElementRow(kind, value, ref attack, ref defense, ref penetration)
                    || TryMapAffinity(kind, value, ref affinity)
                    || TryMapScalar(kind, value, result))
                {
                    result.RecognizedKeyCount++;
                }
            }

            result.Contribution = CombatContribution.FromVectors(
                CombatSourceType.Equipment, attack, defense, penetration);
            result.Affinity = affinity;
            return result;
        }
        catch (JsonException)
        {
            return new ParsedItemAttributes();
        }
    }

    /// <summary>把多件装备的属性合并成一份来源贡献（攻击求和、防御与破防取最高、通用层相加）。</summary>
    public static ParsedItemAttributes Combine(IEnumerable<ParsedItemAttributes> parts)
    {
        var combined = new ParsedItemAttributes();
        if (parts == null) return combined;

        var attacks = new List<ElementStatVector>();
        var defenses = new List<ElementStatVector>();
        var penetrations = new List<ElementStatVector>();

        foreach (var part in parts)
        {
            if (part == null) continue;
            attacks.Add(part.Contribution.Attack);
            defenses.Add(part.Contribution.Defense);
            penetrations.Add(part.Contribution.Penetration);

            combined.PhysicalAttack += part.PhysicalAttack;
            combined.PhysicalDefense += part.PhysicalDefense;
            combined.MaxHealth += part.MaxHealth;
            combined.MaxEnergy += part.MaxEnergy;
            combined.CritRate += part.CritRate;
            // 亲和是"属性值"层，逐件相加后整体折算一次兜底；与三行的取高口径无关。
            combined.Affinity = combined.Affinity + part.Affinity;
            combined.RecognizedKeyCount += part.RecognizedKeyCount;
        }

        // 五行三行不能在这里相加：装备之间同样遵守"攻=求和、防与破防=已高者计"，
        // 否则两件高防装备会被算成一件超级防具。
        combined.Contribution = CombatContribution.FromVectors(
            CombatSourceType.Equipment,
            ElementalDamageResolver.SumSources(attacks),
            ElementalDamageResolver.MaxSources(defenses),
            ElementalDamageResolver.MaxSources(penetrations));
        return combined;
    }

    /// <summary>
    /// 短名别名：客户端装备表一直用 <c>"HP"</c> / <c>"MP"</c>，而它们不是
    /// <see cref="AttributeType"/> 的成员名（成员叫 MaxHealth / MaxMana）。
    /// 别名集中在这一处，散落各处的 <c>case "HP":</c> 迟早会和枚举对不上。
    /// </summary>
    private static readonly Dictionary<string, AttributeType> AttributeAliases =
        new Dictionary<string, AttributeType>(StringComparer.OrdinalIgnoreCase)
        {
            ["HP"] = AttributeType.MaxHealth,
            ["MP"] = AttributeType.MaxMana,
            ["CritRate"] = AttributeType.CriticalRate,
            ["CritDamage"] = AttributeType.CriticalDamage,
            // 客户端装备表把亲和写成裸元素名（WuxingBonus 的键就是 Metal/Wood/…），
            // 服务端模板沿用同一套写法，两端不必各起一个名字。
            ["Metal"] = AttributeType.MetalAffinity,
            ["Wood"] = AttributeType.WoodAffinity,
            ["Water"] = AttributeType.WaterAffinity,
            ["Fire"] = AttributeType.FireAffinity,
            ["Earth"] = AttributeType.EarthAffinity,
        };

    /// <summary>
    /// 五行属性值（亲和）键。它<b>不</b>进三行：亲和只按
    /// <see cref="AttributeDerivation.DeriveAffinityFallback"/> 折成兜底地板，
    /// 直接摊进攻击力会让雷达图和伤害公式共用一个谁都没定过口径的数字。
    /// </summary>
    private static bool TryMapAffinity(AttributeType kind, float value, ref ElementStatVector affinity)
    {
        switch (kind)
        {
            case AttributeType.MetalAffinity: affinity = affinity.WithRowAdded(ElementRow.Metal, value); return true;
            case AttributeType.WoodAffinity: affinity = affinity.WithRowAdded(ElementRow.Wood, value); return true;
            case AttributeType.WaterAffinity: affinity = affinity.WithRowAdded(ElementRow.Water, value); return true;
            case AttributeType.FireAffinity: affinity = affinity.WithRowAdded(ElementRow.Fire, value); return true;
            case AttributeType.EarthAffinity: affinity = affinity.WithRowAdded(ElementRow.Earth, value); return true;
            default: return false;
        }
    }

    private static bool TryMapElementRow(AttributeType kind, float value,
        ref ElementStatVector attack, ref ElementStatVector defense, ref ElementStatVector penetration)
    {
        switch (kind)
        {
            case AttributeType.MetalAttack: attack = attack.WithRowAdded(ElementRow.Metal, value); return true;
            case AttributeType.WoodAttack: attack = attack.WithRowAdded(ElementRow.Wood, value); return true;
            case AttributeType.WaterAttack: attack = attack.WithRowAdded(ElementRow.Water, value); return true;
            case AttributeType.FireAttack: attack = attack.WithRowAdded(ElementRow.Fire, value); return true;
            case AttributeType.EarthAttack: attack = attack.WithRowAdded(ElementRow.Earth, value); return true;

            case AttributeType.MetalDefense: defense = defense.WithRowAdded(ElementRow.Metal, value); return true;
            case AttributeType.WoodDefense: defense = defense.WithRowAdded(ElementRow.Wood, value); return true;
            case AttributeType.WaterDefense: defense = defense.WithRowAdded(ElementRow.Water, value); return true;
            case AttributeType.FireDefense: defense = defense.WithRowAdded(ElementRow.Fire, value); return true;
            case AttributeType.EarthDefense: defense = defense.WithRowAdded(ElementRow.Earth, value); return true;

            case AttributeType.MetalPenetration: penetration = penetration.WithRowAdded(ElementRow.Metal, value); return true;
            case AttributeType.WoodPenetration: penetration = penetration.WithRowAdded(ElementRow.Wood, value); return true;
            case AttributeType.WaterPenetration: penetration = penetration.WithRowAdded(ElementRow.Water, value); return true;
            case AttributeType.FirePenetration: penetration = penetration.WithRowAdded(ElementRow.Fire, value); return true;
            case AttributeType.EarthPenetration: penetration = penetration.WithRowAdded(ElementRow.Earth, value); return true;
            default: return false;
        }
    }

    private static bool TryMapScalar(AttributeType kind, float value, ParsedItemAttributes target)
    {
        switch (kind)
        {
            case AttributeType.Attack: target.PhysicalAttack += value; return true;
            case AttributeType.Defense: target.PhysicalDefense += value; return true;
            // 客户端装备表历史上写的是 "HP"/"MP"（不是 MaxHealth/MaxMana），两种都认。
            case AttributeType.MaxHealth: target.MaxHealth += value; return true;
            case AttributeType.MaxMana: target.MaxEnergy += value; return true;
            case AttributeType.CriticalRate: target.CritRate += value / 100f; return true;
            default: return false;
        }
    }
}

/// <summary>
/// 五行行号常量（1..5 = 金木水火土，0 = 无属性）。
/// 与 <see cref="ElementStatVector.At"/>、属性表 <c>*_attack</c> 列、客户端
/// <c>Game.Character.Attributes.WuxingElement</c> 同一套编号。
/// </summary>
public static class ElementRow
{
    public const int None = 0;
    public const int Metal = 1;
    public const int Wood = 2;
    public const int Water = 3;
    public const int Fire = 4;
    public const int Earth = 5;
}
