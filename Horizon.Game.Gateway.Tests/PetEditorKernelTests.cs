using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Pets;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 宠物内核（<c>Horizon.Game.Sim/Pets/</c>）的可执行断言。
/// <para>
/// 三条"必须存在"的理由：
/// ① 天龙悟性/灵性资质公式、WoW 快乐三档倍率、梦幻面板公式是**数值承诺**——
///    文档里写了可复核算例（2000 裸资 → 3649/3733），算例是最强的参照，必须逐位钉住。
/// ② 公式族一律**另写一份独立暴力参照**再与内核互比（新旧互比抓不到同错）。
/// ③ 每条拒绝分支都要有**同夹具的正向对照**：只断言"这只被拒绝"时，
///    如果校验器整个坏掉（永远拒绝），测试照样全绿。
/// </para>
/// </summary>
[Collection("pet-kernel")]
public class PetEditorKernelTests
{
    private readonly ITestOutputHelper _log;

    public PetEditorKernelTests(ITestOutputHelper log) => _log = log;

    // ==================== ① 编号与重复副本 ====================

    [Fact]
    public void 个体编号往返可解析且物种号可反解()
    {
        var id = PetIdentityCore.NewInstanceId(500_012, 7);
        Assert.Equal(50_001_200_007L, id);
        Assert.True(PetIdentityCore.TryParseInstanceId(id, out var species, out var seq));
        Assert.Equal(500_012, species);
        Assert.Equal(7, seq);
    }

    [Fact]
    public void 非法物种与非法序号都落到哨兵而不是回绕()
    {
        // 哨兵必须与"合法的第 0 号"区分开：这里 0 号本身也不合法（序号从 1 起）
        Assert.Equal(PetIdentityCore.UnassignedInstanceId, PetIdentityCore.NewInstanceId(300_001, 1)); // 怪物副本号段
        Assert.Equal(PetIdentityCore.UnassignedInstanceId, PetIdentityCore.NewInstanceId(500_001, 0));
        Assert.Equal(PetIdentityCore.UnassignedInstanceId, PetIdentityCore.NewInstanceId(500_001, -3));
        Assert.Equal(PetIdentityCore.UnassignedInstanceId,
            PetIdentityCore.NewInstanceId(500_001, PetIdentityCore.MaxInstancesPerSpecies + 1));
        Assert.False(PetIdentityCore.TryParseInstanceId(0, out _, out _));
        Assert.False(PetIdentityCore.TryParseInstanceId(-1, out _, out _));
        Assert.False(PetIdentityCore.TryParseInstanceId(300_001_000_001L, out _, out _)); // 别域的号
    }

    [Fact]
    public void 号段把怪物与技能域挡在外面()
    {
        // 怪物用了 1–99999/…300000+，技能用 800000+；宠物只能占 500000–799999。
        Assert.Equal(PetRarityTier.Common, PetIdentityCore.RarityTierOf(500_001));
        Assert.Equal(PetRarityTier.Mutant, PetIdentityCore.RarityTierOf(600_001));
        Assert.Equal(PetRarityTier.Divine, PetIdentityCore.RarityTierOf(650_001));
        Assert.Equal(PetRarityTier.Event, PetIdentityCore.RarityTierOf(700_001));
        Assert.Equal(PetRarityTier.Unknown, PetIdentityCore.RarityTierOf(300_001)); // 怪物副本段
        Assert.Equal(PetRarityTier.Unknown, PetIdentityCore.RarityTierOf(800_001)); // 技能段
        Assert.Equal(PetRarityTier.Unknown, PetIdentityCore.RarityTierOf(0));
    }

    [Fact]
    public void 同物种第二第三只照样发得出号()
    {
        // 这条是"宠物被抓后永久存在"的核心：任何按物种判重的实现都会在这里红。
        var a = PetIdentityCore.NewInstanceId(500_003, 1);
        var b = PetIdentityCore.NewInstanceId(500_003, 2);
        var c = PetIdentityCore.NewInstanceId(500_003, 3);
        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
        Assert.True(PetIdentityCore.TryParseInstanceId(b, out var speciesB, out _));
        Assert.Equal(500_003, speciesB);
    }

    [Fact]
    public void 重复副本对账要数得出外来物种()
    {
        var mine = new[]
        {
            PetIdentityCore.NewInstanceId(500_003, 1),
            PetIdentityCore.NewInstanceId(500_003, 2),
            PetIdentityCore.NewInstanceId(500_003, 3),
        };
        Assert.True(PetIdentityCore.ReconcileDuplicates(500_003, 3, mine, out var foreign0));
        Assert.Equal(0, foreign0);

        Assert.False(PetIdentityCore.ReconcileDuplicates(500_003, 3, new[] { mine[0], mine[1] }, out _));

        var mixed = new[] { mine[0], PetIdentityCore.NewInstanceId(500_004, 1) };
        Assert.False(PetIdentityCore.ReconcileDuplicates(500_003, 2, mixed, out var foreign));
        Assert.Equal(1, foreign);
    }

    [Fact]
    public void 重复副本指纹同入参同输出且随成长改变()
    {
        var q = new PetQualities { Attack = 1200, Defense = 1100, Stamina = 1300, Mana = 900, Speed = 1000 };
        var hash = PetIdentityCore.HashOf(q);
        Assert.Equal(hash, PetIdentityCore.HashOf(q)); // 稳定

        var f1 = PetIdentityCore.DuplicateFingerprint(500_003, 1, hash, 11_000);
        var f2 = PetIdentityCore.DuplicateFingerprint(500_003, 1, hash, 11_000);
        var f3 = PetIdentityCore.DuplicateFingerprint(500_003, 1, hash, 11_500);
        Assert.Equal(f1, f2);
        Assert.NotEqual(f1, f3);
    }

    // ==================== ② 资质公式（天龙可复核算例） ====================

    [Fact]
    public void 天龙资质公式复算文档给的例子()
    {
        // 逆向 §D3：裸资 2000、悟性 10、灵性 10 普通 ⇒ 2000×1.393×1.31 ≈ 3650；超灵 ≈ 3733。
        // 内核走整数截断，得到 3649（文档四舍五入到 3650）；这两个数字是**当前实现的承诺值**。
        var normal = PetStatsCore.EffectiveQuality(2000, 10, 10, superSpirit: false);
        var super = PetStatsCore.EffectiveQuality(2000, 10, 10, superSpirit: true);
        Assert.Equal(3649, normal);
        Assert.Equal(3733, super);

        // 独立参照：完全按文档文字重算一遍（不调用内核）。
        var referenceNormal = (int)(2000L * (10_000 + 3930) / 10_000L * (10_000 + 3100) / 10_000L);
        Assert.Equal(referenceNormal, normal);
    }

    [Fact]
    public void 悟性零级与一级必须可区分()
    {
        // 结构体默认 0 会被误当"表的第一项"；这里断言 0 级真的没有任何加成。
        Assert.Equal(2000, PetStatsCore.EffectiveQuality(2000, 0, 0, false));
        Assert.True(PetStatsCore.EffectiveQuality(2000, 1, 0, false) > 2000);
    }

    [Fact]
    public void 资质加成在越界时钳到末项而不抛()
    {
        Assert.Equal(PetStatsCore.EffectiveQuality(2000, 10, 10, false),
            PetStatsCore.EffectiveQuality(2000, 99, 99, false));
        Assert.Equal(2000, PetStatsCore.EffectiveQuality(2000, -5, -5, false));
    }

    [Fact]
    public void 梦幻面板公式与独立暴力参照在全等级上一致()
    {
        var mismatches = new List<string>();
        var compared = 0;
        var qualities = new PetQualities { Attack = 1450, Defense = 1300, Stamina = 1600, Mana = 1200, Speed = 1100 };
        var points = new PetAttributePoints { Strength = 40, Endurance = 30, Constitution = 50, Spirit = 20, Agility = 10 };

        for (var level = 1; level <= 175; level++)
        {
            foreach (var growth in new[] { 10_000, 10_500, 11_000, 11_500, 12_000, 12_850 })
            {
                var got = PetStatsCore.Compute(qualities, level, growth, points);
                var want = ReferenceCombatStats(qualities, level, growth, points);
                compared++;
                if (got.Hp != want.Hp || got.Mp != want.Mp || got.Attack != want.Attack
                    || got.Defense != want.Defense || got.Speed != want.Speed
                    || got.SpiritPower != want.SpiritPower)
                {
                    mismatches.Add($"Lv{level}/成长{growth}: 内核 {got.Hp},{got.Mp},{got.Attack},{got.Defense},{got.Speed},{got.SpiritPower}"
                                   + $" vs 参照 {want.Hp},{want.Mp},{want.Attack},{want.Defense},{want.Speed},{want.SpiritPower}");
                }
            }
        }

        _log.WriteLine($"对拍 {compared} 组");
        Assert.True(compared >= 175 * 6, $"对拍组数偏少：{compared}");
        Assert.Empty(mismatches);
    }

    /// <summary>
    /// 独立参照：把逆向 §B3 的每一条"取整(...)"用另一套整数写法重算，不调用内核的任何助手。
    /// </summary>
    private static PetCombatStats ReferenceCombatStats(in PetQualities q, int level, int growthBps,
        in PetAttributePoints p)
    {
        long g = growthBps;
        long hp = (level * q.Stamina) / 1000 + (p.Constitution * g * 6) / 10_000;
        long mp = (level * q.Mana) / 500 + (p.Spirit * g * 3) / 10_000;
        long atk = (level * (long)q.Attack * (140_000 + 10 * g)) / 75_500_000 + (p.Strength * g) / 10_000;
        long def = (level * q.Defense) / 433 + (p.Endurance * g * 4) / 30_000;
        long spd = (q.Speed * (long)(p.Agility + 0)) / 1000;
        long ling = (p.Constitution * 3000 + p.Spirit * 7000 + p.Strength * 4000 + p.Endurance * 2000) / 10_000;
        return new PetCombatStats((int)hp, (int)mp, (int)atk, (int)def, (int)spd, (int)ling);
    }

    [Fact]
    public void 档位到成长的映射单调且落在大话实测区间内()
    {
        int prev = 0;
        foreach (PetGrowthTier tier in Enum.GetValues<PetGrowthTier>())
        {
            var g = PetStatsCore.GrowthBpsOf(tier);
            Assert.True(g > prev, $"成长档位 {tier} 没有比上一档高");
            prev = g;
            Assert.InRange(g, PetStatsCore.GrowthMinBps, PetStatsCore.GrowthMaxBps);
        }
    }

    // ==================== ③ 默契四轴 ====================

    [Fact]
    public void 快乐三档的边界落在文档给的分段上()
    {
        Assert.Equal(PetMood.Green, PetBondCore.MoodOf(667));
        Assert.Equal(PetMood.Yellow, PetBondCore.MoodOf(666));
        Assert.Equal(PetMood.Yellow, PetBondCore.MoodOf(334));
        Assert.Equal(PetMood.Red, PetBondCore.MoodOf(333));
        Assert.Equal(PetBondCore.DamageMultGreenBps, PetBondCore.DamageMultiplierBps(PetMood.Green));
        Assert.Equal(7_500, PetBondCore.DamageMultiplierBps(PetMood.Red));
        Assert.Equal(12_500, PetBondCore.DamageMultiplierBps(PetMood.Green));
    }

    [Fact]
    public void 忠诚拒绝概率在门槛处为零在零忠诚处为满()
    {
        Assert.Equal(0, PetBondCore.RefusalChanceBps(70));
        Assert.Equal(0, PetBondCore.RefusalChanceBps(100));
        Assert.Equal(10_000, PetBondCore.RefusalChanceBps(0));
        Assert.Equal(5_000, PetBondCore.RefusalChanceBps(35));
        Assert.Equal(10_000, PetBondCore.RefusalChanceBps(-10)); // 负数不能算出"负概率"
    }

    [Fact]
    public void 出战门槛按状态优先于数值判定()
    {
        var good = PetBondValues.Default;
        Assert.Equal(PetBondBlockReason.None, PetBondCore.CheckDeployable(good, 50, 50));

        // 未孵化 + 快乐极低：必须报"未孵化"而不是"快乐不足"，否则玩家看不到真正的原因
        var miserable = new PetBondValues { Loyalty = 0, Happiness = 0, Lifespan = 0, Intimacy = 0 };
        Assert.Equal(PetBondBlockReason.NotHatched,
            PetBondCore.CheckDeployable(miserable, 50, 50, isHatched: false));
        Assert.Equal(PetBondBlockReason.LowHappiness,
            PetBondCore.CheckDeployable(miserable, 50, 50, isHatched: true));
        Assert.Equal(PetBondBlockReason.NotAvailable,
            PetBondCore.CheckDeployable(miserable, 50, 50, isHatched: true, isAvailable: false));

        // 快乐够、寿命不够
        var old = new PetBondValues { Loyalty = 100, Happiness = 900, Lifespan = 50, Intimacy = 0 };
        Assert.Equal(PetBondBlockReason.LowLifespan, PetBondCore.CheckDeployable(old, 50, 50));
        // 神兽豁免寿命
        Assert.Equal(PetBondBlockReason.None, PetBondCore.CheckDeployable(old, 50, 50, isImmortalLifespan: true));

        // 等级超出主人余量
        Assert.Equal(PetBondBlockReason.OverOwnerLevel, PetBondCore.CheckDeployable(good, 61, 50, ownerLevelSlack: 10));
        Assert.Equal(PetBondBlockReason.None, PetBondCore.CheckDeployable(good, 60, 50, ownerLevelSlack: 10));
    }

    [Fact]
    public void 被击飞的忠诚惩罚按当前值百分比而不是固定值()
    {
        var high = new PetBondValues { Loyalty = 100, Happiness = 700, Lifespan = 100, Intimacy = 0 };
        var afterHigh = PetBondCore.ApplyEvent(high, PetBondEvent.KnockedOut);
        Assert.Equal(80, afterHigh.Loyalty); // 100 × 20% = 20

        var low = new PetBondValues { Loyalty = 10, Happiness = 700, Lifespan = 100, Intimacy = 0 };
        var afterLow = PetBondCore.ApplyEvent(low, PetBondEvent.KnockedOut);
        Assert.Equal(8, afterLow.Loyalty); // 10 × 20% = 2 → 8，固定 −20 会把这只钳成 0

        // 大话口径（固定 −20）在同一夹具下必须真的走另一条路
        var xy2 = PetBondCore.ApplyEvent(high, PetBondEvent.KnockedOut, knockedOutUsesPercentLoyalty: false);
        Assert.Equal(80, xy2.Loyalty);
    }

    [Fact]
    public void 喂食按等级差分档且战斗中被打断()
    {
        Assert.Equal(350, PetBondCore.FeedGain(50, 60));
        Assert.Equal(350, PetBondCore.FeedGain(50, 50));
        Assert.Equal(170, PetBondCore.FeedGain(50, 40));
        Assert.Equal(80, PetBondCore.FeedGain(50, 25));
        Assert.Equal(0, PetBondCore.FeedGain(50, 10));

        var v = new PetBondValues { Loyalty = 50, Happiness = 100, Lifespan = 100, Intimacy = 0 };
        Assert.Equal(100, PetBondCore.ApplyFeed(v, 50, 50, inCombatOrJustHurt: true).Happiness);
        Assert.Equal(450, PetBondCore.ApplyFeed(v, 50, 50, inCombatOrJustHurt: false).Happiness);
    }

    [Fact]
    public void 快乐衰减随忠诚增大而变慢()
    {
        var slow = PetBondCore.DecayHappiness(1000, 100, 10);
        var fast = PetBondCore.DecayHappiness(1000, 0, 10);
        Assert.True(slow > fast, $"满忠诚应该比零忠诚掉得慢：{slow} vs {fast}");
        Assert.Equal(1000, PetBondCore.DecayHappiness(1000, 100, 0));
        Assert.Equal(0, PetBondCore.DecayHappiness(10, 0, 10_000)); // 不许出负
    }

    // ==================== ④ 技能学习与打书 ====================

    [Fact]
    public void 技能格数按品级表与悟性双轨展开()
    {
        // 天龙形状：基础 4，悟性 4 → 5，悟性 7 → 6
        var tlbb = new PetSlotRuleConfig(4, 8, null, 4, 7, 125, 3, 1_500, 7_000, 2, 6, 3);
        Assert.Equal(4, PetSkillCore.MaxSlotsFor(tlbb, 0, 3));
        Assert.Equal(5, PetSkillCore.MaxSlotsFor(tlbb, 0, 4));
        Assert.Equal(6, PetSkillCore.MaxSlotsFor(tlbb, 0, 7));

        // 大话形状：品级表给总格数
        var grade = new int[] { 4, 6, 8, 11, 13 };
        var xy2 = new PetSlotRuleConfig(4, 13, grade, 0, 0, 125, 3, 1_500, 7_000, 2, 6, 3);
        Assert.Equal(4, PetSkillCore.MaxSlotsFor(xy2, 0, 0));
        Assert.Equal(11, PetSkillCore.MaxSlotsFor(xy2, 3, 0));
        Assert.Equal(13, PetSkillCore.MaxSlotsFor(xy2, 4, 0));
        Assert.Equal(13, PetSkillCore.MaxSlotsFor(xy2, 99, 0)); // 越界钳末项，不抛
    }

    [Fact]
    public void 打书优先入空槽且同种子逐位可复现()
    {
        var rule = PetSlotRuleConfig.Default;
        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot(),
            new PetSkillSlot(),
        };
        var cfg = new PetSkillLearnConfig(slots, 4, 50, 0);

        var (after, res) = PetSkillCore.LearnSkill(rule, cfg, 800_002, 3, null, seed: 42);
        Assert.Equal(PetSkillLearnOutcome.Added, res.Outcome);
        Assert.Equal(1, res.SlotIndex);
        Assert.Equal(800_002, after[1].SkillId);

        var (again, res2) = PetSkillCore.LearnSkill(rule, cfg, 800_002, 3, null, seed: 42);
        Assert.Equal(res2.Outcome, res.Outcome);
        Assert.Equal(again[1].SkillId, after[1].SkillId);
        Assert.Equal(res.Roll, res2.Roll);
    }

    [Fact]
    public void 入参槽表不被打书修改()
    {
        var rule = PetSlotRuleConfig.Default;
        var slots = new[] { new PetSkillSlot(), new PetSkillSlot() };
        var cfg = new PetSkillLearnConfig(slots, 4, 50, 0);
        _ = PetSkillCore.LearnSkill(rule, cfg, 800_009, 1, null, seed: 7);
        Assert.Equal(0, slots[0].SkillId);
        Assert.Equal(0, slots[1].SkillId);
    }

    [Fact]
    public void 重复学习同一个技能被拒而不是顶掉自己()
    {
        var rule = PetSlotRuleConfig.Default;
        var slots = new[] { new PetSkillSlot { SkillId = 800_005, Kind = PetSkillSlotKind.Learned, Level = 1 } };
        var cfg = new PetSkillLearnConfig(slots, 4, 50, 0);
        var (_, res) = PetSkillCore.LearnSkill(rule, cfg, 800_005, 5, null, seed: 3);
        Assert.Equal(PetSkillLearnOutcome.RejectedAlreadyKnown, res.Outcome);
    }

    [Fact]
    public void 满槽时天生与锁定技不可被顶且全保护时明确拒绝()
    {
        var rule = PetSlotRuleConfig.Default;
        var allProtected = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Locked, Level = 1, Locked = true },
        };
        var cfg = new PetSkillLearnConfig(allProtected, 2, 200, 0); // 等级 200 关掉顶书开格门
        var (_, res) = PetSkillCore.LearnSkill(rule, cfg, 800_003, 1, null, seed: 1, salt: 9);
        Assert.Equal(PetSkillLearnOutcome.RejectedAllProtected, res.Outcome);

        // 正向对照：同样满槽但其中一格可覆盖，就必须发生覆盖
        var overwritable = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        var cfg2 = new PetSkillLearnConfig(overwritable, 2, 200, 0);
        var (after2, res2) = PetSkillCore.LearnSkill(rule, cfg2, 800_003, 1, null, seed: 1, salt: 9);
        Assert.Equal(PetSkillLearnOutcome.Replaced, res2.Outcome);
        Assert.Equal(800_002, res2.ReplacedSkillId);
        Assert.Equal(800_003, after2[res2.SlotIndex].SkillId);
    }

    [Fact]
    public void 顶书开格只在两道门都过时发生()
    {
        var rule = PetSlotRuleConfig.Default; // 等级≤125 且 已有≤3 时才有开格概率
        // 找一个掷值小于开格概率的种子
        long growSeed = 0;
        for (var s = 1L; s < 5_000; s++)
        {
            if (PetSkillCore.Roll(s, 0) < rule.SlotGrowChanceBps) { growSeed = s; break; }
        }
        Assert.True(growSeed != 0, "找不到命中开格概率的种子——概率配置可能为 0");

        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_003, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        // 正向：等级 50、已有 3 技、开放上限 4 → 开格
        var ok = new PetSkillLearnConfig(slots, 4, 50, 0);
        var (grown, res) = PetSkillCore.LearnSkill(rule, ok, 800_010, 1, null, seed: growSeed);
        Assert.Equal(PetSkillLearnOutcome.SlotGrown, res.Outcome);
        Assert.Equal(4, grown.Length);

        // 反向：等级 126 越门 → 不许开格
        var gated = new PetSkillLearnConfig(slots, 4, 126, 0);
        var (_, resGated) = PetSkillCore.LearnSkill(rule, gated, 800_010, 1, null, seed: growSeed);
        Assert.NotEqual(PetSkillLearnOutcome.SlotGrown, resGated.Outcome);

        // 反向：已有 4 技越门 → 不许开格
        var four = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_003, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_004, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        var crowded = new PetSkillLearnConfig(four, 5, 50, 0);
        var (_, resCrowded) = PetSkillCore.LearnSkill(rule, crowded, 800_010, 1, null, seed: growSeed);
        Assert.NotEqual(PetSkillLearnOutcome.SlotGrown, resCrowded.Outcome);
    }

    [Fact]
    public void 冲突技能命中时新技生效旧技失效并换原因码()
    {
        var rule = new PetSlotRuleConfig(4, 4, null, 4, 7, 125, 3, 0, 0, 2, 6, 3); // 关闭开格与概率顶替
        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        var cfg = new PetSkillLearnConfig(slots, 4, 50, 0);
        var conflicts = new[] { 800_002 };
        var (after, res) = PetSkillCore.LearnSkill(rule, cfg, 800_003, 1, conflicts, seed: 5);
        Assert.Equal(PetSkillLearnOutcome.ReplacedByConflict, res.Outcome);
        Assert.Equal(800_002, res.ReplacedSkillId);
        Assert.Equal(800_003, after[res.SlotIndex].SkillId);
    }

    [Fact]
    public void 领悟满栏不领悟空栏才领悟()
    {
        var full = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        Assert.False(PetSkillCore.TryEpiphany(full, new[] { 800_003 }, 10_000, 1, out _));

        var open = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot(),
        };
        Assert.True(PetSkillCore.TryEpiphany(open, new[] { 800_003 }, 10_000, 1, out var picked));
        Assert.Equal(800_003, picked);
        Assert.Equal(PetSkillSlotKind.Epiphany, open[1].Kind);

        // 候选池全是已会技能 → 不许"领悟到一个空转结果"
        var dup = new[] { new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Learned, Level = 1 }, new PetSkillSlot() };
        Assert.False(PetSkillCore.TryEpiphany(dup, new[] { 800_001 }, 10_000, 1, out _));
    }

    [Fact]
    public void 天生技恢复到第一格并顶掉已有技()
    {
        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800_009, Kind = PetSkillSlotKind.Learned, Level = 3 },
            new PetSkillSlot { SkillId = 800_010, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        var displaced = PetSkillCore.RestoreInnateSkill(slots, 800_001);
        Assert.Equal(800_009, displaced);
        Assert.Equal(800_001, slots[0].SkillId);
        Assert.Equal(PetSkillSlotKind.Innate, slots[0].Kind);

        // 已在槽中时不许再占一次位
        var again = PetSkillCore.RestoreInnateSkill(slots, 800_001);
        Assert.Equal(0, again);
        Assert.Equal(800_010, slots[1].SkillId);
    }

    [Fact]
    public void 锁定技数受模板上限约束且天生技不可锁()
    {
        var rule = PetSlotRuleConfig.Default; // MaxLockableSlots = 2
        var slots = new[]
        {
            new PetSkillSlot { SkillId = 800_001, Kind = PetSkillSlotKind.Innate, Level = 1 },
            new PetSkillSlot { SkillId = 800_002, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_003, Kind = PetSkillSlotKind.Learned, Level = 1 },
            new PetSkillSlot { SkillId = 800_004, Kind = PetSkillSlotKind.Learned, Level = 1 },
        };
        Assert.False(PetSkillCore.TrySetLock(slots, 0, true, rule, out var innateReason));
        Assert.False(string.IsNullOrEmpty(innateReason));

        Assert.True(PetSkillCore.TrySetLock(slots, 1, true, rule, out _));
        Assert.True(PetSkillCore.TrySetLock(slots, 2, true, rule, out _));
        Assert.False(PetSkillCore.TrySetLock(slots, 3, true, rule, out var overReason));
        Assert.Contains("2", overReason);

        // 空槽不能锁
        var withEmpty = new[] { new PetSkillSlot() };
        Assert.False(PetSkillCore.TrySetLock(withEmpty, 0, true, rule, out _));
    }

    [Fact]
    public void 内丹格数表逐段落在文档边界上()
    {
        Assert.Equal(1, PetSkillCore.InnerCoreSlotsFor(0));
        Assert.Equal(1, PetSkillCore.InnerCoreSlotsFor(34));
        Assert.Equal(2, PetSkillCore.InnerCoreSlotsFor(35));
        Assert.Equal(2, PetSkillCore.InnerCoreSlotsFor(74));
        Assert.Equal(3, PetSkillCore.InnerCoreSlotsFor(75));
        Assert.Equal(3, PetSkillCore.InnerCoreSlotsFor(104));
        Assert.Equal(4, PetSkillCore.InnerCoreSlotsFor(105));
        Assert.Equal(4, PetSkillCore.InnerCoreSlotsFor(144));
        Assert.Equal(5, PetSkillCore.InnerCoreSlotsFor(145));
        Assert.Equal(5, PetSkillCore.InnerCoreSlotsFor(174));
        Assert.Equal(6, PetSkillCore.InnerCoreSlotsFor(175));
    }

    // ==================== ⑤ 成长/孵化/蜕变/洗炼 ====================

    [Fact]
    public void 等级上限受主人约束且封顶后不再暗攒经验()
    {
        Assert.Equal(60, PetGrowthCore.LevelCapFor(50, 175, ownerLevelSlack: 10));
        Assert.Equal(175, PetGrowthCore.LevelCapFor(300, 175, ownerLevelSlack: 10));
        Assert.Equal(175, PetGrowthCore.LevelCapFor(0, 175)); // 主人未知时只受物种上限

        var (lv, xp, gained) = PetGrowthCore.AddXp(level: 59, currentXp: 0, xp: 50_000, levelCap: 60);
        Assert.Equal(60, lv);
        Assert.Equal(0, xp); // 封顶后溢出经验清零，不能留成"解封暴升"的暗池
        Assert.True(gained > 0);
    }

    [Fact]
    public void 同级宠物不再分享主人经验()
    {
        Assert.Equal(0, PetGrowthCore.ShareOwnerXp(10_000, 60, 60));
        Assert.Equal(0, PetGrowthCore.ShareOwnerXp(10_000, 61, 60));
        Assert.Equal(1_000, PetGrowthCore.ShareOwnerXp(10_000, 59, 60));
        Assert.Equal(0, PetGrowthCore.ShareOwnerXp(0, 10, 60));
    }

    [Fact]
    public void 孵化时长为零即捕捉即得()
    {
        var (progress, hatched) = PetGrowthCore.AdvanceHatch(0, 0, 0);
        Assert.True(hatched);
        Assert.Equal(0, progress);

        var partial = PetGrowthCore.AdvanceHatch(30, 100, 40);
        Assert.False(partial.Hatched);
        Assert.Equal(70, partial.ProgressSeconds);
        Assert.True(PetGrowthCore.AdvanceHatch(70, 100, 40).Hatched);
    }

    [Fact]
    public void 蜕变前置逐条可归因且有达标对照()
    {
        Assert.Equal(PetMetamorphosisBlockReason.None,
            PetGrowthCore.CheckMetamorphosis(PetGeneration.Hatchling, 60, 60, 80, 70, 3, 3));
        Assert.Equal(PetMetamorphosisBlockReason.LevelTooLow,
            PetGrowthCore.CheckMetamorphosis(PetGeneration.Hatchling, 59, 60, 80, 70, 3, 3));
        Assert.Equal(PetMetamorphosisBlockReason.LoyaltyTooLow,
            PetGrowthCore.CheckMetamorphosis(PetGeneration.Hatchling, 60, 60, 69, 70, 3, 3));
        Assert.Equal(PetMetamorphosisBlockReason.MaterialShortage,
            PetGrowthCore.CheckMetamorphosis(PetGeneration.Hatchling, 60, 60, 80, 70, 2, 3));
        Assert.Equal(PetMetamorphosisBlockReason.AlreadyApex,
            PetGrowthCore.CheckMetamorphosis(PetGeneration.Awakened, 60, 60, 80, 70, 3, 3));

        Assert.Equal(PetGeneration.Adult, PetGrowthCore.NextStage(PetGeneration.Hatchling));
        Assert.Equal(PetGeneration.Awakened, PetGrowthCore.NextStage(PetGeneration.Awakened));
    }

    [Fact]
    public void 洗炼保底命中时给出上界全套()
    {
        var min = new PetQualities { Attack = 800, Defense = 800, Stamina = 800, Mana = 800, Speed = 800 };
        var max = new PetQualities { Attack = 1500, Defense = 1500, Stamina = 1500, Mana = 1500, Speed = 1500 };

        var before = PetGrowthCore.Refine(min, max, 10_000, 12_000, PetGrowthCore.RefineGuaranteeAttempts - 2, 11, true);
        Assert.False(before.GuaranteeHit);

        var hit = PetGrowthCore.Refine(min, max, 10_000, 12_000, PetGrowthCore.RefineGuaranteeAttempts - 1, 11, true);
        Assert.True(hit.GuaranteeHit);
        Assert.Equal(max.Attack, hit.Qualities.Attack);
        Assert.Equal(max.Speed, hit.Qualities.Speed);
        Assert.Equal(12_000, hit.GrowthBps);
        Assert.True(hit.ResetToBaby);
    }

    [Fact]
    public void 洗炼结果始终落在物种资质区间内()
    {
        var min = new PetQualities { Attack = 900, Defense = 700, Stamina = 1_000, Mana = 500, Speed = 800 };
        var max = new PetQualities { Attack = 1_600, Defense = 1_400, Stamina = 1_700, Mana = 1_300, Speed = 1_500 };
        for (long seed = 1; seed <= 300; seed++)
        {
            var r = PetGrowthCore.Refine(min, max, 10_000, 12_000, 0, seed, false);
            Assert.InRange(r.Qualities.Attack, min.Attack, max.Attack);
            Assert.InRange(r.Qualities.Defense, min.Defense, max.Defense);
            Assert.InRange(r.Qualities.Stamina, min.Stamina, max.Stamina);
            Assert.InRange(r.Qualities.Mana, min.Mana, max.Mana);
            Assert.InRange(r.Qualities.Speed, min.Speed, max.Speed);
            Assert.InRange(r.GrowthBps, 10_000, 12_000);
        }
    }

    [Fact]
    public void 悟性安全档失败不回退非安全档失败回退()
    {
        Assert.True(PetGrowthCore.IsSafeComprehensionLevel(4));
        Assert.True(PetGrowthCore.IsSafeComprehensionLevel(7));
        Assert.True(PetGrowthCore.IsSafeComprehensionLevel(9));
        Assert.False(PetGrowthCore.IsSafeComprehensionLevel(5));

        // 找一个失败的种子
        long failSeed = 0;
        for (var s = 1L; s < 100_000; s++)
        {
            if (PetSkillCore.Roll(s, 20_001) >= PetGrowthCore.ComprehensionBaseSuccessBps) { failSeed = s; break; }
        }
        Assert.True(failSeed != 0);

        Assert.Equal(4, PetGrowthCore.TryComprehension(4, failSeed).NewLevel);       // 安全档：不掉
        Assert.Equal(2, PetGrowthCore.TryComprehension(3, failSeed).NewLevel);       // 非安全档：3 → 2
        Assert.True(PetGrowthCore.TryComprehension(3, failSeed).Failed);
        // 已满（10 级）时既不上升也不报失败
        var maxed = PetGrowthCore.TryComprehension(10, failSeed);
        Assert.Equal(10, maxed.NewLevel);
        Assert.False(maxed.Failed);
    }

    [Fact]
    public void 融合度成功率表照抄原文且末两档不单调()
    {
        Assert.Equal(9_000, PetGrowthCore.FusionSuccessBps[0]);
        Assert.Equal(1_500, PetGrowthCore.FusionSuccessBps[7]);
        Assert.Equal(1_000, PetGrowthCore.FusionSuccessBps[8]);
        Assert.Equal(2_000, PetGrowthCore.FusionSuccessBps[9]); // 原文如此：最后一档比倒数第二档高
        Assert.Equal(10, PetGrowthCore.FusionSuccessBps.Length);
    }

    [Fact]
    public void 融合度受悟性与灵性双重前置约束()
    {
        // 正向：悟性/灵性都够
        var ok = PetGrowthCore.TryFusion(3, 1, comprehensionLevel: 10, spiritLevel: 10, out var blocked1);
        Assert.False(blocked1);

        // 反向：前置不够 → 不掷骰且不报失败，只报"被前置挡住"
        _ = PetGrowthCore.TryFusion(9, 1, comprehensionLevel: 2, spiritLevel: 2, out var blocked2);
        Assert.True(blocked2);
    }

    [Fact]
    public void 成长提升道具的收益随成长递减()
    {
        Assert.Equal(50, PetGrowthCore.GrowthBookGainBps(12_000));
        Assert.Equal(10, PetGrowthCore.GrowthBookGainBps(13_000));
        Assert.Equal(1, PetGrowthCore.GrowthBookGainBps(14_000));
        Assert.True(PetGrowthCore.GrowthBookGainBps(11_000) >= PetGrowthCore.GrowthBookGainBps(13_000));
    }

    // ==================== ⑥ AI 智能程度 ====================

    [Fact]
    public void 档位展开的能力位单调递增且可空覆盖不改变默认()
    {
        for (var tier = (int)PetIntellectTier.Mindless; tier < (int)PetIntellectTier.Master; tier++)
        {
            var a = PetAiFlags.DefaultOf((PetIntellectTier)tier);
            var b = PetAiFlags.DefaultOf((PetIntellectTier)(tier + 1));
            Assert.True(Count(a) <= Count(b), $"档位 {tier} 的能力位多于 {tier + 1}");
        }

        // null 覆盖必须取档位默认，而不是 false
        var resolved = PetAiFlags.Resolve(PetIntellectTier.Master, null, null, null, null, null);
        Assert.Equal(PetAiFlags.DefaultOf(PetIntellectTier.Master), resolved);
        var off = PetAiFlags.Resolve(PetIntellectTier.Master, 0, null, null, null, null);
        Assert.False(off.CanAcquireTarget);

        static int Count(PetAiFlags f)
            => (f.CanAcquireTarget ? 1 : 0) + (f.CanAutoCast ? 1 : 0) + (f.CanEvade ? 1 : 0)
               + (f.CanGuardOwner ? 1 : 0) + (f.CanHoldControl ? 1 : 0);
    }

    [Fact]
    public void 低忠诚让宠物整轮拒绝出战而高忠诚不会()
    {
        var flags = PetAiFlags.DefaultOf(PetIntellectTier.Master);
        var targets = new ulong[] { 1UL };
        var skills = new[] { 800_001 };
        var levels = new[] { 10 };

        var refused = PetAiCore.Decide(flags, PetStance.Aggressive, PetCommand.Attack, loyalty: 0, 8_000,
            false, true, targets, skills, levels, null, PetPersonality.Brave, 0, seed: 12_345);
        Assert.Equal(PetAiReason.RefusedByLoyalty, refused.Reason);

        var accepted = PetAiCore.Decide(flags, PetStance.Aggressive, PetCommand.Attack, loyalty: 100, 8_000,
            false, true, targets, skills, levels, null, PetPersonality.Brave, 0, seed: 12_345);
        Assert.NotEqual(PetAiReason.RefusedByLoyalty, accepted.Reason);
    }

    [Fact]
    public void 待命指令冻结跟随且交战中不受跟随影响()
    {
        var cfg = PetFollowConfig.Default;
        var cursor = 0;
        var pathLength = 0;
        var since = 0;
        var step = PetFollowCore.Advance(PetFollowState.Following, cfg, 0f, 0f, 2f, 0f, 1f, PetCommand.Wait,
            default, null, Span<int>.Empty, ref cursor, ref pathLength, ref since);
        Assert.Equal(PetFollowState.Waiting, step.State);
        Assert.Equal(0f, step.NewX, 6);
    }

    [Fact]
    public void 木讷档永不施法而训练档会施法()
    {
        var targets = new ulong[] { 9UL };
        var skills = new[] { 800_001 };
        var levels = new[] { 10 };

        var mindless = PetAiCore.Decide(PetAiFlags.DefaultOf(PetIntellectTier.Mindless),
            PetStance.Aggressive, PetCommand.Attack, 100, 9_000, false, true, targets, skills, levels, null,
            PetPersonality.Brave, 0, seed: 5);
        Assert.Equal(PetAiReason.BasicAttack, mindless.Reason);
        Assert.Equal(0, mindless.SkillId);

        // 正向对照：训练档在某个种子下确实会施法（扫种子，而不是假设 seed 5 一定命中）
        long castSeed = 0;
        for (var s = 1L; s < 2_000 && castSeed == 0; s++)
        {
            var d = PetAiCore.Decide(PetAiFlags.DefaultOf(PetIntellectTier.Trained),
                PetStance.Aggressive, PetCommand.Attack, 100, 9_000, false, true, targets, skills, levels, null,
                PetPersonality.Brave, 0, seed: s);
            if (d.Reason == PetAiReason.Casting) castSeed = s;
        }
        Assert.True(castSeed != 0, "训练档在任何种子下都不施法——自动释放概率表可能全 0");

        var cast = PetAiCore.Decide(PetAiFlags.DefaultOf(PetIntellectTier.Trained),
            PetStance.Aggressive, PetCommand.Attack, 100, 9_000, false, true, targets, skills, levels, null,
            PetPersonality.Brave, 0, seed: castSeed);
        Assert.Equal(PetAiReason.Casting, cast.Reason);
        Assert.Equal(800_001, cast.SkillId);
    }

    [Fact]
    public void 通灵档保留控制技直到主人受击()
    {
        var flags = PetAiFlags.DefaultOf(PetIntellectTier.Master);
        var skills = new[] { 800_777 };
        var levels = new[] { 10 };
        var control = new[] { 0 };

        var holding = PetAiCore.Decide(flags, PetStance.Aggressive, PetCommand.Attack, 100, 9_000,
            ownerUnderAttack: false, ownerEngaged: true, new ulong[] { 3UL }, skills, levels, control,
            PetPersonality.Cautious, 0, seed: 8);
        Assert.Equal(PetAiReason.HoldingControl, holding.Reason);

        // 正向对照：主人受击时可以放
        var guard = PetAiCore.Decide(flags, PetStance.Aggressive, PetCommand.Attack, 100, 9_000,
            ownerUnderAttack: true, ownerEngaged: true, new ulong[] { 3UL }, skills, levels, control,
            PetPersonality.Cautious, 0, seed: 8);
        Assert.NotEqual(PetAiReason.HoldingControl, guard.Reason);
    }

    [Fact]
    public void 出战位只有一个而附体位不占它()
    {
        Assert.True(PetAiCore.CanDeployAlongside(0));
        Assert.False(PetAiCore.CanDeployAlongside(1));
        Assert.Equal(1, PetAiCore.MaxBattlingPets);
        Assert.Equal(3, PetAiCore.MaxPossessedSlots);

        Assert.False(PetAiCore.CanOccupyPossessionSlot(74, 0));
        Assert.True(PetAiCore.CanOccupyPossessionSlot(75, 0));
        Assert.False(PetAiCore.CanOccupyPossessionSlot(75, 3));
    }

    [Fact]
    public void 每条决策原因都有可读且互不相同的文案()
    {
        // 遍历所有原因码：漏一条会让界面显示空字符串，串号会让两种不同的事显示成同一句话。
        var texts = new Dictionary<string, PetAiReason>();
        foreach (PetAiReason reason in Enum.GetValues<PetAiReason>())
        {
            var text = PetAiReasonText.Of(reason);
            Assert.False(string.IsNullOrWhiteSpace(text), $"原因 {reason} 没有文案");

            if (reason == PetAiReason.None) continue; // 兜底文案与 None 同源，允许重名
            Assert.True(texts.TryAdd(text, reason), $"文案 \"{text}\" 同时属于 {reason} 和 {texts[text]}");
        }
        Assert.Equal(Enum.GetValues<PetAiReason>().Length - 1, texts.Count);
    }

    // ==================== ⑦ 装备 ====================

    [Fact]
    public void 镶错部位的灵石不进属性总和但件数照记()
    {
        var slots = new[] { PetEquipSlot.Wrist, PetEquipSlot.Collar, PetEquipSlot.Body };
        var allowed = new[] { PetEquipSlot.Wrist };
        var items = new[]
        {
            new PetGearItem
            {
                ItemId = 1, Slot = PetEquipSlot.Wrist, Star = 1, Appraisal = PetAppraisalTier.Ordinary,
                BaseStat = 100, BaseStatKind = PetEquipStat.Attack,
                Gem = PetGemKind.Health, GemLevel = 10, // 血量灵石镶在护腕上：非法
            },
        };
        var wrong = PetEquipmentCore.Summarize(allowed, items);
        Assert.Equal(1, wrong.CountedPieces);
        Assert.Equal(100, wrong.Attack);
        Assert.Equal(0, wrong.Hp); // 非法镶嵌没有加任何血

        // 正向对照：合法部位就加
        var ok = new[]
        {
            new PetGearItem
            {
                ItemId = 2, Slot = PetEquipSlot.Body, Star = 1, Appraisal = PetAppraisalTier.Ordinary,
                BaseStat = 100, BaseStatKind = PetEquipStat.Defense,
                Gem = PetGemKind.Health, GemLevel = 10,
            },
        };
        var good = PetEquipmentCore.Summarize(slots, ok);
        Assert.Equal(300, good.Hp); // 30 × 10 级
    }

    [Fact]
    public void 超完美鉴定按文档给基础属性加八成()
    {
        var slots = new[] { PetEquipSlot.Wrist };
        var items = new[]
        {
            new PetGearItem
            {
                ItemId = 3, Slot = PetEquipSlot.Wrist, Star = 1, Appraisal = PetAppraisalTier.Superb,
                BaseStat = 100, BaseStatKind = PetEquipStat.Attack,
            },
        };
        var r = PetEquipmentCore.Summarize(slots, items);
        Assert.Equal(180, r.Attack);
        Assert.Equal(18_000, PetEquipmentCore.AppraisalMultiplierBpsOf(PetAppraisalTier.Superb));
    }

    [Fact]
    public void 同部位叠穿只认第一件其余计入驳回()
    {
        var slots = new[] { PetEquipSlot.Wrist };
        var items = new[]
        {
            new PetGearItem { ItemId = 1, Slot = PetEquipSlot.Wrist, Star = 1, BaseStat = 50, BaseStatKind = PetEquipStat.Attack },
            new PetGearItem { ItemId = 2, Slot = PetEquipSlot.Wrist, Star = 1, BaseStat = 80, BaseStatKind = PetEquipStat.Attack },
        };
        var r = PetEquipmentCore.Summarize(slots, items);
        Assert.Equal(1, r.CountedPieces);
        Assert.Equal(1, r.RejectedPieces);
        Assert.Equal(50, r.Attack);
    }

    [Fact]
    public void 空部位集合等于没有宠物装备系统()
    {
        // WoW 档（逆向 §A7）：部位集合为空时任何装备都被驳回
        var items = new[] { new PetGearItem { ItemId = 1, Slot = PetEquipSlot.Wrist, Star = 1, BaseStat = 100 } };
        var r = PetEquipmentCore.Summarize(Array.Empty<PetEquipSlot>(), items);
        Assert.Equal(0, r.CountedPieces);
        Assert.Equal(1, r.RejectedPieces);
        Assert.Equal(0, r.Attack);
    }

    [Fact]
    public void 觉醒格要求三件全部达标()
    {
        var weak = new[]
        {
            new PetGearItem { Quality = 3_000, EnhanceLevel = 10 },
            new PetGearItem { Quality = 3_000, EnhanceLevel = 10 },
            new PetGearItem { Quality = 100, EnhanceLevel = 1 },
        };
        Assert.Equal(0, PetEquipmentCore.CountAwakeningSlots(weak));

        var strong = new[]
        {
            new PetGearItem { Quality = 3_000, EnhanceLevel = 10 },
            new PetGearItem { Quality = 3_000, EnhanceLevel = 10 },
            new PetGearItem { Quality = 3_000, EnhanceLevel = 10 },
        };
        Assert.Equal(1, PetEquipmentCore.CountAwakeningSlots(strong));

        var apex = new[]
        {
            new PetGearItem { Quality = 12_000, EnhanceLevel = 20 },
            new PetGearItem { Quality = 12_000, EnhanceLevel = 20 },
            new PetGearItem { Quality = 12_000, EnhanceLevel = 20 },
        };
        Assert.Equal(3, PetEquipmentCore.CountAwakeningSlots(apex));
    }

    [Fact]
    public void 升星消耗照抄文档给的鳞数()
    {
        Assert.Equal(3, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier75, 2));
        Assert.Equal(5, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier75, 3));
        Assert.Equal(7, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier75, 4));
        Assert.Equal(11, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier75, 5));
        Assert.Equal(36, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier95, 1));
        Assert.Equal(56, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.Tier95, 4));
        Assert.Equal(0, PetEquipmentCore.ScaleCostFor(PetGearLevelTier.None, 3));
    }

    // ==================== ⑧ 辅助（对角色的加成） ====================

    [Fact]
    public void 附体加成不受技能等级影响()
    {
        // 天龙 §D11 明文："附体珍兽的技能等级不影响属性/战力加成"。
        var q = new PetQualities { Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000 };
        var skills = new[] { 800_901 };

        var low = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, skills, new[] { 1 }, new[] { false },
            instanceId: 1, inBattle: true, possessed: true);
        var high = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, skills, new[] { 10 }, new[] { true },
            instanceId: 1, inBattle: true, possessed: true);

        // 技能等级带来的差别只应出现在"出战技能"通路上；把 inBattle 关掉后两者必须逐列相同。
        var lowNoBattle = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, skills, new[] { 1 }, new[] { false },
            1, inBattle: false, possessed: true);
        var highNoBattle = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, skills, new[] { 10 }, new[] { true },
            1, inBattle: false, possessed: true);
        Assert.Equal(lowNoBattle.Strength, highNoBattle.Strength);
        Assert.Equal(lowNoBattle.Spirit, highNoBattle.Spirit);
        Assert.True(low.Strength > lowNoBattle.Strength); // 出战确实更多
    }

    [Fact]
    public void 未出战时辅主特技不生效()
    {
        var q = new PetQualities { Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000 };
        var battle = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, new[] { 800_901 }, new[] { 5 }, new[] { false },
            1, inBattle: true, possessed: false);
        var idle = PetAssistCore.ComputeOwnerBonus(50, q, 1_000, new[] { 800_901 }, new[] { 5 }, new[] { false },
            1, inBattle: false, possessed: false);
        Assert.Equal(25, battle.Strength); // 5 级 × 5 点
        Assert.Equal(0, idle.Strength);
        Assert.Empty(idle.Sources);
    }

    [Fact]
    public void 加成来源清单与属性列逐列对得上()
    {
        var q = new PetQualities { Attack = 1200, Defense = 1300, Stamina = 1400, Mana = 1500, Speed = 1600 };
        var bonus = PetAssistCore.ComputeOwnerBonus(60, q, 2_000,
            new[] { 800_901, 800_902, 800_903, 800_911, 800_912, 800_913, 800_915 },
            new[] { 3, 4, 5, 6, 7, 8, 9 },
            new[] { false, true, false, false, false, true, false },
            instanceId: 500_001_000_007L, inBattle: true, possessed: true);

        // 独立暴力求和：按 Kind 重新聚合一遍，不调用内核的任何汇总
        int str = 0, spi = 0, vit = 0, agi = 0, ste = 0, hp = 0, mp = 0, crit = 0, leech = 0;
        foreach (var s in bonus.Sources)
        {
            switch (s.Kind)
            {
                case PetAssistKind.OwnerStrength: str += s.Value; break;
                case PetAssistKind.OwnerSpirit: spi += s.Value; break;
                case PetAssistKind.OwnerVitality: vit += s.Value; break;
                case PetAssistKind.OwnerAgility: agi += s.Value; break;
                case PetAssistKind.OwnerSteadiness: ste += s.Value; break;
                case PetAssistKind.OwnerHpFromPetHp: hp += s.Value; break;
                case PetAssistKind.OwnerManaRegen: mp += s.Value; break;
                case PetAssistKind.OwnerCritTankChance: crit += s.Value; break;
                case PetAssistKind.OwnerLeechFromDamage: leech += s.Value; break;
            }
        }
        Assert.Equal(str, bonus.Strength);
        Assert.Equal(spi, bonus.Spirit);
        Assert.Equal(vit, bonus.Vitality);
        Assert.Equal(agi, bonus.Agility);
        Assert.Equal(ste, bonus.Steadiness);
        Assert.Equal(hp, bonus.HpFlat);
        Assert.Equal(mp, bonus.ManaRegen);
        Assert.True(crit <= 10_000);
        Assert.True(leech > 0);
        Assert.Equal(leech, bonus.DamageLeechBps);
    }

    [Fact]
    public void 共生与血迹按资质与等级折算()
    {
        Assert.Equal(500, PetAssistCore.SymbiosisBps(0));
        Assert.Equal(1_000, PetAssistCore.SymbiosisBps(1000));
        Assert.Equal(2_500, PetAssistCore.SymbiosisBps(9_999)); // 钳到上限

        Assert.Equal(0, PetAssistCore.BloodlustManaRegen(0, 1000));
        Assert.Equal(60, PetAssistCore.BloodlustManaRegen(60, 1000));
        Assert.Equal(0, PetAssistCore.BloodlustManaRegen(60, 0));
        Assert.Equal(2_400, PetAssistCore.CritTankChanceBps(8));
    }

    // ==================== 反哺合成（多宠 → 一份主人面板） ====================

    private static PetAssistCore.ActivePet AssistPet(long instanceId, int level, int qual, int petHp,
        int[] skills, int[] skillLevels, bool possessed = false, bool inBattle = true)
        => new(instanceId, level,
            new PetQualities { Attack = qual, Defense = qual, Stamina = qual, Mana = qual, Speed = qual },
            petHp, skills, skillLevels, null, possessed, inBattle);

    [Fact]
    public void 反哺合成属性列相加且概率列只取最大()
    {
        var pets = new[]
        {
            // 出战宠：借力 5 级 → 主人力量 25 点（技能通路只看技能等级）
            AssistPet(500_003_000_001L, 60, 1000, 1000, new[] { 800_901 }, new[] { 5 }, false, true),
            // 两只附体宠：各按"等级 × 资质"给主人五维各 240 点（附体通路**不看技能等级**，§D11）
            AssistPet(500_003_000_002L, 60, 1000, 1000, new[] { 800_913 }, new[] { 8 }, true, true),
            AssistPet(500_003_000_003L, 60, 1000, 1000, new[] { 800_913 }, new[] { 10 }, true, true),
        };

        var bonus = PetAssistCore.AggregateOwnerBonuses(pets, out var excluded, out _);
        Assert.Equal(0, excluded);

        // 属性列相加：25 + 240 + 240。附体位给的正是"面板力量"，所以它必须进同一列
        Assert.Equal(25 + 240 + 240, bonus.Strength);
        Assert.Equal(240 + 240, bonus.Spirit);      // 附体给灵气，出战宠没带凝神

        // 概率列取最强那只（10 级忠心 = 3000bps），而不是 2400 + 3000 = 5400
        Assert.Equal(3_000, bonus.CritTankChanceBps);
        Assert.True(bonus.CritTankChanceBps <= 10_000, "概率超过 100% 说明它被相加了");

        // 来源清单逐条保留：1（借力）+ 6（附体 5 维 + 忠心）+ 6
        Assert.NotNull(bonus.Sources);
        Assert.Equal(13, bonus.Sources!.Length);
        var instanceIds = new HashSet<long>();
        foreach (var s in bonus.Sources!) instanceIds.Add(s.FromInstanceId);
        Assert.Equal(3, instanceIds.Count);

        // 独立暴力参照：属性列必须等于"来源清单里非 bps 条目的力量之和"
        int bruteStrength = 0;
        foreach (var s in bonus.Sources!)
        {
            if (!s.IsBasisPoints && s.Kind == PetAssistKind.OwnerStrength) bruteStrength += s.Value;
        }
        Assert.Equal(bonus.Strength, bruteStrength);
    }

    [Fact]
    public void 出战位超一只与附体位超三个都被挡下并计数()
    {
        var skills = new[] { 800_901 };
        var pets = new[]
        {
            AssistPet(500_003_000_001L, 60, 1000, 1000, skills, new[] { 5 }, false),
            AssistPet(500_003_000_002L, 60, 1000, 1000, skills, new[] { 5 }, false),   // 第二只出战 → 挡
            AssistPet(500_003_000_003L, 60, 1000, 1000, null, null, true),
            AssistPet(500_003_000_004L, 60, 1000, 1000, null, null, true),
            AssistPet(500_003_000_005L, 60, 1000, 1000, null, null, true),
            AssistPet(500_003_000_006L, 60, 1000, 1000, null, null, true),              // 第四只附体 → 挡
        };

        var bonus = PetAssistCore.AggregateOwnerBonuses(pets, out var excluded, out var reason);
        Assert.Equal(2, excluded);
        Assert.False(string.IsNullOrEmpty(reason));
        _log.WriteLine(reason);

        // 被挡下的两只不许进属性，也不许进来源清单：1 只借力 + 3 只附体 × 5 维 = 16 条
        Assert.NotNull(bonus.Sources);
        Assert.Equal(16, bonus.Sources!.Length);
        Assert.Equal(25 + 240 * 3, bonus.Strength);

        // 正向对照：只给一只出战宠时，力量就只有它那一份
        var single = PetAssistCore.AggregateOwnerBonuses(new[] { pets[0] }, out var ex2, out _);
        Assert.Equal(0, ex2);
        Assert.Equal(25, single.Strength);
        Assert.Single(single.Sources!);
        Assert.True(bonus.Strength > single.Strength, "附体位没给主人加力量，说明附体通路没进合成");
    }

    [Fact]
    public void 没有个体编号的宠物不参与反哺()
    {
        // 结构体默认 0 会伪装成"合法但第 0 号"，这里必须被排除
        var pets = new[]
        {
            AssistPet(0L, 60, 1000, 1000, new[] { 800_901 }, new[] { 5 }, false),
            AssistPet(500_003_000_009L, 60, 1000, 1000, new[] { 800_901 }, new[] { 5 }, false),
        };
        var bonus = PetAssistCore.AggregateOwnerBonuses(pets, out var excluded, out var reason);
        Assert.Equal(1, excluded);
        Assert.Contains("个体编号", reason);

        Assert.NotNull(bonus.Sources);
        Assert.Equal(1, bonus.Sources!.Length);
        Assert.Equal(500_003_000_009L, bonus.Sources![0].FromInstanceId);
    }

    [Fact]
    public void 空输入合成出零加成而不是崩溃()
    {
        var none = PetAssistCore.AggregateOwnerBonuses(Array.Empty<PetAssistCore.ActivePet>(), out var excluded, out _);
        Assert.Equal(0, excluded);
        Assert.Equal(0, none.Strength);
        Assert.Empty(none.Sources!);

        var nul = PetAssistCore.AggregateOwnerBonuses(null!, out _, out _);
        Assert.Equal(0, nul.Strength);
        Assert.Empty(nul.Sources!);
    }

    [Fact]
    public void 宠物加成叠进主人属性时单列可归因()
    {
        var q = new PetQualities { Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000 };
        var bonus = PetAssistCore.ComputeOwnerBonus(50, q, 1000,
            new[] { 800_901, 800_902, 800_903, 800_904, 800_905 },
            new[] { 10, 10, 10, 10, 10 }, null, 500_003_000_001L, true, false);

        var baseStats = new PetAssistCore.OwnerBaseStats
        {
            Attack = 100, Hp = 1000, Defense = 50, Hit = 200, ManaRegen = 5, BaseCritTakenBps = 1000,
        };
        var final = PetAssistCore.ApplyToOwnerStats(bonus, baseStats);

        // 每只辅助技能 10 级 → 50 点（10×5）；力量与灵气各折 2 点攻击
        Assert.Equal(50, bonus.Strength);
        Assert.Equal(50, bonus.Spirit);
        Assert.Equal(50 * PetAssistCore.AttackPerStrength + 50 * PetAssistCore.AttackPerSpirit, final.FromPetsAttack);
        Assert.Equal(baseStats.Attack + final.FromPetsAttack, final.Attack);

        // 面板要能单列出"其中宠物给了多少"，否则玩家与策划都没法核对
        Assert.True(final.FromPetsAttack > 0);
        Assert.True(final.FromPetsHp > 0);
        Assert.True(final.FromPetsDefense > 0);
        Assert.True(final.FromPetsHit > 0);
        Assert.Equal(baseStats.ManaRegen, final.ManaRegen); // 这套技能里没有血迹，回复不该变
    }

    [Fact]
    public void 替挡概率与主人基准取最大并钳在百分之百()
    {
        var q = new PetQualities { Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000 };
        var bonus = PetAssistCore.ComputeOwnerBonus(50, q, 1000, new[] { 800_913 }, new[] { 10 },
            null, 500_003_000_001L, true, false);
        Assert.Equal(3_000, bonus.CritTankChanceBps);

        // 主人自己已经有 50% 的基准（更强的来源），宠物那份不许把它拉低
        var strong = PetAssistCore.ApplyToOwnerStats(bonus,
            new PetAssistCore.OwnerBaseStats { BaseCritTakenBps = 5_000 });
        Assert.Equal(5_000, strong.CritTakenBps);

        var weak = PetAssistCore.ApplyToOwnerStats(bonus,
            new PetAssistCore.OwnerBaseStats { BaseCritTakenBps = 500 });
        Assert.Equal(3_000, weak.CritTakenBps);

        // 溢出的概率必须被钳住而不是回绕
        var overflow = new PetOwnerBonus { CritTankChanceBps = 9_900 };
        var capped = PetAssistCore.ApplyToOwnerStats(overflow,
            new PetAssistCore.OwnerBaseStats { BaseCritTakenBps = 9_900 });
        Assert.True(capped.CritTakenBps <= 10_000);
    }

    [Fact]
    public void 零加成时主人属性逐列不变()
    {
        var baseStats = new PetAssistCore.OwnerBaseStats
        {
            Attack = 137, Hp = 2048, Defense = 61, Hit = 311, ManaRegen = 7, BaseCritTakenBps = 800,
        };
        var final = PetAssistCore.ApplyToOwnerStats(new PetOwnerBonus(), baseStats);
        Assert.Equal(baseStats.Attack, final.Attack);
        Assert.Equal(baseStats.Hp, final.Hp);
        Assert.Equal(baseStats.Defense, final.Defense);
        Assert.Equal(baseStats.Hit, final.Hit);
        Assert.Equal(baseStats.ManaRegen, final.ManaRegen);
        Assert.Equal(baseStats.BaseCritTakenBps, final.CritTakenBps);
        Assert.Equal(0, final.FromPetsAttack);
    }

    [Fact]
    public void 属性相加不许溢出成负数()
    {
        var bonus = new PetOwnerBonus { Strength = int.MaxValue / 2, Spirit = int.MaxValue / 2, HpFlat = int.MaxValue };
        var final = PetAssistCore.ApplyToOwnerStats(bonus, new PetAssistCore.OwnerBaseStats { Attack = 500, Hp = 100 });
        Assert.Equal(int.MaxValue, final.Attack);
        Assert.Equal(int.MaxValue, final.Hp);
        Assert.True(final.Attack >= 0 && final.Hp >= 0, "溢出回绕成负数——面板会显示负血");
    }

    // ==================== ⑨ 合成 / 合体 ====================

    private static PetParentSnapshot Breeder(long id, int species, int level, int gender,
        PetOrigin origin = PetOrigin.Baby, int happiness = 100, int lifespan = 2_000,
        int breedingCount = 0, bool battling = false, long spouse = 0)
        => new(id, species, level, origin, gender,
            new PetQualities { Attack = 1000, Defense = 1000, Stamina = 1000, Mana = 1000, Speed = 1000 },
            11_000, new[] { 800_001, 800_002 }, happiness, lifespan, breedingCount, battling, spouse, false);

    [Fact]
    public void 繁殖九条前置逐条可归因且有达标对照()
    {
        var a = Breeder(1, 500_003, 40, 0);
        var b = Breeder(2, 500_003, 40, 1);

        // 正向对照：全部满足
        Assert.Equal(PetMergeBlockReason.None, PetMergeCore.ValidateBreed(a, b, partyGoldAvailable: 2));

        Assert.Equal(PetMergeBlockReason.SpeciesMismatch,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_004, 40, 1), 2));
        Assert.Equal(PetMergeBlockReason.GenderMismatch,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 0), 2));
        Assert.Equal(PetMergeBlockReason.GenderMismatch,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 2), 2)); // 未知性别也不许配
        Assert.Equal(PetMergeBlockReason.OriginNotAllowed,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, PetOrigin.Mature), 2));
        Assert.Equal(PetMergeBlockReason.MutantExcluded,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, PetOrigin.Mutant), 2));
        Assert.Equal(PetMergeBlockReason.CurrentlyBattling,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, battling: true), 2));
        Assert.Equal(PetMergeBlockReason.SpouseConflict,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, spouse: 99), 2));
        Assert.Equal(PetMergeBlockReason.BondTooLow,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, happiness: 99), 2));
        Assert.Equal(PetMergeBlockReason.BondTooLow,
            PetMergeCore.ValidateBreed(a, Breeder(2, 500_003, 40, 1, lifespan: 999), 2));
        Assert.Equal(PetMergeBlockReason.PartyOrCostMissing,
            PetMergeCore.ValidateBreed(a, b, partyGoldAvailable: 1));
        Assert.Equal(PetMergeBlockReason.NotEnoughParents,
            PetMergeCore.ValidateBreed(a, Breeder(0, 500_003, 40, 1), 2));

        // 互为配偶是允许的
        Assert.Equal(PetMergeBlockReason.None,
            PetMergeCore.ValidateBreed(Breeder(1, 500_003, 40, 0, spouse: 2), Breeder(2, 500_003, 40, 1, spouse: 1), 2));
    }

    [Fact]
    public void 等级差超五只在未双双达标时拦人()
    {
        // 等级差 10（>5），但双方都已达婚配级 30 → 豁免
        Assert.Equal(PetMergeBlockReason.None,
            PetMergeCore.ValidateBreed(Breeder(1, 500_003, 40, 0), Breeder(2, 500_003, 50, 1), 2));

        // 一方未达婚配级 → 等级差判据先命中（20 与 30 差 10 > 5），这条比"等级不足"更贴切
        Assert.Equal(PetMergeBlockReason.LevelGapTooWide,
            PetMergeCore.ValidateBreed(Breeder(1, 500_003, 20, 0), Breeder(2, 500_003, 30, 1), 2));

        // 等级差合规但一方未达婚配级 → 才报等级不足
        Assert.Equal(PetMergeBlockReason.LevelTooLow,
            PetMergeCore.ValidateBreed(Breeder(1, 500_003, 20, 0), Breeder(2, 500_003, 22, 1), 2));
    }

    [Fact]
    public void 婚配等级阶梯照抄文档()
    {
        Assert.Equal(30, PetMergeCore.MarriageLevelFor(0));
        Assert.Equal(50, PetMergeCore.MarriageLevelFor(1));
        Assert.Equal(70, PetMergeCore.MarriageLevelFor(2));
        Assert.Equal(90, PetMergeCore.MarriageLevelFor(3));
        Assert.Equal(110, PetMergeCore.MarriageLevelFor(4)); // 资料止于 3 次，+20 外推并写进注释
        Assert.Equal(30, PetMergeCore.MarriageLevelFor(-5));
    }

    [Fact]
    public void 繁殖产物是两只二代且有四十八小时领取窗()
    {
        var a = Breeder(1, 500_003, 40, 0);
        var b = Breeder(2, 500_003, 40, 1);
        var r = PetMergeCore.Breed(a, b, 2, seed: 31);
        Assert.True(r.Ok);
        Assert.Equal(2, r.OffspringCount);
        Assert.Equal(PetOrigin.SecondGeneration, r.OffspringOrigin);
        Assert.Equal(PetMergeCore.BreedClaimWindowSeconds, r.ClaimWindowSeconds);
        Assert.False(r.ParentsConsumed); // 繁殖保留父母（与炼妖的关键差别）
        Assert.Equal(48 * 3600, r.ClaimWindowSeconds);
    }

    [Fact]
    public void 临时变异中的个体不许参与繁殖()
    {
        var a = Breeder(1, 500_003, 40, 0);
        var b = Breeder(2, 500_003, 40, 1);
        var r = PetMergeCore.Breed(a, b, 2, seed: 31, hasTimedMutationA: true);
        Assert.Equal(PetMergeBlockReason.MutantExcluded, r.Blocked);
        Assert.Equal(0, r.OffspringCount);
    }

    [Fact]
    public void 炼妖要求等级且变异个体不许参与但有达标对照()
    {
        var ok = Breeder(1, 500_003, 40, 0);
        var young = Breeder(2, 500_003, 29, 1);
        Assert.Equal(PetMergeBlockReason.LevelTooLow, PetMergeCore.Fuse(ok, young, seed: 3).Blocked);

        var mutant = Breeder(3, 500_003, 40, 1, PetOrigin.Mutant);
        Assert.Equal(PetMergeBlockReason.MutantExcluded, PetMergeCore.Fuse(ok, mutant, seed: 3).Blocked);

        // 正向对照：两只达标且非变异 → 不再被前置拦
        var r = PetMergeCore.Fuse(ok, Breeder(2, 500_003, 40, 1), seed: 3);
        Assert.Equal(PetMergeBlockReason.None, r.Blocked);
    }

    [Fact]
    public void 炼妖失败时材料照吃且不产出个体()
    {
        var a = Breeder(1, 500_003, 40, 0);
        var b = Breeder(2, 500_003, 40, 1);

        long failSeed = 0;
        for (var s = 1L; s < 200_000 && failSeed == 0; s++)
        {
            if (PetSkillCore.Roll(s, 40_001) >= PetMergeCore.FuseBaseSuccessBps) failSeed = s;
        }
        Assert.True(failSeed != 0, "找不到失败种子——概率可能配成了 100%");

        var r = PetMergeCore.Fuse(a, b, failSeed);
        Assert.False(r.Success);
        Assert.True(r.ParentsConsumed);
        Assert.Equal(0, r.OffspringCount);

        // 正向对照：命中种子里顶书过的必然成功（3 倍后钳到 95%，而失败种子取的是 roll<9500 的段）
        long hitSeed = 0;
        for (var s = 1L; s < 200_000 && hitSeed == 0; s++)
        {
            if (PetSkillCore.Roll(s, 40_001) < PetMergeCore.FuseBaseSuccessBps) hitSeed = s;
        }
        var good = PetMergeCore.Fuse(a, b, hitSeed);
        Assert.True(good.Ok);
        Assert.Equal(1, good.OffspringCount);
        Assert.True(good.GrowthBps >= PetStatsCore.GrowthMinBps);
    }

    [Fact]
    public void 炼妖资质落在均值的七到一成倍带内()
    {
        var a = Breeder(1, 500_003, 40, 0);
        var b = Breeder(2, 500_003, 40, 1);
        var outOfBand = new List<string>();
        for (long seed = 1; seed <= 500; seed++)
        {
            var r = PetMergeCore.Fuse(a, b, seed);
            if (!r.Ok) continue;
            // 亲本资质都是 1000 ⇒ 均值 1000 ⇒ 合法区间 [700, 1100]
            if (r.Qualities.Attack < 700 || r.Qualities.Attack > 1_100)
                outOfBand.Add($"seed{seed} → {r.Qualities.Attack}（带宽 {r.QualityBandBps}）");
        }
        Assert.Empty(outOfBand);
    }

    [Fact]
    public void 技能继承不重不漏且不超过上限()
    {
        var inherited = PetMergeCore.InheritSkills(new[] { 1, 2, 3, 4, 5 }, new[] { 5, 6, 7, 8, 9, 10 }, 7, 8);
        var seen = new HashSet<int>();
        foreach (var id in inherited)
        {
            Assert.True(seen.Add(id), $"继承结果里有重复技能 {id}");
            Assert.InRange(id, 1, 10);
        }
        Assert.True(inherited.Length <= 8);

        // 独立参照：数一下并集基数，断言结果不超过它
        var union = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        Assert.True(inherited.Length <= union.Count);

        Assert.Empty(PetMergeCore.InheritSkills(null, null, 1, 8));
    }

    // ==================== ⑩ 变异与临时变异 ====================

    [Fact]
    public void 永久变异关掉炼妖与繁殖并抬高资质上限()
    {
        var m = PetMutationCore.FromTier(8, "mutant_t8");
        Assert.False(m.CanFuse);
        Assert.False(m.CanBreed);
        Assert.Equal(10_800, m.QualityCapBps); // 1 + 8 × 1%
        Assert.Equal(12_000, m.LifespanCapBps);

        var none = PetMutationCore.FromTier(0, null);
        Assert.True(none.CanFuse);
        Assert.True(none.CanBreed);
        Assert.Equal(10_000, none.QualityCapBps);

        // 越界档钳到上限而不是抛
        Assert.Equal(PetMutationCore.MaxMutationTier, PetMutationCore.FromTier(99, "x").Tier);
    }

    [Fact]
    public void 未来才生效的临时层不算已生效()
    {
        var t = PetMutationCore.StartTemporary("dye", currentTick: 1_000, durationTicks: 100, 10_500, "red", 0);
        Assert.False(PetMutationCore.IsTemporaryActive(t, currentTick: 500)); // 还没开始
        Assert.True(PetMutationCore.IsTemporaryActive(t, currentTick: 1_000));
        Assert.True(PetMutationCore.IsTemporaryActive(t, currentTick: 1_099));
        Assert.False(PetMutationCore.IsTemporaryActive(t, currentTick: 1_100)); // 到期
    }

    [Fact]
    public void 临时层叠加永久层相乘且到期整体回退()
    {
        var perm = PetMutationCore.FromTier(8, "mutant_t8"); // 10800
        var temp = PetMutationCore.StartTemporary("dye", 0, 100, 10_500, "gold", 1);

        var composed = PetMutationCore.Compose(perm, temp, currentTick: 50);
        Assert.True(composed.TemporaryActive);
        Assert.Equal(1, composed.ExtraSkillSlots);
        Assert.Equal("gold", composed.ColorKey);
        Assert.Equal((int)(10_800L * 10_500L / 10_000L), composed.QualityCapBps);

        // 到期：临时项全部消失，永久项一项不少
        var expired = PetMutationCore.ExpireTemporary(perm);
        Assert.False(expired.TemporaryActive);
        Assert.Equal(0, expired.ExtraSkillSlots);
        Assert.Equal("mutant_t8", expired.ColorKey);
        Assert.Equal(10_800, expired.QualityCapBps);
        Assert.False(expired.CanBreed);

        // 幂等：再回退一次必须逐字段相同（回退写成"减法"就会在这里红）
        var again = PetMutationCore.ExpireTemporary(perm);
        Assert.Equal(expired.QualityCapBps, again.QualityCapBps);
        Assert.Equal(expired.ColorKey, again.ColorKey);
    }

    [Fact]
    public void 两层相乘被封顶拦住()
    {
        var perm = PetMutationCore.FromTier(PetMutationCore.MaxMutationTier, "top");
        var temp = PetMutationCore.StartTemporary("x", 0, 10, 14_000, null, 0);
        var composed = PetMutationCore.Compose(perm, temp, 1);
        Assert.True(composed.QualityCapBps <= PetMutationCore.QualityCapCeilingBps);
    }

    // ==================== ⑪ 跟随与寻路 ====================

    [Fact]
    public void 超距直接传送而leash内走位()
    {
        var cfg = PetFollowConfig.Default;
        var cursor = 0;
        var pathLength = 0;
        var since = 0;

        // 正向：主人在 5 米外（leash 12 米）→ 走位，不传送
        var near = PetFollowCore.Advance(PetFollowState.Following, cfg, 0f, 0f, 5f, 0f, 1f, PetCommand.Follow,
            default, null, Span<int>.Empty, ref cursor, ref pathLength, ref since);
        Assert.False(near.Teleported);
        Assert.True(near.NewX > 0f);
        Assert.True(near.DistanceToOwner > 0f);

        // 反向：主人在 100 米外（> 硬传送 40 米）→ 直接出现在主人身边
        var far = PetFollowCore.Advance(PetFollowState.Following, cfg, 0f, 0f, 100f, 0f, 1f, PetCommand.Follow,
            default, null, Span<int>.Empty, ref cursor, ref pathLength, ref since);
        Assert.True(far.Teleported);
        Assert.Equal(PetFollowState.Teleported, far.State);
        Assert.True(PetFollowCore.Distance(far.NewX, far.NewY, 100f, 0f) <= cfg.FollowDistance + 0.001f);
    }

    [Fact]
    public void 尸体与复活状态不参与跟随()
    {
        var cfg = PetFollowConfig.Default;
        var cursor = 0;
        var pathLength = 0;
        var since = 0;
        var step = PetFollowCore.Advance(PetFollowState.Corpse, cfg, 1f, 1f, 9f, 9f, 1f, PetCommand.Follow,
            default, null, Span<int>.Empty, ref cursor, ref pathLength, ref since);
        Assert.Equal(PetFollowState.Corpse, step.State);
        Assert.Equal(1f, step.NewX, 6);
        Assert.Equal(1f, step.NewY, 6);

        var (state, seconds) = PetFollowCore.OnPetDied(cfg);
        Assert.Equal(PetFollowState.Summoned_Out, state); // 默认不留尸体（天龙口径）
        Assert.Equal(0, seconds);

        var withCorpse = new PetFollowConfig(3f, 12f, 40f, 10, 5.5f, 0.6f, corpseSeconds: 30,
            reviveCastSeconds: 4, summonCooldownSeconds: 0, followStartDelayMs: 300);
        Assert.Equal(PetFollowState.Corpse, PetFollowCore.OnPetDied(withCorpse).State);
        Assert.Equal(30, PetFollowCore.OnPetDied(withCorpse).CorpseSeconds);
    }

    [Fact]
    public void 复活施法要走完时长而零时长即刻归位()
    {
        var cfg = PetFollowConfig.Default; // 4 秒
        var mid = PetFollowCore.AdvanceRevive(2f, cfg);
        Assert.Equal(PetFollowState.Reviving, mid.State);
        Assert.Equal(2f, mid.RemainingCastSeconds, 5);

        var done = PetFollowCore.AdvanceRevive(4f, cfg);
        Assert.Equal(PetFollowState.Returning, done.State);
        Assert.Equal(0f, done.RemainingCastSeconds, 5);

        var instant = new PetFollowConfig(3f, 12f, 40f, 10, 5.5f, 0.6f, 0, 0, 0, 0);
        Assert.Equal(PetFollowState.Returning, PetFollowCore.AdvanceRevive(0f, instant).State);
    }

    [Fact]
    public void 近距离不走导航网格()
    {
        var cfg = PetFollowConfig.Default;
        Assert.False(PetFollowCore.ShouldUseNavigation(cfg, cfg.FollowDistance * 2f));      // 边界：不大于
        Assert.True(PetFollowCore.ShouldUseNavigation(cfg, cfg.FollowDistance * 2f + 0.1f));
    }

    [Fact]
    public void 跟随目标落在主人身边而不是宠物身前()
    {
        // 主人在 (10,0)、宠物在 (0,0)、跟随距离 3 ⇒ 目标应当在主人身边 3 米处（也就是 x=7），
        // 而不是"从宠物出发走 3 米"（x=3）——后者会让宠物永远停在离主人 7 米的地方。
        float gx = 10f, gy = 0f;
        PetFollowCore.OffsetToFollowDistance(ref gx, ref gy, 0f, 0f, 3f);
        Assert.Equal(7f, gx, 4);
        Assert.Equal(0f, gy, 4);

        // 宠物已经在跟随距离之内：目标回退到宠物自己所在位置（等价于不再靠近）
        float ix = 1f, iy = 0f;
        PetFollowCore.OffsetToFollowDistance(ref ix, ref iy, 0f, 0f, 3f);
        Assert.Equal(0f, ix, 5);

        // 与主人重合：不许除零，给固定偏移
        float ox = 0f, oy = 0f;
        PetFollowCore.OffsetToFollowDistance(ref ox, ref oy, 0f, 0f, 3f);
        Assert.Equal(3f, ox, 5);
    }

    [Fact]
    public void 有导航网格时宠物走真实路径且不穿墙()
    {
        // 造一条必须绕路的网格：第 5 列整列是墙，只在第 10 行留一个缺口。
        const int cells = 12;
        var cost = new byte[cells * cells];
        for (var i = 0; i < cost.Length; i++) cost[i] = 10;
        for (var y = 0; y < cells; y++)
        {
            if (y != 10) cost[y * cells + 5] = 0; // 0 = 不可走
        }

        var grid = new MonsterNavGrid
        {
            Width = cells,
            Height = cells,
            CellSize = 1f,
            OriginX = 0f,
            OriginY = 0f,
            CellCost = cost,
        };
        Assert.True(grid.IsValid);

        var scratch = new MonsterNavScratch();
        scratch.EnsureCapacity(cells * cells);
        var path = new int[cells * cells];

        // 独立参照：直线上 (0,0)→(11,0) 必然穿过第 5 列的墙，所以"路径长度 == 12"就是错的。
        var result = MonsterPathCore.FindPath(in grid, scratch, 0f, 0f, 11f, 0f, path, out var length);
        Assert.Equal(MonsterPathResult.Found, result);
        Assert.True(length > 15, $"绕墙路径却只有 {length} 格——多半直接穿墙了");

        // 逐格验：路径上任何一格都可走、相邻两格距离不超过 1 格、且首格确实从起点出发
        var first = path[0];
        Assert.True(cost[first] > 0, "路径起点就在墙里");
        Assert.True(Math.Abs(first % cells - 0) <= 1 && Math.Abs(first / cells - 0) <= 1,
            $"路径第一格不在起点 (0,0) 的邻域：格 {first}");
        var prev = first;
        for (var i = 1; i < length; i++)
        {
            var cell = path[i];
            Assert.True(cost[cell] > 0, $"第 {i} 步落进了墙里：格 {cell}");
            var dx = Math.Abs(cell % cells - prev % cells);
            var dy = Math.Abs(cell / cells - prev / cells);
            Assert.True(dx <= 1 && dy <= 1, $"第 {i} 步跳了 {dx},{dy} 格");
            prev = cell;
        }

        // 跟随推进：有网格 + 距离足够 → 必须走导航，而不是直线穿墙
        var cfg = PetFollowConfig.Default;
        var cursor = 0;
        var pathLength = 0;
        var since = 0;
        var step = PetFollowCore.Advance(PetFollowState.Returning, cfg, 0f, 0f, 10f, 0f, 1f,
            PetCommand.Follow, grid, scratch, path.AsSpan(), ref cursor, ref pathLength, ref since);
        Assert.True(step.UsedNavigation, "有网格且距离足够时应当走导航");
        Assert.True(pathLength > 0 && pathLength <= path.Length, $"有效路径长度被写坏了：{pathLength}");
        Assert.True(step.NewX >= 0f && step.NewX <= 4f, $"导航第一步就冲到跟随点之外：X={step.NewX}");
    }

    // ==================== ⑫ 捕捉与个体出生 ====================

    private static PetSpeciesTemplate Catchable(PetCaptureMode mode, int baseChanceBps = 9_000,
        int mutationChanceBps = 0, int hatchSeconds = 0)
        => new()
        {
            SpeciesId = 500_003,
            Name = "赤瞳狼",
            CarryLevelMin = 25,
            CarryLevelMax = 60,
            AllowedOrigins = new[] { PetOrigin.Wild },
            QualityMin = new PetQualities { Attack = 900, Defense = 900, Stamina = 900, Mana = 900, Speed = 900 },
            QualityMax = new PetQualities { Attack = 1500, Defense = 1500, Stamina = 1500, Mana = 1500, Speed = 1500 },
            AllowedGrowthTiers = new[] { PetGrowthTier.Ordinary, PetGrowthTier.Good },
            CaptureMode = mode,
            CaptureBaseChanceBps = baseChanceBps,
            MutationChanceBps = mutationChanceBps,
            HatchSeconds = hatchSeconds,
            Published = true,
        };

    [Fact]
    public void 捕捉同物种第二只照样拿到新编号()
    {
        var species = Catchable(PetCaptureMode.CombatWeaken);

        long hitSeed = 0;
        for (var s = 1L; s < 100_000 && hitSeed == 0; s++)
        {
            if (PetSkillCore.Roll(s, 60_001) < 9_500) hitSeed = s;
        }
        Assert.True(hitSeed != 0);

        var first = PetCaptureCore.Attempt(new PetCaptureRequest(species, 50, 5, 1_000,
            false, false, 3, 0, false, true, existingInstancesOfSpecies: 0, hitSeed));
        var second = PetCaptureCore.Attempt(new PetCaptureRequest(species, 50, 5, 1_000,
            false, false, 3, 0, false, true, existingInstancesOfSpecies: 1, hitSeed));

        Assert.True(first.Success && second.Success);
        Assert.NotEqual(first.NewInstanceId, second.NewInstanceId);
        Assert.Equal(50_000_300_001L, first.NewInstanceId);
        Assert.Equal(50_000_300_002L, second.NewInstanceId);
    }

    [Fact]
    public void 号段耗尽报身份问题而不是复用别人的号()
    {
        var species = Catchable(PetCaptureMode.CombatWeaken);
        long hitSeed = 0;
        for (var s = 1L; s < 100_000 && hitSeed == 0; s++)
        {
            if (PetSkillCore.Roll(s, 60_001) < 9_500) hitSeed = s;
        }

        var r = PetCaptureCore.Attempt(new PetCaptureRequest(species, 50, 5, 1_000,
            false, false, 3, 0, false, true,
            existingInstancesOfSpecies: PetIdentityCore.MaxInstancesPerSpecies, hitSeed));
        Assert.False(r.Success);
        Assert.Equal(PetIdentityIssue.SequenceExhausted, r.IdentityIssue);
        Assert.Equal(PetIdentityCore.UnassignedInstanceId, r.NewInstanceId);
    }

    [Fact]
    public void 捕捉前置逐条可归因且有达标对照()
    {
        var divine = Catchable(PetCaptureMode.Uncatchable);
        Assert.Equal(PetCaptureBlockReason.SpeciesUncatchable,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(divine, 50, 5, 1_000,
                true, false, 3, 0, false, true, 0, 1)));

        var wolf = Catchable(PetCaptureMode.TameGuidance);
        Assert.Equal(PetCaptureBlockReason.TargetNotBeast,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 50, 5, 10_000,
                false, false, 3, 20, false, true, 0, 1)));
        Assert.Equal(PetCaptureBlockReason.ActivePetOccupiesSlot,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 50, 5, 10_000,
                true, true, 3, 20, false, true, 0, 1)));
        Assert.Equal(PetCaptureBlockReason.GuidanceInterrupted,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 50, 5, 10_000,
                true, false, 3, 20, true, true, 0, 1)));
        Assert.Equal(PetCaptureBlockReason.NoFreeSlot,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 50, 5, 10_000,
                true, false, 0, 20, false, true, 0, 1)));
        Assert.Equal(PetCaptureBlockReason.PlayerLevelTooLow,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 10, 5, 10_000,
                true, false, 3, 20, false, true, 0, 1)));

        // 正向对照：驯服走完全程、有栏位、是野兽 → 放行
        Assert.Equal(PetCaptureBlockReason.None,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(wolf, 50, 5, 10_000,
                true, false, 3, 20, false, true, 0, 1)));

        var weaken = Catchable(PetCaptureMode.CombatWeaken);
        Assert.Equal(PetCaptureBlockReason.TargetTooHealthy,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(weaken, 50, 5, 9_000,
                false, false, 3, 0, false, true, 0, 1)));
        Assert.Equal(PetCaptureBlockReason.None,
            PetCaptureCore.ValidatePreconditions(new PetCaptureRequest(weaken, 50, 5, 2_000,
                false, false, 3, 0, false, true, 0, 1)));
    }

    [Fact]
    public void 驯服不看血量而战斗捕捉只看血量()
    {
        var tame = Catchable(PetCaptureMode.TameGuidance);
        // 引导没走完 → 概率必须为 0（WoW：引导没结束根本不出结果）
        Assert.Equal(0, PetCaptureCore.ChanceBps(new PetCaptureRequest(tame, 50, 5, 10_000,
            true, false, 3, 5, false, true, 0, 1)));
        // 引导走完 → 高概率，且满血也一样
        Assert.True(PetCaptureCore.ChanceBps(new PetCaptureRequest(tame, 50, 5, 10_000,
            true, false, 3, 20, false, true, 0, 1)) >= 9_000);

        var weaken = Catchable(PetCaptureMode.CombatWeaken, baseChanceBps: 1_000);
        var healthy = PetCaptureCore.ChanceBps(new PetCaptureRequest(weaken, 50, 0, 8_000,
            false, false, 3, 0, false, true, 0, 1));
        var nearly = PetCaptureCore.ChanceBps(new PetCaptureRequest(weaken, 50, 0, 1_000,
            false, false, 3, 0, false, true, 0, 1));
        Assert.True(nearly > healthy, $"残血应当更容易抓：{nearly} vs {healthy}");
        Assert.True(nearly <= PetCaptureCore.ChanceCeilingBps);
    }

    [Fact]
    public void 带孵化时长的物种抓到的是蛋()
    {
        var egg = Catchable(PetCaptureMode.CombatWeaken, hatchSeconds: 600);
        long hitSeed = 0;
        for (var s = 1L; s < 100_000 && hitSeed == 0; s++)
        {
            if (PetSkillCore.Roll(s, 60_001) < 9_500) hitSeed = s;
        }
        var r = PetCaptureCore.Attempt(new PetCaptureRequest(egg, 50, 5, 1_000,
            false, false, 3, 0, false, true, 0, hitSeed));
        Assert.True(r.Success);
        Assert.Equal(PetGeneration.Egg, r.Stage);

        var direct = Catchable(PetCaptureMode.CombatWeaken);
        var r2 = PetCaptureCore.Attempt(new PetCaptureRequest(direct, 50, 5, 1_000,
            false, false, 3, 0, false, true, 0, hitSeed));
        Assert.Equal(PetGeneration.Hatchling, r2.Stage);
    }

    [Fact]
    public void 捕捉出的资质落在物种区间内()
    {
        var species = Catchable(PetCaptureMode.CombatWeaken);
        for (long seed = 1; seed <= 400; seed++)
        {
            var origin = PetCaptureCore.RollOrigin(species, seed);
            var q = PetCaptureCore.RollQualities(species, origin, seed);
            Assert.InRange(q.Attack, 900, 1_500);
            Assert.InRange(q.Speed, 900, 1_500);
            var g = PetCaptureCore.RollGrowth(species, origin, seed);
            Assert.True(g == 10_000 || g == 10_500, $"成长 roll 出了模板不允许的档位：{g}");
        }
    }

    // ==================== ⑬ 内容装配 ====================

    [Fact]
    public void 空内容包不覆盖已有内容并报出状态()
    {
        PetContent.ResetToHardcoded();
        var baseline = PetContent.Published().Count;
        Assert.True(baseline > 0, "硬编码回落目录为空");

        var empty = PetContent.Install(new PetContentBundle());
        Assert.Equal(PetContentLoadStatus.Empty, empty.Status);
        Assert.Equal(baseline, PetContent.Published().Count); // 没被清掉

        var nul = PetContent.Install(null);
        Assert.Equal(PetContentLoadStatus.Malformed, nul.Status);

        // 装配一份新包后回落目录不再被读到
        var bundle = new PetContentBundle { ContentRevision = 7 };
        bundle.Species[500_900] = new PetSpeciesTemplate { SpeciesId = 500_900, Name = "测试种", Published = true };
        Assert.Equal(PetContentLoadStatus.Installed, PetContent.Install(bundle).Status);
        Assert.Equal(7, PetContent.Revision);
        Assert.True(PetContent.TryGetTemplate(500_900, out _));

        PetContent.ResetToHardcoded();
        Assert.False(PetContent.TryGetTemplate(500_900, out _));
        Assert.True(PetContent.TryGetTemplate(500_003, out _));
    }

    [Fact]
    public void 硬编码目录的每一行物种号都在合法号段()
    {
        foreach (var row in PetSpeciesCatalog.Rows)
        {
            Assert.True(PetIdentityCore.IsSpeciesId(row.SpeciesId),
                $"目录行 {row.Name}({row.SpeciesId}) 落在宠物号段之外");
        }
    }

    // ==================== ⑭ 内容快照装载（DS 运行时的入口） ====================

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Fixture(string name) => Path.Combine(RepoRoot(), "HundunWorld", "Data", "Pets", name);

    [Fact]
    public void 内容快照装载合法物种并挡下越界号段()
    {
        PetContent.ResetToHardcoded();
        var receipt = PetContentLoader.LoadFile(Fixture("dev_content.json"));
        try
        {
            Assert.True(receipt.Ok, "装载失败：" + string.Join(" | ", receipt.Problems));
            Assert.Equal(2, receipt.Species);
            Assert.Equal(1, receipt.Rejected);
            Assert.Equal(3, receipt.ContentRevision);
            Assert.Contains(receipt.Problems, p => p.Contains("300007"));
            _log.WriteLine(receipt.Describe());

            Assert.True(PetContent.TryGetTemplate(500_003, out var wolf));
            Assert.Equal("pet_red_wolf", wolf.ModelKey);
            Assert.Equal(1_500, wolf.QualityMax.Attack);
            Assert.Single(wolf.InnateSkillIds);
            Assert.Equal(3, wolf.LearnableSkillIds.Length);
            Assert.Single(wolf.EpiphanySkillIds);
            Assert.Equal(2, wolf.AssistSkillIds.Length);
            Assert.Equal(PetCaptureMode.CombatWeaken, wolf.CaptureMode);
            Assert.Equal(0.6f, wolf.Follow.ArriveEpsilon, 3);
            Assert.Equal(40f, wolf.Follow.TeleportBeyondRadius, 3);
            Assert.Equal(3, wolf.EquipSlots.Length);   // 夹具给的是 护腕/项圈/躯干（梦幻三部位）

            Assert.True(PetContent.TryGetTemplate(650_001, out var divine));
            Assert.True(divine.ImmortalLifespan);
            Assert.Equal(PetIntellectTier.Master, divine.IntellectTier);
            Assert.Equal(PetCaptureMode.Uncatchable, divine.CaptureMode);
            Assert.Equal(7_200, divine.HatchSeconds);
            Assert.Equal(6, divine.EquipSlots.Length);

            // 神兽的寿命门槛被永生豁免，而初值是 0：这正是"神兽出战不掉寿命"的可执行表达
            Assert.Equal(PetBondBlockReason.None,
                PetBondCore.CheckDeployable(divine.InitialBond, 175, 175, divine.OwnerLevelSlack, true));
            Assert.Equal(PetBondBlockReason.LowLifespan,
                PetBondCore.CheckDeployable(divine.InitialBond, 175, 175, divine.OwnerLevelSlack, false));
        }
        finally
        {
            PetContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 能力位的三态覆盖在装载后仍然区分未配与显式关()
    {
        PetContent.ResetToHardcoded();
        var receipt = PetContentLoader.LoadFile(Fixture("dev_content.json"));
        try
        {
            Assert.True(receipt.Ok);
            PetContent.TryGetTemplate(650_001, out var divine);
            Assert.Null(divine.AiFlagOverrides[0]);        // 未配 → 跟档位
            Assert.Equal(1, divine.AiFlagOverrides[2]);    // 显式开

            // null 与 0 展开后必须不同：都当 false 会让"没配"变成"关掉了"
            var nullResolved = PetAiFlags.Resolve(divine.IntellectTier, divine.AiFlagOverrides[0],
                divine.AiFlagOverrides[1], divine.AiFlagOverrides[2], divine.AiFlagOverrides[3], divine.AiFlagOverrides[4]);
            Assert.True(nullResolved.CanAcquireTarget);   // 通灵档默认开，未被覆盖
            Assert.True(nullResolved.CanEvade);           // 显式 1

            var forcedOff = PetAiFlags.Resolve(divine.IntellectTier, 0, 0, 0, 0, 0);
            Assert.False(forcedOff.CanAcquireTarget);
            Assert.False(forcedOff.CanEvade);
        }
        finally
        {
            PetContent.ResetToHardcoded();
        }
    }

    [Fact]
    public void 缺失文件与坏内容都大声失败且不动已装内容()
    {
        PetContent.ResetToHardcoded();
        var baseline = PetContent.Published().Count;
        Assert.True(baseline > 0);

        var missing = PetContentLoader.LoadFile(Fixture("does_not_exist.json"));
        Assert.False(missing.Ok);
        Assert.Contains(missing.Problems, p => p.Contains("不存在"));
        Assert.Equal(baseline, PetContent.Published().Count); // 没被清成 0

        // 号段全非法 → 零可用物种，必须报失败而不是"装成功了但里面是空的"
        var bad = Path.GetTempFileName();
        try
        {
            File.WriteAllText(bad, "{\"contentRevision\":9,\"species\":[{\"speciesId\":123,\"name\":\"x\"}]}");
            var receipt = PetContentLoader.LoadFile(bad);
            Assert.False(receipt.Ok);
            Assert.Equal(1, receipt.Rejected);
            Assert.Contains(receipt.Problems, p => p.Contains("零可用物种"));
        }
        finally
        {
            File.Delete(bad);
        }

        // 根节点不是对象 / 缺 species 都要各报一条，不能都塞进"解析失败"
        var notArray = Path.GetTempFileName();
        try
        {
            File.WriteAllText(notArray, "{\"contentRevision\":1}");
            var receipt = PetContentLoader.LoadFile(notArray);
            Assert.False(receipt.Ok);
            Assert.Contains(receipt.Problems, p => p.Contains("species"));
        }
        finally
        {
            File.Delete(notArray);
        }
    }

    [Fact]
    public void 资质区间反了的条目被挡下而不是静默返回下界()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file,
                "{\"contentRevision\":1,\"species\":[{\"speciesId\":500500,"
                + "\"qualityMin\":{\"attack\":2000,\"defense\":1000,\"stamina\":1000,\"mana\":1000,\"speed\":1000},"
                + "\"qualityMax\":{\"attack\":1000,\"defense\":1000,\"stamina\":1000,\"mana\":1000,\"speed\":1000}}]}");
            var receipt = PetContentLoader.LoadFile(file);
            Assert.False(receipt.Ok);
            Assert.Equal(1, receipt.Rejected);
            Assert.Contains(receipt.Problems, p => p.Contains("下界大于上界"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void 装载器与客户端外观表读的是同一组字面键名()
    {
        // 导出侧（CLI）写、装载器读、客户端外观目录读——三处都按字面量取值。
        // 这条把装载器钉回协议上：任何一侧改字段名，这里会红而不是线上读空。
        var loader = ReadSimSource("PetContentLoader.cs");
        foreach (var key in new[]
                 {
                     "speciesId", "modelKey", "prefabPath", "qualityMin", "qualityMax", "slotRule",
                     "initialBond", "follow", "aiFlagOverrides", "equipSlots", "allowedOrigins",
                     "allowedGrowthTiers", "allowedPersonalities", "innateSkillIds", "learnableSkillIds",
                     "epiphanySkillIds", "assistSkillIds", "contentRevision", "species",
                 })
        {
            Assert.Contains("\"" + key + "\"", loader, StringComparison.Ordinal);
        }
    }

    private static string ReadSimSource(string file)
    {
        var path = Path.Combine(RepoRoot(), "Horizon.Game.Sim", "Pets", file);
        Assert.True(File.Exists(path), "源文件不存在：" + path);
        return File.ReadAllText(path);
    }
}
