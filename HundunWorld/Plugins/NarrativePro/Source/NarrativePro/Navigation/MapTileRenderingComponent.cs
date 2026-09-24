using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 地图瓦片渲染组件。适配 UE5 UMapTileRenderingComponent（其 SceneProxy 负责绘制 POI 连线与包围盒）。
    /// <para>
    /// Flax 无 <c>FDebugRenderSceneProxy</c>，改用 <see cref="Script.OnDebugDraw"/> + <see cref="DebugDraw"/>，
    /// 在编辑器视口与运行时绘制：
    /// 1) 地图整体边界（XZ 正方形）；
    /// 2) 瓦片网格（步长 = MapWidth / GridWidth，与世界单位一致）；
    /// 3) POI 之间的 <c>LinkedPOIs</c> 连线（对应 UE5 的 ArrowLines）。
    /// </para>
    /// </summary>
    public class MapTileRenderingComponent : Script
    {
        /// <summary>地图边界绘制颜色</summary>
        public Color TileBoundsColor = new Color(0.3f, 0.6f, 1f, 1f);

        /// <summary>瓦片网格绘制颜色</summary>
        public Color TileGridColor = new Color(0.3f, 0.6f, 1f, 0.4f);

        /// <summary>POI 连线绘制颜色（对应 UE5 的红色 ArrowLines）</summary>
        public Color POILinkColor = new Color(1f, 0.2f, 0.2f, 1f);

        /// <summary>调试绘制持续时间（秒，&lt;=0 表示每帧重绘）</summary>
        public float DebugDrawDuration = 0f;

        /// <summary>是否绘制瓦片网格</summary>
        public bool bDrawTileGrid = true;

        /// <summary>是否绘制 POI 连线</summary>
        public bool bDrawPOILinks = true;

        /// <summary>最大网格线数量（防止 GridWidth 误填导致每帧画数万条线）</summary>
        public int MaxGridLines = 64;

        /// <summary>关联的地图瓦片边界 Actor（自动查找）</summary>
        public MapTileBoundsActor MapTileBounds { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            ResolveMapTileBounds();
        }

        public override void OnDisable()
        {
            MapTileBounds = null;
            base.OnDisable();
        }

        private void ResolveMapTileBounds()
        {
            // 优先从导航子系统获取，其次场景查找
            MapTileBounds = NavigationSubsystem.Instance?.MapTileBounds ?? Level.FindScript<MapTileBoundsActor>();
        }

#if FLAX_EDITOR
        // `DebugDraw.*` 的绘制方法未导出到非编辑器目标，整段调试绘制只在编辑器编译。
        public override void OnDebugDraw()
        {
            base.OnDebugDraw();

            if (MapTileBounds == null)
            {
                ResolveMapTileBounds();
                if (MapTileBounds == null) return;
            }

            DrawMapBounds();

            if (bDrawTileGrid)
            {
                DrawTileGrid();
            }

            if (bDrawPOILinks)
            {
                DrawPOILinks();
            }
        }

        private void DrawMapBounds()
        {
            DebugDraw.DrawWireBox(CalcBounds(), TileBoundsColor, DebugDrawDuration, false);
        }
#endif

        /// <summary>地图在世界空间的包围盒。适配 UE5 CalcBounds。</summary>
        public virtual BoundingBox CalcBounds()
        {
            if (MapTileBounds != null)
            {
                return MapTileBounds.GetWorldBounds();
            }
            if (Actor != null)
            {
                return Actor.Box;
            }
            return new BoundingBox(Vector3.Zero, Vector3.Zero);
        }

#if FLAX_EDITOR

        /// <summary>
        /// 绘制瓦片网格。步长取 <c>MapWidth / GridWidth</c>（世界单位），
        /// 与 <c>TileImageSize</c>（像素分辨率）是两个不同量纲，不可混用。
        /// </summary>
        private void DrawTileGrid()
        {
            int gridWidth = MapTileBounds.TilesPerSide;
            if (gridWidth <= 1) return;

            if (gridWidth > MaxGridLines)
            {
                NarrativeLog.LogWarning($"MapTileRenderingComponent: GridWidth={gridWidth} 超过绘制上限 {MaxGridLines}，跳过网格绘制");
                return;
            }

            var bounds = CalcBounds();
            Vector3 min = bounds.Minimum;
            Vector3 max = bounds.Maximum;
            float stepX = (max.X - min.X) / gridWidth;
            float stepZ = (max.Z - min.Z) / gridWidth;
            if (stepX <= 0f || stepZ <= 0f) return;

            // Flax 为 Y-up，地图平面是 XZ
            for (int i = 0; i <= gridWidth; i++)
            {
                float x = min.X + i * stepX;
                DebugDraw.DrawLine(
                    new Vector3(x, min.Y, min.Z),
                    new Vector3(x, min.Y, max.Z),
                    TileGridColor, DebugDrawDuration, false);

                float z = min.Z + i * stepZ;
                DebugDraw.DrawLine(
                    new Vector3(min.X, min.Y, z),
                    new Vector3(max.X, min.Y, z),
                    TileGridColor, DebugDrawDuration, false);
            }
        }

        /// <summary>绘制 POI 之间的链接（对应 UE5 FMapTileRenderingSceneProxy 的 ArrowLines）。</summary>
        private void DrawPOILinks()
        {
            var pois = MapTileBounds?.POIs;
            if (pois == null || pois.Count < 2) return;

            foreach (var poi in pois)
            {
                if (poi?.LinkedPOIs == null) continue;

                foreach (var linkedTag in poi.LinkedPOIs)
                {
                    var linked = FindPOI(pois, linkedTag);
                    if (linked == null) continue;

                    DebugDraw.DrawLine(poi.POILocation, linked.POILocation, POILinkColor, DebugDrawDuration, false);
                }
            }
        }
#endif

        private static POIData FindPOI(System.Collections.Generic.List<POIData> pois, GameplayTag tag)
        {
            if (!tag.IsValid()) return null;

            for (int i = 0; i < pois.Count; i++)
            {
                var candidate = pois[i];
                if (candidate != null && candidate.POITag == tag) return candidate;
            }
            return null;
        }
    }
}
