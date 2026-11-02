using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Nodes;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等到对话里的某一节点被说完」的任务条目。对应 UE5 蓝图任务
    /// <c>BPT_PlayDialogueNode</c>（Content/Pro/Core/Tales/Tasks/BPT_PlayDialogueNode.uasset，
    /// 另有同名 <c>PlayDialogueNode.uasset</c> —— 那个是 <c>UNarrativeDataTask</c> 资产，
    /// 父类在二进制里写明 <c>NarrativeArsenal.NarrativeDataTask</c>，不是任务条目）。
    /// <para>
    /// 二进制证据：图取 <c>DialogueNodeSelector</c>（<c>CallFunc_BreakDialogueNodeSelector_NodeID</c>）
    /// 与说话者 NPC（<c>CallFunc_FindNPC_ReturnValue</c> +
    /// <c>CallFunc_GetWorldSubsystem_ReturnValue</c> 走 <c>NarrativeCharacterSubsystem</c>），
    /// 用 <c>K2Node_AssignDelegate</c> 同时绑 <c>OnNPCDialogueLineFinished</c> 与
    /// <c>OnPlayerDialogueLineFinished</c>（<c>NPCDialogueLineFinished__DelegateSignature</c> /
    /// <c>PlayerDialogueLineFinished__DelegateSignature</c>），并实现
    /// <c>GetNavigationMarkerAttachActor</c>（把标记挂到说话者头上）与
    /// <c>CallFunc_GetNumberOfTimesTaskWasCompleted_ReturnValue</c> 的回溯判定。
    /// </para>
    /// <para>
    /// 回溯判定在端口有真实落点：<c>Dialogue.cs:814/:851</c> 播完节点后会
    /// <c>CompleteNarrativeDataTask("PlayDialogueNode", node.GetID())</c>，
    /// 常量见 <c>Dialogue.cs:26</c>（UE5 Dialogue.cpp:47）。
    /// </para>
    /// </summary>
    [Serializable]
    public class PlayDialogueNodeTask : NarrativeTask
    {
        /// <summary>节点 ID。对应 UE5 <c>DialogueNodeSelector.NodeID</c>。</summary>
        public string NodeID { get; set; } = "";

        /// <summary>只认这段对话里的节点；留空表示任意对话中同 ID 节点。对应 <c>Dialogue</c>。</summary>
        public string DialogueId { get; set; } = "";

        /// <summary>说话者 NPC 提供者。对应图里的 <c>SpeakerNPCDefinition</c> / <c>FindNPC</c>。</summary>
        public NarrativePro.Core.NarrativeActorProvider SpeakerProvider { get; set; }

        /// <summary>提供者缺位时按 NPC 定义 ID 找说话者（端口用 <c>FindNPCByID</c>）。</summary>
        public string SpeakerNpcId { get; set; } = "";

        private TalesComponent _boundComp;
        private Actor _cachedSpeaker;

        protected override void OnBeginTask()
        {
            var comp = NarrativeComp;
            if (comp == null)
            {
                NarrativeLog.LogWarning("PlayDialogueNodeTask: 没有 TalesComponent，无法监听对话行事件");
                return;
            }

            if (string.IsNullOrEmpty(NodeID))
            {
                NarrativeLog.LogWarning("PlayDialogueNodeTask: NodeID 为空，任务没有可等待的节点");
                return;
            }

            // 回溯：该节点在本次进分支前就已经播完过（数据任务已被 Dialogue.cs 记进 MasterTaskList）。
            if (comp.HasCompletedTask(DialogueDataTaskName, NodeID))
            {
                CompleteTask();
                return;
            }

            _boundComp = comp;
            _boundComp.OnNPCDialogueLineFinished += HandleNPCLineFinished;
            _boundComp.OnPlayerDialogueLineFinished += HandlePlayerLineFinished;

            _cachedSpeaker = BuiltinTaskSupport.ResolveTargetActor(SpeakerProvider, SpeakerNpcId, Guid.Empty);
        }

        /// <summary>UE5 用数据任务 <c>PlayDialogueNode</c> 记录节点完成，端口常量同源。</summary>
        private const string DialogueDataTaskName = "PlayDialogueNode";

        private void HandleNPCLineFinished(TalesComponent source, DialogueClass dialogue, DialogueNode_NPC node,
                                           Data.DialogueLine line, Data.SpeakerInfo speaker)
        {
            if (!bIsActive || source != _boundComp) return;
            if (!Matches(dialogue, node != null ? node.GetID() : null)) return;
            AddProgress(1);
        }

        private void HandlePlayerLineFinished(TalesComponent source, DialogueClass dialogue, DialogueNode_Player node,
                                              Data.DialogueLine line)
        {
            if (!bIsActive || source != _boundComp) return;
            if (!Matches(dialogue, node != null ? node.GetID() : null)) return;
            AddProgress(1);
        }

        private bool Matches(DialogueClass dialogue, string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || !string.Equals(nodeId, NodeID, StringComparison.OrdinalIgnoreCase))
                return false;

            // UE5 的对话资产是类引用，端口是字符串 id；留空即通配。
            if (string.IsNullOrEmpty(DialogueId) || dialogue == null) return true;
            return string.Equals(dialogue.DialogueId, DialogueId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>把导航标记挂到说话者 NPC 上（对应图内实现的 GetNavigationMarkerAttachActor）。</summary>
        public override Actor GetNavigationMarkerAttachActor()
        {
            if (_cachedSpeaker == null)
                _cachedSpeaker = BuiltinTaskSupport.ResolveTargetActor(SpeakerProvider, SpeakerNpcId, Guid.Empty);
            return _cachedSpeaker;
        }

        protected override void OnEndTask()
        {
            if (_boundComp != null)
            {
                _boundComp.OnNPCDialogueLineFinished -= HandleNPCLineFinished;
                _boundComp.OnPlayerDialogueLineFinished -= HandlePlayerLineFinished;
                _boundComp = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(NodeID) ? "听完对话节点" : $"听完对话节点：{NodeID}";
        }
    }
}
