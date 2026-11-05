using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using HundunWorld.Game.Planet;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans.Runtime;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// §6.1 的地面约束在服务端活路径上的判据。
/// <para>这批判据存在的理由与 <see cref="PlanetRegionCaliberGateTests"/> 同一条：库里早有
/// 一份共享采样（<c>HeightSampler</c>），但服务端一侧压根没调用过它——于是"服务端不许把角色放进地里"
/// 这件事在离线判据里从来没地方长，而它唯一的实机症状是玩家站在地面下面。</para>
/// <para>两个方向都要钉：<b>该判的判</b>（穿模被拒、三条改状态的入口都挡、拒的时候不改权威表）、
/// <b>不该判的不判</b>（没挂基场数据时<em>整条跳过</em>，而不是把海拔当 0——那会把所有站在山上的人
/// 判成穿模，同时把所有泡在水里的人判成正常）。</para>
/// </summary>
public class PlanetRegionGroundGateTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = Neighbour(Home, RegionEdge.UPlus);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n)
            ? n
            : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");

    private static PlanetVector3 Dir(RegionKey key) => key.CenterDirection();

    /// <summary>海面点（整数厘米精确）。</summary>
    private static PlanetVector3 Surface(RegionKey key) => Dir(key) * Radius;

    private static byte[] NoCaliber = Array.Empty<byte>();

    /// <summary>
    /// 地面来源替身。<b>海拔由递进来的点自己算</b>（<see cref="ElevationOf"/>），因为请求要过
    /// <see cref="EcefCentimeters"/> 的整数厘米：夹具里硬编码"埋深 100 cm"会被取整挪动，
    /// 边界用例就从"判不判得对"变成"取整运气"。让桩按同一个点算，埋深就正好是想要的数。
    /// </summary>
    private sealed class StubGround : IPlanetGroundSource
    {
        public Func<PlanetVector3, double?> ElevationOf = _ => 0.0;
        public int Calls;
        public uint? Version { get; set; } = 0xABCD1234;

        public bool TryGetGroundElevationCm(in PlanetVector3 globalCm, out double elevationCm)
        {
            Calls++;
            var got = ElevationOf(globalCm);
            elevationCm = got ?? double.NaN;
            return got.HasValue;
        }

        public uint? TerrainVersion => Version;
        public string Describe() => $"stub version={Version?.ToString("x8") ?? "<none>"}";
    }

    private sealed class StubCaliber : IPlanetCaliberSource
    {
        public PlanetHandshake? Caliber;
        public bool TryGetCaliber(out PlanetHandshake caliber)
        {
            caliber = Caliber ?? default;
            return Caliber is not null;
        }
    }

    private sealed class Rig
    {
        public Rig(IPlanetGroundSource? ground, IPlanetCaliberSource? caliber = null)
        {
            var state = new PlanetRegionPersistedState();
            var mock = new Mock<IPersistentState<PlanetRegionPersistedState>>();
            mock.SetupGet(s => s.State).Returns(state);
            mock.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask).Callback(() => Writes++);
            Grain = new PlanetRegionGrain(new Mock<ILogger<PlanetRegionGrain>>().Object, mock.Object,
                                          caliber, ground);
            GrainTestHarness.AttachGrainContext(Grain, "PlanetRegion", "1");
        }

        public PlanetRegionGrain Grain { get; }
        public int Writes { get; private set; }
        public PlanetRegionAuthority Authority => Grain.AuthorityForChecks;

        /// <summary>把点写成整数厘米再读回来——线上走的就是这条路，夹具不许绕过它。</summary>
        public static long[] Ecef(PlanetVector3 point)
        {
            var ecef = EcefCentimeters.FromVector(point);
            return new[] { ecef.X, ecef.Y, ecef.Z };
        }
    }

    /// <summary>埋深正好是 <paramref name="belowCm"/> 的点：桩按同一个点算，取整不影响埋深。</summary>
    private static PlanetVector3 BelowGround(RegionKey key, double belowCm)
    {
        var dir = Dir(key);
        return dir * (Radius - belowCm);
    }

    private static StubGround StubsBelow(double belowCm) => new()
    {
        ElevationOf = p => (p.Length - Radius) + belowCm
    };

    [Fact]
    public async Task 没挂地面来源时地下点照样落位_海拔不会被当成0()
    {
        // 这是"该跳过"那一半：把 null 当成"地面海拔 0"会拒掉所有站在山上的人。
        // 桩在同样输入下会判成穿模（下面那条用例），所以这一条红只可能是"没挂时拿 0 当地面"。
        var h = new Rig(null);
        var point = BelowGround(Home, 5_000);            // 海面以下 50 m，挂了地面来源必判穿模
        var ecef = Rig.Ecef(point);
        Assert.True(await h.Grain.BindAsync(7, ecef[0], ecef[1], ecef[2], 100, NoCaliber),
                    "没挂基场数据时落位被拒了：说明服务端把取不出的海拔当成了 0");
        Assert.Equal(Home, h.Authority.OwnerOf(7));
    }

    [Fact]
    public async Task 挂上地面来源后同一个地下点被拒()
    {
        var ground = StubsBelow(5_000);
        var h = new Rig(ground);
        var ecef = Rig.Ecef(BelowGround(Home, 5_000));
        Assert.False(await h.Grain.BindAsync(7, ecef[0], ecef[1], ecef[2], 100, NoCaliber));
        Assert.True(ground.Calls > 0);
        Assert.Null(h.Authority.OwnerOf(7));              // 权威表里没留下任何东西
        Assert.Equal(0, h.Writes);                        // 也没落盘
    }

    [Fact]
    public async Task 地面上方的点放行_每条拒绝分支都要有同夹具的正向孪生()
    {
        var ground = new StubGround { ElevationOf = p => 0.0 };   // 地面正好在 sea level
        var h = new Rig(ground);
        var ecef = Rig.Ecef(Surface(Home));                // 埋深 0
        Assert.True(await h.Grain.BindAsync(7, ecef[0], ecef[1], ecef[2], 100, NoCaliber));
        Assert.Equal(Home, h.Authority.OwnerOf(7));
        Assert.True(ground.Calls > 0, "正向孪生也没问过地面：那上面两条用例都是空转");
    }

    [Fact]
    public async Task 容差之内不判超出才判_边界用的是两端共用的那个数()
    {
        // 容差之内：站在土里 1 cm 不该被拒（两端的采样差异就是厘米级）
        var inside = new Rig(StubsBelow(PlanetGround.BelowToleranceCm));   // 正好等于容差 → 不判
        var eInside = Rig.Ecef(BelowGround(Home, PlanetGround.BelowToleranceCm));
        Assert.True(await inside.Grain.BindAsync(7, eInside[0], eInside[1], eInside[2], 100, NoCaliber));

        // 超出 1 cm：同一份夹具只换这一个数，所以这一条红不是"运气不好"
        var outside = new Rig(StubsBelow(PlanetGround.BelowToleranceCm + 1));
        var eOutside = Rig.Ecef(BelowGround(Home, PlanetGround.BelowToleranceCm + 1));
        Assert.False(await outside.Grain.BindAsync(7, eOutside[0], eOutside[1], eOutside[2], 100, NoCaliber));
    }

    [Fact]
    public async Task 地面高程取不出时只跳过判定不拒人()
    {
        // 挂上了来源但这份数据取不出海拔（坏目录 / 未覆盖的极点）：漏判最多是一次穿模，
        // 误拒却是把玩家整个挡在世界之外
        var ground = new StubGround { ElevationOf = _ => null };
        var h = new Rig(ground);
        var ecef = Rig.Ecef(BelowGround(Home, 5_000));
        Assert.True(await h.Grain.BindAsync(7, ecef[0], ecef[1], ecef[2], 100, NoCaliber));
        Assert.True(ground.Calls > 0, "桩压根没被问：这条用例是在空转");
    }

    [Fact]
    public async Task 落位传送迁移三条改状态的入口都挡_快照那条读路径不查地面()
    {
        var ground = new StubGround();                    // 地面就在海平面：地上的点放行、地下的点拒
        var h = new Rig(ground);
        var below = Rig.Ecef(BelowGround(Home, 5_000));
        var above = Rig.Ecef(Surface(Home));

        Assert.False(await h.Grain.BindAsync(7, below[0], below[1], below[2], 100, NoCaliber));
        Assert.False(await h.Grain.TeleportAsync(7, below[0], below[1], below[2], 100, NoCaliber));

        // 先合法落位，再试一条地下点的迁移
        Assert.True(await h.Grain.BindAsync(8, above[0], above[1], above[2], 101, NoCaliber));
        int callsBeforeSnapshot = ground.Calls;
        var snap = await h.Grain.SnapshotAsync(8, NoCaliber);
        Assert.True(snap.Accepted, snap.Reason);        Assert.True(ground.Calls == callsBeforeSnapshot,
                    "快照那条读路径也在判地面：表里的位置被拒时客户端没有任何办法自救");

        var request = new PlanetRegionTransferRequest
        {
            CharacterId = 8,
            FromRegionId = Home.ToId(),
            ToRegionId = East.ToId(),
            GlobalXcm = below[0],
            GlobalYcm = below[1],
            GlobalZcm = below[2],
            Tick = 102,
        };
        var receipt = await h.Grain.TransferAsync(request);
        Assert.False(receipt.Accepted);
        Assert.Equal(PlanetRegionRejectCode.Underground, receipt.Code);
        Assert.False(receipt.Applied);
        Assert.True(ground.Calls > callsBeforeSnapshot);
        Assert.Equal(Home, h.Authority.OwnerOf(8));            // 归属没被地下点改走
    }

    [Fact]
    public async Task 拒绝回执把三个数都念出来_只念穿模查不到是哪份数据错()
    {
        var h = new Rig(StubsBelow(5_000));
        var below = Rig.Ecef(BelowGround(Home, 5_000));
        var receipt = await h.Grain.TransferAsync(new PlanetRegionTransferRequest
        {
            CharacterId = 7,
            FromRegionId = Home.ToId(),
            ToRegionId = East.ToId(),
            GlobalXcm = below[0], GlobalYcm = below[1], GlobalZcm = below[2],
            Tick = 100,
        });
        Assert.Contains("穿模", receipt.Reason, StringComparison.Ordinal);      // Explain(Underground) 那句
        Assert.Contains("地面海拔", receipt.Reason, StringComparison.Ordinal);
        Assert.Contains("点海拔", receipt.Reason, StringComparison.Ordinal);
        Assert.Contains("埋深", receipt.Reason, StringComparison.Ordinal);
        Assert.Contains("容差", receipt.Reason, StringComparison.Ordinal);
        // 数字要真的在串里（不是"埋深 米"这种空壳）：50 m 那个量级
        Assert.Contains("50", receipt.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 闸门状态三种都能读出来_漏注册不该是一个看不见的差别()
    {
        Assert.Equal("off(未注册地面来源)", new Rig(null).Grain.GroundGateStatus);
        Assert.StartsWith("off(地面数据取不出来",
            new Rig(new StubGround { Version = null }).Grain.GroundGateStatus, StringComparison.Ordinal);
        Assert.Equal("on(terrainVersion=abcd1234)", new Rig(new StubGround()).Grain.GroundGateStatus);
    }

    [Fact]
    public void 地面与口径挂的不是同一份数据时要当场说出来()
    {
        // 两个来源各读一遍配置是配得出分歧的，而两条判定各自都自洽：唯一能看见它的地方是这里
        var caliber = new StubCaliber
        {
            Caliber = new PlanetHandshake
            {
                WireProtocol = PlanetHandshake.ProtocolVersion,
                PlanetRadiusCm = Radius,
                RegionLevel = (byte)Level,
                TerrainVersion = 0x1111_2222,
            }
        };
        var status = new Rig(new StubGround { Version = 0x9999_8888 }, caliber).Grain.GroundGateStatus;
        Assert.Contains("与口径不符", status, StringComparison.Ordinal);
        Assert.Contains("99998888", status, StringComparison.Ordinal);
        Assert.Contains("11112222", status, StringComparison.Ordinal);

        // 正向孪生：同一个版本不许报这句
        var same = new Rig(new StubGround { Version = 0x1111_2222 }, caliber).Grain.GroundGateStatus;
        Assert.DoesNotContain("与口径不符", same, StringComparison.Ordinal);
        Assert.StartsWith("on(", same, StringComparison.Ordinal);
    }

    // ===== 两端共用一个门槛：客户端抬位、服务端拒收，用的必须是同一个数 =====

    [Fact]
    public void 客户端抬位后服务端不再判穿模_容差是两端共用的那个常量()
    {
        foreach (double below in new[] { 50.0, 5_000.0, 400_000.0 })
        {
            var point = BelowGround(Home, below);
            var lifted = PlanetRegionTransferRules.LiftAboveGround(point, _ => 0.0);   // 桩：地面就在海面
            double afterLift = PlanetGround.BelowGroundCm(0.0, lifted.Length - Radius);
            if (below <= PlanetGround.BelowToleranceCm)
            {
                Assert.Equal(point, lifted);                              // 容差之内原样返回，一个数都不动
                Assert.True(afterLift <= PlanetGround.BelowToleranceCm); // 服务端同判：不拒
            }
            else
            {
                Assert.True(afterLift <= PlanetGround.BelowToleranceCm,
                            $"抬完之后服务端仍会判穿模（埋深 {afterLift:0.###} cm）：两端的门槛不是一个数");
            }
        }
    }

    [Fact]
    public void 客户端抬位只改半径不改方向_反查出来的区域与埋着时同一块()
    {
        var point = BelowGround(Home, 5_000);
        var lifted = PlanetRegionTransferRules.LiftAboveGround(point, _ => 0.0);
        var before = RegionKey.FromDirection(point, Level);
        var after = RegionKey.FromDirection(lifted, Level);
        Assert.Equal(before, after);                                   // 抬位不该把换区变成跳格
        Assert.True(Math.Abs(lifted.Length - Radius) < 1.0);
        Assert.True(Math.Abs(PlanetVector3.Dot(point / point.Length, lifted / lifted.Length) - 1.0) < 1e-12);
    }

    [Fact]
    public void 客户端没有高度场时原样发_抬位不是发送的前置条件()
    {
        var point = BelowGround(Home, 5_000);
        Assert.Equal(point, PlanetRegionTransferRules.LiftAboveGround(point, null));
        Assert.Equal(point, PlanetRegionTransferRules.LiftAboveGround(point, _ => null));
        Assert.Equal(point, PlanetRegionTransferRules.LiftAboveGround(point, _ => double.NaN));
        // 退化点交给后面的几何判定去报，这里不制造一个 NaN 坐标
        var zero = new PlanetVector3(0, 0, 0);
        Assert.Equal(zero, PlanetRegionTransferRules.LiftAboveGround(zero, _ => 0.0));
    }

    // ===== 接线：判据能跑不代表线上有人调 =====

    [Fact]
    public void 地面约束两端都真的接上了线_而不是库里有采样没人调()
    {
        string silo = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Orleans.Silo", "Program.cs"));
        Assert.Contains("IPlanetGroundSource", silo, StringComparison.Ordinal);
        Assert.Contains("DemPlanetGroundSource", silo, StringComparison.Ordinal);
        // 两个来源必须从同一处取目录：各读一遍键就能配出"口径是 A、地面是 B"
        Assert.Equal(1, CountOccurrences(silo, "PLANET_DEM_ROOT"));

        string grain = File.ReadAllText(Path.Combine(RepoRoot(), "Horizon.Orleans.Grains", "PlanetRegionGrain.cs"));
        Assert.Contains("PlanetRegionRejectCode.Underground", grain, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(grain, "GroundRejection(new PlanetVector3"));   // 落位/传送/迁移
        Assert.DoesNotContain("!double.IsFinite(globalCm.X)", grain, StringComparison.Ordinal);

        // 客户端：探针由高度场接上，并且排在 TryBuild 之前
        string driver = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Planet",
                                                      "PlanetNearFieldDriver.cs"));
        Assert.Contains("PlanetRegionTransfers.GroundElevationCm = p =>", driver, StringComparison.Ordinal);
        Assert.Contains("PlanetHeightField.TryCreateFromDirectory", driver, StringComparison.Ordinal);
        // 装配只许一处：驱动里再出现手拼就是第二套海拔口径
        foreach (string needle in new[] { "new DemGrid(", "new HybridHeightSource(", "new HeightSampler(" })
            Assert.DoesNotContain(needle, driver, StringComparison.Ordinal);

        string transfers = File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Planet",
                                                         "PlanetRegionTransfers.cs"));
        Assert.Contains("LiftBeforeSend(globalCm)", transfers, StringComparison.Ordinal);
        int liftAt = transfers.IndexOf("LiftBeforeSend(globalCm)", StringComparison.Ordinal);
        int buildAt = transfers.IndexOf("TryBuild(CharacterIdProvider()", StringComparison.Ordinal);
        Assert.True(liftAt >= 0 && buildAt > liftAt, "抬位必须排在 TryBuild 之前：否则地下点会先算出一个没意义的区域");
    }

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.Planet", "Horizon.Planet.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
