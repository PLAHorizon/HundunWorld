using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// POI 渲染组件。适配 UE5 UPOIRenderingComponent（继承 UActorComponent）。
    /// UE5 中作为 POI Actor 的渲染辅助组件（构造函数为空，渲染逻辑由派生类或 SceneProxy 实现）。
    /// Flax 中转为 Script，提供 POI 在场景中的调试可视化能力。
    /// </summary>
    public class POIRenderingComponent : Script
    {
        /// <summary>关联的 POIActor（自动查找）</summary>
        public POIActor OwningPOI { get; private set; }

        /// <summary>调试绘制颜色</summary>
        public Color POIDebugColor = new Color(0.9f, 0.2f, 0.2f, 1f);

        /// <summary>调试绘制图标半径</summary>
        public float DebugIconRadius = 30f;

        /// <summary>调试绘制持续时间（秒，&lt;=0 表示每帧重绘）</summary>
        public float DebugDrawDuration = 0f;

        /// <summary>是否在未发现状态下以置灰颜色绘制</summary>
        public bool bDrawUndiscoveredAsGrayed = true;

        /// <summary>未发现状态的置灰颜色</summary>
        public Color UndiscoveredColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找所属 POIActor
            if (OwningPOI == null && Actor != null)
            {
                OwningPOI = Actor.GetScript<POIActor>();
            }
        }

        public override void OnDisable()
        {
            OwningPOI = null;
            base.OnDisable();
        }

        /// <summary>
        /// 调试绘制。在场景中绘制 POI 位置标记，便于直观查看 POI 分布。
        /// 已发现 POI 使用 POIDebugColor，未发现 POI 使用 UndiscoveredColor。
        /// </summary>
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (Actor == null) return;

            Color drawColor = POIDebugColor;
            if (bDrawUndiscoveredAsGrayed && OwningPOI != null && OwningPOI.POITag.IsValid())
            {
                // 检查是否已被任意导航组件发现；若无可发现导航组件，则按未发现绘制
                var navComps = NavigationSubsystem.Instance?.GetAllNavigationComponents();
                bool bDiscovered = false;
                if (navComps != null)
                {
                    foreach (var navComp in navComps)
                    {
                        if (navComp != null && navComp.HasDiscoveredPOI(OwningPOI.POITag))
                        {
                            bDiscovered = true;
                            break;
                        }
                    }
                }
                if (!bDiscovered)
                {
                    drawColor = UndiscoveredColor;
                }
            }

            Vector3 center = Actor.Position;

            // 绘制 POI 标记十字
            float r = DebugIconRadius;
            DebugDraw.DrawLine(
                center + new Vector3(-r, 0, 0),
                center + new Vector3(r, 0, 0),
                drawColor, DebugDrawDuration, false);
            DebugDraw.DrawLine(
                center + new Vector3(0, 0, -r),
                center + new Vector3(0, 0, r),
                drawColor, DebugDrawDuration, false);
            DebugDraw.DrawLine(
                center + new Vector3(0, -r, 0),
                center + new Vector3(0, r, 0),
                drawColor, DebugDrawDuration, false);
        }
    }
}
