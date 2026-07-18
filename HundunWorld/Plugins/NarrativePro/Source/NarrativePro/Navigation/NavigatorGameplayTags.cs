using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航系统相关 GameplayTag 常量。
    /// 适配 UE5 FNavigatorGameplayTags，使用 NarrativePro.Items.GameplayTag 轻量级实现。
    /// </summary>
    public static class NavigatorGameplayTags
    {
        // 地图层级
        public static readonly GameplayTag MapLayer_Default = "Navigator.MapLayer.Default";

        // 导航器类型
        public static readonly GameplayTag NavigatorTypes_Compass = "Navigator.NavigatorTypes.Compass";
        public static readonly GameplayTag NavigatorTypes_Screenspace = "Navigator.NavigatorTypes.Screenspace";
        public static readonly GameplayTag NavigatorTypes_Minimap = "Navigator.NavigatorTypes.Minimap";
        public static readonly GameplayTag NavigatorTypes_Worldmap = "Navigator.NavigatorTypes.Worldmap";

        // 兴趣点类别根
        public static readonly GameplayTag PointOfInterestCategory = "Navigator.PointOfInterest";

        /// <summary>根据字符串查找标签。</summary>
        public static GameplayTag FindTagByString(string tagString)
        {
            return new GameplayTag(tagString);
        }
    }
}
