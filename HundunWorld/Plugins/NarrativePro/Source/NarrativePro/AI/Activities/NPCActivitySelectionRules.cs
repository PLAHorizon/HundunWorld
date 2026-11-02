// 模块关系说明：
// 本文件属于 NarrativePro.AI.Activities（UE5 NarrativeArsenal NPC 活动系统移植）。
//
// 为什么需要它：「哪个活动该跑、什么时候该停、什么时候该换」这套评选规则长在
// UNPCActivityComponent（FlaxEngine.Script，无引擎环境 new 不出来）里，进程外打不到；
// 而这条链路正是本项目实际踩过的静默缺陷类型：
//   ① `score > 0` 这道门一旦被漏掉，0 分/负分活动会被当成合法选择并反复启动；
//   ② 没有任何候选时必须停掉当前活动（UE5:298-303），漏掉会让 NPC 抱着已失效的目标不放；
//   ③ BlockTags 命中时必须比更高分的活动先出局（UE5:366-376），写反就是带病活动上线。
//
// 本文件把评选/门控/目标重选启发式抽成纯函数（不引用 FlaxEngine 类型）：候选的活动实例与
// 目标实例在纯层只是 opaque key，评分由实时侧的 ScoreActivity 提供。
// 实时通路 NPCActivityComponent.PerformActivitySelection / CanRunActivity / AddGoal 全部经由这里，
// harness 喂进纯数据候选跑的是同一份实现。
//
// UE5 口径核对：
//   NPCActivityComponent.cpp:240-253 当前活动仍有目标且重评 >0 且非强制检查 → 保留继续跑；
//   :281-296 逐个活动评分，`Score > 0.f && Score > BestScore` 才成为最优（平分时保留先枚举者）；
//   :298-303 `if (!ActivityToRun) { StopActivity_Internal(CurrentActivity); CurrentActivity = nullptr; }`；
//   :344-385 CanRunActivity 的门控顺序：权威 → 模板非空 → BlockTags → RequireTags → 模板自查；
//   :686-694 AddGoal 的目标重选启发式：没有当前活动/当前目标，或新目标分*严格大于*当前目标分
//           → PerformActivitySelection(true)；否则仅在 bTriggerReselect 时做保守重选。

using System;
using System.Collections.Generic;

namespace NarrativePro.AI.Activities
{
    /// <summary>参与一轮评选的单个活动候选（评分与门控结果由实时侧取好）。</summary>
    public sealed class ActivityCandidate
    {
        /// <summary>活动实例标识（纯层只比引用；实时侧是 <see cref="NPCActivity"/>）。</summary>
        public object ActivityKey;

        /// <summary>该活动本轮拿到的目标（实时侧是 <see cref="NPCGoalItem"/>），可为 null。</summary>
        public object GoalKey;

        /// <summary>本轮评分（对应 UE5 <c>Activity->ScoreActivity(...)</c> 的返回值）。</summary>
        public float Score;

        /// <summary>是否通过 <see cref="NPCActivitySelectionRules.DecideTagGate"/> 门控。</summary>
        public bool bRunnable;
    }

    /// <summary>一轮评选后要执行的动作。</summary>
    public enum EActivitySelectionAction
    {
        /// <summary>当前活动仍然有效：什么都不做，继续跑。</summary>
        KeepCurrent,

        /// <summary>没有任何候选：停掉当前活动（UE5:298-303）。</summary>
        StopCurrent,

        /// <summary>启动选中的活动（必要时先停掉不同的当前活动）。</summary>
        StartCandidate,

        /// <summary>选中的就是当前活动且没有更好：保持现状不重启。</summary>
        NoChange,
    }

    /// <summary>评选裁决输出。</summary>
    public sealed class ActivitySelectionDecision
    {
        /// <summary>要执行的动作。</summary>
        public EActivitySelectionAction Action = EActivitySelectionAction.NoChange;

        /// <summary><see cref="EActivitySelectionAction.StartCandidate"/> 时命中的候选下标，否则 -1。</summary>
        public int CandidateIndex = -1;

        /// <summary>本轮最高分（无候选时为 <see cref="NoScore"/>）。</summary>
        public float BestScore = NoScore;

        /// <summary>无需动作的原因（仅诊断用）。</summary>
        public string FailReason = "";

        /// <summary>无分数哨兵（对应 UE5 的 BestScore 初值 -1）。</summary>
        public const float NoScore = -1f;
    }

    /// <summary>活动门控结果（顺序即优先级，对应 UE5 CanRunActivity 的判定次序）。</summary>
    public enum EActivityGateResult
    {
        /// <summary>允许运行。</summary>
        Allowed,

        /// <summary>拥有者没有权威。</summary>
        NotAuthority,

        /// <summary>模板为空。</summary>
        NullTemplate,

        /// <summary>拥有者命中 BlockTags。</summary>
        BlockedTag,

        /// <summary>拥有者缺少 RequireTags。</summary>
        MissingRequiredTag,

        /// <summary>模板自身拒绝。</summary>
        TemplateRefused,
    }

    /// <summary>新目标加入后该如何重选（对应 UE5 AddGoal 尾部的启发式）。</summary>
    public enum EGoalReselect
    {
        /// <summary>不重选。</summary>
        None,

        /// <summary>强制检查新活动（PerformActivitySelection(true)）。</summary>
        ReselectCheckNew,

        /// <summary>保守重选（PerformActivitySelection(false)：当前活动仍有效则保留）。</summary>
        ReselectKeepCurrentAllowed,
    }

    /// <summary>
    /// NPC 活动评选的纯函数层（不引用 FlaxEngine / Script 类型，可无引擎运行）。
    /// </summary>
    public static class NPCActivitySelectionRules
    {
        /// <summary>
        /// 启动门槛：UE5:295 的 <c>Score &gt; 0.f</c>。0 分与负分一律不得成为被选中的活动，
        /// 这条门是被专门补回来的（漏掉会让「没有事做」的活动被当成有事做）。
        /// </summary>
        public static bool IsHighEnoughToStart(float score)
        {
            return score > 0f;
        }

        /// <summary>
        /// 在候选里取最优：只有 *同时* 满足「门控通过」「分数 &gt; 0」「严格大于当前最优」才算赢，
        /// 因此平分时候选顺序靠前者胜出（保证确定性，不随枚举顺序抖动）。
        /// </summary>
        /// <returns>最优候选下标，没有可用候选时返回 -1。</returns>
        public static int FindBestIndex(IReadOnlyList<ActivityCandidate> candidates)
        {
            if (candidates == null || candidates.Count == 0) return -1;

            int best = -1;
            float bestScore = ActivitySelectionDecision.NoScore;

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null || !c.bRunnable) continue;
                if (!IsHighEnoughToStart(c.Score)) continue;
                if (c.Score > bestScore)
                {
                    bestScore = c.Score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 「当前活动仍然有效就继续跑」。对应 UE5:240-253：当前活动必须有目标、
        /// 本轮重评分数仍 &gt; 0，且调用方没有强制要求检查新活动。
        /// 注意 UE5 的三个条件缺一不可 —— 只判 <c>CurrentActivity != null</c> 会让失效活动永动。
        /// </summary>
        public static bool KeepsRunningCurrentActivity(float currentActivityScore, bool bCheckNew, bool bCurrentActivityHasGoal)
        {
            if (bCheckNew) return false;
            if (!bCurrentActivityHasGoal) return false;
            return IsHighEnoughToStart(currentActivityScore);
        }

        /// <summary>
        /// 一轮评选的完整裁决。
        /// </summary>
        /// <param name="candidates">已完成评分与门控的候选列表（顺序即 UE5 的 Activities 枚举顺序）。</param>
        /// <param name="currentActivityKey">当前活动（null 表示没有）。</param>
        /// <param name="currentGoalKey">当前活动的目标。</param>
        /// <param name="currentLastScore">当前活动上次得分（同名活动要*更高分*才重启）。</param>
        /// <param name="currentScore">当前活动本轮重评分数（用于 <see cref="KeepsRunningCurrentActivity"/>）。</param>
        /// <param name="bCheckNew">是否强制检查新活动。</param>
        public static ActivitySelectionDecision Decide(
            IReadOnlyList<ActivityCandidate> candidates,
            object currentActivityKey,
            object currentGoalKey,
            float currentLastScore,
            float currentScore,
            bool bCheckNew)
        {
            var decision = new ActivitySelectionDecision();

            // ① 当前活动仍然有效 → 保留（UE5:252-256）。
            if (currentActivityKey != null &&
                KeepsRunningCurrentActivity(currentScore, bCheckNew, currentGoalKey != null))
            {
                decision.Action = EActivitySelectionAction.KeepCurrent;
                decision.FailReason = "Current activity still scores above zero";
                return decision;
            }

            // ② 选出最优候选。
            int best = FindBestIndex(candidates);
            if (best < 0)
            {
                // ③ 没有任何可跑的活动 → 停止当前活动（UE5:298-303）。
                decision.Action = currentActivityKey != null
                    ? EActivitySelectionAction.StopCurrent
                    : EActivitySelectionAction.NoChange;
                decision.FailReason = "No activity scored above zero";
                return decision;
            }

            decision.BestScore = candidates[best].Score;

            var winner = candidates[best];

            // ④ 选中的就是当前活动：端口额外要求「分数比上次更高」才允许重启同名活动
            //    （避免每秒重启动同一个活动）；换了别的活动则直接启动。
            if (currentActivityKey != null && ReferenceEquals(currentActivityKey, winner.ActivityKey))
            {
                if (decision.BestScore > currentLastScore)
                {
                    decision.Action = EActivitySelectionAction.StartCandidate;
                    decision.CandidateIndex = best;
                    return decision;
                }

                decision.Action = EActivitySelectionAction.NoChange;
                decision.FailReason = "Same activity without a better score";
                return decision;
            }

            decision.Action = EActivitySelectionAction.StartCandidate;
            decision.CandidateIndex = best;
            return decision;
        }

        /// <summary>
        /// 活动门控：把 UE5 CanRunActivity（:344-385）的判定次序收敛成一个纯裁决。
        /// BlockTags 命中先于 RequireTags 与模板自查，因此*高分但被标签挡住*的活动必须出局。
        /// </summary>
        /// <param name="bHasAuthority">拥有者控制器是否有执行权威。</param>
        /// <param name="bTemplateNull">模板是否为空。</param>
        /// <param name="bOwnerHasAnyBlockTag">拥有者命中任一 BlockTags。</param>
        /// <param name="bOwnerHasAllRequireTags">拥有者是否具备全部 RequireTags。</param>
        /// <param name="bTemplateAllows">模板自查结果（对应 UE5 <c>ActivityTemplate->CanRunActivity</c>）。</param>
        public static EActivityGateResult DecideTagGate(
            bool bHasAuthority,
            bool bTemplateNull,
            bool bOwnerHasAnyBlockTag,
            bool bOwnerHasAllRequireTags,
            bool bTemplateAllows)
        {
            if (!bHasAuthority) return EActivityGateResult.NotAuthority;
            if (bTemplateNull) return EActivityGateResult.NullTemplate;
            if (bOwnerHasAnyBlockTag) return EActivityGateResult.BlockedTag;
            if (!bOwnerHasAllRequireTags) return EActivityGateResult.MissingRequiredTag;
            if (!bTemplateAllows) return EActivityGateResult.TemplateRefused;
            return EActivityGateResult.Allowed;
        }

        /// <summary>门控结果对应的 UE5 FailReason 文本（逐字对应 :350/:355/:369/:374）。</summary>
        public static string GetFailReason(EActivityGateResult gate)
        {
            switch (gate)
            {
                case EActivityGateResult.NotAuthority: return "Not Authority";
                case EActivityGateResult.NullTemplate: return "Null template provided";
                case EActivityGateResult.BlockedTag: return "Owner had blocked tag";
                case EActivityGateResult.MissingRequiredTag: return "Owner didnt have required tag";
                default: return "";
            }
        }

        /// <summary>
        /// 新目标加入后的重选启发式（UE5 AddGoal:686-694）。
        /// 「新目标严格大于当前目标分」才强制重选 —— 等分不得抢占，否则每个同分目标都会
        /// 触发一轮全量评选并可能打断正在跑的活动。
        /// </summary>
        /// <param name="bHasCurrentActivity">当前有活动在跑。</param>
        /// <param name="bCurrentActivityHasGoal">当前活动带着目标。</param>
        /// <param name="newGoalScore">新目标的 <c>GetGoalScore()</c>。</param>
        /// <param name="currentGoalScore">当前活动目标的 <c>GetGoalScore()</c>。</param>
        /// <param name="bTriggerReselect">调用方显式要求重选。</param>
        public static EGoalReselect DecideGoalReselect(
            bool bHasCurrentActivity,
            bool bCurrentActivityHasGoal,
            float newGoalScore,
            float currentGoalScore,
            bool bTriggerReselect)
        {
            bool bNoCurrentToLose = !bHasCurrentActivity || !bCurrentActivityHasGoal;
            if (bNoCurrentToLose || newGoalScore > currentGoalScore)
            {
                return EGoalReselect.ReselectCheckNew;
            }

            if (bTriggerReselect) return EGoalReselect.ReselectKeepCurrentAllowed;
            return EGoalReselect.None;
        }
    }
}
