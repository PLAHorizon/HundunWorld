using System;
using NarrativePro.Core;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「等某条数据任务被完成」的任务条目。对应 UE5 蓝图任务 <c>BPT_CompleteDataTask</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_CompleteDataTask.uasset）。
    /// <para>
    /// 二进制证据：图里 <c>CallFunc_GetTalesComponentFromTarget_ReturnValue</c> 取组件后
    /// <c>K2Node_CreateDelegate</c> 绑 <c>OnNarrativeDataTaskCompleted</c>
    /// （自定义事件入参 <c>CustomEvent_Name</c> / <c>CustomEvent_NarrativeTask</c>），
    /// 命中后走 <c>CallFunc_Add_IntInt_ReturnValue</c> 推进进度；
    /// <c>CallFunc_GetNumberOfTimesTaskWasCompleted_ReturnValue</c> 用于「任务开始前就已经完成过」
    /// 的回溯判定（图注释："retroactive"）；<c>CallFunc_IsEmpty_ReturnValue</c> +
    /// <c>CallFunc_BooleanOR_ReturnValue</c> 表示参数留空即匹配该任务名下的任意参数。
    /// 变量 <c>DataTaskName</c> / <c>Argument</c> 与 <c>UNarrativeDataTask</c> 的
    /// <c>TaskName</c>/<c>ArgumentName</c>（NarrativeDataTask.h）对应。
    /// </para>
    /// </summary>
    [Serializable]
    public class CompleteDataTaskTask : NarrativeTask
    {
        /// <summary>要等待的数据任务名。对应 UE5 <c>DataTaskName</c>。</summary>
        public string DataTaskName { get; set; } = "";

        /// <summary>数据任务参数；留空表示该任务名下的任意参数都算。对应 UE5 <c>Argument</c>。</summary>
        public string Argument { get; set; } = "";

        private TalesComponent _boundComp;

        protected override void OnBeginTask()
        {
            var comp = NarrativeComp;
            if (comp == null)
            {
                NarrativeLog.LogWarning("CompleteDataTaskTask: 没有 TalesComponent，无法监听数据任务完成事件");
                return;
            }

            // 回溯判定（UE5 图里的 GetNumberOfTimesTaskWasCompleted 分支）：
            // 读档或玩家先做完再进分支时，任务不应变成死等的黑洞。
            int already = comp.GetNumberOfTimesTaskWasCompleted(DataTaskName, Argument);
            if (already > 0)
            {
                AddProgress(already);
                if (IsComplete()) return;
            }

            _boundComp = comp;
            _boundComp.OnNarrativeDataTaskCompleted += HandleDataTaskCompleted;
        }

        private void HandleDataTaskCompleted(TalesComponent source, string taskName, string argument, int quantity)
        {
            if (!bIsActive || source != _boundComp) return;
            if (string.IsNullOrEmpty(DataTaskName)) return;
            if (!string.Equals(taskName, DataTaskName, StringComparison.OrdinalIgnoreCase)) return;

            // 参数留空 = 通配（UE5 的 IsEmpty || 相等 判定）
            if (!string.IsNullOrEmpty(Argument) &&
                !string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase)) return;

            AddProgress(quantity);
        }

        protected override void OnEndTask()
        {
            if (_boundComp != null)
            {
                _boundComp.OnNarrativeDataTaskCompleted -= HandleDataTaskCompleted;
                _boundComp = null;
            }
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return NarrativeFunctionLibrary.MakeDisplayString(DataTaskName);
        }
    }
}
