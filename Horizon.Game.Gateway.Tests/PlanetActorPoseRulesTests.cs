using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Horizon.Planet;
using HundunWorld.Game.Planet;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 角色位姿 → 星球局部帧的口径判据。
/// <para>要防的是一件今天还没发生、但一旦发生就完全静默的事：把角色的位置按<em>错误的轴序</em>
/// 塞进 <see cref="RegionFrame"/>。本仓库里"换 Y/Z、不缩放"这件事已经有六处各写一遍
/// （网络同步、GASP 上行、DS 世界会话…），而星球这套局部帧是第三种顺序（X=东、Y=上、Z=南）。
/// 塞错的后果不是报错：角色的离地高度会变成"把深度当高度"，公里级，
/// 于是换区上行带着一个地下/空中的点出门，而屏幕上什么都没有。</para>
/// <para>这一层的立场是<em>不猜</em>：两种候选各算一遍离地高度，落在站立带里的那一个才有资格被用；
/// 两个都在带里报 Ambiguous，两个都不在报 Neither（今天场景是平的 C1，真实结果就是这一档）。
/// 所以下面每条判定都要配一个"反过来也成立"的孪生，否则"永远说不能判"的实现也是绿的。</para>
/// </summary>
public class PlanetActorPoseRulesTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;
    private static readonly RegionKey Home = new(2, Level, 8192, 8192);     // 非极轴面：东向不退化

    private static RegionFrame Frame(long radiusCm = Radius) => RegionFrame.Build(Home, radiusCm);

    private static void CloseTo(double expected, double actual, double tol, string what) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"{what}：期望 {expected:0.####} 实得 {actual:0.####}（容差 {tol}）");

    // ==================== 置换本身的形状 ====================

    [Fact]
    public void 两种口径把同一份三元数放进不同的轴()
    {
        var keep = PlanetActorPoseRules.ToLocalCm(100, 200, 300, PlanetActorPoseRules.AxisConvention.FlaxYUp);
        Assert.Equal(100.0, keep.X); Assert.Equal(200.0, keep.Y); Assert.Equal(300.0, keep.Z);

        // 仓里明写的那条映射：Flax.Y → ECS.Z、Flax.Z → ECS.Y（HundunWorldGame.cs:1565）
        var swapped = PlanetActorPoseRules.ToLocalCm(100, 200, 300, PlanetActorPoseRules.AxisConvention.EcsZUp);
        Assert.Equal(100.0, swapped.X); Assert.Equal(300.0, swapped.Y); Assert.Equal(200.0, swapped.Z);
        Assert.NotEqual(keep.Y, swapped.Y);       // 两档必须真的不同：相同就说明置换没生效
    }

    [Fact]
    public void 帧的第二个槽就是上_所以身份映射的槽二直接是离地高度()
    {
        // 这条钉的是 FlaxYUp 那一档的前提：局部帧 (X,Y,Z) = (东,上,南)。
        // 如果哪天 RegionFrame 换了轴序，这里会先红，而不是让角色安静地浮起来。
        var f = Frame();
        double alt = PlanetActorPoseRules.AltitudeAboveGroundCm(new PlanetVector3(0, 500, 0), f, 0.0);
        CloseTo(500.0, alt, 1e-3, "槽二=上");     // 容差按这个量级上的 double 分辨率给（半径 6.371e9 cm）
        Assert.Equal(Home.ToId(), PlanetActorPoseRules.RegionOf(new PlanetVector3(0, 500, 0), f));
    }

    [Fact]
    public void 半径取帧里的数而不是全局常量()
    {
        // 混用两个半径不会报错，只会让离地高度整体偏掉一个常数。
        // 判法：切平面在水平距离 d 处<em>高出</em>球面 d²/2R（sag 是正号），把半径换成地球那份要大约 10 倍。
        var triple = new PlanetVector3(120_000, 0, -80_000);
        double onPlanet = PlanetActorPoseRules.AltitudeAboveGroundCm(triple, Frame(Radius), 0.0);
        double onEarth = PlanetActorPoseRules.AltitudeAboveGroundCm(triple, Frame(PlanetConstants.EarthMeanRadiusCm), 0.0);
        Assert.True(onPlanet > 0 && onEarth > 0, $"切平面上的点该在球面<em>上方</em>：{onPlanet} / {onEarth}");
        Assert.True(onEarth > 5 * onPlanet,
            $"地球半径那份的下垂没有比 10× 星球那份大 5 倍以上，说明半径没从帧里取：{onPlanet:0.###} vs {onEarth:0.###}");
    }

    [Fact]
    public void 地面海拔进了算式_同一位置换地面就该跟着变()
    {
        var f = Frame();
        var local = new PlanetVector3(0, 150, 0);
        double seaLevel = PlanetActorPoseRules.AltitudeAboveGroundCm(local, f, 0.0);
        double highGround = PlanetActorPoseRules.AltitudeAboveGroundCm(local, f, 1000.0);
        CloseTo(seaLevel - 1000.0, highGround, 1e-3, "地面海拔");
    }

    // ==================== 判定：四个出口各有孪生 ====================

    [Fact]
    public void 高度在第二槽时判成Flax档_而错的那一档差到公里级()
    {
        var p = PlanetActorPoseRules.Decide(120_000, 150, -80_000, Frame(), groundElevationCm: 0.0);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.FlaxYUpSupported, p.Verdict);
        CloseTo(150.0, p.AltitudeFlaxCm, 5.0, "Flax 档离地高度");
        Assert.True(p.AltitudeEcsCm < -70_000, $"另一档应当差到公里级，实得 {p.AltitudeEcsCm}");
        Assert.Equal(Home.ToId(), p.RegionFlaxId);
        Assert.Contains("在带内", p.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void 高度在第三槽时判成ECS档_反过来的同一个证据()
    {
        // 正向孪生的另一半：如果 Decide 只会判成 Flax 档（比如两档共用了同一个映射），这条必红。
        var p = PlanetActorPoseRules.Decide(120_000, -80_000, 150, Frame(), groundElevationCm: 0.0);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.EcsZUpSupported, p.Verdict);
        CloseTo(150.0, p.AltitudeEcsCm, 5.0, "ECS 档离地高度");
        Assert.True(p.AltitudeFlaxCm < -70_000, $"另一档应当差到公里级，实得 {p.AltitudeFlaxCm}");
    }

    [Fact]
    public void 两档都落在带里时报不能判_而不是挑一个()
    {
        var p = PlanetActorPoseRules.Decide(120_000, 150, 150, Frame(), 0.0);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.Ambiguous, p.Verdict);
        Assert.Contains("不许猜", p.Why, StringComparison.Ordinal);
        // 两档的读数都要还看得到：判不出来时排查靠的就是这两个数
        CloseTo(150.0, p.AltitudeFlaxCm, 5.0, "Flax 档");
        CloseTo(150.0, p.AltitudeEcsCm, 5.0, "ECS 档");
    }

    [Fact]
    public void 两档都不在带里时报Neither_这是今天场景的真实档位()
    {
        var p = PlanetActorPoseRules.Decide(120_000, 40_000, 60_000, Frame(), 0.0);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.Neither, p.Verdict);
        Assert.Contains("不是贴在这块近景地面上", p.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void 站立带边界用同一个常量算_越过它判定就翻()
    {
        // 期望值是量出来的，不是推出来的：切平面在水平距离 d 处高出球面 d²/2R（这里 1.63 cm），
        // 所以"离地高度"不等于槽里的数。先让公开入口把两个读数算出来，再要求带子常量正好夹在中间。
        var f = Frame();
        double inBand = PlanetActorPoseRules.AltitudeAboveGroundCm(new PlanetVector3(120_000, 400, -80_000), f, 0.0);
        double outBand = PlanetActorPoseRules.AltitudeAboveGroundCm(new PlanetVector3(120_000, 600, -80_000), f, 0.0);
        double edge = PlanetActorPoseRules.StandingBandCm;
        Assert.True(Math.Abs(inBand) < edge, $"400 cm 那档该落在带内，实得 {inBand:0.###}（带 {edge}）");
        Assert.True(Math.Abs(outBand) > edge, $"600 cm 那档该落在带外，实得 {outBand:0.###}（带 {edge}）");

        // 判定必须与这两个读数同向：带子换个数（比如 100 或 5000）这里就红
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.FlaxYUpSupported,
            PlanetActorPoseRules.Decide(120_000, 400, -80_000, f, 0.0).Verdict);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.Neither,
            PlanetActorPoseRules.Decide(120_000, 600, -80_000, f, 0.0).Verdict);
    }

    [Fact]
    public void 输入退化时不抛异常_换区那一侧不许被一次探测打断()
    {
        var p = PlanetActorPoseRules.Decide(double.NaN, 150, -80_000, Frame(), 0.0);
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.UnusableInput, p.Verdict);
        Assert.Equal(0, p.RegionFlaxId);
        Assert.True(double.IsNaN(p.AltitudeFlaxCm));
        // 地面取不出来（NaN）也是同一档：宁可不判，也不要拿 0 米海平面冒充
        Assert.Equal(PlanetActorPoseRules.PoseVerdict.UnusableInput,
            PlanetActorPoseRules.Decide(120_000, 150, 0, Frame(), double.NaN).Verdict);
        // 而低层的裸映射必须<em>抛</em>：那一步没人兜底，静默换成 NaN 会更难查
        Assert.Throws<ArgumentException>(() =>
            PlanetActorPoseRules.ToLocalCm(double.PositiveInfinity, 0, 0, PlanetActorPoseRules.AxisConvention.FlaxYUp));
    }

    [Fact]
    public void 区域号由点反查_零向量与退化点回0而不是回一个看着合法的块()
    {
        var f = Frame();
        Assert.Equal(Home.ToId(), PlanetActorPoseRules.RegionOf(PlanetVector3.Zero, f));   // 帧原点就是本区中心
        Assert.Equal(0, PlanetActorPoseRules.RegionOf(new PlanetVector3(double.NaN, 0, 0), f));
        // 注意这里<em>不</em>测"局部零向量回 0"：局部零换出去是帧原点（半径非零），
        // 那条 guard 管的是全局点退化，正常帧下取不到——写成断言就成了空转的死分支。
    }

    // ==================== 接线：驱动真用了这一层，而不是自己再换一次轴 ====================

    [Fact]
    public void 驱动按这条探针取样_且不许在引擎那一侧再手写一份置换()
    {
        var code = StripComments(Read("PlanetNearFieldDriver.cs"));
        Assert.Equal(1, Count(code, "PlanetActorPoseRules.Decide("));
        Assert.Contains("Say(\"actor_pose", code, StringComparison.Ordinal);
        // 成功路径那一行必须原样带着判据器要读的两个 token。改名是一种很安静的断法：
        // 回执里那一行还在（上面那条针照样绿），而 Tools/planet-session-check.py 读不到 verdict，
        // 于是"探过没有"这件事变成一个只有读侧才知道的秘密。
        Assert.Equal(1, Count(code, "pose_mode="));
        Assert.Contains("\"actor_pose at={0} pose_mode={1}", code, StringComparison.Ordinal);
        Assert.Contains("HundunWorldGame.Instance?.LocalPlayerActor", code, StringComparison.Ordinal);
        // marker 里的开关：默认只探不猜，明说 flax/ecs 才改取样
        Assert.Contains("pose=", code, StringComparison.Ordinal);

        // 反向：整份驱动里<em>只许</em>有一处直接构造 PlanetVector3，就是那个合成走查点。
        // 角色位姿的换轴必须在 PlanetActorPoseRules 那一处——第二份换轴的形状就是
        // "两档口径各写一遍，然后只有一档是对的"，而它的差别是角色站在地面上还是两公里下面。
        var ctors = System.Text.RegularExpressions.Regex.Matches(code, @"new\s+PlanetVector3\([^)]*\)")
                             .Cast<Match>().Select(m => m.Value.Replace("\r", "").Trim()).ToList();
        Assert.Equal(new[] { "new PlanetVector3(_localX, 0, _localZ)" }, ctors);
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HundunWorld", "Source", "Game", "Planet", file));

    private static string StripComments(string text) =>
        string.Join("\n", text.Split('\n').Select(line =>
        {
            int cut = line.IndexOf("//", StringComparison.Ordinal);
            return cut >= 0 ? line.Substring(0, cut) : line;
        }));

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
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
