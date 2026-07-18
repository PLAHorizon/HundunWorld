using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 地图瓦片渲染组件。适配 UE5 UMapTileRenderingComponent（继承 UDebugDrawComponent）。
    /// UE5 中通过 CreateDebugSceneProxy 在场景中绘制地图瓦片边界与网格。
    /// Flax 中简化为 Script，通过 OnDebugDraw 绘制瓦片边界可视化。
    /// </summary>
    public class MapTileRenderingComponent : Script
    {
        /// <summary>瓦片边界绘制颜色</summary>
        public Color TileBoundsColor = new Color(0.3f, 0.6f, 1f, 1f);

        /// <summary>瓦片网格绘制颜色</summary>
        public Color TileGridColor = new Color(0.3f, 0.6f, 1f, 0.4f);

        /// <summary>调试绘制持续时间（秒，&lt;=0 表示每帧重绘）</summary>
        public float DebugDrawDuration = 0f;

        /// <summary>是否绘制瓦片网格</summary>
        public bool bDrawTileGrid = true;

        /// <summary>关联的地图瓦片边界 Actor（自动查找）</summary>
        public MapTileBoundsActor MapTileBounds { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            // 优先从导航子系统获取，其次自动查找
            MapTileBounds = NavigationSubsystem.Instance?.MapTileBounds;
            if (MapTileBounds == null && Actor != null)
            {
                MapTileBounds = Actor.GetScript<MapTileBoundsActor>();
            }
        }

        public override void OnDisable()
        {
            MapTileBounds = null;
            base.OnDisable();
        }

        /// <summary>
        /// 调试绘制。对应 UE5 CreateDebugSceneProxy/CalcBounds 的可视化功能。
        /// 在编辑器与运行时绘制地图瓦片边界与网格。
        /// </summary>
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (MapTileBounds == null)
            {
                MapTileBounds = NavigationSubsystem.Instance?.MapTileBounds;
                if (MapTileBounds == null) return;
            }

            // 绘制地图整体边界
            DrawMapBounds();

            // 绘制瓦片网格
            if (bDrawTileGrid)
            {
                DrawTileGrid();
            }
        }

        /// <summary>计算组件包围盒。适配 UE5 CalcBounds。</summary>
        public virtual BoundingBox CalcBounds()
        {
            if (MapTileBounds != null && MapTileBounds.MapTileBoundsCollider != null)
            {
                return MapTileBounds.MapTileBoundsCollider.Box;
            }
            if (Actor != null)
            {
                return Actor.Box;
            }
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

        private void DrawMapBounds()
        {
            BoundingBox bounds = CalcBounds();
            DrawBoundingBox(bounds, TileBoundsColor, DebugDrawDuration);
        }

        /// <summary>使用 DebugDraw.DrawLine 绘制包围盒的 12 条边。</summary>
        protected void DrawBoundingBox(BoundingBox bounds, Color color, float duration)
        {
            Vector3 min = bounds.Minimum;
            Vector3 max = bounds.Maximum;
            Vector3 a = new Vector3(min.X, min.Y, min.Z);
            Vector3 b = new Vector3(max.X, min.Y, min.Z);
            Vector3 c = new Vector3(max.X, min.Y, max.Z);
            Vector3 d = new Vector3(min.X, min.Y, max.Z);
            Vector3 e = new Vector3(min.X, max.Y, min.Z);
            Vector3 f = new Vector3(max.X, max.Y, min.Z);
            Vector3 g = new Vector3(max.X, max.Y, max.Z);
            Vector3 h = new Vector3(min.X, max.Y, max.Z);

            // 底面 4 条边
            DebugDraw.DrawLine(a, b, color, duration, false);
            DebugDraw.DrawLine(b, c, color, duration, false);
            DebugDraw.DrawLine(c, d, color, duration, false);
            DebugDraw.DrawLine(d, a, color, duration, false);
            // 顶面 4 条边
            DebugDraw.DrawLine(e, f, color, duration, false);
            DebugDraw.DrawLine(f, g, color, duration, false);
            DebugDraw.DrawLine(g, h, color, duration, false);
            DebugDraw.DrawLine(h, e, color, duration, false);
            // 垂直 4 条边
            DebugDraw.DrawLine(a, e, color, duration, false);
            DebugDraw.DrawLine(b, f, color, duration, false);
            DebugDraw.DrawLine(c, g, color, duration, false);
            DebugDraw.DrawLine(d, h, color, duration, false);
        }

        private void DrawTileGrid()
        {
            if (MapTileBounds.TileSet == null) return;

            float tileSize = MapTileBounds.TileSet.TileImageSize;
            if (tileSize <= 0f) return;

            int gridWidth = MapTileBounds.TileSet.GridWidth;
            if (gridWidth <= 0) return;

            BoundingBox bounds = CalcBounds();
            Vector3 min = bounds.Minimum;
            Vector3 max = bounds.Maximum;

            // 在 XZ 平面绘制网格线（Flax 中 Y 为向上轴）
            float extentX = max.X - min.X;
            float extentZ = max.Z - min.Z;
            if (extentX <= 0f || extentZ <= 0f) return;

            float stepX = extentX / gridWidth;
            float stepZ = extentZ / gridWidth;

            // 沿 X 方向的网格线
            for (int i = 0; i <= gridWidth; i++)
            {
                float z = min.Z + i * stepZ;
                DebugDraw.DrawLine(
                    new Vector3(min.X, min.Y, z),
                    new Vector3(max.X, min.Y, z),
                    TileGridColor, DebugDrawDuration, false);
            }

            // 沿 Z 方向的网格线
            for (int i = 0; i <= gridWidth; i++)
            {
                float x = min.X + i * stepX;
                DebugDraw.DrawLine(
                    new Vector3(x, min.Y, min.Z),
                    new Vector3(x, min.Y, max.Z),
                    TileGridColor, DebugDrawDuration, false);
            }
        }
    }
}
