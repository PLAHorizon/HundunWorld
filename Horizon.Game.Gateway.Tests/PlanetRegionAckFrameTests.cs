using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Grains;
using Horizon.Planet;
using Horizon.Planet.Wire;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// ack 里那套新坐标系的解回与对账（§6.3 第 3 步）。
/// <para>此前服务端把原点与姿态发出去了，但<em>两侧都没有把它解回去用过</em>——客户端换基是靠全局点自己
/// 反查的（那是 §13.15 的结论，没错）。"发出去没人读"的意思是：那一半协议错不错，没有任何地方会知道。
/// 这批判据把它变成一个交叉检查，并把量化误差本身量出来（它不是零，而它到底是多少决定了两端该信谁）。</para>
/// </summary>
public class PlanetRegionAckFrameTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;

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

    private static readonly RegionKey[] Probes =
    {
        new(2, Level, 8192, 8192),            // 赤道带的一般面
        new(0, Level, 3, 100),                // 另一个面，靠近 uv 原点
        new(4, Level, 500, 500),              // ±Z 极地面之一（§13.47 那里东向是奇异的）
        new(5, Level, (1 << Level) - 4, (1 << Level) - 4),
    };

    private static PlanetRegionTransferResult AckFor(RegionKey key, PlanetVector3 globalCm)
    {
        var frame = RegionFrame.Build(key, Radius);
        var ack = new PlanetRegionTransferResult
        {
            CharacterId = 7,
            Accepted = true,
            Applied = true,
            Code = PlanetRegionRejectCode.None,
            NewRegionId = key.ToId(),
        };
        PlanetRegionGrain.FillAck(ack, frame, globalCm);
        return ack;
    }

    [Fact]
    public void 探针块自己可信_中心点反查回自己且帧是右手正交基()
    {
        // 下面所有判据都建立在这批夹具之上；夹具自己不可信时，"解回一致"是蒙出来的
        foreach (var key in Probes)
        {
            var center = key.CenterDirection();
            Assert.Equal(key, RegionKey.FromDirection(center * Radius, Level));
            var f = RegionFrame.Build(key, Radius);
            Assert.True(Math.Abs(PlanetVector3.Dot(f.East, f.Up)) < 1e-12, $"{key} 东·上");
            Assert.True(Math.Abs(PlanetVector3.Dot(f.East, f.South)) < 1e-12, $"{key} 东·南");
            Assert.True(Math.Abs(PlanetVector3.Dot(f.Up, f.South)) < 1e-12, $"{key} 上·南");
            // 右手：东 × 上 = 南（RegionFrame 的约定；不是这个就是左手系，四元数会转反）
            PlanetVector3 cross = PlanetVector3.Cross(f.East, f.Up);
            Assert.True(Math.Abs(PlanetVector3.Dot(cross, f.South) - 1.0) < 1e-9, $"{key} 手性");
        }
    }

    [Fact]
    public void ack换算回全局的误差随局部距离放大_界按相对量给()
    {
        // 姿态是 Q15 的，残余角误差是个<em>角度</em>，所以位置误差正比于"离原点有多远"：
        // 站在地面上它是厘米级，飞到 50 km 高就是一米级。用绝对厘米数设界会把这条规律藏起来，
        // 所以分两档：贴地（ gameplay 真正依赖的那一档）给绝对界，高空给相对界。
        double worstSurface = 0, worstAir = 0;
        string whereSurface = "", whereAir = "";
        foreach (var key in Probes)
        {
            var frame = RegionFrame.Build(key, Radius);
            var (halfX, halfZ) = PlanetRegionAoi.LocalHalfExtents(key, Radius);
            var locals = new (PlanetVector3 Local, bool Air)[]
            {
                (new PlanetVector3(0, 0, 0), false),
                (new PlanetVector3(halfX, 0, halfZ), false),
                (new PlanetVector3(-halfX, 0, -halfZ), false),
                (new PlanetVector3(halfX, 0, -halfZ), false),
                (new PlanetVector3(-halfX, 0, halfZ), false),
                (new PlanetVector3(0, 5_000_000, 0), true),          // 离地 50 km（线上局部口径 ±100 km）
            };
            foreach (var (local, air) in locals)
            {
                var global = frame.GlobalFromLocalCm(local);
                var ack = AckFor(key, global);
                Assert.True(PlanetRegionAckFrame.TryReconstruct(ack, in Caliber, out var back, out string why),
                            $"{key} @ {local}: {why}");
                double dev = PlanetRegionAckFrame.DeviationCm(back, PlanetRegionAckFrame.LocalFromAck(ack), global);
                if (air)
                {
                    double relative = dev / local.Length;
                    if (relative > worstAir) { worstAir = relative; whereAir = $"{key} 高 {local.Y / 100000.0:0.#} km"; }
                }
                else if (dev > worstSurface) { worstSurface = dev; whereSurface = $"{key} 局部 {local.X:0},{local.Z:0}"; }
            }
        }
        // 贴地：量化后的局部厘米本身就有 ±0.5 cm/轴，加上姿态残余角在 ~3e5 cm 上的放大
        Assert.True(worstSurface < SurfaceBoundCm,
            $"贴地最差 {worstSurface:0.###} cm（{whereSurface}）超出 {SurfaceBoundCm} cm");
        // 高空：相对量就是那个残余角（实测 ~3.5e-5 rad），界取 5e-5
        Assert.True(worstAir < 5e-5,
            $"高空相对偏差 {worstAir:E3}（{whereAir}）超出 5e-5：那姿态误差已经不是量化而是约定错了");
    }

    /// <summary>贴地的绝对界：判据实测出来的值放一倍余量。改它要同时改 §13.54 里那两个数。</summary>
    private const double SurfaceBoundCm = 25.0;


    [Fact]
    public void 服务端发的姿态与客户端自己算的帧是同一个()
    {
        foreach (var key in Probes)
        {
            var client = RegionFrame.Build(key, Radius);
            var ack = AckFor(key, client.GlobalFromLocalCm(new PlanetVector3(1000, 0, -2000)));
            Assert.True(PlanetRegionAckFrame.TryReconstruct(ack, in Caliber, out var back, out string why), why);

            double signed = PlanetRegionAckFrame.SignedAxisDeviationDeg(back, client);
            Assert.True(signed < 0.01, $"{key}：轴夹角 {signed:E3}° —— 两端对同一块地面的切平面有两种说法");
        }
    }

    [Fact]
    public void 轴搞混与轴反号分得开_两种都得报但别混成一类()
    {
        var key = Generic;
        var frame = RegionFrame.Build(key, Radius);
        var global = frame.GlobalFromLocalCm(new PlanetVector3(0, 0, 0));
        var good = AckFor(key, global);
        Assert.True(PlanetRegionAckFrame.TryReconstruct(good, in Caliber, out var back, out _));

        // ① 局部 X 轴被当成"上"发出去（列序写反）。选循环置换而不是任意对调：
        //    (东,上,南) → (上,南,东) 仍是右手系，FromBasis 不会拒，所以它是"线上真能出现"的错。
        var swapped = QuaternionQuantized.FromBasis(frame.Up, frame.South, frame.East);
        var confused = good;
        confused.FrameX = swapped.X; confused.FrameY = swapped.Y;
        confused.FrameZ = swapped.Z; confused.FrameW = swapped.W;
        Assert.True(PlanetRegionAckFrame.TryReconstruct(confused, in Caliber, out var permuted, out string swapWhy),
                    "换列不该让四元数变得非法：它确实是一个合法旋转。解不开说明这条判据在验别的东西：" + swapWhy);
        Assert.True(PlanetRegionAckFrame.AxisDeviationDeg(permuted, frame) > 80.0,
                    "列序错了却没被夹角抓出来：那 AxisDeviationDeg 是空的");

        // ② 共轭（q 与 q⁻¹ 写反）：仍是合法单位四元数、解出来仍是正交单位轴，自洽检查全过，
        //    只有跟客户端的帧一比才露馅——这就是必须有交叉检查的理由。
        //    （左手系那种压根发不出来：FromBasis 会当场拒，下面单独钉一条。）
        var conjugate = good;
        conjugate.FrameX = (short)-good.FrameX;
        conjugate.FrameY = (short)-good.FrameY;
        conjugate.FrameZ = (short)-good.FrameZ;
        Assert.True(PlanetRegionAckFrame.TryReconstruct(conjugate, in Caliber, out var flipped, out string conjWhy),
                    "共轭四元数是合法旋转，解算层不该替判定层做主：" + conjWhy);
        double conjDeg = PlanetRegionAckFrame.SignedAxisDeviationDeg(flipped, frame);
        Assert.True(conjDeg > 1.0, $"共轭写反却只差了 {conjDeg:E3}°：这条判据抓不到东西");

        // ③ "把上轴朝下发"：绕东轴转 180° 是一个<em>合法</em>旋转（东→东、上→−上、南→−南），
        //    所以取绝对值那条看不见它（夹角是 0°），只有带符号的那条报 180°。
        //    这一条存在的理由是：没有它，SignedAxisDeviationDeg 换成 abs 版本也不会红——
        //    而两个 helper 的差别就变成纸面上的。
        var halfTurnAboutEast = QuaternionQuantized.FromBasis(frame.East, frame.Up * -1.0, frame.South * -1.0);
        var flippedUp = good;
        flippedUp.FrameX = halfTurnAboutEast.X; flippedUp.FrameY = halfTurnAboutEast.Y;
        flippedUp.FrameZ = halfTurnAboutEast.Z; flippedUp.FrameW = halfTurnAboutEast.W;
        Assert.True(PlanetRegionAckFrame.TryReconstruct(flippedUp, in Caliber, out var upsideDown, out string flipWhy),
                    "绕东轴的 180° 是合法旋转，解算层不该替判定层做主：" + flipWhy);
        Assert.True(PlanetRegionAckFrame.AxisDeviationDeg(upsideDown, frame) < 0.01,
                    "取绝对值那条应当<em>看不见</em>这个反号（这正是它存在的原因，不是它的漏洞）");
        Assert.True(PlanetRegionAckFrame.SignedAxisDeviationDeg(upsideDown, frame) > 170.0,
                    "反号没被带符号的那条抓到：两个 helper 其实是同一个");

        // 生产者这一侧的底线：左手系根本造不出来
        Assert.Throws<ArgumentException>(() =>
            QuaternionQuantized.FromBasis(frame.East, frame.Up, frame.South * -1.0));

        // 正向对照：原件两条都过关
        Assert.True(PlanetRegionAckFrame.AxisDeviationDeg(back, frame) < 0.01);
        Assert.True(PlanetRegionAckFrame.SignedAxisDeviationDeg(back, frame) < 0.01);
    }

    [Fact]
    public void 库里那条ack结构是活的_生产造的过自己的编解码并与DTO逐字段相同()
    {
        // §6.2 的协议里有一条 RegionTransferAck，而实现里此前只有 DTO 那一份形状——库里那个结构体
        // 谁都不建，等于"协议里一条、实现里另一条"。FillAck 现在先建库结构再镜像进 DTO，
        // 这条判据钉的就是那份同源：两边逐字段必须相同，并且库结构自己的编解码要能把它完整带走。
        var key = Probes[1];
        var frame = RegionFrame.Build(key, Radius);
        var global = frame.GlobalFromLocalCm(new PlanetVector3(1234, 0, -5678));

        var ack = PlanetRegionGrain.BuildAck(frame, global);
        var dto = AckFor(key, global);                       // 走 FillAck

        Assert.Equal(key.ToId(), ack.RegionId);
        Assert.Equal(dto.LocalXCm, ack.LocalXCm);
        Assert.Equal(dto.LocalYCm, ack.LocalYCm);
        Assert.Equal(dto.LocalZCm, ack.LocalZCm);
        Assert.Equal(dto.OriginXcm, ack.OriginCm.X);
        Assert.Equal(dto.OriginYcm, ack.OriginCm.Y);
        Assert.Equal(dto.OriginZcm, ack.OriginCm.Z);
        Assert.Equal(dto.FrameX, ack.Frame.X);
        Assert.Equal(dto.FrameY, ack.Frame.Y);
        Assert.Equal(dto.FrameZ, ack.Frame.Z);
        Assert.Equal(dto.FrameW, ack.Frame.W);

        var bytes = new byte[RegionTransferAck.Size];
        Assert.True(RegionTransferAck.TryWrite(bytes, ack, out int written));
        Assert.Equal(RegionTransferAck.Size, written);
        Assert.True(RegionTransferAck.TryRead(bytes, out var back, out var status, out int consumed));
        Assert.Equal(PlanetWireStatus.Ok, status);
        Assert.Equal(RegionTransferAck.Size, consumed);
        Assert.Equal(ack.OriginCm, back.OriginCm);

        // 带走之后还得是<em>同一个帧</em>：字段对了但转不出原方向，等于带了一包装饰
        Assert.True(Math.Abs(back.OriginCm.ToVector().Length - Radius) < 1.0);
        PlanetVector3 east = back.Frame.Rotate(PlanetVector3.AxisX);
        PlanetVector3 up = back.Frame.Rotate(PlanetVector3.AxisY);
        PlanetVector3 south = back.Frame.Rotate(PlanetVector3.AxisZ);
        Assert.True(PlanetVector3.Dot(east, frame.East) > 1.0 - 1e-6, "东轴过一趟线偏了");
        Assert.True(PlanetVector3.Dot(up, frame.Up) > 1.0 - 1e-6, "上轴过一趟线偏了");
        Assert.True(PlanetVector3.Dot(south, frame.South) > 1.0 - 1e-6, "南轴过一趟线偏了");

        // 局部厘米按 ack 换算回去，仍然要落在原来那个点上（判据 13.54 的界）
        double dev = PlanetRegionAckFrame.DeviationCm(
            new PlanetRegionAckFrame.Reconstructed(east, up, south, back.OriginCm.ToVector()),
            new PlanetVector3(back.LocalXCm, back.LocalYCm, back.LocalZCm), global);
        Assert.True(dev < SurfaceBoundCm, $"库结构往返之后位置差 {dev:0.####} cm");
    }

    [Fact]
    public void 不该被解开的ack一律拒_每种坏法各有各的说法()
    {
        var key = Generic;
        var frame = RegionFrame.Build(key, Radius);
        var global = frame.GlobalFromLocalCm(new PlanetVector3(0, 0, 0));

        var cases = new List<(string Label, Func<PlanetRegionTransferResult> Build, string Expect)>
        {
            ("未生效", () => { var a = AckFor(key, global); a.Applied = false; return a; }, "未生效"),
            // Applied 保持 true：这才是"自相矛盾的 ack"——说生效了又带着拒绝码，
            // 而 Applied=false 那一支上面一条已经覆盖了，两条要各判各的
            ("带拒绝码", () => { var a = AckFor(key, global); a.Code = PlanetRegionRejectCode.NotAdjacent; return a; },
             "拒绝码"),
            ("归属为0", () => { var a = AckFor(key, global); a.NewRegionId = 0; return a; }, "归属是 0"),
            ("四元数全0", () => { var a = AckFor(key, global); a.FrameX = 0; a.FrameY = 0; a.FrameZ = 0; a.FrameW = 0;
                                  return a; }, "退化"),
            ("四元数没归一", () => { var a = AckFor(key, global);   // 四个分量一起放大：只动一个可能撞上 0，等于没动
                                a.FrameX = (short)(a.FrameX * 12 / 10); a.FrameY = (short)(a.FrameY * 12 / 10);
                                a.FrameZ = (short)(a.FrameZ * 12 / 10); a.FrameW = (short)(a.FrameW * 12 / 10);
                                return a; }, "没归一"),
            ("原点在地心", () => { var a = AckFor(key, global); a.OriginXcm = 0; a.OriginYcm = 0; a.OriginZcm = 0;
                                   return a; }, "退化"),
            ("原点是别的星球", () => { var a = AckFor(key, global);
                                       var shrink = (double)PlanetConstants.EarthMeanRadiusCm / Radius;   // 别写成整数除法：那得到 0
                                       a.OriginXcm = (long)(a.OriginXcm * shrink);
                                       a.OriginYcm = (long)(a.OriginYcm * shrink);
                                       a.OriginZcm = (long)(a.OriginZcm * shrink);
                                       return a; }, "离海面"),
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, build, expect) in cases)
        {
            Assert.False(PlanetRegionAckFrame.TryReconstruct(build(), in Caliber, out var frame2, out string why), label);
            Assert.Contains(expect, why, StringComparison.Ordinal);
            Assert.Equal(0, frame2.OriginCm.X + frame2.OriginCm.Y + frame2.OriginCm.Z);   // 失败时不许留半套
            Assert.True(seen.Add(why), $"{label} 的理由与别的坏法撞车：{why}");
        }
        Assert.Equal(cases.Count, seen.Count);

        // 正向孪生：原件能解开（上面不是因为"永远解不开"而绿）
        Assert.True(PlanetRegionAckFrame.TryReconstruct(AckFor(key, global), in Caliber, out _, out string ok), ok);

        // null 不许抛
        Assert.False(PlanetRegionAckFrame.TryReconstruct(null, in Caliber, out _, out string nullWhy));
        Assert.Contains("ack 为空", nullWhy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 走一趟真grain的ack解得开_而且是同一条填充代码()
    {
        // 上面的夹具直接调 FillAck；这条走完整的 TransferAsync，钉住"判据里验的填充函数
        // 就是活路径上那个"，而不是另一份长得一样的代码。
        var h = new PlanetRegionCaliberRig();
        var home = EcefCentimeters.FromVector(Probes[0].CenterDirection() * Radius);
        Assert.True(await h.Grain.BindAsync(7, home.X, home.Y, home.Z, 100, h.CaliberBytes));

        var east = Neighbour(Probes[0], RegionEdge.UPlus);
        var eastEcef = EcefCentimeters.FromVector(east.CenterDirection() * Radius);
        var receipt = await h.Grain.TransferAsync(new PlanetRegionTransferRequest
        {
            CharacterId = 7,
            FromRegionId = Probes[0].ToId(),
            ToRegionId = east.ToId(),
            GlobalXcm = eastEcef.X,
            GlobalYcm = eastEcef.Y,
            GlobalZcm = eastEcef.Z,
            Tick = 101,
            PlanetCaliber = h.CaliberBytes,
        });
        Assert.True(receipt.Applied, receipt.Reason);

        Assert.True(PlanetRegionAckFrame.TryReconstruct(receipt, in Caliber, out var back, out string why), why);
        var client = RegionFrame.Build(east, Radius);
        Assert.True(PlanetRegionAckFrame.SignedAxisDeviationDeg(back, client) < 0.01,
                    $"真回执与客户端的帧差了 {PlanetRegionAckFrame.SignedAxisDeviationDeg(back, client):E3}°");

        // 客户端照 §6.3 第 3 步换算回去的位置，必须就是它自己发出去的那个点
        double dev = PlanetRegionAckFrame.DeviationCm(back, PlanetRegionAckFrame.LocalFromAck(receipt),
                                                     eastEcef.ToVector());
        Assert.True(dev < SurfaceBoundCm, $"真回执的换算偏差 {dev:0.####} cm 超出 {SurfaceBoundCm} cm");
    }

    /// <summary>一个姿态"通用"的块：四元数四个分量都不为 0，也不是 180° 的特例。
    /// 形状类检查（共轭、列序）必须用它——180° 姿态下 q 与 −q 表示同一个旋转，看不出反号。</summary>
    private static readonly RegionKey Generic = new(0, Level, 3, 100);

    private static RegionKey Neighbour(RegionKey key, RegionEdge edge) =>
        PlanetRegions.TryNeighbour(key, edge, out var n)
            ? n
            : throw new InvalidOperationException($"{key} 沿 {edge} 没有邻居");
}

/// <summary>
/// 给 ack 判据用的最小 grain 夹具。<b>放在这里而不是复用别的文件</b>：
/// 那几组的 Harness 各自能红，共用一份会让 ack 判据跟着别人的构造改动一起失效。
/// </summary>
internal sealed class PlanetRegionCaliberRig
{
    public PlanetRegionCaliberRig()
    {
        var state = new PlanetRegionPersistedState();
        var mock = new Moq.Mock<global::Orleans.Runtime.IPersistentState<PlanetRegionPersistedState>>();
        mock.SetupGet(s => s.State).Returns(state);
        mock.Setup(s => s.WriteStateAsync()).Returns(Task.CompletedTask);
        Grain = new PlanetRegionGrain(
            new Moq.Mock<Microsoft.Extensions.Logging.ILogger<PlanetRegionGrain>>().Object, mock.Object);
        GrainTestHarness.AttachGrainContext(Grain, "PlanetRegion", "1");

        var caliber = new PlanetHandshake
        {
            WireProtocol = PlanetHandshake.ProtocolVersion,
            PlanetRadiusCm = PlanetConstants.PlanetRadiusCm,
            RegionLevel = (byte)PlanetConstants.DefaultRegionLevel,
            TerrainSeed = unchecked((long)0x123456789ABCDEF0UL),
            TerrainVersion = 0xA5A51234,
            BasefieldVersion = 1,
            BasefieldHashHi = unchecked((long)0xFEDCBA9876543210UL),
        };
        var bytes = new byte[PlanetHandshake.Size];
        System.Diagnostics.Debug.Assert(PlanetHandshake.TryWrite(bytes, caliber, out _));
        CaliberBytes = bytes;
    }

    public PlanetRegionGrain Grain { get; }
    public byte[] CaliberBytes { get; }
}
