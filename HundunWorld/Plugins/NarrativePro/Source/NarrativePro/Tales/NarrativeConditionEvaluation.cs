// 模块关系说明：
// 本文件属于 NarrativePro.Tales（UE5 NarrativeArsenal 条件系统移植）。
//
// 为什么需要它：节点级条件（UNarrativeNodeBase::AreConditionsMet，NarrativeNodeBase.cpp:107-209）
// 与对话行级条件（UDialogueNode::GetRandomLine 内联的同一段逻辑，DialogueSM.cpp:29-125）共用同一套
// 判定语义：bNot 取反、ConditionFilter 选目标集、PartyConditionPolicy 决定在谁身上跑。
// 这套语义原本长在 TalesComponent/NarrativeCharacterSubsystem（都是 FlaxEngine.Script，
// 无引擎环境 new 不出来）中间，因此进程外没有任何一条断言能打到它 ——
// 而它恰恰是本项目踩过的那类「写反一个取反就全线静默失效」的规则
// （队伍分支的 PartyPasses/PartyLeaderPasses 曾经把「失败」当「通过」返回）。
//
// 本文件把整套判定收敛为纯托管函数：条件自身只提供「原始判定回调」（引擎侧把
// CheckCondition(...) == bNot 的结果喂进来），目标集/成员键/队长键由调用方准备。
// 实时通路（NarrativeCondition.Evaluate / NarrativeNodeBase.AreConditionsMet /
// DialogueNode.IsLineConditionsMet）与 harness 驱动的是同一份实现。

using System;
using System.Collections.Generic;
using NarrativePro.Core;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 一次原始判定的三个入参（对应 UE5 <c>CheckCondition(APawn*, APlayerController*, UTalesComponent*)</c>）。
    /// 纯层不认识具体类型，只负责把它原样交给回调 —— 队伍分支必须传 (null, null, Party)，
    /// 角色目标分支必须传 (Character, Controller, 原组件)，两者搞混会让条件跑在错误的对象上。
    /// </summary>
    public sealed class NarrativeConditionEvalTarget
    {
        /// <summary>UE5 的 Pawn 位。</summary>
        public object Pawn;

        /// <summary>UE5 的 Controller 位。</summary>
        public object Controller;

        /// <summary>UE5 的 NarrativeComponent 位。</summary>
        public object Component;

        public NarrativeConditionEvalTarget() { }

        public NarrativeConditionEvalTarget(object pawn, object controller, object component)
        {
            Pawn = pawn;
            Controller = controller;
            Component = component;
        }
    }

    /// <summary>
    /// 单条条件求值的纯输入。所有「谁参与判定」的事实都在这里被预先取好，
    /// 判定顺序与短路规则由 <see cref="NarrativeConditionEvaluation"/> 决定。
    /// </summary>
    public sealed class NarrativeConditionEvalRequest
    {
        /// <summary>本条件的队伍策略（对应 UE5 <c>PartyConditionPolicy</c>）。</summary>
        public EPartyConditionPolicy PartyConditionPolicy = EPartyConditionPolicy.None;

        /// <summary>
        /// 是否满足走队伍分支的三个前提：策略 &gt; None、拿到了队伍组件、且不是单机。
        /// 对应 UE5 <c>Policy > None && Party && GetNetMode() != NM_Standalone</c>（NarrativeNodeBase.cpp:130）。
        /// </summary>
        public bool bPartyBranchAvailable;

        /// <summary>队伍自身的判定入参（UE5 用 (null, null, Party) 跑 PartyPasses）。</summary>
        public NarrativeConditionEvalTarget PartyTarget;

        /// <summary>队长的判定入参；null 表示 UE5 会直接对空 Leader 解引用（端口选择判为不通过）。</summary>
        public NarrativeConditionEvalTarget PartyLeaderTarget;

        /// <summary>
        /// 队员的判定入参列表，允许含 null 元素 —— UE5 循环体里有 <c>if (Member)</c> 守卫（:151），
        /// null 槽位必须被跳过而不是终止遍历。
        /// </summary>
        public List<NarrativeConditionEvalTarget> PartyMembers;

        /// <summary>ConditionFilter 选出的目标定义是否非空（对应 UE5 <c>CharTargets.Num()</c>）。</summary>
        public bool bHasCharacterTargets;

        /// <summary>角色子系统是否可用（UE5 拿不到 World/Subsystem 时整条条件判失败）。</summary>
        public bool bCharacterSubsystemAvailable;

        /// <summary>
        /// 已由角色子系统把定义解析成实际 Actor 的目标入参集合（解析不到的定义已被跳过，可为空）。
        /// </summary>
        public List<NarrativeConditionEvalTarget> ResolvedTargets;

        /// <summary>标准路径的判定入参（UE5 传入的 Pawn）。</summary>
        public NarrativeConditionEvalTarget PawnTarget;

        /// <summary>
        /// 原始判定回调：对给定入参返回「该条件是否失败」。
        /// 语义严格等于 UE5 的 <c>(Cond->CheckCondition(...) == Cond->bNot)</c> ——
        /// 取反已经在这一个地方完成过一次，求值器内部不得再次取反。
        /// </summary>
        public Func<NarrativeConditionEvalTarget, bool> RawFails;
    }

    /// <summary>单条条件求值的失败原因（供日志/测试区分「为什么没过」）。</summary>
    public enum EConditionEvalFailure
    {
        /// <summary>条件通过。</summary>
        None,

        /// <summary>叙事组件为空（UE5 直接 return false 并告警）。</summary>
        NullNarrativeComponent,

        /// <summary>PartyLeaderPasses 但队伍没有队长。</summary>
        PartyLeaderMissing,

        /// <summary>配置了角色目标却拿不到角色子系统。</summary>
        CharacterSubsystemMissing,

        /// <summary>原始判定失败（含 bNot 取反语义）。</summary>
        RawCheckFailed,
    }

    /// <summary>
    /// 条件求值的纯函数层（不引用 FlaxEngine / Script 类型，可无引擎运行）。
    /// </summary>
    public static class NarrativeConditionEvaluation
    {
        /// <summary>
        /// 单条条件的完整判定。返回 true 表示条件满足。
        /// 逐分支对应 UE5 NarrativeNodeBase.cpp:117-204。
        /// </summary>
        /// <param name="request">预先取好的判定输入。</param>
        /// <param name="bNarrativeComponentAvailable">叙事组件是否存在（UE5 :109-113 的入口守卫）。</param>
        /// <param name="outFailure">输出失败原因。</param>
        public static bool Evaluate(NarrativeConditionEvalRequest request, bool bNarrativeComponentAvailable, out EConditionEvalFailure outFailure)
        {
            outFailure = EConditionEvalFailure.None;

            if (!bNarrativeComponentAvailable)
            {
                outFailure = EConditionEvalFailure.NullNarrativeComponent;
                return false;
            }

            if (request == null)
            {
                outFailure = EConditionEvalFailure.RawCheckFailed;
                return false;
            }

            Func<NarrativeConditionEvalTarget, bool> fails = request.RawFails ?? FailNever;

            // ① 队伍分支（UE5 :130-172）。
            if (request.PartyConditionPolicy != EPartyConditionPolicy.None && request.bPartyBranchAvailable)
            {
                switch (request.PartyConditionPolicy)
                {
                    case EPartyConditionPolicy.PartyPasses:
                        // UE5:135-139 —— 在队伍组件自身上跑；失败当且仅当 RawFails(队伍)。
                        if (fails(request.PartyTarget))
                        {
                            outFailure = EConditionEvalFailure.RawCheckFailed;
                            return false;
                        }
                        return true;

                    case EPartyConditionPolicy.PartyLeaderPasses:
                    {
                        // UE5:141-147 —— `Leader->GetOwningPawn()` 对空队长是直接解引用（崩溃）。
                        // 端口不能崩，判为不通过；这是有意的空值加固，不改变非空时的结论。
                        if (request.PartyLeaderTarget == null)
                        {
                            outFailure = EConditionEvalFailure.PartyLeaderMissing;
                            return false;
                        }
                        if (fails(request.PartyLeaderTarget))
                        {
                            outFailure = EConditionEvalFailure.RawCheckFailed;
                            return false;
                        }
                        return true;
                    }

                    case EPartyConditionPolicy.AllPlayersPass:
                    {
                        // UE5:148-165 —— 任一队员失败即整体失败。
                        if (request.PartyMembers != null)
                        {
                            foreach (var member in request.PartyMembers)
                            {
                                if (member == null) continue; // UE5 的 if (Member) 守卫
                                if (fails(member))
                                {
                                    outFailure = EConditionEvalFailure.RawCheckFailed;
                                    return false;
                                }
                            }
                        }
                        return true;
                    }

                    case EPartyConditionPolicy.AnyPlayerPasses:
                    {
                        // UE5:148-170 的「落空即通过」怪癖（照抄，不修）：
                        // 循环体内只有 AllPlayersPass 会 return false；AnyPlayerPasses 命中成功时
                        // 仅 break，失败时什么都不做继续找下一个。循环结束后无条件走到 :171 的
                        // `return true;`。因此「没有任何队员通过」时 UE5 判定为通过。
                        if (request.PartyMembers != null)
                        {
                            foreach (var member in request.PartyMembers)
                            {
                                if (member == null) continue;
                                if (!fails(member)) break; // 找到一名通过者，与 UE5 的 break 等价
                            }
                        }
                        return true;
                    }
                }

                // UE5:171 —— 策略枚举里没被上面覆盖到的取值（理论上不存在）也落到 return true。
                return true;
            }

            // ② 角色目标分支（UE5 :176-197）。
            if (request.bHasCharacterTargets)
            {
                if (!request.bCharacterSubsystemAvailable)
                {
                    outFailure = EConditionEvalFailure.CharacterSubsystemMissing;
                    return false;
                }

                if (request.ResolvedTargets != null)
                {
                    foreach (var target in request.ResolvedTargets)
                    {
                        if (target == null) continue;
                        if (fails(target))
                        {
                            outFailure = EConditionEvalFailure.RawCheckFailed;
                            return false;
                        }
                    }
                }

                // UE5:197 —— 目标分支跑完就是 return true（不再回落到标准路径）。
                return true;
            }

            // ③ 标准路径（UE5 :198-203）：跑在传入的 Pawn 上。
            if (fails(request.PawnTarget))
            {
                outFailure = EConditionEvalFailure.RawCheckFailed;
                return false;
            }

            return true;
        }

        /// <summary>不带失败原因的便捷重载。</summary>
        public static bool Evaluate(NarrativeConditionEvalRequest request, bool bNarrativeComponentAvailable = true)
        {
            return Evaluate(request, bNarrativeComponentAvailable, out _);
        }

        /// <summary>
        /// 「与」语义：节点/对话行上的全部条件都要满足。
        /// 对应 UE5 <c>for (auto&amp; Cond : Conditions) { ... return false; } return true;</c>
        /// （NarrativeNodeBase.cpp:116,206）—— 空条件集恒为真，null 条目被跳过（:118）。
        /// </summary>
        public static bool EvaluateAll(IReadOnlyList<NarrativeConditionEvalRequest> requests, bool bNarrativeComponentAvailable)
        {
            if (requests == null || requests.Count == 0) return bNarrativeComponentAvailable;

            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request == null) continue;
                if (!Evaluate(request, bNarrativeComponentAvailable, out _)) return false;
            }

            return true;
        }

        /// <summary>
        /// 「原始判定永不失败」的占位回调，供调用方在条件缺少实现时保持 UE5 的
        /// <c>CheckCondition_Implementation</c> 默认返回 true（即不失败，bNot=false 时）。
        /// </summary>
        public static readonly Func<NarrativeConditionEvalTarget, bool> FailNever = _ => false;

        /// <summary>
        /// 由条件结果构造一条「已过 bNot 取反」的失败回调（测试与调用方复用同一取反口径，
        /// 避免第二处实现 <c>result == bNot</c>）。
        /// </summary>
        public static Func<NarrativeConditionEvalTarget, bool> MakeFailsFromResult(
            Func<NarrativeConditionEvalTarget, bool> rawPasses, bool bNot)
        {
            if (rawPasses == null) return FailNever;
            return target => rawPasses(target) == bNot;
        }
    }
}
