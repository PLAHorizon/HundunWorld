using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Spawners
{
    /// <summary>
    /// NPC 生成可视化组件。对应 UE5 UNPCSpawnVisualizerComponent。
    /// UE5 中继承 UPrimitiveComponent，通过 CreateSceneProxy 在编辑器视口中绘制生成范围可视化。
    /// Flax 中简化为 Script，通过 OnDebugDraw 绘制调试几何体（如 UntetherDistance 范围）。
    /// </summary>
    public class NPCSpawnVisualizerComponent : Script
    {
        /// <summary>可视化绘制的半径（对应 NPCSpawnComponent 的 UntetherDistance）。</summary>
        public float VisualizerRadius = 3000f;

        /// <summary>可视化圆圈颜色。</summary>
        public Color VisualizerColor = new Color(0.2f, 0.8f, 0.2f, 1.0f);

        /// <summary>可视化圆圈分段数。</summary>
        public int CircleSegments = 48;

        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        /// <summary>
        /// 调试绘制。对应 UE5 CreateSceneProxy/CalcBounds 的可视化功能。
        /// 在编辑器与运行时绘制生成范围圆圈，便于直观查看生成器覆盖区域。
        /// </summary>
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (Actor == null) return;

            // 同步所在 NPCSpawnComponent 的 UntetherDistance
            var spawnComp = Actor.GetScript<NPCSpawnComponent>();
            if (spawnComp != null)
            {
                VisualizerRadius = spawnComp.UntetherDistance;
            }

            Vector3 center = Actor.Position;

            // 绘制水平圆圈（XZ 平面，Flax 中 Y 为向上轴）
            float angleStep = (float)(2.0 * Math.PI / CircleSegments);
            Vector3 prevPoint = center + new Vector3(
                VisualizerRadius,
                0,
                0);

            for (int i = 1; i <= CircleSegments; i++)
            {
                float angle = i * angleStep;
                Vector3 currentPoint = center + new Vector3(
                    VisualizerRadius * (float)Math.Cos(angle),
                    0,
                    VisualizerRadius * (float)Math.Sin(angle));

                DebugDraw.DrawLine(prevPoint, currentPoint, VisualizerColor, 0f, false);
                prevPoint = currentPoint;
            }

            // 绘制中心十字标记
            float crossSize = 20f;
            DebugDraw.DrawLine(
                center + new Vector3(-crossSize, 0, 0),
                center + new Vector3(crossSize, 0, 0),
                VisualizerColor, 0f, false);
            DebugDraw.DrawLine(
                center + new Vector3(0, 0, -crossSize),
                center + new Vector3(0, 0, crossSize),
                VisualizerColor, 0f, false);
        }
    }
}
