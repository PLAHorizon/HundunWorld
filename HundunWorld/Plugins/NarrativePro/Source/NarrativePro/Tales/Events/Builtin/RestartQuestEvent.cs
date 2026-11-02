using NarrativePro.Core;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 重启任务事件。清除既有任务实例后从头重新开始。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_RestartQuest.uasset 符号表）：
    /// CallFunc 含 <c>RestartQuest</c>、<c>GetClassDisplayName</c>、
    /// <c>NotEqual_NameName</c>（StartFromID 是否指定），BP 变量 <c>QuestClass</c>/<c>StartFromID</c>——
    /// 即 <c>UTalesComponent::RestartQuest(QuestClass, StartFromID)</c>
    /// （TalesComponent.cpp:258-283：任务不在 QuestList 里时返回 false，不做兜底新开）。
    /// </para>
    /// <para>
    /// NarrativeUpdate 口径：端口 TalesComponent.RestartQuest 内部已在网络化权威端
    /// 自行发 <c>NarrativeUpdate.RestartQuest</c> 并复用 BeginQuest 的更新链
    /// （TalesComponent.cs:206-232），事件层不得重复发送——与 <see cref="StartQuestEvent"/> 同一约定。
    /// </para>
    /// </summary>
    public class RestartQuestEvent : NarrativeEvent
    {
        /// <summary>任务类标识。对应 UE5 <c>QuestClass</c>。</summary>
        public string QuestClassId { get; set; } = "";

        /// <summary>可选的起始状态 id。对应 UE5 <c>StartFromID</c>（NAME_None 即默认起点）。</summary>
        public string StartFromId { get; set; } = "";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(QuestClassId))
            {
                NarrativeLog.LogWarning("RestartQuestEvent: QuestClassId 为空，跳过");
                return;
            }

            var component = narrativeComponent as TalesComponent;
            if (component == null)
            {
                NarrativeLog.LogWarning("RestartQuestEvent: 上下文没有 TalesComponent，无法重启任务");
                return;
            }

            // UE5 RestartQuest 只对已在清单里的任务生效；未开始过的任务应走 StartQuestEvent
            if (!component.RestartQuest(QuestClassId, StartFromId ?? ""))
                NarrativeLog.LogWarning($"RestartQuestEvent: 任务 '{QuestClassId}' 不在当前任务清单，未重启（如需新开请用 StartQuestEvent）");
        }

        public override string GetGraphDisplayText() => $"Restart Quest: {QuestClassId}";
        public override string GetHintText() => "重新开始任务";
    }
}
