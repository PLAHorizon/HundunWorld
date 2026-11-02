using System;
using Horizon.Game.Sim;
using Horizon.Game.Sim.Monsters;
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
        long respawnAtTick = 0,
        ulong tauntTarget = 0UL,
        long tauntUntil = 0L,
        long graceUntil = 0L,
        long leashExtension = 0L)
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
            // v2 那三条口径的输入（全部缺省 = 不开，用来钉"改动前后逐字相同"）。
            TauntTargetId = tauntTarget,
            TauntUntilTick = tauntUntil,
            NoInitiateUntilTick = graceUntil,
            LastLeashExtensionTick = leashExtension,
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

    // ---------- 风筝宽限窗（AC 的 leash 计时器） ----------

    [Fact]
    public void 风筝宽限窗按AC的等级公式给十一到十六秒()
    {
        // AC `Creature::GetLeashTimer`：`11 + minlevel/10 - 2`，等级 < 20 时 uint8 下溢再被 max 压回 11。
        Assert.Equal(11, MonsterAiGate.LeashExtensionSeconds(1));
        Assert.Equal(11, MonsterAiGate.LeashExtensionSeconds(19));
        Assert.Equal(11, MonsterAiGate.LeashExtensionSeconds(20));
        Assert.Equal(12, MonsterAiGate.LeashExtensionSeconds(30));
        Assert.Equal(16, MonsterAiGate.LeashExtensionSeconds(70));
        Assert.Equal(11, MonsterAiGate.LeashExtensionSeconds(0));    // 没带等级 ⇒ 下限
    }

    [Fact]
    public void 从没挨过打的怪没有宽限窗而把零当时间点就会让全场免检()
    {
        // 0 的两义：从未挨打 / tick 0 挨打。判据必须是合取，否则开局 11 秒内所有怪都免检距离。
        Assert.False(MonsterAiGate.KitingGraceActive(100, 0, 660));
        Assert.True(MonsterAiGate.KitingGraceActive(100, 100, 660));
        Assert.True(MonsterAiGate.KitingGraceActive(759, 100, 660));
        Assert.False(MonsterAiGate.KitingGraceActive(760, 100, 660));
        Assert.False(MonsterAiGate.KitingGraceActive(99, 100, 660));   // 起点在未来（时钟回拨）不算
    }

    [Fact]
    public void 刚挨过打的怪被拉出脱战半径也不回家而窗过期后才回()
    {
        var cfg = MakeConfig();                       // LeashRadius 30、level 缺省 ⇒ 窗 11 秒 = 660 拍
        var targets = new[] { Target(7, 45f, 0f) };

        // 同一位置 40 > 30：有宽限窗 ⇒ 照追（上面 `Chase_FarFromSpawn_TransitionsToLeash` 是不带窗的反照）
        var recent = MakeSelf(MonsterAiState.Chase, x: 40f, y: 0f, threat: 7UL, spawnX: 0f, spawnY: 0f,
                              leashExtension: 100);
        var d1 = MonsterAiCore.Decide(recent, targets, Dt, cfg, 100);
        Assert.Equal(MonsterAiState.Chase, d1.NextState);

        // 窗过期（100 + 660 = 760）⇒ 同一具夹具立刻回家
        var expired = MakeSelf(MonsterAiState.Chase, x: 40f, y: 0f, threat: 7UL, spawnX: 0f, spawnY: 0f,
                               leashExtension: 100);
        var d2 = MonsterAiCore.Decide(expired, targets, Dt, cfg, 760);
        Assert.Equal(MonsterAiState.Leash, d2.NextState);
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

    // ---------- v2：三条 AzerothCore 口径（判据住在 MonsterAiGate，与 v2 状态机共用同一个主人） ----------

    [Fact]
    public void RespawnGrace_BlocksNewInitiation_ButAnsweredBlowStillRetaliates()
    {
        // 玩家站在 5 米（仇恨半径 10 米内），怪在 Idle —— 没有宽限时它必须立刻转 Chase。
        var cfg = MakeConfig();
        var targets = new[] { Target(7001, 5f, 0f) };
        var fresh = MakeSelf(MonsterAiState.Idle, graceUntil: 100);

        // 窗内：不许**主动**起手，而且连"锁定"都不写——
        // 窗内先写进仇恨、窗过再转 Chase，与"起手"没有区别，AC 挡的是后者。
        var inside = MonsterAiCore.Decide(fresh, targets, Dt, cfg, serverTick: 50);
        Assert.Equal(MonsterAiState.Idle, inside.NextState);
        Assert.Equal(0UL, inside.ThreatTargetId);

        // 窗口一过立刻恢复原有表现（5 秒是限时的，不是"这只怪从此不咬人"）。
        var after = MonsterAiCore.Decide(fresh, targets, Dt, cfg, serverTick: 100);
        Assert.Equal(MonsterAiState.Chase, after.NextState);

        // AC 那条判据带的例外是 `!IsEngagedBy(victim)`：先挨过手的，窗内照样还手。
        // （宿主在伤害结算里把攻击者写进单槽仇恨，正是这一档的输入。）
        var alreadyHit = MakeSelf(MonsterAiState.Idle, graceUntil: 100, threat: 7001);
        var retaliate = MonsterAiCore.Decide(alreadyHit, targets, Dt, cfg, serverTick: 50);
        Assert.Equal(MonsterAiState.Chase, retaliate.NextState);
        Assert.Equal(7001UL, retaliate.ThreatTargetId);
    }

    [Fact]
    public void TauntWindow_AndWorldBossRank_SuppressTheLeashDistanceCheck()
    {
        var cfg = MakeConfig();                    // LeashRadius = 30
        var targets = new[] { Target(7001, 60f, 0f) };
        var far = MakeSelf(MonsterAiState.Chase, x: 60f, threat: 7001);

        // 对照：没有锁、普通档 ⇒ 超出脱战半径必须回家（这条不成立，下面两档就都是空转）。
        Assert.Equal(MonsterAiState.Leash, MonsterAiCore.Decide(far, targets, Dt, cfg, 100).NextState);

        // ① 嘲讽窗内不查距离：坦克把怪拉开 60 米，怪仍追着那个人——
        //    这一档存在的全部意义就是"那一嗓子不是白喊"。
        var taunted = MakeSelf(MonsterAiState.Chase, x: 60f, threat: 7001, tauntTarget: 7001, tauntUntil: 200);
        Assert.NotEqual(MonsterAiState.Leash, MonsterAiCore.Decide(taunted, targets, Dt, cfg, 100).NextState);

        // ② 窗一过立刻恢复（锁不是永久的）。
        Assert.Equal(MonsterAiState.Leash, MonsterAiCore.Decide(taunted, targets, Dt, cfg, 200).NextState);

        // ③ 三条出口之一：被钉的人不在场 ⇒ 锁当场撤、距离检查照做。
        //    少了这一档会露出"锁撤了但距离检查还被锁挡着"的木桩怪：既没目标又不回家。
        var taunterGone = MakeSelf(MonsterAiState.Chase, x: 60f, threat: 7001, tauntTarget: 9999, tauntUntil: 200);
        Assert.Equal(MonsterAiState.Leash, MonsterAiCore.Decide(taunterGone, targets, Dt, cfg, 100).NextState);

        // ④ 世界 BOSS 整段不查（AC 把这段写在 `!isWorldBoss()` 里）——同一份几何，只改等级档。
        var bossCfg = MakeConfig();
        bossCfg.Rank = Horizon.Game.Sim.Monsters.MonsterRank.WorldBoss;
        Assert.NotEqual(MonsterAiState.Leash, MonsterAiCore.Decide(far, targets, Dt, bossCfg, 100).NextState);
    }

    [Fact]
    public void Version2_WithAllThreeInputsOff_ReproducesTheV1Transitions()
    {
        // "涨的是能力不是手感"这句话要有可执行的形状：三格输入全缺省（0 / 0 / Normal）时，
        // 每一档状态迁移都必须与接这三条判据**之前**逐字相同。期望值按旧式子手填，
        // 不是拿新代码算一遍再比（那样测的是恒等式）。
        var cfg = MakeConfig();                    // Aggro=10 / Leash=30 / AttackRange=2.5
        // 目标一律摆在"怪前方 5 米"：这样它永远在仇恨半径内，
        // 唯一能改变结论的就是"距出生点有多远"——不然这一档测的是半径而不是脱战。
        var cases = new (MonsterAiState State, float X, ulong Threat, MonsterAiState Expected, string Why)[]
        {
            (MonsterAiState.Idle, 0f, 0UL, MonsterAiState.Chase, "仇恨半径内有人 → 起手追"),
            (MonsterAiState.Idle, 40f, 0UL, MonsterAiState.Idle, "人就在旁边但怪已超出脱战半径 → 不起手"),
            (MonsterAiState.Patrol, 0f, 0UL, MonsterAiState.Chase, "巡逻中发现目标 → 转追"),
            (MonsterAiState.Patrol, 40f, 0UL, MonsterAiState.Patrol, "巡逻中超出脱战半径 → 不转追（在原地处边界档）"),
            (MonsterAiState.Chase, 40f, 7001UL, MonsterAiState.Leash, "追出半径 → 回家"),
            (MonsterAiState.Chase, 0f, 7001UL, MonsterAiState.Chase, "在半径内追 → 继续追（还没进射程）"),
            (MonsterAiState.Attack, 40f, 7001UL, MonsterAiState.Leash, "攻击中被人拉出半径 → 回家"),
        };

        foreach (var c in cases)
        {
            var self = MakeSelf(c.State, x: c.X, threat: c.Threat);
            var near = new[] { Target(7001, c.X + 5f, 0f) };
            var d = MonsterAiCore.Decide(self, near, Dt, cfg, serverTick: 500);
            Assert.True(d.NextState == c.Expected,
                $"{c.Why}：状态 {c.State} 在 x={c.X} 应得 {c.Expected}，实得 {d.NextState}");
            Assert.True(d.NoInitiateUntilTick == 0L, "非复活路径不许偷偷装宽限");
        }

        // 复活那一 tick 才装宽限（另一个写方在宿主：刷怪点重建实体那一路）。
        var dead = MakeSelf(MonsterAiState.Dead, hp: 100, respawnAtTick: 100);
        var reborn = MonsterAiCore.Decide(dead, new[] { Target(7001, 5f, 0f) }, Dt, cfg,
                                          serverTick: 100, ticksPerSecond: 60);
        Assert.Equal(MonsterAiState.Idle, reborn.NextState);
        Assert.Equal(100L + (long)(Horizon.Game.Sim.Monsters.MonsterAiGate.RespawnGraceSeconds * 60),
                     reborn.NoInitiateUntilTick);
    }

    [Fact]
    public void LeashHome_HealsToFull_AndOpensImmunityWindow_ThatOnlyBlocksNewInitiation()
    {
        // v2 的 Evade 分支早就做这两件事（刷满血 + 不接新敌），而 v1 的 Leash 只走路——
        // 生产数据里全部种子怪都走 v1，于是"远程风筝把怪磨到一丝血再耗死"对玩家成立。
        var cfg = MakeConfig();
        cfg.MaxHp = 100;
        var targets = new[] { Target(7001, 0.5f, 0f) };          // 人就贴在出生点旁边

        var halfDead = MakeSelf(MonsterAiState.Leash, x: 0.2f, hp: 30, spawnX: 0f, spawnY: 0f);
        var home = MonsterAiCore.Decide(halfDead, targets, Dt, cfg, serverTick: 1000, ticksPerSecond: 60);
        Assert.Equal(MonsterAiState.Idle, home.NextState);
        Assert.True(home.HealedToFull, "回位没报\"刷满血\"：这一格就是玩家可以无伤磨怪的那条缝");
        Assert.Equal(100, home.HpAfterHeal);
        var until = home.EvadeImmuneUntilTick;
        Assert.True(until > 1000, $"回位免疫窗没装（截止 {until}，当前 tick 1000）");
        Assert.True(until - 1000 <= 10 * 60, $"免疫窗长得离谱（{until - 1000} tick）——秒数或换算有问题");

        // 窗内：贴着出生点的人不许被"起手"（这正是 WoW 里刚回家的怪那一下迟钝的表现）。
        var inWindow = MakeSelf(MonsterAiState.Idle, x: 0f, hp: 100, graceUntil: 0L);
        inWindow.EvadeImmuneUntilTick = until;
        var blocked = MonsterAiCore.Decide(inWindow, targets, Dt, cfg, serverTick: until - 1);
        Assert.Equal(0UL, blocked.ThreatTargetId);
        Assert.Equal(MonsterAiState.Idle, blocked.NextState);

        // 窗一过立刻恢复（否则"开窗"等于"这只怪从此聋了"）。
        var opened = MonsterAiCore.Decide(inWindow, targets, Dt, cfg, serverTick: until);
        Assert.Equal(7001UL, opened.ThreatTargetId);
        Assert.Equal(MonsterAiState.Chase, opened.NextState);

        // 两个窗都只挡"接新敌"：**已经锁定的目标必须留着**——
        // 挡了它，回位途中被人追上一刀，怪会既不追也不还手，变成一个会走路的木桩。
        var locked = MakeSelf(MonsterAiState.Idle, x: 0f, hp: 100, threat: 7001);
        locked.EvadeImmuneUntilTick = until;
        var kept = MonsterAiCore.Decide(locked, targets, Dt, cfg, serverTick: until - 1);
        Assert.Equal(7001UL, kept.ThreatTargetId);

        // 免疫秒数配 0 = **根本没窗**（不是"窗的截止正好等于这一 tick"那种看起来一样的写法）。
        var noWindowCfg = MakeConfig();
        noWindowCfg.EvadeImmunitySeconds = 0f;
        var noWindow = MonsterAiCore.Decide(halfDead, targets, Dt, noWindowCfg, serverTick: 1000, ticksPerSecond: 60);
        Assert.Equal(0L, noWindow.EvadeImmuneUntilTick);
        Assert.True(noWindow.EvadeImmuneUntilTick <= 1000,
                    $"秒数配 0 却装了窗（截止 {noWindow.EvadeImmuneUntilTick}）：下一 tick 就会被它挡住");
        // 同一 tick 上贴脸也必须能起手——窗是空的，任何一挡都不该发生。
        var sameTick = MonsterAiCore.Decide(MakeSelf(MonsterAiState.Idle, x: 0f, hp: 100), targets,
                                            Dt, noWindowCfg, serverTick: 1000);
        Assert.Equal(7001UL, sameTick.ThreatTargetId);

        // MaxHp 没配（≤0）时**不许**刷血：刷了就是把血量写成 0，等于回位自杀。
        var noMaxHpCfg = MakeConfig();
        noMaxHpCfg.MaxHp = 0;
        var noMaxHp = MonsterAiCore.Decide(halfDead, targets, Dt, noMaxHpCfg, serverTick: 1000, ticksPerSecond: 60);
        Assert.False(noMaxHp.HealedToFull, "MaxHp=0 还报刷满血：宿主会把怪的血写成 0");
    }

    [Fact]
    public void WalkingHome_DoesNotAcquireNewTargets_EvenWithZeroSecondImmunityWindow()
    {
        // AzerothCore 把回位免疫做成**状态**（进 evade 挂 UNIT_STATE_EVADE，到达点位才清），
        // 而本作以前只有"到家后 N 秒"那一半 ⇒ 秒数配 0 时，回家路上它可以随便接新敌。
        // 玩家看得见的是：怪正往家走，队友在旁边打起来了，它掉头就打——而 AC 是走到底才醒。
        var cfg = MakeConfig();
        cfg.EvadeImmunitySeconds = 0f;      // 把"秒数窗"整条关掉，只剩"整段路"这一条判据在挡
        var targets = new[] { Target(7001, 1f, 0f) };

        // 到家那一 tick（离出生点 0.2 米）：状态还是 Leash，所以这一 tick 仍然不许接新敌。
        var arriving = MakeSelf(MonsterAiState.Leash, x: 0.2f, hp: 100);
        var d = MonsterAiCore.Decide(arriving, targets, Dt, cfg, serverTick: 5000, ticksPerSecond: 60);
        Assert.Equal(MonsterAiState.Idle, d.NextState);
        Assert.Equal(0UL, d.ThreatTargetId);

        // 对照：同一份几何，只把状态换回 Idle ⇒ 必须立刻接敌。
        // （少了这一条，"路上不接敌"可能只是"这只怪永远不接敌"——两种错在断言里长得一样。）
        var idle = MakeSelf(MonsterAiState.Idle, x: 0.2f, hp: 100);
        var control = MonsterAiCore.Decide(idle, targets, Dt, cfg, serverTick: 5000, ticksPerSecond: 60);
        Assert.Equal(7001UL, control.ThreatTargetId);

        // 路上（还没到家）也一样：它只走路，人不许进它的仇恨。
        var walking = MakeSelf(MonsterAiState.Leash, x: 12f, hp: 100);
        var mid = MonsterAiCore.Decide(walking, new[] { Target(7001, 13f, 0f) }, Dt, cfg, 5000, 60);
        Assert.Equal(0UL, mid.ThreatTargetId);
        Assert.True(mid.Moved, "路上这一 tick 应该在往家走（否则测不到\"整段路\"这一段）");

        // 而"已经钉着的人"在路上会被放下——这不是"宽限例外"那一条：AC 的 evade 会整段清仇恨
        //（`_EnterEvadeMode` → 清威胁表 + 挂 UNIT_STATE_EVADE），本作 v1 的 Leash 分支每 tick 清单槽仇恨，
        // 这里钉住它，免得以后有人把"先挨的那一手照样还手"错当成"回位路上也别放手"。
        var lockedWhileWalking = MakeSelf(MonsterAiState.Leash, x: 12f, hp: 100, threat: 7001);
        var dropped = MonsterAiCore.Decide(lockedWhileWalking, new[] { Target(7001, 13f, 0f) }, Dt, cfg, 5000, 60);
        Assert.Equal(0UL, dropped.ThreatTargetId);
    }

    // ---------- 版本契约 ----------

    [Fact]
    public void Version_IsPositive_AndDocumentedAsContract()
    {
        // 版本号是对拍锚点：任何影响位置/状态迁移的改动都必须递增。
        Assert.True(MonsterAiCore.Version >= 1);
    }

    // ---------- 站桩档（follow_type=3）在 v1 的三条位移出口 ----------

    [Fact]
    public void 站桩档的v1怪三条位移出口都不动而同样几何的步行档会动()
    {
        // v1 以前连 FollowType 这个输入都没有 ⇒ 编辑器"移动方式=站桩"那一格对默认档
        // （生产里全部种子怪 ai_profile=0）不成立：巡逻游荡 / 追击 / 回位三条直接写位移的分支
        // 一条都不问（v2 那边长在 MoveToward 的出口）。
        // 对照档用**同一份几何**、只换那一格——否则"不动"完全可能只是"这套夹具本来就动不了"。
        static MonsterAiDecision Decide(
            Horizon.Game.Sim.Monsters.MonsterFollowType follow, MonsterAiState state,
            float spawnX, ulong threat, float targetX)
        {
            var cfg = MonsterAiConfig.Default;
            cfg.FollowType = follow;
            var self = new MonsterAiSelf
            {
                X = 0f, Y = 0f, Z = 0f, Yaw = 0f,
                SpawnX = spawnX, SpawnY = 0f, SpawnZ = 0f,
                State = state, Hp = 100,
                StateElapsedSeconds = 10f,      // 早就过了 Idle→Patrol 那一档
                PatrolDirection = 1f,
                ThreatTargetId = threat,
            };
            var targets = new[]
            {
                new MonsterAiTarget
                {
                    EntityId = 91UL, X = targetX, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true,
                },
            };
            return MonsterAiCore.Decide(self, targets, 1f / 60f, cfg, 1000L, 60);
        }

        var walk = Horizon.Game.Sim.Monsters.MonsterFollowType.GroundWalk;
        var post = Horizon.Game.Sim.Monsters.MonsterFollowType.Static;

        // ① 巡逻游荡（v1 这一支是直接写 DeltaX 的，最容易被漏）
        var patrolWalk = Decide(walk, MonsterAiState.Patrol, 0f, 0UL, 400f);
        var patrolPost = Decide(post, MonsterAiState.Patrol, 0f, 0UL, 400f);
        Assert.True(patrolWalk.Moved && Math.Abs(patrolWalk.DeltaX) > 0f,
                    "对照档（步行）在巡逻里没有位移：夹具本身动不了，那后面每句\"站桩不动\"都是在空转");
        Assert.True(!patrolPost.Moved && patrolPost.DeltaX == 0f && patrolPost.DeltaY == 0f,
                    $"站桩档在巡逻分支里仍然漂了 {patrolPost.DeltaX}：那一格对 v1 还是装饰");

        // ② 追击（目标 40 米外而攻击射程 2.5 米 ⇒ 步行档一定是在追）
        var chaseWalk = Decide(walk, MonsterAiState.Chase, 0f, 91UL, 40f);
        var chasePost = Decide(post, MonsterAiState.Chase, 0f, 91UL, 40f);
        Assert.True(chaseWalk.Moved, "对照档没追：这条\"站桩不追\"没有参照");
        Assert.True(!chasePost.Moved && chasePost.DeltaX == 0f,
                    $"站桩档还是会追击位移（{chasePost.DeltaX}）");

        // ③ 回位（把出生锚点挪到 60 米外）
        var leashWalk = Decide(walk, MonsterAiState.Leash, 60f, 0UL, 400f);
        var leashPost = Decide(post, MonsterAiState.Leash, 60f, 0UL, 400f);
        Assert.True(leashWalk.Moved && Math.Abs(leashWalk.DeltaX) > 0f, "对照档没往家走：这条没有参照");
        Assert.True(!leashPost.Moved && leashPost.DeltaX == 0f,
                    $"站桩档还是会走回家（{leashPost.DeltaX}）");

        // ④ 站桩档**不进入**回位：走不回去的怪一旦被判成"正在回位"就永远出不去那个状态，
        //    而切片 18 之后"回位中打不到"是生效的 ⇒ 它会变成一只永远打不到的怪。
        var farWalk = Decide(walk, MonsterAiState.Chase, 60f, 91UL, 0.5f);
        var farPost = Decide(post, MonsterAiState.Chase, 60f, 91UL, 0.5f);
        Assert.Equal(MonsterAiState.Leash, farWalk.NextState);    // 步行档：离家 60 > 脱战 30 ⇒ 回位
        Assert.NotEqual(MonsterAiState.Leash, farPost.NextState); // 站桩档：没有"回家"这件事

        // ⑤ 不动但打得人：站桩炮台在射程内仍要出手（这条只抹位移，不抹攻击）
        var inRange = Decide(post, MonsterAiState.Attack, 0f, 91UL, 2f);
        Assert.True(inRange.WantsAttack, "站桩档连攻击一起被抹掉了：炮台本该\"不动而打得人\"");
        Assert.True(!inRange.Moved && inRange.DeltaX == 0f, "站桩炮台出手的同时还挪了位置");
    }

    [Fact]
    public void 被动档的v1怪不看上人但先挨的那一手照样还手()
    {
        // v1 以前没有"被动怪"这个输入：内容表 `is_aggressive=0` 只在 v2 那两处 inline 生效
        // ⇒ 默认档（生产里全部种子怪）配成"不打你先手"也照样先咬人。
        // 判据主人现在是 MonsterAiGate.MayInitiateOnSight，两路都只问它。
        static MonsterAiDecision Decide(bool isPassive, MonsterAiState state, ulong locked)
        {
            var cfg = MonsterAiConfig.Default;
            cfg.IsPassive = isPassive;
            var self = new MonsterAiSelf
            {
                X = 0f, Y = 0f, Z = 0f, Yaw = 0f,
                SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
                State = state, Hp = 100,
                StateElapsedSeconds = 0f,
                ThreatTargetId = locked,
            };
            // 玩家站在 5 米：Default 的仇恨半径是 10 米 ⇒ 半径内、射程（2.5 米）外。
            var targets = new[]
            {
                new MonsterAiTarget { EntityId = 91UL, X = 5f, Y = 0f, Z = 0f, IsAlive = true, IsAttackable = true },
            };
            return MonsterAiCore.Decide(self, targets, 1f / 60f, cfg, 1000L, 60);
        }

        // ① 对照档先证明"这套几何本来会主动起手"，否则下面的"被动不起手"可能是空转。
        var aggressiveIdle = Decide(isPassive: false, MonsterAiState.Idle, locked: 0UL);
        Assert.Equal(91UL, aggressiveIdle.ThreatTargetId);
        Assert.Equal(MonsterAiState.Chase, aggressiveIdle.NextState);

        // ② 同一份几何只换那一格：被动怪不起手。
        var passiveIdle = Decide(isPassive: true, MonsterAiState.Idle, locked: 0UL);
        Assert.Equal(0UL, passiveIdle.ThreatTargetId);
        Assert.NotEqual(MonsterAiState.Chase, passiveIdle.NextState);

        // ③ 先挨的那一手（宿主把攻击者写进单槽仇恨）不该被"被动"抹掉——
        //    否则表现会变成"打了它它还不还手"，那是另一条判据（复活宽限）的宽度，不是这一格的。
        var passiveLocked = Decide(isPassive: true, MonsterAiState.Idle, locked: 91UL);
        Assert.Equal(91UL, passiveLocked.ThreatTargetId);
        Assert.Equal(MonsterAiState.Chase, passiveLocked.NextState);

        // ④ 转火也算"主动起手"：被动怪在 Chase/Attack/Leash 里丢了目标之后不许立刻按半径找下一个。
        var passiveChaseNoLock = Decide(isPassive: true, MonsterAiState.Chase, locked: 0UL);
        Assert.Equal(0UL, passiveChaseNoLock.ThreatTargetId);
        var aggressiveChaseNoLock = Decide(isPassive: false, MonsterAiState.Chase, locked: 0UL);
        Assert.Equal(91UL, aggressiveChaseNoLock.ThreatTargetId);  // 同几何只换那一格，主动档马上转火
    }

    [Fact]
    public void v1的游荡速度用巡逻那一格而没配时退回追击速度()
    {
        // 以前 v1 的无路线游荡直接拿 MoveSpeed（那是"追你"的速度），而 v2 拿 WalkSpeed
        // ⇒ 种子内容 walk=2.5 / move=4 时，默认档的怪**溜达比冲刺快**，编辑器那一格是装饰。
        static float WanderDelta(float walk, float move)
        {
            var cfg = MonsterAiConfig.Default;
            cfg.MoveSpeed = move;
            cfg.WalkSpeed = walk;
            var self = new MonsterAiSelf
            {
                X = 0f, Y = 0f, Z = 0f, Yaw = 0f,
                SpawnX = 0f, SpawnY = 0f, SpawnZ = 0f,
                State = MonsterAiState.Patrol, Hp = 100,
                PatrolDirection = 1f,
            };
            var d = MonsterAiCore.Decide(self, System.Array.Empty<MonsterAiTarget>(), 1f / 60f, cfg, 1000L, 60);
            Assert.True(d.Moved, "夹具坏了：这一档根本没有位移，速度读数无从谈起");
            return d.DeltaX * 60f;     // 换算成"米/秒"
        }

        // ① 配了巡逻速度：按它走（这就是那一格到得了 v1 的证据）。
        Assert.Equal(2.5, WanderDelta(2.5f, 4f), 4);
        // ② 同一份夹具把巡逻速度改成别的数，读数必须跟着变（否则 ① 可能只是"随便一个速度"）。
        Assert.Equal(1.0, WanderDelta(1f, 4f), 4);
        // ③ **没配**（0）时退回追击速度：与接进这一格之前逐字相同（旧内容与金轨迹的护栏就在这半条上）。
        Assert.Equal(4.0, WanderDelta(0f, 4f), 4);
    }

    [Fact]
    public void v1的怪装上导航图之后不会踩进墙里而够不到五秒就脱战()
    {
        // v1 以前完全不知道导航图：装上图之后它会**穿墙**追人（切片 86 在 v2 修掉的同一个形状）。
        // 这里钉三半：墙挡路 ⇒ 位置停在墙前且 5 秒后脱战；空地 ⇒ 照常推进；没装图 ⇒ 与旧行为逐字相同（金轨迹的护栏）。
        const int w = 24, h = 8;
        byte[] Costs(bool wall)
        {
            var costs = new byte[w * h];
            for (var i = 0; i < costs.Length; i++) costs[i] = (byte)Horizon.Game.Sim.Monsters.MonsterNavGrid.BaseCellCost;
            if (wall) for (var y = 0; y < h; y++) costs[y * w + 10] = 0;
            return costs;
        }

        (float EndX, bool Evaded, long StartTick) Run(Horizon.Game.Sim.Monsters.MonsterNavGrid grid, float seconds,
                                                       Horizon.Game.Sim.Monsters.MonsterFollowType follow = Horizon.Game.Sim.Monsters.MonsterFollowType.GroundWalk)
        {
            var cfg = MakeConfig();
            cfg.FollowType = follow;
            cfg.AggroRadius = 60f;
            cfg.AggroConeDegrees = 360f;
            cfg.LeashRadius = 200f;
            cfg.NavGrid = grid;
            var self = MakeSelf(x: 1f, y: 2f, threat: 88UL);
            var targets = new[] { Target(88UL, 19f, 2f) };
            var startTick = 0L;
            var evaded = false;
            var ticks = (int)(seconds * 60);
            for (var tick = 0; tick < ticks; tick++)
            {
                var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 100L + tick, 60);
                if (d.EvadedByNoPath) evaded = true;
                startTick = d.NextNoPathStartTick;
                self = MakeSelf(x: self.X + d.DeltaX, y: self.Y + d.DeltaY, threat: d.ThreatTargetId,
                                state: d.NextState);
                // MakeSelf 不给这几格 ⇒ 手工带回去（够不到的计时与状态机字段必须逐拍流动）
                self.NoPathStartTick = d.NextNoPathStartTick;
                self.StateElapsedSeconds = d.NextState == self.State ? self.StateElapsedSeconds : 0f;
            }
            return (self.X, evaded, startTick);
        }

        var wallGrid = new Horizon.Game.Sim.Monsters.MonsterNavGrid
        { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(true) };

        // 窗口要按"**撞上墙之后**再等 5 秒"算：从 x=1 走到墙前本身要 ~3.8 秒，
        // 所以总时长取 `走过去的秒数 + 窗口 ± 一点`，早测那半取窗口过了一半的时刻（这时还在贴墙站）。
        var early = Run(wallGrid, Horizon.Game.Sim.Monsters.MonsterAiGate.NoPathEvadeSeconds + 2f);
        Assert.False(early.Evaded, $"不到窗口就脱战了（起点拍={early.StartTick}）：贴墙绕的不该被误踢");
        Assert.True(early.EndX < 10f, $"提前穿过了墙（x={early.EndX:0.##}）：v1 的挡墙门没生效");

        var walled = Run(wallGrid, Horizon.Game.Sim.Monsters.MonsterAiGate.NoPathEvadeSeconds + 7f);
        Assert.True(walled.Evaded, $"墙后追了 6 秒也没脱战（x={walled.EndX:0.###} 起点拍={walled.StartTick}）：v1 会永远贴着墙站着");
        Assert.True(walled.EndX < 10f, $"穿过墙了（x={walled.EndX:0.##}）");

        var open = Run(new Horizon.Game.Sim.Monsters.MonsterNavGrid
        { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(false) },
            Horizon.Game.Sim.Monsters.MonsterAiGate.NoPathEvadeSeconds + 1f);
        Assert.False(open.Evaded, "空地也判够不到：正常追击会被踢成脱战");
        Assert.True(open.EndX > 6f, $"空地那只没推进（x={open.EndX:F2}）：夹具前提没了");

        // **直线档不查图**：同一张墙图换成 Straight，照旧一路穿过去（与 v2 的 `useNav` 同一个口径）。
        // 少了这一半，"挡墙门"就会把直线档一起拦住——那两条通路对同一格的手感就此分家。
        var straight = Run(new Horizon.Game.Sim.Monsters.MonsterNavGrid
        { Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(true) },
            Horizon.Game.Sim.Monsters.MonsterAiGate.NoPathEvadeSeconds + 7f, Horizon.Game.Sim.Monsters.MonsterFollowType.Straight);
        Assert.False(straight.Evaded, "直线档被挡墙门拦成脱战了：它的语义是「不查图」");
        Assert.True(straight.EndX > 10f, $"直线档没穿过去（x={straight.EndX:F2}）：它本该完全不看这张图");

        // 没装图：与旧行为逐字相同（这一半就是金轨迹的护栏）
        var noGrid = Run(default, Horizon.Game.Sim.Monsters.MonsterAiGate.NoPathEvadeSeconds + 1f);
        Assert.False(noGrid.Evaded, "没装图也判够不到：直线档会被误踢");
        Assert.True(noGrid.EndX > 6f, $"没装图那只没推进（x={noGrid.EndX:F2}）：旧行为被改动了");
    }

    [Fact]
    public void v1的怪隔着墙也不起手而没装图照旧()
    {
        // AC 的 `IsWithinLOSInMap` 写在"能不能打"里，不分档位；v1 是生产里默认档走的那条路。
        // 这里与 v2 那条同一个主人（`MonsterPathCore.HasLineOfSight`），"没图"那半必须是照旧起手——
        // 否则分不清是视线门生效还是索敌整个被关掉了。
        const int w = 24, h = 8;
        byte[] Costs(bool wall)
        {
            var costs = new byte[w * h];
            for (var i = 0; i < costs.Length; i++) costs[i] = (byte)Horizon.Game.Sim.Monsters.MonsterNavGrid.BaseCellCost;
            if (wall)
            {
                for (var y = 0; y < h; y++) costs[y * w + 10] = 0;   // x=10 一道竖墙
            }
            return costs;
        }

        Horizon.Game.Sim.Monsters.MonsterNavGrid Grid(bool wall, bool none)
            => none
                ? default
                : new Horizon.Game.Sim.Monsters.MonsterNavGrid
                {
                    Width = w, Height = h, CellSize = 1f, OriginX = 0f, OriginY = 0f, CellCost = Costs(wall),
                };

        (MonsterAiState State, ulong Threat) Run(Horizon.Game.Sim.Monsters.MonsterNavGrid grid)
        {
            var cfg = MakeConfig();
            cfg.AggroRadius = 60f;
            cfg.AggroConeDegrees = 360f;
            cfg.LeashRadius = 200f;
            cfg.NavGrid = grid;
            var self = MakeSelf();
            var targets = new[] { Target(88UL, 19f, 2f) };
            var d = MonsterAiCore.Decide(self, targets, Dt, cfg, 100L);
            return (d.NextState, d.ThreatTargetId);
        }

        // 墙后：不起手（IdleToPatrolSeconds 缺省会把不进战的怪推到 Patrol，所以判"没进战斗"而不是"恰好 Idle"）
        var walled = Run(Grid(wall: true, none: false));
        Assert.True(walled.State != MonsterAiState.Chase && walled.State != MonsterAiState.Attack,
                    "墙后 v1 还是进了战斗：" + walled.State);
        Assert.Equal(0UL, walled.Threat);

        // 空地：同一份夹具只把墙拿掉 → 起手
        var open = Run(Grid(wall: false, none: false));
        Assert.Equal(MonsterAiState.Chase, open.State);
        Assert.Equal(88UL, open.Threat);

        // 没装图（生产现状）：照旧起手
        var noGrid = Run(Grid(wall: false, none: true));
        Assert.Equal(MonsterAiState.Chase, noGrid.State);
        Assert.Equal(88UL, noGrid.Threat);
    }
}
