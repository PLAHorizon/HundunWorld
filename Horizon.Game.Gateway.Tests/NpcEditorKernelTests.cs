using System;
using System.Collections.Generic;
using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// NPC 编辑器内核（<c>Horizon.Game.Sim/Npcs</c>）的可执行断言。
/// <para>
/// 四条"必须存在"的理由：
/// ① 指示器四态与优先级是**玩家第一眼看到的承诺**（黄 <c>!</c> 却没任务可接 = 直接砸信誉），
///    所以 <c>None ⇔ 全空</c> 这条双向不变量必须被钉住，而不是只测"能算出一个符号"。
/// ② 服务位图的**每个拒绝分档都要有正向同夹具对照**：只测拒绝分支时，
///    "这条分支永远不触发"与"这条分支正确触发"在绿灯下长得一模一样。
/// ③ 经验与让分**必须逐字等于怪物内核的输出**——本层只允许做分派，
///    出现第二套曲线就是"为什么这次没经验"有两个答案（记忆 [改全局判定要量所有消费者]）。
/// ④ 库存补货与缺料统计各配一份**独立暴力参照**（不同算法：逐周期推进 / 双层循环），
///    新旧同错的互比抓不出问题（记忆 [等价测试的参照要另写暴力版]）。
/// </para>
/// </summary>
public class NpcEditorKernelTests
{
    private readonly ITestOutputHelper _log;

    public NpcEditorKernelTests(ITestOutputHelper log) => _log = log;

    // ==================== ① 任务指示器 ====================

    /// <summary>不变量：符号为 None 当且仅当四类计数全为 0（两个方向都断言）。</summary>
    [Fact]
    public void Indicator_NoneIsExactlyAllEmpty_BothDirections()
    {
        // 正向：全空 → None。
        var empty = new NpcQuestCounts(0, 0, 0, 0);
        Assert.Equal(NpcQuestIndicatorKind.None, NpcQuestIndicatorCore.Resolve(empty));
        Assert.False(empty.HasAny);
        Assert.Equal(string.Empty, NpcQuestIndicatorCore.GlyphOf(NpcQuestIndicatorKind.None));

        // 反向：任一类非 0 → 一定不是 None，且 glyph 非空。
        var cases = new[]
        {
            new NpcQuestCounts(1, 0, 0, 0),
            new NpcQuestCounts(0, 1, 0, 0),
            new NpcQuestCounts(0, 0, 1, 0),
            new NpcQuestCounts(0, 0, 0, 1),
        };

        foreach (var c in cases)
        {
            var kind = NpcQuestIndicatorCore.Resolve(c);
            Assert.NotEqual(NpcQuestIndicatorKind.None, kind);
            Assert.True(c.HasAny);
            Assert.NotEqual(string.Empty, NpcQuestIndicatorCore.GlyphOf(kind));
            Assert.True(NpcQuestIndicatorCore.CountOf(c, kind) > 0,
                $"符号 {kind} 对应的条数必须 >0，否则数字前缀与符号自相矛盾：{c.Describe()}");
        }
    }

    /// <summary>优先级：可交付 &gt; 可接 &gt; 进行中 &gt; 条件不足。数字前缀必须跟着同一优先级。</summary>
    [Fact]
    public void Indicator_PriorityOrderIsFixedAndPrefixFollowsIt()
    {
        var all = new NpcQuestCounts(1, 2, 3, 4);
        Assert.Equal(NpcQuestIndicatorKind.TurnInReady, NpcQuestIndicatorCore.Resolve(all));
        Assert.Equal("?Y", NpcQuestIndicatorCore.GlyphOf(NpcQuestIndicatorCore.Resolve(all)));

        // 前缀取的是"当前符号那一类"的条数，不是总数——否则玩家看到 ? 配一个 10，而可交付只有 1 条。
        Assert.Equal(0, NpcQuestIndicatorCore.PrefixCountOf(all)); // 可交付只有 1 条 → 不画数字
        var twoTurnIns = new NpcQuestCounts(2, 5, 5, 5);
        Assert.Equal(2, NpcQuestIndicatorCore.PrefixCountOf(twoTurnIns));

        var noTurnIn = new NpcQuestCounts(0, 3, 1, 1);
        Assert.Equal(NpcQuestIndicatorKind.OfferAvailable, NpcQuestIndicatorCore.Resolve(noTurnIn));
        Assert.Equal(3, NpcQuestIndicatorCore.PrefixCountOf(noTurnIn));

        _log.WriteLine(NpcQuestIndicatorCore.Describe(all));
    }

    /// <summary>单行归类的六条形状，逐条对应规格 §4.1 的观测。</summary>
    [Fact]
    public void Indicator_ClassifyCoversEveryObservableShape()
    {
        // 可接：未接 + 门槛过 + 我是发布者。
        Assert.Equal(NpcIndicatorBucket.OfferAvailable,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, isGiverHere: true, isTurnInHere: false, accepted: false, objectivesDone: false, gatePassed: true)));

        // 条件不足：同上但门槛没过 → 灰 !。
        Assert.Equal(NpcIndicatorBucket.LockedOffer,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, isGiverHere: true, isTurnInHere: false, accepted: false, objectivesDone: false, gatePassed: false)));

        // 可交付：已接 + 目标达成 + 我是交付者 → 黄 ?。
        Assert.Equal(NpcIndicatorBucket.TurnInReady,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, isGiverHere: true, isTurnInHere: true, accepted: true, objectivesDone: true, gatePassed: false)));

        // 目标达成但交付者不是我 → 仍算进行中（灰 ?），不是可交付。
        Assert.Equal(NpcIndicatorBucket.InProgress,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, isGiverHere: true, isTurnInHere: false, accepted: true, objectivesDone: true, gatePassed: false)));

        // 已接未完 → 进行中；且此时门槛结论一律不看（等级掉下去也不该让头顶消失）。
        Assert.Equal(NpcIndicatorBucket.InProgress,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, isGiverHere: true, isTurnInHere: true, accepted: true, objectivesDone: false, gatePassed: false)));

        // 与我无关（既非发布者又非交付者）。
        Assert.Equal(NpcIndicatorBucket.NotRelated,
            NpcQuestIndicatorCore.Classify(new NpcQuestRow(10, false, false, false, false, true)));
    }

    /// <summary>坏行（id 非正）必须被数出来，且不许改变任何合法计数。</summary>
    [Fact]
    public void Indicator_BadRowsAreCountedAndNeverShiftLegalCounts()
    {
        var rows = new List<NpcQuestRow>
        {
            new(11, true, false, false, false, true),
            new(0, true, false, false, false, true),   // 坏：默认 0 会撞成"0 号任务"
            new(-7, true, false, false, false, true),  // 坏：负数
            new(12, true, false, false, false, true),
        };

        var counts = NpcQuestIndicatorCore.Accumulate(rows, out var ignored);
        Assert.Equal(2, ignored);
        Assert.Equal(2, counts.OfferAvailable);
        Assert.Equal(0, counts.TurnInReady + counts.InProgress + counts.LockedOffer);

        // 只有坏行时：全空 + None（不能凭空冒出一个符号）。
        var onlyBad = NpcQuestIndicatorCore.Accumulate(new[] { new NpcQuestRow(0, true, true, true, true, true) }, out var ig2);
        Assert.Equal(1, ig2);
        Assert.False(onlyBad.HasAny);
        Assert.Equal(NpcQuestIndicatorKind.None, NpcQuestIndicatorCore.Resolve(onlyBad));
    }

    // ==================== ② 服务位图 ====================

    /// <summary>每一个拒绝分档都必须能在同一夹具里被翻成可见（拒绝分支要有正向对照）。</summary>
    [Fact]
    public void Service_EveryRejectBucketHasAPositiveTwin()
    {
        var withQuest = new NpcQuestCounts(1, 1, 0, 0);
        var noQuest = default(NpcQuestCounts);

        // 位没开 → BitOff；同一位开了 → 可见。
        Assert.Equal(NpcServiceHiddenReason.BitOff,
            NpcServiceCore.Evaluate(NpcServiceBits.Vendor, (uint)NpcServiceBits.Trainer,
                new NpcServiceContext(10, 5, 1, 0, noQuest)).Reason);
        Assert.True(
            NpcServiceCore.Evaluate(NpcServiceBits.Vendor, (uint)NpcServiceBits.Vendor,
                new NpcServiceContext(10, 5, 1, 0, noQuest)).Visible);

        // 货架空 → NoVendorStock；补一件货就可见。
        Assert.Equal(NpcServiceHiddenReason.NoVendorStock,
            NpcServiceCore.Evaluate(NpcServiceBits.Vendor, (uint)NpcServiceBits.Vendor,
                new NpcServiceContext(10, 0, 0, 0, noQuest)).Reason);
        Assert.True(
            NpcServiceCore.Evaluate(NpcServiceBits.Vendor, (uint)NpcServiceBits.Vendor,
                new NpcServiceContext(10, 1, 0, 0, noQuest)).Visible);

        // 训练师空 / 配方空。
        Assert.Equal(NpcServiceHiddenReason.NoTrainerRows,
            NpcServiceCore.Evaluate(NpcServiceBits.Trainer, (uint)NpcServiceBits.Trainer,
                new NpcServiceContext(10, 0, 0, 0, noQuest)).Reason);
        Assert.Equal(NpcServiceHiddenReason.NoRecipeRows,
            NpcServiceCore.Evaluate(NpcServiceBits.CraftTrainer, (uint)NpcServiceBits.CraftTrainer,
                new NpcServiceContext(10, 0, 0, 0, noQuest)).Reason);

        // 任务位：无任务关系 → NoQuestForThisNpc；有 → 可见（且与指示器同进同退）。
        Assert.Equal(NpcServiceHiddenReason.NoQuestForThisNpc,
            NpcServiceCore.Evaluate(NpcServiceBits.QuestGiver, (uint)NpcServiceBits.QuestGiver,
                new NpcServiceContext(10, 0, 0, 0, noQuest)).Reason);
        Assert.True(
            NpcServiceCore.Evaluate(NpcServiceBits.QuestGiver, (uint)NpcServiceBits.QuestGiver,
                new NpcServiceContext(10, 0, 0, 0, withQuest)).Visible);

        // 设施位：开了就可见（不随玩家态变），但位没开时仍是 BitOff。
        Assert.True(
            NpcServiceCore.Evaluate(NpcServiceBits.CraftStation, (uint)NpcServiceBits.CraftStation,
                new NpcServiceContext(1, 0, 0, 0, noQuest)).Visible);

        // 对话位同理。
        Assert.True(
            NpcServiceCore.Evaluate(NpcServiceBits.Gossip, (uint)NpcServiceBits.Gossip,
                new NpcServiceContext(1, 0, 0, 0, noQuest)).Visible);
    }

    /// <summary>条数下限：Resolve 的返回长度恒等于位全集数（含全不可见时），且顺序就是展示顺序。</summary>
    [Fact]
    public void Service_ResolveAlwaysReturnsOneRowPerBit()
    {
        var entries = NpcServiceCore.Resolve(0, NpcServiceContext.TemplateOnly);
        Assert.Equal(NpcServiceCore.DisplayOrder.Length, entries.Count);

        // 至少要有 6 个位（位表被误删空时这条会红，而不是"安静地找到 0 条"）。
        Assert.True(NpcServiceCore.DisplayOrder.Length >= 6,
            $"服务位数量异常：{NpcServiceCore.DisplayOrder.Length}");

        for (var i = 0; i < entries.Count; i++)
        {
            Assert.Equal(NpcServiceCore.DisplayOrder[i], entries[i].Bit);
            Assert.False(entries[i].Visible, "位图全 0 时不应有任何可见入口");
            Assert.Equal(NpcServiceHiddenReason.BitOff, entries[i].Reason);
        }
    }

    /// <summary>可见掩码必须与逐条求值一致（两处判据同源）。</summary>
    [Fact]
    public void Service_VisibleMaskMatchesPerBitEvaluation()
    {
        uint flags = (uint)(NpcServiceBits.Vendor | NpcServiceBits.Trainer | NpcServiceBits.Gossip | NpcServiceBits.QuestGiver);
        var ctx = new NpcServiceContext(20, 3, 0, 0, new NpcQuestCounts(0, 2, 0, 0));

        var mask = NpcServiceCore.VisibleMask(flags, ctx);
        var expected = 0u;
        foreach (var e in NpcServiceCore.Resolve(flags, ctx))
        {
            if (e.Visible) expected |= (uint)e.Bit;
            Assert.True((mask & (uint)e.Bit) != 0 == e.Visible, $"掩码与逐条求值不一致：{e.Describe()}");
        }

        Assert.Equal(expected, mask);

        // Trainer 开了位但没行 → 不在掩码里（可见性是算出来的，不是位图的复读）。
        Assert.Equal(0u, mask & (uint)NpcServiceBits.Trainer);
        Assert.NotEqual(0u, mask & (uint)NpcServiceBits.Vendor);
    }

    /// <summary>死位必须被数出来（"勾了没反应"的那种静默失效）。</summary>
    [Fact]
    public void Service_RejectsDeadBits()
    {
        Assert.Equal(0u, NpcServiceCore.UnknownBits((uint)NpcServiceBits.Vendor));
        Assert.Equal(1u << 20, NpcServiceCore.UnknownBits((uint)NpcServiceBits.Vendor | (1u << 20)));
        Assert.Equal("none", NpcServiceCore.DescribeBits(0));
        Assert.Contains("Vendor", NpcServiceCore.DescribeBits((uint)NpcServiceBits.Vendor), StringComparison.Ordinal);
    }

    // ==================== ③ AI 档位与增援 ====================

    /// <summary>档位必须单调：高一档的位集合包含低档的全部位（否则"升一档"会关掉某个行为）。</summary>
    [Fact]
    public void Behavior_TierPresetsAreMonotonic()
    {
        uint prev = 0;
        for (var t = (int)NpcAiTier.Static; t <= (int)NpcAiTier.Elite; t++)
        {
            var bits = NpcBehaviorCore.ForTier((NpcAiTier)t);
            Assert.True((bits & prev) == prev,
                $"档位 {t} 丢掉了低档的位：prev={prev} now={bits}");
            Assert.True(bits >= prev);
            prev = bits;
        }

        Assert.True(NpcBehaviorCore.IsTierKnown(NpcAiTier.Elite));
        Assert.False(NpcBehaviorCore.IsTierKnown(NpcAiTier.Unknown));
        Assert.False(NpcBehaviorCore.IsTierKnown((NpcAiTier)99));
    }

    /// <summary>位图反查档位只在逐位相等时命中；否则如实报 Unknown（UI 不许显示"最接近的档"）。</summary>
    [Fact]
    public void Behavior_TierOfRoundTripsOnlyForExactPresets()
    {
        for (var t = (int)NpcAiTier.Static; t <= (int)NpcAiTier.Elite; t++)
        {
            var tier = (NpcAiTier)t;
            Assert.Equal(tier, NpcBehaviorCore.TierOf(NpcBehaviorCore.ForTier(tier)));
        }

        // 加一位不属于任何预设的位 → Unknown。
        var odd = NpcBehaviorCore.ForTier(NpcAiTier.Aware) | (uint)NpcAiBits.Casts;
        Assert.Equal(NpcAiTier.Unknown, NpcBehaviorCore.TierOf(odd));
    }

    /// <summary>冲突口径：强制关赢，且冲突位必须被报出来（不是静默选一个）。</summary>
    [Fact]
    public void Behavior_ForceOffWinsAndConflictIsReported()
    {
        var v = NpcBehaviorCore.Resolve(NpcAiTier.Elite, (uint)NpcAiBits.FacePlayer, (uint)NpcAiBits.FacePlayer);
        Assert.False(v.Flags.Has(NpcAiBits.FacePlayer), "同位既开又关时关赢");
        Assert.Equal((uint)NpcAiBits.FacePlayer, v.ConflictBits);
        Assert.True(v.TierKnown);

        // 未定义的位在展开时被剥掉（运行期位图里永远不该有死位），但**原值必须被 UnknownBits 数出来**。
        var deadInput = 1u << 25;
        var dead = NpcBehaviorCore.Resolve(NpcAiTier.Static, deadInput, 0);
        Assert.Equal(0u, dead.ConflictBits);
        Assert.Equal(0u, dead.Flags.Bits);
        Assert.Equal(deadInput, NpcBehaviorCore.UnknownBits(deadInput));
        Assert.Equal(0u, NpcBehaviorCore.UnknownBits(NpcBehaviorCore.ForTier(NpcAiTier.Elite)));

        // 越界档位不猜预设：位全 0 + TierKnown=false。
        var bad = NpcBehaviorCore.Resolve(NpcAiTier.Unknown, 0, 0);
        Assert.False(bad.TierKnown);
        Assert.Equal(0u, bad.Flags.Bits);
        _log.WriteLine(bad.Describe());
    }

    /// <summary>移动方式由能力位派生，且路线优先于随机踱步（只有一份真相）。</summary>
    [Fact]
    public void Behavior_MovementModeDerivesFromBitsOnly()
    {
        Assert.Equal(NpcMovementMode.Stationary, NpcBehaviorCore.MovementModeOf(new NpcAiFlags(NpcBehaviorCore.ForTier(NpcAiTier.Aware))));
        Assert.Equal(NpcMovementMode.RandomWander, NpcBehaviorCore.MovementModeOf(new NpcAiFlags((uint)NpcAiBits.Wanders)));
        Assert.Equal(NpcMovementMode.WaypointPath, NpcBehaviorCore.MovementModeOf(new NpcAiFlags((uint)(NpcAiBits.Wanders | NpcAiBits.FollowsPath))));
    }

    /// <summary>增援：距离升序 + id 升序的全序；四条出口都带回同一组对账字段。</summary>
    [Fact]
    public void Behavior_ReinforcementPickIsTotalOrderAndEveryBucketReportsCounts()
    {
        var c = new[]
        {
            new NpcReinforcementCandidate(30, 8.0, true),
            new NpcReinforcementCandidate(11, 12.0, true),
            new NpcReinforcementCandidate(9, 12.0, true),   // 与 11 同距离 → 按 id 小者优先
            new NpcReinforcementCandidate(5, 999.0, true),  // 半径外
            new NpcReinforcementCandidate(7, 1.0, false),   // 距离最近但不能应召
        };

        var v = NpcBehaviorCore.PickReinforcements(c, 20);
        Assert.Equal(30, v.ChosenEntityId);          // 距离 8 的 30 是最近的应召者
        Assert.Equal(2, v.ChosenCount);
        Assert.Equal(new[] { 30, 9 }, v.PickedEntityIds); // 全序：8 → 12(按 id 升序，9 在 11 前)
        Assert.Equal(c.Length, v.ScannedCount); // 扫过的条数必须报，不管有没有选中
        Assert.Equal(NpcAlertReject.None, v.Reason);

        // 空列表。
        var none = NpcBehaviorCore.PickReinforcements(Array.Empty<NpcReinforcementCandidate>(), 20);
        Assert.Equal(NpcAlertReject.NoCandidates, none.Reason);
        Assert.Equal(-1, none.ChosenEntityId);   // 不给 0：0 会撞成合法实体 id
        Assert.Equal(0, none.ChosenCount);

        // 半径非法。
        Assert.Equal(NpcAlertReject.BadRadius, NpcBehaviorCore.PickReinforcements(c, 0).Reason);

        // 有人在半径内但都不能应召 → 与"半径内没人"分开报。
        var busy = new[] { new NpcReinforcementCandidate(1, 2.0, false), new NpcReinforcementCandidate(2, 3.0, false) };
        var b = NpcBehaviorCore.PickReinforcements(busy, 20);
        Assert.Equal(NpcAlertReject.NoneAvailable, b.Reason);
        Assert.Equal(2, b.ScannedCount);

        // 负距离是上游算错，宁可不叫。
        var neg = new[] { new NpcReinforcementCandidate(4, -1.0, true) };
        Assert.Equal(NpcAlertReject.NoCandidates, NpcBehaviorCore.PickReinforcements(neg, 20).Reason);
    }

    // ==================== ④ 商人定价与库存 ====================

    /// <summary>价格优先级：商人覆盖 &gt; 物品模板 &gt; 拒绝。0 一律是"不可成交"，不是免费。</summary>
    [Fact]
    public void Commerce_PriceFallsBackOverrideThenTemplateThenReject()
    {
        var row = new NpcVendorRow(101, 0, 500, 120, 0, 0);
        var item = new NpcItemPriceRow(400, 80);

        var buy = NpcCommerceCore.ResolveBuyPrice(in row, in item);
        Assert.True(buy.IsOk);
        Assert.Equal(500, buy.Amount);
        Assert.Equal(NpcCommerceCore.SourceVendorOverride, buy.PriceSource);

        var noOverride = new NpcVendorRow(101, 0, 0, 0, 0, 0);
        var tplBuy = NpcCommerceCore.ResolveBuyPrice(in noOverride, in item);
        Assert.Equal(400, tplBuy.Amount);
        Assert.Equal(NpcCommerceCore.SourceItemTemplate, tplBuy.PriceSource);

        // 两侧都没价 → 拒绝且金额为 0（不给"未定义值"）。
        var free = NpcCommerceCore.ResolveBuyPrice(in noOverride, new NpcItemPriceRow(0, 0));
        Assert.False(free.IsOk);
        Assert.Equal(NpcPriceReject.NoBuyPrice, free.Reason);
        Assert.Equal(0, free.Amount);

        // 收购侧同构，但拒绝档不同。
        Assert.Equal(NpcPriceReject.NoSellPrice, NpcCommerceCore.ResolveSellPrice(in noOverride, new NpcItemPriceRow(400, 0)).Reason);
        Assert.Equal(80, NpcCommerceCore.ResolveSellPrice(in noOverride, in item).Amount);

        // 坏行：物品 id 非正 → 两侧都给 BadItem，且金额为 0。
        var badRow = new NpcVendorRow(0, 0, 500, 120, 5, 60);
        Assert.Equal(NpcPriceReject.BadItem, NpcCommerceCore.ResolveBuyPrice(in badRow, in item).Reason);
        Assert.Equal(NpcPriceReject.BadItem, NpcCommerceCore.ResolveSellPrice(in badRow, in item).Reason);
    }

    /// <summary>库存补货：与独立暴力参照（逐周期推进）在大量时刻组合上逐一相等。</summary>
    [Theory]
    [InlineData(5, 60, 0, 0, 5)]
    [InlineData(5, 60, 3, 0, 2)]
    [InlineData(5, 60, 3, 59, 2)]
    [InlineData(5, 60, 3, 60, 5)]
    [InlineData(5, 60, 5, 30, 0)]
    [InlineData(5, 60, 5, 120, 5)]
    [InlineData(5, 0, 3, 9999, 2)]
    public void Commerce_StockMatchesBruteForceReference(int max, int restock, int purchased, long now, int expectedRemaining)
    {
        var row = new NpcVendorRow(101, 0, 0, 0, max, restock);
        var v = NpcCommerceCore.ResolveStock(in row, purchased, 0, now);

        Assert.Equal(expectedRemaining, v.Remaining);

        // 独立参照：一秒一秒推进，逐秒模拟"跨过周期边界就重置到上限"；没有周期就永不重置。
        var refRemaining = Math.Max(0, max - purchased);
        for (var t = 1; t <= now && t <= 10000; t++)
        {
            if (restock > 0 && t % restock == 0) refRemaining = max;
        }

        Assert.Equal(refRemaining, v.Remaining);
    }

    /// <summary>不限库存、有上限无周期（永远补不了货）、坏行三种形状各归各档。</summary>
    [Fact]
    public void Commerce_StockEdgeShapesUseDistinctBuckets()
    {
        var unlimited = new NpcVendorRow(101, 0, 0, 0, 0, 0);
        var u = NpcCommerceCore.ResolveStock(in unlimited, 999, 0, 12345);
        Assert.True(u.Unlimited);
        Assert.True(u.IsAvailable);
        Assert.Equal(-1, u.Remaining);      // 不限时不给 int.Max
        Assert.Equal(-1, u.NextRestockSeconds);
        Assert.Equal(NpcStockReject.None, u.Reason);

        // 有上限但没周期：卖完就永远没了 → 单独一档，好让 validate 数出来。
        var stuck = new NpcVendorRow(101, 0, 0, 0, 2, 0);
        Assert.Equal(NpcStockReject.RestockNotConfigured, NpcCommerceCore.ResolveStock(in stuck, 2, 0, 10_000).Reason);
        Assert.Equal(NpcStockReject.None, NpcStockRejectFrom(in stuck, 1, 0, 10_000));

        // 时钟回拨不许凭空补货。
        var back = NpcCommerceCore.ResolveStock(in stuck, 2, 100, 50);
        Assert.Equal(0, back.Remaining);

        var bad = new NpcVendorRow(0, 0, 1, 1, -5, 0);
        Assert.Equal(NpcStockReject.BadItem, NpcCommerceCore.ResolveStock(in bad, 0, 0, 0).Reason);
    }

    private static NpcStockReject NpcStockRejectFrom(in NpcVendorRow row, int purchased, long anchor, long now)
        => NpcCommerceCore.ResolveStock(in row, purchased, anchor, now).Reason;

    /// <summary>购买判定的四条拒绝出口 + 一条放行，都要回同一组字段。</summary>
    [Fact]
    public void Commerce_TryBuyReportsSameReceiptOnEveryExit()
    {
        var row = new NpcVendorRow(101, 0, 100, 20, 3, 60);
        var item = new NpcItemPriceRow(90, 10);
        var stocked = NpcCommerceCore.ResolveStock(in row, 0, 0, 0);

        var ok = NpcCommerceCore.TryBuy(in row, in item, in stocked, 10_000, 2);
        Assert.True(ok.IsOk);
        Assert.Equal(200, ok.TotalCost);
        Assert.Equal(2, ok.GrantedCount);

        // 钱不够：总价照样算出来（UI 要说"还差多少"）。
        var poor = NpcCommerceCore.TryBuy(in row, in item, in stocked, 150, 2);
        Assert.Equal(NpcBuyReject.NotEnoughGold, poor.Reject);
        Assert.Equal(200, poor.TotalCost);

        // 件数被库存夹：先夹数量再算钱，否则"钱不够"会盖掉"只剩 2 件"。
        var many = NpcCommerceCore.TryBuy(in row, in item, in stocked, 10_000, 10);
        Assert.True(many.IsOk);
        Assert.Equal(3, many.GrantedCount);
        Assert.Equal(300, many.TotalCost);

        // 卖光。
        var empty = NpcCommerceCore.ResolveStock(in row, 3, 0, 0);
        Assert.False(empty.IsAvailable);
        Assert.Equal(NpcBuyReject.OutOfStock, NpcCommerceCore.TryBuy(in row, in item, in empty, 10_000, 1).Reject);

        // 没定价。
        var noPriceRow = new NpcVendorRow(101, 0, 0, 0, 3, 60);
        Assert.Equal(NpcBuyReject.PriceUnavailable,
            NpcCommerceCore.TryBuy(in noPriceRow, new NpcItemPriceRow(0, 0), in stocked, 10_000, 1).Reject);

        // 非法件数。
        Assert.Equal(NpcBuyReject.BadQuantity, NpcCommerceCore.TryBuy(in row, in item, in stocked, 10_000, 0).Reject);

        // 每条出口都对账：字段齐全（不是只有放行那条有值）。
        foreach (var v in new[] { ok, poor, many, NpcCommerceCore.TryBuy(in row, in item, in empty, 10_000, 1) })
        {
            Assert.NotNull(v.Describe());
            Assert.Contains("buy_total=", v.Describe(), StringComparison.Ordinal);
            Assert.Contains("stock=", v.Describe(), StringComparison.Ordinal);
        }
    }

    // ==================== ⑤ 训练师 ====================

    /// <summary>游戏门槛结论必须原样透传（NPC 层不重新解释、不改写理由码）。</summary>
    [Fact]
    public void Trainer_GameGateCodeIsPassedThroughUnchanged()
    {
        var row = new NpcTrainerRow(7001, NpcTrainerService.Skill, 300, 0, 0, 1);

        var refused = NpcTrainerCore.Evaluate(in row, gameGatePassed: false, gameGateCode: 42, buyerGold: 999_999, haveCostItem: 0);
        Assert.False(refused.Allowed);
        Assert.Equal(NpcTeachReject.GameGateRefused, refused.Reject);
        Assert.True(refused.GameGateCode == 42, "游戏门槛码被 NPC 层改写/吞掉 = 两份理由各说各话");
        Assert.True(refused.CostGold == 300, "被拒时也要把价格带回去（UI 要显示学费）");

        var ok = NpcTrainerCore.Evaluate(in row, true, 0, 300, 0);
        Assert.True(ok.Allowed);
        Assert.Equal(NpcTeachReject.None, ok.Reject);

        // 免费教学是合法设计：0 金必须放行（所以"没定价"不能靠 0 表达）。
        Assert.True(NpcTrainerCore.Evaluate(new NpcTrainerRow(7001, NpcTrainerService.Skill, 0, 0, 0, 1), true, 0, 0, 0).Allowed);
    }

    /// <summary>"这 NPC 不教这一式"与"教但被拒"必须分档；坏行另算一档。</summary>
    [Fact]
    public void Trainer_NotFoundBadRowAndRejectsAreDistinct()
    {
        Assert.Equal(NpcTeachReject.NotTaughtByThisNpc, NpcTrainerCore.NotFound(0).Reject);

        // 服务种类未登记 / 技能 id 非正 / 学费为负：三种坏行同档但都不放行。
        Assert.Equal(NpcTeachReject.BadRow, NpcTrainerCore.Evaluate(new NpcTrainerRow(7001, NpcTrainerService.Unknown, 100, 0, 0, 1), true, 0, 999, 0).Reject);
        Assert.Equal(NpcTeachReject.BadRow, NpcTrainerCore.Evaluate(new NpcTrainerRow(0, NpcTrainerService.Skill, 100, 0, 0, 1), true, 0, 999, 0).Reject);
        Assert.Equal(NpcTeachReject.BadRow, NpcTrainerCore.Evaluate(new NpcTrainerRow(7001, NpcTrainerService.Skill, -1, 0, 0, 1), true, 0, 999, 0).Reject);

        // 材料与钱的拒绝档不同。
        var withItem = new NpcTrainerRow(7001, NpcTrainerService.Recipe, 100, 555, 3, 1);
        Assert.Equal(NpcTeachReject.NotEnoughItem, NpcTrainerCore.Evaluate(in withItem, true, 0, 999_999, 2).Reject);
        Assert.Equal(NpcTeachReject.NotEnoughGold, NpcTrainerCore.Evaluate(in withItem, true, 0, 50, 3).Reject);
        Assert.True(NpcTrainerCore.Evaluate(in withItem, true, 0, 100, 3).Allowed);
    }

    /// <summary>窗口顺序按 seq（数据决定的可观测顺序），同 seq 保序。</summary>
    [Fact]
    public void Trainer_SortBySeqIsStable()
    {
        var rows = new List<NpcTrainerRow>
        {
            new(1, NpcTrainerService.Skill, 10, 0, 0, 5),
            new(2, NpcTrainerService.Skill, 10, 0, 0, 1),
            new(3, NpcTrainerService.Skill, 10, 0, 0, 5),
            new(4, NpcTrainerService.Skill, 10, 0, 0, 0),
        };

        NpcTrainerCore.SortBySeq(rows);
        Assert.Equal(new[] { 4, 2, 1, 3 }, new[] { rows[0].SkillId, rows[1].SkillId, rows[2].SkillId, rows[3].SkillId });

        var summary = NpcTrainerCore.SummarizeWindow(rows, new[] { true, false, true, true }, new[] { 0, 7, 0, 0 }, 999_999);
        Assert.Equal(3, summary.Learnable);
        Assert.Equal(1, summary.GateGray);
        Assert.Equal(0, summary.GoldGray);
    }

    // ==================== ⑥ 制作 ====================

    /// <summary>缺料统计与独立暴力参照（双层循环）在随机构造上逐一相等。</summary>
    [Fact]
    public void Craft_MaterialShortfallMatchesBruteForceReference()
    {
        var rng = new Random(20261025);
        for (var trial = 0; trial < 200; trial++)
        {
            var kinds = rng.Next(1, 7);
            var needs = new List<NpcMaterialNeed>(kinds);
            var held = new List<NpcHeldItem>(kinds);
            for (var i = 0; i < kinds; i++)
            {
                var itemId = 500 + i;
                needs.Add(new NpcMaterialNeed(itemId, rng.Next(1, 6)));
                if (rng.Next(0, 4) > 0) held.Add(new NpcHeldItem(itemId, rng.Next(0, 6)));
            }

            var v = NpcCraftCore.CheckMaterials(needs, held);

            // 参照：对每个需求物品，单独把背包里所有同 id 的行加起来再比。
            var refMissingKinds = 0;
            var refMissingUnits = 0;
            for (var i = 0; i < needs.Count; i++)
            {
                var need = needs[i].NeedCount;
                var have = 0;
                for (var j = 0; j < held.Count; j++)
                {
                    if (held[j].ItemId == needs[i].ItemId) have += Math.Max(0, held[j].HaveCount);
                }

                if (need > have)
                {
                    refMissingKinds++;
                    refMissingUnits += need - have;
                }
            }

            Assert.Equal(refMissingKinds, v.MissingKinds);
            Assert.Equal(refMissingUnits, v.MissingUnits);
            Assert.Equal(needs.Count, v.ScannedRows);
            Assert.Equal(v.MissingKinds > 0 ? NpcCraftReject.ShortMaterial : NpcCraftReject.None, v.Reason);
        }
    }

    /// <summary>同一物品出现两次要合并（否则"缺 2 + 缺 3"被算成"缺 3"，玩家补齐后仍做不出）。</summary>
    [Fact]
    public void Craft_DuplicateNeedsMergeAndAreCountedAsBadRows()
    {
        var needs = new[]
        {
            new NpcMaterialNeed(600, 2),
            new NpcMaterialNeed(600, 3),
        };
        var held = new[] { new NpcHeldItem(600, 4) };

        var v = NpcCraftCore.CheckMaterials(needs, held);
        Assert.Equal(1, v.MissingKinds);
        Assert.Equal(1, v.MissingUnits);   // 需要 5，持有 4 → 缺 1
        Assert.Equal(1, v.BadRows);
        Assert.Single(v.Shortfalls);
        Assert.Equal(5, v.Shortfalls[0].Need);
        Assert.Equal(1, v.Shortfalls[0].Missing);
    }

    /// <summary>空配方 / 坏需求 = 凭空造物，必须显式拒绝（这正是 <c>CraftingGrain.cs:89-103</c> 今天的形状）。</summary>
    [Fact]
    public void Craft_EmptyOrBadRecipeIsRejectedNotAllowed()
    {
        Assert.Equal(NpcCraftReject.NoMaterialsDefined, NpcCraftCore.CheckMaterials(Array.Empty<NpcMaterialNeed>(), Array.Empty<NpcHeldItem>()).Reason);
        Assert.Equal(NpcCraftReject.NoMaterialsDefined, NpcCraftCore.CheckMaterials(null, null).Reason);

        var onlyBad = new[] { new NpcMaterialNeed(0, 1), new NpcMaterialNeed(600, 0), new NpcMaterialNeed(-3, 2) };
        var v = NpcCraftCore.CheckMaterials(onlyBad, Array.Empty<NpcHeldItem>());
        Assert.False(v.IsOk);
        Assert.Equal(3, v.BadRows);
        Assert.Equal(3, v.ScannedRows);
        _log.WriteLine(v.Describe());
    }

    /// <summary>设施与"学过没有"的判定顺序：没学过先拒（不泄露配方长什么样），设施其次。</summary>
    [Fact]
    public void Craft_NotLearnedBeatsStationBeatsMaterials()
    {
        var needs = new[] { new NpcMaterialNeed(600, 1) };
        var held = new[] { new NpcHeldItem(600, 1) };

        Assert.Equal(NpcCraftReject.RecipeNotLearned,
            NpcCraftCore.TryCraft(false, true, (uint)NpcServiceBits.CraftStation, needs, held).Reason);

        Assert.Equal(NpcCraftReject.NotAtStation,
            NpcCraftCore.TryCraft(true, true, (uint)NpcServiceBits.Vendor, needs, held).Reason);

        Assert.True(NpcCraftCore.TryCraft(true, false, 0, needs, held).IsOk, "不要求设施的配方在任何地方都能做");

        // 要求设施且站对了地方 → 继续走到材料判定（这里给空需求，所以应落到"空配方"档而不是设施档）。
        Assert.Equal(NpcCraftReject.NoMaterialsDefined,
            NpcCraftCore.TryCraft(true, true, (uint)NpcServiceBits.CraftStation, Array.Empty<NpcMaterialNeed>(), held).Reason);
        Assert.True(NpcCraftCore.TryCraft(true, true, (uint)NpcServiceBits.CraftStation, needs, held).IsOk);
    }

    /// <summary>成功率曲线：没有默认值可猜；表不整就不算；数组顺序是"由易到难"（橙黄绿灰）。</summary>
    [Fact]
    public void Craft_ChanceCurveRequiresExplicitTable()
    {
        var broken = new NpcCraftChanceCurve(new[] { 0, 5 }, new[] { 10000, 5000 });
        Assert.False(broken.IsUsable);
        Assert.Equal(NpcCraftChanceTier.Gray, broken.TierFor(100));
        Assert.True(broken.ChanceBpsFor(NpcCraftChanceTier.Orange) == 0, "表不可用时不许猜一个概率");

        // 上限递增、概率非递增：橙≤5、黄≤10、绿≤15、灰>15。
        var curve = new NpcCraftChanceCurve(new[] { 5, 10, 15, 60 }, new[] { 10000, 6000, 3000, 1000 });
        Assert.True(curve.IsUsable);
        Assert.True(curve.TierFor(0) == NpcCraftChanceTier.Orange, "玩家已等于配方要求 → 最易档");
        Assert.Equal(NpcCraftChanceTier.Orange, curve.TierFor(-50));
        Assert.Equal(NpcCraftChanceTier.Orange, curve.TierFor(5));
        Assert.Equal(NpcCraftChanceTier.Yellow, curve.TierFor(6));
        Assert.Equal(NpcCraftChanceTier.Green, curve.TierFor(12));
        Assert.Equal(NpcCraftChanceTier.Gray, curve.TierFor(16));
        Assert.True(curve.TierFor(10_000) == NpcCraftChanceTier.Gray, "超出最难档上限仍是难档，不许绕回橙");

        Assert.Equal(10000, curve.ChanceBpsFor(NpcCraftChanceTier.Orange));
        Assert.Equal(1000, curve.ChanceBpsFor(NpcCraftChanceTier.Gray));
        Assert.True(curve.ChanceBpsFor((NpcCraftChanceTier)9) == 0, "越界档不给概率");

        // 上限不递增 = 档位互相吞（差值 6 会同时落进两档）。
        Assert.False(new NpcCraftChanceCurve(new[] { 5, 5, 10, 20 }, new[] { 1, 2, 3, 4 }).IsUsable);
        // 概率非递增是另一条：绿档比黄档还容易成功是坏表。
        Assert.False(new NpcCraftChanceCurve(new[] { 5, 10, 15, 20 }, new[] { 1000, 6000, 3000, 100 }).IsUsable);

        // 下标换算只有一个出口（防止调用方各写一遍反序）。
        Assert.Equal(0, NpcCraftChanceCurve.IndexOf(NpcCraftChanceTier.Orange));
        Assert.Equal(3, NpcCraftChanceCurve.IndexOf(NpcCraftChanceTier.Gray));
    }

    // ==================== ⑦ 击杀贡献经验 ====================

    /// <summary>敌对 NPC / 中立 NPC / PVP 的经验必须**逐字等于怪物内核**的输出（本层只分派）。</summary>
    [Theory]
    [InlineData(NpcKillSource.HostileNpc)]
    [InlineData(NpcKillSource.NeutralNpc)]
    [InlineData(NpcKillSource.PvpPlayer)]
    [InlineData(NpcKillSource.Monster)]
    public void Kill_DelegatesExactlyToMonsterXpCore(NpcKillSource source)
    {
        var cfg = MonsterXpConfig.Default;
        for (var target = 1; target <= 300; target += 7)
        {
            for (var observer = 1; observer <= 300; observer += 11)
            {
                var mine = NpcKillCore.Evaluate(source, cfg, target, observer);
                var theirs = MonsterXpCore.Compute(cfg, target, observer, MonsterRank.Normal);
                Assert.Equal(theirs, mine.Xp);
                Assert.Equal(mine.Xp > 0, mine.Reason == NpcKillXpReject.None);
            }
        }
    }

    /// <summary>友好 NPC 给 0，且理由是"立场"而不是"灰名断供"或"漏配"。</summary>
    [Fact]
    public void Kill_FriendlyNpcReportsItsOwnReason()
    {
        var v = NpcKillCore.Evaluate(NpcKillSource.FriendlyNpc, MonsterXpConfig.Default, 50, 50);
        Assert.Equal(0, v.Xp);
        Assert.Equal(NpcKillXpReject.SourceGrantsNone, v.Reason);
        Assert.False(v.SourceGrantsXp);

        // Unknown 与友好 NPC 同为零，但来源不同：Unknown 也是不给（绝不猜）。
        var unknown = NpcKillCore.Evaluate(NpcKillSource.Unknown, MonsterXpConfig.Default, 50, 50);
        Assert.Equal(0, unknown.Xp);
        Assert.Equal(NpcKillXpReject.SourceGrantsNone, unknown.Reason);
    }

    /// <summary>PVP 的"大号杀小号不给经验"与打灰名怪走的是同一条断供判据。</summary>
    [Fact]
    public void Kill_PvpUsesTheSameGrayCutoff()
    {
        var cfg = MonsterXpConfig.Default;
        // 观察者远超目标：让分判成灰 → 0 且理由是 GrayConcession。
        var v = NpcKillCore.Evaluate(NpcKillSource.PvpPlayer, cfg, 1, MonsterDifficultyCore.MaxPlayerLevel);
        Assert.Equal(0, v.Xp);
        Assert.Equal(NpcKillXpReject.GrayConcession, v.Reason);
        Assert.Equal(MonsterDifficulty.Gray, v.Difficulty);

        // 同一目标换低等级观察者 → 有经验（证明上一条的零不是"没配"）。
        // 观察者取 5：灰名门槛在 Lv≤5 时为 0，目标 Lv1 恰好越过门槛进入"有经验的最低档"。
        var low = NpcKillCore.Evaluate(NpcKillSource.PvpPlayer, cfg, 1, 5);
        Assert.True(low.Xp > 0, $"正向对照失败：{low.Describe()}");
        Assert.Equal(NpcKillXpReject.None, low.Reason);
    }

    /// <summary>坏等级不许被当成"1 级"算。</summary>
    [Fact]
    public void Kill_RejectsNonPositiveLevels()
    {
        Assert.Equal(NpcKillXpReject.BadLevel, NpcKillCore.Evaluate(NpcKillSource.HostileNpc, MonsterXpConfig.Default, 0, 10).Reason);
        Assert.Equal(NpcKillXpReject.BadLevel, NpcKillCore.Evaluate(NpcKillSource.HostileNpc, MonsterXpConfig.Default, 10, -1).Reason);
    }

    /// <summary>立场 → 来源的翻译是唯一入口；越界立场翻成 Unknown（翻成"友好"会让敌对 NPC 白给零经验而无从归因）。</summary>
    [Fact]
    public void Kill_StanceTranslationCoversAllAndUnknown()
    {
        Assert.Equal(NpcKillSource.FriendlyNpc, NpcKillCore.SourceOf(NpcStance.Friendly));
        Assert.Equal(NpcKillSource.NeutralNpc, NpcKillCore.SourceOf(NpcStance.Neutral));
        Assert.Equal(NpcKillSource.HostileNpc, NpcKillCore.SourceOf(NpcStance.Hostile));
        Assert.Equal(NpcKillSource.Unknown, NpcKillCore.SourceOf(NpcStance.Unknown));
    }

    /// <summary>参与分配：人数与逐人经验由怪物内核决定；截断/缓冲不够分别报档。</summary>
    [Fact]
    public void Kill_ParticipantAllocationMatchesSortingAndTruncationReference()
    {
        var cfg = MonsterXpConfig.Default;
        var participants = new[]
        {
            new MonsterXpParticipant { CharacterId = 30, Level = 40, LastContributionTick = 100 },
            new MonsterXpParticipant { CharacterId = 10, Level = 20, LastContributionTick = 300 },
            new MonsterXpParticipant { CharacterId = 20, Level = 60, LastContributionTick = 300 },
        };

        Span<long> xp = stackalloc long[MonsterXpCore.MaxParticipants];
        var paid = NpcKillCore.AllocateToParticipants(NpcKillSource.HostileNpc, cfg, 50, MonsterRank.Normal, participants, xp, out var reason);

        Assert.Equal(3, paid);
        Assert.Equal(NpcKillXpReject.None, reason);

        // 参照：贡献时刻新→旧、同刻 id 升序 → tick=300 的 {10,20} 在前（id 小者优先，10 先 20 后），tick=100 的 30 最后。
        Assert.Equal(MonsterXpCore.Compute(cfg, 50, 20, MonsterRank.Normal), xp[0]);
        Assert.Equal(MonsterXpCore.Compute(cfg, 50, 60, MonsterRank.Normal), xp[1]);
        Assert.Equal(MonsterXpCore.Compute(cfg, 50, 40, MonsterRank.Normal), xp[2]);
        Assert.Equal(10UL, participants[0].CharacterId);
        Assert.Equal(20UL, participants[1].CharacterId);
        Assert.Equal(30UL, participants[2].CharacterId);

        // 超过上限 → 截断档，且人数正好是上限。
        var crowd = new MonsterXpParticipant[MonsterXpCore.MaxParticipants + 2];
        for (var i = 0; i < crowd.Length; i++)
        {
            crowd[i] = new MonsterXpParticipant { CharacterId = (ulong)(100 + i), Level = 30, LastContributionTick = i };
        }

        Span<long> big = stackalloc long[MonsterXpCore.MaxParticipants];
        paid = NpcKillCore.AllocateToParticipants(NpcKillSource.PvpPlayer, cfg, 30, MonsterRank.Normal, crowd, big, out reason);
        Assert.Equal(MonsterXpCore.MaxParticipants, paid);
        Assert.Equal(NpcKillXpReject.TruncatedByParticipantCap, reason);

        // 缓冲太短：内核会回 0，必须有独立档区分"没人该拿"与"我给的缓冲不够"。
        Span<long> tiny = stackalloc long[1];
        paid = NpcKillCore.AllocateToParticipants(NpcKillSource.HostileNpc, cfg, 30, MonsterRank.Normal, participants, tiny, out reason);
        Assert.Equal(0, paid);
        Assert.Equal(NpcKillXpReject.XpBufferTooSmall, reason);

        // 友好 NPC 整组零，理由是来源。
        Span<long> any = stackalloc long[MonsterXpCore.MaxParticipants];
        paid = NpcKillCore.AllocateToParticipants(NpcKillSource.FriendlyNpc, cfg, 30, MonsterRank.Normal, participants, any, out reason);
        Assert.Equal(0, paid);
        Assert.Equal(NpcKillXpReject.SourceGrantsNone, reason);
    }

    // ==================== ⑧ 反应轴 ====================

    /// <summary>立场三档各归各色；坏立场不许被兜成"友好"。</summary>
    [Fact]
    public void Reaction_StanceDrivesColorAndAttackability()
    {
        Assert.Equal(NpcNameColor.Green, NpcReactionCore.ColorOf(NpcStance.Friendly));
        Assert.Equal(NpcNameColor.Yellow, NpcReactionCore.ColorOf(NpcStance.Neutral));
        Assert.Equal(NpcNameColor.Red, NpcReactionCore.ColorOf(NpcStance.Hostile));
        Assert.Equal(NpcNameColor.Gray, NpcReactionCore.ColorOf(NpcStance.Unknown));

        var friendly = NpcReactionCore.Of(NpcStance.Friendly);
        Assert.False(friendly.CanBeAttacked);
        Assert.False(friendly.GrantsKillXp);
        Assert.True(friendly.ProvokesGuard);
        Assert.Equal(NpcAttackReject.FriendlyNotAttackable, friendly.Reject);

        var hostile = NpcReactionCore.Of(NpcStance.Hostile);
        Assert.True(hostile.CanBeAttacked);
        Assert.True(hostile.GrantsKillXp);
        Assert.False(hostile.ProvokesGuard);

        // 坏立场：什么都不发生，但带 BadStance 让 validate 数得出来（不许静默变友好）。
        var bad = NpcReactionCore.Of(NpcStance.Unknown);
        Assert.False(bad.CanBeAttacked);
        Assert.Equal(NpcAttackReject.BadStance, bad.Reject);
        _log.WriteLine(bad.Describe());
    }

    /// <summary>三态覆盖必须能翻出两个方向的例外（bool 会把漏填的敌对 NPC 变成打不动的哑巴）。</summary>
    [Fact]
    public void Reaction_ThreeStateOverrideFlipsBothWays()
    {
        // 友好但强制可打（剧情必杀 NPC）：可打，但仍不给经验。
        var killableAlly = NpcReactionCore.Of(NpcStance.Friendly, 1);
        Assert.True(killableAlly.CanBeAttacked);
        Assert.False(killableAlly.GrantsKillXp);
        Assert.Equal(NpcAttackReject.None, killableAlly.Reject);

        // 敌对但强制不可打（不可杀的剧情靶子）。
        var untouchableFoe = NpcReactionCore.Of(NpcStance.Hostile, 0);
        Assert.False(untouchableFoe.CanBeAttacked);
        Assert.False(untouchableFoe.GrantsKillXp);

        // 跟立场 = −1。
        Assert.True(NpcReactionCore.Of(NpcStance.Neutral, -1).CanBeAttacked);
        Assert.Equal(-1, NpcReactionCore.NormalizeOverride(null));
        Assert.Equal(0, NpcReactionCore.NormalizeOverride(0));
        Assert.Equal(1, NpcReactionCore.NormalizeOverride(7));
        _log.WriteLine("列里任何非 0 值都算『强制可打』，不静默当未填");
    }

    /// <summary>
    /// 作息 → 路线选择：命中用档里的线，没命中/未配/档里 line≤0 一律落回兜底线。
    /// <para>宿主每 tick 问一次这条，判据只此一份；这里把四种出口都钉住。</para>
    /// </summary>
    [Fact]
    public void Schedule_RouteAtPicksSlotRouteOrFallsBack()
    {
        var slots = new[]
        {
            new NpcScheduleSlot(600, 900, 42, NpcPosture.Work, 9),
            new NpcScheduleSlot(900, 1200, 0, NpcPosture.Stand, 0),   // 该档没指定路线
        };

        // 命中具体档且给了路线 → 用档里的。
        Assert.Equal(42, NpcScheduleCore.RouteAt(slots, 700, 1200, fallbackRouteId: 7));

        // 命中但档里 route=0 → 落回兜底（0 不是"第 0 条线"）。
        Assert.Equal(7, NpcScheduleCore.RouteAt(slots, 1000, 1200, fallbackRouteId: 7));

        // 空档（配漏了）→ 落回兜底。
        Assert.Equal(7, NpcScheduleCore.RouteAt(slots, 100, 1200, fallbackRouteId: 7));

        // 没配作息 → 落回兜底。
        Assert.Equal(7, NpcScheduleCore.RouteAt(System.Array.Empty<NpcScheduleSlot>(), 700, 1200, fallbackRouteId: 7));
        Assert.Equal(7, NpcScheduleCore.RouteAt(null, 700, 1200, fallbackRouteId: 7));

        // 跨零点档同样生效。
        var wrap = new[] { new NpcScheduleSlot(1050, 150, 88, NpcPosture.Sit, 0) };
        Assert.Equal(88, NpcScheduleCore.RouteAt(wrap, 1100, 1200, fallbackRouteId: 7));
        Assert.Equal(88, NpcScheduleCore.RouteAt(wrap, 60, 1200, fallbackRouteId: 7));
        Assert.Equal(7, NpcScheduleCore.RouteAt(wrap, 600, 1200, fallbackRouteId: 7));
    }

    // ==================== ⑨ 作息 ====================

    /// <summary>周期换算：负秒归一、非正周期报 −1（不许回绕成"半夜"）。</summary>
    [Fact]
    public void Schedule_SecondsOfDayNormalizesBothSigns()
    {
        Assert.Equal(0, NpcScheduleCore.SecondsOfDay(0, 1200));
        Assert.Equal(1199, NpcScheduleCore.SecondsOfDay(-1, 1200));
        Assert.Equal(600, NpcScheduleCore.SecondsOfDay(1800, 1200));
        Assert.Equal(-1, NpcScheduleCore.SecondsOfDay(10, 0));
    }

    /// <summary>跨零点档、全天兜底档、具体档优先于兜底档。</summary>
    [Fact]
    public void Schedule_WrapAllDayAndPriority()
    {
        var wrap = new NpcScheduleSlot(900, 120, 7, NpcPosture.Sit, 0);
        Assert.True(NpcScheduleCore.Covers(in wrap, 1000, 1200));
        Assert.True(NpcScheduleCore.Covers(in wrap, 60, 1200));
        Assert.False(NpcScheduleCore.Covers(in wrap, 500, 1200));

        var slots = new[]
        {
            new NpcScheduleSlot(0, 0, 0, NpcPosture.Stand, 0),      // 全天兜底
            new NpcScheduleSlot(600, 900, 42, NpcPosture.Work, 9),  // 具体档
        };

        var inWork = NpcScheduleCore.SlotAt(slots, 700, 1200);
        Assert.True(inWork.Matched);
        Assert.Equal(1, inWork.SlotIndex);
        Assert.Equal(42, inWork.RouteId);
        Assert.Equal(NpcPosture.Work, inWork.Posture);
        Assert.Equal(NpcScheduleReject.None, inWork.Reason);

        var inFallback = NpcScheduleCore.SlotAt(slots, 100, 1200);
        Assert.True(inFallback.Matched);
        Assert.True(inFallback.SlotIndex == 0, "具体档必须压过全天兜底档");

        // 空隙与"一行都没配"分档报。
        Assert.Equal(NpcScheduleReject.NoSlots, NpcScheduleCore.SlotAt(Array.Empty<NpcScheduleSlot>(), 100, 1200).Reason);
        var onlyNoon = new[] { new NpcScheduleSlot(600, 900, 1, NpcPosture.Stand, 0) };
        var gap = NpcScheduleCore.SlotAt(onlyNoon, 100, 1200);
        Assert.False(gap.Matched);
        Assert.Equal(NpcScheduleReject.Gap, gap.Reason);
        Assert.Equal(-1, gap.SlotIndex);

        // 终点正好等于周期长度的档必须生效（终点是开区间）。把它当越界丢掉，
        // 症状是"最后一段作息静默消失"——本条正是第一轮测试抓出来的那个缺陷。
        var toCycleEnd = new[] { new NpcScheduleSlot(80, 120, 3, NpcPosture.Sit, 0) };
        var atEnd = NpcScheduleCore.SlotAt(toCycleEnd, 110, 120);
        Assert.True(atEnd.Matched, atEnd.Describe());
        Assert.Equal(0, atEnd.BadSlots);
        Assert.Equal(3, atEnd.RouteId);

        // 起点等于周期长度才是真越界（那一秒不存在）。
        var fromBeyond = new[] { new NpcScheduleSlot(120, 60, 4, NpcPosture.Stand, 0) };
        var beyond = NpcScheduleCore.SlotAt(fromBeyond, 10, 120);
        Assert.Equal(1, beyond.BadSlots);
    }

    /// <summary>越界坏行要连同命中一起报出来（"白天能用、深夜越界"的表不能看起来全绿）。</summary>
    [Fact]
    public void Schedule_BadSlotsReportedAlongsideHit()
    {
        var slots = new[]
        {
            new NpcScheduleSlot(0, 600, 5, NpcPosture.Stand, 0),
            new NpcScheduleSlot(700, 2000, 6, NpcPosture.Sit, 0),  // 越界（周期只有 1200）
        };

        var v = NpcScheduleCore.SlotAt(slots, 100, 1200);
        Assert.True(v.Matched);
        Assert.Equal(1, v.BadSlots);
        Assert.Equal(NpcScheduleReject.BadSlot, v.Reason);
        _log.WriteLine(v.Describe());

        Assert.Equal(NpcScheduleReject.BadDayLength, NpcScheduleCore.SlotAt(slots, 100, 0).Reason);
    }

    /// <summary>覆盖普查：与独立参照（逐秒标记数组）一致。</summary>
    [Fact]
    public void Schedule_UncoveredSecondsMatchesPerSecondReference()
    {
        var cycle = 120;
        var slots = new[]
        {
            new NpcScheduleSlot(0, 40, 1, NpcPosture.Stand, 0),
            new NpcScheduleSlot(80, 120, 2, NpcPosture.Sit, 0),   // 跨到末尾
        };

        var v = NpcScheduleCore.UncoveredSeconds(slots, cycle);

        var flags = new bool[cycle];
        foreach (var s in slots)
        {
            if (s.FromSeconds < s.ToSeconds)
            {
                for (var t = s.FromSeconds; t < s.ToSeconds && t < cycle; t++) flags[t] = true;
            }
            else
            {
                for (var t = s.FromSeconds; t < cycle; t++) flags[t] = true;
                for (var t = 0; t < s.ToSeconds; t++) flags[t] = true;
            }
        }

        var expected = 0;
        for (var t = 0; t < cycle; t++) if (!flags[t]) expected++;

        Assert.Equal(expected, v);
        Assert.Equal(40, v); // 40..79 这 40 秒是配漏的空隙

        Assert.Equal(0, NpcScheduleCore.UncoveredSeconds(new[] { new NpcScheduleSlot(30, 30, 1, NpcPosture.Stand, 0) }, cycle));
        Assert.Equal(cycle, NpcScheduleCore.UncoveredSeconds(null, cycle));
    }

    // ==================== ⑩ 跨内核一致性 ====================

    /// <summary>
    /// 服务可见性与指示器必须同进同退：头顶有符号时菜单里必须有任务项，反之亦然。
    /// <para>这是 NPC 系统最刺眼的一类错（看到 <c>!</c> 点进去没任务）。</para>
    /// </summary>
    [Fact]
    public void Consistency_IndicatorAndServiceEntryAgree()
    {
        var noQuest = default(NpcQuestCounts);
        var withLocked = new NpcQuestCounts(0, 0, 0, 1);   // 只有"条件不足"
        var withOffer = new NpcQuestCounts(0, 1, 0, 0);

        uint flags = (uint)(NpcServiceBits.QuestGiver | NpcServiceBits.QuestTurnIn);

        foreach (var (counts, expectVisible) in new[]
                 {
                     (noQuest, false),
                     (withLocked, true),   // 灰 ! 也算"有关系"：玩家要能问"为什么还不行"
                     (withOffer, true),
                 })
        {
            var ctx = new NpcServiceContext(30, 0, 0, 0, counts);
            Assert.Equal(expectVisible, NpcServiceCore.Evaluate(NpcServiceBits.QuestGiver, flags, ctx).Visible);
            Assert.Equal(expectVisible, NpcServiceCore.Evaluate(NpcServiceBits.QuestTurnIn, flags, ctx).Visible);
            Assert.Equal(expectVisible, counts.HasAny);
            Assert.Equal(expectVisible, NpcQuestIndicatorCore.Resolve(counts) != NpcQuestIndicatorKind.None);
        }
    }
}
