using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交叉口侧覆盖。对应 UE5 FIntersectionSideOverride（QuestRoadControls.h）。
    /// </summary>
    [Serializable]
    public struct IntersectionSideOverride
    {
        /// <summary>用于查询附近交叉口侧的位置。对应 UE5 IntersectionSideLocation。</summary>
        public Vector3 IntersectionSideLocation;

        /// <summary>应用到指定交叉口侧的覆盖规则。对应 UE5 Rule（Bitmask EIntersectionSideRule）。</summary>
        public EIntersectionSideRule Rule;
    }

    /// <summary>
    /// 任务道路控制器。对应 UE5 AQuestRoadControls（QuestRoadControls.h）。
    /// 继承 AActor + INarrativeSavableActor。运行时调整 zonegraph 车道标签及其他特性。
    /// 简化点：
    /// - Flax Actor 为 sealed，改为 Script 挂载到 Actor 上
    /// - INarrativeSavableActor 简化为字段 + 方法（GetActorGUID/SetActorGUID）
    /// - Mass 相关逻辑（车道缓存、生成器激活）用占位保留（Flax 不兼容）
    /// - FGuid → System.Guid
    /// </summary>
    public class QuestRoadControls : Script
    {
        /// <summary>此道路控制器激活时将覆盖的交叉口侧定义。对应 UE5 IntersectionSideOverrides。</summary>
        public List<IntersectionSideOverride> IntersectionSideOverrides = new List<IntersectionSideOverride>();

        /// <summary>搜索附近交叉口侧以覆盖时的查询范围。对应 UE5 IntersectionSideQueryExtent。</summary>
        public Vector3 IntersectionSideQueryExtent = new Vector3(1000f);

        /// <summary>道路控制器是否在 BeginPlay 时自动重新生成 Mass 载具。对应 UE5 bAutoActivate。</summary>
        public bool bAutoActivate = true;

        /// <summary>在盒边界内应生成的 Mass 载具数量。对应 UE5 NewSpawnCount。</summary>
        public int NewSpawnCount = 5;

        /// <summary>道路控制器的存盘 GUID。对应 UE5 RoadControlsSaveGUID。</summary>
        public Guid RoadControlsSaveGUID = Guid.Empty;

        /// <summary>道路控制注解组件。对应 UE5 RoadControlAnnotationComponent。</summary>
        [NonSerialized]
        public RoadControlAnnotationsComponent RoadControlAnnotationComponent;

        /// <summary>缓存的车道列表。对应 UE5 CachedLanes。</summary>
        [NonSerialized]
        public List<ZoneGraphLaneHandle> CachedLanes = new List<ZoneGraphLaneHandle>();

        /// <summary>添加到重叠车道的标签。对应 UE5 TagsToAdd（FZoneGraphTagMask）。</summary>
        public ZoneGraphTagMask TagsToAdd = ZoneGraphTagMask.None;

        /// <summary>查询车道的任意匹配标签。对应 UE5 AnyTags。</summary>
        public ZoneGraphTagMask AnyTags = ZoneGraphTagMask.None;

        /// <summary>查询车道的全部匹配标签。对应 UE5 AllTags。</summary>
        public ZoneGraphTagMask AllTags = ZoneGraphTagMask.None;

        /// <summary>查询车道的不匹配标签。对应 UE5 NotTags。</summary>
        public ZoneGraphTagMask NotTags = ZoneGraphTagMask.None;

        /// <summary>关联的载具生成器。对应 UE5 VehicleSpawner（AMassVehicleSpawner*）。</summary>
        [NonSerialized]
        public MassVehicleSpawner VehicleSpawner;

        /// <summary>此道路控制器是否处于激活状态。对应 UE5 bIsActive。</summary>
        protected bool bIsActive = false;

        /// <summary>存储 Mass 生成器中的原始生成数量。对应 UE5 OldSpawnCount。</summary>
        protected int OldSpawnCount = 0;

        public override void OnEnable()
        {
            base.OnEnable();
            // 对应 UE5 BeginPlay
            if (bAutoActivate)
            {
                SetActive(true);
            }
        }

        public override void OnDisable()
        {
            // 对应 UE5 EndPlay
            base.OnDisable();
        }

        /// <summary>获取 Actor 的 GUID。对应 UE5 GetActorGUID_Implementation。</summary>
        public virtual Guid GetActorGUID()
        {
            return RoadControlsSaveGUID;
        }

        /// <summary>设置 Actor 的 GUID。对应 UE5 SetActorGUID_Implementation。</summary>
        public virtual void SetActorGUID(Guid guid)
        {
            RoadControlsSaveGUID = guid;
        }

        /// <summary>返回道路控制器是否正在主动管理 Mass 载具生成。对应 UE5 IsActive。</summary>
        public virtual bool IsActive()
        {
            return bIsActive;
        }

        /// <summary>将此道路控制器设置为激活，将自动重新生成 Mass 载具。对应 UE5 SetActive。</summary>
        /// <param name="bNewActive">是否激活。</param>
        public virtual void SetActive(bool bNewActive)
        {
            bIsActive = bNewActive;
            // Flax-不兼容: UE5 的 Mass Entity System 在 Flax 无对应物，保留占位。原文 TODO: Flax 无 Mass Entity System，需自定义实现（更新车道标签、调整生成数量等）
            NarrativeLog.Log($"[QuestRoadControls] SetActive({bNewActive}): Flax 无 Mass，需自定义实现");
        }
    }
}
