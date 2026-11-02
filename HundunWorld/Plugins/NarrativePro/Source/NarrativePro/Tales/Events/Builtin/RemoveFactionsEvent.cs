using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 从 Actor 移除阵营事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_RemoveFactions.uasset 符号表）：
    /// 符号含 <c>RemoveFactionsFromActor</c> 与 <c>Default__ArsenalStatics</c>，
    /// 且保留了与 NE_AddFactions 相同的纯文本变量名 <c>FactionsToAdd</c>（上游复制粘贴痕迹），
    /// C++ 实现见 ArsenalStatics.cpp:250-268：Cast 到 INarrativeTeamAgentInterface 后逐标签 RemoveFaction。
    /// </para>
    /// <para>
    /// 端口差异：写入路径与 <see cref="AddFactionsEvent"/> 共用 <see cref="FactionWriter"/>；
    /// UE5 资产里变量名仍叫 FactionsToAdd，端口统一改名 Factions 以免语义误导。
    /// </para>
    /// </summary>
    public class RemoveFactionsEvent : NarrativeEvent
    {
        /// <summary>要移除的阵营标签名集合。对应 UE5 资产变量 <c>FactionsToAdd</c>（实际是待移除容器）。</summary>
        public List<string> Factions { get; set; } = new List<string>();

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (Factions == null || Factions.Count == 0)
            {
                NarrativeLog.LogWarning("RemoveFactionsEvent: Factions 为空，跳过");
                return;
            }

            var targetActor = target as Actor;
            var controllerActor = controller as Actor;
            var removed = new List<string>();

            foreach (var name in Factions)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var tag = new GameplayTag(name);
                if (!tag.IsValid())
                {
                    NarrativeLog.LogWarning($"RemoveFactionsEvent: 阵营标签 '{name}' 无效，跳过");
                    continue;
                }

                if (FactionWriter.ApplyTo(targetActor, tag, false) || FactionWriter.ApplyTo(controllerActor, tag, false))
                    removed.Add(name);
                else
                    NarrativeLog.LogWarning($"RemoveFactionsEvent: 目标不是阵营持有者，无法移除阵营 '{name}'");
            }

            if (removed.Count > 0)
                NarrativeLog.Log($"RemoveFactionsEvent: 已移除阵营 [{string.Join(", ", removed)}]");
        }

        public override string GetGraphDisplayText() => $"Remove Factions: {string.Join(", ", Factions ?? new List<string>())}";
    }
}
