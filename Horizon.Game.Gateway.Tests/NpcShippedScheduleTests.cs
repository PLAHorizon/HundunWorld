using System;
using System.Collections.Generic;
using System.IO;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Network;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim.Npcs;
using Xunit;
using Xunit.Abstractions;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// 作息 → 姿态 → 路线，用**随包产物**跑一整天（1200 秒周期）：
/// 铁匠白天打铁、夜里站立；行商沿巡街路线走；守门人持械站桩。
/// <para>
/// 这条把三张表拧在一起验：`schedule_slots`（换档）、`templates`（姿态与速度）、`waypoint_path/nodes`（路线）。
/// 任何一张改坏，玩家看到的是"到点不换姿势"或"行商站着不动"，而单表测试都还是绿的。
/// </para>
/// </summary>
[Collection("monster-content")]
public class NpcShippedScheduleTests
{
    private readonly ITestOutputHelper _log;

    public NpcShippedScheduleTests(ITestOutputHelper log) => _log = log;

    /// <summary>2 拍/秒：走完 1200 秒周期只要 2400 拍，够快又够真。</summary>
    private static DsWorldSessionOptions Options() => new()
    {
        ShardId = 0,
        TicksPerSecond = 2,
        SnapshotIntervalTicks = 1,
        EnableAoiFilter = false,
        MaxSessions = 10,
        MaxEntities = 200,
    };

    private static string ShippedContentPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "HundunWorld", "Data", "Npcs", "db_content.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("找不到随包导出的 NPC 内容");
    }

    [Fact]
    public void ShippedContent_DayCycleChangesPostureAndMovesMerchant()
    {
        var receipt = NpcContentLoader.LoadFile(ShippedContentPath(), out var error);
        Assert.NotNull(receipt);
        Assert.Equal(0, receipt!.ProblemCount);

        try
        {
            var world = new DsWorldSession(Options());
            // 玩家远远站着，不参与任何交互（只作为世界里的一个存活目标）。
            Assert.True(world.AddPlayer(1001, 1001, -500f, 0f, 0f, out var reason), reason);
            world.ApplyNpcContentSpawns();

            var templateOf = new Dictionary<ulong, int>();
            var postureByDay = new Dictionary<ulong, int>();   // 白天（t≈300s）
            var postureByNight = new Dictionary<ulong, int>(); // 夜里（t≈900s）
            var merchantStart = (X: 0f, Y: 0f);
            var merchantFar = 0f;
            var sawMerchant = false;

            for (var tick = 1; tick <= 2400; tick++)
            {
                var result = world.Tick();
                var deltas = result.WorldSnapshot?.Deltas;
                if (deltas == null) continue;

                foreach (var delta in deltas)
                {
                    if (delta.Identity is { } identity) templateOf[delta.EntityId] = identity.TemplateId;
                }

                if (tick == 600)  // 300 秒：白天档
                {
                    foreach (var delta in deltas)
                        if (delta.State is { } s) postureByDay[delta.EntityId] = s.NpcStandState;
                }

                if (tick == 1800) // 900 秒：夜里档
                {
                    foreach (var delta in deltas)
                        if (delta.State is { } s) postureByNight[delta.EntityId] = s.NpcStandState;
                }

                foreach (var delta in deltas)
                {
                    if (delta.Transform is not { } t) continue;
                    if (!templateOf.TryGetValue(delta.EntityId, out var template)) continue;
                    if (template != 3) continue; // 行商

                    if (!sawMerchant)
                    {
                        merchantStart = (t.X, t.Y);
                        sawMerchant = true;
                    }

                    var dx = t.X - merchantStart.X;
                    var dy = t.Y - merchantStart.Y;
                    var dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > merchantFar) merchantFar = dist;
                }
            }

            ulong smith = 0UL, gatekeeper = 0UL, merchant = 0UL;
            foreach (var pair in templateOf)
            {
                if (pair.Value == 1) smith = pair.Key;
                else if (pair.Value == 2) gatekeeper = pair.Key;
                else if (pair.Value == 3) merchant = pair.Key;
            }

            Assert.True(smith != 0UL && gatekeeper != 0UL && merchant != 0UL, "快照里没认出三只关键 NPC");
            _log.WriteLine($"smith day={postureByDay.GetValueOrDefault(smith, -99)} night={postureByNight.GetValueOrDefault(smith, -99)}");
            _log.WriteLine($"gatekeeper day={postureByDay.GetValueOrDefault(gatekeeper, -99)} night={postureByNight.GetValueOrDefault(gatekeeper, -99)}");
            _log.WriteLine($"merchant_far={merchantFar:0.##}m");

            // 铁匠：白天做工(2) → 夜里站立(0)。这条只可能来自作息表。
            Assert.Equal((int)NpcPosture.Work, postureByDay[smith]);
            Assert.Equal((int)NpcPosture.Stand, postureByNight[smith]);

            // 守门人：全天持械(3)。
            Assert.Equal((int)NpcPosture.Guard, postureByDay[gatekeeper]);
            Assert.Equal((int)NpcPosture.Guard, postureByNight[gatekeeper]);

            // 行商：沿路线真的走了起来（路线是 8/13 × -5/2 的方框，周长约 24 米）。
            Assert.True(sawMerchant, "快照里没看到行商的位置");
            Assert.True(merchantFar > 3f, $"行商几乎没动：{merchantFar:0.##}m——路线/速度可能没生效");
        }
        finally
        {
            NpcContent.ResetToHardcoded();
        }
    }
}
