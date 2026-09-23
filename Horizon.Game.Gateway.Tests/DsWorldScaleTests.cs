using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Horizon.Game.Core.Sim.Server;
using Horizon.Game.Message.Sync;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M5.2 —— DS 权威世界的规模/负载验收。
/// <para>
/// 既有 <c>SyncLoadHarness</c> 压的是**网关 fanout 与 diff 吞吐**（协议层），
/// 从来不压"世界里真的有很多人 + 很多怪时，单 tick 还跑不跑得动"。
/// 而 M3/M4 引入的怪物实体与战斗结算是**加在 tick 循环里的新成本**——
/// 既有深度分析报告 §8.1 早已警告"单 shard 模拟瓶颈"（单 tick 逼近/超过 16.7ms → tick 堆积）。
/// 本套件把这个风险变成可回归的数字。
/// </para>
/// <para>
/// <b>关于墙钟断言</b>：本项目已经吃过"墙钟性能断言在并发构建/烘焙时误报"的亏
/// （见执行报告：M4 与真集群验证期间各出现一次，均为环境 CPU 争用）。
/// 因此这里把阈值取得**宽松**（只为拦住数量级退化），并打 <c>Category=Performance</c> 便于隔离；
/// 精确基线应由 <c>Horizon.PerformanceTests</c> 在专用机器上建立。
/// </para>
/// </summary>
[Trait("Category", "Performance")]
public class DsWorldScaleTests
{
    private const int PlayerCount = 50;
    private const int SpawnPoints = 5;
    private const int MonstersPerPoint = 4;
    private const int Ticks = 600; // 10 秒游戏时间

    private static DsWorldSession BuildWorld()
    {
        var world = new DsWorldSession(new DsWorldSessionOptions
        {
            ShardId = 0,
            TicksPerSecond = 60,
            SnapshotIntervalTicks = 3, // 20Hz 广播
            PlayerDefaultMaxHp = 500,
        });

        var points = new List<SpawnPointConfig>();
        for (var i = 0; i < SpawnPoints; i++)
        {
            var cfg = SpawnPointConfig.Default;
            cfg.SpawnPointId = i + 1;
            cfg.X = i * 30f;
            cfg.Y = 0f;
            cfg.MaxAlive = MonstersPerPoint;
            cfg.MonsterTemplateId = 100 + i;
            cfg.MaxHp = 200;          // 高血量：压测期间不因击杀而改变实体数
            cfg.AttackPower = 1;      // 低攻击：避免玩家批量死亡影响实体数
            cfg.MoveSpeed = 4f;
            cfg.AggroRadius = 25f;
            cfg.LeashRadius = 60f;
            cfg.RespawnDelaySeconds = 30f;
            points.Add(cfg);
        }
        world.ConfigureSpawnPoints(points);
        return world;
    }

    private static void AddPlayers(DsWorldSession world)
    {
        for (var i = 0; i < PlayerCount; i++)
        {
            var characterId = (ulong)(3000 + i);
            // 玩家散布在各刷怪点附近（制造大量仇恨与追击，逼近最坏情况）
            var x = (i % SpawnPoints) * 30f + (i % 7) - 3f;
            Assert.True(world.AddPlayer((long)characterId, characterId, x, 0f, 0f, out var reason), reason);
        }
    }

    private static InputPacket MoveInput(ulong characterId, long tick, float predictedX)
        => new InputPacket
        {
            CharacterId = characterId,
            ClientTick = tick,
            MoveX = 1f,
            MoveY = 0f,
            PredictedEndX = predictedX,
            MaxSpeed = MovementFormula.DefaultMaxSpeed,
        };

    [Fact]
    public void Scale_50Players_20Monsters_600Ticks_StaysWithinTickBudget()
    {
        var world = BuildWorld();
        AddPlayers(world);

        // 每个玩家的预测位置自维护（模拟真实客户端），避免服务端持续下发修正干扰压测。
        var predicted = new float[PlayerCount];
        var sw = new Stopwatch();
        long maxTickMs = 0;
        long totalMs = 0;
        var snapshotTicks = 0;
        var totalEntitiesInLastSnapshot = 0;

        for (var tick = 1; tick <= Ticks; tick++)
        {
            for (var i = 0; i < PlayerCount; i++)
            {
                predicted[i] += MovementFormula.DefaultMaxSpeed / 60f;
                var characterId = (ulong)(3000 + i);
                world.SubmitInput((long)characterId, MoveInput(characterId, tick, predicted[i]));
            }

            sw.Restart();
            var result = world.Tick();
            sw.Stop();

            var ms = sw.ElapsedMilliseconds;
            totalMs += ms;
            if (ms > maxTickMs) maxTickMs = ms;

            if (result.WorldSnapshot is not null)
            {
                snapshotTicks++;
                totalEntitiesInLastSnapshot = result.WorldSnapshot.Deltas.Length;
            }
        }

        var avgMs = (double)totalMs / Ticks;

        // --- 正确性断言（规模下"东西还在"）---
        Assert.Equal(PlayerCount, world.PlayerCount);
        Assert.Equal(SpawnPoints * MonstersPerPoint, world.MonsterCount);
        Assert.True(snapshotTicks >= Ticks / 3 - 1, $"应至少产生 {Ticks / 3 - 1} 次快照，实际 {snapshotTicks}");
        Assert.Equal(PlayerCount + SpawnPoints * MonstersPerPoint, totalEntitiesInLastSnapshot);

        // --- 预算断言（宽松：只拦数量级退化）---
        // 60Hz 单 tick 预算 16.7ms；这里给 50ms 上限（约 3 帧预算），
        // 目的是拦住"加了怪物后单 tick 变成几百毫秒"这类结构性退化，而不是追求精确基线。
        Assert.True(maxTickMs <= 50,
            $"单 tick 最坏耗时 {maxTickMs}ms 超过 50ms 上限（均值 {avgMs:F2}ms）——" +
            $"存在 tick 堆积风险（玩家 {PlayerCount} / 怪 {SpawnPoints * MonstersPerPoint}）。");
    }

    [Fact]
    public void Scale_SnapshotSize_IsBoundedByEntityCount()
    {
        // 快照大小必须与实体数同阶（防止"每人一份全量"之类的 N² 退化）。
        var world = BuildWorld();
        AddPlayers(world);

        SnapshotPacket? snap = null;
        for (var tick = 1; tick <= 30 && snap is null; tick++)
        {
            var r = world.Tick();
            snap = r.WorldSnapshot;
        }

        Assert.NotNull(snap);
        Assert.Equal(PlayerCount + SpawnPoints * MonstersPerPoint, snap!.Deltas.Length);
        // 每个实体一个 delta，且实体 ID 唯一（无重复下发）
        Assert.Equal(snap.Deltas.Length, snap.Deltas.Select(d => d.EntityId).Distinct().Count());
    }

    [Fact]
    public void Scale_MonstersRemainDeterministicUnderLoad()
    {
        // 大规模下也必须可复现：两次同样规模的运行，怪物位置逐位一致。
        static string Run()
        {
            var world = BuildWorld();
            AddPlayers(world);
            var predicted = new float[PlayerCount];
            var sb = new System.Text.StringBuilder();

            for (var tick = 1; tick <= 120; tick++)
            {
                for (var i = 0; i < PlayerCount; i++)
                {
                    predicted[i] += MovementFormula.DefaultMaxSpeed / 60f;
                    var characterId = (ulong)(3000 + i);
                    world.SubmitInput((long)characterId, MoveInput(characterId, tick, predicted[i]));
                }
                world.Tick();
            }

            for (var sp = 1; sp <= SpawnPoints; sp++)
            {
                for (var slot = 0; slot < MonstersPerPoint; slot++)
                {
                    var id = SpawnDirectorCore.ComputeEntityId(sp, slot);
                    if (world.TryGetMonster(id, out var m))
                    {
                        sb.Append(BitConverter.SingleToInt32Bits(m.Ai.X)).Append('|')
                          .Append(BitConverter.SingleToInt32Bits(m.Ai.Y)).Append('|')
                          .Append((byte)m.Ai.State).Append(';');
                    }
                }
            }
            return sb.ToString();
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
        Assert.True(a.Length > 100, "轨迹不应为空（反假绿）");
    }
}
