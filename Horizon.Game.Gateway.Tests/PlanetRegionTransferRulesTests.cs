using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.Message.Network;
using Horizon.Planet;
using Horizon.Planet.Wire;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 客户端换区上行规则的判据（源码从 <c>HundunWorld/Source/Game/Planet/PlanetRegionTransferRules.cs</c>
/// 直接链进本项目编译，不是复制——复制的那份会静默漂移）。
/// <para>这条链上出过一次真事故（§13.15）：客户端把服务端"一次输入只能跨一格"的共边不变量
/// 当成自己的换区规则，沿局部东向走时合法地对角跨格，于是换到了玩家根本没去的块上。
/// 所以这里最重要的一条不是"能发出去"，而是<b>接口上没有"直接把 to 填进来"的入口</b>。</para>
/// </summary>
public class PlanetRegionTransferRulesTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = Neighbour(Home, RegionEdge.UPlus);
    private static readonly RegionKey FarFace = new(5, Level, (1 << Level) - 1, (1 << Level) - 1);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n) ? n : throw new InvalidOperationException("闭合面上的区域没有邻居");

    private static PlanetVector3 Surface(RegionKey key) => key.CenterDirection() * Radius;

    /// <summary>客户端算出来的那份口径（与 <c>PlanetRegionGrainAdapterTests</c> 里服务端那份同一形状）。
    /// 判据要它"看起来像真的"：口径全 0 的话，任何一条"填了口径就能发"的判据都在验一个假场景。</summary>
    private static readonly PlanetHandshake Caliber = new()
    {
        WireProtocol = PlanetHandshake.ProtocolVersion,
        PlanetRadiusCm = Radius,
        RegionLevel = (byte)Level,
        TerrainSeed = unchecked((long)0x123456789ABCDEF0UL),
        TerrainVersion = 0xA5A51234,
        BasefieldVersion = 1,
        BasefieldHashHi = unchecked((long)0xFEDCBA9876543210UL),
    };

    /// <summary>`in PlanetHandshake` 在反射里是 <c>PlanetHandshake&</c>，比类型前先解引用。</summary>
    private static Type Unwrap(Type type) => type.IsByRef ? type.GetElementType()! : type;

    private static bool TryBuild(ulong characterId, RegionKey current, PlanetVector3 point, long tick,
                                 out PlanetRegionTransferRules.Request request, out string why) =>
        PlanetRegionTransferRules.TryBuild(characterId, current, point, tick, in Caliber, out request, out why);

    [Fact]
    public void 目标区域由全局点反查_接口上没有照抄from的入口()
    {
        // 反射这条"形状"：TryBuild 的参数里不许有 toRegionId。
        // §13.15 那个缺陷不是算错了，是多接了一个调用方给的 to——入口没了才不会再犯。
        var parameters = typeof(PlanetRegionTransferRules).GetMethod(nameof(PlanetRegionTransferRules.TryBuild))!
            .GetParameters();
        foreach (var p in parameters)
            Assert.DoesNotContain("to", p.Name!.ToLowerInvariant(), StringComparison.Ordinal);

        Assert.True(TryBuild(7, Home, Surface(East), 100, out var req, out _));
        Assert.Equal(Home.ToId(), req.FromRegionId);
        Assert.Equal(East.ToId(), req.ToRegionId);

        // 对角跨格（局部东向在跨面处会合法地同时对角）：照实反查，不因为"不共边"就不发
        var diagonal = new RegionKey(Home.Face, Level, Home.X + 1, Home.Y + 1);
        Assert.True(TryBuild(7, Home, Surface(diagonal), 101, out var diagReq, out _));
        Assert.Equal(diagonal.ToId(), diagReq.ToRegionId);
    }

    [Fact]
    public void 没真的换区不发_换了就发()
    {
        Assert.False(TryBuild(7, Home, Surface(Home), 100, out var skipped, out string why));
        Assert.Equal(0, skipped.ToRegionId);
        Assert.Contains("还没真的走出当前区域", why);

        // 正向孪生：同一个点挪到邻区就发
        Assert.True(TryBuild(7, Home, Surface(East), 100, out _, out _));
    }

    [Fact]
    public void 角色号为0或越出int64不发()
    {
        Assert.False(TryBuild(0, Home, Surface(East), 100, out _, out string zeroWhy));
        Assert.Contains("角色号为 0", zeroWhy);

        Assert.False(TryBuild((ulong)long.MaxValue + 1, Home, Surface(East), 100, out _, out string overWhy));
        Assert.Contains("超出 int64", overWhy);

        // 正向孪生：上界那个号能发（越界只发生在它之上）
        Assert.True(TryBuild((ulong)long.MaxValue, Home, Surface(East), 100, out var ok, out _));
        Assert.Equal((ulong)long.MaxValue, ok.CharacterId);
    }

    [Fact]
    public void 退化或天外坐标不发_大气内照发()
    {
        foreach (var bad in new[]
        {
            PlanetVector3.Zero,
            new PlanetVector3(double.NaN, 1, 1),
            new PlanetVector3(double.PositiveInfinity, 0, 0),
            Surface(East) * 1.05,                       // 离海面约 3 200 km，超出迁移口径
        })
        {
            Assert.False(TryBuild(7, Home, bad, 100, out _, out string why));
            Assert.False(string.IsNullOrWhiteSpace(why));
        }

        // 正向孪生：离地 100 km 仍在口径内（飞行/高空是合法状态）
        var airborne = RegionFrame.Build(East, Radius).GlobalFromLocalCm(new PlanetVector3(0, 10_000_000, 0));
        Assert.True(TryBuild(7, Home, airborne, 100, out var req, out _));
        Assert.Equal(East.ToId(), req.ToRegionId);
    }

    [Fact]
    public void tick不单调不发()
    {
        Assert.False(TryBuild(7, Home, Surface(East), 0, out _, out string why));
        Assert.Contains("tick 必须为正", why);
        Assert.True(TryBuild(7, Home, Surface(East), 1, out _, out _));
    }

    [Fact]
    public void 回执处置逐条对号()
    {
        long homeId = Home.ToId(), eastId = East.ToId();

        // 没有回执 = 超时/丢包，一律 Ignore：它和"被拒"必须走不同的路
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Ignore,
            PlanetRegionTransferRules.Decide(hasReceipt: false, accepted: false, applied: false, 0, eastId, Home));

        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            PlanetRegionTransferRules.Decide(true, accepted: true, applied: true,
                (int)PlanetRegionRejectCode.None, eastId, Home));
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm,
            PlanetRegionTransferRules.Decide(true, accepted: true, applied: true,
                (int)PlanetRegionRejectCode.None, homeId, Home));            // 受理但回的是同一块
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm,
            PlanetRegionTransferRules.Decide(true, accepted: true, applied: false,
                (int)PlanetRegionRejectCode.None, homeId, Home));            // 合法的空操作
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.SameRegion, homeId, Home));
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.WaitClock,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.TickRegression, homeId, Home));

        foreach (var code in new[]
        {
            PlanetRegionRejectCode.UnknownEntity,
            PlanetRegionRejectCode.MalformedMessage,
            PlanetRegionRejectCode.SessionMismatch,
        })
            Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Rebind,
                PlanetRegionTransferRules.Decide(true, accepted: false, applied: false, (int)code, homeId, Home));

        // 几何类拒绝：服务端回带的是它认定的真实归属，我们本地的信念才是错的那一方
        foreach (var code in new[]
        {
            PlanetRegionRejectCode.NotAdjacent,
            PlanetRegionRejectCode.PointNotInTarget,
            PlanetRegionRejectCode.LevelMismatch,
            PlanetRegionRejectCode.TooFarFromSurface,
            PlanetRegionRejectCode.MalformedRegion,
            PlanetRegionRejectCode.MalformedPoint,
            PlanetRegionRejectCode.OwnerMismatch,
        })
            Assert.Equal(PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
                PlanetRegionTransferRules.Decide(true, accepted: false, applied: false, (int)code, eastId, Home));
    }

    [Fact]
    public void 归属为0永远不许当成目标区域()
    {
        // 0 是"服务端没有归属可报"的约定值（见 PlanetRegionTransferResult.NewRegionId 的注释）。
        // 把它当成一块真区域 = 客户端切到一个不存在的切平面上。
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.NotAdjacent, East.ToId(), Home));
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Rebind,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.NotAdjacent, 0, Home));
        // 受理但回带来 0 是不可信的协议违例：不许换，重新落位
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.Rebind,
            PlanetRegionTransferRules.Decide(true, accepted: true, applied: true,
                (int)PlanetRegionRejectCode.None, 0, Home));
    }

    [Fact]
    public void 面号在整条链上都是64位不截断()
    {
        // face 5 的 id 一定超过 int32：请求里 from/to 全程 long，任何一处收窄成 int 都会在这里红
        Assert.True(TryBuild(7, Home, Surface(FarFace), 100, out var req, out _),
            "跨到另一面最后一块的点应当算作换区");
        Assert.Equal(FarFace.ToId(), req.ToRegionId);
        Assert.True(req.ToRegionId > int.MaxValue);
        Assert.Equal(FarFace, RegionKey.FromId(req.ToRegionId));
    }

    [Fact]
    public void 不声明口径就构造不出请求_接口上没有静默绕过的口子()
    {
        // §6.2 要的是"不匹配就不许进世界"。如果 TryBuild 允许不带口径，那"没握手就换区"就成了默认路径。
        // 这里钉的是签名本身：必须有一个 PlanetHandshake 参数（漏掉的话这条判据直接红，不会静默通过）。
        var parameters = typeof(PlanetRegionTransferRules).GetMethod(nameof(PlanetRegionTransferRules.TryBuild))!
            .GetParameters();
        // `in PlanetHandshake` 反射回来的是 PlanetHandshake&（按引用），直接比 ParameterType 会漏判成"没有这个参数"
        var caliberParams = parameters.Where(p => Unwrap(p.ParameterType) == typeof(PlanetHandshake)).ToList();
        Assert.Single(caliberParams);
        Assert.True(caliberParams[0].ParameterType.IsByRef,
                    "口径按引用传：签名上就写着\"这里只读不写\"，避免规则里某处顺手改了调用方的口径");

        // 默认值 = 压根没算出口径（基场加载失败时 Caliber 就是 default）。不发，而且要说清是哪一项。
        var unfilled = default(PlanetHandshake);          // 基场没加载成功时客户端手里就是这份
        Assert.False(PlanetRegionTransferRules.TryBuild(7, Home, Surface(East), 100,
                       in unfilled, out _, out string why));
        Assert.Contains("wireProtocol=0", why, StringComparison.Ordinal);

        // 半径不对：客户端选了另一颗星球（build 与数据不匹配的形状）
        var otherPlanet = Caliber;
        otherPlanet.PlanetRadiusCm = PlanetConstants.EarthMeanRadiusCm;
        Assert.False(PlanetRegionTransferRules.TryBuild(7, Home, Surface(East), 100, in otherPlanet,
                       out _, out string radiusWhy));
        Assert.Contains("另一颗星球", radiusWhy, StringComparison.Ordinal);

        // 层级不对：口径写着 13 而这块区域是 14，服务端会把同一个点算成另一块区域
        var otherLevel = Caliber;
        otherLevel.RegionLevel = 13;
        Assert.False(PlanetRegionTransferRules.TryBuild(7, Home, Surface(East), 100, in otherLevel,
                       out _, out string levelWhy));
        Assert.Contains("区域层级", levelWhy, StringComparison.Ordinal);

        // 正向孪生：三条都修回原样就发得出去（上面不是因为"永远不发"而绿）
        Assert.True(PlanetRegionTransferRules.TryBuild(7, Home, Surface(East), 100, in Caliber, out _, out string ok), ok);
        Assert.Equal("", ok);
    }

    [Fact]
    public void 口径穿过35字节之后服务端判的还是同一件事()
    {
        // 这一条把客户端的填充与 <see cref="PlanetRegionGrain"/> 的解读串起来：
        // TryWrite（客户端）→ PlanetCaliber 字节 → TryRead + EvaluateRead（服务端 grain 里就是这两步）。
        // 中间任何一处把字段顺序或位宽写错，报出来的项就会变，而判定照样"跑得很顺"。
        Assert.True(TryBuild(7, Home, Surface(East), 100, out var req, out string built), built);
        var bytes = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(bytes, req.Caliber, out int written));
        Assert.Equal(PlanetHandshake.Size, written);

        Assert.True(PlanetHandshake.TryRead(bytes, out var decoded, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.Ok, status);
        Assert.Equal(PlanetHandshake.Size, consumed);
        Assert.Equal(req.Caliber.TerrainVersion, decoded.TerrainVersion);
        Assert.Equal(req.Caliber.TerrainSeed, decoded.TerrainSeed);
        Assert.Equal(req.Caliber.BasefieldHashHi, decoded.BasefieldHashHi);
        Assert.True(PlanetHandshakePolicy.Evaluate(Caliber, decoded).Accepted);

        // 每一项过一趟线之后都要还能被单独认出来：改一项 → 写 → 读 → 判，报的必须是那一项
        //
        // 用 Func 而不是 Action：PlanetHandshake 是结构体，Action 收到的是一份形参副本，
        // 改完就丢——于是"改了半径"那条其实没改任何东西，判据会因为 verdict.Accepted 而红，
        // 但更糟的是它会红在一个跟被验对象无关的断言上。这一课在本文件另一条判据上真踩过。
        var perField = new (string Label, Func<PlanetHandshake, PlanetHandshake> Mutate, PlanetHandshakeReject Expect)[]
        {
            ("半径", p => { p.PlanetRadiusCm += 1000; return p; }, PlanetHandshakeReject.PlanetRadius),
            ("层级", p => { p.RegionLevel = 12; return p; }, PlanetHandshakeReject.RegionLevel),
            ("种子", p => { p.TerrainSeed += 1; return p; }, PlanetHandshakeReject.TerrainSeed),
            ("地形版本", p => { p.TerrainVersion += 1; return p; }, PlanetHandshakeReject.TerrainVersion),
            ("基场版本", p => { p.BasefieldVersion += 1; return p; }, PlanetHandshakeReject.BasefieldVersion),
            ("基场哈希", p => { p.BasefieldHashHi += 1; return p; }, PlanetHandshakeReject.BasefieldHash),
        };
        foreach (var (label, mutate, expect) in perField)
        {
            var tampered = mutate(Caliber);
            Assert.NotEqual(Caliber, tampered);   // 这个改动必须真的生效，否则下面判的是同一份口径
            var wire = new byte[PlanetHandshake.Size];
            Assert.True(PlanetHandshake.TryWrite(wire, tampered, out _), label);
            Assert.True(PlanetHandshake.TryRead(wire, out var back, out _, out _), label);
            var verdict = PlanetHandshakePolicy.Evaluate(Caliber, back);
            Assert.False(verdict.Accepted, label);
            Assert.True(verdict.Field == expect, $"{label} 过一趟线之后报成了 {verdict.Field}");
        }

        // 空数组（旧客户端/没填）走的是"读不出来"那一支，而不是"字段比不过"：两者排查方向不同
        Assert.False(PlanetHandshake.TryRead(Array.Empty<byte>(), out var none, out var noneStatus, out _));
        var readVerdict = PlanetHandshakePolicy.EvaluateRead(noneStatus, false, Caliber, none);
        Assert.False(readVerdict.Accepted);
        Assert.Equal(PlanetHandshakeReject.WireRead, readVerdict.Field);
    }

    [Fact]
    public void 口径不符的处置是停手而不是重新落位()
    {
        // 走 default 会得到 Rebind；重新落位带的还是同一份口径，于是"落位→被拒→落位"循环到永远。
        // 这条判据钉的就是"不许循环"，而它只在看得到 code 的那一层才可能成立。
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.RefuseWorld,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.CaliberMismatch, 0, Home));
        // 即使服务端回带了一个区域号（将来别处复用这条码），也不许客户端换上去
        Assert.Equal(PlanetRegionTransferRules.ReceiptAction.RefuseWorld,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.CaliberMismatch, East.ToId(), Home));
        Assert.NotEqual(PlanetRegionTransferRules.ReceiptAction.Rebind,
            PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                (int)PlanetRegionRejectCode.CaliberMismatch, 0, Home));
    }

    [Fact]
    public void 每条拒绝码的处置都点过名_新增码不许落到default()
    {
        // Decide 的 default 分支是"几何类"处置。新增一条码如果没在这儿点名，它会静默走进 default，
        // 而 0 号 NewRegionId 会让它变成 Rebind —— 于是那条新码的真实语义永远不生效。
        // 这张表就是"每条码都要有人想过"的可执行版本。
        var expected = new Dictionary<PlanetRegionRejectCode, PlanetRegionTransferRules.ReceiptAction>
        {
            [PlanetRegionRejectCode.None] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.MalformedRegion] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.MalformedPoint] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.LevelMismatch] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.PointNotInTarget] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.SameRegion] = PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm,
            [PlanetRegionRejectCode.NotAdjacent] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.TooFarFromSurface] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.UnknownEntity] = PlanetRegionTransferRules.ReceiptAction.Rebind,
            [PlanetRegionRejectCode.OwnerMismatch] = PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            [PlanetRegionRejectCode.TickRegression] = PlanetRegionTransferRules.ReceiptAction.WaitClock,
            [PlanetRegionRejectCode.MalformedMessage] = PlanetRegionTransferRules.ReceiptAction.Rebind,
            [PlanetRegionRejectCode.SessionMismatch] = PlanetRegionTransferRules.ReceiptAction.Rebind,
            [PlanetRegionRejectCode.CaliberMismatch] = PlanetRegionTransferRules.ReceiptAction.RefuseWorld,
            // §6.1 的地面闸门：与口径不符不同类——那条本地修不了，这条本地能修（发送前会把点抬到地面），
            // 所以重新落位一次就收敛。给它 RefuseWorld 会让"站进土里"升级成"整颗星球不玩了"。
            [PlanetRegionRejectCode.Underground] = PlanetRegionTransferRules.ReceiptAction.Rebind,
        };

        var all = (PlanetRegionRejectCode[])Enum.GetValues(typeof(PlanetRegionRejectCode));
        Assert.Equal(all.Length, expected.Count);            // 枚举加值而这张表没跟上 ⇒ 这里红
        foreach (var code in all)
        {
            if (code == PlanetRegionRejectCode.None) continue;   // None 不是拒绝，走的是 accepted 那条路
            var actual = PlanetRegionTransferRules.Decide(true, accepted: false, applied: false,
                                                         (int)code, East.ToId(), Home);
            Assert.True(actual == expected[code],
                $"{code}：期望 {expected[code]}，实际 {actual}（新增的码是不是没在 Decide 里点名？）");
        }
    }
}
