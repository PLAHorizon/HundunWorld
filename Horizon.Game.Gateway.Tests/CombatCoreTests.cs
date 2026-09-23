using System;
using Horizon.Game.Sim;
using Xunit;

namespace Horizon.Game.Gateway.Tests;

/// <summary>
/// MMORPG 开发计划 M4.1/M4.2 —— 确定性战斗内核测试（验收门 H4-a 的内核部分）。
/// <para>
/// 本套件同时锁定两件事：
/// <list type="number">
///   <item><b>数值正确</b>：范围/扇区/闪避/暴击/防御/五行 的判定与既有
///         <c>CombatCalculator</c> 语义一致（切换权威实现时玩家感知的数值不应突变）；</item>
///   <item><b>确定性</b>：同输入必然同结果——这是 Orleans 权威与 UE5 DS 权威能够对拍的前提，
///         也是既有 <c>CombatCalculator</c>（用 <c>Random.Shared</c>/<c>DateTime.Now</c>）无法满足的。</item>
/// </list>
/// </para>
/// </summary>
public class CombatCoreTests
{
    private static CombatantStats Attacker(float attack = 20f, float critRate = 0f, float dodge = 0f, int element = 0)
        => new CombatantStats
        {
            Hp = 100,
            MaxHp = 100,
            AttackPower = attack,
            Defense = 0f,
            Element = element,
            DodgeRate = dodge,
            CritRate = critRate,
            CritMultiplier = CombatCore.DefaultCritMultiplier,
        };

    private static CombatantStats Target(int hp = 100, float defense = 0f, float dodge = 0f, int element = 0)
        => new CombatantStats
        {
            Hp = hp,
            MaxHp = hp,
            AttackPower = 10f,
            Defense = defense,
            Element = element,
            DodgeRate = dodge,
            CritRate = 0f,
            CritMultiplier = CombatCore.DefaultCritMultiplier,
        };

    /// <summary>构造"目标在攻击者正前方 1 米"的请求（扇区判定通过）。</summary>
    private static CombatRequest InRange(ulong attackerId = 1UL, ulong targetId = 2UL, long tick = 100)
        => new CombatRequest
        {
            AttackerId = attackerId,
            TargetId = targetId,
            SkillId = 0,
            SkillRawDamage = 0f,
            ServerTick = tick,
            AttackerX = 0f,
            AttackerY = 0f,
            AttackerYaw = 0f,      // 朝向 +X
            TargetX = 1f,          // 正前方 1 米
            TargetY = 0f,
            AttackRange = 3f,
            FacingCosThreshold = 0.5f,
        };

    // ---------- 合法性 ----------

    [Fact]
    public void ZeroIds_AreInvalid()
    {
        Assert.Equal(CombatOutcome.Invalid, CombatCore.Resolve(InRange(attackerId: 0), Attacker(), Target()).Outcome);
        Assert.Equal(CombatOutcome.Invalid, CombatCore.Resolve(InRange(targetId: 0), Attacker(), Target()).Outcome);
    }

    [Fact]
    public void SelfTarget_IsInvalid()
    {
        var r = CombatCore.Resolve(InRange(attackerId: 5, targetId: 5), Attacker(), Target());
        Assert.Equal(CombatOutcome.Invalid, r.Outcome);
    }

    [Fact]
    public void DeadAttacker_CannotAttack()
    {
        var dead = Attacker();
        dead.Hp = 0;
        Assert.Equal(CombatOutcome.AttackerDead, CombatCore.Resolve(InRange(), dead, Target()).Outcome);
    }

    [Fact]
    public void DeadTarget_IsRejected()
    {
        Assert.Equal(CombatOutcome.TargetDead, CombatCore.Resolve(InRange(), Attacker(), Target(hp: 0)).Outcome);
    }

    // ---------- 空间判定 ----------

    [Fact]
    public void TargetBeyondRange_IsOutOfRange()
    {
        var req = InRange();
        req.TargetX = 10f; // 10m > 3m
        Assert.Equal(CombatOutcome.OutOfRange, CombatCore.Resolve(req, Attacker(), Target()).Outcome);
    }

    [Fact]
    public void TargetExactlyAtRange_IsHit()
    {
        var req = InRange();
        req.TargetX = 3f; // 恰好等于射程
        Assert.True(CombatCore.Resolve(req, Attacker(), Target()).DealtDamage);
    }

    [Fact]
    public void TargetBehindAttacker_IsOutOfArc()
    {
        var req = InRange();
        req.TargetX = -1f; // 正后方
        Assert.Equal(CombatOutcome.OutOfArc, CombatCore.Resolve(req, Attacker(), Target()).Outcome);
    }

    [Fact]
    public void TargetAt90Degrees_IsOutOfArc_WhenThresholdIsHalf()
    {
        var req = InRange();
        req.TargetX = 0f;
        req.TargetY = 1f; // 正侧方（点积 = 0 < 0.5）
        Assert.Equal(CombatOutcome.OutOfArc, CombatCore.Resolve(req, Attacker(), Target()).Outcome);
    }

    [Fact]
    public void MonsterStyleAttack_SkipsArcCheck_WhenThresholdDisabled()
    {
        // 阈值 -2 表示"不做扇区判定"（怪物由 AI 决定打谁）。
        var req = InRange();
        req.AttackerYaw = (float)Math.PI; // 背对目标
        req.FacingCosThreshold = -2f;
        Assert.True(CombatCore.Resolve(req, Attacker(), Target()).DealtDamage);
    }

    // ---------- 闪避 / 暴击 ----------

    [Fact]
    public void FullDodgeRate_AlwaysDodges()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(), Target(dodge: 1f));
        Assert.Equal(CombatOutcome.Dodged, r.Outcome);
        Assert.Equal(0, r.Damage);
        Assert.False(r.TargetDied);
    }

    [Fact]
    public void ZeroDodgeRate_NeverDodges()
    {
        Assert.True(CombatCore.Resolve(InRange(), Attacker(), Target(dodge: 0f)).DealtDamage);
    }

    [Fact]
    public void FullCritRate_AlwaysCrits()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(critRate: 1f), Target());
        Assert.True(r.IsCritical);
    }

    [Fact]
    public void ZeroCritRate_NeverCrits()
    {
        Assert.False(CombatCore.Resolve(InRange(), Attacker(critRate: 0f), Target()).IsCritical);
    }

    [Fact]
    public void CritMultipliesDamageByDefault1_5()
    {
        var normal = CombatCore.Resolve(InRange(tick: 7), Attacker(critRate: 0f), Target());
        var crit = CombatCore.Resolve(InRange(tick: 7), Attacker(critRate: 1f), Target());
        Assert.Equal((int)MathF.Round(normal.Damage * CombatCore.DefaultCritMultiplier), crit.Damage);
    }

    // ---------- 伤害公式（与既有 CombatCalculator 一致）----------

    [Fact]
    public void BaseDamage_IsHalfAttackPower_WhenNoSkillDamage()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(attack: 40f), Target());
        Assert.Equal(20, r.Damage); // 40 * 0.5 + 0
    }

    [Fact]
    public void SkillRawDamage_IsAdded()
    {
        var req = InRange();
        req.SkillRawDamage = 10f;
        var r = CombatCore.Resolve(req, Attacker(attack: 40f), Target());
        Assert.Equal(30, r.Damage); // 20 + 10
    }

    [Fact]
    public void DefenseReducesDamage_PerExistingFormula()
    {
        // defense=100 → 减免 100/200 = 50%
        var r = CombatCore.Resolve(InRange(), Attacker(attack: 40f), Target(defense: 100f));
        Assert.Equal(10, r.Damage); // 20 * (1 - 0.5)
    }

    [Fact]
    public void DefenseReduction_MatchesCombatCalculatorFormula()
    {
        Assert.Equal(0f, CombatCore.CalculateDefenseReduction(0f), 5);
        Assert.Equal(0.5f, CombatCore.CalculateDefenseReduction(100f), 5);
        Assert.Equal(0.75f, CombatCore.CalculateDefenseReduction(300f), 5);
    }

    [Fact]
    public void MinimumDamage_IsOne_AgainstHugeDefense()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(attack: 1f), Target(defense: 1_000_000f));
        Assert.Equal(1, r.Damage);
    }

    [Theory]
    [InlineData(1, 2, 1.25f)] // 金克木
    [InlineData(2, 5, 1.25f)] // 木克土
    [InlineData(1, 4, 0.8f)]  // 金被火克
    [InlineData(3, 1, 1.0f)]  // 无关系
    [InlineData(0, 2, 1.0f)]  // 无属性
    public void WuxingMultiplier_MatchesCombatCalculator(int atk, int def, float expected)
    {
        Assert.Equal(expected, CombatCore.GetWuxingMultiplier(atk, def), 5);
    }

    [Fact]
    public void WuxingAdvantage_IncreasesDamage()
    {
        var neutral = CombatCore.Resolve(InRange(), Attacker(attack: 40f, element: 0), Target(element: 0));
        var advantage = CombatCore.Resolve(InRange(), Attacker(attack: 40f, element: 1), Target(element: 2));
        Assert.True(advantage.Damage > neutral.Damage);
    }

    // ---------- 死亡判定 ----------

    [Fact]
    public void LethalDamage_MarksTargetDead()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(attack: 40f), Target(hp: 10));
        Assert.True(r.TargetDied);
        Assert.Equal(0, r.TargetRemainingHp);
    }

    [Fact]
    public void NonLethalDamage_KeepsTargetAlive()
    {
        var r = CombatCore.Resolve(InRange(), Attacker(attack: 40f), Target(hp: 100));
        Assert.False(r.TargetDied);
        Assert.Equal(80, r.TargetRemainingHp);
    }

    // ---------- 确定性 ----------

    [Fact]
    public void Resolve_IsBitwiseDeterministic()
    {
        var req = InRange();
        var a = Attacker(critRate: 0.5f);
        var t = Target(dodge: 0.3f);

        var first = CombatCore.Resolve(req, a, t);
        for (var i = 0; i < 200; i++)
        {
            var again = CombatCore.Resolve(req, a, t);
            Assert.Equal(first.Outcome, again.Outcome);
            Assert.Equal(first.Damage, again.Damage);
            Assert.Equal(first.IsCritical, again.IsCritical);
            Assert.Equal(first.TargetRemainingHp, again.TargetRemainingHp);
        }
    }

    [Fact]
    public void Roll01_IsInUnitInterval()
    {
        for (ulong i = 1; i < 500; i++)
        {
            var v = CombatCore.Roll01(i, i * 7, (long)i, 0x1234u);
            Assert.InRange(v, 0f, 0.9999999f);
        }
    }

    [Fact]
    public void Roll01_VariesAcrossInputs()
    {
        // 反退化断言：若哈希塌缩成常量，所有随机性都失效（"永远暴击"或"永不暴击"）。
        var set = new System.Collections.Generic.HashSet<int>();
        for (ulong i = 1; i <= 200; i++)
        {
            set.Add(BitConverter.SingleToInt32Bits(CombatCore.Roll01(i, 2, 3, 0x9E37u)));
        }
        Assert.True(set.Count > 100, $"掷骰应随输入变化，实际只有 {set.Count} 个不同值");
    }

    [Fact]
    public void Roll01_DiffersBySalt()
    {
        var dodgeRoll = CombatCore.Roll01(1, 2, 3, 0x9E37u);
        var critRoll = CombatCore.Roll01(1, 2, 3, 0xC2B2u);
        Assert.NotEqual(BitConverter.SingleToInt32Bits(dodgeRoll), BitConverter.SingleToInt32Bits(critRoll));
    }

    [Fact]
    public void Version_IsPositive_AndDocumentedAsContract()
    {
        Assert.True(CombatCore.Version >= 1);
    }
}
