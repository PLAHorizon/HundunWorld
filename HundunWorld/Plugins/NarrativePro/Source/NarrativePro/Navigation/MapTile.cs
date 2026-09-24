using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 地图瓦片标记。作为地图背景图像的特殊标记类型（不可交互）。
    /// 适配 UE5 UMapTileMarker。
    /// <para>
    /// 由 <see cref="NarrativeNavigationComponent"/> 在构建导航时按 <see cref="MapTileBoundsActor.TileSet"/> 逐块生成：
    /// 贴图 = 当前地图层级对应的瓦片图，ImageSize = <see cref="MapTileSet.TileImageSize"/>，
    /// ZOrder = 0（瓦片必须位于所有图标之下）。
    /// </para>
    /// </summary>
    public class MapTileMarker : MapMarker
    {
        /// <summary>瓦片默认 ZOrder —— 必须小于图标标记，保证瓦片在地图最底层。</summary>
        public const int TileZOrder = 0;

        /// <summary>瓦片对应的 MapTile 数据</summary>
        public MapTile TileData { get; set; }

        /// <summary>瓦片所属的 MapTileSet</summary>
        public MapTileSet TileSet { get; set; }

        /// <summary>瓦片在 TileSet.Tiles 中的索引（-1 表示未关联）。用于分层切换时快速定位。</summary>
        public int TileIndex { get; set; } = -1;

        public MapTileMarker()
        {
            bWantsOnPaint = false;
            ZOrder = TileZOrder;
        }

        /// <summary>瓦片不可交互（与 UE5 UMapTileMarker 一致）。</summary>
        public override bool CanInteract(NarrativeNavigationComponent selector)
        {
            return false;
        }

        /// <summary>
        /// 按地图层级刷新瓦片贴图。对应 UE5 UNarrativeNavigationComponent::SetMapLayer 中对瓦片的处理：
        /// 该层无专属贴图时回退到默认层。
        /// </summary>
        /// <returns>贴图路径是否发生变化。</returns>
        public bool ApplyLayerImage(GameplayTag layer)
        {
            if (TileData == null) return false;

            string imagePath = TileData.GetImagePathForLayer(layer);
            if (DefaultMarkerSettings.LocationIconPath == imagePath) return false;

            DefaultMarkerSettings.LocationIconPath = imagePath;
            RefreshMarker();
            return true;
        }

        /// <summary>按 <see cref="MapTileSet.TileImageSize"/> 同步图标尺寸。</summary>
        public void ApplyTileImageSize()
        {
            if (TileSet == null || TileSet.TileImageSize <= 0f) return;

            var size = new Vector2(TileSet.TileImageSize, TileSet.TileImageSize);
            if (DefaultMarkerSettings.IconSize != size)
            {
                DefaultMarkerSettings.IconSize = size;
                RefreshMarker();
            }
        }

        /// <summary>
        /// 瓦片图像由地图 UI 控件直接绘制（瓦片是背景层，不参与标记 OnPaint 流程）。
        /// </summary>
        public override void MarkerOnPaint(MarkerOnPaintData onPaintData)
        {
        }
    }
}
