using System;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「把某段对话演到结束」的任务条目。对应 UE5 蓝图任务 <c>BPT_FinishDialogue</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_FinishDialogue.uasset）。
    /// <para>
    /// 二进制证据：图里 <c>CallFunc_BeginDialogue_ReturnValue</c>（配
    /// <c>K2Node_MakeStruct_DialoguePlayParams</c> 的 <c>PlayParams_StartFromID</c> /
    /// <c>PlayParams_Priority</c> / <c>PlayParams_Speakers</c> / <c>PlayParams_bFreeMovement</c> /
    /// <c>bStopMovement</c> / <c>bUnskippable</c> 引脚）+ <c>K2Node_AssignDelegate</c> 绑
    /// <c>OnDialogueFinished</c>，自定义事件入参含
    /// <c>K2Node_CustomEvent_bStartingNewDialogue</c> 与 <c>CustomEvent_Reason</c>
    /// （<c>EExitDialogueReason</c>）—— 即「已有对话在演」与「本任务新开对话」两条分支共用一个
    /// 结束等待。命中后 <c>CallFunc_Add_IntInt_ReturnValue</c> 推进进度。
    /// </para>
    /// <para>
    /// 端口差异：UE5 的 <c>FDialoguePlayParams.Speakers</c> 是
    /// <c>TArray&lt;UNarrativeNPCCharacter*&gt;</c>，端口的
    /// <see cref="DialoguePlayParams.Speakers"/> 需要 <c>NarrativeNPCCharacter</c> 实例，
    /// 而资产侧只写得到 NPC id 字符串，故非唯一 NPC 的演出仍靠对话资产的
    /// <c>Speakers</c>/<c>NPCDataAsset</c> 解析，本任务只透传 StartFromID/Priority/移动与跳过开关。
    /// </para>
    /// </summary>
    [Serializable]
    public class FinishDialogueTask : NarrativeTask
    {
        /// <summary>要演完的对话 id。对应 UE5 的 <c>Dialogue</c> 资产引用。</summary>
        public string DialogueId { get; set; } = "";

        /// <summary>起始节点 ID。对应 <c>PlayParams.StartFromID</c>。</summary>
        public string StartFromID { get; set; } = "";

        /// <summary>对话优先级。对应 <c>PlayParams.Priority</c>。</summary>
        public int Priority { get; set; } = -1;

        public bool bOverrideFreeMovement { get; set; } = false;
        public bool bFreeMovement { get; set; } = true;
        public bool bOverrideStopMovement { get; set; } = false;
        public bool bStopMovement { get; set; } = false;
        public bool bOverrideUnskippable { get; set; } = false;
        public bool bUnskippable { get; set; } = false;

        /// <summary>
        /// 对话起不来（冷却/优先级/缺资产）时是否直接按完成处理。
        /// 端口差异：UE5 会让任务永远等下去，本端口默认选择「不卡死状态机」，
        /// 并留下 <see cref="NarrativeLog"/> 告警可查。
        /// </summary>
        public bool bCompleteIfDialogueCannotStart { get; set; } = true;

        private TalesComponent _boundComp;
        private DialogueClass _watchedDialogue;
        private bool _bStartingNewDialogue;

        protected override void OnBeginTask()
        {
            var comp = NarrativeComp;
            if (comp == null)
            {
                NarrativeLog.LogWarning("FinishDialogueTask: 没有 TalesComponent，无法演出对话");
                if (bCompleteIfDialogueCannotStart) CompleteTask();
                return;
            }

            _boundComp = comp;
            _boundComp.OnDialogueFinished += HandleDialogueFinished;

            if (comp.InDialogue())
            {
                // UE5 的 bStartingNewDialogue=false 分支：接管正在演的对话，等它结束。
                _bStartingNewDialogue = false;
                _watchedDialogue = comp.CurrentDialogue;
                if (_watchedDialogue == null)
                    NarrativeLog.LogWarning("FinishDialogueTask: 已在对话中但取不到当前对话实例，只能等结束广播");
                return;
            }

            _bStartingNewDialogue = true;
            if (!comp.BeginDialogue(DialogueId, BuildPlayParams()))
            {
                NarrativeLog.LogWarning($"FinishDialogueTask: BeginDialogue('{DialogueId}') 失败");
                if (bCompleteIfDialogueCannotStart)
                {
                    CompleteTask();
                    return;
                }
                return;
            }

            _watchedDialogue = comp.CurrentDialogue;
        }

        private DialoguePlayParams BuildPlayParams()
        {
            return new DialoguePlayParams
            {
                StartFromID = StartFromID ?? "",
                Priority = Priority,
                bOverrideFreeMovement = bOverrideFreeMovement,
                bFreeMovement = bFreeMovement,
                bOverrideStopMovement = bOverrideStopMovement,
                bStopMovement = bStopMovement,
                bOverrideUnskippable = bOverrideUnskippable,
                bUnskippable = bUnskippable,
            };
        }

        private void HandleDialogueFinished(TalesComponent source, DialogueClass dialogue, EExitDialogueReason reason)
        {
            if (!bIsActive || source != _boundComp) return;

            // 新开的对话：只认它自己的结束（被更高优先级打断时 reason 为 NewDialogueStarted）。
            if (_bStartingNewDialogue && _watchedDialogue != null && dialogue != null && dialogue != _watchedDialogue)
                return;

            AddProgress(1);
        }

        protected override void OnEndTask()
        {
            if (_boundComp != null)
            {
                _boundComp.OnDialogueFinished -= HandleDialogueFinished;
                _boundComp = null;
            }
            _watchedDialogue = null;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(DialogueId) ? "听完当前对话" : $"听完对话：{DialogueId}";
        }
    }
}
