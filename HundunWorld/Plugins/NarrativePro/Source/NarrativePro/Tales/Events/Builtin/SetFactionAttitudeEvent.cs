using System;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 设置一对阵营的态度事件。
    /// <para>
    /// UE5 依据（Content/Pro/Core/Tales/Events/NE_SetFactionAttitude.uasset 符号表）：
    /// CallFunc 含 <c>GetGameState</c>（GameplayStatics）与 <c>GetEnumeratorUserFriendlyName</c>
    /// （把 ETeamAttitude 枚举转显示名进 Format 调试串），符号含
    /// <c>SetFactionAttitude</c>、<c>SourceFaction</c>、<c>TargetFaction</c>、<c>NewAttitude</c>、
    /// <c>ETeamAttitude</c>，即先 GetGameState 再调
    /// <c>ANarrativeGameState::SetFactionAttitude(Source, Target, NewAttitude)</c>
    /// 写 FactionAllianceMap（NarrativeGameState.h:297/312）。
    /// </para>
    /// <para>
    /// 端口差异：Attitude 用字符串而非枚举 —— Tales/NarrativeLogicFactory.cs 的兜底反序列化
    /// 未注册 JsonStringEnumConverter，枚举属性只能按数字绑定，字符串 + <c>Enum.TryParse</c>
    /// 才是 JSON 图表可写的形态；写表落到 UnrealFramework/NarrativeGameState.cs:384-397 的
    /// <c>SetFactionAttitude</c>（已带 OnFactionAttitudeChanged 广播）。
    /// </para>
    /// </summary>
    public class SetFactionAttitudeEvent : NarrativeEvent
    {
        /// <summary>源阵营标签名。对应 UE5 <c>SourceFaction</c>。</summary>
        public string SourceFaction { get; set; } = "";

        /// <summary>目标阵营标签名。对应 UE5 <c>TargetFaction</c>。</summary>
        public string TargetFaction { get; set; } = "";

        /// <summary>新态度："Friendly" / "Neutral" / "Hostile"。对应 UE5 <c>NewAttitude</c>（ETeamAttitude）。</summary>
        public string Attitude { get; set; } = "Neutral";

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var source = new GameplayTag(SourceFaction);
            var targetFaction = new GameplayTag(TargetFaction);
            if (!source.IsValid() || !targetFaction.IsValid())
            {
                NarrativeLog.LogWarning($"SetFactionAttitudeEvent: 阵营标签无效（'{SourceFaction}' / '{TargetFaction}'），跳过");
                return;
            }

            // 对应 UE5 Cast<ANarrativeGameState> 失败的静默路径：端口记告警而不是无声吞掉
            var gameState = NarrativeGameState.GetCurrent();
            if (gameState == null)
            {
                NarrativeLog.LogWarning("SetFactionAttitudeEvent: 找不到 NarrativeGameState，态度未写入");
                return;
            }

            if (!Enum.TryParse<ArsenalStatics.ETeamAttitude>(Attitude, true, out var newAttitude))
            {
                NarrativeLog.LogWarning($"SetFactionAttitudeEvent: 无法识别的态度 '{Attitude}'（应为 Friendly/Neutral/Hostile），跳过");
                return;
            }

            gameState.SetFactionAttitude(source, targetFaction, newAttitude);
            NarrativeLog.Log($"SetFactionAttitudeEvent: {SourceFaction} → {TargetFaction} = {newAttitude}");
        }

        public override string GetGraphDisplayText() => $"Set Faction Attitude: {SourceFaction} → {TargetFaction} = {Attitude}";
    }
}
