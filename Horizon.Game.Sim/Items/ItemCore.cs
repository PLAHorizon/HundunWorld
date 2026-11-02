using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Horizon.Game.Sim.Items;

/// <summary>
/// 物品内核：号段、槽位、品质、属性预算、价格、战斗换算、插槽、词缀、套装、tooltip、校验。
/// <para>
/// <b>零存储、零框架依赖、确定性</b>：同一输入永远同一输出。CLI 的 preview-* 与文档里的
/// "编辑器显示值必须与运行时同源"全靠这一点，所以这里不允许出现 <c>DateTime.Now</c>、
/// 随机数发生器或数据库访问。
/// </para>
/// </summary>
public static class ItemCore
{
    /// <summary>内核版本。改变任何数值口径都要 +1，并同步插件侧的镜像常量。</summary>
    public const int Version = 1;

    /// <summary>角色等级上限（需求校验用）。与技能/怪物侧的 175 不同：本作装备需求按 175 封顶。</summary>
    public const int MaxRequiredLevel = 175;

    /// <summary>铜币换算：1 金 = 10000 铜，1 银 = 100 铜。</summary>
    public const int CopperPerSilver = 100;
    public const int CopperPerGold = 10000;

    // ---------------- 号段 ----------------

    /// <summary>
    /// 号段表（**唯一实现**，精确区间匹配，不靠级联阈值）。
    /// <para>区间取自盘上既有数据：外观 91001..91004（`EquipmentDatabase.AppearanceIdBase = 91000`）、
    /// 五行灵材/信物 910001..910015。两者数字近似但**是两段**——上一版把 910000+ 当外观，
    /// 会让 150 多件灵材被编辑器判成外观。</para>
    /// </summary>
    private static readonly (ItemSegment Segment, int Min, int Max)[] SegmentRanges =
    {
        (ItemSegment.Legacy, 1, 9999),
        (ItemSegment.Equipment, 10001, 49999),
        (ItemSegment.Consumable, 50001, 59999),
        (ItemSegment.Gem, 60001, 69999),
        (ItemSegment.Appearance, 91000, 91999),
        (ItemSegment.Affix, 900001, 909999),
        (ItemSegment.WuxingMaterial, 910000, 919999),
        (ItemSegment.Set, 920000, 929999),
        (ItemSegment.Material, 700001, 799999),
        (ItemSegment.Book, 800000, 899999),
    };

    /// <summary>按编号推断号段。号段表见 docs/ITEM_EDITOR_PLUGIN_DESIGN.md §3.2。</summary>
    public static ItemSegment SegmentOf(int templateId)
    {
        if (templateId <= 0) return ItemSegment.Invalid;
        foreach (var range in SegmentRanges)
        {
            if (templateId >= range.Min && templateId <= range.Max) return range.Segment;
        }
        return ItemSegment.Invalid;
    }

    public static bool IsInSegment(int templateId, ItemSegment segment) => SegmentOf(templateId) == segment;

    /// <summary>号段的闭区间（编辑器"新建"按钮用它找空号）。</summary>
    public static (int Min, int Max) RangeOf(ItemSegment segment)
    {
        foreach (var range in SegmentRanges)
        {
            if (range.Segment == segment) return (range.Min, range.Max);
        }
        return (0, 0);
    }

    public static string SegmentName(ItemSegment segment) => segment switch
    {
        ItemSegment.Legacy => "历史/杂项",
        ItemSegment.Equipment => "装备",
        ItemSegment.Consumable => "消耗品",
        ItemSegment.Gem => "宝石/镶嵌",
        ItemSegment.Material => "采集材料",
        ItemSegment.Book => "秘籍/配方",
        ItemSegment.Affix => "随机词缀",
        ItemSegment.Appearance => "外观",
        ItemSegment.Set => "套装",
        ItemSegment.WuxingMaterial => "五行灵材/信物",
        _ => "号段外",
    };

    // ---------------- 槽位 ----------------

    /// <summary>`inventory_type` 对应的战斗槽位序号（0..15）；非装备返回 -1。</summary>
    public static int EquipSlotIndex(int inventoryType)
        => inventoryType >= (int)InventoryType.Head && inventoryType <= (int)InventoryType.Appearance
            ? inventoryType - 1
            : -1;
    public static bool IsEquippable(int inventoryType)
        => EquipSlotIndex(inventoryType) >= 0 || inventoryType == (int)InventoryType.Bag;

    public static string SubTypeName(int itemClass, int subType)
    {
        if (itemClass == (int)ItemClass.Weapon)
            return subType switch
            {
                0 => "单手剑", 1 => "双手剑", 2 => "单手斧", 3 => "双手斧",
                4 => "匕首", 5 => "长柄武器", 6 => "法杖", 7 => "弓", 8 => "拳套", 9 => "奇门兵器",
                _ => "武器",
            };
        if (itemClass == (int)ItemClass.Armor)
            return subType switch
            {
                0 => "布甲", 1 => "皮甲", 2 => "锁甲", 3 => "板甲", 4 => "盾牌", 5 => "披风",
                _ => "防具",
            };
        if (itemClass == (int)ItemClass.Accessory)
            return subType switch
            {
                0 => "戒指", 1 => "项链", 2 => "护符", 3 => "腕饰",
                _ => "饰品",
            };
        if (itemClass == (int)ItemClass.Consumable)
            return subType switch
            {
                0 => "丹药", 1 => "食物", 2 => "卷轴", 3 => "药剂", 4 => "投掷物",
                _ => "消耗品",
            };
        if (itemClass == (int)ItemClass.Material) return subType switch { 0 => "矿石", 1 => "草药", 2 => "木材", 3 => "兽皮", _ => "材料" };
        if (itemClass == (int)ItemClass.Book) return "秘籍";
        if (itemClass == (int)ItemClass.Recipe) return "配方";
        if (itemClass == (int)ItemClass.Container) return "背包";
        return string.Empty;
    }

    // ---------------- 品质 ----------------

    // ---------------- 显示表（插件侧有一份逐字副本，契约测试按数组元素对账） ----------------

    /// <summary>品质名（下标 = 品质 0..6，0=普通；与客户端 InkQuality 同序）。</summary>
    public static readonly string[] QualityNames = { "普通", "优秀", "精良", "史诗", "传说", "神器", "神话" };

    /// <summary>品质色（RGBA hex，与客户端品质色同族：白/绿/蓝/紫/橙/金/红）。</summary>
    public static readonly string[] QualityColorHexes =
    {
        "#FFFFFF", "#1EFF00", "#0070DD", "#A335EE", "#FF8000", "#E6CC80", "#FF2020",
    };

    /// <summary>属性预算的品质系数（万分比，下标 = 品质 0..6）。<b>只允许递增</b>。</summary>
    public static readonly int[] QualityMultiplierBpsTable = { 7_000, 9_000, 11_000, 13_000, 16_000, 20_000, 24_000 };

    /// <summary>装备部位名（下标 = inventory_type 0..17）。</summary>
    public static readonly string[] InventoryTypeNames =
    {
        "不装备", "头部", "颈部", "肩部", "背部", "身体", "腰部", "腿部", "脚部",
        "主手", "副手", "右手指", "左手指", "右腕", "左腕", "面部", "外观", "背包",
    };

    /// <summary>大类名（下标 = item_class 0..9）。</summary>
    public static readonly string[] ClassNames =
    {
        "武器", "防具", "饰品", "消耗品", "材料", "任务物品", "秘籍", "配方", "宝箱/背包", "外观",
    };

    /// <summary>属性名（下标 = stat_type 0..41，0 = 未知）。</summary>
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

    /// <summary>属性预算的品质系数（万分比）。<b>只允许递增</b>：品质越高预算必须越大。</summary>
    public static int QualityMultiplierBps(int quality)
        => quality >= 0 && quality < QualityMultiplierBpsTable.Length ? QualityMultiplierBpsTable[quality] : 7_000;

    public static string InventoryTypeName(int inventoryType) => At(InventoryTypeNames, inventoryType, "不装备");

    public static string ClassName(int itemClass) => At(ClassNames, itemClass, "未知");

    public static string StatTypeName(int type) => At(StatTypeNames, type, "未知属性");

    private static string At(string[] table, int index, string fallback)
        => index >= 0 && index < table.Length ? table[index] : fallback;

    // ---------------- 属性预算与战斗换算 ----------------

    /// <summary>
    /// 属性点预算：物品等级与品质的函数。
    /// <para>形状取 WoW "ilvl 越高、品质越高，词条总量越大"，但不照抄其曲线（本作没有 statModifier_budget 表）。
    /// 严格性由内核测试锁：同品质 ilvl 递增 ⇒ 预算递增；同 ilvl 品质递增 ⇒ 预算递增。</para>
    /// </summary>
    public static int StatBudget(int itemLevel, int quality)
    {
        if (itemLevel <= 0) return 0;
        var ilvl = Math.Min(itemLevel, ItemSpec.MaxItemLevel);
        var baseValue = (long)ilvl * 5L + (long)ilvl * ilvl / 4L;
        return (int)Math.Max(1L, baseValue * QualityMultiplierBps(quality) / 10_000L);
    }

    /// <summary>护甲：由预算派生（盾牌更高：4/2 预算，普通防具 3/2 预算）。</summary>
    public static int ArmorFromBudget(int budget, bool shield) => budget * (shield ? 4 : 3) / 2;

    /// <summary>武器每秒伤害：两段伤害的平均值按攻击间隔折算。</summary>
    public static int WeaponDps(int minDamage, int maxDamage, int delayMs)
    {
        if (delayMs <= 0) return 0;
        var low = Math.Min(minDamage, maxDamage);
        var high = Math.Max(minDamage, maxDamage);
        var average = (low + high) / 2.0;
        return (int)Math.Round(average * 1000.0 / delayMs, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// 按权重把预算分到若干属性上（编辑器"按预算填充"按钮）。
    /// <para><b>总和恰好等于预算</b>：先给每条 1 点，再把 <c>预算 - 条数</c> 按权重分配，余数补第一项。
    /// 这样既不会出现 0 值条目（校验会拒），也不会四舍五入后对不上账。</para>
    /// <para>预算小于条目数时只保留前 <c>预算</c> 条、每条第 1 点——总点数不够分时，
    /// "多给几条 1 点"比"每条都四舍五入到 1 导致总数超预算"更可控。</para>
    /// </summary>
    public static ItemStatLine[] DistributeBudget(int budget, int[] statTypes, int[] weightsBps)
    {
        if (budget <= 0 || statTypes == null || statTypes.Length == 0) return Array.Empty<ItemStatLine>();
        weightsBps ??= Array.Empty<int>();

        if (budget < statTypes.Length)
        {
            var limited = new ItemStatLine[budget];
            for (var i = 0; i < budget; i++) limited[i] = new ItemStatLine { Type = statTypes[i], Value = 1 };
            return limited;
        }

        var totalWeight = 0L;
        for (var i = 0; i < statTypes.Length; i++)
            totalWeight += Math.Max(1, i < weightsBps.Length ? weightsBps[i] : 10_000);
        if (totalWeight <= 0) totalWeight = statTypes.Length;

        var remaining = budget - statTypes.Length;
        var result = new ItemStatLine[statTypes.Length];
        var distributed = 0;
        for (var i = 0; i < statTypes.Length; i++)
        {
            var weight = Math.Max(1, i < weightsBps.Length ? weightsBps[i] : 10_000);
            var extra = (int)((long)remaining * weight / totalWeight);
            result[i] = new ItemStatLine { Type = statTypes[i], Value = 1 + extra };
            distributed += extra;
        }

        // 余数全给第一项：保证总和精确等于预算。
        result[0].Value += remaining - distributed;
        return result;
    }

    // ---------------- 价格 ----------------

    /// <summary>WoW 口径：售价 = 买价 / 4（至少 1 铜）。买价为 0 时售价也是 0。</summary>
    public static int DefaultSellPrice(int buyPrice) => buyPrice <= 0 ? 0 : Math.Max(1, buyPrice / 4);

    public static int DefaultBuyPrice(int sellPrice) => sellPrice <= 0 ? 0 : sellPrice * 4;

    /// <summary>
    /// 从历史 <c>use_effect</c> JSON 里读回血/回气数值（客户端 <c>ItemDefinition.HpRestore/EnergyRestore</c> 的来源）。
    /// <para>键名容错：<c>hp/HP/hpRestore</c>、<c>mp/MP/mpRestore/energyRestore</c>；数字或数字字符串都认。
    /// 坏 JSON/缺键一律回 0，不抛异常——一件消耗品没写效果不该让整批导出失败。</para>
    /// </summary>
    public static void ParseRestoreFromUseEffect(string json, out int hpRestore, out int energyRestore)
    {
        hpRestore = 0;
        energyRestore = 0;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name.Trim();
                var value = ReadNumber(property.Value);
                if (value == 0) continue;
                if (key.Equals("hp", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("hpRestore", StringComparison.OrdinalIgnoreCase))
                    hpRestore = value;
                else if (key.Equals("mp", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("mpRestore", StringComparison.OrdinalIgnoreCase)
                         || key.Equals("energyRestore", StringComparison.OrdinalIgnoreCase))
                    energyRestore = value;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 脏数据当"没有效果"。
        }
    }

    private static int ReadNumber(System.Text.Json.JsonElement value)
    {
        if (value.ValueKind == System.Text.Json.JsonValueKind.Number)
        {
            if (value.TryGetInt32(out var integer)) return integer;
            if (value.TryGetDouble(out var number)
                && !double.IsNaN(number) && !double.IsInfinity(number)
                && number >= int.MinValue && number <= int.MaxValue)
                return (int)Math.Round(number, MidpointRounding.AwayFromZero);
            return 0;
        }
        if (value.ValueKind == System.Text.Json.JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 0;
    }

    public static string PriceText(int copper)
    {
        if (copper <= 0) return "0";
        var gold = copper / CopperPerGold;
        var rest = copper % CopperPerGold;
        var silver = rest / CopperPerSilver;
        var bronze = rest % CopperPerSilver;

        var sb = new StringBuilder();
        if (gold > 0) sb.Append(gold.ToString(CultureInfo.InvariantCulture)).Append("金");
        if (silver > 0) sb.Append(silver.ToString(CultureInfo.InvariantCulture)).Append("银");
        if (bronze > 0 || sb.Length == 0) sb.Append(bronze.ToString(CultureInfo.InvariantCulture)).Append("铜");
        return sb.ToString();
    }

    // ---------------- 插槽 ----------------

    public static string SocketColorName(int color) => color switch
    {
        (int)SocketColor.Meta => "多彩",
        (int)SocketColor.Red => "红",
        (int)SocketColor.Yellow => "黄",
        (int)SocketColor.Blue => "蓝",
        (int)SocketColor.None => "无色",
        _ => "未知",
    };

    /// <summary>
    /// 宝石能否插入该槽。规则与 WoW 一致：多彩槽只收多彩；普通槽收同色或无色；无色槽收任意。
    /// </summary>
    public static bool CanSocket(int gemColor, int socketColor)
    {
        if (socketColor == (int)SocketColor.None) return true;
        if (socketColor == (int)SocketColor.Meta) return gemColor == (int)SocketColor.Meta;
        return gemColor == socketColor || gemColor == (int)SocketColor.None;
    }

    // ---------------- 词缀 ----------------

    /// <summary>确定性抽取词缀下标。种子相同则结果相同（试算与线上重放同源）。</summary>
    public static int RollAffixIndex(int seed, int count)
    {
        if (count <= 1) return 0;
        var state = unchecked((uint)seed * 1_103_515_245u + 12_345u);
        return (int)(state % (uint)count);
    }

    /// <summary>确定性抽取词缀数值（闭区间）。</summary>
    public static int RollAffixValue(int seed, int min, int max)
    {
        if (max <= min) return min;
        var state = unchecked((uint)seed * 2_147_007u + 253_101_1u);
        var span = (uint)(max - min + 1);
        return min + (int)(state % span);
    }

    /// <summary>把词缀属性按 roll 值展开（同一词缀的每条属性共用一个 roll 比例）。</summary>
    public static ItemStatLine[] ApplyAffix(ItemAffixSpec affix, int seed)
    {
        if (affix?.Stats == null || affix.Stats.Length == 0) return Array.Empty<ItemStatLine>();
        var roll = RollAffixValue(seed, affix.ValueMin > 0 ? affix.ValueMin : 1,
            affix.ValueMax >= affix.ValueMin ? affix.ValueMax : affix.ValueMin);
        var result = new ItemStatLine[affix.Stats.Length];
        for (var i = 0; i < affix.Stats.Length; i++)
        {
            var template = affix.Stats[i];
            // 词缀行里的 Value 是"权重"，最终值 = 权重 × roll / 100
            result[i] = new ItemStatLine
            {
                Type = template.Type,
                Value = template.Value * roll / 100,
            };
        }
        return result;
    }

    // ---------------- 套装 ----------------

    /// <summary>已穿戴件数下生效的档位（每档只在其件数 ≤ 已穿戴时生效）。</summary>
    public static List<ItemSetBonusTier> ActiveTiers(ItemSetSpec set, int equippedPieces)
    {
        var active = new List<ItemSetBonusTier>();
        if (set?.Tiers == null || equippedPieces <= 0) return active;
        foreach (var tier in set.Tiers)
        {
            if (tier != null && tier.Pieces > 0 && tier.Pieces <= equippedPieces) active.Add(tier);
        }
        active.Sort((a, b) => a.Pieces.CompareTo(b.Pieces));
        return active;
    }

    /// <summary>生效档位的属性合计（同类型相加）。</summary>
    public static List<ItemStatLine> SetStatTotals(ItemSetSpec set, int equippedPieces)
    {
        var sums = new List<int>();
        var types = new List<int>();
        foreach (var tier in ActiveTiers(set, equippedPieces))
        {
            if (tier.Stats == null) continue;
            foreach (var line in tier.Stats)
            {
                var index = types.IndexOf(line.Type);
                if (index < 0)
                {
                    types.Add(line.Type);
                    sums.Add(line.Value);
                }
                else
                {
                    sums[index] += line.Value;
                }
            }
        }

        var result = new List<ItemStatLine>(types.Count);
        for (var i = 0; i < types.Count; i++) result.Add(new ItemStatLine { Type = types[i], Value = sums[i] });
        return result;
    }


    /// <summary>
    /// 属性类型 → <c>base_attributes</c> JSON 的键名（= <c>AttributeType</c> 成员名）。
    /// <para>空行返回 null。CLI 保存时用它派生 JSON，所以服务端 <c>ItemAttributeContract</c>
    /// 不需要认识 <see cref="ItemStatType"/> 这一层。</para>
    /// </summary>
    public static string AttributeKey(int type)
    {
        if (type <= 0 || type > (int)ItemStatType.Energy) return null;
        return ((ItemStatType)type).ToString();
    }

    /// <summary>是否为百分比属性（显示时带 %）。</summary>
    public static bool IsPercentStat(int type)
        => type is (int)ItemStatType.CriticalRate or (int)ItemStatType.HitRate
            or (int)ItemStatType.DodgeRate or (int)ItemStatType.MoveSpeed
            or (int)ItemStatType.AttackSpeed or (int)ItemStatType.CastSpeed;

    /// <summary>
    /// 历史 JSON 的短名别名（与 <c>ItemAttributeContract</c> 同一份口径）：
    /// 客户端旧表写的是 HP/MP/CritRate/裸元素名，服务端解析器也认这些。
    /// <b>不认它们，编辑器"打开再保存"会把这几条属性静默丢掉。</b>
    /// </summary>
    private static readonly Dictionary<string, int> AttributeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HP"] = (int)ItemStatType.MaxHealth,
        ["MP"] = (int)ItemStatType.MaxMana,
        ["CritRate"] = (int)ItemStatType.CriticalRate,
        ["CritDamage"] = (int)ItemStatType.CriticalDamage,
        ["Metal"] = (int)ItemStatType.MetalAffinity,
        ["Wood"] = (int)ItemStatType.WoodAffinity,
        ["Water"] = (int)ItemStatType.WaterAffinity,
        ["Fire"] = (int)ItemStatType.FireAffinity,
        ["Earth"] = (int)ItemStatType.EarthAffinity,
    };

    /// <summary>键名 → 属性类型（<see cref="AttributeKey"/> 的逆运算，含历史短名，大小写不敏感）。</summary>
    public static bool TryParseAttributeKey(string key, out int statType)
    {
        statType = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;
        var trimmed = key.Trim();
        if (AttributeAliases.TryGetValue(trimmed, out statType)) return true;
        for (var value = 1; value <= (int)ItemStatType.Energy; value++)
        {
            if (string.Equals(((ItemStatType)value).ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                statType = value;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 属性行 → <c>base_attributes</c> JSON（服务端 <c>ItemAttributeContract</c> 的输入）。
    /// <para>空集合写成 <c>{}</c>：库里的历史行也是这个形状，空串会让解析方多一条"是不是没填"的分支。</para>
    /// </summary>
    public static string BuildAttributeJson(IReadOnlyList<ItemStatLine> stats)
    {
        if (stats == null || stats.Count == 0) return "{}";
        var sb = new StringBuilder("{");
        var first = true;
        foreach (var line in stats)
        {
            var key = AttributeKey(line.Type);
            if (key == null || line.Value == 0) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append('"').Append(key).Append("\":").Append(line.Value.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// 反向：把库里的 <c>base_attributes</c> JSON 读回属性行。
    /// <para><b>为什么必须有这条</b>：老种子物品只有 JSON、没有属性行。编辑器打开再保存时若只认属性行，
    /// 会把一件本来有 10 点攻击的装备静默改成"没有属性"——这正是"改了没人读"的镜像坑。</para>
    /// 未识别的键忽略（与 <c>ItemAttributeContract</c> 同口径），不抛异常。
    /// </summary>
    public static List<ItemStatLine> ParseAttributeJson(string json)
    {
        var lines = new List<ItemStatLine>();
        if (string.IsNullOrWhiteSpace(json)) return lines;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return lines;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!TryParseAttributeKey(property.Name, out var type)) continue;
                if (property.Value.ValueKind != System.Text.Json.JsonValueKind.Number) continue;

                // 真实库的 base_attributes 里有小数（如 0.05）与超出 int 的字面量；
                // 直接 GetInt32() 会抛 FormatException，把"读一件物品"变成"整条导出链失败"。
                // 能取整就四舍五入，取不了就跳过这一条（与"未识别键忽略"同口径）。
                int value;
                if (!property.Value.TryGetInt32(out value))
                {
                    if (!property.Value.TryGetDouble(out var number)
                        || double.IsNaN(number) || double.IsInfinity(number)
                        || number < int.MinValue || number > int.MaxValue)
                        continue;
                    value = (int)Math.Round(number, MidpointRounding.AwayFromZero);
                }
                if (value == 0) continue;
                lines.Add(new ItemStatLine { Type = type, Value = value });
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // 脏数据当"没有属性"，与 ItemAttributeContract 的容错口径一致。
        }
        catch (FormatException)
        {
            // 极端字面量（0x 前缀、NaN 字符串等）同样按"这条不认"处理。
        }
        catch (InvalidOperationException)
        {
            // JsonElement 的类型误用同样不应拖垮整条读链。
        }
        return lines;
    }

    public static string StatLineText(ItemStatLine line)
    {
        var name = StatTypeName(line.Type);
        // 未知类型把编号念出来：只写"未知属性"时，用户无法判断是填错了还是表缺了一档。
        if (name == "未知属性") name += "（#" + line.Type.ToString(CultureInfo.InvariantCulture) + "）";
        var value = line.Value >= 0 ? "+" + line.Value : line.Value.ToString(CultureInfo.InvariantCulture);
        return IsPercentStat(line.Type) ? name + " " + value + "%" : name + " " + value;
    }

    // ---------------- 绑定 / 需求 ----------------

    public static string BindTypeName(int bindType) => bindType switch
    {
        (int)ItemBindType.Pickup => "拾取后绑定",
        (int)ItemBindType.Equip => "装备后绑定",
        (int)ItemBindType.Use => "使用后绑定",
        _ => "不绑定",
    };

    // ---------------- Tooltip ----------------

    /// <summary>tooltip 一行。Kind 供 UI 上色/分组，Text 是成品文本。</summary>
    public struct ItemTooltipLine
    {
        public string Kind;
        public string Text;
    }

    /// <summary>
    /// 生成 WoW 风格 tooltip 行（不带套装明细）。**编辑器与客户端共用**：UI 只负责按 Kind 上色。
    /// 行顺序固定：名称 → 品质/等级 → 绑定 → 部位 → 伤害 → 护甲 → 属性 → 插槽 → 套装 → 需求 → 售价 → 描述。
    /// </summary>
    public static List<ItemTooltipLine> BuildTooltip(ItemSpec spec, int equippedSetPieces = 0, int playerLevel = 0)
        => BuildTooltip(spec, null, equippedSetPieces, playerLevel);

    /// <summary>
    /// 生成 tooltip 行，并附上套装明细（WoW 的"套装：xxx"+逐档加成，已达成的档高亮）。
    /// <para><c>set</c> 为 null 时只画"套装 #id（已穿 N 件）"——不知道套装内容也不该编造。</para>
    /// </summary>
    public static List<ItemTooltipLine> BuildTooltip(ItemSpec spec, ItemSetSpec set, int equippedSetPieces, int playerLevel)
    {
        var lines = new List<ItemTooltipLine>();
        if (spec == null) return lines;

        var quality = ClampQuality(spec.Quality);
        lines.Add(Line("title", spec.Name));

        var subtitle = QualityName(quality) + " · " + ClassName(spec.ItemClass);
        if (spec.SubType > 0 || spec.ItemClass is (int)ItemClass.Weapon or (int)ItemClass.Armor)
        {
            var sub = SubTypeName(spec.ItemClass, spec.SubType);
            if (!string.IsNullOrEmpty(sub)) subtitle += " · " + sub;
        }
        if (spec.ItemLevel > 0) subtitle += "（物品等级 " + spec.ItemLevel.ToString(CultureInfo.InvariantCulture) + "）";
        lines.Add(Line("quality", subtitle));

        if (spec.BindType != (int)ItemBindType.None) lines.Add(Line("bind", BindTypeName(spec.BindType)));
        if (spec.InventoryType != (int)InventoryType.None)
            lines.Add(Line("slot", InventoryTypeName(spec.InventoryType)));
        if (spec.IsUnique) lines.Add(Line("unique", "唯一"));

        if (spec.Damage != null)
        {
            foreach (var damage in spec.Damage)
            {
                if (damage.Max <= 0 && damage.Min <= 0) continue;
                var text = damage.Min.ToString(CultureInfo.InvariantCulture) + " - "
                           + damage.Max.ToString(CultureInfo.InvariantCulture) + " 点伤害";
                if (spec.AttackDelayMs > 0)
                    text += "　速度 " + (spec.AttackDelayMs / 1000.0).ToString("0.00", CultureInfo.InvariantCulture)
                            + "　每秒伤害 " + WeaponDps(damage.Min, damage.Max, spec.AttackDelayMs).ToString(CultureInfo.InvariantCulture);
                lines.Add(Line("damage", text));
            }
        }

        if (spec.Armor > 0) lines.Add(Line("armor", "护甲 +" + spec.Armor.ToString(CultureInfo.InvariantCulture)));
        if (spec.Block > 0) lines.Add(Line("armor", "格挡 +" + spec.Block.ToString(CultureInfo.InvariantCulture)));
        if (spec.ResistanceValue > 0)
            lines.Add(Line("resist", ElementName(spec.ResistanceElement) + "抗性 +" + spec.ResistanceValue.ToString(CultureInfo.InvariantCulture)));

        if (spec.Stats != null)
        {
            foreach (var stat in spec.Stats)
            {
                if (stat.Type <= 0 || stat.Value == 0) continue;
                lines.Add(Line("stat", StatLineText(stat)));
            }
        }

        if (spec.Sockets != null && spec.Sockets.Length > 0)
        {
            var sb = new StringBuilder("插槽：");
            for (var i = 0; i < spec.Sockets.Length; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append(SocketColorName(spec.Sockets[i].Color));
            }
            lines.Add(Line("socket", sb.ToString()));
            if (spec.SocketBonus > 0)
                lines.Add(Line("socket", "插槽奖励 #" + spec.SocketBonus.ToString(CultureInfo.InvariantCulture)));
        }

        if (spec.SetId > 0)
        {
            var title = "套装" + (set != null && !string.IsNullOrWhiteSpace(set.Name) ? "：" + set.Name : " #" + spec.SetId);
            if (equippedSetPieces > 0) title += "（已穿 " + equippedSetPieces + " 件）";
            lines.Add(Line("set", title));

            if (set?.Tiers != null)
            {
                foreach (var tier in set.Tiers)
                {
                    if (tier == null || tier.Pieces <= 0) continue;
                    var active = equippedSetPieces >= tier.Pieces;
                    var text = "（" + tier.Pieces + "件）" + DescribeTier(tier);
                    lines.Add(Line(active ? "set-active" : "set-inactive", text));
                }
            }
        }

        if (spec.LevelRequire > 0) lines.Add(Line("req", "需要等级 " + spec.LevelRequire.ToString(CultureInfo.InvariantCulture)));
        if (spec.RealmRequire > 0) lines.Add(Line("req", "需要境界 " + spec.RealmRequire.ToString(CultureInfo.InvariantCulture)));
        if (spec.RequiredSkill > 0)
            lines.Add(Line("req", "需要专业 " + spec.RequiredSkill.ToString(CultureInfo.InvariantCulture)
                                 + "（" + spec.RequiredSkillRank + "）"));
        if (playerLevel > 0 && spec.LevelRequire > playerLevel) lines.Add(Line("req", "等级不足，无法使用"));

        if (spec.SellPrice > 0) lines.Add(Line("price", "售价：" + PriceText(spec.SellPrice)));
        if (spec.MaxStack > 1) lines.Add(Line("stack", "可堆叠 " + spec.MaxStack.ToString(CultureInfo.InvariantCulture)));

        if (!string.IsNullOrWhiteSpace(spec.Description)) lines.Add(Line("desc", spec.Description));
        return lines;
    }

    private static ItemTooltipLine Line(string kind, string text) => new() { Kind = kind, Text = text };

    /// <summary>套装档的说明：优先用策划写的描述，没有就用属性行拼。</summary>
    private static string DescribeTier(ItemSetBonusTier tier)
    {
        if (!string.IsNullOrWhiteSpace(tier.Description)) return tier.Description;
        if (tier.Stats == null || tier.Stats.Length == 0) return "（无属性）";
        var parts = new List<string>(tier.Stats.Length);
        foreach (var stat in tier.Stats)
        {
            if (stat.Type <= 0 || stat.Value == 0) continue;
            parts.Add(StatLineText(stat));
        }
        return parts.Count == 0 ? "（无属性）" : string.Join("，", parts);
    }

    public static string ElementName(int element) => element switch
    {
        0 => "金", 1 => "木", 2 => "水", 3 => "火", 4 => "土", 5 => "无",
        _ => "无",
    };

    public static int ClampQuality(int quality) => quality < 0 ? 0 : quality > 6 ? 6 : quality;

    /// <summary>
    /// 这件物品能不能进客户端内容表。
    /// <para><b>放行</b>：装备/外观（必须有部位）、秘籍（800000+技能 id）、消耗品、宝石、材料、任务物品、配方。
    /// <b>挡住</b>：历史号段（1..9999，含 2001..2004 背包——客户端按 Bag 硬编码，覆盖会变消耗品）、
    /// 背包大类（客户端背包槽位是硬编码的）、缺部位的装备/外观。</para>
    /// </summary>
    public static bool IsClientExportable(ItemSpec spec, out string reason)
    {
        reason = string.Empty;
        if (spec == null)
        {
            reason = "草稿为空";
            return false;
        }

        var segment = SegmentOf(spec.TemplateId);
        if (segment is ItemSegment.Legacy or ItemSegment.Invalid)
        {
            reason = "历史/号段外物品不导出（客户端按旧硬编码处理，覆盖会改变分类）";
            return false;
        }

        // 910000..919999 是五行灵材/信物：库里 item_type 可能是 0/1/2（早年录入按装备类），
        // 但语义是材料，**不能**按装备去要求 inventory_type。
        if (segment == ItemSegment.WuxingMaterial)
        {
            return true;
        }

        var isAppearance = spec.InventoryType == (int)InventoryType.Appearance;
        var isBook = spec.ItemClass == (int)ItemClass.Book;
        var isEquip = spec.ItemClass is (int)ItemClass.Weapon or (int)ItemClass.Armor or (int)ItemClass.Accessory;
        var isConsumable = spec.ItemClass == (int)ItemClass.Consumable;
        var isMaterial = spec.ItemClass == (int)ItemClass.Material;
        var isQuest = spec.ItemClass == (int)ItemClass.Quest;
        var isRecipe = spec.ItemClass == (int)ItemClass.Recipe;
        var isContainer = spec.ItemClass == (int)ItemClass.Container;

        if (isContainer)
        {
            reason = "背包大类暂不导出（客户端背包槽位是硬编码的）";
            return false;
        }

        if (!isAppearance && !isBook && !isEquip && !isConsumable && !isMaterial && !isQuest && !isRecipe)
        {
            reason = "未知大类，客户端覆盖层无法重建";
            return false;
        }

        if ((isEquip || isAppearance) && spec.InventoryType <= 0)
        {
            reason = "装备/外观缺 inventory_type（历史数据），导出会覆盖掉可穿的硬编码条目";
            return false;
        }

        if (isBook && (spec.TemplateId < 800001 || spec.TemplateId > 899999))
        {
            reason = "秘籍编号必须是 800000+技能 id";
            return false;
        }

        return true;
    }

    // ---------------- 校验 ----------------

    /// <summary>校验结果。Errors 与 ErrorPaths 一一对应（UI 逐条打印"哪一格错了"）。</summary>
    public sealed class ValidationResult
    {
        public List<string> Errors { get; } = new();
        public List<string> ErrorPaths { get; } = new();
        public List<string> Warnings { get; } = new();
        public bool Ok => Errors.Count == 0;

        public void Error(string path, string message)
        {
            ErrorPaths.Add(path);
            Errors.Add(message);
        }

        public void Warn(string message) => Warnings.Add(message);
    }

    /// <summary>
    /// 结构校验。只做"不看库也能判"的事；跨域引用（技能/套装/词缀是否存在）由 CLI 的引用索引补。
    /// </summary>
    public static ValidationResult Validate(ItemSpec spec)
    {
        var result = new ValidationResult();
        if (spec == null)
        {
            result.Error("<draft>", "草稿为空");
            return result;
        }

        var segment = SegmentOf(spec.TemplateId);
        if (segment == ItemSegment.Invalid)
            result.Error("templateId", $"物品编号 {spec.TemplateId} 不在任何号段内（见设计文档 §3.2）");

        if (string.IsNullOrWhiteSpace(spec.Name))
            result.Error("name", "名称不能为空");
        else if (spec.Name.Length > 50)
            result.Error("name", "名称超过 50 字（库里是 nvarchar(50)）");

        if (spec.Description != null && spec.Description.Length > 500)
            result.Error("description", "描述超过 500 字（库里是 nvarchar(500)）");

        if (spec.ItemClass < 0 || spec.ItemClass > (int)ItemClass.Appearance)
            result.Error("itemClass", $"物品大类 {spec.ItemClass} 非法（0..9）");
        if (spec.SubType < 0 || spec.SubType > 99)
            result.Error("subType", "子类型必须在 0..99");

        if (spec.Quality < 0 || spec.Quality > 6)
            result.Error("quality", "品质必须在 0..6");
        if (spec.Rarity < 0 || spec.Rarity > 5)
            result.Error("rarity", "掉落稀有度必须在 0..5（注意它与品质不是同一个字段）");

        if (spec.ItemLevel < 0 || spec.ItemLevel > ItemSpec.MaxItemLevel)
            result.Error("itemLevel", $"物品等级必须在 0..{ItemSpec.MaxItemLevel}");
        if (spec.MaterialGrade < 0 || spec.MaterialGrade > 9)
            result.Error("materialGrade", "材料品阶必须在 0..9");
        if (spec.GatherType < 0 || spec.GatherType > 4)
            result.Error("gatherType", "采集类型必须在 0..4");
        if (spec.SourceType < 0 || spec.SourceType > 6)
            result.Error("sourceType", "出处类型必须在 0..6");
        if (spec.ItemLevel == 0 && spec.ItemClass is (int)ItemClass.Weapon or (int)ItemClass.Armor)
            result.Warn("武器/防具的物品等级为 0：属性预算将按 0 计，装备不会有词条");

        if (spec.InventoryType < 0 || spec.InventoryType > (int)InventoryType.Bag)
            result.Error("inventoryType", "部位必须在 0..16");
        if (spec.ItemClass == (int)ItemClass.Weapon && EquipSlotIndex(spec.InventoryType) < 0)
            result.Error("inventoryType", "武器必须指定手持部位（主手/副手/外观）");
        if (spec.ItemClass == (int)ItemClass.Armor && EquipSlotIndex(spec.InventoryType) < 0)
            result.Error("inventoryType", "防具必须指定身体部位");

        if (spec.BindType < 0 || spec.BindType > 3)
            result.Error("bindType", "绑定类型必须在 0..3");

        if (spec.LevelRequire < 0 || spec.LevelRequire > MaxRequiredLevel)
            result.Error("levelRequire", $"等级需求必须在 0..{MaxRequiredLevel}");
        if (spec.RealmRequire < 0)
            result.Error("realmRequire", "境界需求不能为负");
        if (spec.RequiredSkillRank < 0)
            result.Error("requiredSkillRank", "专业等级不能为负");
        if (spec.RequiredSkillRank > 0 && spec.RequiredSkill <= 0)
            result.Error("requiredSkill", "填了专业等级却没有专业 id");
        if (spec.AllowableClass < 0 || spec.AllowableRace < 0)
            result.Error("allowableClass", "职业/种族掩码不能为负");

        if (spec.MaxStack < 1 || spec.MaxStack > ItemSpec.MaxStackLimit)
            result.Error("maxStack", $"叠加上限必须在 1..{ItemSpec.MaxStackLimit}");
        if (spec.MaxCount < 0 || spec.MaxCount > ItemSpec.MaxStackLimit)
            result.Error("maxCount", "持有上限必须在 0.." + ItemSpec.MaxStackLimit);
        if (spec.ContainerSlots < 0 || spec.ContainerSlots > 36)
            result.Error("containerSlots", "背包格数必须在 0..36");
        if (spec.Durability < 0)
            result.Error("durability", "耐久不能为负");

        if (spec.Armor < 0 || spec.Block < 0) result.Error("armor", "护甲/格挡不能为负");
        if (spec.AttackDelayMs < 0 || spec.AttackDelayMs > 60_000)
            result.Error("attackDelayMs", "攻击间隔必须在 0..60000 毫秒");
        if (spec.ResistanceValue < 0) result.Error("resistanceValue", "抗性不能为负");
        if (spec.ResistanceValue > 0 && (spec.ResistanceElement < 0 || spec.ResistanceElement > 5))
            result.Error("resistanceElement", "抗性元素必须在 0..5");

        if (spec.BuyPrice < 0 || spec.SellPrice < 0)
            result.Error("buyPrice", "买价/卖价不能为负");
        if (spec.SellPrice > 0 && spec.BuyPrice > 0 && spec.SellPrice > spec.BuyPrice)
            result.Warn("售价高于买价：玩家可以无限刷差价");
        if (spec.BuyPrice == 0 && spec.SellPrice > 0)
            result.Warn("买价为 0 而售价大于 0：商店不会出售它，但玩家仍可卖出");
        if (spec.MinMoneyLoot < 0 || spec.MaxMoneyLoot < 0 || spec.MaxMoneyLoot < spec.MinMoneyLoot)
            result.Error("maxMoneyLoot", "钱袋区间非法（min ≤ max 且非负）");

        if (spec.SocketBonus < 0) result.Error("socketBonus", "插槽奖励 id 不能为负");
        if (spec.DurationSeconds < 0 || spec.ValidDays < 0)
            result.Error("durationSeconds", "有效期不能为负");

        ValidateStats(spec, result);
        ValidateDamage(spec, result);
        ValidateSockets(spec, result);
        ValidateSpells(spec, result);

        if (spec.ItemClass == (int)ItemClass.Book)
        {
            if (spec.TemplateId < 800000 || spec.TemplateId > 899999)
                result.Error("templateId", "秘籍编号必须是 800000+技能 id");
            else if (spec.TemplateId - 800000 <= 0)
                result.Error("templateId", "秘籍编号减 800000 后必须是正技能 id");
        }

        if (spec.InventoryType == (int)InventoryType.Appearance && segment != ItemSegment.Appearance)
            result.Error("templateId", "外观物品必须落在 91000..91999 段（910000..919999 是五行灵材/信物，不是外观）");

        return result;
    }

    private static void ValidateStats(ItemSpec spec, ValidationResult result)
    {
        if (spec.Stats == null) return;
        if (spec.Stats.Length > ItemSpec.MaxStatLines)
            result.Error("stats", $"属性行最多 {ItemSpec.MaxStatLines} 条（当前 {spec.Stats.Length}）");

        var seen = new HashSet<int>();
        for (var i = 0; i < spec.Stats.Length; i++)
        {
            var stat = spec.Stats[i];
            var path = "stats[" + i + "]";
            if (stat.Type < 0 || stat.Type > (int)ItemStatType.Energy)
            {
                result.Error(path, "属性类型非法");
                continue;
            }
            if (stat.Type == 0)
            {
                result.Error(path, "属性类型为 0（空行请删掉）");
                continue;
            }
            if (stat.Value == 0) result.Error(path, "属性值为 0（空行请删掉）");
            if (!seen.Add(stat.Type)) result.Error(path, "同一属性出现两次：后一条会覆盖前一条的显示");
        }
    }

    private static void ValidateDamage(ItemSpec spec, ValidationResult result)
    {
        if (spec.Damage == null) return;
        if (spec.Damage.Length > ItemSpec.MaxDamageLines)
            result.Error("damage", $"伤害段最多 {ItemSpec.MaxDamageLines} 段");
        for (var i = 0; i < spec.Damage.Length; i++)
        {
            var d = spec.Damage[i];
            var path = "damage[" + i + "]";
            if (d.Min < 0 || d.Max < 0) result.Error(path, "伤害不能为负");
            if (d.Max < d.Min) result.Error(path, "最大伤害小于最小伤害");
            if (d.Type < 0 || d.Type > 5) result.Error(path, "伤害类型必须在 0..5");
        }
        if (spec.Damage.Length > 0 && spec.AttackDelayMs <= 0)
            result.Warn("有伤害段但攻击间隔为 0：DPS 无法计算");
    }

    private static void ValidateSockets(ItemSpec spec, ValidationResult result)
    {
        if (spec.Sockets == null) return;
        if (spec.Sockets.Length > ItemSpec.MaxSockets)
            result.Error("sockets", $"固定插槽最多 {ItemSpec.MaxSockets} 个");
        for (var i = 0; i < spec.Sockets.Length; i++)
        {
            var color = spec.Sockets[i].Color;
            if (color < 0 || color > (int)SocketColor.None)
                result.Error("sockets[" + i + "]", "插槽颜色非法");
        }
    }

    private static void ValidateSpells(ItemSpec spec, ValidationResult result)
    {
        if (spec.Spells == null) return;
        if (spec.Spells.Length > ItemSpec.MaxSpellLines)
            result.Error("spells", $"法术触发最多 {ItemSpec.MaxSpellLines} 行");
        for (var i = 0; i < spec.Spells.Length; i++)
        {
            var spell = spec.Spells[i];
            var path = "spells[" + i + "]";
            if (spell.SpellId <= 0) result.Error(path, "法术 id 必须为正");
            if (spell.Trigger < 0 || spell.Trigger > 5) result.Error(path, "触发方式必须在 0..5");
            if (spell.Charges < -1 || spell.Charges > 20) result.Error(path, "充能必须在 -1..20");
            if (spell.PpmRate < 0 || spell.CooldownMs < 0 || spell.CategoryCooldownMs < 0)
                result.Error(path, "PPM/冷却不能为负");
        }
    }
}
