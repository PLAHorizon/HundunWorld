using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 地图瓦片标记。作为地图背景图像的特殊标记类型。
    /// 适配 UE5 UMapTileMarker。
    /// </summary>
    public class MapTileMarker : MapMarker
    {
        /// <summary>瓦片对应的 MapTile 数据</summary>
        public MapTile TileData { get; set; }

        /// <summary>瓦片所属的 MapTileSet</summary>
        public MapTileSet TileSet { get; set; }

        public MapTileMarker()
        {
            // 瓦片标记默认在所有域都不可交互
            bWantsOnPaint = true;
        }

        public override bool CanInteract(NarrativeNavigationComponent selector)
        {
            // 瓦片不可交互
            return false;
        }

        public override void MarkerOnPaint(MarkerOnPaintData onPaintData)
        {
            // 默认不绘制瓦片图像，由 UI 控件负责
            // 子类可覆盖以使用 Render2D.DrawTexture 绘制瓦片图像
        }
    }
}
