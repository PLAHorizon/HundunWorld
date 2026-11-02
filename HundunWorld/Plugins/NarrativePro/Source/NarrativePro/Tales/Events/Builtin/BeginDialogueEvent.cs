using NarrativePro.Core;
using NarrativePro.Tales.Data;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 开始对话事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_BeginDialogue.uasset 符号表）：
    /// CallFunc 含 <c>BeginDialogue</c>（UTalesComponent::BeginDialogue，TalesComponent.h:565）、
    /// <c>GetParty</c>（NarrativePartyComponent）、<c>IsValidClass</c>/<c>GetClassDisplayName</c>
    /// （拼 GetGraphDisplayText），BP 变量 <c>Dialogue</c>/<c>StartFromID</c>/<c>Priority</c>/
    /// <c>Speakers</c>/<c>ComponentToUse</c>（self / RunOnParty 两选），
    /// 组装 <c>FDialoguePlayParams</c>（Dialogue.h:168-232）后在选定的 TalesComponent 上播放。
    /// </para>
    /// <para>
    /// 端口差异：①<see cref="Speakers"/> 在 UE5 是 Actor 引用数组，端口 JSON 图表只能承载
    /// 字符串标识，暂不透传（说话人仍按对话资产的 NPCDefinition 解析）；
    /// ②NarrativeUpdate 口径：端口 BeginDialogue 不走 FNarrativeUpdate 复制队列
    /// ——对话 chunk 由 TalesComponent.BeginDialogue 内部经 ClientBeginDialogue 下发
    /// （TalesComponent.cs:531-560），事件侧只负责选组件；
    /// ③RunOnParty 时端口用 <c>GetParty()</c>（TalesComponent.cs:125-128），队员列表为空时
    /// 该组件本身就是队伍组件的场景由 party==null 分支兜住。
    /// </para>
    /// </summary>
    public class BeginDialogueEvent : NarrativeEvent
    {
        /// <summary>对话类标识。对应 UE5 <c>Dialogue</c>（TSubclassOf&lt;UDialogue&gt;）。</summary>
        public string DialogueClassId { get; set; } = "";

        /// <summary>起始节点 ID。对应 UE5 <c>StartFromID</c>。</summary>
        public string StartFromID { get; set; } = "";

        /// <summary>播放优先级，-1 = 用对话资产默认。对应 FDialoguePlayParams 的 Priority。</summary>
        public int Priority { get; set; } = -1;

        /// <summary>对应 UE5 <c>ComponentToUse</c>：true 时优先在队伍组件上播对话。</summary>
        public bool bRunOnParty { get; set; } = false;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (string.IsNullOrEmpty(DialogueClassId))
            {
                NarrativeLog.LogWarning("BeginDialogueEvent: DialogueClassId 为空，跳过");
                return;
            }

            var component = narrativeComponent as TalesComponent;
            if (component == null)
            {
                NarrativeLog.LogWarning("BeginDialogueEvent: 上下文没有 TalesComponent，无法开始对话");
                return;
            }

            // 对应 UE5 ComponentToUse=RunOnParty：队伍对话统一由队伍组件持有并分发
            var use = component;
            if (bRunOnParty)
            {
                var party = component.GetParty();
                if (party != null) use = party;
            }

            var playParams = new DialoguePlayParams
            {
                StartFromID = StartFromID ?? "",
                Priority = Priority,
            };

            if (!use.BeginDialogue(DialogueClassId, playParams))
                NarrativeLog.LogWarning($"BeginDialogueEvent: 对话 '{DialogueClassId}' 未能开始（无权限、被更高优先级对话占用或资产无效）");
        }

        public override string GetGraphDisplayText() => $"Begin Dialogue: {DialogueClassId}";
        public override string GetHintText() => "开始对话";
    }
}
