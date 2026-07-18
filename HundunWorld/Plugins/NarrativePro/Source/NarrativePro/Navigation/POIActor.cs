using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Items;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// POI Actor。设计师用于在场景中放置兴趣点。
    /// 适配 UE5 APOIActor（继承 ATargetPoint，Flax 中改为 Script 挂到 Actor）。
    /// 默认在进入重叠区域时自动发现，可通过添加碰撞体定义发现范围。
    /// </summary>
    public class POIActor : Script
    {
        /// <summary>POI 标签</summary>
        public GameplayTag POITag { get; set; } = GameplayTag.None;

        /// <summary>是否创建地图标记</summary>
        public bool bCreateMapMarker { get; set; } = true;

        /// <summary>是否支持快速旅行</summary>
        public bool bSupportsFastTravel { get; set; } = true;

        /// <summary>UI 显示名</summary>
        public string POIDisplayName { get; set; } = "Point of Interest";

        /// <summary>POI 地图标记图标资源路径</summary>
        public string POIIconPath { get; set; } = "";

        /// <summary>关联的 POI 列表（用于高级导航图）</summary>
        public List<string> LinkedPOIPaths { get; set; } = new List<string>();

        /// <summary>关联的 POIActor 引用</summary>
        public List<POIActor> LinkedPOIActors { get; set; } = new List<POIActor>();

        /// <summary>发现 POI 的触发碰撞体</summary>
        public Collider DiscoveryCollider { get; set; }

        /// <summary>快速旅行放置角色的碰撞体</summary>
        public Collider FastTravelCapsule { get; set; }

        /// <summary>POI 数据缓存</summary>
        public POIData POIDataCache { get; private set; }

        public override void OnEnable()
        {
            base.OnEnable();
            // 构建 POI 数据
            POIDataCache = BuildPOIData();
            // 注册到导航子系统
            NavigationSubsystem.Instance?.RegisterPOI(POIDataCache);

            // 注册碰撞体重叠事件
            if (DiscoveryCollider != null)
            {
                DiscoveryCollider.TriggerEnter += OnDiscoveryTriggerEnter;
            }
            else
            {
                // 自动查找碰撞体
                DiscoveryCollider = Actor.GetScript<Collider>();
                if (DiscoveryCollider != null)
                {
                    DiscoveryCollider.TriggerEnter += OnDiscoveryTriggerEnter;
                }
            }
        }

        public override void OnDisable()
        {
            if (DiscoveryCollider != null)
            {
                DiscoveryCollider.TriggerEnter -= OnDiscoveryTriggerEnter;
            }
            base.OnDisable();
        }

        /// <summary>构建 POI 数据。</summary>
        protected virtual POIData BuildPOIData()
        {
            var data = new POIData
            {
                POITag = POITag,
                POILocation = Actor != null ? Actor.Position : Vector3.Zero,
                POIFastTravelSpot = Actor != null ? Actor.Transform : Transform.Identity,
                bNeedsMapMarker = bCreateMapMarker,
                bSupportsFastTravel = bSupportsFastTravel,
                bIsDiscoverable = true,
                MapMarkerIconPath = POIIconPath,
                POIDisplayName = POIDisplayName,
                POISubtitle = ""
            };

            // 填充关联 POI 标签
            if (LinkedPOIActors != null)
            {
                foreach (var linked in LinkedPOIActors)
                {
                    if (linked != null && linked.POITag.IsValid())
                    {
                        data.LinkedPOIs.Add(linked.POITag);
                    }
                }
            }

            return data;
        }

        /// <summary>发现触发器进入事件。</summary>
        private void OnDiscoveryTriggerEnter(PhysicsColliderActor other)
        {
            // PhysicsColliderActor 自身就是 Actor
            if (other == null) return;

            var navComp = other.GetScript<NarrativeNavigationComponent>();
            if (navComp != null && POITag.IsValid())
            {
                navComp.DiscoverPOI(POITag);
            }
        }

        /// <summary>获取快速旅行位置。</summary>
        public Transform GetFastTravelTransform()
        {
            // FastTravelCapsule 是 Collider，本身是 Actor 子类
            if (FastTravelCapsule != null)
            {
                return FastTravelCapsule.Transform;
            }
            return Actor != null ? Actor.Transform : Transform.Identity;
        }
    }
}
