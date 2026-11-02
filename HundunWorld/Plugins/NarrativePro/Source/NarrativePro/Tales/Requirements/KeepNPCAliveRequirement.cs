using NarrativePro.Tales.Quest;
using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Requirements
{
    /// <summary>
    /// 「别让这名 NPC 死掉」的任务要求：目标一死就让任务失败。
    /// 对应 UE5 蓝图要求 <c>BPR_KeepNPCAlive</c>
    /// （Content/Pro/Core/Tales/Requirements/BPR_KeepNPCAlive.uasset）。
    /// <para>
    /// 二进制证据：<c>CallFunc_GetWorldSubsystem_ReturnValue</c> →
    /// <c>CallFunc_FindNPC_ReturnValue</c>（<c>bOutSucceeded</c>）按
    /// <c>NPCDefinition</c>/<c>NPCName</c> 找到目标，再
    /// <c>CallFunc_GetNarrativeAbilitySystemComponent_ReturnValue</c> 取其 ASC，
    /// <c>K2Node_AssignDelegate</c> 绑 <c>OnDeathStateChanged</c>
    /// （<c>OnDeathStateChanged__DelegateSignature</c>，自定义事件入参
    /// <c>KilledActor</c> / <c>KilledActorASC</c> / <c>bIsDead</c>），死亡即
    /// <c>FailQuest</c> 并带 <c>QuestFailedMessage</c>；另有
    /// <c>CheckTimerHandle</c> + <c>K2_SetTimerDelegate</c> 的周期补绑，
    /// 以及 <c>CallFunc_GetCharacterName_ReturnValue</c> 用于文案。
    /// </para>
    /// </summary>
    [Serializable]
    public class KeepNPCAliveRequirement : TickedQuestRequirement
    {
        /// <summary>要保活的 NPC 的 Actor 提供者（UE5 图用 FindNPC，端口两种都支持）。</summary>
        public NarrativePro.Core.NarrativeActorProvider NpcProvider { get; set; }

        /// <summary>按 NPC 定义 ID 点名。对应图里的 <c>NPCName</c>。</summary>
        public string NpcId { get; set; } = "";

        /// <summary>目标死亡时的任务失败文案。对应 <c>QuestFailedMessage</c>。</summary>
        public string QuestFailedMessage { get; set; } = "";

        /// <summary>补绑间隔秒。对应图里的 <c>CheckTimerHandle</c> 周期。</summary>
        public float CheckIntervalSeconds { get; set; } = 1f;

        private NarrativeAbilitySystemComponent _targetAsc;
        private Actor _targetActor;

        public override void OnAdded(NarrativePro.Tales.Quest.Quest quest)
        {
            CheckInterval = CheckIntervalSeconds > 0f ? CheckIntervalSeconds : 1f;
            base.OnAdded(quest);
            TryBind();
        }

        public override void OnRemoved(NarrativePro.Tales.Quest.Quest quest)
        {
            Unbind();
            base.OnRemoved(quest);
        }

        private void TryBind()
        {
            if (_targetAsc != null) return;

            _targetActor = NarrativePro.Tales.Tasks.BuiltinTaskSupport.ResolveTargetActor(NpcProvider, NpcId, Guid.Empty);
            if (_targetActor == null) return;

            _targetAsc = ArsenalStatics.GetNarrativeAbilitySystemComponent(_targetActor);
            if (_targetAsc == null)
            {
                NarrativeLog.LogWarning($"KeepNPCAliveRequirement: 目标 '{_targetActor.Name}' 没有 ASC，无法保活判定");
                return;
            }

            // 补绑前目标已经死了：UE5 的计时器首次触发同样会立刻判失败。
            if (_targetAsc.IsDead)
            {
                Fail(ResolveName());
                return;
            }

            _targetAsc.OnDeathStateChanged += HandleDeathStateChanged;
        }

        private void Unbind()
        {
            if (_targetAsc != null)
                _targetAsc.OnDeathStateChanged -= HandleDeathStateChanged;
            _targetAsc = null;
            _targetActor = null;
        }

        private void HandleDeathStateChanged(Actor actor, NarrativeAbilitySystemComponent asc, bool bIsDead)
        {
            if (!bIsDead || asc != _targetAsc) return;
            Unbind();
            Fail(!string.IsNullOrEmpty(actor?.Name) ? actor.Name : ResolveName());
        }

        private string ResolveName()
        {
            var character = _targetActor != null ? _targetActor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>() : null;
            string name = character != null ? character.GetCharacterName() : null;
            return !string.IsNullOrEmpty(name) ? name : NpcId;
        }

        private void Fail(string npcName)
        {
            string message = !string.IsNullOrEmpty(QuestFailedMessage)
                ? QuestFailedMessage
                : $"{npcName} 死了";
            OwningQuest?.FailQuest(message);
        }

        public override void TickRequirement(float deltaTime)
        {
            // 目标可能是流式加载/稍后才生成的：UE5 用计时器补绑，这里同一职责。
            if (_targetAsc == null) TryBind();
        }
    }
}
