using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.Tales.Events.Builtin
{
    /// <summary>
    /// 传送事件。把角色传送到指定地点。
    /// <para>
    /// 目标解析顺序：<see cref="TargetLocationName"/> 作为 POI 标签（或 POIActor 标签）解析 →
    /// 解析失败时回退到显式坐标 <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/>。
    /// 落点会经 <see cref="ArsenalStatics.FindTeleportSpot"/> 求解，避免传送到几何体内部或不可行走区域。
    /// </para>
    /// </summary>
    public class TeleportEvent : NarrativeEvent
    {
        /// <summary>目标地点名（POI 标签或 POIActor 标签）；为空时使用显式坐标</summary>
        public string TargetLocationName { get; set; } = "";

        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float Z { get; set; } = 0f;

        public override void ExecuteEvent(object target, object controller, object narrativeComponent)
        {
            var character = ArsenalStatics.ResolveCharacterFromContext(target, controller);
            if (character?.Actor == null)
            {
                NarrativeLog.LogWarning($"TeleportEvent: 无法解析被传送的角色（目标地点 '{TargetLocationName}'）");
                return;
            }

            if (!TryResolveDestination(out var destination))
            {
                NarrativeLog.LogError($"TeleportEvent: 无法解析目标地点 '{TargetLocationName}'，且显式坐标为原点，传送取消");
                return;
            }

            var rotation = character.Actor.Orientation;
            if (!ArsenalStatics.FindTeleportSpot(character.Actor, ref destination, ref rotation))
            {
                NarrativeLog.LogError($"TeleportEvent: 目标地点 '{TargetLocationName}' 找不到安全落点，传送取消");
                return;
            }

            ArsenalStatics.TeleportCharacter(character, destination, rotation);
            NarrativeLog.Log($"TeleportEvent: 已传送到 '{TargetLocationName}' @ {destination}");
        }

        /// <summary>解析传送目标位置。</summary>
        private bool TryResolveDestination(out Vector3 destination)
        {
            destination = new Vector3(X, Y, Z);

            if (string.IsNullOrEmpty(TargetLocationName))
            {
                return destination.LengthSquared > 1e-6f;
            }

            var tag = new GameplayTag(TargetLocationName);

            // ① 按 POI 数据解析
            var navigation = NavigationSubsystem.Instance;
            if (navigation != null && navigation.GetPointOfInterest(out var poi, tag) && poi != null)
            {
                destination = poi.POILocation;
                return true;
            }

            // ② 按场景中的 POIActor 解析
            var poiActors = Level.GetScripts<POIActor>(null);
            if (poiActors != null)
            {
                foreach (var poiActor in poiActors)
                {
                    if (poiActor?.Actor != null && poiActor.POITag == tag)
                    {
                        destination = poiActor.Actor.Position;
                        return true;
                    }
                }
            }

            // ③ 名称无法解析：回退到显式坐标
            return destination.LengthSquared > 1e-6f;
        }

        public override string GetGraphDisplayText() =>
            string.IsNullOrEmpty(TargetLocationName)
                ? $"Teleport: ({X},{Y},{Z})"
                : $"Teleport: {TargetLocationName}";
    }
}
