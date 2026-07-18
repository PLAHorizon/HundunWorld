using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 自定义航点标记。玩家可在地图上放置的临时航点。
    /// 适配 UE5 UCustomWaypointMarker。
    /// </summary>
    public class CustomWaypointMarker : MapMarker
    {
        public CustomWaypointMarker()
        {
            DefaultMarkerSettings.MarkerTitleText = "自定义航点";
            DefaultMarkerSettings.IconSize = new Vector2(24f, 24f);

            // 默认显示在所有域
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Worldmap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Minimap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Compass);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Screenspace);

            // 不绘制面包屑路径
            bDrawBreadcrumbs = false;
        }

        public override string GetMarkerActionText(NarrativeNavigationComponent selector)
        {
            return "移除航点";
        }

        public override void OnSelect(NarrativeNavigationComponent selector)
        {
            base.OnSelect(selector);
            // 点击自定义航点时移除它
            selector?.RemoveCustomWaypoint(this);
        }
    }
}
