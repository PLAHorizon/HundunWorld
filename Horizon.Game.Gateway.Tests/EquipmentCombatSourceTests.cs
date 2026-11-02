using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Core.Combat;
using Horizon.Game.Message.Combat;
using Horizon.Game.Message.Network;
using Horizon.Model.GameModel;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Moq;
using Orleans;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 装备属性契约与服务端装备取数的测试。
/// <para>
/// 装备数值此前只存在于客户端硬编码表里（服务端模板表零读者），
/// 本套件钉死"服务端读自己的模板 JSON、按求和/取高口径合并"的新口径，
/// 以及任何一步取数失败都只降级、绝不抛异常打断结算。
/// </para>
/// </summary>
public class EquipmentCombatSourceTests
{
    // ── 契约解析 ──────────────────────────────────────────────────

    [Fact]
    public void Parse_maps_each_element_key_onto_its_own_row()
    {
        var parsed = ItemAttributeContract.Parse(
            """{"MetalAttack":100,"WoodAttack":50,"WaterDefense":30,"FirePenetration":40,"EarthDefense":10}""");

        Assert.Equal(100f, parsed.Contribution.Attack.Metal);
        Assert.Equal(50f, parsed.Contribution.Attack.Wood);
        Assert.Equal(30f, parsed.Contribution.Defense.Water);
        Assert.Equal(10f, parsed.Contribution.Defense.Earth);
        Assert.Equal(40f, parsed.Contribution.Penetration.Fire);
        Assert.Equal((int)CombatSourceType.Equipment, parsed.Contribution.Source);
        Assert.Equal(5, parsed.RecognizedKeyCount);
    }

    [Fact]
    public void Parse_accepts_the_generic_layer_and_the_client_short_names()
    {
        var parsed = ItemAttributeContract.Parse("""{"Attack":25,"Defense":12,"HP":300,"MP":200,"CritRate":5}""");

        Assert.Equal(25f, parsed.PhysicalAttack);
        Assert.Equal(12f, parsed.PhysicalDefense);
        Assert.Equal(300f, parsed.MaxHealth);
        Assert.Equal(200f, parsed.MaxEnergy);
        Assert.Equal(0.05f, parsed.CritRate, 5);   // 百分数入库，结算按比例
        Assert.Equal(0f, parsed.Contribution.Attack.Total);
    }

    [Fact]
    public void Parse_is_case_insensitive_and_ignores_unknown_keys_instead_of_guessing()
    {
        // "CritRate" 曾被随手摊进攻击力，导致装备数值与面板对不上——未识别的键必须原样忽略。
        var parsed = ItemAttributeContract.Parse("""{"metalattack":80,"MysteryStat":9999,"Nested":{"a":1},"Name":"长剑"}""");

        Assert.Equal(80f, parsed.Contribution.Attack.Metal);
        Assert.Equal(1, parsed.RecognizedKeyCount);
        Assert.Equal(0f, parsed.PhysicalAttack + parsed.PhysicalDefense);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"武器\"")]
    public void Parse_degrades_to_nothing_instead_of_throwing(string json)
    {
        var parsed = ItemAttributeContract.Parse(json);

        Assert.Equal(0f, parsed.Contribution.Attack.Total);
        Assert.Equal(0, parsed.RecognizedKeyCount);
    }

    [Fact]
    public void Parse_reads_numeric_values_written_as_strings()
    {
        // 内容管道（Excel/WebAdmin 导出）经常把数字写成字符串，不能因此丢掉一整件装备。
        var parsed = ItemAttributeContract.Parse("""{"MetalAttack":"120.5","WoodDefense":" 30 "}""");

        Assert.Equal(120.5f, parsed.Contribution.Attack.Metal);
        Assert.Equal(30f, parsed.Contribution.Defense.Wood);
    }

    /// <summary>
    /// 钉住 scripts/sql/006_seed_equipment_item_templates.sql 里三件代表装备的实际载荷：
    /// 武器给五行攻击、护甲给五行防御、配饰给五行破防。改了 seed 没改这里（或反之）会在这里暴露。
    /// </summary>
    [Fact]
    public void Seeded_weapon_armor_and_accessory_land_on_the_rows_the_rule_promises()
    {
        var sword = ItemAttributeContract.Parse("""{"Attack":15.0,"MetalAttack":30}""");
        Assert.Equal(15f, sword.PhysicalAttack);
        Assert.Equal(30f, sword.Contribution.Attack.Metal);
        Assert.Equal(0f, sword.Contribution.Defense.Total + sword.Contribution.Penetration.Total);

        var clothes = ItemAttributeContract.Parse("""{"Defense":10.0,"HP":50.0,"EarthDefense":20}""");
        Assert.Equal(10f, clothes.PhysicalDefense);
        Assert.Equal(50f, clothes.MaxHealth);
        Assert.Equal(20f, clothes.Contribution.Defense.Earth);

        var necklace = ItemAttributeContract.Parse("""{"MP":20.0,"WoodPenetration":10}""");
        Assert.Equal(20f, necklace.MaxEnergy);
        Assert.Equal(10f, necklace.Contribution.Penetration.Wood);
        Assert.Equal(0f, necklace.Contribution.Attack.Total);
    }

    // ── 多件装备的合并口径 ────────────────────────────────────────

    [Fact]
    public void Combine_sums_attack_but_keeps_the_highest_defense_and_penetration_across_items()
    {
        var parts = new[]
        {
            ItemAttributeContract.Parse("""{"MetalAttack":100,"MetalDefense":300,"FirePenetration":20}"""),
            ItemAttributeContract.Parse("""{"MetalAttack": 60,"MetalDefense":400,"FirePenetration":50}"""),
            ItemAttributeContract.Parse("""{"MetalAttack":  0,"MetalDefense":350,"FirePenetration":99}"""),
        };

        var combined = ItemAttributeContract.Combine(parts);

        Assert.Equal(160f, combined.Contribution.Attack.Metal);   // 100+60+0
        Assert.Equal(400f, combined.Contribution.Defense.Metal);  // max：三件高防不会叠成 1050
        Assert.Equal(99f, combined.Contribution.Penetration.Fire); // max：取最高的一路破防
    }

    [Fact]
    public void Combine_adds_the_generic_layer_across_items()
    {
        var parts = new[]
        {
            ItemAttributeContract.Parse("""{"Attack":10,"HP":100}"""),
            ItemAttributeContract.Parse("""{"Attack":25,"HP":150}"""),
        };

        var combined = ItemAttributeContract.Combine(parts);

        Assert.Equal(35f, combined.PhysicalAttack);
        Assert.Equal(250f, combined.MaxHealth);
    }

    // ── 取数：穿戴快照 + 模板 + 宝石 ──────────────────────────────

    private sealed class Fixture
    {
        public Mock<IGrainFactory> Factory = new();
        public Mock<Horizon.Core.Abstract.IDataContext<Horizon.Entities.GameEntityContext, ItemTemplateEntity, int>> Templates = new();
        public Dictionary<int, ItemTemplateEntity?> Cache = new();

        /// <summary>模板表被真实查询的次数（缓存生效时应当不再增长）。</summary>
        public int TemplateReads;
    }

    /// <summary>造一个"穿戴了这些装备、模板按表返回属性"的服务端形状。</summary>
    private static Fixture CreateFixture(List<ItemInfo>? equipped = null, params (int TemplateId, string? Attributes)[] templates)
    {
        var f = new Fixture();

        var inventory = new Mock<IInventoryGrain>();
        inventory.Setup(g => g.GetEquippedItemInfosAsync())
            .ReturnsAsync(equipped ?? new List<ItemInfo>());
        f.Factory.Setup(x => x.GetGrain<IInventoryGrain>(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Returns(inventory.Object);

        var byId = new Dictionary<int, ItemTemplateEntity?>();
        foreach (var (id, attributes) in templates)
            byId[id] = new ItemTemplateEntity { Id = id, BaseAttributes = attributes };

        f.Templates.Setup(c => c.QueryFirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<ItemTemplateEntity, bool>>>()))
            .ReturnsAsync((System.Linq.Expressions.Expression<Func<ItemTemplateEntity, bool>> predicate, bool _) =>
            {
                f.TemplateReads++;
                // 谓词直接编译后逐个匹配：取数器写的是 t => t.Id == templateId，
                // templateId 是闭包里的局部变量而不是常量，去扒表达式树只会随编译器改写而崩。
                var matcher = predicate.Compile();
                foreach (var entry in byId)
                    if (entry.Value != null && matcher(entry.Value)) return entry.Value;
                return null!;
            });
        return f;
    }

    [Fact]
    public async Task Gather_returns_nothing_when_the_template_context_is_missing()
    {
        // 单测/降级形状：没有模板上下文就没有装备贡献，而不是编一套数字。
        var f = CreateFixture();

        var result = await CombatEquipmentSource.GatherAsync(f.Factory.Object, null, 42, f.Cache);

        Assert.Equal(0f, result.Contribution.Attack.Total);
        Assert.Equal(0f, result.PhysicalAttack);
    }

    [Fact]
    public async Task Gather_tolerates_an_unreachable_inventory_grain()
    {
        var f = new Fixture();
        f.Factory.Setup(x => x.GetGrain<IInventoryGrain>(It.IsAny<Guid>(), It.IsAny<string?>()))
            .Throws(new InvalidOperationException("silo 不可用"));

        var result = await CombatEquipmentSource.GatherAsync(
            f.Factory.Object, f.Templates.Object, 42, f.Cache);

        Assert.Equal(0f, result.Contribution.Defense.Total);
    }

    [Fact]
    public async Task Gather_skips_items_whose_template_has_no_attributes()
    {
        // 装备存在但内容表还没给属性：结果是"不加属性"，且不会因此报错。
        var f = CreateFixture(
            new List<ItemInfo> { new ItemInfo { ItemId = 1, TemplateId = 10001 } },
            (10001, null));

        var result = await CombatEquipmentSource.GatherAsync(f.Factory.Object, f.Templates.Object, 42, f.Cache);

        Assert.Equal(0, result.RecognizedKeyCount);
    }

    [Fact]
    public async Task Cached_template_lookup_avoids_repeated_database_reads()
    {
        var equipped = new List<ItemInfo>
        {
            new ItemInfo { ItemId = 1, TemplateId = 10001 },
            new ItemInfo { ItemId = 2, TemplateId = 10001 },
        };
        var f = CreateFixture(equipped, (10001, """{"MetalAttack":10}"""));

        var first = await CombatEquipmentSource.GatherAsync(f.Factory.Object, f.Templates.Object, 42, f.Cache);
        int readsAfterFirst = f.TemplateReads;
        var second = await CombatEquipmentSource.GatherAsync(f.Factory.Object, f.Templates.Object, 42, f.Cache);

        Assert.Equal(20f, first.Contribution.Attack.Metal);    // 两件各 +10，求和
        Assert.Equal(20f, second.Contribution.Attack.Metal);
        Assert.Equal(1, readsAfterFirst);                      // 同一模板只查一次
        Assert.Equal(readsAfterFirst, f.TemplateReads);        // 第二次全部命中缓存
    }
}
