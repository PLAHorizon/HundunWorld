using System;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 沿切平面"往东走"时，区域归属到底怎么变。
/// <para>起因是引擎侧近景驱动的一次真实拒绝：它按 <see cref="PlanetRegions.AreEdgeAdjacent"/>
/// 校验新区域，结果 4 次换区被拒 3 次。纯 C# 复现证明坐标链没错——<b>立方投影的 uv 格网与局部东西线
/// 不平行</b>，所以沿东向走会合法地同时对角换格；把服务端"一次输入只跨一格"的不变量搬到客户端换基上，
/// 判据就会拒绝一个正确结果，而拒绝之后玩家继续在旧基里走，才会累积成"跨两格"那种更假的形状。</para>
/// <para>因此这里钉两件事：① 对角换区确实会发生（不为 0），并且每次检查只走一小段时<b>不会</b>跨两格；
/// ② 换基后玩家相对新区域中心的局部距离必须落在该区域"中心到角"之内——
/// 驱动 v1 那个"换区后局部坐标凭空 +8.1 km"就是这条被破坏的样子。</para>
/// </summary>
public class PlanetRegionWalkGeometryTests
{
    private const int Level = PlanetConstants.DefaultRegionLevel;      // 14：区域宽实测 4.226–7.723 km
    private const double PatchCm = PlanetConstants.TerrainPatchSizeCm; // 508 m，驱动每步一块

    private static PlanetVector3 Start() =>
        Geodetic.DirectionFromLatLon(30.0 * Math.PI / 180.0, 120.0 * Math.PI / 180.0);

    [Fact]
    public void WalkingEast_CrossesDiagonallySometimes_NeverMoreThanOneTilePerCheck()
    {
        var key = RegionKey.FromDirection(Start(), Level);
        var frame = RegionFrame.Build(key);

        int same = 0, edgeOnly = 0, diagonal = 0, multiTile = 0;
        double localX = 0, localZ = 0;
        const int checks = 40;
        const int stepsPerCheck = 6;                    // 3.05 km，小于最窄区域宽 4.226 km

        for (int c = 0; c < checks; c++)
        {
            for (int s = 0; s < stepsPerCheck; s++) localX += PatchCm;

            var globalCm = frame.GlobalFromLocalCm(new PlanetVector3(localX, 0, localZ));
            var found = RegionKey.FromDirection(globalCm.Normalized(), Level);

            if (found == key) { same++; continue; }

            int dx = found.X - key.X, dy = found.Y - key.Y;
            int reach = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (reach > 1) multiTile++;
            else if (dx != 0 && dy != 0) diagonal++;
            else edgeOnly++;

            // 换基：新 frame 下玩家的局部坐标必须还在"中心到角"之内
            var nextFrame = RegionFrame.Build(found);
            var newLocal = nextFrame.LocalFromGlobalCm(globalCm);
            double corner = RegionMetrics.CenterToCornerCm(found);
            double horizontal = Math.Sqrt(newLocal.X * newLocal.X + newLocal.Z * newLocal.Z);
            Assert.True(horizontal <= corner + 1_000.0,
                        $"换基后玩家离新区域中心 {horizontal / 100000.0:0.###} km，超过中心到角 {corner / 100000.0:0.###} km");

            frame = nextFrame;
            key = found;
            localX = newLocal.X;
            localZ = newLocal.Z;
        }

        Assert.Equal(0, multiTile);                                  // 检查间隔足够密：一次最多跨一格
        Assert.True(diagonal > 0, "沿东向走 40 次检查里一次对角换区都没有：uv 格网与局部轴的对齐关系变了，客户端的换基规则要重推");
        Assert.True(edgeOnly > 0, "只出现对角换区、没有单纯边换区：同样说明轴表变了");
        Assert.True(same > 0, "每次检查都换区？检查间隔比区域还宽，这条夹具的密度不对");
    }

    [Fact]
    public void EdgeAdjacencyIsTheServerInvariant_NotTheClientsReanchorRule()
    {
        // 同一段走查里，对角换区是<em>正确</em>的落点，但它不是共边邻居。
        // 服务端一次输入只能跨一格（§13.13 的 NotAdjacent 拒绝），客户端换基必须无条件跟到真正所在的区域。
        // 这条把两者的边界钉死：谁在什么时候不该拿 AreEdgeAdjacent 当闸门。
        var key = RegionKey.FromDirection(Start(), Level);
        var frame = RegionFrame.Build(key);
        double localX = 0, localZ = 0;
        int diagonalFound = 0;

        for (int c = 0; c < 40 && diagonalFound == 0; c++)
        {
            for (int s = 0; s < 6; s++) localX += PatchCm;
            var globalCm = frame.GlobalFromLocalCm(new PlanetVector3(localX, 0, localZ));
            var found = RegionKey.FromDirection(globalCm.Normalized(), Level);
            if (found != key)
            {
                int dx = found.X - key.X, dy = found.Y - key.Y;
                if (dx != 0 && dy != 0)
                {
                    diagonalFound++;
                    Assert.False(PlanetRegions.AreEdgeAdjacent(key, found),
                                 "对角邻居却通过了共边校验：共边判据变宽了，服务端的 NotAdjacent 拒绝会跟着失效");
                    // 但反查必须自认：这个点确实落在 found 里
                    Assert.Equal(found, RegionKey.FromDirection(globalCm.Normalized(), Level));
                }
            }
            if (found != key)
            {
                var nextFrame = RegionFrame.Build(found);
                var nl = nextFrame.LocalFromGlobalCm(globalCm);
                frame = nextFrame; key = found; localX = nl.X; localZ = nl.Z;
            }
        }

        Assert.True(diagonalFound > 0, "40 次检查里没走到对角换区，这条对照失效了（区域宽/步长变了）");
    }
}
