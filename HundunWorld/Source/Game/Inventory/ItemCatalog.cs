using FlaxEngine;
using Game.Character.Attributes;
using Game.Equipment.Material;
using HundunWorld.Game.Equipment;
using System.Collections.Generic;

namespace HundunWorld.Game.Inventory
{
    /// <summary>
    /// 物品大类（目录解析结果，与 UI 筛选/按钮启用态一一对应）。
    /// </summary>
    public enum ItemKind
    {
        Unknown = 0,
        Equipment = 1,
        Consumable = 2,
        Material = 3,
        Quest = 4,
        Gem = 5,
    }

    /// <summary>
    /// 统一的物品展示定义：由模板 ID 解析出的名称/图标字/类型/槽位/品质/效果等。
    /// 纯只读数据，由 <see cref="ItemCatalog.Resolve"/> 产出。
    /// </summary>
    public sealed class ItemDefinition
    {
        public int TemplateId;
        public string Name = "神秘物品";
        public string Glyph = "？";
        /// <summary>类型显示名：武器 / 防具 / 饰品 / 背包 / 丹药 / 材料 / 未知。</summary>
        public string TypeName = "未知";
        public ItemKind Kind = ItemKind.Unknown;
        public EquipmentSlot? EquipSlot;
        /// <summary>0..4，与 InkWashTheme.InkQuality 序数一致。</summary>
        public int Quality;
        public string Description = "";
        /// <summary>装备开放宝石孔数（品质+1，夹 1..5）；非装备为 0。</summary>
        public int GemSlots;
        /// <summary>详情/Tooltip 属性行（标签+值）。</summary>
        public List<(string Label, string Value)> Stats = new List<(string, string)>();
        /// <summary>五行名（金/木/水/火/土），无则 null。</summary>
        public string Element;
        public Color ElementColor;
        /// <summary>宝石的结构化五行元素（仅宝石有值），供属性聚合桥计入角色五行。</summary>
        public WuxingElement? GemElement;
        /// <summary>单颗宝石的五行亲和加成值（仅宝石有值）。</summary>
        public int GemAffinityBonus;
        public float HpRestore;
        public float EnergyRestore;

        public bool IsEquippable => Kind == ItemKind.Equipment && EquipSlot.HasValue;
        public bool IsUsable => Kind == ItemKind.Consumable;
        public bool IsGem => Kind == ItemKind.Gem;
    }

    /// <summary>
    /// 客户端物品模板目录：把服务端背包里只有编号的 TemplateId 解析成可渲染、可判定的定义。
    /// <para>
    /// 解析顺序：<b>消耗品表 → 装备表 → 材料表 → 未知回退</b>。
    /// 注意装备表与材料表存在重叠 ID（两边的 10001 等不同义），当前权威背包数据源
    /// （本地迁移种子 + DS 掉落）中重叠 ID 均来自装备语义，故装备优先；
    /// 掉落产生的无法解析 ID 回退为"未知"占位，不允许穿戴/使用。
    /// </para>
    /// </summary>
    public static class ItemCatalog
    {
        private static readonly Dictionary<int, ItemDefinition> _cache = new Dictionary<int, ItemDefinition>();
        private static bool _consumablesRegistered;

        /// <summary>金创药模板 ID（新手种子物品）。</summary>
        public const int HealingSalveTemplateId = 50001;
        /// <summary>回气丹模板 ID。</summary>
        public const int EnergyPillTemplateId = 50002;

        /// <summary>五行宝石模板 ID（金/木/水/火/土）。</summary>
        public const int GemMetalId = 60001;
        public const int GemWoodId = 60002;
        public const int GemWaterId = 60003;
        public const int GemFireId = 60004;
        public const int GemEarthId = 60005;

        /// <summary>服务端 ItemInfo.Gems 里空槽的约定值，客户端与服务端一致。</summary>
        public const int EmptyGemSlot = -1;

        /// <summary>装备开放孔数上限（与 <c>InventoryGrain.MaxGemSlots</c> 对齐）。</summary>
        public const int MaxGemSlots = 5;

        /// <summary>解析模板 ID；永不返回 null，未注册物品返回 Unknown 定义。</summary>
        public static ItemDefinition Resolve(int templateId)
        {
            EnsureConsumables();
            if (_cache.TryGetValue(templateId, out var cached))
                return cached;

            var def = Build(templateId);
            _cache[templateId] = def;
            return def;
        }

        private static void EnsureConsumables()
        {
            if (_consumablesRegistered) return;
            _consumablesRegistered = true;

            RegisterGem(GemMetalId, "金锐珠", WuxingElement.Metal);
            RegisterGem(GemWoodId, "木灵珠", WuxingElement.Wood);
            RegisterGem(GemWaterId, "水华珠", WuxingElement.Water);
            RegisterGem(GemFireId, "赤焰珠", WuxingElement.Fire);
            RegisterGem(GemEarthId, "厚土珠", WuxingElement.Earth);

            _cache[HealingSalveTemplateId] = new ItemDefinition
            {
                TemplateId = HealingSalveTemplateId,
                Name = "金创药",
                Glyph = "药",
                TypeName = "丹药",
                Kind = ItemKind.Consumable,
                Quality = 2,
                Description = "外伤圣药，服用后恢复 300 点生命。",
                HpRestore = 300f,
                Stats = { ("效果", "生命 +300") },
            };
            _cache[EnergyPillTemplateId] = new ItemDefinition
            {
                TemplateId = EnergyPillTemplateId,
                Name = "回气丹",
                Glyph = "丹",
                TypeName = "丹药",
                Kind = ItemKind.Consumable,
                Quality = 2,
                Description = "培元固本，服用后恢复 200 点内力。",
                EnergyRestore = 200f,
                Stats = { ("效果", "内力 +200") },
            };
        }

        private static void RegisterGem(int templateId, string name, WuxingElement element)
        {
            _cache[templateId] = new ItemDefinition
            {
                TemplateId = templateId,
                Name = name,
                Glyph = ElementName(element) ?? "珠",
                TypeName = "宝石",
                Kind = ItemKind.Gem,
                Quality = 3,
                Description = $"五行属{ElementName(element)}的宝石，镶嵌后提升对应五行亲和 {GemAffinityPerGem} 点。",
                Element = ElementName(element),
                ElementColor = ElementColor(element),
                GemElement = element,
                GemAffinityBonus = GemAffinityPerGem,
                Stats = { ($"{ElementName(element)}亲和", "+" + GemAffinityPerGem) },
            };
        }

        /// <summary>单颗宝石的五行亲和加成（展示与属性聚合共用同一常量）。</summary>
        public const int GemAffinityPerGem = 20;

        private static ItemDefinition Build(int templateId)
        {
            var equipment = EquipmentDatabase.GetEquipment(templateId);
            if (equipment != null)
                return FromEquipment(equipment);

            var material = MaterialDatabase.GetMaterial(templateId);
            if (material != null)
                return FromMaterial(material);

            return new ItemDefinition { TemplateId = templateId };
        }

        private static ItemDefinition FromEquipment(EquipmentData equipment)
        {
            var slot = equipment.Slot;
            bool weapon = equipment.Type == EquipmentType.Weapon;
            bool handSlot = slot == EquipmentSlot.RightHand || slot == EquipmentSlot.LeftHand;

            var def = new ItemDefinition
            {
                TemplateId = equipment.Id,
                Name = equipment.Name,
                Glyph = SlotGlyph(slot),
                TypeName = TypeDisplayName(equipment.Type, slot),
                Kind = ItemKind.Equipment,
                EquipSlot = slot,
                Quality = equipment.Quality < 0 ? 0 : (equipment.Quality > 4 ? 4 : equipment.Quality),
                Description = equipment.Description ?? "",
                // 孔数规则：品质+1（普通1孔 … 传说5孔），上限与 ItemCatalog.MaxGemSlots 一致。
                GemSlots = equipment.Type == EquipmentType.Weapon || equipment.Type == EquipmentType.Body ||
                           equipment.Type == EquipmentType.Accessory
                    ? ClampSlots(equipment.Quality + 1)
                    : 0,
            };

            foreach (var kv in equipment.BaseStats)
            {
                def.Stats.Add((StatLabel(kv.Key), "+" + kv.Value.ToString("0.#")));
            }

            if (equipment.WuxingBonus != null && equipment.WuxingBonus.Count > 0)
            {
                var first = equipment.WuxingBonus.Keys;
                foreach (var element in first)
                {
                    def.Element = ElementName(element);
                    def.ElementColor = ElementColor(element);
                    break;
                }
            }
            return def;
        }

        private static ItemDefinition FromMaterial(MaterialData material)
        {
            int quality = material.Tier switch
            {
                MaterialTier.Basic => 0,
                MaterialTier.Intermediate => 1,
                MaterialTier.Advanced => 2,
                _ => 3,
            };
            var def = new ItemDefinition
            {
                TemplateId = material.MaterialId,
                Name = material.MaterialName,
                Glyph = GlyphOf(material.MaterialName, "材"),
                TypeName = "材料",
                Kind = ItemKind.Material,
                Quality = quality,
                Description = material.Description ?? "",
            };
            def.Element = ElementName(material.Element);
            def.ElementColor = MaterialElementColor(material.Element);
            return def;
        }

        // ── 显示映射 ──────────────────────────────────────────────

        private static string SlotGlyph(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Head => "头",
            EquipmentSlot.Neck => "链",
            EquipmentSlot.Shoulder => "肩",
            EquipmentSlot.Back => "披",
            EquipmentSlot.Body => "衣",
            EquipmentSlot.Waist => "腰",
            EquipmentSlot.Legs => "腿",
            EquipmentSlot.Feet => "脚",
            EquipmentSlot.RightHand => "武",
            EquipmentSlot.LeftHand => "副",
            EquipmentSlot.RightRing => "戒",
            EquipmentSlot.LeftRing => "戒",
            EquipmentSlot.RightWrist => "腕",
            EquipmentSlot.LeftWrist => "腕",
            EquipmentSlot.Face => "面",
            _ => "？",
        };

        private static string TypeDisplayName(EquipmentType type, EquipmentSlot slot) => type switch
        {
            EquipmentType.Weapon => "武器",
            EquipmentType.Bag => "背包",
            EquipmentType.Body => slot == EquipmentSlot.Body || slot == EquipmentSlot.Legs ||
                                  slot == EquipmentSlot.Feet || slot == EquipmentSlot.Head ||
                                  slot == EquipmentSlot.Face ? "防具" : "防具",
            _ => "饰品",
        };

        private static string StatLabel(string key) => key switch
        {
            "Attack" => "攻击力",
            "Defense" => "防御力",
            "HP" => "生命",
            "MP" => "内力",
            _ => key,
        };

        /// <summary>槽位显示名（空槽 Tooltip 占位用）。</summary>
        public static string SlotDisplayName(EquipmentSlot slot) => slot switch
        {
            EquipmentSlot.Head => "头部",
            EquipmentSlot.Neck => "颈部",
            EquipmentSlot.Shoulder => "肩部",
            EquipmentSlot.Back => "背部",
            EquipmentSlot.Body => "身体",
            EquipmentSlot.Waist => "腰部",
            EquipmentSlot.Legs => "腿部",
            EquipmentSlot.Feet => "脚部",
            EquipmentSlot.RightHand => "主手",
            EquipmentSlot.LeftHand => "副手",
            EquipmentSlot.RightRing => "右戒指",
            EquipmentSlot.LeftRing => "左戒指",
            EquipmentSlot.RightWrist => "右手腕",
            EquipmentSlot.LeftWrist => "左手腕",
            EquipmentSlot.Face => "面部",
            _ => "槽位",
        };

        private static string ElementName(WuxingElement element) => element switch
        {
            WuxingElement.Metal => "金",
            WuxingElement.Wood => "木",
            WuxingElement.Water => "水",
            WuxingElement.Fire => "火",
            WuxingElement.Earth => "土",
            _ => null,
        };

        private static Color ElementColor(WuxingElement element) => element switch
        {
            WuxingElement.Metal => new Color(1f, 0.84f, 0f),
            WuxingElement.Wood => new Color(0f, 0.7f, 0.2f),
            WuxingElement.Water => new Color(0f, 0.5f, 1f),
            WuxingElement.Fire => new Color(1f, 0.25f, 0f),
            _ => new Color(0.8f, 0.6f, 0.2f),
        };

        private static Color MaterialElementColor(WuxingElement element) => ElementColor(element);

        private static string GlyphOf(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            return name.Substring(0, 1);
        }

        private static int ClampSlots(int slots)
            => slots < 1 ? 1 : (slots > MaxGemSlots ? MaxGemSlots : slots);
    }
}
