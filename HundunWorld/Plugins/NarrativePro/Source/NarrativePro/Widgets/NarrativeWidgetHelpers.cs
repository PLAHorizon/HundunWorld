using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Navigation;

namespace NarrativePro.Widgets
{
    /// <summary>
    /// 控件几何工具。对应 UE5 UNarrativeWidgetHelpers（Private/Widgets/NarrativeWidgetHelpers.h）。
    /// <para>
    /// UE5 的 FGeometry 有 LocalToAbsolute / AbsoluteToLocal，
    /// Flax 的 <see cref="Control"/> 有等价的 PointToWindow / PointFromWindow（已核对该类型的公开方法），
    /// 因此两个函数都能一一对应实现。没有控件引用时退化为按矩形左上角做平移，
    /// 这与 UE5 在单位缩放几何下的结果一致。
    /// </para>
    /// </summary>
    public static class NarrativeWidgetHelpers
    {
        /// <summary>
        /// 把一个控件局部空间的点换算到另一个控件的局部空间。
        /// 对应 UE5 <c>ToGeometry.AbsoluteToLocal(FromGeometry.LocalToAbsolute(LocalPosition))</c>。
        /// </summary>
        /// <param name="toControl">目标控件</param>
        /// <param name="fromControl">来源控件</param>
        /// <param name="localPosition">来源控件的局部坐标</param>
        public static Float2 TransformLocalSpace(Control toControl, Control fromControl, Float2 localPosition)
        {
            if (toControl == null || fromControl == null) return localPosition;
            if (ReferenceEquals(toControl, fromControl)) return localPosition;

            // LocalToAbsolute -> AbsoluteToLocal
            Float2 windowSpace = fromControl.PointToWindow(localPosition);
            return toControl.PointFromWindow(windowSpace);
        }

        /// <summary>
        /// 矩形版坐标换算（无控件引用时的等价实现）：
        /// 把来源几何的局部点先抬到"窗口"空间（加来源左上角），再落到目标几何的局部空间（减目标左上角）。
        /// </summary>
        public static Float2 TransformLocalSpace(Rectangle toGeometry, Rectangle fromGeometry, Float2 localPosition)
        {
            Float2 absolute = fromGeometry.Location + localPosition;
            return absolute - toGeometry.Location;
        }

        /// <summary>
        /// 优先用控件层级换算，缺少控件引用时用矩形换算。
        /// </summary>
        public static Float2 TransformLocalSpace(Rectangle toGeometry, Rectangle fromGeometry, Control toControl, Control fromControl, Float2 localPosition)
        {
            if (toControl != null && fromControl != null)
            {
                return TransformLocalSpace(toControl, fromControl, localPosition);
            }
            return TransformLocalSpace(toGeometry, fromGeometry, localPosition);
        }

        /// <summary>
        /// 世界位置 → 地图空间位置（不含控件层级换算）。
        /// 对应 UE5 的 <c>OnPaintData.MapOrigin - FVector2D(WorldLocation) + OnPaintData.MapPan</c>。
        /// <para>
        /// 坐标约定差异：UE5 是 Z-up，地图平面为 (X, Y)；Flax 是 Y-up，地图平面为 (X, Z)。
        /// 本端口统一采用 <see cref="MapMarker.GetMarkerMapLocalPosition"/> 已有的
        /// <c>world - mapOrigin + mapPan</c> 约定，避免与既有地图 UI 打架。
        /// </para>
        /// </summary>
        public static Float2 WorldLocationToMapSpace(MarkerOnPaintData onPaintData, Vector3 worldLocation, bool bScaleToWorld = false)
        {
            if (onPaintData == null) return Float2.Zero;

            Float2 mapOrigin = onPaintData.MapOrigin;
            Float2 mapPan = onPaintData.MapPan;

            Float2 local = new Float2(
                worldLocation.X - (mapOrigin.X == float.MaxValue ? 0f : mapOrigin.X),
                worldLocation.Z - (mapOrigin.Y == float.MaxValue ? 0f : mapOrigin.Y));

            if (mapPan.X != float.MaxValue) local.X += mapPan.X;
            if (mapPan.Y != float.MaxValue) local.Y += mapPan.Y;

            if (bScaleToWorld && onPaintData.MapWorldWidth > 0f && onPaintData.MapImagePixels > 0f)
            {
                float scale = onPaintData.MapImagePixels / onPaintData.MapWorldWidth;
                local.X *= scale;
                local.Y *= scale;
            }

            return local;
        }

        /// <summary>
        /// 世界位置 → 父控件的绘制空间位置。
        /// 对应 UE5 <c>WorldLocationToPaint(FMarkerOnPaintData&amp;, const FVector&amp;)</c>：
        /// 先算地图空间位置，再从地图几何换算到父几何。
        /// </summary>
        public static Float2 WorldLocationToPaint(MarkerOnPaintData onPaintData, Vector3 worldLocation)
        {
            if (onPaintData == null) return Float2.Zero;

            Float2 mapLocal = WorldLocationToMapSpace(onPaintData, worldLocation);
            return TransformLocalSpace(
                onPaintData.ParentGeometry,
                onPaintData.MapGeometry,
                onPaintData.ParentControl,
                onPaintData.MapControl,
                mapLocal);
        }

        /// <summary>
        /// 整条世界坐标路径 → 绘制空间点列（地图面包屑绘制用）。
        /// </summary>
        public static List<Float2> WorldPathToPaint(MarkerOnPaintData onPaintData, IReadOnlyList<Vector3> worldPath)
        {
            var result = new List<Float2>();
            if (onPaintData == null || worldPath == null) return result;

            for (int i = 0; i < worldPath.Count; i++)
            {
                result.Add(WorldLocationToPaint(onPaintData, worldPath[i]));
            }
            return result;
        }

        /// <summary>
        /// 把一条绘制空间折线按虚线拆分成若干短线段。
        /// 对应 UE5 <c>UArsenalStatics::DrawDashedLine</c>（Flax 的 Render2D 没有虚线原语，
        /// 因此这里产出可供逐段绘制的线段端点）。
        /// </summary>
        /// <param name="points">绘制空间点列</param>
        /// <param name="dashLength">虚线单段长度（像素）</param>
        /// <param name="gapLength">空白长度（像素），<=0 时为实线</param>
        public static List<Float2[]> BuildDashedLineSegments(IReadOnlyList<Float2> points, float dashLength, float gapLength)
        {
            var segments = new List<Float2[]>();
            if (points == null || points.Count < 2) return segments;

            if (dashLength <= 0f) dashLength = 10f;
            if (gapLength < 0f) gapLength = 0f;

            float cycle = dashLength + gapLength;

            for (int i = 1; i < points.Count; i++)
            {
                Float2 start = points[i - 1];
                Float2 end = points[i];
                Float2 delta = end - start;
                float length = delta.Length;
                if (length <= 0.0001f) continue;

                Float2 direction = delta / length;

                if (gapLength <= 0f)
                {
                    segments.Add(new[] { start, end });
                    continue;
                }

                float traveled = 0f;
                while (traveled < length)
                {
                    float dashEnd = Math.Min(traveled + dashLength, length);
                    segments.Add(new[]
                    {
                        start + direction * traveled,
                        start + direction * dashEnd
                    });
                    traveled += cycle;
                }
            }

            return segments;
        }

        /// <summary>
        /// 在折线上找到离 <paramref name="location"/> 最近的点，返回其沿线的累计距离（弧长参数）。
        /// 对应 UE5 <c>FPathCurve::FindNearest</c>，用于跳过玩家身后的面包屑点。
        /// </summary>
        public static float FindNearestDistanceAlongPath(IReadOnlyList<Vector3> path, Vector3 location, out float outDistanceSquared)
        {
            outDistanceSquared = float.MaxValue;
            float bestAt = 0f;

            if (path == null || path.Count == 0) return 0f;
            if (path.Count == 1)
            {
                outDistanceSquared = Vector3.DistanceSquared(location, path[0]);
                return 0f;
            }

            float accumulated = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1];
                Vector3 b = path[i];
                float segmentLength = Vector3.Distance(a, b);

                if (segmentLength <= 0.0001f)
                {
                    accumulated += segmentLength;
                    continue;
                }

                Vector3 direction = (b - a) / segmentLength;
                float t = Vector3.Dot(location - a, direction);
                t = Math.Max(0f, Math.Min(segmentLength, t));

                Vector3 projected = a + direction * t;
                float distSq = Vector3.DistanceSquared(location, projected);
                if (distSq < outDistanceSquared)
                {
                    outDistanceSquared = distSq;
                    bestAt = accumulated + t;
                }

                accumulated += segmentLength;
            }

            return bestAt;
        }
    }
}
