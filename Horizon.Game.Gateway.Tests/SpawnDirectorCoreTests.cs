using System;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M3.2 —— 确定性刷怪调度内核单元测试（验收门 H3-a）。
/// <para>
/// 覆盖目标：槽位池上限（MaxAlive）、首次生成不受重生延迟约束、死亡后按 tick 重生、
/// 实体 ID 的确定性与唯一性、出生坐标的对称展开、以及同输入序列可位级复现。
/// </para>
/// </summary>
public class SpawnDirectorCoreTests
{
    private const int TicksPerSecond = 60;

    private static SpawnPointConfig MakeConfig(int maxAlive = 3, float respawnSeconds = 15f)
    {
        var cfg = SpawnPointConfig.Default;
        cfg.SpawnPointId = 1;
        cfg.MaxAlive = maxAlive;
        cfg.RespawnDelaySeconds = respawnSeconds;
        cfg.SlotSpacing = 8f;
        cfg.X = 100f;
        cfg.Y = 200f;
        cfg.Z = 5f;
        return cfg;
    }

    // ---------- 首次生成 ----------

    [Fact]
    public void Tick_FreshSlots_SpawnsAllSlotsImmediately()
    {
        var cfg = MakeConfig(maxAlive: 3);
        var slots = SpawnDirectorCore.CreateSlots(3);

        var d = SpawnDirectorCore.Tick(cfg, slots, serverTick: 0, TicksPerSecond);

        Assert.Equal(3, d.Spawns.Length);
        Assert.All(d.Spawns, s => Assert.True(s.ShouldSpawn));
    }

    [Fact]
    public void Tick_ZeroMaxAlive_SpawnsNothing()
    {
        var cfg = MakeConfig(maxAlive: 0);
        var d = SpawnDirectorCore.Tick(cfg, Array.Empty<SpawnSlotState>(), 0, TicksPerSecond);
        Assert.Empty(d.Spawns);
    }

    [Fact]
    public void Tick_AllSlotsAlive_SpawnsNothing()
    {
        var cfg = MakeConfig(maxAlive: 3);
        var slots = SpawnDirectorCore.CreateSlots(3);
        for (var i = 0; i < 3; i++)
        {
            SpawnDirectorCore.MarkSlotAlive(slots, i, SpawnDirectorCore.ComputeEntityId(cfg.SpawnPointId, i));
        }

        var d = SpawnDirectorCore.Tick(cfg, slots, serverTick: 9999, TicksPerSecond);

        Assert.Empty(d.Spawns);
        Assert.Equal(3, SpawnDirectorCore.CountAlive(slots));
    }

    [Fact]
    public void Tick_SlotsArrayShorterThanMaxAlive_TreatsMissingAsPending()
    {
        // 宿主尚未扩容数组时，不应抛异常；缺失槽位视为待生成。
        var cfg = MakeConfig(maxAlive: 3);
        var slots = SpawnDirectorCore.CreateSlots(1);

        var d = SpawnDirectorCore.Tick(cfg, slots, 0, TicksPerSecond);

        Assert.Equal(3, d.Spawns.Length);
    }

    // ---------- 重生延迟 ----------

    [Fact]
    public void Tick_DeadSlotBeforeRespawnTick_DoesNotSpawn()
    {
        var cfg = MakeConfig(maxAlive: 1, respawnSeconds: 15f);
        var slots = SpawnDirectorCore.CreateSlots(1);
        SpawnDirectorCore.MarkSlotAlive(slots, 0, SpawnDirectorCore.ComputeEntityId(cfg.SpawnPointId, 0));

        var respawnAt = SpawnDirectorCore.MarkSlotDead(slots, 0, serverTick: 100, TicksPerSecond, cfg.RespawnDelaySeconds);
        Assert.Equal(100 + 15 * 60, respawnAt);

        // 尚未到期
        var d1 = SpawnDirectorCore.Tick(cfg, slots, serverTick: 100 + 15 * 60 - 1, TicksPerSecond);
        Assert.Empty(d1.Spawns);

        // 到期
        var d2 = SpawnDirectorCore.Tick(cfg, slots, serverTick: respawnAt, TicksPerSecond);
        Assert.Single(d2.Spawns);
        Assert.Equal(0, d2.Spawns[0].SlotIndex);
    }

    [Fact]
    public void MarkSlotDead_OutOfRange_ReturnsMinusOne()
    {
        var slots = SpawnDirectorCore.CreateSlots(2);
        Assert.Equal(-1L, SpawnDirectorCore.MarkSlotDead(slots, 5, 0, TicksPerSecond, 10f));
        Assert.Equal(-1L, SpawnDirectorCore.MarkSlotDead(slots, -1, 0, TicksPerSecond, 10f));
    }

    [Fact]
    public void MarkSlotAlive_OutOfRange_ReturnsFalse()
    {
        var slots = SpawnDirectorCore.CreateSlots(2);
        Assert.False(SpawnDirectorCore.MarkSlotAlive(slots, 9, 1UL));
    }

    [Fact]
    public void MarkSlotAlive_ClearsRespawnTick()
    {
        var slots = SpawnDirectorCore.CreateSlots(1);
        SpawnDirectorCore.MarkSlotDead(slots, 0, 0, TicksPerSecond, 15f);
        Assert.True(slots[0].RespawnAtTick > 0);

        SpawnDirectorCore.MarkSlotAlive(slots, 0, 123UL);
        Assert.True(slots[0].Alive);
        Assert.Equal(123UL, slots[0].EntityId);
        Assert.Equal(0L, slots[0].RespawnAtTick);
    }

    // ---------- 实体 ID 确定性 ----------

    [Fact]
    public void ComputeEntityId_IsStableAndUniqueAcrossSlotsAndPoints()
    {
        var a0 = SpawnDirectorCore.ComputeEntityId(1, 0);
        var a1 = SpawnDirectorCore.ComputeEntityId(1, 1);
        var b0 = SpawnDirectorCore.ComputeEntityId(2, 0);

        Assert.NotEqual(a0, a1);
        Assert.NotEqual(a0, b0);
        Assert.NotEqual(a1, b0);

        // 同一输入必然同一输出（跨重生保持一致身份）。
        Assert.Equal(a0, SpawnDirectorCore.ComputeEntityId(1, 0));
    }

    [Fact]
    public void ComputeEntityId_EncodesSpawnPointInHighBits()
    {
        // 低 32 位 = 槽位序号 + 1（0 保留为"无实体"），故 slot 0 → lo = 1。
        Assert.Equal((1UL << 32) | 1UL, SpawnDirectorCore.ComputeEntityId(1, 0));
        Assert.Equal((2UL << 32) | 3UL, SpawnDirectorCore.ComputeEntityId(2, 2));

        // 实体 ID 永不为 0（0 是"无实体"哨兵值，不能与真实怪冲突）。
        Assert.NotEqual(0UL, SpawnDirectorCore.ComputeEntityId(0, 0));
    }

    // ---------- 出生坐标 ----------

    [Fact]
    public void ComputeSpawnPosition_SpreadsSlotsSymmetricallyAroundCenter()
    {
        var cfg = MakeConfig(maxAlive: 3);
        cfg.X = 100f;
        cfg.Y = 200f;
        cfg.Z = 5f;
        cfg.SlotSpacing = 8f;

        var s0 = SpawnDirectorCore.ComputeSpawnPosition(cfg, 0);
        var s1 = SpawnDirectorCore.ComputeSpawnPosition(cfg, 1);
        var s2 = SpawnDirectorCore.ComputeSpawnPosition(cfg, 2);

        Assert.Equal(92f, s0.X, 4);
        Assert.Equal(100f, s1.X, 4);
        Assert.Equal(108f, s2.X, 4);

        Assert.All(new[] { s0, s1, s2 }, s =>
        {
            Assert.Equal(200f, s.Y, 4);
            Assert.Equal(5f, s.Z, 4);
        });
    }

    [Fact]
    public void ComputeSpawnPosition_SingleSlot_SitsOnCenter()
    {
        var cfg = MakeConfig(maxAlive: 1);
        var s = SpawnDirectorCore.ComputeSpawnPosition(cfg, 0);
        Assert.Equal(cfg.X, s.X, 4);
    }

    [Fact]
    public void Tick_SpawnDecisionCarriesConfiguredPosition()
    {
        var cfg = MakeConfig(maxAlive: 2);
        var slots = SpawnDirectorCore.CreateSlots(2);

        var d = SpawnDirectorCore.Tick(cfg, slots, 0, TicksPerSecond);

        Assert.Equal(2, d.Spawns.Length);
        Assert.Equal(cfg.Y, d.Spawns[0].Y, 4);
        Assert.Equal(cfg.Z, d.Spawns[0].Z, 4);
        // 两槽位不应重叠。
        Assert.NotEqual(d.Spawns[0].X, d.Spawns[1].X);
    }

    // ---------- 确定性 ----------

    [Fact]
    public void Tick_IsBitwiseDeterministic_AcrossRepeatedRuns()
    {
        var cfg = MakeConfig(maxAlive: 4);
        var slots = SpawnDirectorCore.CreateSlots(4);
        SpawnDirectorCore.MarkSlotAlive(slots, 0, SpawnDirectorCore.ComputeEntityId(cfg.SpawnPointId, 0));
        SpawnDirectorCore.MarkSlotDead(slots, 1, 500, TicksPerSecond, 15f);

        var first = SpawnDirectorCore.Tick(cfg, slots, 700, TicksPerSecond);
        for (var i = 0; i < 100; i++)
        {
            var again = SpawnDirectorCore.Tick(cfg, slots, 700, TicksPerSecond);
            Assert.Equal(first.Spawns.Length, again.Spawns.Length);
            for (var j = 0; j < first.Spawns.Length; j++)
            {
                Assert.Equal(first.Spawns[j].EntityId, again.Spawns[j].EntityId);
                Assert.Equal(first.Spawns[j].SlotIndex, again.Spawns[j].SlotIndex);
                Assert.Equal(BitConverter.SingleToInt32Bits(first.Spawns[j].X), BitConverter.SingleToInt32Bits(again.Spawns[j].X));
            }
        }
    }

    [Fact]
    public void FullLifecycle_KillAndRespawnSequence_IsReproducible()
    {
        // 模拟 30 秒：全部生成 → 全灭 → 到期重生，断言两次运行逐 tick 一致。
        static string Run()
        {
            var cfg = MakeConfig(maxAlive: 3, respawnSeconds: 5f);
            var slots = SpawnDirectorCore.CreateSlots(3);
            var sb = new System.Text.StringBuilder();

            for (var tick = 0; tick < 30 * TicksPerSecond; tick++)
            {
                var d = SpawnDirectorCore.Tick(cfg, slots, tick, TicksPerSecond);
                foreach (var s in d.Spawns)
                {
                    SpawnDirectorCore.MarkSlotAlive(slots, s.SlotIndex, s.EntityId);
                    sb.Append("S").Append(s.SlotIndex).Append('@').Append(tick).Append(';');
                }

                // 生成后第 60 tick 一次性全灭（模拟玩家清场）。
                if (tick == TicksPerSecond)
                {
                    for (var i = 0; i < slots.Length; i++)
                    {
                        if (slots[i].Alive)
                        {
                            SpawnDirectorCore.MarkSlotDead(slots, i, tick, TicksPerSecond, cfg.RespawnDelaySeconds);
                            sb.Append("D").Append(i).Append('@').Append(tick).Append(';');
                        }
                    }
                }
            }

            return sb.ToString();
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a, b);

        // 反假绿：必须真的发生了生成 → 死亡 → 重生三段事件。
        Assert.Contains("S0@0;", a, StringComparison.Ordinal);
        Assert.Contains("D0@60;", a, StringComparison.Ordinal);
        Assert.Contains("S0@360;", a, StringComparison.Ordinal);
    }

    [Fact]
    public void CountAlive_ReflectsSlots()
    {
        var slots = SpawnDirectorCore.CreateSlots(3);
        Assert.Equal(0, SpawnDirectorCore.CountAlive(slots));

        SpawnDirectorCore.MarkSlotAlive(slots, 1, 7UL);
        Assert.Equal(1, SpawnDirectorCore.CountAlive(slots));

        Assert.Equal(0, SpawnDirectorCore.CountAlive(Array.Empty<SpawnSlotState>()));
    }

    [Fact]
    public void CreateSlots_NonPositiveCount_ReturnsEmpty()
    {
        Assert.Empty(SpawnDirectorCore.CreateSlots(0));
        Assert.Empty(SpawnDirectorCore.CreateSlots(-3));
    }

    // ---------- 配置投影 ----------

    [Fact]
    public void ToAiConfig_ProjectsGameplayNumbers()
    {
        var cfg = MakeConfig();
        cfg.MaxHp = 250;
        cfg.AttackPower = 33;
        cfg.MoveSpeed = 5.5f;
        cfg.AggroRadius = 12f;
        cfg.LeashRadius = 25f;
        cfg.WanderRadius = 4f;
        cfg.RespawnDelaySeconds = 20f;

        var ai = cfg.ToAiConfig();

        Assert.Equal(250, ai.MaxHp);
        Assert.Equal(33, ai.AttackPower);
        Assert.Equal(5.5f, ai.MoveSpeed, 4);
        Assert.Equal(12f, ai.AggroRadius, 4);
        Assert.Equal(25f, ai.LeashRadius, 4);
        Assert.Equal(4f, ai.WanderRadius, 4);
        Assert.Equal(20f, ai.RespawnDelaySeconds, 4);
    }

    [Fact]
    public void Version_IsPositive_AndDocumentedAsContract()
    {
        Assert.True(SpawnDirectorCore.Version >= 1);
    }
}
