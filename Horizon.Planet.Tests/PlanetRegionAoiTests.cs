using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// §6.4 的 AOI 第二层（区域 → 100 m cell → 实体）。
/// <para>这一层最容易做错的两件事都有专门的判据：① "某条边朝哪个轴"不能按面号查表
/// （§13.15 那次实机事故就是把"局部 +X"当成"东 = UPlus"用到了六个面上，而 uv 轴向每面不同）；
/// ② cell 下标范围必须两端都覆盖，漏掉靠正方向那一列就是漏掉共享边所在的那一列。</para>
/// </summary>
public class PlanetRegionAoiTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;
    private const int S = PlanetRegionAuthority.CellSizeCm;         // 100 m

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey EdgeOfFace = new(2, Level, (1 << Level) - 1, 8192);

    private static CellKey CellOf(RegionKey key, int axis, int index, int other = 0) =>
        axis == PlanetRegionAoi.AxisX ? new CellKey(key, index, other) : new CellKey(key, other, index);

    /// <summary>沿给定局部水平方向走 step 厘米，把落点投回球面后反查区域。</summary>
    private static RegionKey WalkInto(RegionKey from, int axis, int sign, double stepCm)
    {
        var frame = RegionFrame.Build(from, Radius);
        var local = new PlanetVector3(
            axis == PlanetRegionAoi.AxisX ? sign * stepCm : 0,
            0,
            axis == PlanetRegionAoi.AxisZ ? sign * stepCm : 0);
        var global = frame.GlobalFromLocalCm(local);
        return RegionKey.FromDirection(global.Normalized(), from.Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void 边方向是量出来的_往那一侧走一步真的进邻区(int face)
    {
        // 三个取样点：面心、面边缘、面角附近——面心不跨面，另两个跨面，uv 轴向翻转只在后两者出现
        foreach (var (x, y) in new[] { (8192, 8192), ((1 << Level) - 1, 8192), (8192, 0) })
        {
            var key = new RegionKey(face, Level, x, y);
            var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(key, Radius);
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                if (!PlanetRegions.TryNeighbour(key, edge, out var neighbour)) continue;
                var (axis, sign) = PlanetRegionAoi.EdgeDirection(key, edge, Radius);
                double half = axis == PlanetRegionAoi.AxisX ? halfX : halfZ;

                // 独立参照：几何上往那个方向走过边界，反查回来的必须就是那个邻居
                var landed = WalkInto(key, axis, sign, half * 1.5);
                Assert.Equal(neighbour, landed);

                // 反方向走一步必须还留在本区（说明符号没搞反）
                Assert.Equal(key, WalkInto(key, axis, -sign, half * 0.4));
            }
        }
    }

    [Fact]
    public void 跨面那对邻居的边方向也量得出来()
    {
        Assert.True(PlanetRegions.TryNeighbour(EdgeOfFace, RegionEdge.UPlus, out var across));
        Assert.NotEqual(EdgeOfFace.Face, across.Face);                 // 确实跨面了

        var (axis, sign) = PlanetRegionAoi.EdgeDirection(EdgeOfFace, RegionEdge.UPlus, Radius);
        var (halfX, _) = PlanetRegionAoi.LocalHalfExtents(EdgeOfFace, Radius);
        Assert.Equal(across, WalkInto(EdgeOfFace, axis, sign, halfX * 1.5));

        // §13.13 那条"回程边不一定等于对边"在这里必须显式成立一次：
        // 用回程边算出来的方向，从邻居那侧走回来要落在 EdgeOfFace
        Assert.True(PlanetRegions.TrySharedEdge(EdgeOfFace, across, out _, out var edgeBack));
        var (backAxis, backSign) = PlanetRegionAoi.EdgeDirection(across, edgeBack, Radius);
        var (backHalfX, backHalfZ) = PlanetRegionAoi.LocalHalfExtents(across, Radius);
        double backHalf = backAxis == PlanetRegionAoi.AxisX ? backHalfX : backHalfZ;
        Assert.Equal(EdgeOfFace, WalkInto(across, backAxis, backSign, backHalf * 1.5));
    }

    [Fact]
    public void cell下标范围两端都覆盖_不漏共享边那一列()
    {
        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        foreach (var half in new[] { halfX, halfZ })
        {
            var (first, last) = PlanetRegionAoi.CellRange(half);
            // 覆盖 [-half, +half] 闭区间：靠上界那一列必须在范围内
            Assert.True(first * S <= -half, $"下界没盖住：first={first} half={half:0}");
            Assert.True((last + 1) * S > half, $"上界没盖住：last={last} half={half:0}");
            Assert.True(last >= (int)Math.Floor(half / S), "共享边那一列被漏掉了");

            // 反证（用合成值，真实半跨可能恰好是整数倍让这条没有牙齿）：
            // 旧写法 -ceil(half/S) 起、ceil(2·half/S) 列，在 half=105 000 cm 时最后一列被丢掉
            var (nFirst, _) = PlanetRegionAoi.CellRange(half);
            int naiveColumns = (int)Math.Ceiling(2.0 * 105_000 / S);
            var (synthFirst, synthLast) = PlanetRegionAoi.CellRange(105_000);
            Assert.Equal(synthLast, synthFirst + synthLast - synthFirst);        // 参照：闭区间的上界
            Assert.True(synthFirst + naiveColumns - 1 < synthLast,
                        "naive 版本在合成值上也没漏列，这条反证需要换一个非整数倍的半跨");
            Assert.True(nFirst <= first);
        }
    }

    [Fact]
    public void 边界cell铺满整条共享边且只在靠边一侧()
    {
        const double margin = 500 * 100;                                // 500 m
        var cells = PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, margin, Radius);
        Assert.NotEmpty(cells);
        var neighbour = PlanetRegions.TryNeighbour(Home, RegionEdge.UPlus, out var n) ? n : throw new InvalidOperationException();
        var (axis, sign) = PlanetRegionAoi.EdgeDirection(neighbour,
            PlanetRegions.TrySharedEdge(Home, neighbour, out _, out var edgeBack) ? edgeBack : throw new InvalidOperationException(),
            Radius);
        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(neighbour, Radius);
        double half = axis == PlanetRegionAoi.AxisX ? halfX : halfZ;

        // 每一格都真的靠边：靠边那一端到边的距离 ≤ margin
        foreach (var cell in cells)
        {
            Assert.Equal(neighbour, cell.Region);
            int index = axis == PlanetRegionAoi.AxisX ? cell.Cx : cell.Cz;
            Assert.True(PlanetRegionAoi.CellNearEdge(index, axis, sign, half, margin, S),
                        $"cell {cell} 并不靠边");
        }

        // 沿边那一维必须铺满：否则斜着走到角上的人会突然看不见
        var (otherFirst, otherLast) = PlanetRegionAoi.CellRange(
            axis == PlanetRegionAoi.AxisX ? halfZ : halfX, S);
        var alongEdge = cells.Select(c => axis == PlanetRegionAoi.AxisX ? c.Cz : c.Cx).Distinct().OrderBy(v => v).ToList();
        Assert.Equal(Enumerable.Range(otherFirst, otherLast - otherFirst + 1), alongEdge);

        // 靠边那一维的列数 = margin 覆盖到的列数（不多不少）
        // 允许比 ceil(margin/S) 多一列：最靠外那一格的远端本来就超出半跨（角点给的半跨是上界），
        // 这一列多订阅不是错——筛子的方向必须是"宁可多算一列"，少算才是"边界上的人突然不动"。
        int expectedColumns = (int)Math.Ceiling(margin / S);
        var nearSide = cells.Select(c => axis == PlanetRegionAoi.AxisX ? c.Cx : c.Cz).Distinct().Count();
        Assert.True(nearSide >= expectedColumns && nearSide <= expectedColumns + 1,
                    $"靠边列数 {nearSide} 与余量应有的 {expectedColumns} 列差太多");
    }

    [Fact]
    public void 靠边互相订阅_深入邻区那一侧不订阅()
    {
        const double margin = 500 * 100;
        var neighbour = NeighbourOf(Home, RegionEdge.UPlus);
        var backEdge = SharedEdgeBackFrom(Home, neighbour);

        var (nAxis, nSign) = PlanetRegionAoi.EdgeDirection(neighbour, backEdge, Radius);
        var (nHalfX, nHalfZ) = PlanetRegionAoi.LocalHalfExtents(neighbour, Radius);
        var (nFirst, nLast) = PlanetRegionAoi.CellRange(nAxis == PlanetRegionAoi.AxisX ? nHalfX : nHalfZ, S);

        var (vAxis, vSign) = PlanetRegionAoi.EdgeDirection(Home, RegionEdge.UPlus, Radius);
        var (vHalfX, vHalfZ) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        var (vFirst, vLast) = PlanetRegionAoi.CellRange(vAxis == PlanetRegionAoi.AxisX ? vHalfX : vHalfZ, S);

        var viewerNear = CellOf(Home, vAxis, vSign > 0 ? vLast : vFirst);
        var targetNear = CellOf(neighbour, nAxis, nSign > 0 ? nLast : nFirst);
        var targetDeep = CellOf(neighbour, nAxis, nSign > 0 ? nFirst : nLast);
        var viewerDeep = CellOf(Home, vAxis, vSign > 0 ? vFirst : vLast);

        Assert.True(PlanetRegionAoi.Subscribes(viewerNear, targetNear, margin, Radius),
                    "两边都贴着共享边却不互相订阅");
        Assert.False(PlanetRegionAoi.Subscribes(viewerNear, targetDeep, margin, Radius),
                     "深入邻区那一格不该被订阅——余量只有 500 m");

        // 另一半的正向对照：本区不靠边时，即使对方贴边也不订阅（这一层要的是"两边都贴边"）
        Assert.False(PlanetRegionAoi.Subscribes(viewerDeep, targetNear, margin, Radius));
        Assert.False(PlanetRegionAoi.Subscribes(targetNear, viewerDeep, margin, Radius));
    }

    private static RegionKey NeighbourOf(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n) ? n : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");

    private static RegionEdge SharedEdgeBackFrom(RegionKey a, RegionKey b) =>
        PlanetRegions.TrySharedEdge(a, b, out _, out var edgeBack)
            ? edgeBack
            : throw new InvalidOperationException($"{a} 与 {b} 不共边");

    [Fact]
    public void 订阅关系对称()
    {
        const double margin = 500 * 100;
        var east = PlanetRegions.TryNeighbour(Home, RegionEdge.UPlus, out var e) ? e : throw new InvalidOperationException();
        var north = PlanetRegions.TryNeighbour(Home, RegionEdge.VMinus, out var nm) ? nm : throw new InvalidOperationException();
        var farAway = new RegionKey((Home.Face + 2) % 6, Level, 100, 100);

        var regions = new[] { Home, east, north, farAway };
        var samples = new List<CellKey>();
        foreach (var key in regions)
        {
            var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(key, Radius);
            var (fx, lx) = PlanetRegionAoi.CellRange(halfX, S);
            var (fz, lz) = PlanetRegionAoi.CellRange(halfZ, S);
            samples.Add(new CellKey(key, lx, lz));            // 角上（两个方向都靠边）
            samples.Add(new CellKey(key, fx, fz));            // 对角
            samples.Add(new CellKey(key, (fx + lx) / 2, (fz + lz) / 2));   // 中心（谁都不靠）
            samples.Add(new CellKey(key, lx, (fx + lx) / 2));
        }

        foreach (var a in samples)
            foreach (var b in samples)
                Assert.Equal(PlanetRegionAoi.Subscribes(a, b, margin, Radius),
                             PlanetRegionAoi.Subscribes(b, a, margin, Radius));
    }

    [Fact]
    public void 不相邻的区域一律不订阅()
    {
        const double margin = 5_000 * 100;                     // 余量给到 5 km，也不该把不相邻的拉进来
        var farAway = new RegionKey((Home.Face + 2) % 6, Level, 100, 100);
        var a = new CellKey(Home, 0, 0);
        var b = new CellKey(farAway, 100, 100);
        Assert.False(PlanetRegionAoi.Subscribes(a, b, margin, Radius));

        // 同面但隔着一块：也不订阅（区域层只订阅共边邻居）
        var twoApart = PlanetRegions.TryNeighbour(Home, RegionEdge.UPlus, out var e1)
            ? PlanetRegions.TryNeighbour(e1, RegionEdge.UPlus, out var e2) ? e2 : throw new InvalidOperationException()
            : throw new InvalidOperationException();
        Assert.False(PlanetRegionAoi.Subscribes(a, new CellKey(twoApart, 0, 0), margin, Radius));
    }

    [Fact]
    public void 同区域内一视同仁_这一层不裁区域内的可见性()
    {
        // P1 那条"双客户端跨区不丢实体"的验收下界：同区就是看得见，与 cell 无关。
        // 这一层要是顺手把区域内也按距离裁掉，验收判据就会变成"走远了队友自动消失"。
        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        var (fx, lx) = PlanetRegionAoi.CellRange(halfX, S);
        var (fz, lz) = PlanetRegionAoi.CellRange(halfZ, S);
        Assert.True(PlanetRegionAoi.Subscribes(new CellKey(Home, fx, fz), new CellKey(Home, lx, lz), 1, Radius));

        // 与权威表的账对得上：区域粒度的可见性不因这一层而变。
        // 取点用"近中心但不同格"：角上那些极端 cell 投回球面已经出区了，那不是这条要说的东西。
        var frame = RegionFrame.Build(Home, Radius);
        var (halfX2, halfZ2) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(a.TryBind(1, frame.GlobalFromLocalCm(new PlanetVector3(-0.3 * halfX2, 0, -0.3 * halfZ2)), 0, out var r1));
        Assert.True(a.TryBind(2, frame.GlobalFromLocalCm(new PlanetVector3(0.3 * halfX2, 0, 0.3 * halfZ2)), 0, out var r2));
        Assert.Equal(r1, r2);                                  // 前提：两个点确实还在同一块区域里
        Assert.Contains(2L, a.VisibleEntities(1));
        Assert.Contains(1L, a.VisibleEntities(2));
    }

    [Fact]
    public void 余量越大订阅的cell只会变多()
    {
        var small = new HashSet<CellKey>(PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, 100 * 100, Radius));
        var big = new HashSet<CellKey>(PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, 900 * 100, Radius));
        Assert.True(big.Count > small.Count, $"余量放大 9 倍，集合却没长大：{small.Count} → {big.Count}");
        foreach (var cell in small) Assert.True(big.Contains(cell), $"小余量里的 {cell} 在大余量里没了");
    }

    [Fact]
    public void 参数不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, 0, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, -1, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.Subscribes(new CellKey(Home, 0, 0), new CellKey(Home, 1, 1), 0, Radius));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.CellNearEdge(0, PlanetRegionAoi.AxisX, 1, 1e5, 100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.CellNearEdge(0, PlanetRegionAoi.AxisX, 1, 1e5, -1, S));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.CellNearEdge(0, PlanetRegionAoi.AxisX, 0, 1e5, 100, S));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionAoi.CellRange(1e5, 0));

        // 层级不同不该被拿来比较：cell 号在两个层级上根本不是同一把尺
        Assert.Throws<ArgumentException>(() => PlanetRegionAoi.Subscribes(
            new CellKey(Home, 0, 0), new CellKey(new RegionKey(2, Level - 1, 4096, 4096), 0, 0), 100 * 100, Radius));

        // 正向孪生：合法参数照常给出结果
        Assert.NotEmpty(PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, 300 * 100, Radius));
    }

    [Fact]
    public void 同一面内相邻帧连续_极地面不再差九十度()
    {
        // §13.47 那个缺陷的形状是**同一个面内**相邻区域的东轴差 90°（面 4/5 面心附近地理东奇异）。
        // 修好之后这条从"断言缺陷存在"翻成"要求连续"——注释里当时就写了这一步。
        //
        // 只比同面的邻居：跨面那对本来就要换帧（立方体在这里折 90°），
        // 库里的规矩是跨面由全局点反查重算帧（§13.15），不靠帧之间的连续性。
        // 跨面那对的要求是另一件事，由 跨面那对邻居的边方向也量得出来 与 AOI 的对称性判据管。
        double WorstSameFaceAngle(int face, int x, int y)
        {
            var key = new RegionKey(face, Level, x, y);
            var frame = RegionFrame.Build(key, Radius);
            double worst = 0;
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                if (!PlanetRegions.TryNeighbour(key, edge, out var n)) continue;
                if (n.Face != key.Face) continue;                       // 跨面不比：本来就该差
                double dot = Math.Clamp(PlanetVector3.Dot(frame.East, RegionFrame.Build(n, Radius).East), -1.0, 1.0);
                worst = Math.Max(worst, Math.Acos(dot) * 180.0 / Math.PI);
            }
            return worst;
        }

        foreach (int face in Enumerable.Range(0, 6))
        {
            double worst = 0;
            foreach (var (x, y) in new[] { (8192, 8192), ((1 << Level) - 1, 8192), (8192, 0), (0, 0) })
                worst = Math.Max(worst, WorstSameFaceAngle(face, x, y));
            Console.WriteLine($"AOI_FRAME_FACE_SAMEFACE face={face} worst={worst:0.#####}");
            Assert.True(worst <= 1.0, $"面 {face} 同面相邻帧东轴差 {worst:0.###}°，超过 1°");
        }
    }

    [Fact]
    public void 极地面面心那一块的邻居帧现在只差一点点_旧缺陷的规模留档()
    {
        // 修之前实测：面 4 与面 5 的面心那一块，同面邻居的东轴正好差 90°。
        // 这条把"修前 90°、修后 ≤1°"钉在同一处几何上，免得以后有人把口径改回去而全套判据照样绿。
        foreach (int face in new[] { 4, 5 })
        {
            var centre = new RegionKey(face, Level, 8192, 8192);
            var frame = RegionFrame.Build(centre, Radius);
            int compared = 0;
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                if (!PlanetRegions.TryNeighbour(centre, edge, out var n) || n.Face != face) continue;
                double dot = Math.Clamp(PlanetVector3.Dot(frame.East, RegionFrame.Build(n, Radius).East), -1.0, 1.0);
                double angle = Math.Acos(dot) * 180.0 / Math.PI;
                Console.WriteLine($"AOI_POLAR_FIXED face={face} edge={edge} angle_deg={angle:0.#####}");
                Assert.True(angle <= 1.0, $"面 {face} 沿 {edge} 的邻居帧还差 {angle:0.###}°");
                compared++;
            }
            Assert.True(compared >= 2, $"面 {face} 同面邻居不足两块，这条没有覆盖到极面心（{compared}）");
        }
    }

    [Fact]
    public void 修法只动极地面_其余四个面的东轴逐位不变()
    {
        // 这条是"改动半径"的判据：修极点缺陷不能顺手把四个本来正常的面的局部语义换掉。
        // 参照是旧口径的解析式（地理东 = 极轴 × 上），逐位比。
        foreach (int face in new[] { 0, 1, 2, 3 })
            foreach (var (x, y) in new[] { (8192, 8192), ((1 << Level) - 1, 8192), (8192, 0) })
            {
                var key = new RegionKey(face, Level, x, y);
                var up = key.CenterDirection();
                var expected = PlanetVector3.Cross(PlanetVector3.AxisZ, up).Normalized();
                Assert.Equal(expected.X, RegionFrame.Build(key, Radius).East.X, 12);
                Assert.Equal(expected.Y, RegionFrame.Build(key, Radius).East.Y, 12);
                Assert.Equal(expected.Z, RegionFrame.Build(key, Radius).East.Z, 12);
            }

        // 极地面则必须换成"本面 u 轴的切平面投影"，并且仍然单位正交右手
        foreach (int face in new[] { 4, 5 })
        {
            var key = new RegionKey(face, Level, 8192, 8192);
            var frame = RegionFrame.Build(key, Radius);
            var u = CubeSphere.FaceUAxis(face);
            var expected = (u - frame.Up * PlanetVector3.Dot(u, frame.Up)).Normalized();
            Assert.Equal(expected.X, frame.East.X, 12);
            Assert.Equal(1.0, frame.East.LengthSquared, 12);
            Assert.Equal(0.0, PlanetVector3.Dot(frame.East, frame.Up), 12);
            Assert.Equal(0.0, PlanetVector3.Dot(frame.East, frame.South), 12);
            Assert.Equal(PlanetVector3.Cross(frame.East, frame.Up).X, frame.South.X, 12);
        }
    }

    [Fact]
    public void 默认余量由两个实测锚推出_而且真的在筛()
    {
        // 余量不是拍出来的：它必须盖住"客户端会把地形加载到多远"和"现有网络视野有多大"，
        // 又不能大到把整块邻区都订阅回来（那样第二层就白做）。
        Assert.True(PlanetRegionAoi.DefaultMarginCm >= PlanetRegionAoi.NearFieldLoadHalfSpanCm,
                    $"余量 {PlanetRegionAoi.DefaultMarginCm} 盖不住近景加载半跨 {PlanetRegionAoi.NearFieldLoadHalfSpanCm}");
        Assert.True(PlanetRegionAoi.DefaultMarginCm >= PlanetRegionAoi.LegacyViewRadiusCm,
                    $"余量比既有平面世界的网络视野 {PlanetRegionAoi.LegacyViewRadiusCm} cm 还小，上线后会更看不见人");
        Assert.Equal(0, PlanetRegionAoi.DefaultMarginCm % S);                    // 整格，不留半列

        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        Assert.True(PlanetRegionAoi.DefaultMarginCm < halfX && PlanetRegionAoi.DefaultMarginCm < halfZ,
                    $"余量 {PlanetRegionAoi.DefaultMarginCm / 100.0:0} m 已经接近区域半跨 {halfX / 100.0:0} m，筛子等于没筛");

        // 正面量一次"筛掉了多少"：边界列数 / 邻区总列数必须明显小于一
        var neighbour = NeighbourOf(Home, RegionEdge.UPlus);
        var cells = PlanetRegionAoi.BoundaryCells(Home, RegionEdge.UPlus, PlanetRegionAoi.DefaultMarginCm, Radius);
        int totalColumns = PlanetRegionAoi.CellColumns(neighbour, PlanetRegionAoi.AxisX, Radius);
        int nearColumns = cells.Select(c => c.Cx).Distinct().Count();
        double share = (double)nearColumns / totalColumns;
        Console.WriteLine($"AOI_MARGIN share={share:0.###} near={nearColumns} total={totalColumns}");
        Assert.True(share < 0.5, $"只订阅了邻区 {share:0%} 的深度还算筛子，但接近一半时这一层的收益已经很小");
        Assert.True(share > 0.05, $"筛得太狠（{share:0%}），边界外一点的人就进不来了");
    }

    [Fact]
    public void 边界订阅逐条与Subscribes一致()
    {
        var subs = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(subs.TryBind(1, RegionFrame.Build(Home, Radius).GlobalFromLocalCm(new PlanetVector3(0, 0, 0)), 0, out var own));
        var regions = subs.Subscriptions(1);
        var listed = PlanetRegionAoi.BoundaryCellSubscriptions(own, regions);

        // 本区不列出来（整块订阅）
        Assert.DoesNotContain(listed, c => c.Region == own);
        // 列出来的每一格都属于订阅集里的某个邻区
        Assert.All(listed, c => Assert.Contains(c.Region, regions));

        // 逐条与 Subscribes 对齐：取本区靠边那一格当观察者，看到的集合必须正好等于列出来的集合
        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(own, Radius);
        foreach (var region in regions.Where(r => r != own))
        {
            if (!PlanetRegions.TrySharedEdge(own, region, out var edgeFromOwn, out var edgeFromRegion)) continue;
            var (vAxis, vSign) = PlanetRegionAoi.EdgeDirection(own, edgeFromOwn, Radius);
            var (nAxis, nSign) = PlanetRegionAoi.EdgeDirection(region, edgeFromRegion, Radius);
            var (vFirst, vLast) = PlanetRegionAoi.CellRange(vAxis == PlanetRegionAoi.AxisX ? halfX : halfZ, S);
            var (nFirst, nLast) = PlanetRegionAoi.CellRange(
                nAxis == PlanetRegionAoi.AxisX
                    ? PlanetRegionAoi.LocalHalfExtents(region, Radius).HalfX
                    : PlanetRegionAoi.LocalHalfExtents(region, Radius).HalfZ, S);
            var viewerEdge = vAxis == PlanetRegionAoi.AxisX
                ? new CellKey(own, vSign > 0 ? vLast : vFirst, 0)
                : new CellKey(own, 0, vSign > 0 ? vLast : vFirst);

            var expected = PlanetRegionAoi.BoundaryCells(own, edgeFromOwn, PlanetRegionAoi.DefaultMarginCm, Radius);
            Assert.NotEmpty(expected);
            Assert.All(expected, c => Assert.True(
                PlanetRegionAoi.Subscribes(viewerEdge, c, PlanetRegionAoi.DefaultMarginCm, Radius),
                $"列出来的 {c} 却不被 Subscribes 认账——筛子与判据两套真相"));
            Assert.All(expected, c => Assert.Contains(c, listed));
        }
    }

    [Fact]
    public void 不相邻的区域不出现在边界订阅里()
    {
        var farAway = new RegionKey((Home.Face + 2) % 6, Level, 100, 100);
        var regions = new List<RegionKey> { Home, farAway };
        var listed = PlanetRegionAoi.BoundaryCellSubscriptions(Home, regions);
        Assert.Empty(listed);                                   // 只有本区 + 一个不相邻的：什么都不该加载
        Assert.Throws<ArgumentNullException>(
            () => PlanetRegionAoi.BoundaryCellSubscriptions(Home, null!));
    }

    [Fact]
    public void 局部半跨是上界_角点比边中点远()
    {
        var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(Home, Radius);
        var frame = RegionFrame.Build(Home, Radius);
        // 四条边中点（比角点更"正"）到中心的水平距离必须不超过角点给出的半跨
        foreach (var edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
        {
            var (u0, u1, v0, v1) = Home.UvBounds();
            double u = edge is RegionEdge.UMinus or RegionEdge.UPlus ? (edge == RegionEdge.UMinus ? u0 : u1) : (u0 + u1) / 2;
            double v = edge is RegionEdge.VMinus or RegionEdge.VPlus ? (edge == RegionEdge.VMinus ? v0 : v1) : (v0 + v1) / 2;
            var local = frame.LocalFromGlobalCm(Horizon.Planet.CubeSphere.ToDirection(Home.Face, u, v) * Radius);
            Assert.True(Math.Abs(local.X) <= halfX + 1, $"边中点的 X 超出了角点给出的半跨：{local.X:0} vs {halfX:0}");
            Assert.True(Math.Abs(local.Z) <= halfZ + 1, $"边中点的 Z 超出了角点给出的半跨：{local.Z:0} vs {halfZ:0}");
        }
    }

    [Fact]
    public void 六个面每一边都有边界cell_闭合球面不许出现空订阅()
    {
        foreach (int face in Enumerable.Range(0, 6))
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                var key = new RegionKey(face, Level, 8192, 8192);
                var cells = PlanetRegionAoi.BoundaryCells(key, edge, 300 * 100, Radius);
                Assert.True(cells.Count > 0, $"{key} 沿 {edge} 的边界 cell 是空的");
                // 每一格都真的属于那个邻居
                var neighbour = PlanetRegions.TryNeighbour(key, edge, out var n) ? n : throw new InvalidOperationException();
                Assert.All(cells, c => Assert.Equal(neighbour, c.Region));
            }
    }
}
