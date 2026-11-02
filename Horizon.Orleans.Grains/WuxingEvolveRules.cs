using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 演化门槛的纯规则（方案 §6.2）：把 learn_cost JSON 拆开，再判"周天圆满次数"与"道阶系圆满对数"。
    /// <para>
    /// 这两条是"五行技能在特定条件下可演化"里那个<b>特定条件</b>。它们刻意做成无 Grain/无存储的
    /// 静态函数：门槛判定错了不会崩，只会让玩家白读一卷书，所以必须能逐值回归，而不是等 PIE 里发现。
    /// </para>
    /// </summary>
    public static class WuxingEvolveRules
    {
        /// <summary>
        /// 模板 <c>learn_cost</c> 列里那份 JSON 的读法。同一个天阶式可能出现在两段相生弧上，
        /// 单值列 <c>advance_skill_id</c> 表达不了"融掉哪两式"，因此消耗表放这里。
        /// <para>
        /// §14 的动态门槛（好感域 / 特殊材料 / 基础属性）同样住在这份 JSON 里：门槛是内容数据，
        /// 每加一种就改一次表结构的做法不成立，而这一列已经原样下行给客户端了。
        /// </para>
        /// </summary>
        public readonly record struct LearnCost(
            int[] Consume, int LoopNeed, int NeedPairsCompleted,
            int FavorDomain = 0, bool FavorAllDomains = false, int FavorMin = 0,
            IReadOnlyList<MaterialNeed>? Materials = null,
            int StrengthNeed = 0, int AgilityNeed = 0, int HealthNeed = 0, int IntelligenceNeed = 0)
        {
            public static LearnCost None => new(Array.Empty<int>(), 0, 0);

            /// <summary>这份门槛里有没有需要外部事实（背包/好感/属性）才能判的条目。</summary>
            public bool NeedsDynamicContext =>
                FavorMin > 0 || (Materials?.Count ?? 0) > 0
                || StrengthNeed > 0 || AgilityNeed > 0 || HealthNeed > 0 || IntelligenceNeed > 0;
        }

        public readonly record struct MaterialNeed(int ItemId, int Count);

        /// <summary>
        /// 解析 learn_cost。这列也用来存普通式的开销描述（不是 JSON 或没有这些字段），
        /// 所以脏值一律退化成 <see cref="LearnCost.None"/> 而不是抛——一个脏字符串不该让整式无法研读。
        /// </summary>
        public static LearnCost Parse(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return LearnCost.None;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return LearnCost.None;
                var root = doc.RootElement;

                var consume = Array.Empty<int>();
                if (root.TryGetProperty("consume", out var consumeElement)
                    && consumeElement.ValueKind == JsonValueKind.Array)
                {
                    var ids = new List<int>();
                    foreach (var item in consumeElement.EnumerateArray())
                        if (item.TryGetInt32(out var id) && id > 0) ids.Add(id);
                    consume = ids.ToArray();
                }

                var materials = new List<MaterialNeed>();
                if (root.TryGetProperty("materials", out var mats) && mats.ValueKind == JsonValueKind.Array)
                {
                    foreach (var m in mats.EnumerateArray())
                    {
                        if (m.ValueKind != JsonValueKind.Object) continue;
                        if (!m.TryGetProperty("itemId", out var idEl) || !idEl.TryGetInt32(out var itemId) || itemId <= 0)
                            continue;
                        if (!m.TryGetProperty("count", out var cntEl) || !cntEl.TryGetInt32(out var count) || count <= 0)
                            continue;
                        materials.Add(new MaterialNeed(itemId, count));
                    }
                }

                int favorDomain = 0, favorMin = 0;
                bool favorAll = false;
                if (root.TryGetProperty("favorNeed", out var favor) && favor.ValueKind == JsonValueKind.Object)
                {
                    favorAll = favor.TryGetProperty("allDomains", out var all) && all.ValueKind == JsonValueKind.True;
                    favorDomain = ReadInt(favor, "domain");
                    favorMin = ReadInt(favor, "min");
                    if (favorMin <= 0) favorAll = false;      // 没有门槛值就等于没有这条要求
                }

                var attrs = root.TryGetProperty("attrNeed", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;

                return new LearnCost(
                    consume,
                    ReadInt(root, "loopNeed"),
                    ReadInt(root, "needPairsCompleted"),
                    FavorDomain: favorDomain,
                    FavorAllDomains: favorAll,
                    FavorMin: favorMin,
                    Materials: materials,
                    StrengthNeed: attrs.ValueKind == JsonValueKind.Object ? ReadInt(attrs, "strength") : 0,
                    AgilityNeed: attrs.ValueKind == JsonValueKind.Object ? ReadInt(attrs, "agility") : 0,
                    HealthNeed: attrs.ValueKind == JsonValueKind.Object ? ReadInt(attrs, "health") : 0,
                    IntelligenceNeed: attrs.ValueKind == JsonValueKind.Object ? ReadInt(attrs, "intelligence") : 0);
            }
            catch (JsonException)
            {
                return LearnCost.None;
            }
        }

        private static int ReadInt(JsonElement root, string property) =>
            root.TryGetProperty(property, out var value) && value.TryGetInt32(out var parsed) && parsed > 0
                ? parsed : 0;

        /// <summary>
        /// 道阶式 id 的中间两位就是它所归属的相生对（70101/70102/70103 → 第 1 对，金生水）。
        /// 混沌式（90001）不在此表内，它的对数由 <see cref="CountCompleteEvolutionPairs"/> 只数道阶。
        /// </summary>
        public static int EvolutionPairOf(int daoSkillId) => (daoSkillId / 100) % 100;

        public static int EvolutionStageOf(int daoSkillId) => daoSkillId % 100;

        /// <summary>
        /// 已圆满的道阶<b>系</b>数：一对要三境俱全才算一系（§6.2 的"任意 3 对道阶系三境圆满（9 式）"）。
        /// 入参是已学且练满的道阶式 id 集合。
        /// </summary>
        public static int CountCompleteEvolutionPairs(IEnumerable<int> maxedDaoSkillIds)
            => (maxedDaoSkillIds ?? Enumerable.Empty<int>())
                .GroupBy(EvolutionPairOf)
                .Where(g => g.Key > 0 && g.Select(EvolutionStageOf).Distinct().Count() >= 3)
                .Select(g => g.Key)
                .Distinct()
                .Count();

        /// <summary>
        /// 直接学习（非研读）通道能不能碰这一式（§14.5）。
        /// <para>
        /// 判据从"第几阶"换成"有没有登记秘籍"：只要秘籍表里有这本，就必须走研读
        /// ——白阶也一样，因为初级书本来就是任务与刷怪给的。这比按阶位拦准确得多，
        /// 也把 <c>LearnMethod</c> 从"客户端自己声明"变成服务端事实，
        /// 顺带把"按一个按钮绕过整条技能书经济"的口子从源头堵掉。
        /// 没登记秘籍的行（旧 26 技、剧情授予式）仍然可以直接学。
        /// </para>
        /// </summary>
        public static string? DirectLearnBlocker(bool hasRegisteredBook, string? bookName = null)
        {
            if (!hasRegisteredBook) return null;
            var name = string.IsNullOrWhiteSpace(bookName) ? "对应秘籍" : bookName!;
            return $"此式需研读《{name}》方可习得（书由任务与战斗产出，直接学习通道不放开）";
        }

        /// <summary>
        /// 周天圆满次数与道阶系圆满对数两条计数门槛。返回 null 表示这两条都过了。
        /// <para>
        /// 文案要能直接给玩家看：他手里那卷书被拒时，"条件不足"没有意义，
        /// "周天圆满不足：需 12 次，当前 3 次"才告诉得了他下一步该去做什么。
        /// </para>
        /// </summary>
        public static string? CheckCycleGates(LearnCost cost, int completionCount, int completeEvolutionPairs)
        {
            if (cost.LoopNeed > 0 && completionCount < cost.LoopNeed)
                return $"周天圆满不足：需 {cost.LoopNeed} 次，当前 {completionCount} 次";

            if (cost.NeedPairsCompleted > 0 && completeEvolutionPairs < cost.NeedPairsCompleted)
                return $"道阶系圆满不足：需 {cost.NeedPairsCompleted} 对（每对三境俱全），当前 {completeEvolutionPairs} 对";

            return null;
        }
    }
}
