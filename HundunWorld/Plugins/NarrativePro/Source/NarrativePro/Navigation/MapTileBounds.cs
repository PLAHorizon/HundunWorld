using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 单层瓦片贴图条目。适配 UE5 FMapTile.TileImages（TMap&lt;FGameplayTag, UTexture2D&gt;）的一个键值对。
    /// 键使用字符串（标签名）而非 <see cref="GameplayTag"/>，以便在场景/存档中稳定序列化与比较。
    /// </summary>
    [Serializable]
    public class MapTileLayerImage
    {
        /// <summary>地图层级标签名，例如 "Navigator.MapLayer.Default"</summary>
        public string Layer = "";

        /// <summary>该层的瓦片贴图资产路径</summary>
        public string ImagePath = "";
    }

    /// <summary>
    /// 单个地图瓦片数据。适配 UE5 FMapTile。
    /// <para>坐标约定：Flax 为 Y-up 左手系，地图平面是 <b>XZ</b>，瓦片位置在 XZ 上展开，Y 取地图 Actor 高度。</para>
    /// </summary>
    [Serializable]
    public class MapTile
    {
        /// <summary>瓦片图像资源路径（默认层，兼容 UE5 已被标记为待废弃的 TileImage）</summary>
        public string TileImagePath { get; set; } = "";

        /// <summary>按地图层级区分的瓦片贴图（对应 UE5 TileImages）</summary>
        public List<MapTileLayerImage> LayerImages { get; set; } = new List<MapTileLayerImage>();

        /// <summary>瓦片中心的世界位置（XZ 平面展开）</summary>
        public Vector3 TileLocation { get; set; } = Vector3.Zero;

        /// <summary>取指定层级的贴图路径；该层无贴图时回退到默认层。</summary>
        public string GetImagePathForLayer(GameplayTag layer)
        {
            if (!string.IsNullOrEmpty(layer.TagName) && LayerImages != null)
            {
                for (int i = 0; i < LayerImages.Count; i++)
                {
                    var entry = LayerImages[i];
                    if (entry != null && entry.Layer == layer.TagName && !string.IsNullOrEmpty(entry.ImagePath))
                    {
                        return entry.ImagePath;
                    }
                }
            }
            return TileImagePath;
        }

        /// <summary>写入指定层级的贴图路径（已存在则覆盖）。</summary>
        public void SetImagePathForLayer(GameplayTag layer, string imagePath)
        {
            if (!layer.IsValid()) return;

            if (LayerImages == null) LayerImages = new List<MapTileLayerImage>();
            for (int i = 0; i < LayerImages.Count; i++)
            {
                var entry = LayerImages[i];
                if (entry != null && entry.Layer == layer.TagName)
                {
                    entry.ImagePath = imagePath ?? "";
                    return;
                }
            }
            LayerImages.Add(new MapTileLayerImage { Layer = layer.TagName, ImagePath = imagePath ?? "" });
        }
    }

    /// <summary>
    /// 地图瓦片集合。适配 UE5 FMapTileSet。
    /// </summary>
    [Serializable]
    public class MapTileSet
    {
        /// <summary>瓦片层标签</summary>
        public GameplayTag TileLayer { get; set; } = GameplayTag.None;

        /// <summary>瓦片集网格宽度（每边瓦片数）</summary>
        public int GridWidth { get; set; } = 0;

        /// <summary>瓦片绘制分辨率（像素，正方形）</summary>
        public float TileImageSize { get; set; } = 0f;

        /// <summary>瓦片列表（行主序：先 Z 后 X）</summary>
        public List<MapTile> Tiles { get; set; } = new List<MapTile>();
    }

    /// <summary>
    /// 兴趣点（POI）数据。适配 UE5 FPOIData。
    /// </summary>
    [Serializable]
    public class POIData
    {
        /// <summary>POI 标签 ID</summary>
        public GameplayTag POITag { get; set; } = GameplayTag.None;

        /// <summary>关联的 POI 标签列表（用于高级导航图）</summary>
        public List<GameplayTag> LinkedPOIs { get; set; } = new List<GameplayTag>();

        /// <summary>POI 固定位置（适用于不移动的 POI）</summary>
        public Vector3 POILocation { get; set; } = Vector3.Zero;

        /// <summary>快速旅行到 POI 时的目标变换</summary>
        public Transform POIFastTravelSpot { get; set; } = Transform.Identity;

        /// <summary>是否需要地图标记</summary>
        public bool bNeedsMapMarker { get; set; } = true;

        /// <summary>是否支持快速旅行</summary>
        public bool bSupportsFastTravel { get; set; } = true;

        /// <summary>是否可被发现（如城市、营地等，未发现时图标置灰）</summary>
        public bool bIsDiscoverable { get; set; } = false;

        /// <summary>
        /// 发现半径（世界单位，XZ 平面）。仅当 <see cref="bIsDiscoverable"/> 为 true 时生效：
        /// 本地玩家进入该半径即视为已发现。
        /// <para>
        /// UE5 用 <c>APOIActor</c> 的触发体积表达「发现范围」，Flax 侧除了触发器体积外还支持这种距离形式——
        /// 因为本项目玩家角色没有碰撞体（触发器无法触发），距离判定是当前唯一可靠的发现途径。
        /// </para>
        /// </summary>
        public float DiscoveryRadius { get; set; } = 800f;

        /// <summary>地图标记图标资源路径</summary>
        public string MapMarkerIconPath { get; set; } = "";

        /// <summary>POI 显示名</summary>
        public string POIDisplayName { get; set; } = "Point of Interest";

        /// <summary>POI 副标题</summary>
        public string POISubtitle { get; set; } = "Location";

        /// <summary>复制一份（避免调用方拿到共享引用后误改资产数据）。</summary>
        public POIData Clone()
        {
            return new POIData
            {
                POITag = POITag,
                LinkedPOIs = LinkedPOIs != null ? new List<GameplayTag>(LinkedPOIs) : new List<GameplayTag>(),
                POILocation = POILocation,
                POIFastTravelSpot = POIFastTravelSpot,
                bNeedsMapMarker = bNeedsMapMarker,
                bSupportsFastTravel = bSupportsFastTravel,
                bIsDiscoverable = bIsDiscoverable,
                DiscoveryRadius = DiscoveryRadius,
                MapMarkerIconPath = MapMarkerIconPath,
                POIDisplayName = POIDisplayName,
                POISubtitle = POISubtitle,
            };
        }
    }

    /// <summary>
    /// 地图瓦片边界 Actor。管理地图瓦片集合和 POI 数据，并负责**瓦片网格生成**。
    /// 适配 UE5 AMapTileBounds；UE5 中瓦片网格由蓝图 BP_MapTileActor 生成，Flax 侧无蓝图节点，故由本类承担。
    /// <para>
    /// 坐标约定：地图是 XZ 平面上的正方形，边长 = <see cref="MapWidth"/>，中心为 Actor 位置。
    /// 瓦片按行主序（先 Z 行、后 X 列）排列，索引 = row * TilesPerSide + column。
    /// </para>
    /// </summary>
    public class MapTileBoundsActor : Script
    {
        /// <summary>每边瓦片数上限，防止误填（如 512）导致生成数十万瓦片拖死编辑器。</summary>
        public const int MaxTilesPerSide = 32;

        /// <summary>单块瓦片贴图分辨率（像素，正方形）。生成瓦片集时写入 <see cref="MapTileSet.TileImageSize"/>。</summary>
        public float TilePixelSize = 1024f;

        /// <summary>是否在启用的场景里自动补全瓦片网格（仅当瓦片集为空时）</summary>
        public bool bAutoGenerateOnEnable = true;

        /// <summary>瓦片集合</summary>
        public MapTileSet TileSet { get; set; } = new MapTileSet();

        /// <summary>世界中找到的所有 POI</summary>
        public List<POIData> POIs { get; set; } = new List<POIData>();

        /// <summary>地图宽度（世界单位，XZ 平面正方形边长）</summary>
        public float MapWidth { get; set; } = 100000f;

        /// <summary>每边瓦片数量（瓦片总数 = NumTiles²）</summary>
        public int NumTiles { get; set; } = 3;

        /// <summary>边界盒碰撞体（用于场景中可视化）</summary>
        public BoxCollider MapTileBoundsCollider { get; set; }

        /// <summary>每边瓦片数（钳制到合法区间）</summary>
        public int TilesPerSide => Mathf.Clamp(NumTiles, 1, MaxTilesPerSide);

        /// <summary>单块瓦片的世界边长</summary>
        public float TileWorldSize => MapWidth / TilesPerSide;

        /// <summary>瓦片总数</summary>
        public int TileCount => TilesPerSide * TilesPerSide;

        /// <summary>地图中心的世界位置</summary>
        public Vector3 MapCenter => Actor != null ? Actor.Position : Vector3.Zero;

        /// <summary>地图中心在 XZ 平面上的二维表示（供 UI / 投影使用）</summary>
        public Float2 MapOrigin2D
        {
            get
            {
                var c = MapCenter;
                return new Float2(c.X, c.Z);
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();

            EnsureCollider();
            ApplyColliderBounds();

            if (bAutoGenerateOnEnable && (TileSet == null || TileSet.Tiles == null || TileSet.Tiles.Count == 0))
            {
                RegenerateTiles(false);
            }

            NavigationSubsystem.Instance?.SetMapTileBounds(this);
        }

        public override void OnDisable()
        {
            if (NavigationSubsystem.Instance?.MapTileBounds == this)
            {
                NavigationSubsystem.Instance?.SetMapTileBounds(null);
            }
            base.OnDisable();
        }

        /// <summary>取得（必要时查找）边界盒碰撞体。BoxCollider 是 Actor 而非 Script，必须用 GetChild。</summary>
        public BoxCollider EnsureCollider()
        {
            if (MapTileBoundsCollider == null && Actor != null)
            {
                MapTileBoundsCollider = Actor.GetChild<BoxCollider>();
            }
            return MapTileBoundsCollider;
        }

        /// <summary>按 MapWidth 同步边界盒尺寸（XZ 正方形，Y 方向取 1 便于可视化）。</summary>
        public void ApplyColliderBounds()
        {
            var collider = EnsureCollider();
            if (collider != null)
            {
                collider.Size = new Vector3(MapWidth, 1f, MapWidth);
            }
        }

        /// <summary>
        /// 由 <see cref="MapWidth"/> / <see cref="NumTiles"/> 生成瓦片网格，并写入
        /// <see cref="MapTileSet.GridWidth"/> / <see cref="MapTileSet.TileImageSize"/> 与每块瓦片的 <c>TileLocation</c>。
        /// </summary>
        /// <param name="preserveExistingImages">
        /// 为 true 时按位置就近匹配保留已烘焙的贴图（重新生成网格不应丢失烘焙结果）。
        /// </param>
        public void RegenerateTiles(bool preserveExistingImages = true)
        {
            int side = TilesPerSide;

            if (TileSet == null) TileSet = new MapTileSet();
            TileSet.GridWidth = side;
            TileSet.TileImageSize = TilePixelSize;
            if (!TileSet.TileLayer.IsValid())
            {
                TileSet.TileLayer = NavigatorGameplayTags.MapLayer_Default;
            }

            var oldTiles = preserveExistingImages ? TileSet.Tiles : null;
            var newTiles = new List<MapTile>(side * side);
            float matchRadius = TileWorldSize * 0.5f;
            float matchRadiusSqr = matchRadius * matchRadius;

            for (int row = 0; row < side; row++)
            {
                for (int column = 0; column < side; column++)
                {
                    var location = GetTileWorldLocation(column, row);
                    var tile = new MapTile { TileLocation = location };

                    var previous = FindNearestTile(oldTiles, location, matchRadiusSqr);
                    if (previous != null)
                    {
                        tile.TileImagePath = previous.TileImagePath;
                        if (previous.LayerImages != null && previous.LayerImages.Count > 0)
                        {
                            tile.LayerImages = new List<MapTileLayerImage>(previous.LayerImages);
                        }
                    }

                    newTiles.Add(tile);
                }
            }

            TileSet.Tiles = newTiles;
        }

        /// <summary>取指定行列的瓦片中心世界位置（行 → Z，列 → X）。</summary>
        public Vector3 GetTileWorldLocation(int column, int row)
        {
            int side = TilesPerSide;
            float offset = (side - 1) * 0.5f;
            float size = TileWorldSize;
            var center = MapCenter;

            return new Vector3(
                center.X + (column - offset) * size,
                center.Y,
                center.Z + (row - offset) * size);
        }

        /// <summary>取瓦片在集合中的索引；越界返回 -1。</summary>
        public int GetTileIndex(int column, int row)
        {
            int side = TilesPerSide;
            if (column < 0 || row < 0 || column >= side || row >= side) return -1;
            return row * side + column;
        }

        /// <summary>按世界位置解算出瓦片行列（不校验范围，可能越界）。</summary>
        public void WorldToTileCoords(Vector3 world, out int column, out int row)
        {
            int side = TilesPerSide;
            var center = MapCenter;
            float offset = (side - 1) * 0.5f;
            float size = TileWorldSize <= 0f ? 1f : TileWorldSize;

            column = Mathf.RoundToInt((world.X - center.X) / size + offset);
            row = Mathf.RoundToInt((world.Z - center.Z) / size + offset);
        }

        /// <summary>取包含指定世界位置的瓦片；超出地图范围返回 null。</summary>
        public MapTile GetTileAtWorldPosition(Vector3 world)
        {
            WorldToTileCoords(world, out int column, out int row);
            int index = GetTileIndex(column, row);
            if (index < 0 || TileSet == null || TileSet.Tiles == null || index >= TileSet.Tiles.Count) return null;
            return TileSet.Tiles[index];
        }

        /// <summary>按索引取瓦片；越界返回 null。</summary>
        public MapTile GetTileAtIndex(int index)
        {
            if (index < 0 || TileSet == null || TileSet.Tiles == null || index >= TileSet.Tiles.Count) return null;
            return TileSet.Tiles[index];
        }

        /// <summary>地图在世界空间的包围盒（XZ 正方形，Y 方向厚度可指定）。</summary>
        public BoundingBox GetWorldBounds(float halfHeight = 0.5f)
        {
            var center = MapCenter;
            var extent = new Vector3(MapWidth * 0.5f, halfHeight, MapWidth * 0.5f);
            return new BoundingBox(center - extent, center + extent);
        }

        private static MapTile FindNearestTile(List<MapTile> tiles, Vector3 location, float radiusSqr)
        {
            if (tiles == null || tiles.Count == 0) return null;

            MapTile best = null;
            float bestDistanceSqr = radiusSqr;

            for (int i = 0; i < tiles.Count; i++)
            {
                var candidate = tiles[i];
                if (candidate == null) continue;

                float distanceSqr = Vector3.DistanceSquared(candidate.TileLocation, location);
                if (distanceSqr <= bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
