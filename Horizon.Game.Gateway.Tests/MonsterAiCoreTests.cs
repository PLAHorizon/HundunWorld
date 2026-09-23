using System;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M3.3 —— 确定性怪物 AI 内核单元测试。
/// <para>
/// 覆盖目标（验收门 H3-b）：六态机的合法迁移、追击/攻击/脱战回归、
/// 同距离目标的确定性选择、以及**同输入产生位级一致输出**（金轨迹对拍的前提）。
/// </para>
/// <para>
/// 与移动公式同级的确定性纪律：任何改变状态迁移或位移的改动都必须递增
/// <see cref="MonsterAiCore.Version"/> 并在此补充回归断言。
/// </para>
/// </summary>
public class MonsterAiCoreTests
{
    private const float Dt = 1f / 60f;

    private static MonsterAiSelf MakeSelf(
        MonsterAiState state = MonsterAiState.Idle,
        float x = 0f, float y = 0f,
        int hp = 100,
        float stateElapsed = 0f,
        float attackCooldown = 0f,
        float spawnX = 0f, float spawnY = 0f,
        float patrolDirection = 1f,
        ulong threat = 0UL,
        long respawnAtTick = 0)
        => new MonsterAiSelf
        {
            X = x,
            Y = y,
            Z = 0f,
            Yaw = 0f,
            SpawnX = spawnX,
            SpawnY = spawnY,
            SpawnZ = 0f,
            State = state,
            Hp = hp,
            AttackCooldownRemaining = attackCooldown,
            StateElapsedSeconds = stateElapsed,
            PatrolDirection = patrolDirection,
            RespawnAtTick = respawnAtTick,
            ThreatTargetId = threat,
        };

    private static MonsterAiConfig MakeConfig() => MonsterAiConfig.Default;

    private static MonsterAiTarget Target(ulong id, float x, float y, bool alive = true, bool attackable = true)
        => new MonsterAiTarget { EntityId = id, X = x, Y = y, Z = 0f, IsAlive = alive, IsAttackable = attackable };

    // ---------- 基础：非法输入早退 ----------

    [Fact]
    public void Decide_NonPositiveDt_DoesNotAdvance()
    {
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f);
        var targets = new[] { Target(1, 5f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, 0f, MakeConfig(), serverTick: 100);

        Assert.Equal(0f, d.DeltaX);
        Assert.Equal(0f, d.DeltaY);
        Assert.False(d.Moved);
        Assert.Equal(MonsterAiState.Chase, d.NextState);
    }

    // ---------- Idle → Patrol ----------

    [Fact]
    public void Idle_BeforeThreshold_StaysIdle()
    {
        var self = MakeSelf(MonsterAiState.Idle, stateElapsed: 1f);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), 0);
        Assert.Equal(MonsterAiState.Idle, d.NextState);
        Assert.False(d.Moved);
    }

    [Fact]
    public void Idle_AfterThreshold_TransitionsToPatrol()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Idle, stateElapsed: cfg.IdleToPatrolSeconds);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);
        Assert.Equal(MonsterAiState.Patrol, d.NextState);
    }

    // ---------- Idle/Patrol → Chase（仇恨发现）----------

    [Fact]
    public void Idle_TargetInAggroRange_TransitionsToChase()
    {
        var self = MakeSelf(MonsterAiState.Idle, stateElapsed: 0f);
        var targets = new[] { Target(42, 5f, 0f) }; // 5m < AggroRadius(10m)

        var d = MonsterAiCore.Decide(self, targets, Dt, MakeConfig(), 0);

        Assert.Equal(MonsterAiState.Chase, d.NextState);
        Assert.Equal(42UL, d.ThreatTargetId);
    }

    [Fact]
    public void Idle_TargetOutsideAggroRange_StaysIdle()
    {
        var self = MakeSelf(MonsterAiState.Idle, stateElapsed: 0f);
        var targets = new[] { Target(42, 50f, 0f) }; // 50m > AggroRadius(10m)

        var d = MonsterAiCore.Decide(self, targets, Dt, MakeConfig(), 0);

        Assert.Equal(MonsterAiState.Idle, d.NextState);
        Assert.Equal(0UL, d.ThreatTargetId);
    }

    [Fact]
    public void Aggro_DeadOrNonAttackableTargets_AreIgnored()
    {
        var self = MakeSelf(MonsterAiState.Idle);
        var targets = new[]
        {
            Target(1, 3f, 0f, alive: false),
            Target(2, 4f, 0f, attackable: false),
        };

        var d = MonsterAiCore.Decide(self, targets, Dt, MakeConfig(), 0);

        Assert.Equal(0UL, d.ThreatTargetId);
        Assert.Equal(MonsterAiState.Idle, d.NextState);
    }

    // ---------- Chase：直线追击与射程 ----------

    [Fact]
    public void Chase_MovesTowardTarget_AtConfiguredSpeed()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
        var targets = new[] { Target(7, 20f, 0f) }; // 20m > AttackRange

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Chase, d.NextState);
        Assert.True(d.Moved);
        // 沿 +X 直线：位移 = speed * dt，Y 分量应为 0。
        Assert.Equal(cfg.MoveSpeed * Dt, d.DeltaX, 5);
        Assert.Equal(0f, d.DeltaY);
    }

    [Fact]
    public void Chase_StepNeverOvershootsTarget()
    {
        var cfg = MakeConfig();
        // 目标极近但在射程外（射程 2.5m），一步步长 4m/s*1/60 = 0.0667m —— 远小于距离。
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
        var targets = new[] { Target(7, 3f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.True(d.DeltaX <= 3f);
    }

    [Fact]
    public void Chase_StepClampsToRemainingDistance()
    {
        // 用超大 dt 让单步位移超过剩余距离，验证截断不越过目标。
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
        var targets = new[] { Target(7, 3f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, dt: 10f, cfg, 0);

        Assert.Equal(3f, d.DeltaX, 5);
    }

    [Fact]
    public void Chase_TargetWithinAttackRange_TransitionsToAttack()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
        var targets = new[] { Target(7, 2f, 0f) }; // 2m <= AttackRange 2.5m

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Attack, d.NextState);
        Assert.False(d.Moved);
    }

    // ---------- Attack：冷却驱动 ----------

    [Fact]
    public void Attack_CooldownReady_FiresAndResetsCooldown()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Attack, x: 0f, y: 0f, threat: 7UL, attackCooldown: 0f);
        var targets = new[] { Target(7, 2f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.True(d.WantsAttack);
        Assert.Equal(7UL, d.AttackTargetId);
        Assert.Equal(cfg.AttackCooldownSeconds, d.NextAttackCooldownRemaining, 5);
    }

    [Fact]
    public void Attack_CooldownNotReady_DoesNotFire()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Attack, x: 0f, y: 0f, threat: 7UL, attackCooldown: 5f);
        var targets = new[] { Target(7, 2f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.False(d.WantsAttack);
        Assert.Equal(5f - Dt, d.NextAttackCooldownRemaining, 4);
    }

    [Fact]
    public void Attack_CooldownDecrementsEvenWhileIdle()
    {
        // 脱战期间冷却不应"冻结"（否则回归后再接战会异常延迟）。
        var self = MakeSelf(MonsterAiState.Idle, attackCooldown: 0.5f);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), 0);
        Assert.True(d.NextAttackCooldownRemaining < 0.5f);
    }

    [Fact]
    public void Attack_CooldownNeverGoesNegative()
    {
        var self = MakeSelf(MonsterAiState.Idle, attackCooldown: 0.001f);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), 0);
        Assert.Equal(0f, d.NextAttackCooldownRemaining);
    }

    // ---------- Leash：脱战回归 ----------

    [Fact]
    public void Chase_FarFromSpawn_TransitionsToLeash()
    {
        var cfg = MakeConfig();
        // 距出生点 40m > LeashRadius 30m
        var self = MakeSelf(MonsterAiState.Chase, x: 40f, y: 0f, threat: 7UL, spawnX: 0f, spawnY: 0f);
        var targets = new[] { Target(7, 45f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Leash, d.NextState);
        Assert.False(d.WantsAttack);
    }

    [Fact]
    public void Leash_MovesBackTowardSpawn_AndClearsThreat()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Leash, x: 40f, y: 0f, spawnX: 0f, spawnY: 0f, threat: 7UL);

        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Leash, d.NextState);
        Assert.True(d.Moved);
        Assert.True(d.DeltaX < 0f); // 沿 -X 回归
        Assert.Equal(0UL, d.ThreatTargetId);
    }

    [Fact]
    public void Leash_ReachingSpawn_TransitionsToIdle()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Leash, x: 0.2f, y: 0f, spawnX: 0f, spawnY: 0f);

        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Idle, d.NextState);
    }

    [Fact]
    public void Leash_IgnoresNearbyTargets_UntilHome()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Leash, x: 40f, y: 0f, spawnX: 0f, spawnY: 0f);
        var targets = new[] { Target(7, 41f, 0f) }; // 近在咫尺也不重新仇恨

        var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Leash, d.NextState);
        Assert.False(d.WantsAttack);
    }

    // ---------- 死亡与重生 ----------

    [Fact]
    public void ZeroHp_TransitionsToDead()
    {
        var self = MakeSelf(MonsterAiState.Attack, hp: 0, threat: 7UL);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), 1000);

        Assert.Equal(MonsterAiState.Dead, d.NextState);
        Assert.Equal(0UL, d.ThreatTargetId);
        Assert.False(d.Moved);
    }

    [Fact]
    public void Dead_BeforeRespawnTick_StaysDead()
    {
        var self = MakeSelf(MonsterAiState.Dead, hp: 0, respawnAtTick: 2000);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), serverTick: 1500);

        Assert.Equal(MonsterAiState.Dead, d.NextState);
    }

    [Fact]
    public void Dead_AtRespawnTick_TransitionsToIdle()
    {
        var self = MakeSelf(MonsterAiState.Dead, hp: 0, respawnAtTick: 2000);
        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, MakeConfig(), serverTick: 2000);

        Assert.Equal(MonsterAiState.Idle, d.NextState);
        Assert.Equal(0f, d.NextAttackCooldownRemaining);
    }

    [Fact]
    public void ComputeRespawnTick_UsesTicksPerSecond()
    {
        Assert.Equal(1000 + 15 * 60, MonsterAiCore.ComputeRespawnTick(1000, 60, 15f));
    }

    [Fact]
    public void ComputeRespawnTick_NonPositiveTicksPerSecond_FallsBackTo60()
    {
        Assert.Equal(0 + 60, MonsterAiCore.ComputeRespawnTick(0, 0, 1f));
    }

    // ---------- 目标选择确定性 ----------

    [Fact]
    public void SelectThreat_PicksNearestTarget()
    {
        var self = MakeSelf(MonsterAiState.Idle);
        var targets = new[]
        {
            Target(1, 9f, 0f),
            Target(2, 3f, 0f),
            Target(3, 7f, 0f),
        };

        var d = MonsterAiCore.Decide(self, targets, Dt, MakeConfig(), 0);

        Assert.Equal(2UL, d.ThreatTargetId);
    }

    [Fact]
    public void SelectThreat_TieOnDistance_PicksSmallerEntityId()
    {
        var self = MakeSelf(MonsterAiState.Idle);
        // 两个目标与怪物等距（3m）；按 EntityId 升序传入 → 选较小 ID。
        var targets = new[] { Target(5, 3f, 0f), Target(9, -3f, 0f) };

        var d = MonsterAiCore.Decide(self, targets, Dt, MakeConfig(), 0);

        Assert.Equal(5UL, d.ThreatTargetId);
    }

    [Fact]
    public void SortTargetsByEntityId_OrdersAscendingAcrossUnsortedInput()
    {
        var targets = new[]
        {
            Target(9, 0f, 0f),
            Target(2, 0f, 0f),
            Target(7, 0f, 0f),
            Target(1, 0f, 0f),
        };

        MonsterAiCore.SortTargetsByEntityId(targets);

        Assert.Equal(new ulong[] { 1, 2, 7, 9 }, Array.ConvertAll(targets, t => t.EntityId));
    }

    [Fact]
    public void SortTargetsByEntityId_EmptyAndSingle_AreNoOps()
    {
        var empty = Array.Empty<MonsterAiTarget>();
        MonsterAiCore.SortTargetsByEntityId(empty);

        var single = new[] { Target(5, 0f, 0f) };
        MonsterAiCore.SortTargetsByEntityId(single);
        Assert.Equal(5UL, single[0].EntityId);
    }

    // ---------- 确定性：金轨迹对拍前提 ----------

    [Fact]
    public void Decide_IsBitwiseDeterministic_AcrossRepeatedRuns()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Chase, x: 1.25f, y: -3.5f, threat: 11UL);
        var targets = new[] { Target(11, 18.75f, 4.125f), Target(12, 2f, 2f) };

        var first = MonsterAiCore.Decide(self, targets, Dt, cfg, 12345);
        for (var i = 0; i < 200; i++)
        {
            var again = MonsterAiCore.Decide(self, targets, Dt, cfg, 12345);
            Assert.Equal(BitConverter.SingleToInt32Bits(first.DeltaX), BitConverter.SingleToInt32Bits(again.DeltaX));
            Assert.Equal(BitConverter.SingleToInt32Bits(first.DeltaY), BitConverter.SingleToInt32Bits(again.DeltaY));
            Assert.Equal(BitConverter.SingleToInt32Bits(first.NextYaw), BitConverter.SingleToInt32Bits(again.NextYaw));
            Assert.Equal(first.NextState, again.NextState);
            Assert.Equal(first.AttackTargetId, again.AttackTargetId);
        }
    }

    [Fact]
    public void Decide_FullChaseSequence_IsReproducibleBitForBit()
    {
        // 用一个"从远处追到射程内"的多 tick 序列验证整条轨迹可按位复现——
        // 这是把怪物 AI 纳入金轨迹对拍（M5.1）的最小可行前提。
        // 目标距离取 20m：必须 < LeashRadius(30m)，否则怪物会先脱战回归而测不到追击轨迹。
        static string Run()
        {
            var cfg = MakeConfig();
            var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
            var targets = new[] { Target(7, 20f, 0f) };
            var sb = new System.Text.StringBuilder();

            for (var tick = 0; tick < 400; tick++)
            {
                var d = MonsterAiCore.Decide(self, targets, Dt, cfg, tick);
                self.X += d.DeltaX;
                self.Y += d.DeltaY;
                self.State = d.NextState;
                self.AttackCooldownRemaining = d.NextAttackCooldownRemaining;
                self.ThreatTargetId = d.ThreatTargetId;
                sb.Append(BitConverter.SingleToInt32Bits(self.X)).Append('|')
                  .Append(BitConverter.SingleToInt32Bits(self.Y)).Append('|')
                  .Append((byte)self.State).Append(';');
            }

            return sb.ToString();
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a, b);

        // 反假绿断言：序列必须真的推进过（怪物确实逼近了目标），
        // 否则"两次都早退"也会相等，测试就失去意义。
        // 20 米 / 4 米每秒 = 5 秒 = 300 tick；跑 400 tick 应已进入攻击射程。
        // 注意目标距离必须 < LeashRadius(30m)，否则会先触发脱战回归。
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Chase, x: 0f, y: 0f, threat: 7UL);
        var targets = new[] { Target(7, 20f, 0f) };
        for (var tick = 0; tick < 400; tick++)
        {
            var d = MonsterAiCore.Decide(self, targets, Dt, cfg, tick);
            self.X += d.DeltaX;
            self.Y += d.DeltaY;
            self.State = d.NextState;
            self.AttackCooldownRemaining = d.NextAttackCooldownRemaining;
            self.ThreatTargetId = d.ThreatTargetId;
        }
        Assert.True(self.X > 5f, $"怪物应已逼近目标，实际 X={self.X}");
        Assert.Equal(MonsterAiState.Attack, self.State);
        // 进入射程后应稳定在射程内（不越过目标）。
        Assert.True(self.X <= 20f, $"怪物不应越过目标，实际 X={self.X}");
    }

    // ---------- 巡逻边界 ----------

    [Fact]
    public void Patrol_WithinWanderRadius_MovesAlongPatrolDirection()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Patrol, x: 0f, y: 0f, spawnX: 0f, spawnY: 0f, patrolDirection: 1f);

        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Patrol, d.NextState);
        Assert.True(d.DeltaX > 0f);
    }

    [Fact]
    public void Patrol_ReachingWanderBoundary_StopsMoving()
    {
        var cfg = MakeConfig();
        // 已在游荡半径边缘，下一步会越界 → 停在边界（不移动）。
        var self = MakeSelf(MonsterAiState.Patrol, x: cfg.WanderRadius, y: 0f, spawnX: 0f, spawnY: 0f, patrolDirection: 1f);

        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);

        Assert.Equal(MonsterAiState.Patrol, d.NextState);
        Assert.False(d.Moved);
        Assert.Equal(0f, d.DeltaX);
    }

    [Fact]
    public void Patrol_NegativeDirection_MovesOppositeWay()
    {
        var cfg = MakeConfig();
        var self = MakeSelf(MonsterAiState.Patrol, x: 0f, y: 0f, spawnX: 0f, spawnY: 0f, patrolDirection: -1f);

        var d = MonsterAiCore.Decide(self, ReadOnlySpan<MonsterAiTarget>.Empty, Dt, cfg, 0);

        Assert.True(d.DeltaX < 0f);
    }

    // ---------- 版本契约 ----------

    [Fact]
    public void Version_IsPositive_AndDocumentedAsContract()
    {
        // 版本号是对拍锚点：任何影响位置/状态迁移的改动都必须递增。
        Assert.True(MonsterAiCore.Version >= 1);
    }
}
