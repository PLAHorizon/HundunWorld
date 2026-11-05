using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 远/中景壳 LOD 规划器的判据。核心是三件事，每件都有各自的反证：
/// ① 铺满整颗星球、不重不漏；② 每块的弦高不超过它那个距离上"允许 N 像素"换出来的厘米数；
/// ③ 裙边不多挂也不漏挂——而且 ③ 的两条都问几何（那块面积最终归谁），不问"同级邻居在不在集合里"，
///    否则判据与实现就是同一句话，永远绿。
/// </summary>
public class PlanetShellPlannerTests
{
    private const double Radius = PlanetConstants.PlanetRadiusCm;      // 10× 地球：6.371e9 cm
    private const int Quads = 8;

    private static readonly RegionEdge[] Edges =
        { RegionEdge.UPlus, RegionEdge.UMinus, RegionEdge.VPlus, RegionEdge.VMinus };

    private static PlanetShellPlanner NewPlanner(int maxLevel, double pixels = 1.0, int maxTiles = PlanetShellPlanner.DefaultMaxTiles)
        => new(Radius, Quads, maxLevel, pixels, 60.0, 1080.0, maxTiles);

    private static PlanetVector3 PlayerAt(double latDeg, double lonDeg)
        => Geodetic.DirectionFromLatLon(latDeg * Math.PI / 180.0, lonDeg * Math.PI / 180.0);

    private static PlanetVector3 CenterDirection(RegionKey key)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        return CubeSphere.ToDirection(key.Face, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
    }

    private static double CellUvArea(RegionKey key)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        return (u1 - u0) * (v1 - v0);
    }

    [Fact]
    public void 计划必须铺满整颗星球且不重不漏()
    {
        var planner = NewPlanner(maxLevel: 5);
        var plan = planner.Build(PlayerAt(30, 120));
        Assert.False(plan.Truncated);

        // 面积：每面 uv 跨度是 2×2=4，六面合计 24。块与块若有重叠，总面积就会大于 24；
        // 若有洞，就会小于 24。两个方向一起卡死在 24 上。
        double area = plan.Tiles.Sum(t => CellUvArea(t.Key));
        Assert.Equal(24.0, area, 9);

        // 再拿采样点验"不漏"：任何方向都必须恰好落在一块里
        int samples = 0, misses = 0, doubles = 0;
        for (int lat = -80; lat <= 80; lat += 10)
            for (int lon = -180; lon < 180; lon += 15)
            {
                var dir = PlayerAt(lat, lon);
                int hits = plan.Tiles.Count(t => SameTile(t.Key, RegionKey.FromDirection(dir, t.Key.Level)));
                samples++;
                if (hits == 0) misses++;
                if (hits > 1) doubles++;
            }
        Assert.Equal(0, misses);
        Assert.Equal(0, doubles);
        Assert.True(samples > 200, $"采样点太少（{samples}），这条判据等于没跑");
    }

    [Fact]
    public void 每块的弦高都不许超过它那个距离允许的像素()
    {
        var planner = NewPlanner(maxLevel: 6, pixels: 1.0);
        var plan = planner.Build(PlayerAt(-12, 77));
        Assert.False(plan.Truncated);

        // 判据自己算视距，不调规划器的那把尺：允许值 = 1 px 在"max(到块中心弧长, 半个块边长)"上的厘米数。
        // 半个块边长这个下限不是放水——玩家脚下那块的中心弧长≈0，允许值也≈0，任何层级都"不达标"，
        // 判据会退化成立不住的断言；而那块缝本来就在块的另外一头，至少离玩家半个块。
        var player = PlayerAt(-12, 77);
        var tooCoarse = plan.Tiles
            .Where(t => PlanetRegionSeams.ChordSagCm(t.Key, Quads, Radius)
                        > PlanetRegionSeams.AllowedSagCm(ViewDistance(t.Key, player), 1.0, 60.0, 1080.0) * 1.000001)
            .ToList();
        // 断言消息是**无条件**求值的，所以不能对可能为空的结果取 FirstOrDefault()：
        // default(ShellTile) 的 Skirts 是 null，ToString() 直接 NRE，把一条本来通过的判据报成异常。
        Assert.True(tooCoarse.Count == 0,
                    $"{tooCoarse.Count} 块在它自己的距离上超了 1 个像素的允许张角" +
                    (tooCoarse.Count == 0 ? "" : $"，例如 {tooCoarse[0]}"));

        // 反证：把允许值收到 0.01 像素，同一批块里必然有超的——不然上面那条断言是空转
        var tight = NewPlanner(maxLevel: 6, pixels: 0.01).Build(player);
        Assert.True(tight.Truncated || tight.Tiles.Any(t =>
                        PlanetRegionSeams.ChordSagCm(t.Key, Quads, Radius)
                        > PlanetRegionSeams.AllowedSagCm(ViewDistance(t.Key, player), 0.01, 60.0, 1080.0)),
                    "允许值收到 0.01 像素却一块都不超 ⇒ MaxLevel 高到没意义，这条判据什么都没卡");
    }

    [Fact]
    public void 允许张角越紧块数与顶点数必须单调上升()
    {
        var pixels = new[] { 8.0, 4.0, 2.0, 1.0, 0.5 };
        int lastTiles = 0;
        long lastVerts = 0;
        foreach (double p in pixels)
        {
            var plan = NewPlanner(maxLevel: 7, pixels: p).Build(PlayerAt(30, 120));
            Assert.True(plan.Tiles.Count > lastTiles,
                        $"允许值 {p} px 给 {plan.Tiles.Count} 块，比更松的 {lastTiles} 块还少或相同 ⇒ 判据没在起作用");
            Assert.True(plan.TotalVertices >= lastVerts,
                        $"允许值 {p} px 的顶点数 {plan.TotalVertices} 比更松的 {lastVerts} 还小");
            lastTiles = plan.Tiles.Count;
            lastVerts = plan.TotalVertices;
        }
    }

    [Fact]
    public void 近处必须比远处细()
    {
        var plan = NewPlanner(maxLevel: 6).Build(PlayerAt(30, 120));
        Assert.True(plan.Tiles.Count > 6);
        Assert.Equal(plan.Tiles.Max(t => t.Key.Level), plan.Tiles[0].Key.Level);     // 已按由近到远排
        Assert.Equal(plan.Tiles.Min(t => t.Key.Level), plan.Tiles[plan.Tiles.Count - 1].Key.Level);
        Assert.True(plan.Tiles[0].DistanceCm < plan.Tiles[plan.Tiles.Count - 1].DistanceCm);

        // 距离越远允许的层级越大，这是"按像素"而不是"按档位"的全部意义
        double near = plan.Tiles[0].DistanceCm, far = plan.Tiles[plan.Tiles.Count - 1].DistanceCm;
        Assert.True(NewPlanner(maxLevel: 7).LevelAllowedAt(near) >= 0);
        Assert.True(NewPlanner(maxLevel: 7).LevelAllowedAt(far) <= NewPlanner(maxLevel: 7).LevelAllowedAt(near),
                    $"远处 {far / 100000.0:0.#} km 允许的层级比近处 {near / 100000.0:0.#} km 还细，像素判据反了");
    }

    [Fact]
    public void 挂了的裙边必须真的对着更细的表面()
    {
        var planner = NewPlanner(maxLevel: 5);
        var plan = planner.Build(PlayerAt(30, 120));
        int checkedSkirts = 0;
        foreach (var tile in plan.Tiles)
        {
            foreach (var edge in tile.Skirts)
            {
                Assert.True(PlanetRegions.TryNeighbour(tile.Key, edge, out var neighbour), $"{tile.Key} {edge} 没有邻居");
                var covering = planner.TileCovering(plan, CenterDirection(neighbour));
                Assert.NotNull(covering);
                Assert.True(covering!.Value.Key.Level > tile.Key.Level,
                            $"{tile.Key} 的 {edge} 挂了裙边，可那边最终归 {covering.Value.Key}（同级或更粗）⇒ 白挂");
                checkedSkirts++;
            }
        }
        Assert.True(checkedSkirts > 0, "一条裙边都没挂，这条判据是空转");
    }

    [Fact]
    public void 对着更细表面的那条边一条都不能漏挂()
    {
        var planner = NewPlanner(maxLevel: 5);
        var plan = planner.Build(PlayerAt(-45, 160));
        int missed = 0, checkedEdges = 0;
        foreach (var tile in plan.Tiles)
        {
            foreach (var edge in Edges)
            {
                if (!PlanetRegions.TryNeighbour(tile.Key, edge, out var neighbour)) continue;
                var covering = planner.TileCovering(plan, CenterDirection(neighbour));
                Assert.NotNull(covering);
                bool neighbourIsFiner = covering!.Value.Key.Level > tile.Key.Level;
                if (neighbourIsFiner && !tile.Skirts.Contains(edge)) missed++;
                if (!neighbourIsFiner && tile.Skirts.Contains(edge))
                    Assert.Fail($"{tile.Key} 的 {edge} 挂了裙边，但那边是 {covering.Value.Key}（不更细）");
                checkedEdges++;
            }
        }
        Assert.Equal(0, missed);
        Assert.True(checkedEdges > 20, $"只查了 {checkedEdges} 条边");
    }

    [Fact]
    public void 允许张角大到一层就够时正好六块零裙边()
    {
        // 反证的正面对：允许 1e6 像素＝什么都允许 ⇒ 每面一块、没有跨级边界 ⇒ 0 条裙边。
        // 这条同时钉住"裙边数不是无条件加出来的"。
        // 每边 16 quad、允许 4 像素：实测整颗星球 6 块就达标（顶点 1734，裙边 0）。
        // 不用"允许 1e6 像素"那种写法——张角超出小角模型后 AllowedSagCm 会当场拒（这是它该有的行为）。
        var plan = new PlanetShellPlanner(Radius, 16, 6, 4.0, 60.0, 1080.0).Build(PlayerAt(30, 120));
        Assert.Equal(CubeSphere.FaceCount, plan.Tiles.Count);
        Assert.Equal(0, plan.SkirtCount);
        Assert.All(plan.Tiles, t => Assert.Equal(0, t.Key.Level));
    }

    [Fact]
    public void 帧预算前缀只数塞得下的那些块()
    {
        var plan = NewPlanner(maxLevel: 5).Build(PlayerAt(30, 120));
        Assert.True(plan.Tiles.Count >= 4);
        long three = plan.Tiles[0].VertexCount + plan.Tiles[1].VertexCount + plan.Tiles[2].VertexCount;
        Assert.Equal(3, plan.CountWithinBudget(three));
        Assert.Equal(2, plan.CountWithinBudget(three - 1));
        Assert.Equal(0, plan.CountWithinBudget(0));
        Assert.Equal(plan.Tiles.Count, plan.CountWithinBudget(plan.TotalVertices));
        Assert.True(plan.CountWithinBudget(plan.TotalVertices - 1) < plan.Tiles.Count);
    }

    [Fact]
    public void 同一输入两次规划逐块相同()
    {
        var planner = NewPlanner(maxLevel: 5);
        var a = planner.Build(PlayerAt(30, 120));
        var b = planner.Build(PlayerAt(30, 120));
        Assert.Equal(a.Tiles.Count, b.Tiles.Count);
        for (int i = 0; i < a.Tiles.Count; i++)
        {
            Assert.Equal(a.Tiles[i].Key, b.Tiles[i].Key);
            Assert.Equal(a.Tiles[i].Skirts.Count, b.Tiles[i].Skirts.Count);
            Assert.Equal(a.Tiles[i].VertexCount, b.Tiles[i].VertexCount);
        }
    }

    [Fact]
    public void 撞到块数上限必须认账而不是悄悄变粗()
    {
        // 允许 0.02 像素要细到远超 64 块，上限压到 64 ⇒ 必须停下并置 Truncated，
        // 因为这时"每块不超允许张角"已经不再成立。
        var plan = NewPlanner(maxLevel: 8, pixels: 0.02, maxTiles: 64).Build(PlayerAt(30, 120));
        Assert.True(plan.Truncated, "只给了 64 块的预算却没有认账，回执里的'每块达标'就是假的");
        Assert.True(plan.Tiles.Count >= 64, $"撞到上限却只给了 {plan.Tiles.Count} 块");
    }

    /// <summary>
    /// 玩家正好站在块中心时，"到块中心的弧长"在每一层都≈0，允许张角也就≈0 ⇒ 没有视距下限的话
    /// 规划器会一路细分到 MaxLevel，而且每一层都还"不达标"。这条判据钉的就是那个下限：
    /// 站在面心，最深层必须停在像素判据自己算出来的那一层，而不是顶到 MaxLevel。
    /// </summary>
    [Fact]
    public void 站在块中心时不许一路细分到最深层()
    {
        var atFaceCenter = CubeSphere.ToDirection(0, 0, 0);
        var plan = NewPlanner(maxLevel: 7, pixels: 1.0).Build(atFaceCenter);
        int deepest = plan.Tiles.Max(t => t.Key.Level);
        Assert.False(plan.Truncated);
        Assert.True(deepest < 7, $"站在面心却一路细分到 MaxLevel（最深层 {deepest}）⇒ 视距下限没了");
        Assert.True(plan.Tiles.Count < 400, $"面心位置的计划有 {plan.Tiles.Count} 块，像素判据本该把它压在小几十块");
        // 而且这块最近的块确实达标：它的弦高不超过"半个块边长"这个视距上的 1 像素
        var nearest = plan.Tiles[0];
        Assert.True(PlanetRegionSeams.ChordSagCm(nearest.Key, Quads, Radius)
                    <= PlanetRegionSeams.AllowedSagCm(ViewDistance(nearest.Key, atFaceCenter), 1.0, 60.0, 1080.0));
    }

    [Fact]
    public void 允许张角超出小角模型时当场拒绝()
    {
        // 45° 以上的"允许像素"已经不是这条公式的适用范围：tan 会绕回来，给个看着合理的假数
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlanetRegionSeams.AllowedSagCm(100_000, 1e6, 60.0, 1080.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PlanetShellPlanner(Radius, Quads, 5, 1e6).Build(PlayerAt(30, 120)));
        // 范围内正常
        Assert.True(PlanetRegionSeams.AllowedSagCm(100_000, 4.0, 60.0, 1080.0) > 0);
    }

    [Fact]
    public void 参数不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellPlanner(0, Quads, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellPlanner(Radius, 0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellPlanner(Radius, Quads, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellPlanner(Radius, Quads, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellPlanner(Radius, Quads, 3, 1.0, 60.0, 1080.0, 5));
        var planner = NewPlanner(maxLevel: 4);
        Assert.Throws<ArgumentException>(() => planner.Build(new PlanetVector3(0, 0, 0)));
    }

    /// <summary>判据用的视距：到块中心的弧长，但不小于半个块的边长（理由见上）。</summary>
    private static double ViewDistance(RegionKey key, in PlanetVector3 player)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        var dir = CubeSphere.ToDirection(key.Face, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
        double dot = PlanetVector3.Dot(player, dir);
        if (dot > 1.0) dot = 1.0;
        if (dot < -1.0) dot = -1.0;
        double arc = Radius * Math.Acos(dot);
        double cellArc = Radius * (u1 - u0) * (Math.PI / 4.0);
        return Math.Max(arc, cellArc * 0.5);
    }

    private static bool SameTile(RegionKey a, RegionKey b) =>
        a.Face == b.Face && a.Level == b.Level && a.X == b.X && a.Y == b.Y;
}
