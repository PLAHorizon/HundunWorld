using System;
using System.Collections.Generic;
using Game.Character.Attributes;

namespace HundunWorld.Game.Combat.Skills
{
    /// <summary>
    /// 五行技能树的客户端语义层：阶/境排布、相生演化、封存判定、解锁缺项。
    /// <para>
    /// 存在的理由是把规则收在一处。技能栏、技能树页、秘籍解析都要问"这一式是不是已经被
    /// 演化融合掉了""还差什么才能学"，各写一遍必然分叉 —— 尤其"封存"的口径（D2：演化是换代，
    /// 被融合的源技不再可用）一旦两处理解不同，玩家就会看到栏上留着一个点了没反应的格子。
    /// </para>
    /// <para>数据来源只有两份：服务端下行的已学目录，与 <see cref="SkillDatabase"/> 的技能模板
    /// （阶位、境内序号、相生对、演化来源都随模板下行，这里不再抄写对照表）。</para>
    /// </summary>
    public static class WuxingSkillTree
    {
        /// <summary>道阶（超越天阶，只能由相生演化得到）。</summary>
        public const int GradeDao = 6;

        /// <summary>混沌一炁（终式）。</summary>
        public const int GradeChaos = 7;

        /// <summary>技能树列数：白黄玄地天 + 道 + 混沌。</summary>
        public const int ColumnCount = 7;

        /// <summary>相生序对应的行索引（金木水火土 = 0..4）。</summary>
        public static readonly string[] ElementRowKeys = { "Metal", "Wood", "Water", "Fire", "Earth" };

        /// <summary>
        /// 相生对 P1..P5（金生水、水生木、木生火、火生土、土生金）在技能树上归位的行索引，
        /// 按"子行"落位：金水式挂在水行那一行。
        /// </summary>
        public static readonly int[] PairChildRow = { 2, 1, 3, 4, 0 };

        private static readonly string[] ElementCn = { "金", "木", "水", "火", "土" };
        private static readonly string[] GradeCn = { "白", "黄", "玄", "地", "天", "道", "混沌" };
        private static readonly string[] StageCn = { "初", "中", "极" };

        /// <summary>取五行阶梯内的全部模板（1..7 阶），按 行→列→境 稳定排序。</summary>
        public static List<SkillConfig> CollectTree()
        {
            var all = SkillDatabase.GetAllSkills();
            var tree = new List<SkillConfig>();
            for (int i = 0; i < all.Count; i++)
            {
                var cfg = all[i];
                if (cfg == null || cfg.Grade < 1 || cfg.Grade > ColumnCount) continue;
                tree.Add(cfg);
            }

            tree.Sort((a, b) =>
            {
                int ra = RowOf(a), rb = RowOf(b);
                if (ra != rb) return ra.CompareTo(rb);
                int ca = ColumnOf(a), cb = ColumnOf(b);
                if (ca != cb) return ca.CompareTo(cb);
                return a.GradeStage.CompareTo(b.GradeStage);
            });
            return tree;
        }

        public static int RowOf(SkillConfig cfg)
        {
            if (cfg == null) return -1;
            if (cfg.Grade == GradeDao)
            {
                int pair = cfg.Pair - 1;
                return pair >= 0 && pair < PairChildRow.Length ? PairChildRow[pair] : -1;
            }
            if (cfg.Grade == GradeChaos) return 2;              // 混沌居中一行
            return (int)cfg.GetWuxingElement() - 1;
        }

        public static int ColumnOf(SkillConfig cfg) => cfg == null ? -1 : cfg.Grade - 1;

        public static bool IsDao(SkillConfig cfg) => cfg != null && cfg.Grade == GradeDao;
        public static bool IsChaos(SkillConfig cfg) => cfg != null && cfg.Grade == GradeChaos;

        /// <summary>
        /// 已封存集合：某个已学的演化式把哪两式融合掉了，那两式就退役
        /// （不可施放、不显示、不占技能栏），散功可原路解封。
        /// </summary>
        public static HashSet<int> ComputeRetired(IEnumerable<int> learnedIds)
        {
            var retired = new HashSet<int>();
            if (learnedIds == null) return retired;

            foreach (var id in learnedIds)
            {
                var cfg = SkillDatabase.GetSkill(id);
                var from = cfg?.EvolveFrom;
                if (from == null || from.Length == 0) continue;
                for (int i = 0; i < from.Length; i++) retired.Add(from[i]);
            }
            return retired;
        }

        /// <summary>当前已学 id（服务端权威目录；未下行时为空，页面按"未学"渲染而不是猜）。</summary>
        public static List<int> LearnedIds()
        {
            var ids = new List<int>();
            var learned = Services.SkillCatalogService.Instance?.Learned;
            if (learned == null) return ids;
            foreach (var kv in learned) ids.Add(kv.Key);
            ids.Sort();
            return ids;
        }

        /// <summary>
        /// 权威背包里这个材料模板共有几件（跨堆叠求和）。
        /// 只在 <c>HasServerData</c> 为真时调用：没有下行快照时返回 0 会被读成"一件都没有"。
        /// </summary>
        private static int CountMaterial(Services.PlayerInventoryService inv, int templateId)
        {
            int total = 0;
            var items = inv.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item != null && item.TemplateId == templateId) total += item.Count;
            }
            return total;
        }

        /// <summary>
        /// 演化源式"已习得但还没练满"的文案；null 表示这一式已满足服务端的圆满要求。
        /// 口径与服务端 <c>SkillGrain.CheckLearnGates(requireEvolveSourcesMaxed: true)</c> 逐字对齐：
        /// <c>MaxLevel &lt;= 0</c> 视为无从再进（即圆满），只有 <c>Level &lt; MaxLevel</c> 才算缺。
        /// </summary>
        private static string EvolveSourceMaxingBlocker(int sourceId)
        {
            var learned = Services.SkillCatalogService.Instance?.Learned;
            if (learned == null || !learned.TryGetValue(sourceId, out var info) || info == null) return null;
            if (info.MaxLevel <= 0 || info.Level >= info.MaxLevel) return null;

            var src = SkillDatabase.GetSkill(sourceId);
            return $"演化源《{(src != null ? src.SkillName : sourceId.ToString())}》未圆满（{info.Level}/{info.MaxLevel}），需先精研";
        }

        /// <summary>
        /// 已学且练满的<b>道阶</b>式 id。"练满"沿用服务端口径：Level 到 MaxLevel（§6.2 的"圆满"）。
        /// </summary>
        public static List<int> MaxedDaoIds()
        {
            var ids = new List<int>();
            var learned = Services.SkillCatalogService.Instance?.Learned;
            if (learned == null) return ids;

            foreach (var kv in learned)
            {
                if (kv.Value == null) continue;
                var cfg = SkillDatabase.GetSkill(kv.Key);
                if (cfg == null || !IsDao(cfg)) continue;
                // 圆满口径与演化源判定共用同一个函数，别在两处各写一遍 < 号
                if (EvolveSourceMaxingBlocker(kv.Key) != null) continue;
                ids.Add(kv.Key);
            }

            ids.Sort();
            return ids;
        }

        /// <summary>
        /// 圆满的道阶<b>系</b>数：一对相生弧要三境俱全才计一系（"任意 3 对道阶系三境圆满"= 9 式）。
        /// 道阶式 id 的中间两位就是它所归属的相生对（70101/70102/70103 → 第 1 对）。
        /// </summary>
        public static int CountCompleteEvolutionSeries(IEnumerable<int> maxedDaoIds)
        {
            if (maxedDaoIds == null) return 0;

            var stagesPerSeries = new Dictionary<int, HashSet<int>>();
            foreach (var id in maxedDaoIds)
            {
                int series = (id / 100) % 100;
                if (series <= 0) continue;
                if (!stagesPerSeries.TryGetValue(series, out var stages))
                    stagesPerSeries[series] = stages = new HashSet<int>();
                stages.Add(id % 100);
            }

            int count = 0;
            foreach (var stages in stagesPerSeries.Values)
                if (stages.Count >= 3) count++;
            return count;
        }

        /// <summary>该式的亲和门槛比对：非五行式（混沌）无亲和要求。</summary>
        public static int AffinityOf(CharacterAttributesComponent actor, SkillConfig cfg)
        {
            if (actor == null || cfg == null) return 0;
            switch (cfg.GetWuxingElement())
            {
                case WuxingElement.Metal: return actor.MetalAffinity;
                case WuxingElement.Wood: return actor.WoodAffinity;
                case WuxingElement.Water: return actor.WaterAffinity;
                case WuxingElement.Fire: return actor.FireAffinity;
                case WuxingElement.Earth: return actor.EarthAffinity;
                default: return int.MaxValue;      // 无属性式不因亲和被卡住
            }
        }

        /// <summary>
        /// 还差什么才能研读此式；返回 null 表示条件已具。
        /// 只报事实（等级/亲和/前置），不做任何"帮他练级"的推断。
        /// </summary>
        public static string DescribeBlockers(SkillConfig cfg, CharacterAttributesComponent actor, ICollection<int> learnedIds)
        {
            if (cfg == null || cfg.Grade < 1) return "不属于五行技能树";

            var parts = new List<string>();
            int level = actor != null ? actor.Level : 1;
            if (level < cfg.RequiredLevel) parts.Add($"等级 {level}/{cfg.RequiredLevel}");

            int have = AffinityOf(actor, cfg);
            if (have < cfg.ComprehensionRequire)
                parts.Add($"{ElementNameOf(cfg)}亲和 {have}/{cfg.ComprehensionRequire}");

            if (cfg.PreSkillId > 0 && (learnedIds == null || !learnedIds.Contains(cfg.PreSkillId)))
            {
                var pre = SkillDatabase.GetSkill(cfg.PreSkillId);
                parts.Add($"前置《{(pre != null ? pre.SkillName : cfg.PreSkillId.ToString())}》");
            }

            // 演化式的门槛是"两段弧的各阶三式圆满"，模板用 PreSkillId 串道阶内部三境，
            // 被融合的两式属于哪一对由 Pair 决定，这里给出可读的缺项名而不是干巴巴的 id。
            // 服务端研读路径（SkillGrain.LearnFromBookAsync）额外要求源式**练满**，
            // 只报"未习得"会让玩家看着"条件已具"去读书、然后被一句"演化源未圆满"退回来。
            if (IsDao(cfg) && cfg.EvolveFrom != null && cfg.EvolveFrom.Length > 0 && learnedIds != null)
            {
                for (int i = 0; i < cfg.EvolveFrom.Length; i++)
                {
                    int sourceId = cfg.EvolveFrom[i];
                    if (!learnedIds.Contains(sourceId))
                    {
                        var src = SkillDatabase.GetSkill(sourceId);
                        parts.Add($"演化源《{(src != null ? src.SkillName : sourceId.ToString())}》");
                        continue;
                    }
                    string notMaxed = EvolveSourceMaxingBlocker(sourceId);
                    if (notMaxed != null) parts.Add(notMaxed);
                }
            }

            // §6.2 的两条计数门槛。周天次数取权威下行校正后的预测器（服务端拒绝时玩家先在树上看到缺项），
            // 道阶系数只看已练满的那几式——两边口径必须与服务端 WuxingEvolveRules 一致。
            if (cfg.LoopNeed > 0)
            {
                int loopDone = WuxingCycleTracker.Instance.CompletionCount;
                if (loopDone < cfg.LoopNeed) parts.Add($"周天圆满 {loopDone}/{cfg.LoopNeed}");
            }

            if (cfg.NeedPairsCompleted > 0)
            {
                int series = CountCompleteEvolutionSeries(MaxedDaoIds());
                if (series < cfg.NeedPairsCompleted) parts.Add($"道阶系圆满 {series}/{cfg.NeedPairsCompleted}");
            }

            // §14 的三族动态门槛现在都能在客户端算出"还差多少"：好感随技能目录下行、材料与加点本地就有权威快照。
            // 共同纪律：拿不到快照时只说"要什么"，拿到后达标就整条不列——
            // 把备齐的东西列进缺项，页面会对一个合法可研读的人说"暂不可学"（G19/G20 的同一类假缺项）。
            var gates = cfg.LearnCost;
            if (gates != null)
            {
                var favor = gates.FavorNeed;
                if (favor != null && favor.Min > 0)
                {
                    // 好感快照随技能目录一起下行（网关顺手读 FavorabilityGrain），所以这里能报"还差多少"。
                    // 口径抄服务端 WuxingDynamicGates.CheckFavorability：五域那档是"每一域都要到 Min"，
                    // 所以缺项要点名最差的那一域——服务端也正是按第一个不达标的域来回绝的。
                    var catalog = Services.SkillCatalogService.Instance;
                    bool known = catalog != null && catalog.FavorByDomain != null;
                    if (!known)
                    {
                        parts.Add(favor.AllDomains
                            ? $"五域好感各需 {favor.Min}"
                            : $"{RowNameOf(favor.Domain)}域好感需 {favor.Min}");
                    }
                    else if (favor.AllDomains)
                    {
                        // 与服务端 CheckFavorability 的 AllDomains 分支**逐字同序**：从金到土扫，命中第一个不达标就报它。
                        // （不是"报最差的那一域"——那看着更贴心，却与服务端回绝时点名的域不一致，
                        //  玩家会拿着"火域不够"的提示去刷火，服务端其实卡在木。）
                        for (int d = 1; d <= ElementCn.Length; d++)
                        {
                            int favorHave = catalog.FavorOf(d);
                            if (favorHave < favor.Min)
                            {
                                parts.Add($"五域好感各需 {favor.Min}，{RowNameOf(d)}域只有 {favorHave}");
                                break;
                            }
                        }
                    }
                    else
                    {
                        int favorHave = catalog.FavorOf(favor.Domain);
                        if (favorHave < favor.Min) parts.Add($"{RowNameOf(favor.Domain)}域好感 {favorHave}/{favor.Min}");
                    }
                }

                if (gates.Materials != null && gates.Materials.Count > 0)
                {
                    var inv = Services.PlayerInventoryService.Instance;
                    bool authoritative = inv != null && inv.HasServerData;
                    var bits = new List<string>(gates.Materials.Count);
                    foreach (var m in gates.Materials)
                    {
                        if (m == null || m.Count <= 0) continue;
                        var def = HundunWorld.Game.Inventory.ItemCatalog.Resolve(m.ItemId);
                        string name = def != null && !string.IsNullOrWhiteSpace(def.Name)
                            ? def.Name : "物品 " + m.ItemId;

                        if (!authoritative)
                        {
                            bits.Add($"{name}×{m.Count}");      // 背包还没下行：退回"只说要什么"
                            continue;
                        }
                        int owned = CountMaterial(inv, m.ItemId);
                        if (owned >= m.Count) continue;          // 备齐了就不是缺项
                        bits.Add($"{name} {owned}/{m.Count}");
                    }
                    if (bits.Count > 0) parts.Add("研读材料 " + string.Join("、", bits));
                }

                var a = gates.AttrNeed;
                if (a != null)
                {
                    // 服务端拿**加点数**比（WuxingDynamicGates.CheckAttributes 读 AttributeAllocationData），
                    // 不是最终属性值：这里必须问同一个源，否则"力量 60"这种显示会把加点点数不够的人误导成属性不够。
                    var alloc = Services.AttributeAllocationService.Instance;
                    bool known = alloc != null && alloc.HasServerData;
                    int str = known ? alloc.Allocation.Strength : -1;
                    int agi = known ? alloc.Allocation.Agility : -1;
                    int hp = known ? alloc.Allocation.Health : -1;
                    int intel = known ? alloc.Allocation.Intelligence : -1;

                    var bits = new List<string>(4);
                    AppendAttrNeed(bits, "力量", a.Strength, str);
                    AppendAttrNeed(bits, "敏捷", a.Agility, agi);
                    AppendAttrNeed(bits, "体魄", a.Health, hp);
                    AppendAttrNeed(bits, "神识", a.Intelligence, intel);
                    if (bits.Count > 0) parts.Add("基础属性 " + string.Join("、", bits));
                }
            }

            return parts.Count == 0 ? null : string.Join("、", parts);
        }

        /// <summary>
        /// 单条基础属性（加点）门槛。<paramref name="have"/> 传负数表示"服务端还没下行"，
        /// 此时只说要什么；够则整条不列（备齐了就不是缺项）；不够报"已有/需要"。
        /// </summary>
        private static void AppendAttrNeed(List<string> bits, string name, int need, int have)
        {
            if (need <= 0) return;
            if (have < 0) { bits.Add($"{name} {need}"); return; }
            if (have >= need) return;
            bits.Add($"{name} {have}/{need}");
        }

        /// <summary>五行域号（1金…5土）→ 行名。越界退化成"该行"，不让一条脏数据把整页文案打断。</summary>
        private static string RowNameOf(int domain) =>
            domain >= 1 && domain <= ElementCn.Length ? ElementCn[domain - 1] : "该行";

        public static string ElementNameOf(SkillConfig cfg)
        {
            int row = RowOfBaseElement(cfg);
            return row >= 0 && row < ElementCn.Length ? ElementCn[row] : "混沌";
        }

        private static int RowOfBaseElement(SkillConfig cfg) => (int)cfg.GetWuxingElement() - 1;

        // ---- 效果说明的诚实口径（方案 §14.7 G25 / G33）----
        // 数据里写着定身/DOT/减速/禁足/护盾，但服务端今天只结算破甲与回复这两类。
        // 症状不是报错，而是"照着说明玩、发现效果没出现"，所以在说明里把未接入的部分点名。
        // 这张表是服务端账本 WuxingSkillEffects.AppliedKeys 的镜像（客户端拿不到账本），
        // 两端一致性由 WuxingClientEffectHonestyTests 钉住：服务端新兑现一条效果而这里没跟上，测试就红。
        private static readonly System.Collections.Generic.HashSet<string> SettledEffectTypes =
            new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal)
            {
                "Penetration",
                "Heal",
            };

        private static readonly string[] PendingEffectLabels =
        {
            "Stun|定身",
            "Root|禁足",
            "Slow|减速",
            "DamageOverTime|持续伤害",
            "Shield|护盾",
        };

        /// <summary>招式说明：原文在上，若本式还带着未接入结算的效果，就补一句实话。</summary>
        public static string DescribeEffects(SkillConfig cfg)
        {
            if (cfg == null) return "";
            string head = cfg.Description ?? "";
            if (cfg.Effects == null || cfg.Effects.Count == 0) return head;

            var pending = new System.Collections.Generic.List<string>();
            foreach (var label in PendingEffectLabels)
            {
                int sep = label.IndexOf('|');
                string type = label.Substring(0, sep);
                if (SettledEffectTypes.Contains(type)) continue;
                foreach (var e in cfg.Effects)
                {
                    if (e != null && e.EffectType == type)
                    {
                        pending.Add(label.Substring(sep + 1));
                        break;
                    }
                }
            }

            if (pending.Count == 0) return head;
            return head + "\n（实装中，暂未生效：" + string.Join("、", pending) + "）";
        }

        public static string GradeName(int grade) =>
            grade >= 1 && grade <= GradeCn.Length ? GradeCn[grade - 1] : "未知";

        public static string StageName(int stage) =>
            stage >= 1 && stage <= StageCn.Length ? StageCn[stage - 1] : "·";

        /// <summary>给 UI 的一行小注：这一式在树上的位置。</summary>
        public static string PositionLabel(SkillConfig cfg) =>
            cfg == null ? "" : $"{GradeName(cfg.Grade)} · {StageName(cfg.GradeStage)}境";
    }
}
