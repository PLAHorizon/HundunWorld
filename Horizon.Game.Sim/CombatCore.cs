using System;
using System.Runtime.CompilerServices;

namespace Horizon.Game.Sim;

/// <summary>战斗结算结果类型。</summary>
public enum CombatOutcome : byte
{
    /// <summary>未结算（保留）。</summary>
    Unknown = 0,

    /// <summary>命中并造成伤害。</summary>
    Hit = 1,

    /// <summary>目标闪避。</summary>
    Dodged = 2,

    /// <summary>目标超出攻击距离。</summary>
    OutOfRange = 3,

    /// <summary>目标不在攻击者正面扇区内。</summary>
    OutOfArc = 4,

    /// <summary>攻击者已死亡。</summary>
    AttackerDead = 5,

    /// <summary>目标已死亡。</summary>
    TargetDead = 6,

    /// <summary>请求非法（ID 为 0 / 自伤 / 配置非法）。</summary>
    Invalid = 7,

    /// <summary>
    /// 目标正在回位，<b>这一类目标根本打不到</b>（AzerothCore 的 <code>SPELL_MISS_EVADE</code> 那一档：
    /// <code>Creature::IsEvadingAttacks()</code> 挂在受击方那侧，见 <c>Monsters/MonsterAiGate.cs</c>）。
    /// <para>与 <see cref="Dodged"/> 分开是有必要的：闪避是"这一刀被躲开了"，玩家下一刀还会照打；
    /// 回位免伤是"这段时间整个攻击都无效"，玩家该停手等它到家。合成一个值就说不清该不该继续。</para>
    /// </summary>
    Evading = 8,
}

/// <summary>参战单位数值（攻击者与目标共用）。</summary>
public struct CombatantStats
{
    /// <summary>当前生命值。</summary>
    public int Hp;

    /// <summary>最大生命值。</summary>
    public int MaxHp;

    /// <summary>攻击力。</summary>
    public float AttackPower;

    /// <summary>防御力；减免公式 <c>defense / (defense + 100)</c>（与既有 CombatCalculator 一致）。</summary>
    public float Defense;

    /// <summary>五行属性（0=无, 1=金, 2=木, 3=水, 4=火, 5=土）。</summary>
    public int Element;

    /// <summary>闪避率 [0,1]。</summary>
    public float DodgeRate;

    /// <summary>暴击率 [0,1]。</summary>
    public float CritRate;

    /// <summary>暴击倍率（默认 1.5）。</summary>
    public float CritMultiplier;
}

/// <summary>一次攻击的请求参数。</summary>
public struct CombatRequest
{
    /// <summary>攻击者实体 ID。</summary>
    public ulong AttackerId;

    /// <summary>目标实体 ID。</summary>
    public ulong TargetId;

    /// <summary>技能 ID（0 = 普通攻击）。</summary>
    public int SkillId;

    /// <summary>技能附加基础伤害（普通攻击为 0）。</summary>
    public float SkillRawDamage;

    /// <summary>结算时的服务器 tick。</summary>
    public long ServerTick;

    /// <summary>攻击者 X。</summary>
    public float AttackerX;

    /// <summary>攻击者 Y。</summary>
    public float AttackerY;

    /// <summary>攻击者朝向（弧度，atan2(dy,dx) 约定）。</summary>
    public float AttackerYaw;

    /// <summary>目标 X。</summary>
    public float TargetX;

    /// <summary>目标 Y。</summary>
    public float TargetY;

    /// <summary>攻击距离（米）。</summary>
    public float AttackRange;

    /// <summary>
    /// 正面扇区的**余弦阈值**（而非角度）。
    /// <para>刻意用 cos 而非角度：避免在内核里调用 <c>MathF.Cos/Acos</c> 造成跨运行时位级分歧。
    /// 例如 120° 扇区 → cos(60°) = 0.5。</para>
    /// </summary>
    public float FacingCosThreshold;
}

/// <summary>一次攻击的结算结果。</summary>
public struct CombatResolution
{
    /// <summary>结果类型。</summary>
    public CombatOutcome Outcome;

    /// <summary>实际伤害（<see cref="CombatOutcome.Hit"/> 时有效）。</summary>
    public int Damage;

    /// <summary>是否暴击。</summary>
    public bool IsCritical;

    /// <summary>目标结算后剩余生命。</summary>
    public int TargetRemainingHp;

    /// <summary>目标最大生命。</summary>
    public int TargetMaxHp;

    /// <summary>目标是否因本次攻击死亡。</summary>
    public bool TargetDied;

    /// <summary>便捷判定：是否真的造成了伤害。</summary>
    public bool DealtDamage => Outcome == CombatOutcome.Hit && Damage > 0;
}

/// <summary>
/// 确定性战斗内核（MMORPG 开发计划 M4.1/M4.2）—— 伤害/命中/暴击/闪避的唯一权威实现。
/// <para>
/// <b>为什么必须新建而不是直接复用 <c>Horizon.Orleans.Grains.CombatCalculator</c></b>：
/// 后者的 <c>RollDodge</c>/<c>ApplyBlock</c> 使用 <c>Random.Shared</c>、
/// <c>IsSkillReady</c> 使用 <c>DateTime.Now</c>——**非确定性**。Orleans 权威路径与 UE5 DS
/// 权威路径若各自掷骰，同一场战斗在两套宿主上结果不同，灰度对拍与金轨迹回放全部失效。
/// 本内核把这些随机源替换为 <b>由 (attacker, target, tick, salt) 纯整数哈希驱动的确定性掷骰</b>，
/// 因此"同输入序列 → 同战斗结果"跨进程、跨重启成立。
/// </para>
/// <para>
/// <b>与既有数值公式保持一致</b>：防御减免 <c>d/(d+100)</c>、五行相克 ×1.25 / 被克 ×0.8、
/// 基础伤害 <c>攻击力×0.5 + 附加</c>、暴击 ×1.5 —— 全部照搬 <c>CombatCalculator</c> 的既有语义，
/// 只把"掷骰源"换掉。这样 Orleans 侧将来切到本内核时，玩家感知的数值不会突变。
/// </para>
/// <para>
/// <b>确定性约束</b>：纯静态、无全局状态、无 <c>DateTime</c>、无 <c>Random</c>、
/// 不使用 SIMD；朝向判定用点积与**预算好的余弦阈值**，不调用三角函数。
/// 任何改变伤害或命中判定的改动都必须递增 <see cref="Version"/>。
/// </para>
/// </summary>
public static class CombatCore
{
    /// <summary>
    /// 战斗内核版本。任何影响命中/伤害/暴击/闪避的改动都必须递增（金轨迹对拍锚点）。
    /// <para>v1：首版——确定性掷骰 + 范围/扇区判定 + 防御/五行减免 + 暴击 + 闪避。</para>
    /// </summary>
    public const int Version = 1;

    /// <summary>默认暴击倍率（与 <c>CombatCalculator.ApplyCriticalDamage</c> 默认值一致）。</summary>
    public const float DefaultCritMultiplier = 1.5f;

    /// <summary>防御减免基准常量（与既有公式 <c>defense/(defense+100)</c> 一致）。</summary>
    public const float DefenseBaseline = 100f;

    /// <summary>
    /// 结算一次攻击。
    /// </summary>
    /// <param name="request">攻击参数。</param>
    /// <param name="attacker">攻击者数值。</param>
    /// <param name="target">目标数值。</param>
    /// <returns>结算结果；<see cref="CombatResolution.TargetRemainingHp"/> 在未命中时等于目标当前血量。</returns>
    public static CombatResolution Resolve(in CombatRequest request, in CombatantStats attacker, in CombatantStats target)
    {
        var result = new CombatResolution
        {
            Outcome = CombatOutcome.Unknown,
            TargetRemainingHp = target.Hp,
            TargetMaxHp = target.MaxHp,
        };

        // --- 合法性 ---
        if (request.AttackerId == 0UL || request.TargetId == 0UL || request.AttackerId == request.TargetId)
        {
            result.Outcome = CombatOutcome.Invalid;
            return result;
        }

        if (attacker.Hp <= 0)
        {
            result.Outcome = CombatOutcome.AttackerDead;
            return result;
        }

        if (target.Hp <= 0)
        {
            result.Outcome = CombatOutcome.TargetDead;
            return result;
        }

        // --- 距离判定（XY 平面；Z 不参与，避免飞行/台阶造成"打不到"的体感问题）---
        var dx = request.TargetX - request.AttackerX;
        var dy = request.TargetY - request.AttackerY;
        var distSq = dx * dx + dy * dy;
        var range = request.AttackRange > 0f ? request.AttackRange : 0f;

        // 用平方比较，避免开方（也顺带避免 MathF.Sqrt 的跨运行时分歧面）。
        if (distSq > range * range)
        {
            result.Outcome = CombatOutcome.OutOfRange;
            return result;
        }

        // --- 正面扇区判定（点积 vs 预算余弦阈值）---
        // 距离极近时方向无意义，跳过扇区判定（否则贴身打空气会出现"贴脸打不到"）。
        if (distSq > 1e-6f && request.FacingCosThreshold > -2f)
        {
            var dist = MathF.Sqrt(distSq);
            var inv = 1f / dist;
            var dirX = dx * inv;
            var dirY = dy * inv;
            var faceX = MathF.Cos(request.AttackerYaw);
            var faceY = MathF.Sin(request.AttackerYaw);
            var dot = faceX * dirX + faceY * dirY;
            if (dot < request.FacingCosThreshold)
            {
                result.Outcome = CombatOutcome.OutOfArc;
                return result;
            }
        }

        // --- 闪避（确定性掷骰）---
        var dodgeRate = Clamp01(target.DodgeRate);
        if (dodgeRate > 0f)
        {
            var roll = Roll01(request.AttackerId, request.TargetId, request.ServerTick, salt: 0x9E37u);
            if (roll < dodgeRate)
            {
                result.Outcome = CombatOutcome.Dodged;
                return result;
            }
        }

        // --- 伤害计算（照搬既有数值公式）---
        var baseDamage = attacker.AttackPower * 0.5f + (request.SkillRawDamage > 0f ? request.SkillRawDamage : 0f);
        var wuxing = GetWuxingMultiplier(attacker.Element, target.Element);
        var defenseReduction = CalculateDefenseReduction(target.Defense);
        var damage = baseDamage * wuxing * (1f - defenseReduction);

        // --- 暴击（确定性掷骰）---
        var critRate = Clamp01(attacker.CritRate);
        var isCritical = false;
        if (critRate > 0f)
        {
            var roll = Roll01(request.AttackerId, request.TargetId, request.ServerTick, salt: 0xC2B2u);
            if (roll < critRate)
            {
                isCritical = true;
                var mult = attacker.CritMultiplier > 0f ? attacker.CritMultiplier : DefaultCritMultiplier;
                damage *= mult;
            }
        }

        // 至少 1 点伤害：避免高防御目标完全免疫导致的"打不动"死局。
        var finalDamage = (int)MathF.Round(damage);
        if (finalDamage < 1) finalDamage = 1;

        var remaining = target.Hp - finalDamage;
        if (remaining < 0) remaining = 0;

        result.Outcome = CombatOutcome.Hit;
        result.Damage = finalDamage;
        result.IsCritical = isCritical;
        result.TargetRemainingHp = remaining;
        result.TargetDied = remaining <= 0;
        return result;
    }

    /// <summary>
    /// 五行相克乘数（与 <c>CombatCalculator.GetWuxingMultiplier</c> 逐值一致）。
    /// <para>1=金, 2=木, 3=水, 4=火, 5=土；相克 ×1.25、被克 ×0.8、其余 ×1.0。</para>
    /// </summary>
    public static float GetWuxingMultiplier(int attackerElement, int defenderElement)
    {
        if (attackerElement == 0 || defenderElement == 0) return 1.0f;

        if ((attackerElement == 1 && defenderElement == 2) ||
            (attackerElement == 2 && defenderElement == 5) ||
            (attackerElement == 5 && defenderElement == 3) ||
            (attackerElement == 3 && defenderElement == 4) ||
            (attackerElement == 4 && defenderElement == 1))
        {
            return 1.25f;
        }

        if ((attackerElement == 1 && defenderElement == 4) ||
            (attackerElement == 4 && defenderElement == 2) ||
            (attackerElement == 2 && defenderElement == 3) ||
            (attackerElement == 3 && defenderElement == 5) ||
            (attackerElement == 5 && defenderElement == 1))
        {
            return 0.8f;
        }

        return 1.0f;
    }

    /// <summary>防御减免系数：<c>defense / (defense + 100)</c>。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CalculateDefenseReduction(float defense)
    {
        if (defense <= 0f) return 0f;
        return defense / (defense + DefenseBaseline);
    }

    /// <summary>
    /// 确定性掷骰：把 (attacker, target, tick, salt) 映射到 [0,1)。
    /// <para>
    /// 使用 splitmix64 混合（纯整数位运算，无浮点、无平台差异），再取高 24 位归一化——
    /// 这是"同输入必然同结果、不同输入统计均匀"的最小实现，且**不需要携带随机状态**：
    /// 状态就是输入本身，因此重放任意 tick 都能得到相同掷骰值。
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Roll01(ulong attackerId, ulong targetId, long serverTick, uint salt)
    {
        var x = attackerId;
        x ^= targetId + 0x9E3779B97F4A7C15UL + (x << 6) + (x >> 2);
        x ^= (ulong)serverTick + 0xBF58476D1CE4E5B9UL + (x << 6) + (x >> 2);
        x ^= salt + 0x94D049BB133111EBUL + (x << 6) + (x >> 2);

        // splitmix64 finalizer
        x ^= x >> 30;
        x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27;
        x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;

        // 取高 24 位 → [0, 1)
        return (x >> 40) * (1.0f / 16777216.0f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp01(float v)
    {
        if (v < 0f) return 0f;
        if (v > 1f) return 1f;
        return v;
    }
}
