using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Horizon.Game.Message;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Orleans.Interface;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using MemoryPack;
using Microsoft.Extensions.Logging;
using Moq;
using Orleans.Runtime;
using Orleans.TestingHost;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// <see cref="PlanetRegionGrain"/> 这一层适配器的判据。
/// <para>几何与权限的判定本身已经在 <c>Horizon.Planet.Tests</c> 里钉住了，这里只管适配器会错的那几件事：
/// ① 两套枚举往一条线上码段映射时撞码或漏码；② 位打包区域号跨线时宽度不够；
/// ③ 存档恢复的回放次序（<c>TryBind</c> 拒绝比时钟旧的落位，而字典枚举顺序不保证）；
/// ④ 口径漂移的存档不许硬恢复；⑤ 落盘节奏（位置当场写，时钟攒着写）；⑥ 投影字段逐条对得上。</para>
/// <para>每条拒绝分支都配了正向孪生：只测"被拒"的话，一个把所有请求都拒掉的实现也是全绿的。</para>
/// </summary>
public class PlanetRegionGrainAdapterTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

    private static readonly RegionKey Home = new(2, Level, 8192, 8192);
    private static readonly RegionKey East = Neighbour(Home, RegionEdge.UPlus);
    private static readonly RegionKey North = Neighbour(Home, RegionEdge.VMinus);
    private static readonly RegionKey Far = new(0, Level, 100, 100);         // 另一个面，绝不可能与 Home 相邻
    private static readonly RegionKey FarAdj = Neighbour(Far, RegionEdge.UPlus);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n)
            ? n
            : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");

    /// <summary>区域中心的海面点（整数厘米可精确表示，跨存档往返不漂）。</summary>
    private static PlanetVector3 Surface(RegionKey key) => key.CenterDirection() * Radius;

    private static EcefCentimeters SurfaceEcef(RegionKey key) => EcefCentimeters.FromVector(Surface(key));

    /// <summary>没有口径来源的夹具传空字节即可：闸门整段跳过（见 服务端没声明口径时闸门不生效）。</summary>
    private static readonly byte[] NoCaliber = Array.Empty<byte>();

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

    private static PlanetRegionTransferRequest BadRegionId(ulong character, RegionKey to, PlanetVector3 point, long tick)
    {
        var ecef = EcefCentimeters.FromVector(point);
        return new PlanetRegionTransferRequest
        {
            CharacterId = character,
            FromRegionId = -1,                        // 位打包约定 id 非负：解不开就是坏包
            ToRegionId = to.ToId(),
            GlobalXcm = ecef.X,
            GlobalYcm = ecef.Y,
            GlobalZcm = ecef.Z,
            Tick = tick,
        };
    }

    private static PlanetRegionEntityRecord Record(RegionKey key, long tick)
    {
        var ecef = SurfaceEcef(key);
        return new PlanetRegionEntityRecord
        {
            RegionId = key.ToId(),
            Xcm = ecef.X,
            Ycm = ecef.Y,
            Zcm = ecef.Z,
            Tick = tick,
        };
    }

    /// <summary>裸激活的 grain + 可控的持久化替身；写次数由替身记账，落盘节奏才有地方断言。</summary>
    private sealed class Harness
    {
        public Harness(PlanetRegionPersistedState? existing = null)
        {
            State = existing ?? new PlanetRegionPersistedState();
            var mock = new Mock<IPersistentState<PlanetRegionPersistedState>>();
            mock.SetupGet(s => s.State).Returns(State);
            mock.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask).Callback(() => Writes++);
            Grain = new PlanetRegionGrain(new Mock<ILogger<PlanetRegionGrain>>().Object, mock.Object);
            GrainTestHarness.AttachGrainContext(Grain, "PlanetRegion", "1");
        }

        public PlanetRegionGrain Grain { get; }
        public PlanetRegionPersistedState State { get; }
        public int Writes { get; private set; }
        public PlanetRegionAuthority Authority => Grain.AuthorityForChecks;

        public Task<bool> Bind(ulong characterId, RegionKey key, long tick)
        {
            var ecef = SurfaceEcef(key);
            return Grain.BindAsync(characterId, ecef.X, ecef.Y, ecef.Z, tick, NoCaliber);
        }
    }

    [Fact]
    public void 夹具前提_区域中心点反查回自己且跨面不相邻()
    {
        // 上面那批夹具如果自身不可信，后面所有判据都是空中楼阁
        foreach (var key in new[] { Home, East, North, Far, FarAdj })
            Assert.Equal(key, RegionKey.FromDirection(Surface(key), Level));
        Assert.True(PlanetRegions.AreEdgeAdjacent(Home, East));
        Assert.False(PlanetRegions.AreEdgeAdjacent(Home, Far));
        Assert.True(Math.Abs(Surface(Home).Length - Radius) < 1.0,
            $"海面点应当落在半径上（实际偏差 {Surface(Home).Length - Radius:0.###} cm）");
        // "离海面太远"这条用例要有牙齿：1% 半径还在那点以下，得放大到 5% 才算天外
        Assert.True(Radius * 0.01 < PlanetRegions.MaxTransferAltitudeCm);
        Assert.True(Radius * 0.05 > PlanetRegions.MaxTransferAltitudeCm);
    }

    [Fact]
    public void 两套拒绝枚举逐条映射成互不撞车的线上码()
    {
        var geometry = Enum.GetValues<TransferRejectReason>();
        var authorityRejects = Enum.GetValues<AuthorityReject>();
        var geometryCodes = geometry.Where(r => r != TransferRejectReason.None)
                                    .Select(r => PlanetRegionGrain.ToCode(r)).ToList();
        var authorityCodes = authorityRejects.Where(r => r != AuthorityReject.None)
                                             .Select(r => PlanetRegionGrain.ToCode(r)).ToList();

        Assert.Equal(geometry.Length - 1, geometryCodes.Distinct().Count());     // 几何侧内部不撞码
        Assert.Equal(authorityRejects.Length - 1, authorityCodes.Distinct().Count());
        Assert.Empty(geometryCodes.Intersect(authorityCodes));                   // 两侧之间也不撞码
        Assert.Equal(PlanetRegionRejectCode.None, PlanetRegionGrain.ToCode(TransferRejectReason.None));
        Assert.Equal(PlanetRegionRejectCode.None, PlanetRegionGrain.ToCode(AuthorityReject.None));

        // 线上码段不许有孤儿：除了适配器/网关自己的入口检查，每条码都要能由某个枚举值发出来
        var produced = new HashSet<PlanetRegionRejectCode>(geometryCodes.Concat(authorityCodes));
        Assert.True(produced.Add(PlanetRegionRejectCode.None));
        var orphans = Enum.GetValues<PlanetRegionRejectCode>().Where(c => !produced.Contains(c)).ToList();
        // 这几条都不来自库里那两个枚举，而是装配层自己发的：
        // MalformedMessage 在 grain 的入口判（请求进不了判定），
        // SessionMismatch 在网关判（连接与角色号不符，压根不该进 grain），
        // CaliberMismatch 由 §6.2 的口径闸门发，Underground 由 §6.1 的地面闸门发。
        var handWritten = new[]
        {
            PlanetRegionRejectCode.MalformedMessage,
            PlanetRegionRejectCode.SessionMismatch,
            PlanetRegionRejectCode.CaliberMismatch,
            PlanetRegionRejectCode.Underground,
        };
        Assert.Equal(handWritten, orphans);
        // 白名单本身不设防：往上面加一条就够让一个"从来没被发出来"的码变绿。
        // 所以每条都要在装配层源码里真的出现——判据要问的是"有没有人发这条"，不是"这份清单对不对"。
        var assemblyLayer = string.Concat(
            File.ReadAllText(RepoSource("Horizon.Orleans.Grains", "PlanetRegionGrain.cs")),
            File.ReadAllText(RepoSource("Horizon.Game.Core", "Handlers", "PlanetRegionHandler.cs")));
        foreach (var orphan in handWritten)
            Assert.Contains($"PlanetRegionRejectCode.{orphan}", assemblyLayer, StringComparison.Ordinal);
        // 反证用的针必须真的在场上：这句断言的是"上面那条 Contains 不是空转"
        Assert.Contains("PlanetRegionRejectCode.SessionMismatch", assemblyLayer, StringComparison.Ordinal);

        // 复合入口先看权限侧：几何与权限不会同时给出非 None，所以这条式子的分支是穷尽的
        Assert.Equal(PlanetRegionRejectCode.NotAdjacent,
            PlanetRegionGrain.ToCode(TransferRejectReason.NotAdjacent, AuthorityReject.None));
        Assert.Equal(PlanetRegionRejectCode.OwnerMismatch,
            PlanetRegionGrain.ToCode(TransferRejectReason.None, AuthorityReject.OwnerMismatch));

        // 每条码都要有自己的说法，否则客户端收到的是"拒了但不知道为什么"
        var acceptedWord = PlanetRegionGrain.Explain(PlanetRegionRejectCode.None);
        foreach (var code in Enum.GetValues<PlanetRegionRejectCode>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PlanetRegionGrain.Explain(code)));
            if (code != PlanetRegionRejectCode.None)
                Assert.NotEqual(acceptedWord, PlanetRegionGrain.Explain(code));
        }
    }

    [Fact]
    public void 枚举加了值却没映射时当场抛而不是静默落成None()
    {
        // 正向孪生在上一条：全部真实值都能映射。这里验"漏映射"这个错不会被放过
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionGrain.ToCode((TransferRejectReason)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionGrain.ToCode((AuthorityReject)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionGrain.ToCode((TransferRejectReason)999, AuthorityReject.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlanetRegionGrain.ToCode(TransferRejectReason.None, (AuthorityReject)999));
    }

    [Fact]
    public void 区域号跨线不截断_退回int时必然装不下()
    {
        foreach (int face in Enumerable.Range(0, CubeSphere.FaceCount))
        {
            foreach (int level in new[] { 2, 8, Level })
            {
                int span = 1 << level;
                var key = new RegionKey(face, level, span - 1, span - 1);
                long id = key.ToId();
                var request = Req(7, key, key, Surface(key), 1);
                Assert.Equal(key, RegionKey.FromId(request.FromRegionId));

                // 反证：face 在 bit58、level 在 bit54，任何 int32 字段都装不下这个 id
                //（不是"face 0 侥幸能装"，是值域整体越界）——字段当初写成 int 时六个面会塌成一堆非法/错区域
                Assert.True(id > int.MaxValue, $"{key} 的 id={id} 竟然装得进 int32，这条反证就失去意义了");
                int truncated = unchecked((int)id);
                var decoded = TryDecodeRegionId(truncated);
                Assert.True(decoded is null || decoded.Value != key,
                    $"{key} 截成 int32 之后仍然等价，这条反证没有牙齿");
            }
        }
    }

    /// <summary>截断后的值多半连合法区域都解不出来（level 被抹成 0，x/y 越界）——那也算"不能退回 int"的证据，但要显式接住。</summary>
    private static RegionKey? TryDecodeRegionId(long id)
    {
        try { return RegionKey.FromId(id); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    [Fact]
    public async Task 合法共边迁移经grain受理并当场落盘()
    {
        var h = new Harness();
        Assert.True(await h.Bind(7, Home, 100));
        Assert.Equal(1, h.Writes);                                   // 落位是权威位置，必须当场写

        var result = await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 120));

        Assert.True(result.Accepted, result.Reason);
        Assert.True(result.Applied);
        Assert.Equal(PlanetRegionRejectCode.None, result.Code);
        Assert.Equal(East.ToId(), result.NewRegionId);
        Assert.Equal(120, result.HandoverTick);
        Assert.Equal(120, result.ServerTick);
        Assert.Equal(7UL, result.CharacterId);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(East, h.Authority.OwnerOf(7));
        Assert.Equal(2, h.Writes);                                   // 归属真的变了才再写一次

        // 落盘投影与请求里的整数厘米逐位相同：半径 6.371e9 cm 量级上"double 够不够"不是感觉问题
        var claimed = Req(7, Home, East, Surface(East), 120);
        var record = h.State.Entities[7];
        Assert.Equal(East.ToId(), record.RegionId);
        Assert.Equal(120, record.Tick);
        Assert.Equal(claimed.GlobalXcm, record.Xcm);
        Assert.Equal(claimed.GlobalYcm, record.Ycm);
        Assert.Equal(claimed.GlobalZcm, record.Zcm);
        Assert.Equal(120, h.State.Clock);
        Assert.Equal(Radius, h.State.PlanetRadiusCm);                // 口径字段要一起写出去，漂移才可判
        Assert.Equal(Level, h.State.RegionLevel);
    }

    [Fact]
    public async Task 每条被拒的迁移都带回独立码_理由非空且一行状态都不写()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        int writesAfterBind = h.Writes;
        var eastEcef = SurfaceEcef(East);

        var cases = new List<(string Label, PlanetRegionTransferRequest Request, PlanetRegionRejectCode Code, bool Geometry)>
        {
            ("区域号解不开", BadRegionId(7, East, eastEcef.ToVector(), 120), PlanetRegionRejectCode.MalformedRegion, true),
            ("全局点退化", Req(7, Home, East, PlanetVector3.Zero, 120), PlanetRegionRejectCode.MalformedPoint, true),
            ("层级不符", Req(7, new RegionKey(2, Level - 1, 4096, 4096),
                            new RegionKey(2, Level - 1, 4097, 4096), Surface(East), 120),
             PlanetRegionRejectCode.LevelMismatch, true),
            ("点不在声明的目标区", Req(7, Home, Home, Surface(East), 120), PlanetRegionRejectCode.PointNotInTarget, true),
            ("本来同区", Req(7, Home, Home, Surface(Home), 120), PlanetRegionRejectCode.SameRegion, true),
            ("不共边", Req(7, Home, FarAdj, Surface(FarAdj), 120), PlanetRegionRejectCode.NotAdjacent, true),
            ("离海面太远", Req(7, Home, East, Surface(East) * 1.05, 120), PlanetRegionRejectCode.TooFarFromSurface, true),
            ("服务端不认识这个实体", Req(999, Home, East, eastEcef.ToVector(), 120), PlanetRegionRejectCode.UnknownEntity, false),
            ("声明的from不是当前归属", Req(7, Far, FarAdj, Surface(FarAdj), 120), PlanetRegionRejectCode.OwnerMismatch, false),
            ("tick倒退", Req(7, Home, East, eastEcef.ToVector(), 99), PlanetRegionRejectCode.TickRegression, false),
        };

        var seen = new HashSet<PlanetRegionRejectCode>();
        int geometryExpected = 0, authorityExpected = 0;
        foreach (var (label, request, code, isGeometry) in cases)
        {
            var result = await h.Grain.TransferAsync(request);
            Assert.False(result.Accepted, label + "：应当被拒");
            Assert.False(result.Applied, label + "：应当没改归属");
            Assert.True(result.Code == code, $"{label}：期望码 {code}，实际 {result.Code}（理由 {result.Reason}）");
            Assert.False(string.IsNullOrWhiteSpace(result.Reason), label + "：拒绝必须带理由");
            Assert.True(seen.Add(code), $"{label} 与其它分支撞码：{code}");
            if (isGeometry) geometryExpected++; else authorityExpected++;

            // 归属一点不许动；实体未知的那条，服务端要如实报"没有归属"
            Assert.Equal(Home, h.Authority.OwnerOf(7));
            Assert.Null(h.Authority.OwnerOf(999));
        }

        Assert.Equal(cases.Count, seen.Count);                       // 十条拒绝十条不同的码
        Assert.Equal(geometryExpected, h.Authority.RejectedGeometry);
        Assert.Equal(authorityExpected, h.Authority.RejectedAuthority);
        Assert.Equal(0, h.Authority.Applied);                        // 库里那条"被拒不改状态"的账要一致
        Assert.Equal(writesAfterBind, h.Writes);                     // 被拒不许产生落盘 IO
        Assert.Equal(100, h.Authority.Clock);                        // 狂发坏包也拨不动服务端时钟
    }

    [Fact]
    public async Task 被拒时回执带的是真实归属_未知实体则为0()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);

        // 几何全对、但 from 报错：服务端该把"你其实在 Home"回给客户端，而不是照抄它的声明
        var mismatch = await h.Grain.TransferAsync(Req(7, Far, FarAdj, Surface(FarAdj), 120));
        Assert.Equal(PlanetRegionRejectCode.OwnerMismatch, mismatch.Code);
        Assert.Equal(Home.ToId(), mismatch.NewRegionId);

        var unknown = await h.Grain.TransferAsync(Req(4242, Home, East, Surface(East), 120));
        Assert.Equal(PlanetRegionRejectCode.UnknownEntity, unknown.Code);
        Assert.Equal(0, unknown.NewRegionId);                        // 0 = 服务端没有归属可报，客户端要重新落位
    }

    [Fact]
    public async Task 角色号为0或越出int64时不进判定()
    {
        var h = new Harness();
        foreach (ulong bad in new ulong[] { 0, (ulong)long.MaxValue + 1, ulong.MaxValue })
        {
            var result = await h.Grain.TransferAsync(Req(bad, Home, East, Surface(East), 10));
            Assert.False(result.Accepted);
            Assert.Equal(PlanetRegionRejectCode.MalformedMessage, result.Code);
            Assert.False(string.IsNullOrWhiteSpace(result.Reason));
            Assert.Equal(0, result.NewRegionId);
            Assert.False(await h.Bind(bad, Home, 10));
            var ecef = SurfaceEcef(East);
            Assert.False(await h.Grain.TeleportAsync(bad, ecef.X, ecef.Y, ecef.Z, 10, NoCaliber));
        }
        Assert.Equal(0, h.Authority.Count);                          // 一条都不该进表
        Assert.Equal(0, h.Writes);

        // 正向孪生：int64 上界的角色号能正常落位（越界只发生在它之上，不是"大数一律拒"）
        Assert.True(await h.Bind((ulong)long.MaxValue, Home, 10));
        Assert.Equal(1, h.Writes);
        var snapshot = await h.Grain.SnapshotAsync((ulong)long.MaxValue, NoCaliber);
        Assert.Equal(Home.ToId(), snapshot.OwnerRegionId);
    }

    [Fact]
    public async Task 空请求包体被拒且不炸后续判定()
    {
        var h = new Harness();
        var result = await h.Grain.TransferAsync(null!);
        Assert.False(result.Accepted);
        Assert.Equal(PlanetRegionRejectCode.MalformedMessage, result.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        Assert.Equal(0, h.Writes);
        Assert.Equal(0, h.Authority.Count);
        // 之后正常请求照旧能受理：一条坏包不该把 grain 打进不可用状态
        Assert.True(await h.Bind(7, Home, 100));
        Assert.True((await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 110))).Accepted);
    }

    [Fact]
    public async Task 传送直接改归属且不要求共边()
    {
        var h = new Harness();
        Assert.True(await h.Bind(7, Home, 100));
        var ecef = SurfaceEcef(Far);                                 // 另一个面：迁移要走四次跨边，传送一步到位
        Assert.True(await h.Grain.TeleportAsync(7, ecef.X, ecef.Y, ecef.Z, 120, NoCaliber));
        Assert.Equal(Far, h.Authority.OwnerOf(7));
        Assert.Equal(2, h.Writes);                                   // 落位 + 传送各写一次，没有多余的

        // 正向孪生的反面：没落过位的实体传不动（传送不是"创建实体"的入口）
        Assert.False(await h.Grain.TeleportAsync(8, ecef.X, ecef.Y, ecef.Z, 130, NoCaliber));
        // tick 倒退的传送也不受理，且归属保持不变
        Assert.False(await h.Grain.TeleportAsync(7, SurfaceEcef(North).X, SurfaceEcef(North).Y, SurfaceEcef(North).Z, 110, NoCaliber));
        Assert.Equal(Far, h.Authority.OwnerOf(7));
        Assert.Null(h.Authority.OverlapOf(7));                       // 传送不留重叠窗口
    }

    [Fact]
    public void 存档按tick升序回放_倒着插的存档也能整表进来()
    {
        var state = new PlanetRegionPersistedState { PlanetRadiusCm = Radius, RegionLevel = Level, Clock = 500 };
        // 故意按 tick 倒着插：TryBind 拒绝比时钟旧的落位，Restore 少排一次序就只剩一条
        state.Entities[7] = Record(Home, 30);
        state.Entities[8] = Record(East, 20);
        state.Entities[9] = Record(North, 10);
        var h = new Harness(state);

        int skipped = h.Grain.RestoreFromState();

        Assert.Equal(0, skipped);
        Assert.Equal(3, h.Authority.Count);
        Assert.Equal(Home, h.Authority.OwnerOf(7));
        Assert.Equal(East, h.Authority.OwnerOf(8));
        Assert.Equal(North, h.Authority.OwnerOf(9));
        Assert.Equal(500, h.Authority.Clock);                        // 时钟接回存档值，不是最后一条记录的 tick
        // 恢复完的实体 tick 仍是各自自己的（不是被时钟覆盖）：否则下一次落位的 tick 判定会全线失真
        Assert.Equal(10, h.Authority.SnapshotEntities().First(e => e.EntityId == 9).Tick);
        // 反向对照：去掉排序就只剩一条——把这条判据交给变异检查，这里先把期望写死
        var unsorted = new List<PlanetRegionEntityRecord> { Record(Home, 30), Record(East, 20), Record(North, 10) };
        var direct = new PlanetRegionAuthority(Radius, Level);
        int bound = 0;
        foreach (var record in unsorted)                             // 降序插入：正是"忘记排序"的枚举次序
            if (direct.TryBind(7 + bound, record.Xcm == 0 ? default : new PlanetVector3(record.Xcm, record.Ycm, record.Zcm),
                               record.Tick, out _)) bound++;
        Assert.Equal(1, bound);
    }

    [Fact]
    public void 口径漂移的存档整份不恢复_而不是把人放进错区域()
    {
        PlanetRegionPersistedState Build(int level, long radius)
        {
            var state = new PlanetRegionPersistedState { PlanetRadiusCm = radius, RegionLevel = level, Clock = 500 };
            state.Entities[7] = Record(Home, 30);
            state.Entities[8] = Record(East, 20);
            return state;
        }

        var levelDrift = new Harness(Build(Level - 1, Radius));
        Assert.Equal(2, levelDrift.Grain.RestoreFromState());
        Assert.Equal(0, levelDrift.Authority.Count);

        var radiusDrift = new Harness(Build(Level, Radius + 1));
        Assert.Equal(2, radiusDrift.Grain.RestoreFromState());
        Assert.Equal(0, radiusDrift.Authority.Count);

        // 正向孪生：口径一致的同一份存档全部进来
        var matching = new Harness(Build(Level, Radius));
        Assert.Equal(0, matching.Grain.RestoreFromState());
        Assert.Equal(2, matching.Authority.Count);
    }

    [Fact]
    public void 存档里区域号与坐标不符的那条不进表()
    {
        var state = new PlanetRegionPersistedState { PlanetRadiusCm = Radius, RegionLevel = Level, Clock = 500 };
        state.Entities[7] = Record(Home, 30);
        var tampered = Record(East, 20);
        tampered.RegionId = Far.ToId();                              // 手改库/旧版本写出的记录才会这样
        state.Entities[8] = tampered;
        var h = new Harness(state);

        Assert.Equal(1, h.Grain.RestoreFromState());
        Assert.Equal(Home, h.Authority.OwnerOf(7));                  // 正向孪生：一致的那条照常进来
        Assert.Null(h.Authority.OwnerOf(8));                         // 不一致的那条不许污染权威表
        Assert.Equal(500, h.Authority.Clock);
    }

    [Fact]
    public async Task 坐标不可用或实体号非正的记录不进表()
    {
        var state = new PlanetRegionPersistedState { PlanetRadiusCm = Radius, RegionLevel = Level, Clock = 500 };
        state.Entities[7] = Record(Home, 30);
        state.Entities[8] = new PlanetRegionEntityRecord { RegionId = East.ToId(), Tick = 20 };   // 全零坐标 = 地心
        state.Entities[0] = Record(North, 10);                       // 非正实体号是坏行
        var h = new Harness(state);

        Assert.Equal(2, h.Grain.RestoreFromState());
        Assert.Equal(Home, h.Authority.OwnerOf(7));
        Assert.Null(h.Authority.OwnerOf(8));
        Assert.Null(h.Authority.OwnerOf(0));
        Assert.Equal(500, h.Authority.Clock);
        // 时钟仍然接上了：重启前那批旧 tick 的包不会被当成新落位
        Assert.False(await h.Bind(11, North, 400));
        Assert.True(await h.Bind(11, North, 500));
    }

    [Fact]
    public async Task 空存档也要把时钟接上()
    {
        var h = new Harness(new PlanetRegionPersistedState { Clock = 500 });
        Assert.Equal(0, h.Grain.RestoreFromState());
        Assert.Equal(500, h.Authority.Clock);
        Assert.False(await h.Bind(7, Home, 499));
        Assert.Equal(0, h.Writes);
        Assert.True(await h.Bind(7, Home, 500));
        Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task 归属变更当场落盘_时钟推进攒够才落()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        Assert.Equal(1, h.Writes);

        long tick = 101;
        for (int i = 0; i < PlanetRegionGrain.ClockAdvancesPerFlush - 1; i++)
            await h.Grain.AdvanceAsync(tick++);
        Assert.Equal(1, h.Writes);                                   // 推进时钟不该次次写盘
        Assert.NotEqual(h.Authority.Clock, h.State.Clock);           // 也确实还没落下去

        await h.Grain.AdvanceAsync(tick++);
        Assert.Equal(2, h.Writes);
        Assert.Equal(h.Authority.Clock, h.State.Clock);              // 攒够了要写全，不能只写一半
        Assert.Equal(1, h.State.Entities.Count);                     // 表里只有那一个实体
        Assert.Equal(h.Authority.OwnerOf(7)!.Value.ToId(), h.State.Entities[7].RegionId);

        // 攒着的窗口里 grain 去激活也不许丢时钟
        await h.Grain.AdvanceAsync(tick++);
        Assert.Equal(2, h.Writes);
        await GrainTestHarness.DeactivateAsync(h.Grain);
        Assert.Equal(3, h.Writes);
        Assert.Equal(h.Authority.Clock, h.State.Clock);
    }

    [Fact]
    public async Task 时钟倒退当场抛_不静默吞()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        await h.Grain.AdvanceAsync(100);                             // 正向孪生：原地不动是允许的
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Grain.AdvanceAsync(99));
        Assert.Equal(100, h.Authority.Clock);
        // 吞掉倒退会留下"窗口早该摘却没摘"的脏状态，所以这条分支不能改成记日志了事
        Assert.Equal(1, h.Writes);
    }

    [Fact]
    public async Task 快照把订阅集与可见集翻成线上id()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        await h.Bind(8, East, 100);
        await h.Bind(9, Far, 100);

        var snap = await h.Grain.SnapshotAsync(7, NoCaliber);
        Assert.Equal(7UL, snap.CharacterId);
        Assert.Equal(Home.ToId(), snap.OwnerRegionId);
        Assert.Equal(1 + PlanetRegions.EdgeNeighbourCount, snap.SubscribedRegionIds.Length);
        Assert.Contains(East.ToId(), snap.SubscribedRegionIds);
        Assert.DoesNotContain(Far.ToId(), snap.SubscribedRegionIds);
        Assert.Contains(8UL, snap.VisibleCharacterIds);
        Assert.DoesNotContain(9UL, snap.VisibleCharacterIds);        // 另一面的实体不该被算进可见集
        Assert.DoesNotContain(7UL, snap.VisibleCharacterIds);        // 自己不算自己
        Assert.Equal(100, snap.ServerTick);

        // 迁移后旧区仍然被订阅（来源是共边邻居，不依赖重叠窗口）——所以重叠窗口不落盘是安全的
        Assert.True((await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 110))).Applied);
        var afterMove = await h.Grain.SnapshotAsync(7, NoCaliber);
        Assert.Equal(East.ToId(), afterMove.OwnerRegionId);
        Assert.Contains(Home.ToId(), afterMove.SubscribedRegionIds);

        var unknown = await h.Grain.SnapshotAsync(1234, NoCaliber);
        Assert.Equal(0, unknown.OwnerRegionId);
        Assert.Empty(unknown.SubscribedRegionIds);
        Assert.Empty(unknown.VisibleCharacterIds);
        var zero = await h.Grain.SnapshotAsync(0, NoCaliber);                   // 非法角色号也不该炸
        Assert.Equal(0, zero.OwnerRegionId);
        Assert.Empty(zero.SubscribedRegionIds);
    }

    [Fact]
    public async Task 落盘投影逐条等于权威表_再读回来也逐条相等()
    {
        var h = new Harness();
        Assert.Equal(0, h.Grain.RestoreFromState());                 // 空存档也要把表建起来
        await h.Bind(7, Home, 10);
        await h.Bind(8, East, 11);
        await h.Bind(9, North, 12);

        var snapshot = h.Authority.SnapshotEntities();
        Assert.Equal(snapshot.Count, h.State.Entities.Count);
        foreach (var entity in snapshot)
        {
            var record = h.State.Entities[entity.EntityId];
            var ecef = EcefCentimeters.FromVector(entity.GlobalCm);
            Assert.Equal(entity.Region.ToId(), record.RegionId);
            Assert.Equal(ecef.X, record.Xcm);
            Assert.Equal(ecef.Y, record.Ycm);
            Assert.Equal(ecef.Z, record.Zcm);
            Assert.Equal(entity.Tick, record.Tick);
        }

        var restored = new Harness(h.State);                         // 同一份投影再读一遍
        Assert.Equal(0, restored.Grain.RestoreFromState());
        Assert.Equal(3, restored.Authority.Count);
        foreach (var entity in snapshot)
        {
            Assert.Equal(entity.Region, restored.Authority.OwnerOf(entity.EntityId));
            Assert.Equal(entity.GlobalCm, restored.Authority.GlobalOf(entity.EntityId));
        }
    }

    [Fact]
    public async Task 受理的迁移带回新区坐标系_局部点回代逐位一致()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        var result = await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 120));
        Assert.True(result.Applied, result.Reason);

        var frame = RegionFrame.Build(East, Radius);
        // 原点是 i64 厘米，不该有任何量化损失：客户端拿它换 WorldOrigin，差 1 cm 就是整块地形平移 1 cm
        Assert.Equal(frame.Origin.X, result.OriginXcm);
        Assert.Equal(frame.Origin.Y, result.OriginYcm);
        Assert.Equal(frame.Origin.Z, result.OriginZcm);

        // 局部厘米回代到全局：误差必须 ≤ 半个厘米——这条说的是"交接那一帧与之后每一帧同一条取整规则"
        var local = frame.LocalFromGlobalCm(Surface(East));
        Assert.True(Math.Abs(result.LocalXCm - local.X) <= 0.5, $"局部 X 偏差 {result.LocalXCm - local.X:0.###} cm");
        Assert.True(Math.Abs(result.LocalYCm - local.Y) <= 0.5, $"局部 Y 偏差 {result.LocalYCm - local.Y:0.###} cm");
        Assert.True(Math.Abs(result.LocalZCm - local.Z) <= 0.5, $"局部 Z 偏差 {result.LocalZCm - local.Z:0.###} cm");

        // 姿态：Q15 四元数把局部轴转回全局，必须落回这一帧的三轴（比的是方向，不是位置）
        var q = new QuaternionQuantized
        {
            X = result.FrameX, Y = result.FrameY, Z = result.FrameZ, W = result.FrameW,
        };
        Assert.True(PlanetVector3.Dot(q.Rotate(PlanetVector3.AxisX), frame.East) > 1 - 1e-8, "东轴对不上");
        Assert.True(PlanetVector3.Dot(q.Rotate(PlanetVector3.AxisY), frame.Up) > 1 - 1e-8, "上轴对不上");
        Assert.True(PlanetVector3.Dot(q.Rotate(PlanetVector3.AxisZ), frame.South) > 1 - 1e-8, "南轴对不上");
    }

    [Fact]
    public async Task 被拒的迁移不许带回半套新区坐标系()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        var rejected = await h.Grain.TransferAsync(Req(7, Home, FarAdj, Surface(FarAdj), 120));   // 不共边

        Assert.False(rejected.Applied);
        Assert.Equal(0, rejected.LocalXCm);
        Assert.Equal(0, rejected.LocalYCm);
        Assert.Equal(0, rejected.LocalZCm);
        Assert.Equal(0, rejected.OriginXcm);
        Assert.Equal(0, rejected.OriginYcm);
        Assert.Equal(0, rejected.OriginZcm);
        Assert.Equal((short)0, rejected.FrameX);
        Assert.Equal((short)0, rejected.FrameW);
        // 半套 ack 的危险在于它"看着像一次真交接"：客户端照它换帧，服务端却没记账
        Assert.Equal(Home.ToId(), rejected.NewRegionId);
    }

    [Fact]
    public async Task 跨面换区的ack不会凭空多出一整块区域宽()
    {
        // §13.15 那个客户端缺陷（按固定 UPlus 取邻居，换区后局部坐标凭空多出一整个区域宽）
        // 在服务端这一层的等价判据：ack 里的局部点必须真的落在新区范围内。
        var edgeKey = new RegionKey(2, Level, (1 << Level) - 1, 8192);
        Assert.True(PlanetRegions.TryNeighbour(edgeKey, RegionEdge.UPlus, out var across), "面边缘必须有跨面邻居");
        Assert.NotEqual(edgeKey.Face, across.Face);

        var h = new Harness();
        Assert.True(await h.Bind(7, edgeKey, 100));
        var result = await h.Grain.TransferAsync(Req(7, edgeKey, across, Surface(across), 120));
        Assert.True(result.Applied, result.Reason);

        double bound = RegionMetrics.CenterToCornerCm(across, Radius);          // 区域内任一点到区中心的最大距离
        var local = new PlanetVector3(result.LocalXCm, result.LocalYCm, result.LocalZCm);
        double horizontal = Math.Sqrt(local.X * local.X + local.Z * local.Z);
        Assert.True(horizontal <= bound * 1.01,
            $"局部点离区心 {horizontal / 100000.0:0.##} km，超出新区范围 {bound / 100000.0:0.##} km——就是那一类凭空偏移");
    }

    [Fact]
    public void 局部厘米的取整与状态包同一条规则()
    {
        Assert.Equal(1, PlanetRegionGrain.QuantizeAckLocal(0.5, "X", East));        // .5 远离零
        Assert.Equal(-1, PlanetRegionGrain.QuantizeAckLocal(-0.5, "X", East));
        Assert.Equal(0, PlanetRegionGrain.QuantizeAckLocal(0.4, "X", East));
        Assert.Equal(PlayerStatePacket.MaxAbsLocalCm,
            PlanetRegionGrain.QuantizeAckLocal(PlayerStatePacket.MaxAbsLocalCm, "X", East));   // 正向孪生：正好在上界可用

        // 越界必须抛而不是钳位：钳位给的是一个"看起来合理"的坐标，而它真正在说的是部署口径不匹配
        Assert.Throws<InvalidOperationException>(
            () => PlanetRegionGrain.QuantizeAckLocal(PlayerStatePacket.MaxAbsLocalCm + 1.0, "X", East));
    }

    [Fact]
    public void 回执新增字段是追加而不是重排()
    {
        // 已发布客户端的字段编号一旦挪位，MemoryPack 不会报错，只会把值放到错的地方。
        var orders = typeof(PlanetRegionTransferResult)
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p.Name, Order: p.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order ?? -1))
            .Where(t => t.Order >= 0)
            .ToDictionary(t => t.Name, t => t.Order);

        Assert.Equal(0, orders["CharacterId"]);
        Assert.Equal(1, orders["Accepted"]);
        Assert.Equal(2, orders["Applied"]);
        Assert.Equal(3, orders["Code"]);
        Assert.Equal(4, orders["Reason"]);
        Assert.Equal(5, orders["NewRegionId"]);
        Assert.Equal(6, orders["HandoverTick"]);
        Assert.Equal(7, orders["ServerTick"]);
        Assert.Equal(8, orders["LocalXCm"]);
        Assert.Equal(11, orders["OriginXcm"]);
        Assert.Equal(14, orders["FrameX"]);
        Assert.Equal(17, orders["FrameW"]);
        Assert.Equal(Enumerable.Range(0, 18), orders.Values.OrderBy(v => v));      // 连续、无洞、无重号
    }

    [Fact]
    public async Task 回执里的ack与线结构RegionTransferAck逐字段可互换()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        var result = await h.Grain.TransferAsync(Req(7, Home, East, Surface(East), 120));

        // §6.2 给的是同一个东西的两种写法：走 TouchSocket 那条硬线协议时用的是 RegionTransferAck。
        // 两边对不上就是"同一个交接有两套真相"，所以这里逐字段写一遍再读回来。
        var ack = new RegionTransferAck
        {
            RegionId = result.NewRegionId,
            LocalXCm = result.LocalXCm,
            LocalYCm = result.LocalYCm,
            LocalZCm = result.LocalZCm,
            OriginCm = new EcefCentimeters(result.OriginXcm, result.OriginYcm, result.OriginZcm),
            Frame = new QuaternionQuantized
            {
                X = result.FrameX, Y = result.FrameY, Z = result.FrameZ, W = result.FrameW,
            },
        };
        var buffer = new byte[RegionTransferAck.Size];
        Assert.True(RegionTransferAck.TryWrite(buffer, ack, out int written));
        Assert.Equal(RegionTransferAck.Size, written);
        Assert.True(RegionTransferAck.TryRead(buffer, out var back, out var status, out _));
        Assert.Equal(PlanetWireStatus.Ok, status);

        Assert.Equal(ack.RegionId, back.RegionId);
        Assert.Equal(ack.LocalXCm, back.LocalXCm);
        Assert.Equal(ack.LocalYCm, back.LocalYCm);
        Assert.Equal(ack.LocalZCm, back.LocalZCm);
        Assert.Equal(ack.OriginCm.X, back.OriginCm.X);
        Assert.Equal(ack.OriginCm.Y, back.OriginCm.Y);
        Assert.Equal(ack.OriginCm.Z, back.OriginCm.Z);
        Assert.Equal(ack.Frame.X, back.Frame.X);
        Assert.Equal(ack.Frame.W, back.Frame.W);
        Assert.Equal(East, RegionKey.FromId(back.RegionId));
    }

    [Fact]
    public async Task 快照带回的边界cell与库里的筛子逐条一致()
    {
        var h = new Harness();
        await h.Bind(7, Home, 100);
        var snap = await h.Grain.SnapshotAsync(7, NoCaliber);

        var own = RegionKey.FromId(snap.OwnerRegionId);
        var expected = PlanetRegionAoi.BoundaryCellSubscriptions(
            own, h.Authority.Subscriptions(7));

        // 快照里的集合必须与库里筛出来的逐条相同——两套真相迟早会分叉，所以这里直接比集合
        var actual = snap.SubscribedBoundaryCells
            .Select(c => (RegionKey.FromId(c.RegionId), c.Cx, c.Cz))
            .OrderBy(t => t.Item1.Face).ThenBy(t => t.Item1.X).ThenBy(t => t.Item1.Y).ThenBy(t => t.Cx).ThenBy(t => t.Cz)
            .ToList();
        var want = expected
            .Select(c => (c.Region, c.Cx, c.Cz))
            .OrderBy(t => t.Item1.Face).ThenBy(t => t.Item1.X).ThenBy(t => t.Item1.Y).ThenBy(t => t.Cx).ThenBy(t => t.Cz)
            .ToList();
        Assert.Equal(want, actual);

        // 本区不列在边界格里（整块订阅），而且真的筛掉了大部分邻区
        Assert.DoesNotContain(actual, t => t.Item1 == own);
        Assert.True(actual.Count > 0, "邻区边界格一个都没列，这一层等于没接");
        int neighbourRegions = snap.SubscribedRegionIds.Length - 1;
        Assert.True(actual.Count < neighbourRegions * 78 * 78,
                    "列出的格子数已经超过整块邻区的量，筛子没起作用");

        // 非法角色号的快照：空数组而不是 null（客户端不必为"没数据"写两种分支）
        var none = await h.Grain.SnapshotAsync(0, NoCaliber);
        Assert.Empty(none.SubscribedBoundaryCells);

        // 字段编号只许追加。这一栏存在理由：快照的号一旦被复用，旧客户端会把"结论"读成"订阅集"，
        // 而那不会报错，只会让 AOI 静默变成空集。所以钉两层——老的六个号一个都没动，新的三个是追加。
        var orders = typeof(PlanetRegionSnapshot)
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p.Name, Order: p.GetCustomAttribute<MemoryPackOrderAttribute>()?.Order ?? -1))
            .Where(t => t.Order >= 0)
            .ToDictionary(t => t.Name, t => t.Order);
        Assert.Equal(5, orders["SubscribedBoundaryCells"]);                     // 原来那一格不许挪
        Assert.Equal(6, orders["Accepted"]);
        Assert.Equal(7, orders["Code"]);
        Assert.Equal(8, orders["Reason"]);
        Assert.Equal(Enumerable.Range(0, orders.Count), orders.Values.OrderBy(v => v));

        // Orleans 那一路同理：[Id] 也必须连续追加，否则跨 silo 读到的是错字段
        var ids = typeof(PlanetRegionSnapshot)
            .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<global::Orleans.IdAttribute>() is { } idAttr ? (int)idAttr.Id : -1)
            .Where(v => v >= 0).OrderBy(v => v).ToList();
        Assert.Equal(Enumerable.Range(0, ids.Count), ids);
    }

    [Fact]
    public void 两条新消息的MemoryPack往返保留逐位值_含union头()
    {
        var maxKey = new RegionKey(5, Level, (1 << Level) - 1, (1 << Level) - 1);
        var request = new PlanetRegionTransferRequest
        {
            CharacterId = (ulong)long.MaxValue,
            FromRegionId = maxKey.ToId(),
            ToRegionId = East.ToId(),
            GlobalXcm = long.MinValue + 7,
            GlobalYcm = Radius,
            GlobalZcm = -Radius,
            Tick = long.MaxValue,
        };
        var requestBack = MemoryPackSerializer.Deserialize<PlanetRegionTransferRequest>(
            MemoryPackSerializer.Serialize(request));
        Assert.Equal(request.CharacterId, requestBack.CharacterId);
        Assert.Equal(request.FromRegionId, requestBack.FromRegionId);
        Assert.Equal(request.ToRegionId, requestBack.ToRegionId);
        Assert.Equal(request.GlobalXcm, requestBack.GlobalXcm);
        Assert.Equal(request.GlobalYcm, requestBack.GlobalYcm);
        Assert.Equal(request.GlobalZcm, requestBack.GlobalZcm);
        Assert.Equal(request.Tick, requestBack.Tick);

        // union 头：走网关那条真实路径（多态基类）也要认得这两条消息
        var viaUnion = MemoryPackSerializer.Deserialize<MessageUnion>(MemoryPackSerializer.Serialize<MessageUnion>(request));
        var unionRequest = Assert.IsType<PlanetRegionTransferRequest>(viaUnion);
        Assert.Equal(request.FromRegionId, unionRequest.FromRegionId);
        Assert.Equal(MessageType.PlanetRegionTransfer, unionRequest.Type);

        foreach (var code in Enum.GetValues<PlanetRegionRejectCode>())
        {
            var result = new PlanetRegionTransferResult
            {
                CharacterId = 7,
                Accepted = code == PlanetRegionRejectCode.None,
                Applied = false,
                Code = code,
                Reason = "理由 " + (int)code,
                NewRegionId = maxKey.ToId(),
                HandoverTick = -5,
                ServerTick = long.MaxValue,
            };
            var back = MemoryPackSerializer.Deserialize<PlanetRegionTransferResult>(
                MemoryPackSerializer.Serialize(result));
            Assert.Equal(code, back.Code);
            Assert.Equal(result.Reason, back.Reason);
            Assert.Equal(result.NewRegionId, back.NewRegionId);
            Assert.Equal(result.HandoverTick, back.HandoverTick);
            Assert.Equal(result.ServerTick, back.ServerTick);
            Assert.Equal(result.Accepted, back.Accepted);

            var backViaUnion = MemoryPackSerializer.Deserialize<MessageUnion>(
                MemoryPackSerializer.Serialize<MessageUnion>(result));
            var typed = Assert.IsType<PlanetRegionTransferResult>(backViaUnion);
            Assert.Equal(MessageType.PlanetRegionTransferResult, typed.Type);
            Assert.Equal(code, typed.Code);
        }

        var snapshot = new PlanetRegionSnapshot
        {
            CharacterId = (ulong)long.MaxValue,
            OwnerRegionId = maxKey.ToId(),
            SubscribedRegionIds = new[] { Home.ToId(), East.ToId(), Far.ToId() },
            VisibleCharacterIds = new ulong[] { 1, (ulong)long.MaxValue },
            ServerTick = 12345,
            SubscribedBoundaryCells = new[]
            {
                new PlanetCellKey { RegionId = maxKey.ToId(), Cx = -1, Cz = int.MinValue },
                new PlanetCellKey { RegionId = East.ToId(), Cx = 17, Cz = -42 },
            },
        };
        var snapshotBack = MemoryPackSerializer.Deserialize<PlanetRegionSnapshot>(
            MemoryPackSerializer.Serialize(snapshot));
        Assert.Equal(snapshot.CharacterId, snapshotBack.CharacterId);
        Assert.Equal(snapshot.OwnerRegionId, snapshotBack.OwnerRegionId);
        Assert.Equal(snapshot.SubscribedRegionIds, snapshotBack.SubscribedRegionIds);
        Assert.Equal(snapshot.VisibleCharacterIds, snapshotBack.VisibleCharacterIds);
        Assert.Equal(snapshot.ServerTick, snapshotBack.ServerTick);
        // cell 下标是带符号的，负号与 int 两端都必须活着回来
        Assert.Equal(snapshot.SubscribedBoundaryCells.Length, snapshotBack.SubscribedBoundaryCells.Length);
        for (int i = 0; i < snapshot.SubscribedBoundaryCells.Length; i++)
        {
            Assert.Equal(snapshot.SubscribedBoundaryCells[i].RegionId, snapshotBack.SubscribedBoundaryCells[i].RegionId);
            Assert.Equal(snapshot.SubscribedBoundaryCells[i].Cx, snapshotBack.SubscribedBoundaryCells[i].Cx);
            Assert.Equal(snapshot.SubscribedBoundaryCells[i].Cz, snapshotBack.SubscribedBoundaryCells[i].Cz);
        }
    }

    [Fact]
    public void 存档类型有真正生成的Orleans序列化器()
    {
        // [GenerateSerializer] 没生效时编译照样通过，直到第一次读存档才炸——所以要把"生成器确实产出了类型"钉住
        var generated = typeof(PlanetRegionGrain).Assembly.GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .Where(n => n.Contains("PlanetRegionPersistedState") || n.Contains("PlanetRegionEntityRecord"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(generated.Count > 0,
            "没找到存档类型的生成器；可用类型样本：" + string.Join(", ",
                typeof(PlanetRegionGrain).Assembly.GetTypes().Select(t => t.Name).Where(n => n.EndsWith("_Serializer") || n.EndsWith("_Metadata")).Take(5)));
    }

    [Fact]
    public async Task 真实silo里能激活并跑完落位_迁移_快照()
    {
        // 裸激活覆盖了判定，但"grain 能被 Orleans 发现、构造参数能注入、DTO 能过序列化"只有真集群才算数
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<ZoneShardTestSiloConfigurator>();
        builder.AddClientBuilderConfigurator<ZoneShardTestClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        try
        {
            var grain = cluster.GrainFactory.GetGrain<IPlanetRegionGrain>(1);
            Assert.True(await grain.BindAsync(7, SurfaceEcef(Home).X, SurfaceEcef(Home).Y, SurfaceEcef(Home).Z, 100, NoCaliber));

            var result = await grain.TransferAsync(Req(7, Home, East, Surface(East), 120));
            Assert.True(result.Accepted, result.Reason);
            Assert.True(result.Applied);
            Assert.Equal(East.ToId(), result.NewRegionId);

            var snap = await grain.SnapshotAsync(7, NoCaliber);
            Assert.Equal(East.ToId(), snap.OwnerRegionId);
            Assert.Contains(Home.ToId(), snap.SubscribedRegionIds);

            // 倒退的 tick 要拒。注意 from 得写"当前归属"East：几何与 from 归属都过了才轮到 tick 判定，
            // 照抄上一条的 Home 会先撞上 OwnerMismatch，那就不是这条用例在验的东西了
            var regressed = await grain.TransferAsync(Req(7, East, Home, Surface(Home), 110));
            Assert.False(regressed.Accepted);
            Assert.Equal(PlanetRegionRejectCode.TickRegression, regressed.Code);
            Assert.Equal(East.ToId(), regressed.NewRegionId);        // 被拒时回的是真实归属，没被拉回去

            // 推进时钟跨过了重叠窗口：旧区仍在订阅集里（来源是共边邻居），但重叠账上已经没有它了
            await grain.AdvanceAsync(200);
            var late = await grain.SnapshotAsync(7, NoCaliber);
            Assert.Equal(200, late.ServerTick);
            Assert.Contains(Home.ToId(), late.SubscribedRegionIds);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            cluster.Dispose();
        }
    }
    [Fact]
    public async Task P1换区往返按驱动那一侧的取法走通一次()
    {
        // 这条判据要钉的不是"grain 会受理换区"（那几条早就验过），而是<em>客户端那一侧取样的一次顺序</em>：
        // PlanetNearFieldDriver.CompleteSwap 必须把"旧区域号 + 换完之后所在的全局点"交给规则层。
        // 取错的两种形状都在实机上没有异常：
        //   ① from 在 _frame 被替换之后才取 → from==to，规则层回"还没真的走出当前区域"，一条都不发；
        //   ② 全局点用旧帧算 → 反查回来的还是旧区域，同样是静默不发。
        // 所以这里按驱动的真实取法算一遍，再要求服务端真的 Applied、真的回带新坐标系。
        var h = new Harness();
        Assert.True(await h.Bind(7, Home, 100));

        // —— 与 CompleteSwap 同样的三步：旧帧号、pendingFrame + pendingLocal 还原出的全局点 ——
        RegionKey fromKey = Home;                                             // 必须在换之前取
        var pendingFrame = RegionFrame.Build(East, Radius);
        var pendingLocal = pendingFrame.LocalFromGlobalCm(Surface(East));
        var crossedGlobalCm = pendingFrame.GlobalFromLocalCm(pendingLocal);

        // 夹具自证：换完之后再算回来的全局点，确实落在新区（否则后面那句 FromRegionId 是蒙对的）
        Assert.Equal(East, RegionKey.FromDirection(crossedGlobalCm / crossedGlobalCm.Length, Level));

        var caliber = new PlanetHandshake
        {
            WireProtocol = PlanetHandshake.ProtocolVersion,
            PlanetRadiusCm = Radius,
            RegionLevel = (byte)Level,
            TerrainSeed = unchecked((long)0x123456789ABCDEF0UL),
            TerrainVersion = 0xA5A51234,
            BasefieldVersion = 1,
            BasefieldHashHi = unchecked((long)0xFEDCBA9876543210UL),
        };
        Assert.True(HundunWorld.Game.Planet.PlanetRegionTransferRules.TryBuild(
            7, fromKey, EcefCentimeters.FromVector(crossedGlobalCm).ToVector(), 101, in caliber,
            out var built, out string why), why);
        Assert.Equal(Home.ToId(), built.FromRegionId);
        Assert.Equal(East.ToId(), built.ToRegionId);

        // 反证①：from 取成换完之后的那个号，整条链就不发了——这就是驱动里"先取号再换帧"的理由
        Assert.False(HundunWorld.Game.Planet.PlanetRegionTransferRules.TryBuild(
            7, East, EcefCentimeters.FromVector(crossedGlobalCm).ToVector(), 102, in caliber,
            out _, out string lateFrom));
        Assert.Contains("还没真的走出当前区域", lateFrom, StringComparison.Ordinal);

        // —— 送进 grain，按线上那条消息的字段一比一搬 ——
        var ecef = EcefCentimeters.FromVector(crossedGlobalCm);
        var bytes = new byte[PlanetHandshake.Size];
        Assert.True(PlanetHandshake.TryWrite(bytes, caliber, out int written));
        Assert.Equal(PlanetHandshake.Size, written);
        var receipt = await h.Grain.TransferAsync(new PlanetRegionTransferRequest
        {
            CharacterId = built.CharacterId,
            FromRegionId = built.FromRegionId,
            ToRegionId = built.ToRegionId,
            GlobalXcm = ecef.X,
            GlobalYcm = ecef.Y,
            GlobalZcm = ecef.Z,
            Tick = built.Tick,
            PlanetCaliber = bytes,
        });

        Assert.True(receipt.Accepted, receipt.Reason);
        Assert.True(receipt.Applied);
        Assert.Equal(East.ToId(), receipt.NewRegionId);
        // §6.2 那条 ack 必须真的带了新坐标系：全 0 的 ack 与"没有 ack"在客户端不可分辨
        Assert.NotEqual(0L, receipt.OriginXcm);
        Assert.True(receipt.FrameX != 0 || receipt.FrameY != 0 || receipt.FrameZ != 0 || receipt.FrameW != 0,
            "四元数全 0 不是合法姿态");

        // 服务端给的原点与姿态，客户端拿它重建的帧必须能把自己放回同一个点：
        // 往返差得越多，换区之后脚底下的地面就偏得越多，而这条路上没有任何报错
        var origin = new PlanetVector3(receipt.OriginXcm, receipt.OriginYcm, receipt.OriginZcm);
        Assert.True(Math.Abs(origin.Length - Radius) < 1.0,
            $"区域原点应当在海面上，实际偏差 {origin.Length - Radius:0.###} cm");

        // —— 回执处置：我们本地的信念已经是 East，服务端回的也是 East ⇒ 一次对账成功，不动归属 ——
        Assert.Equal(HundunWorld.Game.Planet.PlanetRegionTransferRules.ReceiptAction.KeepAndConfirm,
            HundunWorld.Game.Planet.PlanetRegionTransferRules.Decide(
                hasReceipt: true, receipt.Accepted, receipt.Applied, (int)receipt.Code,
                receipt.NewRegionId, East));
        // 而如果客户端还没换基（信念仍在 Home），同一条回执就该让它换——这条是"半路收到别的客户端的账"的形状
        Assert.Equal(HundunWorld.Game.Planet.PlanetRegionTransferRules.ReceiptAction.AdoptServerRegion,
            HundunWorld.Game.Planet.PlanetRegionTransferRules.Decide(
                hasReceipt: true, receipt.Accepted, receipt.Applied, (int)receipt.Code,
                receipt.NewRegionId, Home));

        Assert.Equal(East.ToId(), h.Authority.OwnerOf(7)!.Value.ToId());
        Assert.Equal(2, h.Writes);                      // 落盘两次：落位一次、这次换区一次（时钟推进不该再写）
    }

    /// <summary>仓库根下的一个源码文件（与 <c>PlanetRegionCaliberGateTests</c> 同一手法：从输出目录往上找）。</summary>
    private static string RepoSource(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Horizon.Planet", "Horizon.Planet.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(new[] { dir!.FullName }.Concat(segments).ToArray());
    }
}
