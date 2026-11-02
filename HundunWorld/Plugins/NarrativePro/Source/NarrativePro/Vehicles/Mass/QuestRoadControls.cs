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
    /// 任务道路控制器。对应 UE5 AQuestRoadControls（QuestRoadControls.h/.cpp:1-240）。
    /// <para>
    /// UE5 里它是关卡摆放的 Actor：BeginPlay 时按 <c>bAutoActivate</c> 激活，激活后
    /// ① 用 BoxComponent 范围缓存重叠的 zonegraph 车道、② 通过注解组件给这些车道加/去标签、
    /// ③ 调整关联 <c>AMassVehicleSpawner</c> 的生成数量（保存旧值，停用时恢复）、
    /// ④ 按位置查询把 <c>IntersectionSideOverrides</c> 写进交叉口侧（过场/封路用）。
    /// Flax 侧 Actor 为 sealed → Script；BoxComponent 改为显式的 <see cref="BoxCenter"/> /
    /// <see cref="BoxExtent"/>（生成点过滤由 <see cref="RoadControlsSpawnPointsGenerator"/> 读取同一盒），
    /// 其余四步全部实现，并由 <see cref="VehicleTrafficSimulation"/> 与 JSON 生成点驱动。
    /// </para>
    /// </summary>
    public class QuestRoadControls : Script
    {
        /// <summary>当前场景内所有道路控制器。对应 UE5 <c>GetActorOfClass</c> 的作用域。</summary>
        public static readonly List<QuestRoadControls> ActiveControls = new List<QuestRoadControls>();

        /// <summary>此道路控制器激活时将覆盖的交叉口侧定义。对应 UE5 IntersectionSideOverrides。</summary>
        public List<IntersectionSideOverride> IntersectionSideOverrides = new List<IntersectionSideOverride>();

        /// <summary>搜索附近交叉口侧以覆盖时的查询范围。对应 UE5 IntersectionSideQueryExtent。</summary>
        public Vector3 IntersectionSideQueryExtent = new Vector3(1000f);

        /// <summary>道路控制器是否在 BeginPlay 时自动重新生成 Mass 载具。对应 UE5 bAutoActivate。</summary>
        public bool bAutoActivate = true;

        /// <summary>在盒边界内应生成的 Mass 载具数量。对应 UE5 NewSpawnCount（QuestRoadControls.h:71 默认 5）。</summary>
        public int NewSpawnCount = 5;

        /// <summary>道路控制器的存盘 GUID。对应 UE5 RoadControlsSaveGUID。</summary>
        public Guid RoadControlsSaveGUID = Guid.Empty;

        /// <summary>作用盒中心（世界坐标）。对应 UE5 的 BoxComponent 位置。</summary>
        public Vector3 BoxCenter = Vector3.Zero;

        /// <summary>作用盒半边长。对应 UE5 BoxComponent 的 UnscaledBoxExtent。</summary>
        public Vector3 BoxExtent = new Vector3(1500f, 400f, 1500f);

        /// <summary>道路控制注解组件。对应 UE5 RoadControlAnnotationComponent。</summary>
        [NonSerialized]
        public RoadControlAnnotationsComponent RoadControlAnnotationComponent;

        /// <summary>缓存的车道列表。对应 UE5 CachedLanes。</summary>
        [NonSerialized]
        public List<ZoneGraphLaneHandle> CachedLanes = new List<ZoneGraphLaneHandle>();

        /// <summary>查询车道的任意匹配标签。对应 UE5 TagsToAdd（激活时写入车道的标签）。</summary>
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

        /// <summary>本控制器当前写入交叉口侧的覆盖记录（停用时清除）。</summary>
        [NonSerialized]
        private readonly List<TrafficIntersectionSide> _appliedSideOverrides = new List<TrafficIntersectionSide>();

        public override void OnEnable()
        {
            base.OnEnable();
            if (!ActiveControls.Contains(this)) ActiveControls.Add(this);

            // 注解组件必须早于 SetActive 存在（UE5 在构造函数里 CreateDefaultSubobject）
            EnsureAnnotationComponent();

            // 对应 UE5 BeginPlay
            if (bAutoActivate)
            {
                SetActive(true);
            }
        }

        public override void OnDisable()
        {
            if (bIsActive)
            {
                SetActive(false);
            }
            ActiveControls.Remove(this);
            base.OnDisable();
        }

        private void EnsureAnnotationComponent()
        {
            if (RoadControlAnnotationComponent != null && RoadControlAnnotationComponent.Actor != null) return;
            RoadControlAnnotationComponent = Actor != null ? Actor.GetScript<RoadControlAnnotationsComponent>() : null;
            if (RoadControlAnnotationComponent == null && Actor != null)
            {
                RoadControlAnnotationComponent = Actor.AddScript<RoadControlAnnotationsComponent>();
            }
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

        /// <summary>点是否落在本控制器的作用盒内。对应 UE5 IsPointInBoxWithTransform 的等价判定。</summary>
        public bool ContainsPoint(Vector3 location)
        {
            Vector3 center = Actor != null ? Actor.Transform.Translation + BoxCenter : BoxCenter;
            Vector3 delta = location - center;
            return Mathf.Abs(delta.X) <= Mathf.Abs(BoxExtent.X)
                   && Mathf.Abs(delta.Y) <= Mathf.Abs(BoxExtent.Y)
                   && Mathf.Abs(delta.Z) <= Mathf.Abs(BoxExtent.Z);
        }

        /// <summary>
        /// 将此道路控制器设置为激活，将自动重新生成 Mass 载具。对应 UE5 SetActive（cpp:60-140）。
        /// </summary>
        public virtual void SetActive(bool bNewActive)
        {
            if (bIsActive == bNewActive) return;
            bIsActive = bNewActive;

            var subsystem = MassVehicleSubsystem.Instance;
            if (subsystem == null)
            {
                MassVehicleLog.LogWarning("QuestRoadControls：载具子系统尚未就绪，激活推迟到下一帧。");
                bIsActive = false;
                return;
            }

            if (bIsActive)
            {
                CacheLanes(subsystem);
                ApplyLaneTags(subsystem, true);
                ApplyIntersectionSideOverrides();
                AdjustSpawnerCount();
                MassVehicleLog.Log($"QuestRoadControls 激活：缓存车道 {CachedLanes.Count} 条、" +
                                   $"交叉口覆盖 {IntersectionSideOverrides.Count} 项。");
            }
            else
            {
                ApplyLaneTags(subsystem, false);
                ClearIntersectionSideOverrides();
                RestoreSpawnerCount();
                CachedLanes.Clear();
                MassVehicleLog.Log("QuestRoadControls 停用：车道标签与生成数量已恢复。");
            }
        }

        /// <summary>
        /// 缓存作用盒内、且通过标签过滤的车道。对应 UE5 的 zonegraph 重叠车道查询。
        /// </summary>
        private void CacheLanes(MassVehicleSubsystem subsystem)
        {
            CachedLanes.Clear();
            var storage = subsystem.Storage;
            if (storage == null) return;

            foreach (var lane in storage.Lanes)
            {
                if (lane == null || lane.Points == null || lane.Points.Count == 0) continue;

                if (!PassesTagFilter(lane.Tags)) continue;

                bool bInBox = false;
                for (int i = 0; i < lane.Points.Count; i++)
                {
                    if (ContainsPoint(lane.Points[i])) { bInBox = true; break; }
                }
                if (bInBox) CachedLanes.Add(storage.MakeHandle(lane.Index));
            }
        }

        /// <summary>标签过滤。对应 UE5 AnyTags/AllTags/NotTags 的车道查询。</summary>
        private bool PassesTagFilter(List<string> laneTags)
        {
            if (NotTags != null)
            {
                foreach (var tag in NotTags.TagNames)
                {
                    if (Has(laneTags, tag)) return false;
                }
            }
            if (AllTags != null)
            {
                foreach (var tag in AllTags.TagNames)
                {
                    if (!Has(laneTags, tag)) return false;
                }
            }
            if (AnyTags != null && AnyTags.TagNames.Count > 0)
            {
                foreach (var tag in AnyTags.TagNames)
                {
                    if (Has(laneTags, tag)) return true;
                }
                return false;
            }
            return true;
        }

        private static bool Has(List<string> tags, string tag)
        {
            if (tags == null || string.IsNullOrEmpty(tag)) return false;
            foreach (var existing in tags)
            {
                if (string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>给缓存车道加/去标签。对应 UE5 通过注解组件写 zonegraph 车道标签。</summary>
        private void ApplyLaneTags(MassVehicleSubsystem subsystem, bool bEnabled)
        {
            if (RoadControlAnnotationComponent == null) return;
            if (TagsToAdd == null || TagsToAdd.TagNames.Count == 0) return;

            var eventToAdd = new RoadControlAnnotationEvent(bEnabled, new List<ZoneGraphLaneHandle>(CachedLanes), TagsToAdd);
            RoadControlAnnotationComponent.QueueEvent(eventToAdd);
        }

        /// <summary>
        /// 应用交叉口侧覆盖。对应 UE5 在 SetActive 里按位置找侧并写 SideOverride + 置交叉口覆盖标记。
        /// </summary>
        private void ApplyIntersectionSideOverrides()
        {
            var lightSubsystem = TrafficLightSubsystem.Instance;
            if (lightSubsystem == null || IntersectionSideOverrides == null) return;

            foreach (var overrideEntry in IntersectionSideOverrides)
            {
                var sides = lightSubsystem.IntersectionSidesGrid.Query(overrideEntry.IntersectionSideLocation, IntersectionSideQueryExtent);
                if (sides == null || sides.Count == 0)
                {
                    MassVehicleLog.LogWarning($"QuestRoadControls：在 {overrideEntry.IntersectionSideLocation} 未找到交叉口侧，覆盖未生效。");
                    continue;
                }

                var handle = sides[0];
                var side = handle.GetIntersectionSide(lightSubsystem);
                if (side == null) continue;

                side.SideOverride = overrideEntry.Rule;
                var intersection = handle.GetIntersection(lightSubsystem);
                intersection?.SetOverrideIntersection(true);
                if (!_appliedSideOverrides.Contains(side)) _appliedSideOverrides.Add(side);
            }
        }

        /// <summary>清除本控制器写下的交叉口侧覆盖。</summary>
        private void ClearIntersectionSideOverrides()
        {
            var lightSubsystem = TrafficLightSubsystem.Instance;
            foreach (var side in _appliedSideOverrides)
            {
                if (side == null) continue;
                side.SideOverride = EIntersectionSideRule.AllClosed;

                if (lightSubsystem != null)
                {
                    foreach (var data in lightSubsystem.RegisteredLaneData)
                    {
                        foreach (var intersection in data.Intersections)
                        {
                            if (intersection.IntersectionSides.Contains(side))
                            {
                                intersection.SetOverrideIntersection(false);
                            }
                        }
                    }
                }
            }
            _appliedSideOverrides.Clear();
        }

        /// <summary>把关联生成器的数量改为 NewSpawnCount（记住旧值）。对应 UE5 的 SetSpawnCount 调整。</summary>
        private void AdjustSpawnerCount()
        {
            if (VehicleSpawner == null) return;
            OldSpawnCount = VehicleSpawner.SpawnCount;
            VehicleSpawner.SetSpawnCount(NewSpawnCount);
        }

        /// <summary>恢复生成器旧数量。对应 UE5 停用时的 OldSpawnCount 回写。</summary>
        private void RestoreSpawnerCount()
        {
            if (VehicleSpawner == null) return;
            VehicleSpawner.SetSpawnCount(OldSpawnCount);
        }

        /// <summary>关联生成器（由 <see cref="VehicleTrafficSimulation"/> 按配置绑定）。</summary>
        public void BindSpawner(MassVehicleSpawner spawner)
        {
            VehicleSpawner = spawner;
            if (spawner != null) spawner.BindRoadControls(this);
        }
    }
}
