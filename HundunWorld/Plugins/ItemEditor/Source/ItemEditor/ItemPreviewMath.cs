using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ItemEditor
{
    /// <summary>tooltip 一行（与内核 <c>ItemCore.ItemTooltipLine</c> 同形）。</summary>
    public struct ItemTooltipPreviewLine
    {
        public string Kind;
        public string Text;
    }

    /// <summary>
    /// 插件侧的本地即时预览：CLI 没编好/没连上库时，界面仍然要能显示"这件装备大概什么强度"。
    /// <para>
    /// <b>这里的表与公式是内核 <c>Horizon.Game.Sim/Items/ItemCore.cs</c> 的字面副本</b>。
    /// 它不参与写库判定（写库前一定走 CLI 校验），但会显示在屏幕上，所以<b>漂了就是骗策划</b>。
    /// <c>ItemEditorContractTests</c> 按源文本逐表对账（品质系数、品质色、部位名、属性名、表长）。
    /// </para>
    /// </summary>
    public static class ItemPreviewMath
    {
        /// <summary>与内核 <c>ItemCore.Version</c> 对齐；内核改口径时这里必须同步。</summary>
        public const int CoreVersion = 1;

        public const int MaxItemLevel = 300;
        public const int MaxRequiredLevel = 175;
        public const int MaxStatLines = 10;
        public const int MaxDamageLines = 2;
        public const int MaxSockets = 3;
        public const int MaxSpellLines = 5;
        public const int MaxStackLimit = 9999;

        public static readonly string[] QualityNames = { "普通", "优秀", "精良", "史诗", "传说", "神器", "神话" };

        public static readonly string[] QualityColorHexes =
        {
            "#FFFFFF", "#1EFF00", "#0070DD", "#A335EE", "#FF8000", "#E6CC80", "#FF2020",
        };

        public static readonly int[] QualityMultiplierBpsTable = { 7_000, 9_000, 11_000, 13_000, 16_000, 20_000, 24_000 };

        public static readonly string[] InventoryTypeNames =
        {
            "不装备", "头部", "颈部", "肩部", "背部", "身体", "腰部", "腿部", "脚部",
            "主手", "副手", "右手指", "左手指", "右腕", "左腕", "面部", "外观", "背包",
        };

        public static readonly string[] ClassNames =
        {
            "武器", "防具", "饰品", "消耗品", "材料", "任务物品", "秘籍", "配方", "宝箱/背包", "外观",
        };

        public static readonly string[] StatTypeNames =
        {
            "未知属性",
            "力量", "敏捷", "智力", "体质", "幸运",
            "攻击力", "防御力", "魔法攻击", "魔法防御",
            "暴击率", "暴击伤害", "命中率", "闪避率",
            "最大生命", "最大魔法", "生命回复", "魔法回复",
            "移动速度", "攻击速度", "施法速度",
            "金行攻击", "木行攻击", "水行攻击", "火行攻击", "土行攻击",
            "金行防御", "木行防御", "水行防御", "火行防御", "土行防御",
            "金行穿透", "木行穿透", "水行穿透", "火行穿透", "土行穿透",
            "金行亲和", "木行亲和", "水行亲和", "火行亲和", "土行亲和",
            "精力",
        };

        public static string QualityName(int quality) => At(QualityNames, quality, "未知");

        public static string QualityColorHex(int quality) => At(QualityColorHexes, quality, "#FFFFFF");

        public static int QualityMultiplierBps(int quality)
            => quality >= 0 && quality < QualityMultiplierBpsTable.Length ? QualityMultiplierBpsTable[quality] : 7_000;

        public static string InventoryTypeName(int inventoryType) => At(InventoryTypeNames, inventoryType, "不装备");

        public static string ClassName(int itemClass) => At(ClassNames, itemClass, "未知");

        public static string StatTypeName(int type) => At(StatTypeNames, type, "未知属性");

        public static bool IsPercentStat(int type)
            => type == 10 || type == 12 || type == 13 || type == 18 || type == 19 || type == 20;

        private static string At(string[] table, int index, string fallback)
            => index >= 0 && index < table.Length ? table[index] : fallback;

        /// <summary>与内核同公式：ilvl 与品质的单调函数。</summary>
        public static int StatBudget(int itemLevel, int quality)
        {
            if (itemLevel <= 0) return 0;
            var ilvl = Math.Min(itemLevel, MaxItemLevel);
            var baseValue = (long)ilvl * 5L + (long)ilvl * ilvl / 4L;
            return (int)Math.Max(1L, baseValue * QualityMultiplierBps(quality) / 10_000L);
        }

        public static int ArmorFromBudget(int budget, bool shield) => budget * (shield ? 4 : 3) / 2;

        public static int WeaponDps(int minDamage, int maxDamage, int delayMs)
        {
            if (delayMs <= 0) return 0;
            var low = Math.Min(minDamage, maxDamage);
            var high = Math.Max(minDamage, maxDamage);
            return (int)Math.Round((low + high) / 2.0 * 1000.0 / delayMs, MidpointRounding.AwayFromZero);
        }

        public static int DefaultSellPrice(int buyPrice) => buyPrice <= 0 ? 0 : Math.Max(1, buyPrice / 4);

        public static int DefaultBuyPrice(int sellPrice) => sellPrice <= 0 ? 0 : sellPrice * 4;

        public static string PriceText(int copper)
        {
            if (copper <= 0) return "0";
            var gold = copper / 10000;
            var rest = copper % 10000;
            var silver = rest / 100;
            var bronze = rest % 100;
            var sb = new StringBuilder();
            if (gold > 0) sb.Append(gold.ToString(CultureInfo.InvariantCulture)).Append("金");
            if (silver > 0) sb.Append(silver.ToString(CultureInfo.InvariantCulture)).Append("银");
            if (bronze > 0 || sb.Length == 0) sb.Append(bronze.ToString(CultureInfo.InvariantCulture)).Append("铜");
            return sb.ToString();
        }

        public static int SlotIndexOf(int inventoryType)
            => inventoryType >= 1 && inventoryType <= 16 ? inventoryType - 1 : -1;

        public static string BindTypeName(int bindType)
        {
            switch (bindType)
            {
                case 1: return "拾取后绑定";
                case 2: return "装备后绑定";
                case 3: return "使用后绑定";
                default: return "不绑定";
            }
        }

        /// <summary>本地 tooltip：行序与内核 <c>ItemCore.BuildTooltip</c> 相同。</summary>
        public static List<ItemTooltipPreviewLine> BuildTooltip(ItemDraftModel draft, int setPieces, int playerLevel)
        {
            var lines = new List<ItemTooltipPreviewLine>();
            if (draft == null) return lines;
            Add(lines, "title", draft.Name);
            Add(lines, "quality", QualityName(draft.Quality) + " · " + ClassName(draft.ItemClass)
                                         + (draft.ItemLevel > 0 ? "（物品等级 " + draft.ItemLevel + "）" : string.Empty));
            if (draft.BindType != 0) Add(lines, "bind", BindTypeName(draft.BindType));
            if (draft.InventoryType != 0) Add(lines, "slot", InventoryTypeName(draft.InventoryType));
            if (draft.IsUnique) Add(lines, "unique", "唯一");

            foreach (var damage in draft.Damage ?? new List<ItemDamageModel>())
            {
                if (damage.DamageMax <= 0 && damage.DamageMin <= 0) continue;
                var text = damage.DamageMin + " - " + damage.DamageMax + " 点伤害";
                if (draft.AttackDelayMs > 0)
                    text += "　速度 " + (draft.AttackDelayMs / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)
                            + "　每秒伤害 " + WeaponDps(damage.DamageMin, damage.DamageMax, draft.AttackDelayMs);
                Add(lines, "damage", text);
            }

            if (draft.Armor > 0) Add(lines, "armor", "护甲 +" + draft.Armor);
            if (draft.Block > 0) Add(lines, "armor", "格挡 +" + draft.Block);

            foreach (var stat in draft.Stats ?? new List<ItemStatModel>())
            {
                if (stat.StatType <= 0 || stat.StatValue == 0) continue;
                var name = StatTypeName(stat.StatType);
                if (name == "未知属性") name += "（#" + stat.StatType + "）";
                var value = stat.StatValue >= 0 ? "+" + stat.StatValue : stat.StatValue.ToString();
                Add(lines, "stat", name + " " + value + (IsPercentStat(stat.StatType) ? "%" : string.Empty));
            }

            if (draft.Sockets != null && draft.Sockets.Count > 0)
            {
                var sb = new StringBuilder("插槽：");
                for (var i = 0; i < draft.Sockets.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(SocketColorName(draft.Sockets[i].SocketColor));
                }
                Add(lines, "socket", sb.ToString());
            }

            if (draft.SetId > 0)
            {
                var setName = draft.Set != null && !string.IsNullOrWhiteSpace(draft.Set.Name)
                    ? "：" + draft.Set.Name
                    : " #" + draft.SetId;
                Add(lines, "set", "套装" + setName + (setPieces > 0 ? "（已穿 " + setPieces + " 件）" : string.Empty));
                if (draft.Set?.Tiers != null)
                {
                    foreach (var tier in draft.Set.Tiers)
                    {
                        if (tier == null || tier.Pieces <= 0) continue;
                        var text = "（" + tier.Pieces + "件）";
                        if (!string.IsNullOrWhiteSpace(tier.Description))
                        {
                            text += tier.Description;
                        }
                        else if (tier.Stats != null && tier.Stats.Count > 0)
                        {
                            for (var i = 0; i < tier.Stats.Count; i++)
                            {
                                if (i > 0) text += "，";
                                var stat = tier.Stats[i];
                                text += StatTypeName(stat.StatType) + " "
                                        + (stat.StatValue >= 0 ? "+" + stat.StatValue : stat.StatValue.ToString());
                            }
                        }
                        else
                        {
                            text += "（无属性）";
                        }
                        Add(lines, setPieces >= tier.Pieces ? "set-active" : "set-inactive", text);
                    }
                }
            }
            if (draft.LevelRequire > 0) Add(lines, "req", "需要等级 " + draft.LevelRequire);
            if (draft.RealmRequire > 0) Add(lines, "req", "需要境界 " + draft.RealmRequire);
            if (playerLevel > 0 && draft.LevelRequire > playerLevel) Add(lines, "req", "等级不足，无法使用");
            if (draft.SellPrice > 0) Add(lines, "price", "售价：" + PriceText(draft.SellPrice));
            if (draft.MaxStack > 1) Add(lines, "stack", "可堆叠 " + draft.MaxStack);
            if (!string.IsNullOrWhiteSpace(draft.Description)) Add(lines, "desc", draft.Description);
            return lines;
        }

        private static void Add(List<ItemTooltipPreviewLine> lines, string kind, string text)
            => lines.Add(new ItemTooltipPreviewLine { Kind = kind, Text = text ?? string.Empty });

        public static string SocketColorName(int color)
        {
            switch (color)
            {
                case 0: return "多彩";
                case 1: return "红";
                case 2: return "黄";
                case 3: return "蓝";
                default: return "无色";
            }
        }
    }
}
