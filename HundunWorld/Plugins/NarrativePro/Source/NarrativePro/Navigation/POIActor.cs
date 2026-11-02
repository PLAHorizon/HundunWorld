using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
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

        /// <summary>
        /// POI 标签的**字符串**形式（如 <c>Navigator.PointOfInterest.Kaifeng</c>）。
        /// <para>
        /// 存在原因：<see cref="GameplayTag"/> 是自定义结构体，编辑器工具链（反射式属性写入）无法写入它，
        /// 而字符串可以。填写本字段后 <see cref="BuildPOIData"/> 会以它作为 POITag；
        /// 两者都填时以 <see cref="POITag"/> 优先（便于代码侧直接指定）。
        /// </para>
        /// </summary>
        public string POITagName { get; set; } = "";

        /// <summary>是否创建地图标记</summary>
        public bool bCreateMapMarker { get; set; } = true;

        /// <summary>是否支持快速旅行</summary>
        public bool bSupportsFastTravel { get; set; } = true;

        /// <summary>
        /// 发现半径（世界单位，XZ 平面）。玩家进入该半径即视为已发现。
        /// <see cref="DiscoveryCollider"/> 存在时以触发器为准（触发器可用时），本半径用于距离判定。
        /// </summary>
        public float DiscoveryRadius { get; set; } = 800f;

        /// <summary>
        /// 是否需要玩家探索才会显示（未发现时地图上显示为「？」且不可快速旅行）。
        /// <para>
        /// 传送点通常设为 false（传送网络默认已知、可立即使用）；
        /// 村落/遗迹等兴趣点保持 true，构成探索玩法。
        /// </para>
        /// </summary>
        public bool bIsDiscoverable { get; set; } = true;

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

#if FLAX_EDITOR
            // UE5 在 APOIActor 构造函数里 CreateEditorOnlyDefaultSubobject<UPOIRenderingComponent>
            //（POIActor.cpp:48，RenderingComponent 字段见 POIActor.h:61）。Flax 侧等价通路：
            // 仅编辑器目标下挂 POIRenderingComponent（只在 OnDebugDraw 按发现状态画 POI 十字标记，
            // 不影响运行时行为）。
            if (Actor != null && Actor.GetScript<POIRenderingComponent>() == null)
            {
                Actor.AddScript<POIRenderingComponent>();
            }
#endif

            // 构建 POI 数据
            POIDataCache = BuildPOIData();
            // 注册到导航子系统
            NavigationSubsystem.Instance?.RegisterPOI(POIDataCache);

            // 注册碰撞体重叠事件（Collider 是 Actor，必须用 GetChild 而非 GetScript）
            if (DiscoveryCollider == null)
            {
                DiscoveryCollider = FindDiscoveryCollider();
            }

            if (DiscoveryCollider != null)
            {
                DiscoveryCollider.TriggerEnter += OnDiscoveryTriggerEnter;
            }
            else
            {
                NarrativeLog.LogWarning($"POIActor[{POIDisplayName}]: 未找到发现触发器碰撞体，POI 将无法通过重叠自动发现");
            }
        }

        /// <summary>
        /// 首帧补注册：本 POI 早于 <see cref="NavigationSubsystem"/> 启用时，OnEnable 里的自注册会因
        /// Instance 为空而落空（子系统侧同样有场景枚举兜底，两边各补一次使两种启用顺序都不丢 POI）。
        /// </summary>
        public override void OnStart()
        {
            base.OnStart();

            if (POIDataCache == null)
            {
                POIDataCache = BuildPOIData();
            }

            NavigationSubsystem.Instance?.RegisterPOI(POIDataCache);
        }

        /// <summary>
        /// 自动查找用于发现判定的子碰撞体：优先取非快速旅行落点的触发器碰撞体。
        /// </summary>
        private Collider FindDiscoveryCollider()
        {
            if (Actor == null) return null;

            var colliders = Actor.GetChildren<Collider>();
            if (colliders == null || colliders.Length == 0) return null;

            Collider fallback = null;
            foreach (var collider in colliders)
            {
                if (collider == null) continue;
                if (FastTravelCapsule != null && collider == FastTravelCapsule) continue;

                if (collider.IsTrigger) return collider;
                if (fallback == null) fallback = collider;
            }

            return fallback;
        }

        public override void OnDisable()
        {
            if (DiscoveryCollider != null)
            {
                DiscoveryCollider.TriggerEnter -= OnDiscoveryTriggerEnter;
            }

            if (POIDataCache != null && POIDataCache.POITag.IsValid())
            {
                NavigationSubsystem.Instance?.UnregisterPOI(POIDataCache.POITag);
            }

            base.OnDisable();
        }

        /// <summary>构建 POI 数据。</summary>
        protected virtual POIData BuildPOIData()
        {
            // 标签来源：代码侧直接给的 POITag 优先；编辑器里填写的是字符串 POITagName
            var tag = POITag.IsValid() ? POITag : new GameplayTag(POITagName);

            var data = new POIData
            {
                POITag = tag,
                POILocation = Actor != null ? Actor.Position : Vector3.Zero,
                POIFastTravelSpot = GetFastTravelTransform(),
                bNeedsMapMarker = bCreateMapMarker,
                bSupportsFastTravel = bSupportsFastTravel,
                // 显式写 this.：对象初始化器里左右同名，避免歧义
                bIsDiscoverable = this.bIsDiscoverable,
                DiscoveryRadius = this.DiscoveryRadius,
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
