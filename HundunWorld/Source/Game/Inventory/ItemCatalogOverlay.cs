using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using Game.Character.Attributes;
using HundunWorld.Game.Equipment;

namespace HundunWorld.Game.Inventory
{
    /// <summary>
    /// 客户端物品目录覆盖层：读物品编辑器导出的 <c>Data/Items/client_items.json</c>，
    /// 命中则取代硬编码目录，未命中回退 <see cref="ItemCatalog"/> 的既有解析链。
    /// <para>
    /// <b>为什么是覆盖层而不是重写目录</b>：硬编码表（装备 23 条、材料 41 条、消耗品/宝石/秘籍）是
    /// 线上已经能跑的那一份；直接把编辑器数据当唯一源会让"还没导表的新机器"整包物品变成"神秘物品"。
    /// 覆盖层的回退是**逐条**的：表里有的用表，没有的照旧——所以"表缺失"与"表没更新"在界面上
    /// 分别表现为"全回退"和"部分新名"，不会把一件物品变成未知。
    /// </para>
    /// <para>
    /// <b>查找顺序</b>：<see cref="ItemCatalog.Resolve"/> 先问本层 → 命中即返回 → 未命中走原链。
    /// 因此编辑器改的名字/图标/属性在导出后立刻生效，而编辑器没管过的物品行为零变化。
    /// </para>
    /// </summary>
    public static class ItemCatalogOverlay
    {
        /// <summary>表文件（相对工程根）。由物品编辑器/CLI 从库里导出，随包发布。</summary>
        public const string RelativeTablePath = "Data/Items/client_items.json";

        /// <summary>热重载检查间隔（毫秒）。编辑器里"保存→导出"后不必重启游戏。</summary>
        private const long ReloadCheckIntervalMs = 1000;

        private static readonly Dictionary<int, ItemDefinition> Cache = new Dictionary<int, ItemDefinition>();
        private static readonly Dictionary<int, string> SetNames = new Dictionary<int, string>();
        private static bool _loaded;
        private static string _source = "未加载";
        private static long _nextReloadCheckTick;
        private static DateTime _lastWriteUtc;

        /// <summary>已加载的条目数（诊断与自检用；0 表示表缺失或为空，全部回退硬编码）。</summary>
        public static int Count => Cache.Count;

        /// <summary>表来源（诊断用：路径 + 条数，或失败原因）。</summary>
        public static string Source => _source;

        /// <summary>强制重载（重新导出后调用；正常情况首次访问即加载）。</summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        /// <summary>
        /// 尝试用覆盖层解析模板 id。命中返回 true 且带着已构造好的定义；
        /// 未命中返回 false，调用方继续走硬编码链。
        /// </summary>
        public static bool TryResolve(int templateId, out ItemDefinition definition)
        {
            EnsureLoaded();
            MaybeReloadOnFileChange();
            return Cache.TryGetValue(templateId, out definition);
        }

        /// <summary>
        /// 文件变了就重载并清空 <see cref="ItemCatalog"/> 的缓存。
        /// <para>为什么必须做：编辑器"保存到库 → 导出客户端表"是**运行中**发生的；
        /// 覆盖层只在首次访问时读盘，<c>ItemCatalog</c> 又按 id 缓存，不重载的话
        /// "导出成功、游戏里还是旧名字"。节流 1 秒，避免每次 Resolve 都打一次磁盘。</para>
        /// </summary>
        private static void MaybeReloadOnFileChange()
        {
            var now = Environment.TickCount64;
            if (now < _nextReloadCheckTick) return;
            _nextReloadCheckTick = now + ReloadCheckIntervalMs;

            var path = TablePath();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!File.Exists(path)) return;
                var write = File.GetLastWriteTimeUtc(path);
                if (write == _lastWriteUtc) return;

                Cache.Clear();
                SetNames.Clear();
                _loaded = false;
                ItemCatalog.InvalidateCache();
                EnsureLoaded();
                Debug.Log("[ItemCatalogOverlay] 检测到内容表更新，已重载（" + Cache.Count + " 条）");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ItemCatalogOverlay] 热重载失败：" + ex.GetBaseException().Message);
            }
        }

        private static string TablePath()
        {
            var root = Globals.ProjectFolder;
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(root, RelativeTablePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            Cache.Clear();
            SetNames.Clear();
            _source = "未加载";

            var path = TablePath();
            if (string.IsNullOrEmpty(path)) return;

            if (!File.Exists(path))
            {
                // 表不存在是**正常状态**：全新工程、还没点过"导出客户端表"。
                _source = "表不存在：" + path;
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                        || !doc.RootElement.TryGetProperty("items", out var items)
                        || items.ValueKind != System.Text.Json.JsonValueKind.Array)
                    {
                        _source = "缺少 items 数组：" + path;
                        return;
                    }

                    // 先读套装名：物品行要用 setId 去查名字，顺序反了会拿到空名。
                    if (doc.RootElement.TryGetProperty("sets", out var sets)
                        && sets.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var set in sets.EnumerateArray())
                        {
                            var setId = ReadInt(set, "setId", 0);
                            var setName = ReadString(set, "name");
                            if (setId > 0) SetNames[setId] = setName;
                        }
                    }

                    foreach (var item in items.EnumerateArray())
                    {
                        var definition = ToDefinition(item);
                        if (definition == null) continue;
                        Cache[definition.TemplateId] = definition;
                    }
                }
                _lastWriteUtc = File.GetLastWriteTimeUtc(path);
                _source = path + "（" + Cache.Count + " 条）";
                if (Cache.Count > 0)
                    Debug.Log("[ItemCatalogOverlay] 已载入 " + Cache.Count + " 条编辑器物品：" + path);
            }
            catch (Exception ex)
            {
                Cache.Clear();
                _source = "解析失败：" + ex.GetBaseException().Message;
                Debug.LogWarning("[ItemCatalogOverlay] 物品内容表解析失败，全部回退硬编码目录：" + _source);
            }
        }

        /// <summary>JSON 一条 → 客户端统一展示定义。</summary>
        private static ItemDefinition ToDefinition(System.Text.Json.JsonElement item)
        {
            var templateId = ReadInt(item, "templateId", 0);
            if (templateId <= 0) return null;

            var name = ReadString(item, "name");
            var itemClass = ReadInt(item, "itemClass", -1);
            var inventoryType = ReadInt(item, "inventoryType", 0);

            // 防御：装备类没有部位就**不要覆盖**硬编码目录。导出器已经会挡，
            // 但手工改过的表/旧版本导出的表仍可能带进来——一条坏记录会让那件装备穿不上。
            if ((itemClass == 0 || itemClass == 1 || itemClass == 2) && inventoryType <= 0)
                return null;
            if (itemClass == 8) return null;   // 背包槽位是硬编码的，不接

            // 宝石按**号段**识别（库里 item_type 是 3=消耗品，与丹药同类，只看大类会认错）。
            var isGem = templateId >= 60001 && templateId <= 69999;
            // 910000..919999 是五行灵材/信物：库里 item_type 可能是装备类，但语义是材料。
            var isWuxingMaterial = templateId >= 910000 && templateId <= 919999;
            var isAppearance = inventoryType == 16;
            var isBook = itemClass == 6;
            var isEquip = itemClass == 0 || itemClass == 1 || itemClass == 2;
            var isConsumable = itemClass == 3 && !isGem;
            var isMaterial = (itemClass == 4 && !isAppearance) || isWuxingMaterial;
            var isQuest = itemClass == 5;
            var isRecipe = itemClass == 7;
            if (!isGem && !isAppearance && !isBook && !isEquip && !isConsumable && !isMaterial && !isQuest && !isRecipe)
                return null;
            if (isEquip && !isWuxingMaterial && inventoryType <= 0)
                return null;
            if (isBook && (templateId < 800001 || templateId > 899999))
                return null;

            var gemElement = ReadInt(item, "gemElement", -1);
            var definition = new ItemDefinition
            {
                TemplateId = templateId,
                Name = string.IsNullOrEmpty(name) ? "神秘物品" : name,
                Glyph = BuildGlyph(name, isGem, gemElement),
                Description = ReadString(item, "description"),
                Kind = isWuxingMaterial ? ItemKind.Material
                    : isAppearance || isEquip ? ItemKind.Equipment
                    : isGem ? ItemKind.Gem
                    : isBook ? ItemKind.Book
                    : isConsumable ? ItemKind.Consumable
                    : isMaterial ? ItemKind.Material
                    : isQuest ? ItemKind.Quest
                    : ItemKind.Unknown,
                Quality = Clamp(ReadInt(item, "quality", 0), 0, 6),
                EquipSlot = ToEquipSlot(inventoryType),
                TypeName = isWuxingMaterial ? "灵材/信物"
                    : TypeNameForItem(isAppearance, isGem, isConsumable, isMaterial, isQuest, isRecipe,
                        inventoryType, itemClass),
                GemSlots = 0,
                // 秘籍：模板 id 就是 800000+技能 id（客户端/服务端同一约定）。
                // 不填它，覆盖层会把"可研读"的秘籍变成一本不能学的书。
                TeachesSkillId = isBook ? templateId - 800000 : 0,
                HpRestore = ReadInt(item, "hpRestore", 0),
                EnergyRestore = ReadInt(item, "energyRestore", 0),
                GemElement = isGem ? ToWuxingElement(gemElement) : null,
                GemAffinityBonus = isGem ? ReadInt(item, "gemAffinity", 0) : 0,
            };

            var element = ReadInt(item, "element", 5);
            if (element >= 0 && element <= 4)
            {
                definition.Element = ElementName(element);
                definition.ElementColor = ElementColor(element);
            }

            var setId = ReadInt(item, "setId", 0);
            if (setId > 0)
            {
                definition.SetId = setId;
                definition.SetName = SetNames.TryGetValue(setId, out var setName) ? setName : string.Empty;
            }

            if (item.TryGetProperty("stats", out var stats) && stats.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var stat in stats.EnumerateArray())
                {
                    var statName = ReadString(stat, "name");
                    var value = ReadInt(stat, "value", 0);
                    if (string.IsNullOrEmpty(statName) || value == 0) continue;
                    definition.Stats.Add((statName, value >= 0 ? "+" + value : value.ToString()));
                }
            }

            if (item.TryGetProperty("sockets", out var sockets) && sockets.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var count = 0;
                foreach (var _ in sockets.EnumerateArray()) count++;
                definition.GemSlots = count;
            }

            return definition;
        }

        private static string BuildGlyph(string name, bool isGem, int gemElement)
        {
            if (!string.IsNullOrEmpty(name)) return name.Substring(0, 1);
            if (isGem)
            {
                var element = ElementName(gemElement);
                return string.IsNullOrEmpty(element) ? "珠" : element;
            }
            return "？";
        }

        private static string TypeNameForItem(bool appearance, bool gem, bool consumable, bool material,
            bool quest, bool recipe, int inventoryType, int itemClass)
        {
            if (appearance) return "外观";
            if (gem) return "宝石";
            if (consumable) return "丹药";
            if (material) return "材料";
            if (quest) return "任务物品";
            if (recipe) return "配方";
            return TypeNameOfSlot(inventoryType, itemClass);
        }

        private static WuxingElement? ToWuxingElement(int element)
        {
            switch (element)
            {
                case 0: return WuxingElement.Metal;
                case 1: return WuxingElement.Wood;
                case 2: return WuxingElement.Water;
                case 3: return WuxingElement.Fire;
                case 4: return WuxingElement.Earth;
                default: return null;
            }
        }

        private static ItemKind ToKind(int itemClass)
        {
            switch (itemClass)
            {
                case 0:
                case 1:
                case 2:
                case 9: return ItemKind.Equipment;
                case 3: return ItemKind.Consumable;
                case 4: return ItemKind.Material;
                case 5: return ItemKind.Quest;
                case 6:
                case 7: return ItemKind.Book;
                default: return ItemKind.Unknown;
            }
        }

        /// <summary>inventory_type 1..16 → 客户端 EquipmentSlot（17=背包不占槽位，返回 null）。</summary>
        private static EquipmentSlot? ToEquipSlot(int inventoryType)
        {
            if (inventoryType >= 1 && inventoryType <= 16)
                return (EquipmentSlot)(inventoryType - 1);
            return null;
        }

        private static string TypeNameOfSlot(int inventoryType, int itemClass)
        {
            // 历史 item_type 与部位可能不一致（例：10001"默认衣服"的 item_type=0=武器），
            // 类型名以**部位**为准：玩家看到的是"这件戴在哪儿"，不是早年录入的大类字段。
            switch (inventoryType)
            {
                case 1: case 4: case 5: case 6: case 7: case 8: return "防具";   // 头/衣/腰/腿/脚
                case 3: case 15: return "防具";                                   // 肩/面
                case 9: case 10: return "武器";
                case 2: case 11: case 12: case 13: case 14: return "饰品";        // 颈/戒/腕
                default: return TypeNameOf(itemClass, inventoryType);
            }
        }

        private static string TypeNameOf(int itemClass, int inventoryType)
        {
            if (itemClass == 0) return "武器";
            if (itemClass == 1) return inventoryType == 16 ? "外观" : "防具";
            if (itemClass == 2) return "饰品";
            if (itemClass == 3) return "丹药";
            if (itemClass == 4) return "材料";
            if (itemClass == 5) return "任务物品";
            if (itemClass == 6) return "秘籍";
            if (itemClass == 7) return "配方";
            if (itemClass == 8) return inventoryType == 16 ? "背包" : "宝箱";
            if (itemClass == 9) return "外观";
            return "未知";
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

        private static string ElementName(int element)
        {
            switch (element)
            {
                case 0: return "金";
                case 1: return "木";
                case 2: return "水";
                case 3: return "火";
                case 4: return "土";
                default: return null;
            }
        }

        private static Color ElementColor(int element)
        {
            switch (element)
            {
                case 0: return new Color(1f, 0.84f, 0f);
                case 1: return new Color(0f, 0.7f, 0.2f);
                case 2: return new Color(0f, 0.5f, 1f);
                case 3: return new Color(1f, 0.25f, 0f);
                default: return new Color(0.8f, 0.6f, 0.2f);
            }
        }

        private static string ReadString(System.Text.Json.JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var child)
                || child.ValueKind != System.Text.Json.JsonValueKind.String) return string.Empty;
            return child.GetString() ?? string.Empty;
        }

        private static int ReadInt(System.Text.Json.JsonElement element, string name, int fallback)
        {
            if (!element.TryGetProperty(name, out var child)
                || child.ValueKind != System.Text.Json.JsonValueKind.Number) return fallback;
            return child.GetInt32();
        }
    }
}
