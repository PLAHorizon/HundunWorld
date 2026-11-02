using Horizon.Game.Message.Network;
using System.Collections.Generic;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// §14 的三条动态门槛判定：区域好感、特殊材料、基础属性。
    /// <para>
    /// 判定输入是 <see cref="LearnGateContext"/>（跨 Grain 契约类型，由调用方在服务端装配），
    /// 本类自己不做任何 IO，因此可以逐值回归。
    /// </para>
    /// </summary>
    public static class WuxingDynamicGates
    {
        private static readonly string[] DomainNames = { "金", "木", "水", "火", "土" };

        /// <summary>表里没这一项就是 0——上下文集合时省略零值，所以缺项与零是同一件事。</summary>
        private static int ValueOf(IReadOnlyDictionary<int, int>? map, int key) =>
            map != null && map.TryGetValue(key, out var value) ? value : 0;

        /// <summary>判定；返回 null 表示三条都过。</summary>
        public static string? Check(WuxingEvolveRules.LearnCost cost, LearnGateContext? ctx)
        {
            if (!cost.NeedsDynamicContext) return null;

            // fail closed：门槛存在而上下文没给，不能当成"条件满足"放过。
            // 漏装配的后果是"高阶技能无材料、无好感也能学"，比一次拒绝严重得多。
            if (ctx == null) return "服务端未能核验好感/材料/属性条件，暂不放行";

            var favor = CheckFavorability(cost, ctx.FavorByDomain);
            if (favor != null) return favor;

            var materials = CheckMaterials(cost, ctx.ItemCounts, ctx.ItemNames);
            if (materials != null) return materials;

            return CheckAttributes(cost, ctx.Allocation);
        }

        /// <summary>好感门槛。allDomains 式（混沌终式）报的是最不达标的那一域。</summary>
        public static string? CheckFavorability(WuxingEvolveRules.LearnCost cost, IReadOnlyDictionary<int, int>? favorByDomain)
        {
            if (cost.FavorMin <= 0) return null;

            if (cost.FavorAllDomains)
            {
                for (int domain = 1; domain <= DomainNames.Length; domain++)
                {
                    int have = ValueOf(favorByDomain, domain);
                    if (have < cost.FavorMin)
                        return $"{DomainNames[domain - 1]}域好感不足：需 {cost.FavorMin}，当前 {have}";
                }
                return null;
            }

            if (cost.FavorDomain < 1 || cost.FavorDomain > DomainNames.Length) return null;   // 数据没给域 → 视为无门槛
            int value = ValueOf(favorByDomain, cost.FavorDomain);
            if (value >= cost.FavorMin) return null;
            return $"{DomainNames[cost.FavorDomain - 1]}域好感不足：需 {cost.FavorMin}，当前 {value}";
        }

        /// <summary>材料门槛：逐种核对数量，报第一种缺的（玩家一次只缺一种最常见）。</summary>
        public static string? CheckMaterials(WuxingEvolveRules.LearnCost cost,
            IReadOnlyDictionary<int, int>? itemCounts, IReadOnlyDictionary<int, string>? itemNames = null)
        {
            var needs = cost.Materials;
            if (needs == null || needs.Count == 0) return null;

            foreach (var need in needs)
            {
                int have = ValueOf(itemCounts, need.ItemId);
                if (have >= need.Count) continue;

                string name = itemNames != null && itemNames.TryGetValue(need.ItemId, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n : $"物品 {need.ItemId}";
                return $"特殊材料不足：{name} 需 {need.Count}，当前 {have}";
            }
            return null;
        }

        /// <summary>基础属性门槛（融合式用）。名称沿用玩家面板上的叫法：力量/敏捷/体魄/神识。</summary>
        public static string? CheckAttributes(WuxingEvolveRules.LearnCost cost, AttributeAllocationData? allocation)
        {
            if (allocation == null)
                return cost.StrengthNeed > 0 || cost.AgilityNeed > 0 || cost.HealthNeed > 0 || cost.IntelligenceNeed > 0
                    ? "读不到角色基础属性，暂不放行"
                    : null;

            if (cost.StrengthNeed > 0 && allocation.Strength < cost.StrengthNeed)
                return $"力量不足：需 {cost.StrengthNeed}，当前 {allocation.Strength}";
            if (cost.AgilityNeed > 0 && allocation.Agility < cost.AgilityNeed)
                return $"敏捷不足：需 {cost.AgilityNeed}，当前 {allocation.Agility}";
            if (cost.HealthNeed > 0 && allocation.Health < cost.HealthNeed)
                return $"体魄不足：需 {cost.HealthNeed}，当前 {allocation.Health}";
            if (cost.IntelligenceNeed > 0 && allocation.Intelligence < cost.IntelligenceNeed)
                return $"神识不足：需 {cost.IntelligenceNeed}，当前 {allocation.Intelligence}";

            return null;
        }

        /// <summary>把材料清单写成一行显示文本（技能树页用；不做判定）。</summary>
        public static string DescribeMaterials(WuxingEvolveRules.LearnCost cost,
            IReadOnlyDictionary<int, string>? itemNames = null)
        {
            if (cost.Materials == null || cost.Materials.Count == 0) return "";
            var parts = new List<string>(cost.Materials.Count);
            foreach (var need in cost.Materials)
            {
                string name = itemNames != null && itemNames.TryGetValue(need.ItemId, out var n) && !string.IsNullOrWhiteSpace(n)
                    ? n : need.ItemId.ToString();
                parts.Add($"{name}×{need.Count}");
            }
            return string.Join("、", parts);
        }
    }
}
