using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 增删 gameplay 标签事件。对应 UE5 <c>UNarrativeEvent_SetTag</c>（动态标签容器）。
    /// </summary>
    public class SetTagEvent : NarrativeEvent
    {
        /// <summary>要写入的标签。</summary>
        public string Tag { get; set; } = "";

        /// <summary>true 为添加，false 为移除。</summary>
        public bool bAdd { get; set; } = true;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var tag = new GameplayTag(Tag);
            if (!tag.IsValid())
            {
                NarrativeLog.LogWarning("SetTagEvent: Tag 为空，跳过");
                return;
            }

            var asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(target as Actor)
                      ?? ArsenalStatics.GetNarrativeAbilitySystemComponent(controller as Actor);
            if (asc == null)
            {
                NarrativeLog.LogWarning($"SetTagEvent: 目标没有 ASC，无法{(bAdd ? "添加" : "移除")}标签 '{Tag}'");
                return;
            }

            if (bAdd)
                asc.GameplayTags.AddTag(tag);
            else
                asc.GameplayTags.RemoveTag(tag);
        }

        public override string GetGraphDisplayText() => $"{(bAdd ? "Add" : "Remove")} Tag: {Tag}";
    }
}
