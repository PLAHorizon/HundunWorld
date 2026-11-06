using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using MemoryPack;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// §6.2 那句"terrainSeed/terrainVersion 不匹配直接拒绝进入世界"在<em>服务端活路径</em>上的判据。
/// <para>这批判据存在的理由是一条具体的历史：库里有 <see cref="PlanetHandshake"/> 的编解码，
/// 也早有 <c>DescribeMismatch</c>，但没有任何服务端代码调用它们——于是"不匹配拒绝进入"这件事
/// 在客户端缓存那一侧有计数，在服务端这一侧一直是空的。<b>判据要钉住的是"这条判定真的挂在活路径上"，
/// 而不是"库里写得对"</b>：一个正确但没人调用的判定，和没有判定是同一件事。</para>
/// <para>口径走的是 <see cref="PlanetRegionTransferRequest.PlanetCaliber"/> 那 35 字节，
/// 两端共用 <see cref="PlanetHandshake"/> 的编解码：没有第二份字段搬运，也就没有"各错一项"的余地。</para>
/// </summary>
public class PlanetRegionCaliberGateTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = Neighbour(Home, RegionEdge.UPlus);
    private static readonly RegionKey North = Neighbour(Home, RegionEdge.VMinus);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n)
            ? n
            : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");

    private static PlanetVector3 Surface(RegionKey key) => key.CenterDirection() * Radius;

    private static PlanetHandshake ServerCaliber() => new()
    {
        WireProtocol = PlanetHandshake.ProtocolVersion,
        PlanetRadiusCm = Radius,
        RegionLevel = (byte)Level,
        TerrainSeed = unchecked((long)0x123456789ABCDEF0UL),
        TerrainVersion = 0xA5A51234,
        BasefieldVersion = 1,
        BasefieldHashHi = unchecked((long)0xFEDCBA9876543210UL),
    };

    private static PlanetRegionTransferRequest Req(ulong character, RegionKey from, RegionKey to,
                                                   PlanetVector3 point, long tick)
    {
        var ecef = EcefCentimeters.FromVector(point);
        return new PlanetRegionTransferRequest
        {
            CharacterId = character,
            FromRegionId = from.ToId(),
            ToRegionId = to.ToId(),
            GlobalXcm = ecef.X,
            GlobalYcm = ecef.Y,
            GlobalZcm = ecef.Z,
            Tick = tick,
        };
    }

    /// <summary>落位/传送用的口径字节（与 <see cref="WithCaliber"/> 同一份编码）。</summary>
    private static byte[] CaliberBytes(PlanetHandshake caliber)
    {
        var bytes = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(bytes, caliber, out int written));
        Assert.Equal(PlanetHandshake.Size, written);
        return bytes;
    }

    /// <summary>按 §6.2 那 35 字节把口径装进请求——和客户端 <c>PlanetRegionTransfers</c> 做的是同一步。</summary>
    private static PlanetRegionTransferRequest WithCaliber(PlanetRegionTransferRequest request, PlanetHandshake caliber)
    {
        var bytes = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(bytes, caliber, out int written));
        Assert.Equal(PlanetHandshake.Size, written);
        request.PlanetCaliber = bytes;
        return request;
    }

    private sealed class StubCaliber : IPlanetCaliberSource
    {
        private readonly PlanetHandshake? _caliber;
        public StubCaliber(PlanetHandshake? caliber) { _caliber = caliber; }
        public int Calls { get; private set; }
        public bool TryGetCaliber(out PlanetHandshake caliber)
        {
            Calls++;
            caliber = _caliber ?? default;
            return _caliber is not null;
        }
    }

    /// <summary>带口径替身的裸激活 grain；不复用 <c>PlanetRegionGrainAdapterTests</c> 的夹具是为了
    /// 不把两组判据的存活期绑在一起——它们各自都能红。</summary>
    private sealed class Rig
    {
        public Rig(IPlanetCaliberSource? caliber)
        {
            var state = new PlanetRegionPersistedState();
            var mock = new Moq.Mock<global::Orleans.Runtime.IPersistentState<PlanetRegionPersistedState>>();
            mock.SetupGet(s => s.State).Returns(state);
            mock.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask).Callback(() => Writes++);
            Grain = new PlanetRegionGrain(
                new Moq.Mock<Microsoft.Extensions.Logging.ILogger<PlanetRegionGrain>>().Object, mock.Object, caliber);
            GrainTestHarness.AttachGrainContext(Grain, "PlanetRegion", "1");
        }
        public PlanetRegionGrain Grain { get; }
        public int Writes { get; private set; }
        public PlanetRegionAuthority Authority => Grain.AuthorityForChecks;
    }

    private static Rig NewRig(IPlanetCaliberSource? caliber) => new(caliber);

    [Fact]
    public async Task 服务端声明口径而客户端没带_迁移被拒且理由指向解不出来()
    {
        var h = NewRig(new StubCaliber(ServerCaliber()));
        var ecef = EcefCentimeters.FromVector(Surface(Home));
        Assert.True(await h.Grain.BindAsync(7, ecef.X, ecef.Y, ecef.Z, 100, CaliberBytes(ServerCaliber())));

        // 不带口径 = 空数组，TryRead 报 Truncated。它和"带了但比不过"必须分得开：
        // 前者是"这个客户端压根没握手"，后者是"它握的是另一颗星球"
        var receipt = await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 101));
        Assert.False(receipt.Accepted);
        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, receipt.Code);
        Assert.Contains("握手包解不开", receipt.Reason, StringComparison.Ordinal);
        Assert.Contains("Truncated", receipt.Reason, StringComparison.Ordinal);
        Assert.False(receipt.Applied);
    }

    [Fact]
    public async Task 口径逐字节对上才放行_同一份数据不该被自己拒()
    {
        var h = NewRig(new StubCaliber(ServerCaliber()));
        var ecef = EcefCentimeters.FromVector(Surface(Home));
        Assert.True(await h.Grain.BindAsync(7, ecef.X, ecef.Y, ecef.Z, 100, CaliberBytes(ServerCaliber())));

        var receipt = await h.Grain.TransferAsync(WithCaliber(Req(7, Home, East, Surface(East), 101), ServerCaliber()));
        Assert.True(receipt.Accepted, receipt.Reason);
        Assert.True(receipt.Applied);
        Assert.Equal(PlanetRegionRejectCode.None, receipt.Code);
        Assert.Equal(East.ToId(), receipt.NewRegionId);
    }

    [Fact]
    public async Task 口径七项各差一项都被拒_理由互不相同且带两边的值()
    {
        // 用 Func 而不是 Action：结构体改形参副本会静默丢失，那条判据就变成"什么都没改还期待被拒"
        //
        // 每一项都期待<em>具体的字段名</em>，而不是笼统的"理由非空"：七项合并成一句话就没法分别处置
        // （半径错是 build 选错了星球，基场哈希错是数据没同步）。
        // 线协议那一项形状不同：PlanetHandshake.TryRead 会先标 UnsupportedProtocol，
        // 于是走的是 EvaluateRead 的"读不出来"那一支，而不是逐项比较——这个分支差异也要被钉住。
        var mutators = new (string Label, Func<PlanetHandshake, PlanetHandshake> Mutate, string Expect)[]
        {
            ("线协议", p => { p.WireProtocol = 9; return p; }, "UnsupportedProtocol"),
            ("半径", p => { p.PlanetRadiusCm = PlanetConstants.EarthMeanRadiusCm; return p; }, "radius"),
            ("层级", p => { p.RegionLevel = 13; return p; }, "regionLevel"),
            ("种子", p => { p.TerrainSeed = 1; return p; }, "terrainSeed"),
            ("地形版本", p => { p.TerrainVersion = 2; return p; }, "terrainVersion"),
            ("基场版本", p => { p.BasefieldVersion = 7; return p; }, "basefieldVersion"),
            ("基场哈希", p => { p.BasefieldHashHi = 0; return p; }, "basefieldHash"),
        };

        var reasons = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, mutate, expect) in mutators)
        {
            var caliber = ServerCaliber();
            var h = NewRig(new StubCaliber(caliber));
            var home = EcefCentimeters.FromVector(Surface(Home));
            Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));

            var other = mutate(caliber);
            Assert.NotEqual(caliber, other);                       // 改动没生效的话这一轮判的就是空气

            var receipt = await h.Grain.TransferAsync(WithCaliber(Req(7, Home, East, Surface(East), 101), other));
            Assert.False(receipt.Accepted, label);
            Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, receipt.Code);
            Assert.Contains(expect, receipt.Reason, StringComparison.Ordinal);
            if (label != "线协议")
                Assert.Contains("!=", receipt.Reason, StringComparison.Ordinal);   // 值不在理由里就只能靠猜
            Assert.True(reasons.Add(receipt.Reason), $"{label} 的理由与其它项撞车：{receipt.Reason}");

            // 被拒的迁移不许动归属：口径不符说的是"不在同一个世界"，不是"你在别的地方"
            Assert.Equal(0, receipt.NewRegionId);
            Assert.Equal(Home.ToId(), h.Authority.OwnerOf(7)!.Value.ToId());
        }
        Assert.Equal(mutators.Length, reasons.Count);
    }

    [Fact]
    public async Task 口径判定排在几何之前_坏区域号也先报口径()
    {
        // 排序有意义：不是同一个世界，"这个点属于哪块区域"在两边算出的是不同的东西。
        // 用一条本身也会触发 MalformedRegion 的请求来验——报出来的必须是口径而不是几何。
        var h = NewRig(new StubCaliber(ServerCaliber()));
        var home = EcefCentimeters.FromVector(Surface(Home));
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));

        var badFrom = Req(7, Home, East, Surface(East), 101);
        badFrom.FromRegionId = -1;                                  // 位打包约定 id 非负：解不开就是坏包
        var caliberless = await h.Grain.TransferAsync(badFrom);
        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, caliberless.Code);
        Assert.Contains("Truncated", caliberless.Reason, StringComparison.Ordinal);

        // 正向孪生：口径补上之后同一条请求报的才是几何（证明不是"永远报口径"）
        var geometry = await h.Grain.TransferAsync(WithCaliber(badFrom, ServerCaliber()));
        Assert.Equal(PlanetRegionRejectCode.MalformedRegion, geometry.Code);
    }

    [Fact]
    public async Task 服务端没声明口径时闸门不生效_而不是把所有人拒光()
    {
        // 没挂基场数据的部署（本地跑服务端、灰度还没接星球）也走这段判定。
        // 把它判成"全员口径不符"会让整颗星球起不来，所以缺席=不判，与网关的会话绑定校验同口径。
        var h = NewRig(null);
        var home = EcefCentimeters.FromVector(Surface(Home));
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));
        Assert.True((await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 101))).Accepted);

        // 给了一个"取不出口径"的来源也一样不判（目录没配 / 读不出来都归这一类）
        var none = NewRig(new StubCaliber(null));
        Assert.True(await none.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));
        var receipt = await none.Grain.TransferAsync(Req(7, Home, East, Surface(East), 101));
        Assert.True(receipt.Accepted, receipt.Reason);
    }

    [Fact]
    public async Task 口径不符不动时钟不落盘也不凭空造归属()
    {
        var source = new StubCaliber(ServerCaliber());
        var h = NewRig(source);
        long clockBefore = h.Authority.Clock;

        // 实体 8 在服务端根本没有归属：口径不符时先报口径，走不到 UnknownEntity 那一支
        var mismatch = ServerCaliber();
        mismatch.TerrainVersion ^= 3u;
        var receipt = await h.Grain.TransferAsync(WithCaliber(Req(8, Home, East, Surface(East), 500), mismatch));

        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, receipt.Code);
        Assert.Equal(0, receipt.NewRegionId);
        Assert.Equal(clockBefore, h.Authority.Clock);               // 被拒的请求不该推进服务端时钟
        Assert.Equal(0, h.Writes);                                  // 一条落盘都没有
        Assert.Null(h.Authority.OwnerOf(8));                        // 也没凭空造出归属
        Assert.Equal(1, source.Calls);                              // 判定确实问了口径来源
    }

    [Fact]
    public void 闸门关着时是一个读得出来的状态_而不是一个看不见的差别()
    {
        // 漏注册/没配目录都不会报错，只会让 §6.2 那条判定从此不生效。
        // 判据要钉的是：三种"关"的形状互不相同，而且都能从 grain 里读出来。
        Assert.Contains("未注册", new Rig(null).Grain.CaliberGateStatus, StringComparison.Ordinal);

        string missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "hundun_no_such_dem_" + Guid.NewGuid().ToString("N"));
        var broken = new Rig(new DemPlanetCaliberSource(missing));
        Assert.Contains("口径取不出来", broken.Grain.CaliberGateStatus, StringComparison.Ordinal);
        Assert.Contains(missing, broken.Grain.CaliberGateStatus, StringComparison.Ordinal);   // 要说清是哪个目录

        var unconfigured = new Rig(new DemPlanetCaliberSource(null));
        Assert.Contains("未配置基场目录", unconfigured.Grain.CaliberGateStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("on(", unconfigured.Grain.CaliberGateStatus, StringComparison.Ordinal);

        var on = new Rig(new StubCaliber(ServerCaliber()));
        Assert.StartsWith("on(", on.Grain.CaliberGateStatus, StringComparison.Ordinal);
        Assert.Contains(ServerCaliber().TerrainVersion.ToString("x8"),
            on.Grain.CaliberGateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void 落位请求也把口径装在同样两条通路上()
    {
        // 落位是"进世界"那一步：它带的口径必须和迁移请求同一份编码、同样两条通路都登记，
        // 否则客户端落位这条上行会在服务端解成"没握手"，而客户端只会看到"落位被拒"。
        var property = typeof(PlanetRegionBindRequest).GetProperty("PlanetCaliber")!;
        Assert.NotNull(property.GetCustomAttribute<MemoryPackOrderAttribute>());
        Assert.NotNull(property.GetCustomAttribute<global::Orleans.IdAttribute>());

        var request = new PlanetRegionBindRequest
        {
            CharacterId = 9,
            GlobalXcm = 1, GlobalYcm = 2, GlobalZcm = 3,
            Tick = 77,
            PlanetCaliber = CaliberBytes(ServerCaliber()),
        };
        var back = MemoryPackSerializer.Deserialize<PlanetRegionBindRequest>(
            MemoryPackSerializer.Serialize(request))!;
        Assert.Equal(PlanetHandshake.Size, back.PlanetCaliber.Length);
        Assert.True(PlanetHandshake.TryRead(back.PlanetCaliber, out var decoded, out _, out _));
        Assert.True(PlanetHandshakePolicy.Evaluate(ServerCaliber(), decoded).Accepted);

        // 空数组表示"没声明"：必须解不出来，不能与任何服务端口径"相等"
        Assert.False(PlanetHandshake.TryRead(Array.Empty<byte>(), out _, out var status, out _));
        Assert.Equal(PlanetHandshakeReject.WireRead, PlanetHandshakePolicy.EvaluateRead(status, false, ServerCaliber(), default).Field);
    }

    [Fact]
    public void 口径字节在两条序列化通路上都有登记()
    {
        // MemoryPack 走 socket，Orleans [Id] 走 grain 调用。漏掉任何一边，另一条通路会静默
        // 变成"客户端没握手"——那比报错难查，因为它看起来只是一次普通的拒绝。
        var property = typeof(PlanetRegionTransferRequest).GetProperty("PlanetCaliber")!;

        var orders = typeof(PlanetRegionTransferRequest)
            .GetProperties().Select(p => p.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order)
            .Where(o => o is not null).ToList();
        Assert.NotNull(property.GetCustomAttribute<MemoryPackOrderAttribute>());
        Assert.Equal(orders.Count, orders.Distinct().Count());      // 两个字段抢同一个 order 就是丢数据

        var ids = typeof(PlanetRegionTransferRequest)
            .GetProperties().Select(p => p.GetCustomAttribute<global::Orleans.IdAttribute>()?.Id)
            .Where(v => v is not null).ToList();
        Assert.NotNull(property.GetCustomAttribute<global::Orleans.IdAttribute>());
        Assert.Equal(ids.Count, ids.Distinct().Count());

        // 跨一趟 MemoryPack：字节必须原样回来，判定结果也就必须一样
        var request = WithCaliber(Req(7, Home, East, Surface(East), 101), ServerCaliber());
        var back = MemoryPackSerializer.Deserialize<PlanetRegionTransferRequest>(
            MemoryPackSerializer.Serialize(request))!;
        Assert.Equal(PlanetHandshake.Size, back.PlanetCaliber.Length);
        Assert.True(PlanetHandshake.TryRead(back.PlanetCaliber, out var decoded, out _, out _));
        Assert.True(PlanetHandshakePolicy.Evaluate(ServerCaliber(), decoded).Accepted);

        // 反证：截掉一字节就得判成"解不出来"，而不是"口径一致"
        var truncated = new byte[PlanetHandshake.Size - 1];
        Array.Copy(request.PlanetCaliber, truncated, truncated.Length);
        Assert.False(PlanetHandshake.TryRead(truncated, out _, out var status, out _));
        var verdict = PlanetHandshakePolicy.EvaluateRead(status, false, ServerCaliber(), default);
        Assert.Equal(PlanetHandshakeReject.WireRead, verdict.Field);
    }

    [Fact]
    public async Task 落位口径不符就不建归属_带得上才进得了世界()
    {
        var home = EcefCentimeters.FromVector(Surface(Home));

        // 不匹配：落位失败，而且权威表里不该留下这个实体——"进不去"和"进去了但被拒"必须不一样
        var mismatched = ServerCaliber();
        mismatched.BasefieldHashHi ^= 5L;
        var h = NewRig(new StubCaliber(ServerCaliber()));
        Assert.False(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(mismatched)));
        Assert.Null(h.Authority.OwnerOf(7));
        Assert.Equal(0, h.Writes);

        // 没声明（空字节）：同样是拒，走的是"解不出来"那一支
        Assert.False(await h.Grain.BindAsync(8, home.X, home.Y, home.Z, 100, Array.Empty<byte>()));
        Assert.Null(h.Authority.OwnerOf(8));

        // 正向孪生：同一份口径就能落位
        Assert.True(await h.Grain.BindAsync(9, home.X, home.Y, home.Z, 101, CaliberBytes(ServerCaliber())));
        Assert.Equal(Home.ToId(), h.Authority.OwnerOf(9)!.Value.ToId());

        // 服务端没声明口径时照旧放行（不判），别把没挂基场的部署关在门外
        var unconfigured = NewRig(null);
        Assert.True(await unconfigured.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, Array.Empty<byte>()));
        var sourceCannotRead = NewRig(new DemPlanetCaliberSource(null));
        Assert.True(await sourceCannotRead.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, Array.Empty<byte>()));
    }

    [Fact]
    public async Task 传送也先看口径_不匹配时归属一动不动()
    {
        var home = EcefCentimeters.FromVector(Surface(Home));
        var north = EcefCentimeters.FromVector(Surface(North));
        var h = NewRig(new StubCaliber(ServerCaliber()));
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));

        var wrongPlanet = ServerCaliber();
        wrongPlanet.PlanetRadiusCm = PlanetConstants.EarthMeanRadiusCm;
        Assert.False(await h.Grain.TeleportAsync(7, north.X, north.Y, north.Z, 120, CaliberBytes(wrongPlanet)));
        Assert.Equal(Home.ToId(), h.Authority.OwnerOf(7)!.Value.ToId());      // 还在这儿，没被挪走

        // 正向孪生：口径对上之后同一次传送生效
        Assert.True(await h.Grain.TeleportAsync(7, north.X, north.Y, north.Z, 120, CaliberBytes(ServerCaliber())));
        Assert.Equal(North.ToId(), h.Authority.OwnerOf(7)!.Value.ToId());
    }

    [Fact]
    public async Task 三个入口共用同一条判定_线上说法与库里说法逐字相同()
    {
        // 落位、传送、换区各写一遍比较逻辑，就是"同一个握手两套真相"的开工方式。
        // 这里钉两件事：① 三个入口对同一份错误口径都给同一个结论；
        // ② 线上理由里那句字段级描述，与库里 DescribeMismatch 的说法<em>逐字相同</em>——
        //    换一个说法就是第二套真相，而排查时两边对不上号比没有说法更糟。
        var home = EcefCentimeters.FromVector(Surface(Home));
        var other = ServerCaliber();
        other.TerrainVersion = 0xDEADBEEF;

        var server = ServerCaliber();
        var h = NewRig(new StubCaliber(server));
        Assert.False(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(other)));
        Assert.False(await h.Grain.TeleportAsync(7, home.X, home.Y, home.Z, 110, CaliberBytes(other)));
        Assert.Null(h.Authority.OwnerOf(7));                       // 两个入口都没留下归属

        var receipt = await h.Grain.TransferAsync(WithCaliber(Req(7, Home, East, Surface(East), 121), other));
        Assert.False(receipt.Accepted);
        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, receipt.Code);

        string librarySays = PlanetHandshake.DescribeMismatch(server, other);
        Assert.Contains("terrainVersion", librarySays, StringComparison.Ordinal);   // 夹具确实改的是那一项
        Assert.Contains(librarySays, receipt.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 快照也过口径闸门_不匹配时一格订阅都不带出去()
    {
        // §13.55 定的是"快照先只读"，但只读不等于不设卡：订阅集是按层级算出来的区域号，
        // 客户端拿着另一颗星球的清单去加载邻区，得到的是另一块地面的实体，而且全程不报错。
        var home = EcefCentimeters.FromVector(Surface(Home));
        var other = ServerCaliber();
        other.TerrainVersion ^= 9u;

        var h = NewRig(new StubCaliber(ServerCaliber()));
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));
        Assert.True(await h.Grain.BindAsync(8, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));

        var refused = await h.Grain.SnapshotAsync(7, CaliberBytes(other));
        Assert.False(refused.Accepted);
        Assert.Equal(PlanetRegionRejectCode.CaliberMismatch, refused.Code);
        Assert.Contains("terrainVersion", refused.Reason, StringComparison.Ordinal);
        Assert.Equal(0, refused.OwnerRegionId);
        Assert.Empty(refused.SubscribedRegionIds);
        Assert.Empty(refused.VisibleCharacterIds);            // 别人的邻区清单一格都不许带出去
        Assert.Empty(refused.SubscribedBoundaryCells);

        // 正向孪生：同一份口径就能读，而且看得见 8 就在附近
        var ok = await h.Grain.SnapshotAsync(7, CaliberBytes(ServerCaliber()));
        Assert.True(ok.Accepted, ok.Reason);
        Assert.Equal(Home.ToId(), ok.OwnerRegionId);
        Assert.Contains(Home.ToId(), ok.SubscribedRegionIds);
        Assert.Contains(8UL, ok.VisibleCharacterIds);
        Assert.True(ok.SubscribedRegionIds.Length > 1, "邻区一个都没订阅，AOI 那一层等于没接");

        // 没声明口径的部署照旧能读（闸门不生效，而不是把读的人也拒光）
        var unconfigured = NewRig(null);
        Assert.True(await unconfigured.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, Array.Empty<byte>()));
        var freeRead = await unconfigured.Grain.SnapshotAsync(7, Array.Empty<byte>());
        Assert.True(freeRead.Accepted, freeRead.Reason);
    }

    [Fact]
    public async Task 快照里的空有几种意思_说法必须各不相同()
    {
        // 空数组至少有三种完全不同的意思：受理了但服务端不认识你（要重新落位）、
        // 这条请求压根不该被处理（要停手报警）、以及真没处理成功以外的其它空。
        // 客户端对它们的处置相反，所以结论与理由必须随快照一起回来。
        var home = EcefCentimeters.FromVector(Surface(Home));
        var h = NewRig(new StubCaliber(ServerCaliber()));

        // 第一种：合法角色号但从没落过位。这是"受理了，但服务端对你一无所知"——
        // 空数组在这里是事实，不是拒绝，所以 Accepted 必须为真；客户端据此重新落位而不是报警。
        var neverBound = await h.Grain.SnapshotAsync(4242, CaliberBytes(ServerCaliber()));
        Assert.True(neverBound.Accepted, neverBound.Reason);
        Assert.Equal(0, neverBound.OwnerRegionId);
        Assert.Empty(neverBound.SubscribedRegionIds);
        Assert.Empty(neverBound.SubscribedBoundaryCells);

        // 另一种空：角色号本身进不了判定（0 = 没有角色）。那才是拒绝，理由必须非空且指向角色号
        var zero = await h.Grain.SnapshotAsync(0, CaliberBytes(ServerCaliber()));
        Assert.False(zero.Accepted);
        Assert.Equal(PlanetRegionRejectCode.MalformedMessage, zero.Code);
        Assert.False(string.IsNullOrWhiteSpace(zero.Reason));
        Assert.NotEqual(neverBound.Reason, zero.Reason);        // "空的"与"被拒的"说法必然不同

        // 第三种：落了位——非空，而且理由写着"已受理"，与前两种都分得开
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, CaliberBytes(ServerCaliber())));
        var lonely = await h.Grain.SnapshotAsync(7, CaliberBytes(ServerCaliber()));
        Assert.True(lonely.Accepted, lonely.Reason);
        // 实测的事实，写下来而不是猜：可见集<em>不含自己</em>。
        // 这条对消费者有实际后果——客户端若假定列表里有自己，就会把自己当成"要加载的邻区实体"，
        // 于是本地角色被按远端实体的路子重建一次。判据把它钉住，改行为时一定会撞到这条。
        Assert.DoesNotContain(7UL, lonely.VisibleCharacterIds);
        Assert.Equal(Home.ToId(), lonely.OwnerRegionId);              // 归属非 0：这是"受理了、只是周围没人"
        Assert.Contains(Home.ToId(), lonely.SubscribedRegionIds);     // 本区始终在订阅集里
        Assert.Contains("已受理", lonely.Reason, StringComparison.Ordinal);
        Assert.True(lonely.SubscribedRegionIds.Length > lonely.VisibleCharacterIds.Length,
                    "订阅集与可见集一样长，说明这一层没在区分“看在哪些区”与“看见谁”");
    }

    /// <summary>从测试输出目录往上找到仓库根（与 <c>FlaxToolsRefreshListTests</c> 同一手法）。</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.Planet", "Horizon.Planet.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void 这条闸门两端都真的接上了线_而不是库里有判定没人调()
    {
        // §6.2 此前的状态就是"库里有 DescribeMismatch，服务端一侧一直是空的"。
        // 库里再加一版判定不会有任何症状，所以这一条扫的是接线本身：
        // 服务端注册了口径来源、客户端把字节装进了请求。少任何一端，闸门就不存在。
        string silo = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Orleans.Silo", "Program.cs"));
        Assert.Contains("IPlanetCaliberSource", silo, StringComparison.Ordinal);
        Assert.Contains("DemPlanetCaliberSource", silo, StringComparison.Ordinal);

        string transfers = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Planet", "PlanetRegionTransfers.cs"));
        Assert.Contains("PlanetCaliber = caliberBytes", transfers, StringComparison.Ordinal);
        Assert.Contains("PlanetHandshake.TryWrite", transfers, StringComparison.Ordinal);

        // 快照那条上行也得带口径。此前只有迁移这条有门：漏填时服务端判"握手包解不开(Truncated)"，
        // 客户端读起来像"包坏了"，而 WatchRules 会把它升成"不是同一个世界"直接停玩——
        // 一个没填的字段换来一整屏黑，这正是要用一根针钉住的那种错。
        string watchSender = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Planet", "PlanetRegionWatches.cs"));
        Assert.Contains("PlanetCaliber = caliberBytes", watchSender, StringComparison.Ordinal);
        Assert.Contains("TryBuildCaliberBytes(out var caliberBytes", watchSender, StringComparison.Ordinal);
        // 编不出来就不发（而不是发一条空口径）：两条上行都要守这条
        Assert.Contains("LastWhy = caliberWhy", watchSender, StringComparison.Ordinal);

        // 判定确实挂在迁移这条活路径上，而不是只挂在测试里
        string grain = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Orleans.Grains", "PlanetRegionGrain.cs"));
        Assert.Contains("PlanetHandshakePolicy.EvaluateRead", grain, StringComparison.Ordinal);
        Assert.Contains("PlanetRegionRejectCode.CaliberMismatch", grain, StringComparison.Ordinal);

        // ack 坐标系那半条协议也必须有人读：解回 + 交叉检查都在回执路径上，而不是只活在判据里
        string ackSide = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Planet", "PlanetRegionTransfers.cs"));
        Assert.Contains("PlanetRegionAckFrame.TryReconstruct", ackSide, StringComparison.Ordinal);
        Assert.Contains("SignedAxisDeviationDeg", ackSide, StringComparison.Ordinal);
        // 解码只许有一份：客户端里再出现一处 Rotate(AxisX) 就是第二套切平面算法
        var decoders = Directory
            .GetFiles(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Planet"), "*.cs")
            .Where(f => !f.EndsWith("PlanetRegionAckFrame.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("Rotate(PlanetVector3.AxisX", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(decoders);

        // 快照那条读路径在客户端也必须两头都在：有人发请求、有人收回执并交给规则层
        string watches = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Planet", "PlanetRegionWatches.cs"));
        Assert.Contains("new PlanetRegionSnapshotRequest", watches, StringComparison.Ordinal);
        Assert.Contains("PlanetRegionWatchRules.Decide", watches, StringComparison.Ordinal);
        Assert.Contains("PlanetRegionWatchRules.DiffAgainst", watches, StringComparison.Ordinal);
        string watchHandler = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Network", "MessageHandlers", "PlanetRegionSnapshotResponseHandler.cs"));
        Assert.Contains("PlanetRegionWatches.OnResponse", watchHandler, StringComparison.Ordinal);
        string driverWatch = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game",
            "Planet", "PlanetNearFieldDriver.cs"));
        Assert.Contains("MaybeRequestWatch(crossedRegionId: 0)", driverWatch, StringComparison.Ordinal);
        Assert.Contains("PlanetRegionWatches.NoteCrossed", driverWatch, StringComparison.Ordinal);

        // 反向检查：服务端工程里不许出现第二处自己比字段的口径判定
        // （"两套真相"的形状就是这里比 5 项、那里比 7 项；判定只许有一条路）
        var sep = Path.DirectorySeparatorChar;
        var duplicates = Directory
            .GetFiles(Path.Combine(RepoRoot(), "Horizon.Orleans.Grains"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "obj" + sep, StringComparison.Ordinal))
            .Where(f => !f.Contains(sep + "bin" + sep, StringComparison.Ordinal))
            .Where(f => !f.EndsWith("PlanetRegionGrain.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("PlanetHandshakePolicy", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(duplicates);
    }

}
