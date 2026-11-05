using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 壳投放器的判据。三件事各自有反证：预算真的守住了（看实测峰值而不是看配置）、
/// 建过的不重复建、离开计划的块交出来而且只交一次，外加一条账本等式 <c>Built − Evicted = 在册块数</c>。
/// </summary>
public class PlanetShellStreamerTests
{
    private const double Radius = PlanetConstants.PlanetRadiusCm;

    private static PlanetVector3 Dir(double lat, double lon)
        => Geodetic.DirectionFromLatLon(lat * Math.PI / 180.0, lon * Math.PI / 180.0);

    private static ShellPlan Plan(double lat, double lon, int maxLevel = 5, double pixels = 1.0)
        => new PlanetShellPlanner(Radius, 8, maxLevel, pixels, 60.0, 1080.0).Build(Dir(lat, lon));

    /// <summary>跑到 settle，最多这么多次；返回用了几次。</summary>
    private static int Pump(PlanetShellStreamer streamer, int maxSteps, out long issued)
    {
        long total = 0;
        issued = 0;
        for (int i = 0; i < maxSteps; i++)
        {
            var batch = streamer.Step(out _);
            total += batch.Count;
            issued = total;
            if (streamer.IsSettled) return i + 1;
        }
        return -1;
    }

    [Fact]
    public void 预算够时一帧建完并记下用了多少顶点()
    {
        var plan = Plan(30, 120);
        var streamer = new PlanetShellStreamer(plan.TotalVertices);
        streamer.Sync(plan);
        var batch = streamer.Step(out var evict);
        Assert.Equal(plan.Tiles.Count, batch.Count);
        Assert.Equal(plan.TotalVertices, (long)streamer.PeakFrameVertices);
        Assert.Equal(1L, streamer.FramesToSettle);
        Assert.Equal(0, streamer.DeferredFrames);
        Assert.Empty(evict);
        Assert.True(streamer.IsSettled);
    }

    [Fact]
    public void 每帧顶点数从不越预算()
    {
        var plan = Plan(30, 120);
        const long budget = 900;
        var streamer = new PlanetShellStreamer(budget);
        streamer.Sync(plan);
        // 自己数帧与顶点：要验的是"每帧交出来的顶点数"这一列，Pump 只数块数
        long issued = 0, verts = 0;
        int steps = 0;
        for (int i = 0; i < 200 && !streamer.IsSettled; i++)
        {
            var batch = streamer.Step(out _);
            steps = i + 1;
            issued += batch.Count;
            verts += batch.Sum(t => (long)t.VertexCount);
        }
        Assert.True(steps > 0 && streamer.IsSettled, $"200 次还没建完（{streamer.Describe()}）");
        Assert.True(streamer.PeakFrameVertices <= budget,
                    $"预算 {budget} 却出现单帧 {streamer.PeakFrameVertices} 顶点");
        Assert.True(streamer.DeferredFrames > 0, "预算比整份计划小却一次都没延后 ⇒ 预算根本没起作用");
        Assert.Equal((long)plan.Tiles.Count, issued);
        Assert.Equal((long)plan.Tiles.Count, streamer.Built);   // Built 数的是块，不是顶点
        Assert.Equal(plan.TotalVertices, verts);                // 顶点总量一份不少，也没重复投
    }

    [Fact]
    public void 建过的块不许再建第二次()
    {
        var plan = Plan(-20, 95);
        var streamer = new PlanetShellStreamer(plan.TotalVertices);
        streamer.Sync(plan);
        Pump(streamer, 50, out _);
        var again = streamer.Step(out var evict);
        Assert.Empty(again);
        Assert.Empty(evict);
        Assert.Equal((long)plan.Tiles.Count, streamer.Built);      // 一次都没重复
    }

    [Fact]
    public void 预算为0时一块都不建()
    {
        var plan = Plan(30, 120);
        var streamer = new PlanetShellStreamer(0);
        streamer.Sync(plan);
        for (int i = 0; i < 5; i++)
        {
            var batch = streamer.Step(out _);
            Assert.Empty(batch);
        }
        Assert.Equal(0L, streamer.Built);
        Assert.Equal(plan.Tiles.Count, streamer.PendingCount);
        Assert.Equal(5L, streamer.DeferredFrames);
        Assert.False(streamer.IsSettled);
    }

    [Fact]
    public void 计划换血时旧块交出来而且只交一次()
    {
        var first = Plan(30, 120);
        var streamer = new PlanetShellStreamer(first.TotalVertices);
        streamer.Sync(first);
        Pump(streamer, 20, out _);
        Assert.Equal(first.Tiles.Count, streamer.BuiltInPlan);

        // 玩家跑到星球另一面：旧计划里的块大部分不再被需要
        var second = Plan(-55, -70);
        streamer.Sync(second);
        var batch = streamer.Step(out var evict);
        Assert.True(evict.Count > 0, "换了计划却一块旧的都不回收，引擎里会堆两套壳");
        Assert.True(evict.Count < first.Tiles.Count, "全部旧块都不在新计划里？两份计划几乎重合，这条就没意义了");

        var nextBatch = streamer.Step(out var evictAgain);
        Assert.Empty(evictAgain);                              // 同一批不许交两次
        Pump(streamer, 200, out _);
        Assert.True(streamer.IsSettled);
        Assert.Equal(second.Tiles.Count, streamer.BuiltInPlan);
    }

    [Fact]
    public void 账本等式_built减evicted_等于在册块数()
    {
        var streamer = new PlanetShellStreamer(1200);
        var plans = new[] { Plan(30, 120), Plan(31, 121), Plan(-55, -70), Plan(30, 120) };
        foreach (var plan in plans)
        {
            streamer.Sync(plan);
            for (int i = 0; i < 40; i++) streamer.Step(out _);
            Assert.True(streamer.Built - streamer.Evicted == (long)streamer.BuiltInPlan,
                        $"账不平：{streamer.Describe()}");
        }
    }

    [Fact]
    public void 同一串输入两次跑逐帧相同()
    {
        var plan = Plan(12, -160);
        var a = new PlanetShellStreamer(1500);
        var b = new PlanetShellStreamer(1500);
        a.Sync(plan);
        b.Sync(plan);
        for (int frame = 0; frame < 12; frame++)
        {
            var ka = a.Step(out var ea).Select(t => t.Key).ToList();
            var kb = b.Step(out var eb).Select(t => t.Key).ToList();
            Assert.Equal(ka, kb);
            Assert.Equal(ea, eb);
        }
        Assert.Equal(a.Describe(), b.Describe());
    }

    [Fact]
    public void 参数不合法时当场拒绝()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlanetShellStreamer(-1));
        var streamer = new PlanetShellStreamer(100);
        Assert.Throws<ArgumentNullException>(() => streamer.Sync(null!));
        // 没 Sync 之前 Step 不建任何东西，也不炸
        var fresh = new PlanetShellStreamer(1000);
        Assert.Empty(fresh.Step(out var evict));
        Assert.Empty(evict);
    }
}
