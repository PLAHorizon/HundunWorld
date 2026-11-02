using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;
using NarrativePro.Vehicles;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 阵营增删写入辅助。集中处理「Actor → 可写阵营容器」的端口解析顺序，
    /// 供 <see cref="AddFactionsEvent"/> 与 <see cref="RemoveFactionsEvent"/> 共用。
    /// <para>
    /// UE5 依据（ArsenalStatics.cpp:233-268）：AddFactionsToActor/RemoveFactionsFromActor
    /// 都是 <c>Cast&lt;INarrativeTeamAgentInterface&gt;(Actor)</c> 后逐标签调
    /// FactionTeam-&gt;AddFaction/RemoveFaction，即「谁能持有阵营就写在谁身上」。
    /// 端口里阵营不是接口而是分散在三类具体脚本上
    /// （UnrealFramework/NarrativeNPCCharacter.cs:399-415 的 NPCFactions、
    /// NarrativePlayerController.cs:351-362 委托 PlayerState、NarrativePlayerState.cs:94-108，
    /// 另加 Vehicles/NarrativeVehicleBase.cs 走 INarrativeTeamAgentInterface），
    /// 因此按同样口径逐个试探。
    /// </para>
    /// </summary>
    internal static class FactionWriter
    {
        public static bool ApplyTo(Actor actor, GameplayTag faction, bool bAdd)
        {
            if (actor == null || !faction.IsValid()) return false;

            var npc = actor.GetScript<NarrativeNPCCharacter>();
            if (npc != null)
            {
                if (bAdd) npc.AddFaction(faction); else npc.RemoveFaction(faction);
                return true;
            }

            var controller = actor.GetScript<NarrativePlayerController>();
            if (controller != null)
            {
                if (bAdd) controller.AddFaction(faction); else controller.RemoveFaction(faction);
                return true;
            }

            var state = actor.GetScript<NarrativePlayerState>();
            if (state != null)
            {
                if (bAdd) state.AddFaction(faction); else state.RemoveFaction(faction);
                return true;
            }

            var vehicle = actor.GetScript<NarrativeVehicleBase>();
            if (vehicle != null)
            {
                if (bAdd) vehicle.AddFaction(faction); else vehicle.RemoveFaction(faction);
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 为 Actor 添加阵营事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_AddFactions.uasset 符号表）：
    /// CallFunc 序列含 <c>ArsenalStatics.AddFactionsToActor</c>、
    /// <c>GetDebugStringFromGameplayTagContainer</c>（纯文本类变量 <c>FactionsToAdd</c>），
    /// 即把一个 FGameplayTagContainer 里的阵营逐个 AddFaction 进目标的阵营容器；
    /// C++ 实现见 ArsenalStatics.cpp:233-248。
    /// </para>
    /// <para>
    /// 端口差异：UE5 走 ITeamAgentInterface 统一 Cast，端口无统一接口，
    /// 由 <see cref="FactionWriter"/> 按 NPC→控制器→PlayerState→载具的具体脚本顺序写入
    /// （与 Core/ArsenalStatics.cs:103-105 注释里「阵营数据源分散」的现状一致）。
    /// </para>
    /// </summary>
    public class AddFactionsEvent : NarrativeEvent
    {
        /// <summary>要添加的阵营标签名集合。对应 UE5 <c>FactionsToAdd</c>（GameplayTagContainer）。</summary>
        public List<string> Factions { get; set; } = new List<string>();

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            if (Factions == null || Factions.Count == 0)
            {
                NarrativeLog.LogWarning("AddFactionsEvent: Factions 为空，跳过");
                return;
            }

            var targetActor = target as Actor;
            var controllerActor = controller as Actor;
            var added = new List<string>();

            foreach (var name in Factions)
            {
                if (string.IsNullOrEmpty(name)) continue;
                var tag = new GameplayTag(name);
                if (!tag.IsValid())
                {
                    NarrativeLog.LogWarning($"AddFactionsEvent: 阵营标签 '{name}' 无效，跳过");
                    continue;
                }

                // UE5 AddFactionsToActor 只作用一个 Actor；端口事件上下文里 NPC 走 target、
                // 玩家走 controller（宿主装配模式下阵营挂在控制器/PlayerState 一侧），两者都试。
                if (FactionWriter.ApplyTo(targetActor, tag, true) || FactionWriter.ApplyTo(controllerActor, tag, true))
                    added.Add(name);
                else
                    NarrativeLog.LogWarning($"AddFactionsEvent: 目标不是阵营持有者，无法添加阵营 '{name}'");
            }

            // 对应 UE5 GetDebugStringFromGameplayTagContainer 的调试串口径
            if (added.Count > 0)
                NarrativeLog.Log($"AddFactionsEvent: 已添加阵营 [{string.Join(", ", added)}]");
        }

        public override string GetGraphDisplayText() => $"Add Factions: {string.Join(", ", Factions ?? new List<string>())}";
    }
}
