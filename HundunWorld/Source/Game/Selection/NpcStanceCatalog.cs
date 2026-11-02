using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace HundunWorld.Game.Selection
{
    /// <summary>
    /// NPC 立场目录：把服务端下发的 NPC 编号（<c>SelectableEntityTag.TemplateId</c>）或模型键
    /// 映射到**名牌色档**（0 友好绿 / 1 中立黄 / 2 敌对红）。
    /// <para>
    /// <b>为什么名牌色不能沿用怪物的让分色</b>：怪物的名字颜色是 <c>(怪等级 − 我等级)</c> 的函数，
    /// 而 NPC 的名字颜色是**立场**的函数——两条正交的轴。把 NPC 也塞进让分色，
    /// 高等级铁匠会被画成骷髅、低等级守卫会被画成灰色，而它们在 WoW 里永远是绿名/黄名。
    /// 判据本身在服务端 <c>NpcReactionCore</c>，本表只是它下发到客户端的那一份**数据**。
    /// </para>
    /// <para>
    /// <b>表缺失时的口径</b>：内置三条硬编码 NPC（1/2/3）一律按友好处理——这与服务端的
    /// <c>NpcTemplateCatalog</c> 完全一致，因此"没配表"时玩家的观感不变（绿名），
    /// 而不是退化成"全员敌对红"。未登记的 NPC 返回 false，调用方保留原有颜色逻辑。
    /// </para>
    /// </summary>
    public static class NpcStanceCatalog
    {
        /// <summary>表文件（相对工程根）。由 <c>Horizon.NpcCli export-visuals --out</c> 生成，随包发布。</summary>
        public const string RelativeTablePath = "Data/Npcs/client_visuals.json";

        /// <summary>立场值（与 <c>NpcReactionCore</c> / <c>NpcStance</c> 同号，导出口按枚举原样写出）。</summary>
        public const int StanceFriendly = 0;
        public const int StanceNeutral = 1;
        public const int StanceHostile = 2;

        private static readonly Dictionary<int, Entry> ByNpcId = new Dictionary<int, Entry>();
        private static readonly Dictionary<string, Entry> ByModelKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>训练师条目（只做展示：教什么、多少钱；能不能学仍由服务端裁决）。</summary>
        public struct TrainerRow
        {
            public int SkillId;

            /// <summary>1 技能 / 2 配方（与服务端 <c>NpcTrainerEntry.service_kind</c> 同值）。</summary>
            public int Kind;

            public long Gold;
            public int ItemId;
            public int ItemCount;
            public int Seq;

            /// <summary>展示名（配方行是配方名；技能行留空，由客户端按技能表取名）。</summary>
            public string Label;
        }

        private static readonly Dictionary<int, TrainerRow[]> TrainerRowsByNpcId = new Dictionary<int, TrainerRow[]>();

        /// <summary>对话菜单的一项（动作种类与参数来自内容表；文字由客户端按种类合成）。</summary>
        public struct GossipOption
        {
            /// <summary>0 文本 1 商店 2 训练 3 制作 4 交任务 5 子菜单（与服务端 <c>NpcGossipCore</c> 同值）。</summary>
            public int Kind;

            public int Arg;
            public int Seq;
            public int MinLevel;
            public int MaxLevel;

            /// <summary>所属台词组（0 = 通用，任何组都显示）。与 NPC 行的 <see cref="Entry.GossipGroupId"/> 配对。</summary>
            public int GroupId;

            /// <summary>
            /// 台词正文（023 起可配）。空串 = 没配，UI 按 <see cref="Kind"/> 合成标签——
            /// 老版本导出的表没有这个键，读不到就该退回合成，而不是显示空白项。
            /// </summary>
            public string Text;
        }

        private static readonly Dictionary<int, GossipOption[]> GossipByNpcId = new Dictionary<int, GossipOption[]>();

        private static bool _loaded;
        private static string _source = string.Empty;

        private struct Entry
        {
            public int NpcId;
            public int Stance;

            /// <summary>服务位图（与 <c>NpcServiceCore</c> 同位值；客户端只用来决定"有没有商店入口"）。</summary>
            public uint Services;

            /// <summary>逐只指认的预制体 GUID（空串 = 没配，按占位人形渲染）。</summary>
            public string Prefab;

            /// <summary>默认台词组（0 = 不筛，任何组都显示）。</summary>
            public int GossipGroupId;
        }

        /// <summary>已加载条目数（诊断用；0 = 只有内置三条）。</summary>
        public static int Count => ByNpcId.Count;

        /// <summary>表来源（诊断用）。</summary>
        public static string Source => _source;

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            ByNpcId.Clear();
            ByModelKey.Clear();
            TrainerRowsByNpcId.Clear();
            GossipByNpcId.Clear();
            _source = "内置三条（未加载外观表）";

            // 内置兜底：与 NpcTemplateCatalog.cs:37-42 的三行逐字对应。宁可重复三行，
            // 也不要让"表还没导"变成"老任务 NPC 变红名"。
            // 服务位与 022 种子/CLI 兜底同值：1 铁匠（对话/商人/训练/配方/设施=121）、
            // 2 守门人（对话/发布/交付=7）、3 行商（对话/商人=9）。
            Add(1, "npc_smith", StanceFriendly, 121u);
            Add(2, "npc_gatekeeper", StanceFriendly, 7u);
            Add(3, "npc_merchant", StanceFriendly, 9u);

            // 训练师兜底：与 022 种子同值（1001=200 金、1002=500 金、配方 900001=800 金）。
            // 没导表时至少能看见"这位铁匠教什么"，而不是一个只有"习得"按钮的空页。
            AddTrainerRows(1, new[]
            {
                new TrainerRow { SkillId = 1001, Kind = 1, Gold = 200, Seq = 0 },
                new TrainerRow { SkillId = 1002, Kind = 1, Gold = 500, Seq = 1 },
                new TrainerRow { SkillId = 900001, Kind = 2, Gold = 800, Seq = 2 },
            });

            // 对话菜单兜底：与 022 种子同值（1 铁匠：文本/商店/制作；3 行商：文本/商店）。
            AddGossip(1, new[]
            {
                new GossipOption { Kind = 0, Seq = 0 },
                new GossipOption { Kind = 1, Seq = 1 },
                new GossipOption { Kind = 3, Arg = 1, Seq = 2 },
            });
            AddGossip(3, new[]
            {
                new GossipOption { Kind = 0, Seq = 0 },
                new GossipOption { Kind = 1, Seq = 1 },
            });

            var root = Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root)) return;

            var path = Path.Combine(root, RelativeTablePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                _source = "表不存在（用内置三条）：" + path;
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                {
                    _source = "根节点不是对象（用内置三条）";
                    return;
                }

                if (!doc.RootElement.TryGetProperty("npcs", out var npcs)
                    || npcs.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    _source = "缺少 npcs 数组（用内置三条）";
                    return;
                }

                var loaded = 0;
                foreach (var item in npcs.EnumerateArray())
                {
                    var npcId = ReadInt(item, "npcId", 0);
                    if (npcId <= 0) continue;
                    var modelKey = ReadString(item, "modelKey");
                    var stance = ReadInt(item, "stance", StanceFriendly);
                    var services = ReadUInt(item, "services", 0u);
                    var prefab = ReadString(item, "prefab");
                    var gossipGroupId = ReadInt(item, "gossipGroupId", 0);
                    Add(npcId, modelKey, stance, services, prefab, gossipGroupId);

                    // 训练条目：缺这一栏不算坏表（老版本导出的表没有它），只是没有"师承"可显示。
                    if (item.TryGetProperty("trainerRows", out var trainerRows)
                        && trainerRows.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var list = new List<TrainerRow>();
                        foreach (var row in trainerRows.EnumerateArray())
                        {
                            var skillId = ReadInt(row, "skillId", 0);
                            if (skillId <= 0) continue;
                            list.Add(new TrainerRow
                            {
                                SkillId = skillId,
                                Kind = ReadInt(row, "kind", 1),
                                Gold = ReadInt(row, "gold", 0),
                                ItemId = ReadInt(row, "itemId", 0),
                                ItemCount = ReadInt(row, "itemCount", 0),
                                Seq = ReadInt(row, "seq", 0),
                                Label = ReadString(row, "label"),
                            });
                        }
                        AddTrainerRows(npcId, list.ToArray());
                    }

                    if (item.TryGetProperty("gossipOptions", out var gossip)
                        && gossip.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var options = new List<GossipOption>();
                        foreach (var row in gossip.EnumerateArray())
                        {
                            var kind = ReadInt(row, "kind", 0);
                            if (kind < 0 || kind > 5) continue;
                            options.Add(new GossipOption
                            {
                                Kind = kind,
                                Arg = ReadInt(row, "arg", 0),
                                Seq = ReadInt(row, "seq", 0),
                                MinLevel = ReadInt(row, "minLevel", 0),
                                MaxLevel = ReadInt(row, "maxLevel", 0),
                                GroupId = ReadInt(row, "group", 0),
                                Text = ReadString(row, "text"),
                            });
                        }
                        AddGossip(npcId, options.ToArray());
                    }
                    loaded++;
                }

                _source = path + "（" + loaded + " 条）";
            }
            catch (Exception ex)
            {
                // 解析失败不清空内置三条：坏表不该把友好 NPC 变成红名。
                _source = "解析失败（保留内置三条）：" + ex.GetBaseException().Message;
                Debug.LogWarning("[NpcStanceCatalog] NPC 立场表解析失败：" + _source);
            }
        }

        private static void Add(int npcId, string modelKey, int stance, uint services, string prefab = "",
            int gossipGroupId = 0)
        {
            if (npcId <= 0) return;
            if (stance < StanceFriendly || stance > StanceHostile) stance = StanceFriendly;

            var entry = new Entry
            {
                NpcId = npcId,
                Stance = stance,
                Services = services,
                Prefab = prefab ?? string.Empty,
                GossipGroupId = gossipGroupId,
            };
            ByNpcId[npcId] = entry;
            if (!string.IsNullOrEmpty(modelKey)) ByModelKey[modelKey] = entry;
        }

        /// <summary>
        /// 这只 NPC 逐只指认的预制体 GUID（空/未登记 = 没配，调用方回退占位人形）。
        /// <para>
        /// 先按编号、再按模型键：编号是身份，模型键只是分组——两只同名模型键的 NPC
        /// 各配各的美术时，按模型键回退会把后加载的那只的样子套到前一只头上。
        /// </para>
        /// </summary>
        public static bool TryGetPrefab(int npcId, string modelKey, out string prefabGuid)
        {
            EnsureLoaded();
            if (npcId > 0
                && ByNpcId.TryGetValue(npcId, out var byId)
                && !string.IsNullOrEmpty(byId.Prefab))
            {
                prefabGuid = byId.Prefab;
                return true;
            }

            if (!string.IsNullOrEmpty(modelKey)
                && ByModelKey.TryGetValue(modelKey, out var byKey)
                && !string.IsNullOrEmpty(byKey.Prefab))
            {
                prefabGuid = byKey.Prefab;
                return true;
            }

            prefabGuid = string.Empty;
            return false;
        }

        private static void AddTrainerRows(int npcId, TrainerRow[] rows)
        {
            if (npcId <= 0) return;
            TrainerRowsByNpcId[npcId] = rows ?? Array.Empty<TrainerRow>();
        }

        private static void AddGossip(int npcId, GossipOption[] options)
        {
            if (npcId <= 0) return;
            GossipByNpcId[npcId] = options ?? Array.Empty<GossipOption>();
        }

        /// <summary>
        /// 这位 NPC 的对话菜单（按 group/seq 排好）。空数组 = 内容表里没配（调用方退回"按服务位直接开窗"）。
        /// <para>只是**显示顺序与可用项**；每一项执行时仍走各自的服务端通路（商店/训练/制作/交任务）。</para>
        /// </summary>
        public static GossipOption[] GetGossipOptions(int npcId)
        {
            EnsureLoaded();
            if (npcId <= 0 || !GossipByNpcId.TryGetValue(npcId, out var options) || options.Length == 0)
                return Array.Empty<GossipOption>();

            // 按 NPC 的默认台词组过滤（0 = 不筛，兼容还没有这一列的旧表）。
            var group = ByNpcId.TryGetValue(npcId, out var entry) ? entry.GossipGroupId : 0;
            if (group <= 0) return options;

            var groups = new int[options.Length];
            for (var i = 0; i < options.Length; i++) groups[i] = options[i].GroupId;

            var keep = NpcGossipMenu.IndicesToShow(groups, group);
            if (keep.Length == options.Length) return options;

            var filtered = new GossipOption[keep.Length];
            for (var i = 0; i < keep.Length; i++) filtered[i] = options[keep[i]];
            return filtered;
        }

        /// <summary>
        /// 这位训练师教什么（按 seq 排好）。<b>只用于显示</b>：能不能学、收多少钱仍由服务端在
        /// <c>SkillHandler</c> 里按内容表判——客户端这份是导出快照，可能与库里最新一版不同。
        /// </summary>
        public static TrainerRow[] GetTrainerRows(int npcId)
        {
            EnsureLoaded();
            return npcId > 0 && TrainerRowsByNpcId.TryGetValue(npcId, out var rows)
                ? rows
                : Array.Empty<TrainerRow>();
        }

        /// <summary>这位训练师是否教这一式（找到时带回学费）。</summary>
        public static bool TryGetTrainerRow(int npcId, int skillId, out TrainerRow row)
        {
            row = default;
            var rows = GetTrainerRows(npcId);
            for (var i = 0; i < rows.Length; i++)
            {
                if (rows[i].SkillId != skillId) continue;
                row = rows[i];
                return true;
            }

            return false;
        }

        /// <summary>
        /// 查 NPC 立场：先按编号，再按模型键。
        /// </summary>
        /// <param name="npcId">服务端下发的 NPC 编号（<=0 表示不是 NPC，直接 false）。</param>
        /// <param name="modelKey">模型键（编号命中时不看它）。</param>
        /// <param name="stance">0 友好 / 1 中立 / 2 敌对。</param>
        public static bool TryResolve(int npcId, string? modelKey, out int stance)
        {
            stance = StanceFriendly;
            EnsureLoaded();

            Entry hit;
            if (npcId > 0 && ByNpcId.TryGetValue(npcId, out hit))
            {
                stance = hit.Stance;
                return true;
            }

            if (!string.IsNullOrEmpty(modelKey) && ByModelKey.TryGetValue(modelKey!, out hit))
            {
                stance = hit.Stance;
                return true;
            }

            return false;
        }

        /// <summary>位图：任务发布者（<c>NpcServiceBits.QuestGiver</c>，1&lt;&lt;1）。</summary>
        public const uint ServiceQuestGiver = 1u << 1;

        /// <summary>位图：商人（与 <c>NpcServiceBits.Vendor</c> 同值 1&lt;&lt;3）。</summary>
        public const uint ServiceVendor = 1u << 3;

        /// <summary>位图：技能训练师（与 <c>NpcServiceBits.Trainer</c> 同值 1&lt;&lt;4）。</summary>
        public const uint ServiceTrainer = 1u << 4;

        /// <summary>位图：配方教学（<c>NpcServiceBits.CraftTrainer</c>，1&lt;&lt;5）。</summary>
        public const uint ServiceCraftTrainer = 1u << 5;

        /// <summary>位图：制作设施（<c>NpcServiceBits.CraftStation</c>，1&lt;&lt;6）。</summary>
        public const uint ServiceCraftStation = 1u << 6;

        /// <summary>
        /// 这个 NPC 是不是商人（决定"交互时要不要开商店"）。
        /// <para>判据来自导出的服务位图，与服务端 <c>NpcServiceCore</c> 同源；查不到返回 false
        /// （宁可不弹窗，也不要给一个"点进去空无一物"的商店）。</para>
        /// </summary>
        public static bool IsVendor(int npcId, string? modelKey)
        {
            EnsureLoaded();

            Entry hit;
            if (npcId > 0 && ByNpcId.TryGetValue(npcId, out hit))
                return (hit.Services & ServiceVendor) != 0;

            if (!string.IsNullOrEmpty(modelKey) && ByModelKey.TryGetValue(modelKey!, out hit))
                return (hit.Services & ServiceVendor) != 0;

            return false;
        }

        /// <summary>
        /// 这个 NPC 是不是技能训练师（决定"交互时要不要把技能页按收费模式打开"）。
        /// <para>与服务端 <c>NpcServiceCore</c> 同源；查不到返回 false（宁可不收费，也不要弹一个假的训练入口）。</para>
        /// </summary>
        public static bool IsTrainer(int npcId, string? modelKey)
        {
            EnsureLoaded();

            Entry hit;
            if (npcId > 0 && ByNpcId.TryGetValue(npcId, out hit))
                return (hit.Services & ServiceTrainer) != 0;

            if (!string.IsNullOrEmpty(modelKey) && ByModelKey.TryGetValue(modelKey!, out hit))
                return (hit.Services & ServiceTrainer) != 0;

            return false;
        }

        /// <summary>会发布任务（决定交互时要不要按发布者拉 offer 清单）。</summary>
        public static bool IsQuestGiver(int npcId, string? modelKey) => HasService(npcId, modelKey, ServiceQuestGiver);

        /// <summary>会教配方（决定交互时要不要拉制作清单）。</summary>
        public static bool IsCraftTrainer(int npcId, string? modelKey) => HasService(npcId, modelKey, ServiceCraftTrainer);

        /// <summary>是制作设施（决定能不能在这里做东西）。</summary>
        public static bool IsCraftStation(int npcId, string? modelKey) => HasService(npcId, modelKey, ServiceCraftStation);

        /// <summary>
        /// 服务位判定的<b>唯一入口</b>：服务端下发了位图（≥0）就以它为准，未下发（−1）才退到本目录。
        /// <para>为什么要分成两条路：目录是**导出那一刻**的快照。运营在编辑器里给某个 NPC 加了
        /// "商人"之后，只有读服务端位图的那条通路会立刻生效；继续各自读目录的话，
        /// 玩家看到的表现与"根本没改"完全一样，且没有任何报错。</para>
        /// <para><c>0</c> 是"确知没有任何服务"，不是"没下发"——这个区别决定山贼头目会不会长出对话按钮。</para>
        /// </summary>
        public static bool HasServiceOrFallback(int serverServiceFlags, uint bit, int npcId, string? modelKey)
            => serverServiceFlags >= 0 ? (serverServiceFlags & (int)bit) != 0 : HasService(npcId, modelKey, bit);

        private static bool HasService(int npcId, string? modelKey, uint bit)
        {
            EnsureLoaded();

            Entry hit;
            if (npcId > 0 && ByNpcId.TryGetValue(npcId, out hit)) return (hit.Services & bit) != 0;
            if (!string.IsNullOrEmpty(modelKey) && ByModelKey.TryGetValue(modelKey!, out hit)) return (hit.Services & bit) != 0;
            return false;
        }

        private static uint ReadUInt(System.Text.Json.JsonElement e, string name, uint fallback)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.Number)
                return fallback;
            return child.GetUInt32();
        }

        private static string ReadString(System.Text.Json.JsonElement e, string name)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.String)
                return string.Empty;
            return child.GetString() ?? string.Empty;
        }

        private static int ReadInt(System.Text.Json.JsonElement e, string name, int fallback)
        {
            System.Text.Json.JsonElement child;
            if (!e.TryGetProperty(name, out child) || child.ValueKind != System.Text.Json.JsonValueKind.Number)
                return fallback;
            return child.GetInt32();
        }
    }
}
