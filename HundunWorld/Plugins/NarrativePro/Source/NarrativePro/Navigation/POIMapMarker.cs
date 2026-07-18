using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// POI 地图标记。每个 POI 对应一个此类型的标记。
    /// 适配 UE5 UPOIMapMarker。
    /// 可被发现，且点击后可触发快速旅行。
    /// </summary>
    public class POIMapMarker : MapMarker
    {
        /// <summary>对应的 POI 数据</summary>
        public POIData POI { get; set; }

        public POIMapMarker()
        {
            // POI 标记默认显示在所有域
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Worldmap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Minimap);
            MarkerDomain.AddTag(NavigatorGameplayTags.NavigatorTypes_Compass);
        }

        public override string GetMarkerActionText(NarrativeNavigationComponent selector)
        {
            if (POI != null && POI.bSupportsFastTravel)
            {
                return "快速旅行";
            }
            return base.GetMarkerActionText(selector);
        }

        public override string GetMarkerDisplayText(NarrativeNavigationComponent selector, GameplayTag navigatorType, out string outSubtitleText)
        {
            if (POI != null)
            {
                outSubtitleText = POI.POISubtitle;
                // 未发现的可发现 POI 显示为 "???"
                if (POI.bIsDiscoverable && selector != null && !selector.HasDiscoveredPOI(POI.POITag))
                {
                    return "???";
                }
                return POI.POIDisplayName;
            }
            return base.GetMarkerDisplayText(selector, navigatorType, out outSubtitleText);
        }

        public override Color GetMarkerColor(NarrativeNavigationComponent selector, GameplayTag navigatorType)
        {
            // 未发现的 POI 置灰
            if (POI != null && POI.bIsDiscoverable && selector != null && !selector.HasDiscoveredPOI(POI.POITag))
            {
                return Color.Gray;
            }
            return base.GetMarkerColor(selector, navigatorType);
        }

        public override bool CanInteract(NarrativeNavigationComponent selector)
        {
            if (POI == null) return false;
            // 未发现不可交互
            if (POI.bIsDiscoverable && selector != null && !selector.HasDiscoveredPOI(POI.POITag))
            {
                return false;
            }
            return true;
        }

        public override void OnSelect(NarrativeNavigationComponent selector)
        {
            base.OnSelect(selector);
            // 如果支持快速旅行，请求快速旅行
            if (POI != null && POI.bSupportsFastTravel && selector != null)
            {
                selector.RequestFastTravel(POI);
            }
        }
    }
}
