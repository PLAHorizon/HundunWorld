using System;
using NarrativePro.Core;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Nodes;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Async
{
    /// <summary>
    /// 异步操作：开始对话并等待。对应 UE5 UAsyncAction_BeginDialogueAndWait（UBlueprintAsyncActionBase 派生）。
    /// UE5 中作为蓝图异步节点，开始一段对话并广播节点开始/结束与对话结束事件。
    /// 代理签名对应 UE5 FBeginDialogueAndWaitSignature：(FName NodeID, bool bNodeStarted, EExitDialogueReason FinishReason)。
    /// Flax 无 BlueprintAsyncActionBase 对应物，此处改为 [Serializable] 普通类占位 + 事件回调 + 占位实现。
    /// </summary>
    [Serializable]
    public class AsyncAction_BeginDialogueAndWait
    {
        /// <summary>
        /// 拥有此异步操作的 TalesComponent。
        /// UE5 中为 UPROPERTY() TObjectPtr&lt;UTalesComponent&gt; OwningTalesComponent。
        /// </summary>
        public TalesComponent OwningTalesComponent { get; private set; }

        /// <summary>
        /// 对话类路径占位。UE5 中为 TSubclassOf&lt;UDialogue&gt; DialogueClass，
        /// Flax 中无蓝图类，用 string 路径/ID 占位。
        /// </summary>
        public string DialogueClassPath { get; private set; }

        /// <summary>
        /// 对话节点开始/结束事件。对应 UE5 FBeginDialogueAndWaitSignature OnDialogueNode。
        /// 参数：节点ID、是否为节点开始（true=开始，false=结束）、结束原因。
        /// </summary>
        public event Action<string, bool, EExitDialogueReason> OnDialogueNode;

        /// <summary>
        /// 对话结束事件。对应 UE5 FBeginDialogueAndWaitSignature Finished。
        /// 参数：节点ID（结束时常为空）、是否开始新对话、结束原因。
        /// </summary>
        public event Action<string, bool, EExitDialogueReason> Finished;

        private bool _bReadyToDestroy = false;

        /// <summary>
        /// 开始对话并等待。对应 UE5 UAsyncAction_BeginDialogueAndWait::BeginDialogueAndWait。
        /// </summary>
        /// <param name="talesComponent">拥有此操作的 TalesComponent</param>
        /// <param name="dialogueClassPath">对话类路径/ID</param>
        /// <param name="playParams">对话播放参数</param>
        /// <param name="bPersistant">为 true 时操作保持存活直到手动调用 EndTask（可在循环或调用 BP 销毁后使用）</param>
        /// <returns>对话是否成功开始。失败时返回 null。</returns>
        public static AsyncAction_BeginDialogueAndWait BeginDialogueAndWait(
            TalesComponent talesComponent,
            string dialogueClassPath,
            DialoguePlayParams playParams,
            bool bPersistant = false)
        {
            // Flax-不兼容: UE5 的 BlueprintInternalUseOnly/BlueprintAuthorityOnly 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 BlueprintInternalUseOnly/BlueprintAuthorityOnly 概念，权限校验由调用方负责。
            // UE5 中 bPersistant 会调用 RegisterWithGameInstance 让 Action 与 GameInstance 绑定生命周期，
            // Flax 中暂未实现持久化注册，仅作字段保留。
            if (talesComponent == null || string.IsNullOrEmpty(dialogueClassPath))
            {
                return null;
            }

            var action = new AsyncAction_BeginDialogueAndWait();
            action.OwningTalesComponent = talesComponent;
            action.DialogueClassPath = dialogueClassPath;

            // 订阅对话事件，对应 UE5 中 AddDynamic 绑定
            talesComponent.OnNPCDialogueLineStarted += action.OnNPCDialogueLineStartedInternal;
            talesComponent.OnNPCDialogueLineFinished += action.OnNPCDialogueLineFinishedInternal;
            talesComponent.OnPlayerDialogueLineStarted += action.OnPlayerDialogueLineStartedInternal;
            talesComponent.OnPlayerDialogueLineFinished += action.OnPlayerDialogueLineFinishedInternal;
            talesComponent.OnDialogueFinished += action.OnDialogueFinishedInternal;

            bool bDialogueStarted = talesComponent.BeginDialogue(dialogueClassPath, playParams);

            // 对话未开始，清除已绑定事件
            if (!bDialogueStarted)
            {
                action.EndTask();
                return null;
            }

            return action;
        }

        /// <summary>
        /// 结束任务，清除所有绑定事件。对应 UE5 EndTask。
        /// </summary>
        public void EndTask()
        {
            SetReadyToDestroy();
        }

        /// <summary>
        /// 设置为可销毁状态，移除所有绑定事件。对应 UE5 SetReadyToDestroy。
        /// </summary>
        public virtual void SetReadyToDestroy()
        {
            if (_bReadyToDestroy) return;
            _bReadyToDestroy = true;

            if (OwningTalesComponent != null)
            {
                OwningTalesComponent.OnNPCDialogueLineStarted -= OnNPCDialogueLineStartedInternal;
                OwningTalesComponent.OnNPCDialogueLineFinished -= OnNPCDialogueLineFinishedInternal;
                OwningTalesComponent.OnPlayerDialogueLineStarted -= OnPlayerDialogueLineStartedInternal;
                OwningTalesComponent.OnPlayerDialogueLineFinished -= OnPlayerDialogueLineFinishedInternal;
                OwningTalesComponent.OnDialogueFinished -= OnDialogueFinishedInternal;
            }
        }

        private void OnNPCDialogueLineStartedInternal(TalesComponent comp, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            if (node == null || !IsOurDialogue(dialogue)) return;
            OnDialogueNode?.Invoke(node.ID, true, EExitDialogueReason.NoLines);
        }

        private void OnNPCDialogueLineFinishedInternal(TalesComponent comp, DialogueClass dialogue, DialogueNode_NPC node, DialogueLine line, SpeakerInfo speaker)
        {
            if (node == null || !IsOurDialogue(dialogue)) return;
            OnDialogueNode?.Invoke(node.ID, false, EExitDialogueReason.NoLines);
        }

        private void OnPlayerDialogueLineStartedInternal(TalesComponent comp, DialogueClass dialogue, DialogueNode_Player node, DialogueLine line)
        {
            if (node == null || !IsOurDialogue(dialogue)) return;
            OnDialogueNode?.Invoke(node.ID, true, EExitDialogueReason.NoLines);
        }

        private void OnPlayerDialogueLineFinishedInternal(TalesComponent comp, DialogueClass dialogue, DialogueNode_Player node, DialogueLine line)
        {
            if (node == null || !IsOurDialogue(dialogue)) return;
            OnDialogueNode?.Invoke(node.ID, false, EExitDialogueReason.NoLines);
        }

        private void OnDialogueFinishedInternal(TalesComponent comp, DialogueClass dialogue, EExitDialogueReason reason)
        {
            if (!IsOurDialogue(dialogue)) return;
            // UE5 中广播 NAME_None 作为节点ID
            Finished?.Invoke("", false, reason);
            // 对话结束，事件不再需要
            EndTask();
        }

        /// <summary>
        /// 判断给定的对话是否是此异步操作所关注的对话。
        /// UE5 中通过 GetClass() == DialogueClass 比较；Flax 中通过 DialogueId 与 DialogueClassPath 比较。
        /// </summary>
        private bool IsOurDialogue(DialogueClass dialogue)
        {
            if (dialogue == null) return false;
            return string.Equals(dialogue.DialogueId, DialogueClassPath, StringComparison.Ordinal);
        }
    }
}
