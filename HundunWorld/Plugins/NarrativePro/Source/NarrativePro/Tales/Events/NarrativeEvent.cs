using System;
using System.Collections.Generic;
using NarrativePro.AI;
using NarrativePro.Character;
using NarrativePro.Core;

namespace NarrativePro.Tales.Events
{
    /// <summary>
    /// 叙事事件基类。对应 UE5 <c>UNarrativeEvent</c>（NarrativeEvent.h/cpp）。
    /// </summary>
    public abstract class NarrativeEvent
    {
        /// <summary>
        /// 读档时是否重新触发本事件。对应 UE5 <c>bRefireOnLoad</c>。
        /// UE5 构造函数（NarrativeEvent.cpp:11）默认为 true。
        /// <para>
        /// 语义见 NarrativeNodeBase.cpp:49：<c>(!bIsLoading || bRefireOnLoad)</c>。
        /// 像「给 500 经验」这种一次性奖励应设为 false，避免每次读档重复发放。
        /// </para>
        /// </summary>
        public bool bRefireOnLoad { get; set; } = true;

        /// <summary>事件在节点进入还是退出时执行。对应 UE5 <c>EventRuntime</c>。</summary>
        public EEventRuntime EventRuntime { get; set; } = EEventRuntime.Start;

        /// <summary>事件允许跑在什么角色上。对应 UE5 <c>EventFilter</c>，默认 EF_Anyone。</summary>
        public EEventFilter EventFilter { get; set; } = EEventFilter.Anyone;

        /// <summary>
        /// 队伍下事件的执行策略。对应 UE5 <c>PartyEventPolicy</c>，UE5 构造函数默认 <c>Party</c>。
        /// </summary>
        public EPartyEventPolicy PartyEventPolicy { get; set; } = EPartyEventPolicy.Party;

        /// <summary>本事件自身的条件，全部满足才执行。对应 UE5 <c>Conditions</c>。</summary>
        public List<NarrativeCondition> Conditions { get; set; } = new List<NarrativeCondition>();

        /// <summary>需要跑本事件的角色定义。对应 UE5 <c>CharacterTargets</c>。</summary>
        public List<CharacterDefinition> CharacterTargets { get; set; } = new List<CharacterDefinition>();

        /// <summary>需要跑本事件的 NPC 定义。对应 UE5 <c>NPCTargets</c>。</summary>
        public List<NPCDefinition> NPCTargets { get; set; } = new List<NPCDefinition>();

        /// <summary>需要跑本事件的玩家定义。对应 UE5 <c>PlayerTargets</c>。</summary>
        public List<PlayerDefinition> PlayerTargets { get; set; } = new List<PlayerDefinition>();

        /// <summary>执行事件。对应 UE5 <c>ExecuteEvent_Implementation</c>。</summary>
        public abstract void ExecuteEvent(object target, object controller, object narrativeComponent);

        /// <summary>对应 UE5 <c>OnActivate_Implementation</c>：默认直接转调 ExecuteEvent。</summary>
        public virtual void OnActivate(object target, object controller, object narrativeComponent)
        {
            ExecuteEvent(target, controller, narrativeComponent);
        }

        /// <summary>对应 UE5 <c>OnDeactivate_Implementation</c>：默认空实现。</summary>
        public virtual void OnDeactivate(object target, object controller, object narrativeComponent)
        {
        }

        /// <summary>图中显示文本。对应 UE5 <c>GetGraphDisplayText_Implementation</c>。</summary>
        public virtual string GetGraphDisplayText()
        {
            return GetType().Name;
        }

        /// <summary>对话选项后展示的提示文本（「开始任务」等）。对应 UE5 <c>GetHintText_Implementation</c>。</summary>
        public virtual string GetHintText()
        {
            return "";
        }

        /// <summary>
        /// 按 UE5 <c>EEventFilter</c> 选出要跑事件的角色定义列表（NarrativeEvent.cpp:42-67）。
        /// 与条件的区别：UE5 里 EF_Anyone 只返回 <c>CharacterTargets</c>，这里保持一致。
        /// </summary>
        public List<CharacterDefinition> GetCharacterTargets()
        {
            switch (EventFilter)
            {
                case EEventFilter.OnlyNPCs:
                    return ToCharacterDefs(NPCTargets);
                case EEventFilter.OnlyPlayers:
                    return ToCharacterDefs(PlayerTargets);
                case EEventFilter.Anyone:
                default:
                    return CharacterTargets != null ? new List<CharacterDefinition>(CharacterTargets) : new List<CharacterDefinition>();
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

        /// <summary>本事件的条件是否全部满足。</summary>
        public bool ShouldExecute(object target, object controller, object narrativeComponent)
        {
            if (Conditions == null || Conditions.Count == 0) return true;
            return Conditions.TrueForAll(c => c != null && c.Evaluate(target, controller, narrativeComponent as TalesComponent));
        }
    }
}
