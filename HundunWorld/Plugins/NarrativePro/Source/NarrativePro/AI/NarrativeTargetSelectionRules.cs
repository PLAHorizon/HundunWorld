// 模块关系说明：
// 本文件属于 NarrativePro.AI（UE5 NarrativeArsenal 三个 EQS 目标测试的移植：Team / AttackTokens / AttackPriority）。
//
// 为什么需要它：Flax 没有 EQS，UE5 把"这个候选能不能被我打"的三条裁决写在
// UEnvQueryTest_Team / UEnvQueryTest_AttackTokens / UEnvQueryTest_AttackPriority 的 RunTest 里，
// 端口此前只把 Team 压成了 NarrativeNPCController.FindBestAttackTarget 里的一句 "!= Hostile 就 continue"，
// 令牌与优先级两个测试完全没有对应物；而那个方法是 FlaxEngine.Script 成员（要 OverlapSphere + GetScript），
// 无引擎环境一条断言都打不到。三条规则各自的致命点：
//   ① Team 的三种态度是三个互相独立的开关，没被任一开关覆盖到的态度必须失败
//      （漏一条就会让 bIncludeNeutrals=false 时中立目标照样通过）；BoolValue 默认为 true，
//      因此"期望 false"时整条判定要取反（排除敌对而不是只选敌对）。
//   ② AttackTokens 对"没有 ASC 的候选"给 0 分而不是跳过它（EnvQueryTest_AttackTokens.cpp:47-58），
//      所以配上最小阈值 1 就等价于"令牌账本已满的目标不能选"；UE5 用 EQS 资产里的阈值表达这件事，
//      端口把它做成查询里的显式字段。
//   ③ AttackPriority 在候选没有角色定义时同样是 0 分而不是"沿用默认优先级"
//      （EnvQueryTest_AttackPriority.cpp:48-56），而端口 ASC 的 AttackPriority 字段默认就是 1f，
//      把字段值直接当分数会让没有定义的 Actor 白拿加分。
//
// 实时通路：NarrativeNPCController.FindBestAttackTarget 先装配 TargetCandidateView（纯数据），
// 再走本文件的 IsAcceptableTarget + ScoreOf；harness 驱动的是同一份实现。
//
// UE5 依据（只读参考工程 C:\Works\GitHubProjects\20260801\HundunWorld\Plugins\NarrativePro\Source\NarrativeArsenal）：
//   Public/AI/EnvQueryTest_Team.h:25-31 —— bIncludeFriendlies / bIncludeNeutrals / bIncludeEnemies 三个独立开关；
//   Private/AI/EnvQueryTest_Team.cpp:17-19 —— 构造里三者默认全为 true；
//   Private/AI/EnvQueryTest_Team.cpp:38-39 —— BoolValue.BindData 取"期望值"bWantsValid；
//   Private/AI/EnvQueryTest_Team.cpp:45-64 —— 态度由 UArsenalStatics::GetAttitude(QueryActor, ItemActor) 决定，
//     bPassesTest = (bIncludeEnemies && Hostile) || (bIncludeFriendlies && Friendly) || (bIncludeNeutrals && Neutral)，
//     最后 SetScore(TestPurpose, FilterType, bPassesTest, bWantsValid)：即"是否符合开关"要等于期望值才留下；
//   Private/AI/EnvQueryTest_AttackTokens.cpp:44-57 —— 逐候选取 ItemActor 的 ASC，
//     bCheckGrantedTokens ? GetNumGrantedAttackTokens() : GetAvailableAttackTokens()，无 ASC 则 Score=0；
//   Public/AI/EnvQueryTest_AttackTokens.h:27 —— bCheckGrantedTokens 的语义是"看已经发出去几个，而不是还剩几个"；
//   Private/AI/EnvQueryTest_AttackTokens.cpp:35-40 + :60 —— FloatValueMin/Max 绑定后作为阈值交给 SetScore；
//   Private/AI/EnvQueryTest_AttackPriority.cpp:44-56 —— Score 只来自 CharacterDefinition->AttackPriority，
//     候选不是 ANarrativeCharacter 或没有定义时 Score 保持 0；
//   引擎侧阈值语义（EnvironmentQuery/EnvQueryTest.h:88-100 + EnvQueryTest.cpp:12-19）：
//     FilterType 默认 Range，FloatValueMin/Max 都是**闭区间**（越界即判为无效候选），
//     BoolValue.DefaultValue = true，TestPurpose 默认 FilterAndScore 而三个测试各自覆写
//     （Team 用 Filter、Tokens/Priority 在构造里设成 Score，见 EnvQueryTest_AttackTokens.cpp:16、
//      EnvQueryTest_AttackPriority.cpp:18）。
//
// 端口差异（不臆造 UE5 没有的规则，只说明替代方式）：
//   · 没有 EQS 资产，阈值与开关做成 TargetSelectionQuery 的显式字段；UE5 的"默认不筛"（阈值 ±FLT_MAX）
//     在端口里就是 NoThreshold 常量，装配战斗查询请走 NarrativeTargetSelectionRules.CreateCombatQuery，
//     不要用 new TargetSelectionQuery()（零初始化会把态度开关全关掉，与 UE5 构造默认值相反）。
//   · UE5 的 ScoringEquation/Normalization（线性归一化后乘 ScoringFactor）端口没有实现：
//     FindBestAttackTarget 走的是端口既有的合成评分 NarrativeAttackTargetScoring.Score(距离, 优先级)
//     （对应 GAS/NarrativeAttackTokenLedger.cs:301-326 的既有约定），本文件只复用不重定义。
//   · 令牌测试在端口里多一条豁免：本控制器已经握着该目标的令牌时不因"槽位已满"被剔除。
//     UE5 不需要它，因为 BT 申请到令牌后不会再跑这条查询；端口 UpdateCombatAI 每 0.25s 重选目标，
//     少了这条就会出现"令牌在手却被自己的阈值踢掉→归还令牌→再抢同一个槽"的抖动。
//   · 距离门（MaxDistance）不属于这三个测试（UE5 是 UEnvQueryTest_Distance），端口把它并进同一份候选视图，
//     让"搜索半径"只有一处判定；闭区间与 UE5 Range 阈值口径一致。

using System;
using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.AI
{
    /// <summary>
    /// 一个候选目标的纯数据视图：实时侧由 NarrativeNPCController 从 Actor/ASC 取好，
    /// 规则层只认这些字段（不含任何 FlaxEngine 类型）。
    /// </summary>
    [Serializable]
    public sealed class TargetCandidateView
    {
        /// <summary>候选身份（实时侧是目标 ASC 实例），并列时用于保持"保留先枚举者"的确定性。</summary>
        public object Key;

        /// <summary>本控制器看该候选的态度（实时侧来自 ArsenalStatics.GetAttitude）。</summary>
        public ArsenalStatics.ETeamAttitude Attitude;

        /// <summary>是否与发起查询的 Actor 是同一个（UE5 靠 QueryActor，端口把它做成标记）。</summary>
        public bool bIsSelf;

        /// <summary>候选身上有没有 NarrativeAbilitySystemComponent（UE5 EnvQueryTest_AttackTokens.cpp:45-48 的 Cast 结果）。</summary>
        public bool bHasAbilitySystem;

        /// <summary>候选是否已死亡（端口既有门控，非 UE5 EQS 规则）。</summary>
        public bool bIsDead;

        /// <summary>候选是否带 DisableAggro 标签（友好村民不能被 NPC-vs-NPC 逻辑选中）。</summary>
        public bool bHoldsDisableAggro;

        /// <summary>候选有没有角色定义（UE5 EnvQueryTest_AttackPriority.cpp:50-55 的 Cast + GetCharacterDefinition）。</summary>
        public bool bHasCharacterDefinition;

        /// <summary>本控制器到候选中心的距离（端口单位：厘米）。</summary>
        public float Distance;

        /// <summary>候选的 AttackPriority 字段值（只有在 <see cref="bHasCharacterDefinition"/> 为真时才参与打分）。</summary>
        public float AttackPriority;

        /// <summary>候选 ASC 还剩几个攻击令牌槽（UE5 GetAvailableAttackTokens）。</summary>
        public int AvailableAttackTokens;

        /// <summary>候选 ASC 已经发出几个令牌（UE5 GetNumGrantedAttackTokens）。</summary>
        public int GrantedAttackTokens;

        /// <summary>本控制器是否已经持有该候选的令牌（端口豁免用，见文件头端口差异）。</summary>
        public bool bAttackerHoldsTokenOnTarget;
    }

    /// <summary>被剔除的原因（供日志与 harness 断言区分"哪条测试把它拦下了"）。</summary>
    public enum ETargetReject
    {
        /// <summary>通过全部裁决。</summary>
        None,

        /// <summary>候选为空（装配循环不该传 null，这里给一个区别于"自己"的原因）。</summary>
        Invalid,

        /// <summary>候选就是查询发起者自己。</summary>
        Self,

        /// <summary>候选已死亡（端口既有门控）。</summary>
        Dead,

        /// <summary>候选带 DisableAggro（端口既有门控）。</summary>
        DisableAggro,

        /// <summary>Team 测试未通过（态度开关组合与期望值不符，UE5 EnvQueryTest_Team.cpp:47-64）。</summary>
        TeamFilter,

        /// <summary>AttackTokens 测试未通过（令牌分数落在闭区间阈值之外，UE5 EnvQueryTest_AttackTokens.cpp:60）。</summary>
        TokenRange,

        /// <summary>AttackPriority 测试未通过（优先级分数落在闭区间阈值之外，UE5 EnvQueryTest_AttackPriority.cpp:58）。</summary>
        PriorityRange,

        /// <summary>超出查询距离门（闭区间之外）。</summary>
        TooFar,
    }

    /// <summary>
    /// 三条 EQS 测试的开关与阈值快照（对应 UE5 一个 EQS 资产里这三行 Test 的配置）。
    /// </summary>
    [Serializable]
    public struct TargetSelectionQuery
    {
        /// <summary>阈值"不设限"哨兵（UE5 未配置阈值时的等价效果：Range 闭区间永远命中）。</summary>
        public const float NoThreshold = float.MaxValue;

        /// <summary>距离门"不设限"哨兵。</summary>
        public const float NoDistanceLimit = float.MaxValue;

        // ---- Team 测试（UE5 EnvQueryTest_Team.h:25-31） ----

        public bool bRunTeamTest;
        public bool bIncludeFriendlies;
        public bool bIncludeNeutrals;
        public bool bIncludeEnemies;

        /// <summary>BoolValue 的期望值：true=留下符合开关组合的候选，false=留下不符合的（排除式过滤）。</summary>
        public bool bWantsValid;

        // ---- AttackTokens 测试（UE5 EnvQueryTest_AttackTokens.h:27 + .cpp:35-60） ----

        public bool bRunTokensTest;

        /// <summary>true 看"已发出几个"，false 看"还剩几个"。</summary>
        public bool bCheckGrantedTokens;

        public float TokenMinValue;
        public float TokenMaxValue;

        /// <summary>槽位已满但令牌在自己手里时是否豁免（端口差异，见文件头）。</summary>
        public bool bExemptCurrentTokenHolder;

        // ---- AttackPriority 测试（UE5 EnvQueryTest_AttackPriority.cpp:37-58） ----

        public bool bRunPriorityTest;
        public float PriorityMinValue;
        public float PriorityMaxValue;

        // ---- 距离门（端口侧的搜索半径，UE5 由 UEnvQueryTest_Distance 承担） ----

        public float MaxDistance;
    }

    /// <summary>
    /// 目标选择的引擎无关规则层：Team / AttackTokens / AttackPriority 三条测试 + 端口既有门控。
    /// </summary>
    public static class NarrativeTargetSelectionRules
    {
        /// <summary>
        /// NPC 战斗查询的端口默认值：只要敌对（与移植前 "!= Hostile 就跳过" 一致）、
        /// 令牌测试开启且要求候选至少剩 1 个槽（满槽目标先不去碰），距离门由调用方传入。
        /// </summary>
        public static TargetSelectionQuery CreateCombatQuery(float maxDistance = TargetSelectionQuery.NoDistanceLimit)
        {
            var query = new TargetSelectionQuery
            {
                bRunTeamTest = true,
                bIncludeFriendlies = false,
                bIncludeNeutrals = false,
                bIncludeEnemies = true,
                bWantsValid = true,

                bRunTokensTest = true,
                bCheckGrantedTokens = false,
                TokenMinValue = 1f,
                TokenMaxValue = TargetSelectionQuery.NoThreshold,
                bExemptCurrentTokenHolder = true,

                bRunPriorityTest = false,
                PriorityMinValue = TargetSelectionQuery.NoThreshold,
                PriorityMaxValue = TargetSelectionQuery.NoThreshold,

                MaxDistance = maxDistance,
            };
            return query;
        }

        /// <summary>
        /// UE5 构造默认值等价物：三个态度开关全开、期望 true，两个分数测试阈值不设限
        /// （EnvQueryTest_Team.cpp:17-19 + EnvQueryTest.cpp:15 的 Range 默认口径）。
        /// </summary>
        public static TargetSelectionQuery CreateQueryAllAttitudes(float maxDistance = TargetSelectionQuery.NoDistanceLimit)
        {
            var query = CreateCombatQuery(maxDistance);
            query.bIncludeFriendlies = true;
            query.bIncludeNeutrals = true;
            query.bRunTokensTest = false;
            return query;
        }

        /// <summary>
        /// Team 测试本体（UE5 EnvQueryTest_Team.cpp:45-64）：三种态度各由独立开关放行，
        /// 未被任何开关覆盖的态度 bPassesTest 为 false；结果还要与期望值 bWantsValid 相等才留下。
        /// </summary>
        public static bool PassesTeamFilter(ArsenalStatics.ETeamAttitude attitude, TargetSelectionQuery query)
        {
            if (!query.bRunTeamTest) return true;

            bool bPassesTest = false;

            if (query.bIncludeEnemies && attitude == ArsenalStatics.ETeamAttitude.Hostile) bPassesTest = true;
            if (query.bIncludeFriendlies && attitude == ArsenalStatics.ETeamAttitude.Friendly) bPassesTest = true;
            if (query.bIncludeNeutrals && attitude == ArsenalStatics.ETeamAttitude.Neutral) bPassesTest = true;

            return bPassesTest == query.bWantsValid;
        }

        /// <summary>
        /// AttackTokens 分数（UE5 EnvQueryTest_AttackTokens.cpp:45-58）：没有 ASC 的候选记 0 分而不是跳过；
        /// bCheckGrantedTokens 决定看"已发出"还是"还剩"。
        /// </summary>
        public static float AttackTokensScore(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            if (candidate == null || !candidate.bHasAbilitySystem) return 0f;
            return query.bCheckGrantedTokens ? candidate.GrantedAttackTokens : candidate.AvailableAttackTokens;
        }

        /// <summary>
        /// AttackPriority 分数（UE5 EnvQueryTest_AttackPriority.cpp:48-56）：
        /// 候选没有角色定义时保持 0 分，即使端口 ASC 的优先级字段默认是 1f。
        /// </summary>
        public static float AttackPriorityScore(TargetCandidateView candidate)
        {
            if (candidate == null || !candidate.bHasCharacterDefinition) return 0f;
            return candidate.AttackPriority;
        }

        /// <summary>
        /// Range 阈值判定：UE5 的 FloatValueMin/Max 都是闭区间（EnvQueryTest.h:92-100 的 inclusive 注释），
        /// 未配置的边界用 <see cref="TargetSelectionQuery.NoThreshold"/> 表示不设限。
        /// </summary>
        public static bool PassesFloatRange(float value, float minValue, float maxValue)
        {
            if (minValue != TargetSelectionQuery.NoThreshold && value < minValue) return false;
            if (maxValue != TargetSelectionQuery.NoThreshold && value > maxValue) return false;
            return true;
        }

        /// <summary>
        /// 令牌测试的完整放行判定：先按 UE5 的分数+闭区间阈值；端口差异是"令牌已在本控制器手里"的目标
        /// 不因满槽被剔除，否则 UpdateCombatAI 会在自己刚申请的槽位上反复进出战斗。
        /// </summary>
        public static bool PassesTokensTest(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            if (!query.bRunTokensTest) return true;

            float score = AttackTokensScore(candidate, query);
            if (PassesFloatRange(score, query.TokenMinValue, query.TokenMaxValue)) return true;

            return query.bExemptCurrentTokenHolder && candidate != null && candidate.bAttackerHoldsTokenOnTarget;
        }

        /// <summary>Priority 测试的放行判定（分数测试在没有阈值时不可能筛掉任何东西，与 UE5 一致）。</summary>
        public static bool PassesPriorityTest(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            if (!query.bRunPriorityTest) return true;
            return PassesFloatRange(AttackPriorityScore(candidate), query.PriorityMinValue, query.PriorityMaxValue);
        }

        /// <summary>
        /// 一条候选的完整裁决，顺序即语义：自身 → 死亡 → DisableAggro → 距离门 → Team → Tokens → Priority。
        /// 前三条是端口既有门控（UE5 由生成器/黑板保证，端口在同一个循环里做），
        /// 后四条就是三个 EQS 测试。
        /// </summary>
        public static ETargetReject Classify(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            if (candidate == null) return ETargetReject.Invalid;
            if (candidate.bIsSelf) return ETargetReject.Self;
            if (candidate.bIsDead) return ETargetReject.Dead;
            if (candidate.bHoldsDisableAggro) return ETargetReject.DisableAggro;

            // 距离门闭区间：正好卡在搜索半径上的目标仍然算"已发现"（UE5 Range 的 inclusive 口径）
            if (query.MaxDistance != TargetSelectionQuery.NoDistanceLimit && candidate.Distance > query.MaxDistance)
                return ETargetReject.TooFar;

            if (!PassesTeamFilter(candidate.Attitude, query)) return ETargetReject.TeamFilter;
            if (!PassesTokensTest(candidate, query)) return ETargetReject.TokenRange;
            if (!PassesPriorityTest(candidate, query)) return ETargetReject.PriorityRange;

            return ETargetReject.None;
        }

        /// <summary><see cref="Classify"/> 的布尔门面，实时循环用它决定是否跳过候选。</summary>
        public static bool IsAcceptableTarget(TargetCandidateView candidate, TargetSelectionQuery query, out ETargetReject reason)
        {
            reason = Classify(candidate, query);
            return reason == ETargetReject.None;
        }

        /// <summary><see cref="IsAcceptableTarget"/> 的无输出重载。</summary>
        public static bool IsAcceptableTarget(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            return Classify(candidate, query) == ETargetReject.None;
        }

        /// <summary>
        /// 候选排序分：端口把 UE5 两个 Score 测试的意图合成一个"越小越优先"的分 ——
        /// 距离减优先级加权。打分本体复用 GAS 层的既有纯函数，避免两处口径漂移。
        /// </summary>
        public static float ScoreOf(TargetCandidateView candidate, TargetSelectionQuery query)
        {
            // 优先级分取自角色定义存在性门控（UE5 EnvQueryTest_AttackPriority.cpp:50-55）：
            // 没有定义的候选即便 ASC 字段带着默认 1f，也只能按 0 分参与合成，否则会白拿加分。
            return NarrativePro.GAS.NarrativeAttackTargetScoring.Score(
                candidate.Distance, AttackPriorityScore(candidate));
        }

        /// <summary>
        /// 完整选目标：枚举顺序稳定，同分保留先被枚举到的候选（并列规则复用 GAS 层）。
        /// </summary>
        /// <returns>最佳候选下标，无可选项时返回 -1。</returns>
        public static int ChooseBestTarget(IList<TargetCandidateView> candidates, TargetSelectionQuery query, out float bestScore)
        {
            bestScore = float.MaxValue;
            int bestIndex = -1;
            if (candidates == null) return -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!IsAcceptableTarget(candidate, query)) continue;

                float score = ScoreOf(candidate, query);
                if (NarrativePro.GAS.NarrativeAttackTargetScoring.IsBetterCandidate(score, bestScore))
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary><see cref="ChooseBestTarget(IList{TargetCandidateView},TargetSelectionQuery,out float)"/> 的简便重载。</summary>
        public static int ChooseBestTarget(IList<TargetCandidateView> candidates, TargetSelectionQuery query)
        {
            return ChooseBestTarget(candidates, query, out _);
        }
    }
}
