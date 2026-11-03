using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 跨面相邻与服务端迁移校验。
/// <para>判据的重心不是"算出来一个邻居"，而是<b>邻接关系干不干净</b>：每块恰有 4 个共边邻居、
/// 每个邻居只通过唯一一条边回来、跨面那部分真的被走到。服务端要拿它放行/拒绝迁移，
/// 一个自指或不对称的邻居表会直接变成"玩家被传送到不该去的地方"。</para>
/// </summary>
public class PlanetRegionsTests
{
    private static IEnumerable<RegionKey> AllTiles(int level)
    {
        int span = 1 << level;
        for (int face = 0; face < CubeSphere.FaceCount; face++)
            for (int x = 0; x < span; x++)
                for (int y = 0; y < span; y++)
                    yield return new RegionKey(face, level, x, y);
    }

    private static PlanetVector3 EdgeSample(RegionKey key, RegionEdge edge)
    {
        var (u0, u1, v0, v1) = key.UvBounds();
        double midU = (u0 + u1) * 0.5, midV = (v0 + v1) * 0.5;
        return edge switch
        {
            RegionEdge.UMinus => CubeSphere.ToDirection(key.Face, u0, midV),
            RegionEdge.UPlus => CubeSphere.ToDirection(key.Face, u1, midV),
            RegionEdge.VMinus => CubeSphere.ToDirection(key.Face, midU, v0),
            _ => CubeSphere.ToDirection(key.Face, midU, v1),
        };
    }

    private static double ComponentSpan(in PlanetVector3 d)
    {
        double[] a = { Math.Abs(d.X), Math.Abs(d.Y), Math.Abs(d.Z) };
        Array.Sort(a);
        return a[2] - a[1];
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryTile_HasFourDistinctEdgeNeighbours_NoneIsItself(int level)
    {
        int crossFace = 0, total = 0;
        foreach (var key in AllTiles(level))
        {
            var neighbours = PlanetRegions.EdgeNeighbours(key);
            Assert.Equal(PlanetRegions.EdgeNeighbourCount, neighbours.Count);
            Assert.Equal(4, neighbours.Distinct().Count());          // 四个方向不能撞到同一块
            Assert.DoesNotContain(key, neighbours);
            total += neighbours.Count;
            crossFace += neighbours.Count(n => n.Face != key.Face);
            foreach (var n in neighbours) Assert.Equal(level, n.Level);
        }

        Assert.True(crossFace > 0, $"level {level} 一个跨面邻居都没有：判据只走到了同面那条路");
        if (level == 0) Assert.Equal(total, crossFace);              // 一面就是一块，邻居必然全在别的面
        else Assert.True(crossFace < total, $"level {level} 全是跨面邻居？同级网格应该有同面邻居");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void Adjacency_IsMutualAcrossExactlyOneEdgePair(int level)
    {
        foreach (var key in AllTiles(level))
        {
            foreach (RegionEdge edge in new[]
            {
                RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus,
            })
            {
                Assert.True(PlanetRegions.TryNeighbour(key, edge, out var n));
                Assert.True(PlanetRegions.TrySharedEdge(key, n, out var outEdge, out var backEdge),
                            $"{key} 与它的 {edge} 邻居 {n} 之间找不到唯一回程边");
                Assert.Equal(edge, outEdge);                           // 出去那条边必须能复现
                Assert.True(PlanetRegions.AreEdgeAdjacent(n, key));     // 对称性
            }
        }
    }

    [Fact]
    public void ReturnEdgeIsNotAlwaysTheOppositeOne_SoDoNotGuessIt()
    {
        // 立方体各面的 u/v 轴指向不统一：跨面之后，回程边可能是对边，也可能是同型边甚至别的边。
        // 这条把"想当然取 Opposite(edge)"这类实现钉死：只要哪天全部回程都对边了，
        // 说明轴表变了，这套推导要重写。
        int opposite = 0, other = 0;
        foreach (var key in AllTiles(0))
        {
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                Assert.True(PlanetRegions.TryNeighbour(key, edge, out var n));
                Assert.True(PlanetRegions.TrySharedEdge(key, n, out _, out var back));
                if (back == PlanetRegions.Opposite(edge)) opposite++;
                else other++;
            }
        }
        Assert.Equal(24, opposite + other);
        // level 0 的 24 次跨边里，实测 12 次回程落在对边、12 次落在别的边（这两个数是本轮临时把判据改成
        // 恒假、从失败消息里读出来的，不是推的）。判据只守 "other > 0"：它一旦归零就说明轴表变了。
        Assert.True(other > 0, "全部回程都落在对边上：这条「别猜对边」的证据没被走到，轴表可能变了");
    }

    [Fact]
    public void SameFaceNeighbourMatchesIndexArithmetic_CrossFaceNeedsTheNewPath()
    {
        var inner = new RegionKey(1, 6, 30, 30);
        Assert.True(PlanetRegions.TryNeighbour(inner, RegionEdge.UPlus, out var east));
        Assert.True(inner.TryNeighbour(1, 0, out var byIndex));
        Assert.Equal(byIndex, east);                                   // 同面内两条算法必须给同一个答案

        var atEdge = new RegionKey(1, 6, 63, 30);                     // 该面的最后一块
        Assert.False(atEdge.TryNeighbour(1, 0, out _));               // 老的索引算术只能报"没有邻居"
        Assert.True(PlanetRegions.TryNeighbour(atEdge, RegionEdge.UPlus, out var over));
        Assert.NotEqual(atEdge.Face, over.Face);                      // 新路径给的是跨面后的那一块
        Assert.True(PlanetRegions.AreEdgeAdjacent(atEdge, over));
        // 回程边只许由 TrySharedEdge 说了算，不许按"UPlus 的对边是 UMinus"猜：六个面的轴指向不统一，
        // 跨面之后回程可能是别的边（ReturnEdgeIsNotAlwaysTheOppositeOne 钉的就是这点）。真正的不变量是走回去。
        Assert.True(PlanetRegions.TrySharedEdge(over, atEdge, out var back, out var forwardAgain));
        Assert.Equal(RegionEdge.UPlus, forwardAgain);                   // 把两块重新喂进去，报的必须是当初走出去的那条边
        Assert.True(PlanetRegions.TryNeighbour(over, back, out var returned));
        Assert.Equal(atEdge, returned);                                 // 沿回程边再走一次，必须落在原块
    }

    [Fact]
    public void FaceBoundaryEdgesAreOnTheCubeRidge_InteriorEdgesAreNot()
    {
        // 这条不是功能判据，是"邻居采样为什么要推出半格"的证据，所以前提必须量准：
        // 只有落在立方体【面边界】上的边（u 或 v 等于 ±1）才真的踩在棱上——两个分量绝对值并列最大，
        // FaceOf 只能靠先后规则硬选一面，邻居就成了实现细节而不是几何。
        // 面内部的边（level 1 的 u=0 / v=0）三个分量本来就分得出大小，不存在并列；
        // 把两种情况混成"每条边都在棱上"会写出一条永远不该成立的判据。
        int ridge = 0, interior = 0;
        foreach (var key in AllTiles(1))
        {
            var (u0, u1, v0, v1) = key.UvBounds();
            foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
            {
                bool onFaceBoundary = edge switch
                {
                    RegionEdge.UMinus => u0 == -1.0,
                    RegionEdge.UPlus => u1 == 1.0,
                    RegionEdge.VMinus => v0 == -1.0,
                    _ => v1 == 1.0,
                };
                double span = ComponentSpan(EdgeSample(key, edge));
                if (onFaceBoundary)
                {
                    Assert.True(span < 1e-12,
                        $"面边界上的边不在棱上（并列差 {span:E3}）：轴表或 UvBounds 变了，" +
                        "半格采样防的那个歧义已不存在，这条证据要重写");
                    ridge++;
                }
                else
                {
                    Assert.True(span > 0.2,
                        $"面内部的边出现并列（{span:E3}）：它其实也踩在棱上，邻居会歧义");
                    interior++;
                }
            }
        }

        // level 1 每块的两条 u 边里恰好一条在面边界、一条在内部（v 同理），所以两类应当各占一半且都不为空。
        Assert.True(ridge > 0, "一条棱上的边都没找到：面边界的识别或轴表变了");
        Assert.True(interior > 0, "没有内部边：这一层的网格退化成了整面");
        Assert.Equal(interior, ridge);
    }

    private static (RegionKey from, RegionKey to, RegionTransferRequest req) GenuineTransfer(int level = 14)
    {
        var from = new RegionKey(0, level, 1 << level >> 1, 1 << level >> 1);
        Assert.True(PlanetRegions.TryNeighbour(from, RegionEdge.UPlus, out var to));
        PlanetVector3 dir = to.CenterDirection();
        var point = EcefCentimeters.FromVector(dir * (PlanetConstants.PlanetRadiusCm + 12_345 * 100));
        return (from, to, new RegionTransferRequest
        {
            FromRegionId = from.ToId(),
            ToRegionId = to.ToId(),
            GlobalCm = point,
            Tick = 7,
        });
    }

    [Fact]
    public void GenuineTransfer_IsAccepted_AndTheReceiptCarriesTheFacts()
    {
        var (from, to, req) = GenuineTransfer();
        var decision = PlanetRegions.ValidateTransfer(req);
        Assert.True(decision.Accepted, decision.ToString());
        Assert.Equal(TransferRejectReason.None, decision.Reason);
        Assert.Equal(to, decision.To);
        Assert.Equal(to, decision.RegionOfClaimedPoint);
        Assert.Contains("reason=None", decision.ToString());
        string summary = PlanetRegions.DescribeNeighbours(from);
        Assert.StartsWith("neighbours(", summary);
        foreach (var e in new[] { "UMinus=", "UPlus=", "VMinus=", "VPlus=" }) Assert.Contains(e, summary);
    }

    [Fact]
    public void EachRejectionHasItsOwnReason_WithPositiveTwinOnTheSameFixture()
    {
        var (from, to, req) = GenuineTransfer();
        Assert.True(PlanetRegions.ValidateTransfer(req).Accepted);     // 正向对照先跑一遍

        long far = new RegionKey(3, 14, 100, 200).ToId();
        var sameAsFrom = req with { ToRegionId = from.ToId() };
        Assert.Equal(TransferRejectReason.PointNotInTarget,            // 声称的目标与点所在不符（这里也含 from==to 的骗法）
                     PlanetRegions.ValidateTransfer(sameAsFrom).Reason);

        // 点确实在 to 里，但 to 不是 from 的邻居：换个远处的点，且把 to 换成该点真正的区域
        var alien = new RegionKey(2, 14, 1234, 5678);
        var farPoint = EcefCentimeters.FromVector(alien.CenterDirection() * PlanetConstants.PlanetRadiusCm);
        var notAdjacent = req with { ToRegionId = alien.ToId(), GlobalCm = farPoint };
        Assert.Equal(TransferRejectReason.NotAdjacent, PlanetRegions.ValidateTransfer(notAdjacent).Reason);
        Assert.Equal(alien, PlanetRegions.ValidateTransfer(notAdjacent).RegionOfClaimedPoint);

        // 全局点算出的区域对，但声称的 to 是另一个同级区域
        var wrongTo = req with { ToRegionId = far };
        var wrong = PlanetRegions.ValidateTransfer(wrongTo);
        Assert.Equal(TransferRejectReason.PointNotInTarget, wrong.Reason);
        Assert.Equal(to, wrong.RegionOfClaimedPoint);                  // 日志要能指出"你其实在这里"

        // 等级不对：id 用 level 13 编，判定按 14
        var lvl = new RegionKey(0, 13, 100, 100);
        var mismatch = req with { ToRegionId = lvl.ToId() };
        Assert.Equal(TransferRejectReason.LevelMismatch, PlanetRegions.ValidateTransfer(mismatch).Reason);

        // 高空/天外：把点整体放大到 R + 3000 km
        var high = req with
        {
            GlobalCm = EcefCentimeters.FromVector(to.CenterDirection() *
                (PlanetConstants.PlanetRadiusCm + 3_000_000_000)),
        };
        var highDecision = PlanetRegions.ValidateTransfer(high);
        Assert.Equal(TransferRejectReason.TooFarFromSurface, highDecision.Reason);
        Assert.True(highDecision.OffSurfaceCm > PlanetRegions.MaxTransferAltitudeCm);
        Assert.True(Math.Abs(highDecision.OffSurfaceCm - 3_000_000_000) < 1e7,
                    $"离面 {highDecision.OffSurfaceCm:E3} cm 与放大 3000 km 的预期差太多");

        // 允许在闸门内的高空：500 km 应当放行（飞行/跳跃不被误拒）
        var flying = req with
        {
            GlobalCm = EcefCentimeters.FromVector(to.CenterDirection() *
                (PlanetConstants.PlanetRadiusCm + 50_000_000)),
        };
        Assert.True(PlanetRegions.ValidateTransfer(flying).Accepted);

        // 零向量 = 没有方向可言
        var zero = req with { GlobalCm = new EcefCentimeters(0, 0, 0) };
        Assert.Equal(TransferRejectReason.MalformedPoint, PlanetRegions.ValidateTransfer(zero).Reason);

        // regionId 解不开（face 位段越界）：只能算坏包，不能让异常穿过服务端
        var junk = req with { ToRegionId = -1 };
        Assert.Equal(TransferRejectReason.MalformedRegion, PlanetRegions.ValidateTransfer(junk).Reason);
        var junkFace = req with { FromRegionId = 7L << 58 };
        Assert.Equal(TransferRejectReason.MalformedRegion, PlanetRegions.ValidateTransfer(junkFace).Reason);

        // 正向对照：把上面每条改回去，同一夹具仍然放行
        Assert.True(PlanetRegions.ValidateTransfer(req).Accepted);
    }

    [Fact]
    public void SameRegionTransferIsRejectedAfterThePointCheck()
    {
        var (from, to, req) = GenuineTransfer();
        // 点真的在 to 里，同时把 from 也写成 to：这不是跨区，拒
        var self = req with { FromRegionId = to.ToId() };
        Assert.Equal(TransferRejectReason.SameRegion, PlanetRegions.ValidateTransfer(self).Reason);
        Assert.True(PlanetRegions.ValidateTransfer(req).Accepted);
    }

    [Fact]
    public void NeighbourDistanceIsAboutOneTileWide_NotSeveral()
    {
        var key = new RegionKey(4, 14, 8192, 8192);
        double tileWidth = RegionMetrics.CenterWidthCm(key);
        var centre = key.CenterDirection();
        foreach (RegionEdge edge in new[] { RegionEdge.UMinus, RegionEdge.UPlus, RegionEdge.VMinus, RegionEdge.VPlus })
        {
            Assert.True(PlanetRegions.TryNeighbour(key, edge, out var n));
            double arc = CubeSphere.GreatCircleCm(centre, n.CenterDirection(), PlanetConstants.PlanetRadiusCm);
            Assert.True(arc > tileWidth * 0.5 && arc < tileWidth * 1.9,
                        $"{edge} 邻居中心距 {arc / 100000.0:F3} km，本格宽 {tileWidth / 100000.0:F3} km，差一个格子的样子都不像");
        }
    }

    [Fact]
    public void LevelZeroTiles_NeighboursCoverTheFourAdjacentFaces()
    {
        var face0 = new RegionKey(0, 0, 0, 0);
        var neighbours = PlanetRegions.EdgeNeighbours(face0);
        Assert.Equal(4, neighbours.Count);
        Assert.Equal(4, neighbours.Select(n => n.Face).Distinct().Count());   // 立方体一面邻四邻，不重复
        Assert.DoesNotContain(neighbours, n => n.Face == 0);                   // 也不含自己那面
        Assert.All(neighbours, n => Assert.Equal(0, n.Level));
    }
}
