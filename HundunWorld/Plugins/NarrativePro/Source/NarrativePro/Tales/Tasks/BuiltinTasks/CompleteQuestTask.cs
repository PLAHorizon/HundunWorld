using System;
using NarrativePro.Core;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等另一条任务线成功」的任务条目。对应 UE5 蓝图任务 <c>BPT_CompleteQuest</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_CompleteQuest.uasset）。
    /// <para>
    /// 二进制证据：图里 <c>CallFunc_GetTalesComponentFromTarget_ReturnValue</c> 取组件后
    /// <c>K2Node_AssignDelegate</c> 绑 <c>OnQuestSucceeded</c>（自定义事件
    /// <c>K2Node_CustomEvent_Quest</c> + <c>CustomEvent_QuestSucceededMessage</c>），
    /// 用 <c>CallFunc_GetObjectClass_ReturnValue</c> + <c>CallFunc_EqualEqual_ClassClass_ReturnValue</c>
    /// 比对「成功的这条 quest 是不是我要等的那个 quest 类」，命中才
    /// <c>CallFunc_Add_IntInt_ReturnValue</c>。变量 <c>QuestClass</c> 即 UE5 的
    /// <c>TSubclassOf&lt;UQuest&gt;</c>；端口 quest 以字符串 id 建模
    /// （<see cref="TalesComponent.GetQuestInstance"/> 按 <c>QuestName</c> 查），故改用 id 比对。
    /// 图注释另提到 subquests/tracked/reached states，属编辑器提示词，不构成调用。
    /// </para>
    /// </summary>
    [Serializable]
    public class CompleteQuestTask : NarrativeTask
    {
        /// <summary>要等待成功的任务 id。对应 UE5 <c>QuestClass</c>（端口用 <c>QuestName</c> 口径）。</summary>
        public string QuestId { get; set; } = "";

        /// <summary>UE5 侧同名字段：等的是「已完成」而非「成功」时也接受（Succeeded 或 Failed 均算）。</summary>
        public bool bAcceptFailedToo { get; set; } = false;

        private TalesComponent _boundComp;

        protected override void OnBeginTask()
        {
            var comp = NarrativeComp;
            if (comp == null)
            {
                NarrativeLog.LogWarning("CompleteQuestTask: 没有 TalesComponent，无法监听任务成功事件");
                return;
            }

            // 回溯判定：目标 quest 在进本分支前就已结束（读档 / 并行分支）时不能死等。
            if (IsAlreadyFinished(comp))
            {
                CompleteTask();
                return;
            }

            _boundComp = comp;
            _boundComp.OnQuestSucceeded += HandleQuestSucceeded;
            if (bAcceptFailedToo)
                _boundComp.OnQuestFailed += HandleQuestFailed;
        }

        private bool IsAlreadyFinished(TalesComponent comp)
        {
            if (string.IsNullOrEmpty(QuestId)) return false;
            return bAcceptFailedToo ? comp.IsQuestFinished(QuestId) : comp.IsQuestSucceeded(QuestId);
        }

        private void HandleQuestSucceeded(TalesComponent source, QuestClass quest)
        {
            if (!bIsActive || source != _boundComp) return;
            if (!Matches(quest)) return;
            AddProgress(1);
        }

        private void HandleQuestFailed(TalesComponent source, QuestClass quest)
        {
            if (!bIsActive || source != _boundComp) return;
            if (!Matches(quest)) return;
            AddProgress(1);
        }

        private bool Matches(QuestClass quest)
        {
            if (quest == null) return false;
            // UE5 比类，端口比 id；QuestId 留空表示「任意 quest 成功都算」（通配写法）。
            return string.IsNullOrEmpty(QuestId) ||
                   string.Equals(quest.GetQuestName(), QuestId, StringComparison.OrdinalIgnoreCase);
        }

        protected override void OnEndTask()
        {
            if (_boundComp != null)
            {
                _boundComp.OnQuestSucceeded -= HandleQuestSucceeded;
                _boundComp.OnQuestFailed -= HandleQuestFailed;
                _boundComp = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(QuestId) ? "等待任一任务完成" : $"完成任务：{QuestId}";
        }
    }
}
