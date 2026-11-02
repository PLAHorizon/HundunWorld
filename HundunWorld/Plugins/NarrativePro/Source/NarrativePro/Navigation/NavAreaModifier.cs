using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航区域体积：把一片世界区域标记为某种 <see cref="NavArea"/>（道路、障碍等）。
    /// 适配 UE5 中"在世界里放置一个 UNavArea 蓝图体积"来给导航多边形打上区域标签的做法。
    /// <para>
    /// Flax 的托管导航 API 不支持 NavArea 烘焙，因此本组件把区域代价暴露给
    /// <see cref="NavAreaSystem"/>，由 <see cref="NavigatorStatics.TryFindPath"/> 消费。
    /// </para>
    /// </summary>
    public class NavAreaModifier : Script
    {
        /// <summary>此体积赋予区域的导航区域（默认道路）。</summary>
        public NavArea Area;

        /// <summary>区域半尺寸（世界单位，相对 Actor 变换）。Flax 无体积 Actor，用手工盒代替。</summary>
        public Vector3 HalfExtent = new Vector3(500f, 100f, 500f);

        /// <summary>
        /// 沿最长轴的道路中心线采样间距（世界单位）。
        /// 采样点即 <see cref="NavAreaSystem"/> 代价图里的可寻路节点。
        /// </summary>
        public float SampleSpacing = 600f;

        /// <summary>是否参与代价图（关掉等于从此区域移除道路加成）。</summary>
        public bool bIncludeInCostGraph = true;

        /// <summary>把中心线采样点限制在导航网格上（投影失败则丢弃该点）。</summary>
        public bool bProjectSamplesToNavMesh = true;

        /// <summary>Actor 移动后调用，令代价图重建。</summary>
        public void InvalidateArea()
        {
            NavAreaSystem.InvalidateGraph();
        }

        public override void OnEnable()
        {
            base.OnEnable();
            if (Area == null) Area = NavAreaRoad.Instance;
            NavAreaSystem.Register(this);
        }

        public override void OnDisable()
        {
            NavAreaSystem.Unregister(this);
            base.OnDisable();
        }

        /// <summary>此区域的实际代价（Null 区域视为不可通行）。</summary>
        public float GetAreaCost() => Area != null ? Area.DefaultCost : 1f;

        /// <summary>世界空间包围盒。</summary>
        public BoundingBox GetWorldBounds()
        {
            if (Actor == null) return new BoundingBox(Vector3.Zero, Vector3.Zero);
            return new BoundingBox(Actor.Position - HalfExtent, Actor.Position + HalfExtent);
        }

        /// <summary>点是否落在本区域内（世界空间）。</summary>
        public bool ContainsPoint(Vector3 location)
        {
            return GetWorldBounds().Contains(location) == ContainmentType.Contains;
        }

        /// <summary>
        /// 区域中心线采样点（世界空间）：沿 Actor 局部最长水平轴等距取点。
        /// 道路体积因此提供一条"道路骨架"，代价搜索会把路径往它身上贴。
        /// </summary>
        public List<Vector3> GetCostSamplePoints()
        {
            var points = new List<Vector3>();
            if (Actor == null || !bIncludeInCostGraph) return points;

            Vector3 scale = Actor.Scale;
            Vector3 extent = new Vector3(
                Mathf.Abs(HalfExtent.X * scale.X),
                Mathf.Abs(HalfExtent.Y * scale.Y),
                Mathf.Abs(HalfExtent.Z * scale.Z));

            // 选取水平面（X/Z）上更长的一根轴作为中心线方向
            Vector3 axis;
            float length;
            if (extent.Z >= extent.X)
            {
                axis = Actor.Transform.Forward;
                length = extent.Z;
            }
            else
            {
                axis = Actor.Transform.Right;
                length = extent.X;
            }

            float flatLength = Mathf.Sqrt(axis.X * axis.X + axis.Z * axis.Z);
            if (flatLength <= 0.0001f || length <= 0f) return points;
            axis = new Vector3(axis.X / flatLength, 0f, axis.Z / flatLength);

            float spacing = SampleSpacing > 0f ? SampleSpacing : 600f;
            // 至少两个端点，否则中心线不可用
            int steps = Math.Max(1, (int)Math.Round(length * 2f / spacing));
            float step = length * 2f / steps;

            Vector3 center = Actor.Position;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = center + axis * (-length + step * i);
                if (bProjectSamplesToNavMesh && NavigatorStatics.TryFindClosestPoint(point, out var projected))
                {
                    point = projected;
                }
                points.Add(point);
            }

            return points;
        }

#if FLAX_EDITOR
        // `DebugDraw.*` 的绘制方法未导出到非编辑器目标（仅存在于编辑器程序集），故整段调试绘制只在编辑器编译。

        /// <summary>
        /// 调试绘制区域包围盒与道路中心线采样点。
        /// 对应 UE5 NavArea 的 DrawColor 可视化。
        /// </summary>
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();
            DrawArea();
        }

        /// <summary>选中时绘制实体区域（对应 UE5 的 FComponentVisualizer 选中态）。</summary>
        public override void OnDebugDrawSelected()
        {
            base.OnDebugDrawSelected();
            DrawArea();
        }

        private void DrawArea()
        {
            if (Actor == null) return;

            Color color = Area != null ? Area.DrawColor : NavArea.Default.DrawColor;
            DebugDraw.DrawWireBox(GetWorldBounds(), color, 0f, false);

            var samples = GetCostSamplePoints();
            for (int i = 0; i < samples.Count; i++)
            {
                DebugDraw.DrawWireSphere(new BoundingSphere(samples[i], 20f), color, 0f, false);
                if (i > 0)
                {
                    DebugDraw.DrawLine(samples[i - 1], samples[i], color, 0f, false);
                }
            }
        }
#endif
    }
}
