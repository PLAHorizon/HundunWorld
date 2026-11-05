using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Planet;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 区域快照在客户端这一侧的决策规则（§6.3 第 1 步、§6.4 的三层 AOI）。
/// <para>判据重点不在"能算出数组"，而在两件安静的事：差集<em>按内容</em>比（否则每帧都在卸载又重载），
/// 以及"没回执/被拒/真的空"三种空必须走不同的处置。</para>
/// </summary>
public class PlanetRegionWatchRulesTests
{
    private static PlanetCellKey Cell(long region, int cx, int cz) =>
        new() { RegionId = region, Cx = cx, Cz = cz };

    private static readonly PlanetCellKey[] HomeCells = { Cell(10, 3, 4), Cell(10, -1, 2) };

    [Fact]
    public void 换区必问_而每个不发的理由各自不同()
    {
        // 换区：一定要重取，哪怕上一秒刚问过
        Assert.True(PlanetRegionWatchRules.ShouldRequest(nowMs: 5_000, lastRequestMs: 4_999,
            crossedRegionId: 99, lastRequestedForRegionId: 7, withinPrefetchMargin: false,
            responsePending: false, out _));

        // 没换区、也不在边界带：不发
        Assert.False(PlanetRegionWatchRules.ShouldRequest(5_000, 1_000, 0, 7, false, false, out string idle));
        Assert.Contains("离边界还远", idle, StringComparison.Ordinal);

        // 在飞一条就够：快照是幂等读，重复发只会让 grain 排队
        Assert.False(PlanetRegionWatchRules.ShouldRequest(5_000, 1_000, 99, 7, true, true, out string pending));
        Assert.Contains("上一趟还没回来", pending, StringComparison.Ordinal);

        // 正向孪生：在飞的那趟回来之后（responsePending=false），换区照样能立刻再问一次
        Assert.True(PlanetRegionWatchRules.ShouldRequest(5_000, 1_000, 99, 7, true, false, out _));
    }

    [Fact]
    public void 节流只按住边界那一路_换区与钟回退不按住()
    {
        // 站在边界上抖：800 ms 内不该再发
        Assert.False(PlanetRegionWatchRules.ShouldRequest(nowMs: 1_800, lastRequestMs: 1_000,
            crossedRegionId: 0, lastRequestedForRegionId: 7, withinPrefetchMargin: true,
            responsePending: false, out string throttled));
        Assert.Contains("节流", throttled, StringComparison.Ordinal);

        // 过了一秒就发（同一块区域也允许再来一趟：邻区的人在动）
        Assert.True(PlanetRegionWatchRules.ShouldRequest(2_000, 1_000, 0, 7, true, false, out string ok));
        Assert.Equal("", ok);

        // 钟回退（跨进程/换时源都可能）不许变成"永远不发"：sinceLast 为负时按已过节流处理
        Assert.True(PlanetRegionWatchRules.ShouldRequest(900, 5_000, 0, 7, true, false, out _));
    }

    [Fact]
    public void 预取带两个轴都要看_贴任何一条边都算()
    {
        const double half = 300_000;                 // L14 一块区域半边约 3 km
        Assert.False(PlanetRegionWatchRules.WithinPrefetchMargin(0, 0, half, half));
        Assert.True(PlanetRegionWatchRules.WithinPrefetchMargin(half - 50_000, 0, half, half));   // 贴 X 边
        Assert.True(PlanetRegionWatchRules.WithinPrefetchMargin(0, half - 50_000, half, half));   // 贴 Z 边
        Assert.True(PlanetRegionWatchRules.WithinPrefetchMargin(half + 1, half + 1, half, half)); // 已经出去了一点

        // 半边长本身小于预取带时，整块区域都算"贴边"——这时每帧都发就被节流按住，
        // 而"整块都该预取"这件事必须为真（粗层级区域会走到这一档）
        Assert.True(PlanetRegionWatchRules.WithinPrefetchMargin(0, 0, 40_000, 40_000),
                    "区域比预取带还小时应当处处算贴边");

        // 输入不可用时宁可不预取，也不要拿 NaN 去算距离
        Assert.False(PlanetRegionWatchRules.WithinPrefetchMargin(double.NaN, 0, half, half));
        Assert.False(PlanetRegionWatchRules.WithinPrefetchMargin(0, 0, 0, half));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionWatchRules.WithinPrefetchMargin(0, 0, half, half, marginCm: -1));
    }

    [Fact]
    public void 差集按内容比_新数组不算变化()
    {
        var regions = new long[] { 1, 2, 3 };
        var again = new long[] { 3, 2, 1 };                       // 顺序不同也是同一份订阅集
        var cells = new[] { Cell(10, 3, 4), Cell(10, -1, 2) };

        var same = PlanetRegionWatchRules.DiffAgainst(regions, again, HomeCells, cells);
        Assert.True(same.IsEmpty, same.ToString());

        // 真变了才动：加一块、掉一块
        var changed = PlanetRegionWatchRules.DiffAgainst(new long[] { 1, 2 }, new long[] { 2, 3 },
            Array.Empty<PlanetCellKey>(), new[] { Cell(7, 0, 0) });
        Assert.Equal(new long[] { 3 }, changed.AddRegions);
        Assert.Equal(new long[] { 1 }, changed.DropRegions);
        Assert.Single(changed.AddCells);
        Assert.Empty(changed.DropCells);
        Assert.False(changed.IsEmpty);

        // null 当空集：客户端不必为"服务端没带这一栏"写两种分支
        var fromNull = PlanetRegionWatchRules.DiffAgainst(null, null, null, null);
        Assert.True(fromNull.IsEmpty);
    }

    [Fact]
    public void cell的身份是三件事合一_不同面的同列不许混()
    {
        // 单看 (Cx,Cz) 会把另一面的同一列当成同一格：那会卸载掉错误的一块，而且一声不响
        var onFaceA = new[] { Cell(1000, 3, 4) };
        var onFaceB = new[] { Cell(2000, 3, 4) };
        var d = PlanetRegionWatchRules.DiffAgainst(Array.Empty<long>(), Array.Empty<long>(), onFaceA, onFaceB);
        Assert.Single(d.AddCells);
        Assert.Single(d.DropCells);
        Assert.Equal(2000, d.AddCells[0].RegionId);
        Assert.Equal(1000, d.DropCells[0].RegionId);

        // 同区域同列同排才是"没变"
        var again = PlanetRegionWatchRules.DiffAgainst(Array.Empty<long>(), Array.Empty<long>(),
            onFaceA, new[] { Cell(1000, 3, 4) });
        Assert.True(again.IsEmpty, again.ToString());

        // 区域内换一个方向（Z 变一格）：一增一减，不是原地不动
        var moved = PlanetRegionWatchRules.DiffAgainst(Array.Empty<long>(), Array.Empty<long>(),
            onFaceA, new[] { Cell(1000, 3, 5) });
        Assert.Single(moved.AddCells);
        Assert.Single(moved.DropCells);
    }

    [Fact]
    public void 三种空各有各的处置_被拒的快照永远不许变成卸载()
    {
        // 没回执 ≠ 被拒 ≠ 受理但为空。三条混成一个"空"，客户端就只能按最坏的那种处理。
        Assert.Equal(PlanetRegionWatchRules.WatchAction.Ignore,
            PlanetRegionWatchRules.Decide(hasResponse: false, accepted: true, 0, 5));

        Assert.Equal(PlanetRegionWatchRules.WatchAction.RefuseWorld,
            PlanetRegionWatchRules.Decide(true, accepted: false,
                (int)PlanetRegionRejectCode.CaliberMismatch, 0));

        Assert.Equal(PlanetRegionWatchRules.WatchAction.Rebind,
            PlanetRegionWatchRules.Decide(true, accepted: false,
                (int)PlanetRegionRejectCode.MalformedMessage, 0));
        Assert.Equal(PlanetRegionWatchRules.WatchAction.Rebind,
            PlanetRegionWatchRules.Decide(true, accepted: false,
                (int)PlanetRegionRejectCode.SessionMismatch, 0));

        // 受理但归属是 0：服务端不认识这个号，此时"空订阅集"不能拿去做差集，
        // 否则等于把当前所有邻区块全部卸载 —— 那是"被拒"最像"没数据"的一种误伤。
        Assert.Equal(PlanetRegionWatchRules.WatchAction.Rebind,
            PlanetRegionWatchRules.Decide(true, accepted: true, 0, 0));

        Assert.Equal(PlanetRegionWatchRules.WatchAction.Apply,
            PlanetRegionWatchRules.Decide(true, accepted: true, 0, 5));

        // 只有 Apply 会走到差集这一步：把两种空并排跑一次，被拒那条不许产生任何 Drop
        var current = new long[] { 5, 6 };
        var refusedDiff = PlanetRegionWatchRules.DiffAgainst(current, Array.Empty<long>(), HomeCells, Array.Empty<PlanetCellKey>());
        Assert.True(refusedDiff.DropRegions.Length == 2, "夹具要真的能表示\"全卸载\"，否则这条反证是空的");
        Assert.NotEqual(PlanetRegionWatchRules.WatchAction.Apply,
            PlanetRegionWatchRules.Decide(true, accepted: false,
                (int)PlanetRegionRejectCode.CaliberMismatch, 0));       // 所以那条全卸载永远不会被下发
    }

    [Fact]
    public async Task 真grain的两次快照之差就是跨过的那一块()
    {
        // 上面几条比的都是数组形状。这条走真 grain：落位 → 取快照 → 换区 → 再取，
        // 差集必须正好是"跨过的那一块加进来、原来那一块掉出去"，而且 cell 层跟着变。
        const int level = PlanetConstants.DefaultRegionLevel;
        var home = new RegionKey(2, level, 8192, 8192);
        var east = Neighbour(home, RegionEdge.UPlus);
        var rig = new PlanetRegionCaliberRig();
        ulong character = 7UL;

        var homeEcef = EcefCentimeters.FromVector(home.CenterDirection() * PlanetConstants.PlanetRadiusCm);
        Assert.True(await rig.Grain.BindAsync(character, homeEcef.X, homeEcef.Y, homeEcef.Z, 100, rig.CaliberBytes));
        var first = await rig.Grain.SnapshotAsync(character, rig.CaliberBytes);
        Assert.True(first.Accepted, first.Reason);
        Assert.Equal(home.ToId(), first.OwnerRegionId);

        var eastEcef = EcefCentimeters.FromVector(east.CenterDirection() * PlanetConstants.PlanetRadiusCm);
        Assert.True(await rig.Grain.TeleportAsync(character, eastEcef.X, eastEcef.Y, eastEcef.Z, 120, rig.CaliberBytes));
        var second = await rig.Grain.SnapshotAsync(character, rig.CaliberBytes);
        Assert.True(second.Accepted, second.Reason);
        Assert.Equal(east.ToId(), second.OwnerRegionId);

        var diff = PlanetRegionWatchRules.DiffAgainst(first.SubscribedRegionIds, second.SubscribedRegionIds,
            first.SubscribedBoundaryCells, second.SubscribedBoundaryCells);
        // 实测出来的事实（第一版按直觉写反了两条，判据当场顶回来）：
        // 跨一步既不会放下刚离开的那块（它现在是邻区），也不会"新加载脚下这块"——
        // 它在移动之前就已经在订阅集里了。这正是 §6.3 第 1 步要的预取效果：
        // 落点早就是邻区，所以换区不需要现加载一整块地形。
        Assert.Contains(east.ToId(), first.SubscribedRegionIds);
        Assert.Contains(home.ToId(), second.SubscribedRegionIds);
        Assert.DoesNotContain(east.ToId(), diff.AddRegions);
        Assert.DoesNotContain(home.ToId(), diff.DropRegions);

        // 真正增删的是"只与其中一块共边"的那些区。这比按 id 猜有意义得多：
        // 加进来的必须与脚下共边而与来路不共边，放下的反之。
        Assert.NotEmpty(diff.AddRegions);
        Assert.NotEmpty(diff.DropRegions);
        Assert.False(diff.IsEmpty);
        foreach (var added in diff.AddRegions)
        {
            var key = RegionKey.FromId(added);
            Assert.True(PlanetRegions.AreEdgeAdjacent(east, key), $"加进来的 {key} 并不与脚下共边");
            Assert.False(PlanetRegions.AreEdgeAdjacent(home, key), $"{key} 本来就看得见，不该产生一次加载");
        }
        foreach (var dropped in diff.DropRegions)
        {
            var key = RegionKey.FromId(dropped);
            Assert.True(PlanetRegions.AreEdgeAdjacent(home, key), $"放下的 {key} 从没跟来路共边过");
            Assert.False(PlanetRegions.AreEdgeAdjacent(east, key), $"{key} 还在脚下邻接范围内，放下就会看不见人");
        }

        // 区域号全程 64 位：面号在 bit58，任何一处收窄成 int 都会在这里露出来
        Assert.All(diff.AddRegions.Concat(diff.DropRegions).ToArray(),
            id => Assert.Equal(id, RegionKey.FromId(id).ToId()));

        // 边界 cell 也跟着换：两趟快照都得真的给出格子，否则这一层是空转的
        Assert.NotEmpty(first.SubscribedBoundaryCells);
        Assert.NotEmpty(second.SubscribedBoundaryCells);
        Assert.NotEmpty(diff.AddCells);
        Assert.NotEmpty(diff.DropCells);
    }

    // ==================== 结构体默认值 ====================

    [Fact]
    public void default的Diff就是空差集_不抛异常()
    {
        // 结构体的 default 绕过构造器：第一版只在构造器里归 null，于是 LastPlan=default 时
        // ToString() 抛 NRE，驱动每帧抛、回执写不出结尾块（§13.73 实机抓到的）。
        PlanetRegionWatchRules.Diff empty = default;
        Assert.True(empty.IsEmpty);
        Assert.Empty(empty.AddRegions);
        Assert.Empty(empty.DropRegions);
        Assert.Empty(empty.AddCells);
        Assert.Empty(empty.DropCells);
        Assert.Contains("+regions=0", empty.ToString(), StringComparison.Ordinal);   // 能念出来，才算能进日志

        // 反向孪生：非空的时候念的必须是那个数，不是恒 0
        var nonEmpty = new PlanetRegionWatchRules.Diff(new[] { 1L }, null, null, null);
        Assert.False(nonEmpty.IsEmpty);
        Assert.Contains("+regions=1", nonEmpty.ToString(), StringComparison.Ordinal);
        Assert.Empty(nonEmpty.DropRegions);      // 只给一个维度时，其余仍是空集而不是 null
    }

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n)
            ? n
            : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");
}
