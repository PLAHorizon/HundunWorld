// 模块关系说明：
// 本文件属于 NarrativePro.GAS（UE5 NarrativeArsenal 攻击令牌移植）。
//
// 为什么需要它：「同时允许几个 NPC 打我、满了能从谁手里抢、抢了要通知被抢者」这套账本规则
// 长在 UNarrativeAbilitySystemComponent（FlaxEngine.Script，无引擎环境 new 不出来）里，
// 因此进程外一条断言都打不到 —— 而这条规则的两个致命点是：
//   ① 满槽时抢错下标会把*别人*的令牌抹掉（UE5 用 BestScore 严格大于 + 立即抢夺短路 break）；
//   ② 抢完忘了通知被抢者（TokenStolen），那个 NPC 会一直攻击一个它已经没有令牌的目标。
// 另外「优先级高者先被攻击」的打分也曾在距离与优先级之间写反符号。
//
// 本文件把两套规则抽成纯函数（不引用 FlaxEngine 类型）：
//   - NarrativeAttackTokenLedger：持有者视图 + 抢夺设置 → 授予/抢夺下标/拒绝 的账本裁决；
//   - NarrativeAttackTargetScoring：距离与 AttackPriority 合成目标评分 + 并列时的取舍规则。
// 实时通路（NarrativeAbilitySystemComponent.TryClaimToken/CanStealToken/ShouldImmediatelyStealToken/
// ReturnToken(AtIndex)/GetAvailableAttackTokens 与 NarrativeNPCController.FindBestAttackTarget）
// 全部经由这里，harness 驱动的是同一份实现。
//
// UE5 口径核对（重要，避免把「优先级」臆造进抢夺规则）：
//   NarrativeAbilitySystemComponent.cpp:400-462 TryClaimToken —— 满槽才找可抢者，
//   ShouldImmediatelyStealToken(:498-501) 命中即 break，否则 CanStealToken(:503-551) 取评分严格最大者；
//   CanStealToken 只看「令牌够老」与「抢方明显更近」，*完全不看 AttackPriority*；
//   AttackPriority 的实际消费者是 UEnvQueryTest_AttackPriority（EnvQueryTest_AttackPriority.cpp:54
//   `Score = CDef->AttackPriority`，纯 EQS 打分），端口把它与距离合成在 FindBestAttackTarget 里。

using System;
using System.Collections.Generic;

namespace NarrativePro.GAS
{
    /// <summary>
    /// 令牌账本里一个持有槽的纯数据视图（实时侧从 <see cref="AttackToken"/> + 控制器取好）。
    /// </summary>
    public sealed class AttackTokenHolderView
    {
        /// <summary>持有者身份（实时侧是 NarrativeNPCController 实例，纯层只比引用）。</summary>
        public object OwnerKey;

        /// <summary>令牌被授予时的游戏时间（秒）。</summary>
        public float GrantedTime;

        /// <summary>持有者 Pawn 到本 ASC 拥有者的距离平方（对应 UE5 DistSquared(ExistingPawn, OurActor)）。</summary>
        public float DistanceSqToVictim;

        /// <summary>持有者为空（UE5 <c>!Token.Owner</c>）。</summary>
        public bool bOwnerMissing;

        /// <summary>持有者已死（UE5 <c>!Token.Owner->IsAlive()</c>）。</summary>
        public bool bOwnerDead;

        /// <summary>持有者没有可控 Pawn（UE5 <c>!Token.Owner->GetPawn()</c>）。</summary>
        public bool bOwnerHasNoPawn;
    }

    /// <summary>抢夺令牌的三个可调参数快照（实时侧来自 NarrativeCombatDeveloperSettings）。</summary>
    public struct AttackTokenStealSettings
    {
        /// <summary>令牌「够老」的年龄门槛（UE5 <c>TokenStealableAgeSeconds</c>）。</summary>
        public float TokenStealableAgeSeconds;

        /// <summary>抢夺者必须比持有者近到这个比例以内（UE5 <c>StealTokenProximity</c>）。</summary>
        public float StealTokenProximity;
    }

    /// <summary>攻击令牌申领结果。</summary>
    public enum EAttackTokenClaim
    {
        /// <summary>拿到令牌（可能是抢来的，见 <see cref="AttackTokenClaimDecision.StealIndex"/>）。</summary>
        Granted,

        /// <summary>槽位满且没人可抢：拒绝。</summary>
        DeniedNoOpenToken,

        /// <summary>申领者无效（无控制器 / 无可控 Pawn / 本 ASC 没有 Actor）：直接拒绝，不做抢夺扫描。</summary>
        DeniedInvalidClaimer,
    }

    /// <summary>账本裁决的完整输出。</summary>
    public sealed class AttackTokenClaimDecision
    {
        /// <summary>结果。</summary>
        public EAttackTokenClaim Result = EAttackTokenClaim.DeniedInvalidClaimer;

        /// <summary>需要归还的下标（<see cref="NarrativeAttackTokenLedger.NoToken"/> 表示无人被抢）。</summary>
        public int StealIndex = NarrativeAttackTokenLedger.NoToken;

        /// <summary>是否必须通知被抢者（UE5 在归还之前调用 <c>TokenStolen()</c>）。</summary>
        public bool bMustNotifyVictim;

        /// <summary>本次抢占所依据的评分（仅日志/测试用）。</summary>
        public float StealScore;
    }

    /// <summary>
    /// 攻击令牌账本的纯函数层（不引用 FlaxEngine / Script 类型，可无引擎运行）。
    /// </summary>
    public static class NarrativeAttackTokenLedger
    {
        /// <summary>没有令牌槽位时的哨兵（等价 UE5 的 INDEX_NONE）。</summary>
        public const int NoToken = -1;

        /// <summary>
        /// 是否应无条件立即抢夺：持有者为空 / 已死 / 失去 Pawn。
        /// 对应 UE5 <c>ShouldImmediatelyStealToken</c>（cpp:498-501）；端口额外把「null 令牌本身」
        /// 也视为可立即抢（UE5 传的是引用，不可能为 null，这里等价加固）。
        /// </summary>
        public static bool ShouldImmediatelyStealToken(AttackTokenHolderView holder)
        {
            if (holder == null) return true;
            return holder.bOwnerMissing || holder.bOwnerDead || holder.bOwnerHasNoPawn;
        }

        /// <summary>
        /// 能否从 <paramref name="existing"/> 手里抢令牌，并给出评分（评分高者优先被抢）。
        /// 逐句对应 UE5 <c>CanStealToken</c>（cpp:503-551）：只看令牌年龄与相对距离，不看优先级。
        /// </summary>
        /// <param name="existing">现有持有槽。</param>
        /// <param name="distanceSqFromStealer">抢夺者到本 ASC 拥有者的距离平方。</param>
        /// <param name="now">当前游戏时间（UE5 <c>World->TimeSince(GrantTime)</c> 的等价基准）。</param>
        /// <param name="settings">抢夺参数。</param>
        /// <param name="stealScore">输出评分；只有可抢时才有意义。</param>
        public static bool CanStealToken(
            AttackTokenHolderView existing,
            float distanceSqFromStealer,
            float now,
            AttackTokenStealSettings settings,
            out float stealScore)
        {
            stealScore = 0f;

            // UE5:507 `if (Stealer && ExistingToken.Owner)` —— 申领者或持有者缺失都不可抢。
            if (existing == null || existing.bOwnerMissing) return false;

            // UE5:513-515 需要双方 Pawn 都在；端口在 ShouldImmediatelyStealToken 里已经
            // 把「持有者无 Pawn」短路成立即抢，这里保持同一口径：无 Pawn 的槽位不参与评分比较。
            if (existing.bOwnerHasNoPawn) return false;

            float distFromExisting = existing.DistanceSqToVictim;

            // Time.GameTime 是全局累计时间，等价 UE5 的 World->TimeSince(GrantTime)。
            float timeSinceGrant = Math.Max(0f, now - existing.GrantedTime);
            bool bCanSteal = false;

            // 令牌授予很久了：允许抢夺。每超出一秒记 1 分（UE5:522-527）。
            if (timeSinceGrant > settings.TokenStealableAgeSeconds)
            {
                stealScore += timeSinceGrant - settings.TokenStealableAgeSeconds;
                bCanSteal = true;
            }

            // 抢夺者比现有持有者近 StealTokenProximity 倍以内：允许抢夺。
            // 现有持有者越远评分越高（UE5:529-536）。
            // 端口加固：distFromExisting 过小时 UE5 会除以 0 得到 inf/NaN，这里判为不可抢。
            if (distanceSqFromStealer < distFromExisting && distFromExisting > MinDistanceSq &&
                distanceSqFromStealer / distFromExisting < settings.StealTokenProximity)
            {
                stealScore += distFromExisting / (ImpassableDistance * ImpassableDistance);
                bCanSteal = true;
            }

            return bCanSteal;
        }

        /// <summary>距离平方的最小可比较值（避免 UE5 的除零）。</summary>
        public const float MinDistanceSq = 0.0001f;

        /// <summary>评分归一化用的距离基准（UE5:534 的 50000.f）。</summary>
        public const float ImpassableDistance = 50000f;

        /// <summary>
        /// 满槽时扫描账本，找出该从哪个槽位抢。
        /// 对应 UE5 TryClaimToken:411-441 —— 立即抢夺命中即 break（不再比较评分）；
        /// 否则取评分*严格大于*当前最优者，初值 BestScore=0，因此评分为 0 的可抢槽位不会被选中。
        /// </summary>
        /// <returns>要被抢的槽位下标，或 <see cref="NoToken"/>。</returns>
        public static int FindStealIndex(
            IList<AttackTokenHolderView> holders,
            float distanceSqFromStealer,
            float now,
            AttackTokenStealSettings settings,
            out float outBestScore)
        {
            outBestScore = 0f;
            int bestIdx = NoToken;

            if (holders == null || holders.Count == 0) return NoToken;

            for (int idx = 0; idx < holders.Count; idx++)
            {
                var holder = holders[idx];

                // 立即抢夺命中即 break：后面的槽位连评分都不算。
                if (ShouldImmediatelyStealToken(holder))
                {
                    outBestScore = 0f;
                    return idx;
                }

                float score;
                if (CanStealToken(holder, distanceSqFromStealer, now, settings, out score))
                {
                    if (score > outBestScore)
                    {
                        bestIdx = idx;
                        outBestScore = score;
                    }
                }
            }

            return bestIdx;
        }

        /// <summary>
        /// 申领裁决：槽位有余就直接给；满了就按 UE5 的规则找可抢者。
        /// 对应 UE5 <c>TryClaimToken</c>（cpp:400-462）的整体结构。
        /// </summary>
        /// <param name="maxTokens">本 ASC 允许的攻击令牌总数。</param>
        /// <param name="holders">当前账本（按授予顺序）。</param>
        /// <param name="bClaimerValid">申领者控制器有效。</param>
        /// <param name="bClaimerHasPawn">申领者有可控 Pawn。</param>
        /// <param name="bAvatarValid">本 ASC 有可用的拥有者 Actor。</param>
        /// <param name="distanceSqFromClaimer">申领者到本 ASC 拥有者的距离平方。</param>
        /// <param name="now">当前游戏时间。</param>
        /// <param name="settings">抢夺参数。</param>
        /// <param name="bStealScanEnabled">抢夺规则是否可用（战斗设置为空时 UE5 的 CanStealToken
        /// 对每个槽位都返回 false，等价于整轮抢夺扫描被跳过，但仍允许占用空闲槽位）。</param>
        public static AttackTokenClaimDecision Decide(
            int maxTokens,
            IList<AttackTokenHolderView> holders,
            bool bClaimerValid,
            bool bClaimerHasPawn,
            bool bAvatarValid,
            float distanceSqFromClaimer,
            float now,
            AttackTokenStealSettings settings,
            bool bStealScanEnabled = true)
        {
            var decision = new AttackTokenClaimDecision();

            // UE5:402 —— 三者任一为空则整个函数直接返回 false（连扫描都不做）。
            if (!bClaimerValid || !bClaimerHasPawn || !bAvatarValid)
            {
                decision.Result = EAttackTokenClaim.DeniedInvalidClaimer;
                return decision;
            }

            int granted = holders?.Count ?? 0;

            if (granted < maxTokens)
            {
                decision.Result = EAttackTokenClaim.Granted;
                return decision;
            }

            // 满槽：尝试抢夺（规则不可用时整轮扫描跳过，与 UE5 CanStealToken 恒 false 等价）。
            int found = NoToken;
            float best = 0f;
            if (bStealScanEnabled)
            {
                found = FindStealIndex(holders, distanceSqFromClaimer, now, settings, out best);
            }

            if (found != NoToken && found >= 0 && found < granted)
            {
                decision.Result = EAttackTokenClaim.Granted;
                decision.StealIndex = found;
                decision.bMustNotifyVictim = true;  // UE5:446 在归还之前通知被抢者
                decision.StealScore = best;
                return decision;
            }

            // UE5:452 —— 抢不到就再校验一次槽位（仍然满），拒绝。
            decision.Result = EAttackTokenClaim.DeniedNoOpenToken;
            return decision;
        }

        /// <summary>
        /// 剩余可用令牌数。对应 UE5 <c>GetAvailableAttackTokens</c>（cpp:560-563，
        /// 端口把 int.MaxValue 视为「不设限」并把已授予数收敛为 0 下限，避免减法溢出成负数）。
        /// </summary>
        public static int AvailableTokens(int maxTokens, int grantedTokens)
        {
            if (maxTokens == int.MaxValue) return int.MaxValue;
            return Math.Max(0, maxTokens - grantedTokens);
        }

        /// <summary>
        /// 按持有者身份找它的槽位下标（首个命中即返回，对应 UE5 <c>ReturnToken</c>:478-491 的 break）。
        /// </summary>
        public static int FindIndexForOwner(IList<AttackTokenHolderView> holders, object ownerKey)
        {
            if (holders == null || ownerKey == null) return NoToken;
            for (int i = 0; i < holders.Count; i++)
            {
                var holder = holders[i];
                if (holder != null && ReferenceEquals(holder.OwnerKey, ownerKey)) return i;
            }
            return NoToken;
        }
    }

    /// <summary>
    /// NPC 选择攻击目标的打分规则（对应 UE5 AttackPriority 的实际用途：
    /// EnvQueryTest_AttackPriority.cpp:54 把优先级当分数，端口把它与距离合成）。
    /// </summary>
    public static class NarrativeAttackTargetScoring
    {
        /// <summary>优先级每高 1 点折算的距离（厘米口径，端口里的距离单位）。</summary>
        public const float PriorityDistanceWeight = 100f;

        /// <summary>
        /// 目标评分：越近越好、优先级越高越好，因此分数越小越优先。
        /// </summary>
        public static float Score(float distanceToTarget, float attackPriority)
        {
            return distanceToTarget - attackPriority * PriorityDistanceWeight;
        }

        /// <summary>
        /// 并列规则：只有*严格更小*的分数才替换当前最优 —— 同分（同距离同优先级）时保留
        /// 先被枚举到的候选，保证目标选择不随枚举顺序抖动。
        /// </summary>
        public static bool IsBetterCandidate(float candidateScore, float bestScore)
        {
            return candidateScore < bestScore;
        }
    }
}
