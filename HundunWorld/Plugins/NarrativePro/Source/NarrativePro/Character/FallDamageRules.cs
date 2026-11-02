using System;

namespace NarrativePro.Character
{
    /// <summary>
    /// 坠落伤害与坠落布娃娃的纯规则接缝（无引擎依赖，harness 断的就是线上一条）。
    /// <para>
    /// 对应 UE5 <c>ANarrativeCharacter::Landed</c>（NarrativeCharacter.cpp:230-259）：
    /// 取落地时的垂直速度 <c>ZVel</c>，只在 <c>ZVel &lt; 0</c>（正在下落）时结算；
    /// 伤害比例来自 <c>UNarrativeCharacterMovement::FallDamageCurve</c> 按 <c>ZVel</c> 求值，
    /// 比例 <c>&gt; 0.1</c> 才真的扣血（<c>NASC-&gt;DealDamage(GetMaxHealth() * FallDamagePct)</c>）；
    /// 另有"砸在陡地上就布娃娃 3 秒"的分支，要求
    /// <c>ZVel &lt; EnterRagdollFallZImpactGroundThreshold</c> 且出生已超过 3 秒
    /// （<c>GetWorld()-&gt;TimeSince(CreationTime) &gt; 3.f</c>，防出生瞬间误触发）。
    /// </para>
    /// <para>
    /// 与 UE5 的差别（诚实记录）：UE5 用可在编辑器里拉的 FloatCurve，
    /// Flax 这边曲线资产没有等价作者面，因此把曲线离散成"起算速度 / 封顶速度 / 封顶比例"
    /// 三个可调参数做线性近似。端口默认值取保守档（落地速度到 -1200 才满伤），
    /// 真实手感需要在 PIE 里校准。
    /// </para>
    /// </summary>
    public static class FallDamageRules
    {
        /// <summary>开始计算坠落伤害的下落速度（负值，厘米/秒）。比它更缓的落地不掉血。</summary>
        public const float DamageStartZVelocity = -600f;

        /// <summary>达到封顶比例的下落速度（负值）。</summary>
        public const float DamageFullZVelocity = -1800f;

        /// <summary>满伤时按最大生命扣掉的百分比。</summary>
        public const float MaxDamagePercent = 1f;

        /// <summary>UE5 :243 的扣血门槛：比例不超过它就不结算（小幅坠落不磨血）。</summary>
        public const float DamageApplyThreshold = 0.1f;

        /// <summary>UE5 布娃娃分支的下落速度门槛（比它更陡才砸趴下）。</summary>
        public const float RagdollZImpactThreshold = -1500f;

        /// <summary>UE5 :252 的出生保护时间（秒）：刚出生 3 秒内的坠落不砸趴。</summary>
        public const float RagdollSpawnGuardSeconds = 3f;

        /// <summary>
        /// 按落地垂直速度求伤害比例。落在 [0, maxPercent] 之间；
        /// 上升（ZVel &gt;= 0）恒为 0，与 UE5 的 <c>if (ZVel &lt; 0)</c> 一致。
        /// </summary>
        public static float EvaluateDamagePercent(
            float zVelocity,
            float startZVelocity = DamageStartZVelocity,
            float fullZVelocity = DamageFullZVelocity,
            float maxPercent = MaxDamagePercent)
        {
            if (zVelocity >= 0f) return 0f;
            if (maxPercent <= 0f) return 0f;

            // 速度都是负值：起算速度（较缓，如 -600）在数轴上**大于**封顶速度（如 -1800）。
            // 反过来填（起算比封顶更小）就是作者笔误，退化成阈值判定而不是给出一条斜率反了的比例。
            if (startZVelocity <= fullZVelocity)
            {
                return zVelocity <= fullZVelocity ? maxPercent : 0f;
            }

            if (zVelocity >= startZVelocity) return 0f;
            if (zVelocity <= fullZVelocity) return maxPercent;

            float t = (startZVelocity - zVelocity) / (startZVelocity - fullZVelocity);
            float percent = t * maxPercent;
            return percent < 0f ? 0f : (percent > maxPercent ? maxPercent : percent);
        }

        /// <summary>UE5 :243：只有比例严格大于 0.1 才真的扣血。</summary>
        public static bool ShouldApplyDamage(
            float damagePercent, float threshold = DamageApplyThreshold)
        {
            return damagePercent > threshold;
        }

        /// <summary>UE5 :250-253：陡落 + 出生保护期满才砸趴下。</summary>
        public static bool ShouldRagdollOnLanding(
            float zVelocity,
            float secondsSinceCreation,
            float ragdollThreshold = RagdollZImpactThreshold,
            float spawnGuardSeconds = RagdollSpawnGuardSeconds)
        {
            return zVelocity < ragdollThreshold && secondsSinceCreation > spawnGuardSeconds;
        }
    }
}
