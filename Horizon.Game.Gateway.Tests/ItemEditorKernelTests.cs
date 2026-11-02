using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Sim.Items;
using Xunit;
using Xunit.Abstractions;
using AttributeType = Horizon.Game.Message.Enums.AttributeType;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 物品内核（<c>Horizon.Game.Sim/Items/</c>）的可执行断言。
/// <para>
/// 三条"必须存在"的理由（与宠物内核同一套规矩）：
/// ① 预算/价格/DPS 是**数值承诺**：编辑器显示、客户端 tooltip、将来服务端结算都用这一份。
/// ② 单调性必须由测试钉住——系数表被人手滑改成非递增时，界面看不出来，只有对比不同 ilvl 才暴露。
/// ③ 每条拒绝分支都配一个**同夹具的正向对照**：只断言"这份被拒绝"时，校验器整个坏掉（永远拒绝）
///    测试照样全绿。
/// </para>
/// </summary>
public class ItemEditorKernelTests
{
    private readonly ITestOutputHelper _log;

    public ItemEditorKernelTests(ITestOutputHelper log) => _log = log;

    // ==================== ① 号段 ====================

    [Fact]
    public void 号段边界逐点正确()
    {
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(0));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(-1));
        Assert.Equal(ItemSegment.Legacy, ItemCore.SegmentOf(1));
        Assert.Equal(ItemSegment.Legacy, ItemCore.SegmentOf(9999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(10000));
        Assert.Equal(ItemSegment.Equipment, ItemCore.SegmentOf(10001));
        Assert.Equal(ItemSegment.Equipment, ItemCore.SegmentOf(49999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(50000));
        Assert.Equal(ItemSegment.Consumable, ItemCore.SegmentOf(50001));
        Assert.Equal(ItemSegment.Consumable, ItemCore.SegmentOf(59999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(60000));
        Assert.Equal(ItemSegment.Gem, ItemCore.SegmentOf(60001));
        Assert.Equal(ItemSegment.Gem, ItemCore.SegmentOf(69999));
        Assert.Equal(ItemSegment.Material, ItemCore.SegmentOf(700001));
        Assert.Equal(ItemSegment.Material, ItemCore.SegmentOf(799999));
        Assert.Equal(ItemSegment.Book, ItemCore.SegmentOf(800000));
        Assert.Equal(ItemSegment.Book, ItemCore.SegmentOf(899999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(900000));
        Assert.Equal(ItemSegment.Affix, ItemCore.SegmentOf(900001));
        Assert.Equal(ItemSegment.Affix, ItemCore.SegmentOf(909999));
        Assert.Equal(ItemSegment.Appearance, ItemCore.SegmentOf(91000));
        Assert.Equal(ItemSegment.Appearance, ItemCore.SegmentOf(91001));
        Assert.Equal(ItemSegment.Appearance, ItemCore.SegmentOf(91999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(92000));
        Assert.Equal(ItemSegment.WuxingMaterial, ItemCore.SegmentOf(910000));
        Assert.Equal(ItemSegment.WuxingMaterial, ItemCore.SegmentOf(910011));
        Assert.Equal(ItemSegment.WuxingMaterial, ItemCore.SegmentOf(919999));
        Assert.Equal(ItemSegment.Set, ItemCore.SegmentOf(920000));
        Assert.Equal(ItemSegment.Set, ItemCore.SegmentOf(929999));
        Assert.Equal(ItemSegment.Invalid, ItemCore.SegmentOf(930000));
    }

    [Fact]
    public void 既存外观与灵材各自落在正确号段()
    {
        // 盘上既有数据（EquipmentDatabase.AppearanceIdBase=91000；五行灵材/信物 910001..910015）。
        // 这两个族只差一位数字，曾经被并成同一段（灵材被判成外观）。
        foreach (var id in new[] { 91001, 91002, 91003, 91004 })
            Assert.Equal(ItemSegment.Appearance, ItemCore.SegmentOf(id));
        foreach (var id in new[] { 910001, 910005, 910011, 910015 })
            Assert.Equal(ItemSegment.WuxingMaterial, ItemCore.SegmentOf(id));
    }

    [Fact]
    public void 每个号段的区间都落在自己那一段内()
    {
        foreach (ItemSegment segment in Enum.GetValues<ItemSegment>())
        {
            if (segment == ItemSegment.Invalid) continue;
            var (min, max) = ItemCore.RangeOf(segment);
            Assert.True(min <= max, segment + " 区间反了");
            Assert.Equal(segment, ItemCore.SegmentOf(min));
            Assert.Equal(segment, ItemCore.SegmentOf(max));
            Assert.True(ItemCore.IsInSegment(min, segment));
        }
    }

    // ==================== ② 预算与战斗换算 ====================

    [Fact]
    public void 预算对物品等级与品质都严格递增()
    {
        var previousByLevel = new int[ItemSpec.MaxItemLevel + 1];
        for (var quality = 0; quality <= 6; quality++)
        {
            var previous = 0;
            for (var level = 1; level <= 60; level++)
            {
                var budget = ItemCore.StatBudget(level, quality);
                Assert.True(budget > previous,
                    $"品质 {quality} 下 ilvl {level} 的预算 {budget} 不大于 ilvl {level - 1} 的 {previous}");
                previous = budget;
            }

            for (var level = 1; level <= ItemSpec.MaxItemLevel; level++)
                previousByLevel[level] = ItemCore.StatBudget(level, quality);
        }

        // 同 ilvl 下，品质必须严格递增（表被人手滑改乱时这条会红）
        for (var level = 1; level <= 60; level++)
        {
            for (var quality = 1; quality <= 6; quality++)
            {
                Assert.True(ItemCore.StatBudget(level, quality) > ItemCore.StatBudget(level, quality - 1),
                    $"ilvl {level}：品质 {quality} 的预算不大于品质 {quality - 1}");
            }
        }
    }

    [Fact]
    public void 品质系数表严格递增且长度为七()
    {
        Assert.Equal(7, ItemCore.QualityMultiplierBpsTable.Length);
        for (var i = 1; i < ItemCore.QualityMultiplierBpsTable.Length; i++)
            Assert.True(ItemCore.QualityMultiplierBpsTable[i] > ItemCore.QualityMultiplierBpsTable[i - 1]);
    }

    [Fact]
    public void 盾牌护甲高于同预算普通防具()
    {
        for (var budget = 1; budget <= 5000; budget += 97)
            Assert.True(ItemCore.ArmorFromBudget(budget, true) > ItemCore.ArmorFromBudget(budget, false),
                $"预算 {budget} 时盾牌护甲没有更高");
    }

    [Fact]
    public void 武器DPS按平均伤害与间隔折算()
    {
        // 12..18 平均 15，2.6 秒 → 5.769 → 四舍五入 6
        Assert.Equal(6, ItemCore.WeaponDps(12, 18, 2600));
        Assert.Equal(0, ItemCore.WeaponDps(12, 18, 0));
        // 顺序反了也按 min/max 处理
        Assert.Equal(6, ItemCore.WeaponDps(18, 12, 2600));
        // 独立参照：逐个手工算（不调用内核）
        foreach (var sample in new[] { (10, 20, 1000), (5, 5, 2000), (100, 200, 3000) })
        {
            var expected = (int)Math.Round((sample.Item1 + sample.Item2) / 2.0 * 1000.0 / sample.Item3,
                MidpointRounding.AwayFromZero);
            Assert.Equal(expected, ItemCore.WeaponDps(sample.Item1, sample.Item2, sample.Item3));
        }
    }

    // ==================== ③ 价格 ====================

    [Fact]
    public void 价格推导与WoW口径一致()
    {
        Assert.Equal(25, ItemCore.DefaultSellPrice(100));
        Assert.Equal(1, ItemCore.DefaultSellPrice(3));       // 向下取整后至少 1 铜
        Assert.Equal(0, ItemCore.DefaultSellPrice(0));
        Assert.Equal(0, ItemCore.DefaultSellPrice(-5));
        Assert.Equal(400, ItemCore.DefaultBuyPrice(100));
        Assert.Equal(0, ItemCore.DefaultBuyPrice(0));
    }

    [Fact]
    public void 价格文本按金银铜进位()
    {
        Assert.Equal("1金23银45铜", ItemCore.PriceText(12345));
        Assert.Equal("1金", ItemCore.PriceText(10000));
        Assert.Equal("23银45铜", ItemCore.PriceText(2345));
        Assert.Equal("45铜", ItemCore.PriceText(45));
        Assert.Equal("0", ItemCore.PriceText(0));
    }

    // ==================== ④ 插槽 ====================

    [Fact]
    public void 插槽配对矩阵与魔兽一致()
    {
        Assert.True(ItemCore.CanSocket((int)SocketColor.Red, (int)SocketColor.Red));
        Assert.False(ItemCore.CanSocket((int)SocketColor.Red, (int)SocketColor.Blue));
        Assert.True(ItemCore.CanSocket((int)SocketColor.Red, (int)SocketColor.None));
        Assert.True(ItemCore.CanSocket((int)SocketColor.Meta, (int)SocketColor.Meta));
        Assert.False(ItemCore.CanSocket((int)SocketColor.Red, (int)SocketColor.Meta));
        Assert.True(ItemCore.CanSocket((int)SocketColor.None, (int)SocketColor.Yellow));
    }

    // ==================== ⑤ 词缀与套装 ====================

    [Fact]
    public void 词缀抽取确定且在区间内()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var first = ItemCore.RollAffixValue(seed, 80, 120);
            var second = ItemCore.RollAffixValue(seed, 80, 120);
            Assert.Equal(first, second);
            Assert.InRange(first, 80, 120);
        }

        // 边界：min==max 必须恒等，不能除零
        Assert.Equal(80, ItemCore.RollAffixValue(7, 80, 80));
        Assert.Equal(0, ItemCore.RollAffixIndex(7, 0));
    }

    [Fact]
    public void 词缀按roll比例展开属性()
    {
        var affix = new ItemAffixSpec
        {
            AffixId = 900001, Kind = 0, ValueMin = 100, ValueMax = 100,
            Stats = new[]
            {
                new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 100 },
                new ItemStatLine { Type = (int)ItemStatType.CriticalRate, Value = 50 },
            },
        };
        var applied = ItemCore.ApplyAffix(affix, 3);
        Assert.Equal(2, applied.Length);
        Assert.Equal(100, applied[0].Value);   // 权重 100 × roll 100 / 100
        Assert.Equal(50, applied[1].Value);
    }

    [Fact]
    public void 套装档位按已穿件数生效且属性求和()
    {
        var set = new ItemSetSpec
        {
            SetId = 920001,
            Tiers = new[]
            {
                new ItemSetBonusTier
                {
                    Pieces = 2, Description = "两件",
                    Stats = new[] { new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 20 } },
                },
                new ItemSetBonusTier
                {
                    Pieces = 4, Description = "四件",
                    Stats = new[]
                    {
                        new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 40 },
                        new ItemStatLine { Type = (int)ItemStatType.CriticalRate, Value = 5 },
                    },
                },
            },
        };

        Assert.Empty(ItemCore.ActiveTiers(set, 1));
        Assert.Single(ItemCore.ActiveTiers(set, 2));
        Assert.Single(ItemCore.ActiveTiers(set, 3));
        Assert.Equal(2, ItemCore.ActiveTiers(set, 4).Count);

        var totals = ItemCore.SetStatTotals(set, 4);
        Assert.Equal(60, totals.Single(s => s.Type == (int)ItemStatType.Attack).Value);
        // 三件时只有两件套生效
        Assert.Equal(20, ItemCore.SetStatTotals(set, 3).Single(s => s.Type == (int)ItemStatType.Attack).Value);
    }

    [Fact]
    public void 预算分配的总和精确等于预算()
    {
        foreach (var budget in new[] { 1, 7, 100, 221, 1000, 9999 })
        {
            var distributed = ItemCore.DistributeBudget(budget, new[] { 6, 7, 14 }, new[] { 10_000, 10_000, 10_000 });
            Assert.Equal(budget, distributed.Sum(s => s.Value));
            Assert.All(distributed, s => Assert.True(s.Value >= 1));
        }
        Assert.Empty(ItemCore.DistributeBudget(0, new[] { 6 }, null));
    }

    // ==================== ⑥ 属性 JSON 往返 ====================

    [Fact]
    public void 属性JSON的键就是AttributeType成员名()
    {
        var json = ItemCore.BuildAttributeJson(new[]
        {
            new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 20 },
            new ItemStatLine { Type = (int)ItemStatType.CriticalRate, Value = 5 },
            new ItemStatLine { Type = (int)ItemStatType.MetalAffinity, Value = 3 },
            new ItemStatLine { Type = (int)ItemStatType.MaxHealth, Value = 100 },
        });
        Assert.Equal("{\"Attack\":20,\"CriticalRate\":5,\"MetalAffinity\":3,\"MaxHealth\":100}", json);

        // 键必须是服务端 AttributeType 的真实成员名——否则 ItemAttributeContract 会静默忽略
        foreach (var key in new[] { "Attack", "CriticalRate", "MetalAffinity", "MaxHealth" })
            Assert.True(Enum.GetNames<AttributeType>().Contains(key), "AttributeType 里没有 " + key);

        Assert.Equal("{}", ItemCore.BuildAttributeJson(Array.Empty<ItemStatLine>()));
    }

    [Fact]
    public void 属性JSON可以原样读回()
    {
        var source = new[]
        {
            new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 12 },
            new ItemStatLine { Type = (int)ItemStatType.DodgeRate, Value = -3 },
        };
        var json = ItemCore.BuildAttributeJson(source);
        var parsed = ItemCore.ParseAttributeJson(json);
        Assert.Equal(2, parsed.Count);
        Assert.Equal(12, parsed.Single(s => s.Type == (int)ItemStatType.Attack).Value);
        Assert.Equal(-3, parsed.Single(s => s.Type == (int)ItemStatType.DodgeRate).Value);
        Assert.Empty(ItemCore.ParseAttributeJson("{bad json"));
        Assert.Empty(ItemCore.ParseAttributeJson("{\"NotAStat\": 5}"));
    }

    [Fact]
    public void 属性JSON里的小数与超界数不会让读取抛异常()
    {
        // 真实库的 base_attributes 里有 0.05 这类小数；上一版直接 GetInt32() 抛 FormatException，
        // 表现是"打开一件物品/导出整批"直接失败。规则：能取整就四舍五入，取不了就跳过这一条。
        var parsed = ItemCore.ParseAttributeJson(
            "{\"Attack\":12.6,\"CriticalRate\":0.05,\"MaxHealth\":1e12,\"Defense\":\"x\",\"DodgeRate\":-2.7}");
        Assert.Equal(13, parsed.Single(s => s.Type == (int)ItemStatType.Attack).Value);
        Assert.Equal(-3, parsed.Single(s => s.Type == (int)ItemStatType.DodgeRate).Value);
        Assert.DoesNotContain(parsed, s => s.Type == (int)ItemStatType.CriticalRate); // 0.05 取整为 0，丢弃
        Assert.DoesNotContain(parsed, s => s.Type == (int)ItemStatType.MaxHealth);    // 超 int 界，丢弃
        Assert.DoesNotContain(parsed, s => s.Type == (int)ItemStatType.Defense);      // 字符串，丢弃
    }

    // ==================== ⑦ 校验 ====================

    private static ItemSpec ValidArmor(int templateId = 10001) => new()
    {
        TemplateId = templateId,
        Name = "测试胸甲",
        Description = "只用于测试",
        ItemClass = (int)ItemClass.Armor,
        SubType = 3,
        Quality = (int)ItemQuality.Rare,
        ItemLevel = 20,
        InventoryType = (int)InventoryType.Body,
        BindType = (int)ItemBindType.Equip,
        LevelRequire = 10,
        MaxStack = 1,
        CanTrade = true,
        CanDestroy = true,
        BuyPrice = 400,
        SellPrice = 100,
        Stats = new[] { new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 20 } },
    };

    [Fact]
    public void 合法防具通过校验()
    {
        var result = ItemCore.Validate(ValidArmor());
        Assert.True(result.Ok, string.Join(" | ", result.Errors));
    }

    [Fact]
    public void 号段外与空名被拒绝()
    {
        var outside = ValidArmor(930000);
        Assert.False(ItemCore.Validate(outside).Ok);

        var noName = ValidArmor();
        noName.Name = "  ";
        Assert.False(ItemCore.Validate(noName).Ok);
        Assert.Contains("name", ItemCore.Validate(noName).ErrorPaths);
    }

    [Fact]
    public void 同一属性出现两次被拒绝()
    {
        var spec = ValidArmor();
        spec.Stats = new[]
        {
            new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 20 },
            new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 30 },
        };
        var result = ItemCore.Validate(spec);
        Assert.False(result.Ok);
        Assert.Contains("stats[1]", result.ErrorPaths);
    }

    [Fact]
    public void 秘籍编号必须能反解出技能()
    {
        var book = ValidArmor(800001);
        book.ItemClass = (int)ItemClass.Book;
        book.InventoryType = (int)InventoryType.None;
        Assert.True(ItemCore.Validate(book).Ok, string.Join(" | ", ItemCore.Validate(book).Errors));

        var zeroSkill = ValidArmor(800000);
        zeroSkill.ItemClass = (int)ItemClass.Book;
        zeroSkill.InventoryType = (int)InventoryType.None;
        var result = ItemCore.Validate(zeroSkill);
        Assert.False(result.Ok);
        Assert.Contains("templateId", result.ErrorPaths);
    }

    [Fact]
    public void 外观物品必须落在外观段()
    {
        var wrong = ValidArmor(10001);
        wrong.InventoryType = (int)InventoryType.Appearance;
        Assert.False(ItemCore.Validate(wrong).Ok);

        var right = ValidArmor(91001);
        right.InventoryType = (int)InventoryType.Appearance;
        Assert.True(ItemCore.Validate(right).Ok, string.Join(" | ", ItemCore.Validate(right).Errors));
    }

    [Fact]
    public void 售价高于买价是警告而不是错误()
    {
        var spec = ValidArmor();
        spec.BuyPrice = 100;
        spec.SellPrice = 200;
        var result = ItemCore.Validate(spec);
        Assert.True(result.Ok);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void 属性行与法术行上限被拒绝()
    {
        var tooMany = ValidArmor();
        tooMany.Stats = Enumerable.Range(1, ItemSpec.MaxStatLines + 1)
            .Select(i => new ItemStatLine { Type = i, Value = 1 }).ToArray();
        Assert.False(ItemCore.Validate(tooMany).Ok);

        var tooManySpells = ValidArmor();
        tooManySpells.Spells = Enumerable.Range(0, ItemSpec.MaxSpellLines + 1)
            .Select(i => new ItemSpellLine { SpellId = 800001 + i, Trigger = 0, Charges = 1 }).ToArray();
        Assert.Contains("spells", ItemCore.Validate(tooManySpells).ErrorPaths);
    }

    // ==================== ⑧ Tooltip ====================

    [Fact]
    public void Tooltip首行是名称且含需求与售价()
    {
        var lines = ItemCore.BuildTooltip(ValidArmor(), 0, 5);
        Assert.NotEmpty(lines);
        Assert.Equal("title", lines[0].Kind);
        Assert.Equal("测试胸甲", lines[0].Text);
        Assert.Contains(lines, l => l.Kind == "req" && l.Text.Contains("需要等级 10"));
        Assert.Contains(lines, l => l.Kind == "req" && l.Text.Contains("等级不足"));
        Assert.Contains(lines, l => l.Kind == "price" && l.Text.Contains("1银"));
        Assert.Contains(lines, l => l.Kind == "stat" && l.Text.Contains("攻击力"));
    }

    [Fact]
    public void Tooltip对空草稿不抛且返回空()
    {
        Assert.Empty(ItemCore.BuildTooltip(null));
    }

    [Fact]
    public void 缺部位的装备不许进客户端内容表()
    {
        // 历史物品 inventory_type 全是 0；若直接导出，覆盖层会把 EquipSlot 变成 null，
        // 反而把原本能穿的硬编码装备盖坏。导出必须先挡下来并报问题。
        var armorNoSlot = ValidArmor();
        armorNoSlot.InventoryType = 0;
        Assert.False(ItemCore.IsClientExportable(armorNoSlot, out var reason));
        Assert.Contains("inventory_type", reason);

        var armorWithSlot = ValidArmor();
        Assert.True(ItemCore.IsClientExportable(armorWithSlot, out _));

        // 材料/宝石/消耗品/任务物/配方现在都能导出（专属字段已进导出表）
        var material = ValidArmor(700001);
        material.ItemClass = (int)ItemClass.Material;
        material.InventoryType = 0;
        Assert.True(ItemCore.IsClientExportable(material, out _));

        // 910000..919999 是五行灵材/信物：库里 item_type 可能是装备类，但语义是材料，不要求部位
        var wuxingToken = ValidArmor(910011);
        wuxingToken.ItemClass = (int)ItemClass.Accessory;
        wuxingToken.InventoryType = 0;
        Assert.True(ItemCore.IsClientExportable(wuxingToken, out _));

        // 历史号段（1..9999，含 2001..2004 背包）不导出
        var legacyBag = ValidArmor(2001);
        legacyBag.ItemClass = (int)ItemClass.Consumable;
        legacyBag.InventoryType = 0;
        Assert.False(ItemCore.IsClientExportable(legacyBag, out _));

        // 背包大类不导出（客户端槽位硬编码）
        var container = ValidArmor(49001);
        container.ItemClass = (int)ItemClass.Container;
        container.InventoryType = (int)InventoryType.Bag;
        Assert.False(ItemCore.IsClientExportable(container, out _));

        // 外观按 inventory_type=16 识别：018 种子里 item_type 仍是 4（材料），不能用大类挡
        var appearance = ValidArmor(91001);
        appearance.ItemClass = (int)ItemClass.Material;
        appearance.InventoryType = (int)InventoryType.Appearance;
        Assert.True(ItemCore.IsClientExportable(appearance, out _));

        var appearanceNoSlot = ValidArmor(91001);
        appearanceNoSlot.InventoryType = 0;
        Assert.False(ItemCore.IsClientExportable(appearanceNoSlot, out _));

        // 秘籍：id 必须能反解出技能
        var book = ValidArmor(800001);
        book.ItemClass = (int)ItemClass.Book;
        book.InventoryType = 0;
        Assert.True(ItemCore.IsClientExportable(book, out _));
        var badBook = ValidArmor(800000);
        badBook.ItemClass = (int)ItemClass.Book;
        badBook.InventoryType = 0;
        Assert.False(ItemCore.IsClientExportable(badBook, out _));
    }

    [Fact]
    public void Tooltip列出套装档位并标出已达成()
    {
        var spec = ValidArmor(91001);
        spec.InventoryType = (int)InventoryType.Appearance;
        spec.SetId = 920001;
        var set = new ItemSetSpec
        {
            SetId = 920001,
            Name = "玄铁套装",
            Tiers = new[]
            {
                new ItemSetBonusTier
                {
                    Pieces = 2, Description = "两件：攻击 +20",
                    Stats = new[] { new ItemStatLine { Type = (int)ItemStatType.Attack, Value = 20 } },
                },
                new ItemSetBonusTier { Pieces = 4, Description = "四件：暴击 +5" },
            },
        };

        var lines = ItemCore.BuildTooltip(spec, set, 2, 0);
        Assert.Contains(lines, l => l.Kind == "set" && l.Text.Contains("玄铁套装"));
        Assert.Contains(lines, l => l.Kind == "set-active" && l.Text.Contains("（2件）"));
        Assert.Contains(lines, l => l.Kind == "set-inactive" && l.Text.Contains("（4件）"));

        // 不带套装内容时只能画 id，不能编造档位
        var withoutSet = ItemCore.BuildTooltip(spec, 2, 0);
        Assert.Contains(withoutSet, l => l.Kind == "set" && l.Text.Contains("920001"));
        Assert.DoesNotContain(withoutSet, l => l.Kind == "set-active" || l.Kind == "set-inactive");
    }

    // ==================== ⑨ 显示表长度 ====================

    [Fact]
    public void 显示表长度与枚举对齐()
    {
        Assert.Equal(7, ItemCore.QualityNames.Length);
        Assert.Equal(7, ItemCore.QualityColorHexes.Length);
        Assert.Equal(18, ItemCore.InventoryTypeNames.Length);
        Assert.Equal(10, ItemCore.ClassNames.Length);
        Assert.Equal((int)ItemStatType.Energy + 1, ItemCore.StatTypeNames.Length);
        Assert.Equal(ItemCore.InventoryTypeNames.Length, Enum.GetNames<InventoryType>().Length);
    }

    [Fact]
    public void 属性键对每种类型都非空且可反解()
    {
        for (var type = 1; type <= (int)ItemStatType.Energy; type++)
        {
            var key = ItemCore.AttributeKey(type);
            Assert.False(string.IsNullOrEmpty(key));
            Assert.True(ItemCore.TryParseAttributeKey(key, out var parsed));
            Assert.Equal(type, parsed);
        }
        Assert.Null(ItemCore.AttributeKey(0));
        Assert.Null(ItemCore.AttributeKey(99));
    }
}
