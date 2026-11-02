using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 前往指定地点任务。玩家进入 <see cref="AcceptanceRadius"/> 范围内即完成。
    /// <para>
    /// 目标解析：<see cref="LocationName"/> 作为 POI / POIActor 标签解析 → 失败时使用 <see cref="TargetLocation"/>。
    /// 目标点若不在导航网格上，会吸附到最近的导航网格点，避免「目标不可达导致任务永远无法完成」。
    /// </para>
    /// </summary>
    public class GoToLocationTask : NarrativeTask
    {
        /// <summary>目标地点名（POI 标签或 POIActor 标签）；为空时使用 <see cref="TargetLocation"/></summary>
        public string LocationName { get; set; } = "";

        /// <summary>显式目标位置（<see cref="LocationName"/> 解析失败时使用）</summary>
        public Vector3 TargetLocation { get; set; } = Vector3.Zero;

        /// <summary>到达判定半径（XZ 平面距离）</summary>
        public float AcceptanceRadius { get; set; } = 200f;

        /// <summary>是否把目标点吸附到导航网格（避免目标不可达）</summary>
        public bool bSnapTargetToNavigation { get; set; } = true;

        private bool _targetResolved;

        protected override void OnBeginTask()
        {
            TickInterval = 0.25f;
            _targetResolved = false;
        }

        protected override void TickTask()
        {
            CheckArrival();
        }

        /// <summary>
        /// 检查玩家是否已到达目标地点。到达则推进任务进度。
        /// 可由任务系统按 <see cref="NarrativeTask.TickInterval"/> 驱动，也可由外部 Script 主动调用。
        /// </summary>
        /// <returns>是否已到达。</returns>
        public bool CheckArrival()
        {
            if (IsComplete()) return true;
            if (!TryResolveTarget(out var target)) return false;

            var character = ArsenalStatics.ResolveCharacterFromContext(OwningPawn, OwningController);
            if (character?.Actor == null) return false;

            // 使用 XZ 平面距离：高度差不应影响「是否已到达某地点」的判定
            if (NavigatorStatics.Distance2D(character.Actor.Position, target) > AcceptanceRadius) return false;

            AddProgress(1);
            return true;
        }

        /// <summary>解析目标位置。</summary>
        public bool TryResolveTarget(out Vector3 target)
        {
            target = TargetLocation;

            if (!string.IsNullOrEmpty(LocationName))
            {
                var tag = new GameplayTag(LocationName);

                var navigation = NavigationSubsystem.Instance;
                if (navigation != null && navigation.GetPointOfInterest(out var poi, tag) && poi != null)
                {
                    target = poi.POILocation;
                }
                else
                {
                    var poiActors = Level.GetScripts<POIActor>(null);
                    if (poiActors != null)
                    {
                        foreach (var poiActor in poiActors)
                        {
                            if (poiActor?.Actor != null && poiActor.POITag == tag)
                            {
                                target = poiActor.Actor.Position;
                                break;
                            }
                        }
                    }
                }
            }

            if (target.LengthSquared < 1e-6f) return false;

            // 目标点不可导航时吸附到导航网格，避免任务永远无法完成
            if (!_targetResolved && bSnapTargetToNavigation && NavigatorStatics.IsNavigationReady()
                && NavigatorStatics.TryFindClosestPoint(target, out var snapped))
            {
                target = snapped;
            }

            _targetResolved = true;
            return true;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return !string.IsNullOrEmpty(LocationName) ? $"前往 {LocationName}" : "前往目标地点";
        }
    }
}
