using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 单个地图瓦片数据。适配 UE5 FMapTile。
    /// </summary>
    [Serializable]
    public class MapTile
    {
        /// <summary>瓦片图像资源路径</summary>
        public string TileImagePath { get; set; } = "";

        /// <summary>瓦片对应的世界位置</summary>
        public Vector3 TileLocation { get; set; } = Vector3.Zero;
    }

    /// <summary>
    /// 地图瓦片集合。适配 UE5 FMapTileSet。
    /// </summary>
    [Serializable]
    public class MapTileSet
    {
        /// <summary>瓦片层标签</summary>
        public GameplayTag TileLayer { get; set; } = GameplayTag.None;

        /// <summary>瓦片集网格宽度</summary>
        public int GridWidth { get; set; } = 0;

        /// <summary>瓦片绘制分辨率</summary>
        public float TileImageSize { get; set; } = 0f;

        /// <summary>瓦片列表</summary>
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

        /// <summary>地图标记图标资源路径</summary>
        public string MapMarkerIconPath { get; set; } = "";

        /// <summary>POI 显示名</summary>
        public string POIDisplayName { get; set; } = "Point of Interest";

        /// <summary>POI 副标题</summary>
        public string POISubtitle { get; set; } = "Location";
    }

    /// <summary>
    /// 地图瓦片边界 Actor。管理地图瓦片集合和 POI 数据。
    /// 适配 UE5 AMapTileBounds。
    /// </summary>
    public class MapTileBoundsActor : Script
    {
        /// <summary>瓦片集合</summary>
        public MapTileSet TileSet { get; set; } = new MapTileSet();

        /// <summary>世界中找到的所有 POI</summary>
        public List<POIData> POIs { get; set; } = new List<POIData>();

        /// <summary>地图宽度（cm）</summary>
        public float MapWidth { get; set; } = 100000f;

        /// <summary>构成地图的 1024x1024 瓦片数量</summary>
        public int NumTiles { get; set; } = 1;

        /// <summary>边界盒碰撞体（用于场景中可视化）</summary>
        public BoxCollider MapTileBoundsCollider { get; set; }

        public override void OnEnable()
        {
            base.OnEnable();
            // 自动查找碰撞体
            if (MapTileBoundsCollider == null)
            {
                MapTileBoundsCollider = Actor.GetScript<BoxCollider>();
            }
            // 注册到导航子系统
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
    }
}
