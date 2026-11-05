using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 区域权威（§6.1 / §6.3 / §6.4 的服务端那一半）。
/// <para>这里刻意不引 Orleans：这块的正确性是"谁在哪个区域、什么时候交接、谁能看见谁"，
/// 与 grain 的宿主方式无关。§9 给 P1 的那条"双客户端跨区不丢实体"从来没有过判据，
/// 它其实是这里的性质，不需要跑起两个客户端才能验——需要的是把订阅集算对并钉住。</para>
/// </summary>
public class PlanetRegionAuthorityTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static PlanetVector3 CenterOf(RegionKey key) =>
        RegionFrame.Build(key, Radius).GlobalFromLocalCm(new PlanetVector3(0, 0, 0));

    private static RegionTransferRequest ReqIds(long fromId, long toId, PlanetVector3 global, long tick) =>
        new()
        {
            FromRegionId = fromId,
            ToRegionId = toId,
            GlobalCm = EcefCentimeters.FromVector(global),
            Tick = tick,
        };

    private static RegionTransferRequest Req(RegionKey from, RegionKey to, PlanetVector3 global, long tick) =>
        ReqIds(from.ToId(), to.ToId(), global, tick);

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = PlanetRegions.TryNeighbour(Home, RegionEdge.UPlus, out var e)
        ? e : throw new InvalidOperationException("Home 没有 UPlus 邻居");

    [Fact]
    public void 落位按全局点反查区域()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        var p = CenterOf(Home);
        Assert.True(a.TryBind(1, p, tick: 10, out var region));
        Assert.Equal(Home, region);
        Assert.Equal(Home, a.OwnerOf(1));
        Assert.Equal(10, a.Clock);

        // 报什么区域不算数：换一个点就换一个归属，且必须落在离地 20000 km 以内
        Assert.False(a.TryBind(2, p * 3.0, tick: 11, out _));
        Assert.Null(a.OwnerOf(2));
        // 落位不许把时钟拨回去
        Assert.False(a.TryBind(3, CenterOf(East), tick: 5, out _));
    }

    [Fact]
    public void 合法共边迁移改归属并留下重叠窗口()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 0, out _));

        var result = a.RequestTransfer(1, Req(Home, East, CenterOf(East), 1), tick: 1);
        Assert.True(result.Decision.Accepted, $"几何判定被拒：{result.Decision.Reason}");
        Assert.True(result.Applied);
        Assert.Equal(AuthorityReject.None, result.Reject);
        Assert.Equal(East, result.NewRegion);
        Assert.Equal(1, result.HandoverTick);
        Assert.Equal(East, result.Frame.Key);                 // 客户端要切的就是这套基
        Assert.Equal(East, a.OwnerOf(1));

        // §6.3 第 5 步：旧区在窗口内继续可见
        Assert.Contains(Home, a.Subscriptions(1));
        Assert.Equal(Home, a.OverlapOf(1));

        // 这里差点写出一条循环论证：合法迁移的目标必然与旧区共边（§13.13），
        // 所以旧区本来就靠"邻区"这条来源在订阅集里——用可见集去证明重叠窗口有用是套套逻辑。
        // 窗口真正管的是时序（什么时候允许摘旧区的地形/预加载），所以判它自己的账：到点就没了。
        a.AdvanceTo(121);
        Assert.Null(a.OverlapOf(1));
        Assert.Contains(Home, a.Subscriptions(1));            // 仍在，但来源是共边邻居，不是重叠
    }

    [Fact]
    public void 被拒的迁移一点状态都不改()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 100, out _));
        var beforeOwner = a.OwnerOf(1);
        var beforeSubs = a.Subscriptions(1).ToList();
        var beforeClock = a.Clock;

        var far = new RegionKey(Home.Face, Level, Home.X + 7, Home.Y + 7);   // 不相邻
        var badRegionId = 7L << 58;                                            // face 位填 7：解得开字段但 RegionKey 构造当场拒
        var malformedPoint = new PlanetVector3(0, 0, 0);

        var cases = new List<(string name, RegionTransferRequest req, TransferRejectReason expect)>
        {
            ("不相邻", Req(Home, far, CenterOf(far), 110), TransferRejectReason.NotAdjacent),
            ("报同一个区", Req(Home, Home, CenterOf(Home), 110), TransferRejectReason.SameRegion),
            ("点不在目标区里", Req(Home, East, CenterOf(Home), 110), TransferRejectReason.PointNotInTarget),
            ("层级不同", Req(Home, new RegionKey(Home.Face, Level + 1, Home.X * 2, Home.Y * 2),
                             CenterOf(East), 110), TransferRejectReason.LevelMismatch),
            ("坏 regionId", ReqIds(Home.ToId(), badRegionId, CenterOf(East), 110), TransferRejectReason.MalformedRegion),
            ("坏点", Req(Home, East, malformedPoint, 110), TransferRejectReason.MalformedPoint),
            ("离面太远", Req(Home, East, CenterOf(East) * 1.5, 110), TransferRejectReason.TooFarFromSurface),
        };

        foreach (var (name, req, expect) in cases)
        {
            var r = a.RequestTransfer(1, req, tick: 110);
            Assert.False(r.Applied, $"{name}：被拒了还说 Applied=true");
            Assert.Equal(expect, r.Decision.Reason);
            Assert.Equal(beforeOwner, a.OwnerOf(1));                       // 归属没动
            Assert.Equal(beforeSubs, a.Subscriptions(1).ToList());         // 订阅集没动
            Assert.Equal(beforeClock, a.Clock);                            // 时钟没被拨动
            Assert.Equal(0, a.Applied);
        }
        Assert.Equal(cases.Count, a.RejectedGeometry);
    }

    [Fact]
    public void 权限层三条各有正向对照()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 10);

        // ① 实体不存在
        var r1 = a.RequestTransfer(999, Req(Home, East, CenterOf(East), 5), tick: 5);
        Assert.False(r1.Applied);
        Assert.Equal(AuthorityReject.UnknownEntity, r1.Reject);

        // ② 报了个不是自己归属的 from —— 同一条请求对已落位的实体就是合法的（正向对照）
        Assert.True(a.TryBind(1, CenterOf(East), tick: 6, out _));        // 故意落在 East
        var r2 = a.RequestTransfer(1, Req(Home, East, CenterOf(East), 7), tick: 7);
        Assert.False(r2.Applied);
        Assert.Equal(AuthorityReject.OwnerMismatch, r2.Reject);
        Assert.Equal(East, a.OwnerOf(1));                                 // 状态没动

        // ③ tick 倒退
        var r3 = a.RequestTransfer(1, Req(East, Home, CenterOf(Home), 3), tick: 3);
        Assert.False(r3.Applied);
        Assert.Equal(AuthorityReject.TickRegression, r3.Reject);

        // 正向对照：把归属摆正、tick 前进，同一条路径必须能过
        var r4 = a.RequestTransfer(1, Req(East, Home, CenterOf(Home), 8), tick: 8);
        Assert.True(r4.Applied, r4.ToString());
        Assert.Equal(Home, a.OwnerOf(1));
        Assert.Equal(3, a.RejectedAuthority);
    }

    [Fact]
    public void 双客户端跨区不丢实体()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 0, out _));        // 玩家 A
        Assert.True(a.TryBind(2, CenterOf(Home), tick: 0, out _));        // 玩家 B 留在原区

        Assert.Contains(2L, a.VisibleEntities(1));
        Assert.Contains(1L, a.VisibleEntities(2));

        // A 跨到共边邻区
        var r = a.RequestTransfer(1, Req(Home, East, CenterOf(East), 1), tick: 1);
        Assert.True(r.Applied);

        // 交接当帧：两边都还在对方的订阅集里（重叠 + 邻区两条来源都成立）
        Assert.True(a.VisibleEntities(1).Contains(2L), "A 跨区后把 B 丢了");
        Assert.True(a.VisibleEntities(2).Contains(1L), "B 把跨区的 A 丢了");

        // 窗口结束后仍然互相可见——因为共边邻区本来就在订阅集里（§6.4）
        a.AdvanceTo(200);
        Assert.Contains(2L, a.VisibleEntities(1));
        Assert.Contains(1L, a.VisibleEntities(2));

        // 反证（正向对照的另一半）：B 传送到不相邻的远处，A 就再也看不见它。
        // 少了这一条，上面所有"看得见"都可能是"订阅集把整个星球都算进来了"。
        var far = new RegionKey((Home.Face + 1) % 6, Level, 100, 100);
        Assert.True(a.TryTeleport(2, CenterOf(far), tick: 210, out var teleported));
        Assert.Equal(far, teleported);
        Assert.DoesNotContain(2L, a.VisibleEntities(1));
        Assert.DoesNotContain(1L, a.VisibleEntities(2));
    }

    [Fact]
    public void 传送不等共边也不留重叠窗口()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 0, out _));
        var antipode = new RegionKey(Home.Face, Level, (1 << Level) - 1 - Home.X,
                                    (1 << Level) - 1 - Home.Y);

        // 共边校验会直接拒这种请求——所以传送必须走自己的入口
        var viaTransfer = a.RequestTransfer(1, Req(Home, antipode, CenterOf(antipode), 1), tick: 1);
        Assert.False(viaTransfer.Applied);
        Assert.Equal(TransferRejectReason.NotAdjacent, viaTransfer.Decision.Reason);

        Assert.True(a.TryTeleport(1, CenterOf(antipode), tick: 2, out var region));
        Assert.Equal(antipode, region);
        Assert.Equal(2, a.Clock);
        // §6.4：传送不做长距离插值，也不留旧区重叠
        Assert.DoesNotContain(Home, a.Subscriptions(1));
    }

    [Fact]
    public void AOI中间层是100m格且负坐标向下取整()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 0);
        var frame = RegionFrame.Build(Home, Radius);
        Assert.True(a.TryBind(1, frame.GlobalFromLocalCm(new PlanetVector3(0, 0, 0)), tick: 0, out _));
        var c0 = a.CellOf(1)!.Value;
        Assert.Equal(0, c0.Cx);
        Assert.Equal(Home, c0.Region);

        // 差一个格边长正好进下一格；差一点点还在同一格
        a.TryBind(2, frame.GlobalFromLocalCm(new PlanetVector3(PlanetRegionAuthority.CellSizeCm, 0, 0)), 1, out _);
        Assert.Equal(1, a.CellOf(2)!.Value.Cx);
        a.TryBind(3, frame.GlobalFromLocalCm(new PlanetVector3(PlanetRegionAuthority.CellSizeCm - 1, 0, 0)), 1, out _);
        Assert.Equal(0, a.CellOf(3)!.Value.Cx);

        // 负坐标必须向下取整（截断会把 -1 格算成 0 格，边界两侧就会串格）
        a.TryBind(4, frame.GlobalFromLocalCm(new PlanetVector3(-1, 0, -PlanetRegionAuthority.CellSizeCm)), 1, out _);
        var cn = a.CellOf(4)!.Value;
        Assert.Equal(-1, cn.Cx);
        Assert.Equal(-1, cn.Cz);
    }

    [Fact]
    public void 时钟不许倒退且账要对得上()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 5);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 50, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => a.AdvanceTo(49));

        a.RequestTransfer(1, Req(Home, East, CenterOf(East), 60), tick: 60);      // 1 次 applied
        a.RequestTransfer(2, Req(Home, East, CenterOf(East), 61), tick: 61);      // 1 次 权限拒
        a.RequestTransfer(1, Req(Home, East, CenterOf(East), 62), tick: 62);      // 归属已变 → OwnerMismatch
        Assert.Equal(1, a.Applied);
        Assert.Equal(2, a.RejectedAuthority);
        Assert.Equal(0, a.RejectedGeometry);
        var stats = a.Describe();
        Assert.Contains("applied=1", stats);
        Assert.Contains("rejected_authority=2", stats);
        // 被拒的请求不许推进服务端时钟：否则客户端狂发坏包就能拨动权威时间。
        // 时钟只在 bind / teleport / 已应用的迁移上前进，所以这里停在第一次应用的 60。
        Assert.Contains("clock=60", stats);
    }

    [Fact]
    public void 构造参数不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetRegionAuthority(0, Level, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetRegionAuthority(Radius, -1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetRegionAuthority(Radius, Level, -1));
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 0);
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 0, out _));
        // overlapTicks == 0 ⇒ 不留窗口：迁移后旧区只能靠"邻区"这一条来源被订阅到
        var r = a.RequestTransfer(1, Req(Home, East, CenterOf(East), 1), tick: 1);
        Assert.True(r.Applied);
        Assert.Contains(Home, a.Subscriptions(1));       // 仍然订阅，但来源是共边邻居不是重叠
    }

    [Fact]
    public void 落盘投影逐条等于查询接口_且拿到的是副本()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        a.TryBind(1, CenterOf(Home), tick: 10, out _);
        a.TryBind(2, CenterOf(East), tick: 11, out _);
        // 一次真实迁移：让实体 2 的区域、坐标、tick 三个字段都换过值，
        // 免得投影只测到"落位当场写的那一份"，把迁移路径上的漏拷贝留在盲区里。
        Assert.True(PlanetRegions.TryNeighbour(East, RegionEdge.VPlus, out var northEast));
        var transferred = a.RequestTransfer(2, Req(East, northEast, CenterOf(northEast), tick: 12), tick: 12);
        Assert.True(transferred.Applied);

        var snapshot = a.SnapshotEntities();
        Assert.Equal(a.Count, snapshot.Count);
        foreach (var e in snapshot)
        {
            Assert.Equal(a.OwnerOf(e.EntityId), e.Region);
            Assert.Equal(a.GlobalOf(e.EntityId)!.Value, e.GlobalCm);
        }
        var byId = snapshot.ToDictionary(e => e.EntityId);
        Assert.Equal(Home, byId[1].Region);
        Assert.Equal(northEast, byId[2].Region);
        // tick 是每条记录自己的，不是时钟：恢复要按它升序回放，全被时钟覆盖成同一个值就废了
        Assert.Equal(10, byId[1].Tick);
        Assert.Equal(12, byId[2].Tick);

        // 副本语义：清空投影列表不该动到权威表——否则"写出去的和内存里的一样"这条判据没着力点
        snapshot.Clear();
        Assert.Equal(2, a.Count);
        Assert.Equal(2, a.SnapshotEntities().Count);
        Assert.NotNull(a.OwnerOf(1));
    }

    [Fact]
    public void 退化坐标返回false而不是把异常冒到服务端()
    {
        var a = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        // 反查区域要先看方向，零方向/NaN 在 CubeSphere.ToFaceUv 里会抛 InvalidOperationException。
        // 落位与传送都是服务端边界，坏坐标应当是"拒"，不该是"炸"。
        foreach (var bad in new[]
        {
            PlanetVector3.Zero,
            new PlanetVector3(double.NaN, 0, 0),
            new PlanetVector3(double.PositiveInfinity, 0, 0),
            CenterOf(Home) * 1e6,                              // 天外：有限但离海面太远
        })
        {
            Assert.False(a.TryBind(1, bad, tick: 10, out _));
            Assert.Null(a.OwnerOf(1));
        }
        Assert.Equal(0, a.Count);
        Assert.Equal(0, a.Clock);                              // 被拒的落位不许拨动时钟

        // 正向孪生：可用的点照常落位；大气里飞（离地 100 km）也在允许口径里
        Assert.True(a.TryBind(1, CenterOf(Home), tick: 10, out _));
        var airborne = RegionFrame.Build(Home, Radius).GlobalFromLocalCm(new PlanetVector3(0, 10_000_000, 0));
        Assert.True(a.TryBind(2, airborne, tick: 11, out _));
        Assert.Equal(2, a.Count);

        Assert.False(a.TryTeleport(1, PlanetVector3.Zero, tick: 12, out _));
        Assert.Equal(Home, a.OwnerOf(1));                      // 传送被拒也不许动归属
    }
}
