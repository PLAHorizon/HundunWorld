using Horizon.Game.Sim.Monsters;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 路线试算（编辑器「刷点·路线」页的"一个循环多远、多久"）。
/// <para>
/// 顺序与停留由巡逻内核现算（演算层不复制换点规则），距离由点数算；
/// 这里把三条几何形状钉住：两点往返、三点循环、带停留的循环时间，
/// 外加三条"该报的问题"（点太少/段太短/段太长）与"没配速度就不给假时间"。
/// </para>
/// </summary>
public class NpcRoutePreviewTests
{
    private readonly ITestOutputHelper _log;

    public NpcRoutePreviewTests(ITestOutputHelper log) => _log = log;

    private static MonsterPatrolPoint[] Line(float metres, float pause = -1f)
        => new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f, pause),
            new MonsterPatrolPoint(metres, 0f, 0f, pause),
        };

    [Fact]
    public void 往返模式一个循环等于来回两段()
    {
        var preview = NpcRoutePreviewCore.Preview(Line(10f), MonsterPatrolMode.PingPong, 0f, 2f);
        _log.WriteLine(preview.Describe());

        Assert.True(preview.Closed, "往返路线必须被判成闭合：" + preview.Describe());
        Assert.Equal(20f, preview.TotalMetres, 3);       // 去 10 + 回 10
        Assert.Equal(10f, preview.MinLegMetres, 3);
        Assert.Equal(10f, preview.MaxLegMetres, 3);
        Assert.Equal(10f, preview.CycleSeconds, 3);      // 20m / 2m/s，无停留
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public void 循环模式一周等于多边形周长()
    {
        // 直角三角 3-4-5：周长 12。
        var points = new[]
        {
            new MonsterPatrolPoint(0f, 0f, 0f),
            new MonsterPatrolPoint(3f, 0f, 0f),
            new MonsterPatrolPoint(3f, 4f, 0f),
        };

        var preview = NpcRoutePreviewCore.Preview(points, MonsterPatrolMode.Loop, 0f, 1.5f);
        _log.WriteLine(preview.Describe());

        Assert.True(preview.Closed, "循环路线必须闭合：" + preview.Describe());
        Assert.Equal(12f, preview.TotalMetres, 3);
        Assert.Equal(3f, preview.MinLegMetres, 3);
        Assert.Equal(5f, preview.MaxLegMetres, 3);
        Assert.Equal(8f, preview.CycleSeconds, 3);       // 12m / 1.5m/s
    }

    [Fact]
    public void 停留时间按每个到点计一次_并进循环秒数()
    {
        const float pause = 2f;
        var preview = NpcRoutePreviewCore.Preview(Line(10f, pause), MonsterPatrolMode.PingPong, 0f, 2f);
        _log.WriteLine(preview.Describe());

        // 一个往返循环里到点两次（终点一次、回到起点一次）⇒ 停 2×pause。
        Assert.Equal(2f * pause, preview.PauseSeconds, 3);
        Assert.Equal(20f / 2f + 2f * pause, preview.CycleSeconds, 3);
    }

    [Fact]
    public void 没配步行速度就不给假的循环秒数()
    {
        var preview = NpcRoutePreviewCore.Preview(Line(10f), MonsterPatrolMode.PingPong, 0f, 0f);
        _log.WriteLine(preview.Describe());

        Assert.Equal(-1f, preview.CycleSeconds);
        Assert.Contains("walk_speed_not_set", preview.Warnings);
        Assert.Equal(20f, preview.TotalMetres, 3);        // 距离照报，免得"没速度"连距离都不给
    }

    [Fact]
    public void 点太少直接判不成立()
    {
        var preview = NpcRoutePreviewCore.Preview(new[] { new MonsterPatrolPoint(1f, 1f, 0f) },
            MonsterPatrolMode.Loop, 0f, 2f);
        _log.WriteLine(preview.Describe());

        Assert.Equal(0, preview.Points);
        Assert.Equal(-1f, preview.CycleSeconds);
        Assert.Contains("route_needs_two_points", preview.Warnings);
    }

    [Fact]
    public void 段太短与段太长都要点名()
    {
        var shortLeg = NpcRoutePreviewCore.Preview(Line(0.2f), MonsterPatrolMode.Loop, 0f, 2f);
        _log.WriteLine("short: " + shortLeg.Describe());
        Assert.Contains("leg_too_short", shortLeg.Warnings);

        var longLeg = NpcRoutePreviewCore.Preview(Line(80f), MonsterPatrolMode.Loop, 0f, 2f);
        _log.WriteLine("long: " + longLeg.Describe());
        Assert.Contains("leg_too_long", longLeg.Warnings);
        Assert.DoesNotContain("leg_too_short", longLeg.Warnings);
    }

    [Fact]
    public void 随机模式不谎报可预测顺序()
    {
        var preview = NpcRoutePreviewCore.Preview(Line(10f), MonsterPatrolMode.RandomNext, 0f, 2f);
        _log.WriteLine(preview.Describe());
        Assert.Contains("random_mode_order_unpredictable", preview.Warnings);
    }
}