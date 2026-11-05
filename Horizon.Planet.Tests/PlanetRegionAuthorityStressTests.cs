using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Horizon.Planet;
using Horizon.Planet.Authority;
using Horizon.Planet.Wire;
using Xunit;

namespace Horizon.Planet.Tests;

/// <summary>
/// 区域权威在"一屏玩家同时游走"规模下的账与耗时（P4 那行压测的**库级那一半**）。
/// <para>说清楚量级边界：这里压的是判定与表维护本身（grain 只是它的适配器），
/// <b>不是</b> 100 个真客户端跨 silo 的网络压测。后者要跑起网关与 silo，
/// 属于 #27 之后"两套世界模型并轨"才有意义的东西。把这一半先钉住，
/// 是因为 O(N²) 的可见集与每次受理的落盘投影是这里唯一会在人数上炸掉的两处。</para>
/// <para>耗时阈值故意放得很松（实测值打在输出里看）：这条判据要抓的是
/// "复杂度变了"这种量级错误，不是"这台机器今天忙"。§13.41 那条飘红的帧预算判据就是前车之鉴。</para>
/// </summary>
public class PlanetRegionAuthorityStressTests
{
    private const long Radius = PlanetConstants.PlanetRadiusCm;
    private const int Level = PlanetConstants.DefaultRegionLevel;
    private const int Players = 120;
    private const int Frames = 60;                       // 60 帧 = 1 秒（30 Hz 的话是 2 秒）

    private static PlanetVector3 CenterOf(RegionKey key) =>
        RegionFrame.Build(key, Radius).GlobalFromLocalCm(new PlanetVector3(0, 0, 0));

    /// <summary>沿大圆往东走一步：拿<em>当前点</em>的东向，而不是所在区域的东向。</summary>
    private static PlanetVector3 StepEast(PlanetVector3 point, double stepCm)
    {
        var up = point.Normalized();
        var east = RegionFrame.ComputeEast(up, CubeSphere.FaceOf(up));
        return (up * Radius + east * stepCm).Normalized() * Radius;
    }

    [Fact]
    public void 一百二十个实体同时游走_账本与可见性不变量在压力下仍成立()
    {
        var authority = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);

        // 起点铺在一条带上：同一面、同一行，彼此隔几块区域——这样"走几步就换区"是必然事件
        var start = new RegionKey(2, Level, 8192, 8192);
        var ids = new List<long>();
        for (long i = 0; i < Players; i++)
        {
            var key = new RegionKey(2, Level, (int)(start.X + i % 40), (int)(start.Y + i / 40));
            Assert.True(authority.TryBind(i, CenterOf(key), tick: 0, out _), $"实体 {i} 落位失败");
            ids.Add(i);
        }
        Assert.Equal(Players, authority.Count);

        // 位置自己记账：每帧从<em>上一步的位置</em>往前走。
        // 上一版是"从所在区域中心 +1 km"，于是实体永远停在原地附近，60 帧跨区 0 次——
        // 那条判据看着全绿，其实一次换区都没发生过（这就是"跑到了但没压到"的形状）。
        var position = new Dictionary<long, PlanetVector3>();
        foreach (var id in ids) position[id] = authority.GlobalOf(id)!.Value;

        var sw = Stopwatch.StartNew();
        int requests = 0, crossings = 0, maxSubscriptions = 0;
        long maxVisible = 0;

        for (int frame = 1; frame <= Frames; frame++)
        {
            long tick = frame * 10;
            foreach (var id in ids)
            {
                var owner = authority.OwnerOf(id)!.Value;
                var next = StepEast(position[id], 100_000);          // 每帧 1 km：区域约 6 km 宽，6~7 帧必跨一次
                position[id] = next;
                var claimed = RegionKey.FromDirection(next.Normalized(), Level);
                requests++;
                if (claimed != owner)
                {
                    var result = authority.RequestTransfer(id,
                        new RegionTransferRequest
                        {
                            FromRegionId = owner.ToId(),
                            ToRegionId = claimed.ToId(),
                            GlobalCm = EcefCentimeters.FromVector(next),
                            Tick = tick,
                        }, tick);
                    Assert.True(result.Applied, $"跨区必须受理：" + result);
                    crossings++;
                }
                else
                {
                    Assert.True(authority.TryTeleport(id, next, tick, out _));   // 没跨区就只更新坐标
                }
            }

            if (frame % 10 == 0)
            {
                foreach (var id in ids)
                {
                    maxSubscriptions = Math.Max(maxSubscriptions, authority.Subscriptions(id).Count);
                    maxVisible = Math.Max(maxVisible, authority.VisibleEntities(id).Count);
                }
            }
        }
        sw.Stop();

        double msPerFrame = sw.Elapsed.TotalMilliseconds / Frames;
        double usPerOp = sw.Elapsed.TotalSeconds * 1e6 / requests;
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "STRESS players={0} frames={1} requests={2} crossings={3} elapsed_ms={4:0.##} ms_per_frame={5:0.###} us_per_op={6:0.#} max_subs={7} max_visible={8}",
            Players, Frames, requests, crossings, sw.Elapsed.TotalMilliseconds, msPerFrame, usPerOp,
            maxSubscriptions, maxVisible));

        // 一个都没丢：这是 P1 那条验收在规模下的同一件事
        Assert.Equal(Players, authority.Count);
        Assert.Equal(Frames * Players, requests);
        Assert.True(crossings > Players, $"60 帧里总共才跨了 {crossings} 次区，这组参数没压到换区路径");
        // 每一次操作都被受理：跨区走 transfer，未跨区只更新坐标（teleport 也计入 Applied）
        Assert.Equal(requests, authority.Applied);
        Assert.Equal(0, authority.RejectedGeometry);
        Assert.Equal(0, authority.RejectedAuthority);

        // 订阅集不许随人数长：本区 + 4 个共边邻居 + 1 个重叠旧区
        Assert.True(maxSubscriptions <= 1 + PlanetRegions.EdgeNeighbourCount + 1,
                    string.Format(CultureInfo.InvariantCulture,
                        "订阅集到了 {0} 块，超出了『本区 + 共边邻区 + 重叠旧区』的上界", maxSubscriptions));

        // 耗时只钉量级：一帧 120 个实体的账不该超过 5 ms（实测远低于此）
        Assert.True(msPerFrame < 5.0, $"每帧 {msPerFrame:0.###} ms，已经不像 O(N) 的表维护了");
    }

    [Fact]
    public void 落盘投影在满表时也只是一次线性拷贝()
    {
        var authority = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        var start = new RegionKey(2, Level, 8192, 8192);
        for (long i = 0; i < Players; i++)
        {
            var key = new RegionKey(2, Level, (int)(start.X + i % 40), (int)(start.Y + i / 40));
            Assert.True(authority.TryBind(i, CenterOf(key), tick: i, out _));
        }

        var sw = Stopwatch.StartNew();
        var snapshot = authority.SnapshotEntities();
        sw.Stop();

        Assert.Equal(Players, snapshot.Count);
        Assert.True(sw.Elapsed.TotalMilliseconds < 20.0,
                    $"一次全表投影 {sw.Elapsed.TotalMilliseconds:0.###} ms，落盘节奏会被它拖住");

        // 投影逐条与查询接口一致（人数上去之后这条更容易被"缓存了旧值"打破）
        foreach (var e in snapshot)
        {
            Assert.Equal(authority.OwnerOf(e.EntityId), e.Region);
            Assert.Equal(authority.GlobalOf(e.EntityId)!.Value, e.GlobalCm);
        }
    }

    [Fact]
    public void 可见性对称性在满表抽样下也成立()
    {
        var authority = new PlanetRegionAuthority(Radius, Level, overlapTicks: 120);
        var start = new RegionKey(2, Level, 8192, 8192);
        for (long i = 0; i < Players; i++)
        {
            var key = new RegionKey(2, Level, (int)(start.X + i % 40), (int)(start.Y + i / 40));
            Assert.True(authority.TryBind(i, CenterOf(key), tick: 0, out _));
        }

        // 把前 40 个挤到同一块区域里，制造"一堆人互相可见"的最坏情形
        var crowded = RegionFrame.Build(start, Radius);
        for (long i = 0; i < 40; i++)
            Assert.True(authority.TryTeleport(i, crowded.GlobalFromLocalCm(new PlanetVector3(i * 1000, 0, 0)), 1, out _));

        var visibleOf = new Dictionary<long, HashSet<long>>();
        foreach (var id in Enumerable.Range(0, Players).Select(i => (long)i))
            visibleOf[id] = new HashSet<long>(authority.VisibleEntities(id));

        foreach (var (a, set) in visibleOf)
            foreach (var b in set)
                Assert.True(visibleOf[b].Contains(a), $"A={a} 看得见 B={b}，反过来不行——AOI 单向可见会让一边的人打不到另一边");

        Assert.True(visibleOf[0].Count >= 30, $"挤在同一块区域里却只看见 {visibleOf[0].Count} 个，聚集这一档没压到");
    }
}
