using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 批量移除 gameplay 标签事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_RemoveGameplayTags.uasset 符号表）：
    /// CallFunc 含 <c>RemoveLooseGameplayTags</c>（AbilitySystemBlueprintLibrary）、
    /// <c>GetDebugStringFromGameplayTagContainer</c>，BP 变量 <c>TagsToRemove</c>
    /// （FGameplayTagContainer）——对目标 ASC 的 LooseTagSet 做容器级批量移除。
    /// </para>
    /// <para>
    /// 端口差异：上游 NE_AddGameplayTags 在端口只落了单标签版 <see cref="SetTagEvent"/>
    /// （bAdd=true 分支），本事件补齐容器版移除；写入/移除都走
    /// GAS/NarrativeAbilitySystemComponent.cs 的 <c>GameplayTags</c> 动态容器
    /// （与 SetTagEvent 同一解析顺序：先 target 后 controller）。
    /// </para>
    /// </summary>
    public class RemoveGameplayTagsEvent : NarrativeEvent
    {
        /// <summary>要移除的标签名集合。对应 UE5 <c>TagsToRemove</c>。</summary>
        public List<string> Tags { get; set; } = new List<string>();

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (Tags == null || Tags.Count == 0)
            {
                NarrativeLog.LogWarning("RemoveGameplayTagsEvent: Tags 为空，跳过");
                return;
            }

            var asc = ArsenalStatics.GetNarrativeAbilitySystemComponent(target as Actor)
                      ?? ArsenalStatics.GetNarrativeAbilitySystemComponent(controller as Actor);
            if (asc == null)
            {
                NarrativeLog.LogWarning("RemoveGameplayTagsEvent: 目标没有 ASC，无法移除标签");
                return;
            }

            var removed = new List<string>();
            foreach (var name in Tags)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var tag = new GameplayTag(name);
                if (!tag.IsValid())
                {
                    NarrativeLog.LogWarning($"RemoveGameplayTagsEvent: 标签 '{name}' 无效，跳过");
                    continue;
                }

                asc.GameplayTags.RemoveTag(tag);
                removed.Add(name);
            }

            // 对应 UE5 GetDebugStringFromGameplayTagContainer 的调试输出
            if (removed.Count > 0)
                NarrativeLog.Log($"RemoveGameplayTagsEvent: 已移除标签 [{string.Join(", ", removed)}]");
        }

        public override string GetGraphDisplayText() => $"Remove Gameplay Tags: {string.Join(", ", Tags ?? new List<string>())}";
    }
}
