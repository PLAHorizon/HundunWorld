using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航系统静态工具函数库。适配 UE5 UNavigatorStatics。
    /// </summary>
    public static class NavigatorStatics
    {
        /// <summary>将世界位置投影到地图本地空间位置。
        /// MapWidth 为地图世界宽度，MapOrigin 为地图中心位置。</summary>
        public static Float2 WorldToMapLocalPosition(Vector3 worldLocation, Float2 mapOrigin, float mapWidth, float mapImagePixels)
        {
            // 世界 X 投影到地图 X，世界 Y 投影到地图 Y
            float scale = mapImagePixels / mapWidth;
            return new Float2(
                (worldLocation.X - mapOrigin.X) * scale,
                (worldLocation.Y - mapOrigin.Y) * scale
            );
        }

        /// <summary>将地图本地空间位置反向投影到世界位置。</summary>
        public static Vector3 MapLocalPositionToWorld(Float2 mapLocal, Float2 mapOrigin, float mapWidth, float mapImagePixels, float worldZ = 0f)
        {
            float scale = mapWidth / mapImagePixels;
            return new Vector3(
                mapLocal.X * scale + mapOrigin.X,
                mapLocal.Y * scale + mapOrigin.Y,
                worldZ
            );
        }

        /// <summary>计算两标记之间的世界距离。</summary>
        public static float DistanceBetweenMarkers(MapMarker a, MapMarker b)
        {
            if (a == null || b == null) return float.MaxValue;
            return Vector3.Distance(a.GetMarkerTransform().Translation, b.GetMarkerTransform().Translation);
        }

        /// <summary>根据敌意类型获取标记颜色。</summary>
        public static Color GetColorForAttitude(ENavigationAttitude attitude)
        {
            switch (attitude)
            {
                case ENavigationAttitude.Player: return NavigationDeveloperSettings.PlayerColor;
                case ENavigationAttitude.Friendly: return NavigationDeveloperSettings.FriendlyColor;
                case ENavigationAttitude.Neutral: return NavigationDeveloperSettings.NeutralColor;
                case ENavigationAttitude.Hostile: return NavigationDeveloperSettings.HostileColor;
                default: return Color.White;
            }
        }
    }

    /// <summary>
    /// 导航标记的敌意类型，用于决定标记颜色。
    /// </summary>
    public enum ENavigationAttitude
    {
        /// <summary>本地玩家</summary>
        Player = 0,
        /// <summary>友方</summary>
        Friendly = 1,
        /// <summary>中立</summary>
        Neutral = 2,
        /// <summary>敌方</summary>
        Hostile = 3,
        /// <summary>无特定敌意</summary>
        None = 4
    }
}
