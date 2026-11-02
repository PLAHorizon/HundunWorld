using System;
using System.Collections.Generic;
using NarrativePro.AI;
using NarrativePro.Character;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events
{
    /// <summary>
    /// 叙事条件基类。对应 UE5 <c>UNarrativeCondition</c>（NarrativeCondition.h/cpp）。
    /// <para>
    /// UE5 中节点级 <c>AreConditionsMet</c>（NarrativeNodeBase.cpp:107-209）负责三件事：
    /// ① 队伍条件策略（<see cref="PartyConditionPolicy"/>）；② 角色目标解析（<see cref="GetCharacterTargets"/>
    /// 经 <see cref="NarrativeCharacterSubsystem.FindCharacter"/>）；③ <see cref="bNot"/> 取反。
    /// 这里把整套逻辑收敛进 <see cref="Evaluate"/>，由 <c>NarrativeNodeBase.AreConditionsMet</c>
    /// 与 <c>DialogueNode.GetRandomLine</c>（UE5 DialogueSM.cpp:29-125 复制了同一套逻辑）共用。
    /// </para>
    /// </summary>
    public abstract class NarrativeCondition
    {
        /// <summary>为 true 时翻转本条件的结果。对应 UE5 <c>bNot</c>。</summary>
        public bool bNot { get; set; } = false;

        /// <summary>此条件允许跑在什么角色上。对应 UE5 <c>ConditionFilter</c>，默认 CF_AnyCharacter。</summary>
        public EConditionFilter ConditionFilter { get; set; } = EConditionFilter.AnyCharacter;

        /// <summary>
        /// 队伍对话下如何判定本条件。对应 UE5 <c>PartyConditionPolicy</c>。
        /// UE5 的头文件默认值是 AnyPlayerPasses，但构造函数（NarrativeCondition.cpp:12）把它设成 None，
        /// 因此运行时默认值是 <see cref="EPartyConditionPolicy.None"/>。
        /// </summary>
        public EPartyConditionPolicy PartyConditionPolicy { get; set; } = EPartyConditionPolicy.None;

        /// <summary>需要跑此条件的角色定义。对应 UE5 <c>CharacterTargets</c>。</summary>
        public List<CharacterDefinition> CharacterTargets { get; set; } = new List<CharacterDefinition>();

        /// <summary>需要跑此条件的 NPC 定义。对应 UE5 <c>NPCTargets</c>。</summary>
        public List<NPCDefinition> NPCTargets { get; set; } = new List<NPCDefinition>();

        /// <summary>需要跑此条件的玩家定义。对应 UE5 <c>PlayerTargets</c>。</summary>
        public List<PlayerDefinition> PlayerTargets { get; set; } = new List<PlayerDefinition>();

        /// <summary>
        /// 子类实现真正的判定逻辑。对应 UE5 <c>CheckCondition_Implementation</c>。
        /// </summary>
        /// <param name="target">被判定的目标（UE5 中为 APawn，Flax 中通常是 Actor 或 NarrativeCharacter）。</param>
        /// <param name="controller">拥有者控制器，可能为 null（跑在队伍上时）。</param>
        /// <param name="narrativeComponent">跑此条件的叙事组件。</param>
        public abstract bool CheckCondition(object target, object controller, TalesComponent narrativeComponent);

        /// <summary>图中显示文本。对应 UE5 <c>GetGraphDisplayText_Implementation</c>（返回对象名）。</summary>
        public virtual string GetGraphDisplayText()
        {
            return GetType().Name;
        }

        /// <summary>
        /// 按 UE5 <c>EConditionFilter</c> 选出要跑条件的角色定义列表（NarrativeCondition.cpp:25-55）。
        /// </summary>
        public List<CharacterDefinition> GetCharacterTargets()
        {
            switch (ConditionFilter)
            {
                case EConditionFilter.DontTarget:
                    return new List<CharacterDefinition>();
                case EConditionFilter.OnlyNPCs:
                    return ToCharacterDefs(NPCTargets);
                case EConditionFilter.OnlyPlayers:
                    return ToCharacterDefs(PlayerTargets);
                case EConditionFilter.AnyCharacter:
                default:
                    // CharacterTargets 本身是 CharacterDefinition 列表，NPCDefinition/PlayerDefinition 均派生自它，
                    // 因此 AnyCharacter 下三类目标都可以填；这里把非空的额外目标一并纳入，避免配置被忽略。
                    var all = new List<CharacterDefinition>();
                    if (CharacterTargets != null) all.AddRange(CharacterTargets);
                    if (NPCTargets != null) all.AddRange(NPCTargets);
                    if (PlayerTargets != null) all.AddRange(PlayerTargets);
                    return all;
            }
        }

        private static List<CharacterDefinition> ToCharacterDefs<T>(List<T> defs) where T : CharacterDefinition
        {
            var result = new List<CharacterDefinition>();
            if (defs == null) return result;
            foreach (var def in defs)
            {
                if (def != null) result.Add(def);
            }
            return result;
        }

        /// <summary>
        /// 单条条件的完整求值（含队伍策略、NPC 目标解析与 <see cref="bNot"/> 取反）。
        /// 判定本体在纯层 <see cref="NarrativeConditionEvaluation.Evaluate"/> 里，本方法只负责
        /// 把引擎侧事实（队伍组件、队员/队长、角色子系统解析出的 Actor）取好后喂进去 ——
        /// 实时通路 harness 走的因此是同一份实现。
        /// </summary>
        public bool Evaluate(object pawn, object controller, TalesComponent narrativeComponent)
        {
            if (narrativeComponent == null) return false;
            return NarrativeConditionEvaluation.Evaluate(BuildEvalRequest(pawn, controller, narrativeComponent), true);
        }

        /// <summary>
        /// 组装本条件的一次纯求值输入。对应 UE5 NarrativeNodeBase.cpp:117-204 循环体里
        /// 「先拿队伍、再解析角色目标、最后回落 Pawn」的三段准备过程。
        /// </summary>
        public NarrativeConditionEvalRequest BuildEvalRequest(object pawn, object controller, TalesComponent narrativeComponent)
        {
            var request = new NarrativeConditionEvalRequest
            {
                PartyConditionPolicy = PartyConditionPolicy,
                PawnTarget = new NarrativeConditionEvalTarget(pawn, controller, narrativeComponent),
            };

            // 原始判定回调：UE5 的 `CheckCondition(...) == bNot` 就是「是否失败」，
            // 取反只在这里发生一次（MakeFailsFromResult），纯求值层不再取反。
            request.RawFails = NarrativeConditionEvaluation.MakeFailsFromResult(
                target =>
                {
                    if (target == null) return false;
                    return CheckConditionSafe(target.Pawn, target.Controller, target.Component as TalesComponent);
                },
                bNot);

            // ① 队伍策略：非单机且拿到了队伍组件时，按策略在队伍/队员/队长上判定。
            var party = narrativeComponent as NarrativePartyComponent ?? narrativeComponent.GetParty();
            request.bPartyBranchAvailable =
                PartyConditionPolicy != EPartyConditionPolicy.None && party != null && !narrativeComponent.IsStandalone();

            if (request.bPartyBranchAvailable)
            {
                // UE5:137 —— PartyPasses 用 (nullptr, nullptr, Party) 跑条件。
                request.PartyTarget = new NarrativeConditionEvalTarget(null, null, party);

                if (PartyConditionPolicy == EPartyConditionPolicy.PartyLeaderPasses)
                {
                    var leader = party.GetPartyLeader();
                    request.PartyLeaderTarget = leader == null
                        ? null
                        : new NarrativeConditionEvalTarget(leader.GetOwningPawn(), leader.GetOwningController(), leader);
                }
                else if (PartyConditionPolicy == EPartyConditionPolicy.AnyPlayerPasses ||
                         PartyConditionPolicy == EPartyConditionPolicy.AllPlayersPass)
                {
                    // 保留 null 槽位：UE5 的 `if (Member)` 守卫是「跳过」而不是「终止遍历」。
                    var members = party.GetPartyMembers();
                    if (members != null)
                    {
                        request.PartyMembers = new List<NarrativeConditionEvalTarget>(members.Count);
                        foreach (var member in members)
                        {
                            request.PartyMembers.Add(member == null
                                ? null
                                : new NarrativeConditionEvalTarget(member.GetOwningPawn(), member.GetOwningController(), member));
                        }
                    }
                }

                return request;
            }

            // ② 角色目标解析：ConditionFilter 选出的定义若非空，用角色子系统解析成实际 Actor。
            var charTargets = GetCharacterTargets();
            request.bHasCharacterTargets = charTargets.Count > 0;
            if (request.bHasCharacterTargets)
            {
                var subsystem = NarrativeCharacterSubsystem.Instance;
                request.bCharacterSubsystemAvailable = subsystem != null;
                if (subsystem != null)
                {
                    request.ResolvedTargets = new List<NarrativeConditionEvalTarget>(charTargets.Count);
                    foreach (var targetDef in charTargets)
                    {
                        if (targetDef == null) continue;
                        var character = subsystem.FindCharacter(targetDef);
                        if (character == null) continue; // UE5:186 解析不到即跳过该目标
                        request.ResolvedTargets.Add(new NarrativeConditionEvalTarget(character, controller, narrativeComponent));
                    }
                }
            }

            return request;
        }

        /// <summary>
        /// 跑 <see cref="CheckCondition"/>，异常时判为「未通过」（false）。
        /// UE5 的 <c>CheckCondition_Implementation</c> 不会抛，这里保住同一口径；
        /// 「result == bNot」这一次（且唯一一次）取反由
        /// <see cref="NarrativeConditionEvaluation.MakeFailsFromResult"/> 负责，
        /// 本类不再自行比较 bNot —— 避免出现第二份取反实现。
        /// </summary>
        private bool CheckConditionSafe(object target, object controller, TalesComponent comp)
        {
            try
            {
                return CheckCondition(target, controller, comp);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"条件 {GetType().Name} 求值异常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 兼容旧调用点（<c>NarrativeNodeBase</c> / <c>NarrativeEvent.ShouldExecute</c>）的入口，
        /// 语义等同于 <see cref="Evaluate"/>。
        /// </summary>
        public bool IsConditionMet(object target, object controller, object narrativeComponent)
        {
            return Evaluate(target, controller, narrativeComponent as TalesComponent);
        }
    }
}
