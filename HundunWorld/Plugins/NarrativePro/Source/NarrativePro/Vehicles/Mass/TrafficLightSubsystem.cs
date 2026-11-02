using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Vehicles.TrafficLights;

namespace NarrativePro.Vehicles.Mass
{
    /// <summary>
    /// 交通灯子系统（Flax 侧手写宿主）。对应 UE5 UTrafficLightSubsystem（TrafficLightSubsystem.h/.cpp:1-382）。
    /// <para>
    /// UE5 里它是 MassTickableSubsystemBase：Initialize 依赖 ZoneGraph/Mass 子系统并遍历已注册
    /// ZoneGraphData 构建交叉口（<c>PostZoneGraphDataAdded</c> + <c>BuildLaneData</c>），
    /// Tick 逐交叉口推进周期、提前发关闭事件、在交叉口被占用时延后切换。
    /// Flax 无 ZoneGraph/Mass，车道拓扑改由 <see cref="ZoneGraphStorage"/>（JSON 构建）提供，
    /// 注解系统改为本地事件表（<see cref="TrafficIntersectionAnnotations"/> 消费），
    /// Tick 由 <see cref="NarrativePro.Vehicles.VehicleTrafficSimulation"/> 每帧调用。
    /// </para>
    /// </summary>
    public class TrafficLightSubsystem
    {
        /// <summary>当前实例。由 <see cref="NarrativePro.Vehicles.VehicleTrafficSimulation"/> 创建/销毁。</summary>
        public static TrafficLightSubsystem Instance { get; private set; }

        /// <summary>获取实例。对应 UE5 <c>GetSubsystem&lt;UTrafficLightSubsystem&gt;</c>。</summary>
        public static TrafficLightSubsystem Get() => Instance;

        /// <summary>载具子系统。对应 UE5 VehicleSubsystem（Initialize 里的 InitializeDependency）。</summary>
        public MassVehicleSubsystem VehicleSubsystem;

        /// <summary>已按数据句柄索引的车道/交叉口数据。对应 UE5 RegisteredLaneData。</summary>
        public List<TrafficLightData> RegisteredLaneData = new List<TrafficLightData>();

        /// <summary>交叉口侧哈希网格。对应 UE5 IntersectionSidesGrid（FIntersectionSideHashGrid）。</summary>
        public IntersectionSideHashGrid IntersectionSidesGrid = new IntersectionSideHashGrid();

        /// <summary>已注册的交通灯。对应 UE5 RegisteredTrafficLights（TArray&lt;TWeakObjectPtr&lt;ATrafficLight&gt;&gt;）。</summary>
        [NonSerialized]
        public List<TrafficLight> RegisteredTrafficLights = new List<TrafficLight>();

        /// <summary>订阅周期事件的注解组件。对应 UE5 ZoneGraphAnnotationSubsystem 的注解组件登记表。</summary>
        [NonSerialized]
        private readonly List<TrafficIntersectionAnnotations> _annotationHandlers = new List<TrafficIntersectionAnnotations>();

        /// <summary>待应用的周期事件队列。对应 UE5 ZoneGraphAnnotationSubsystem 的事件容器。</summary>
        private readonly List<TrafficPeriodEvent> _pendingEvents = new List<TrafficPeriodEvent>();

        /// <summary>累计发出的事件数（诊断用）。</summary>
        public int SentEventCount { get; private set; }

        /// <summary>推进过的周期切换次数（诊断自证用）。</summary>
        public int PeriodSwitchCount { get; private set; }

        // =====================================================================
        // 生命周期
        // =====================================================================

        /// <summary>
        /// 初始化。对应 UE5 Initialize（cpp:22-42）：绑定依赖、遍历已注册 ZoneGraphData 并构建交叉口。
        /// Flax 侧只有一份由 JSON 构建的存储，因此 <c>PostZoneGraphDataAdded</c> 直接被调用一次。
        /// </summary>
        public virtual void Initialize(MassVehicleSubsystem vehicleSubsystem)
        {
            Instance = this;
            VehicleSubsystem = vehicleSubsystem;
            RegisteredLaneData.Clear();
            IntersectionSidesGrid.Clear();
            _annotationHandlers.Clear();
            _pendingEvents.Clear();

            if (VehicleSubsystem?.Storage != null)
            {
                PostZoneGraphDataAdded(VehicleSubsystem.Storage);
            }
        }

        /// <summary>关闭。</summary>
        public void Shutdown()
        {
            if (Instance == this) Instance = null;
            _annotationHandlers.Clear();
            RegisteredTrafficLights.Clear();
            RegisteredLaneData.Clear();
            IntersectionSidesGrid.Clear();
        }

        /// <summary>注册注解处理器。对应 UE5 注解组件在 PostSubsystemsInitialized 里挂到子系统。</summary>
        public void RegisterAnnotations(TrafficIntersectionAnnotations annotations)
        {
            if (annotations == null || _annotationHandlers.Contains(annotations)) return;
            _annotationHandlers.Add(annotations);
        }

        /// <summary>注销注解处理器。</summary>
        public void UnregisterAnnotations(TrafficIntersectionAnnotations annotations)
        {
            _annotationHandlers.Remove(annotations);
        }

        /// <summary>按数据句柄查车道数据。对应 UE5 RegisteredLaneData[DataHandle.Index]。</summary>
        public TrafficLightData FindLaneData(ZoneGraphDataHandle handle)
        {
            if (handle.Index < 0 || handle.Index >= RegisteredLaneData.Count) return null;
            var data = RegisteredLaneData[handle.Index];
            return data != null && data.DataHandle.Index == handle.Index ? data : null;
        }

        // =====================================================================
        // Tick：周期推进（逐段对应 TrafficLightSubsystem.cpp:49-146）
        // =====================================================================

        /// <summary>每帧更新。对应 UE5 Tick(float DeltaTime)。</summary>
        public virtual void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            var settings = VehicleSubsystem != null ? VehicleSubsystem.TrafficLightSettings : null;
            settings ??= new TrafficLightSettings();

            for (int dataIndex = 0; dataIndex < RegisteredLaneData.Count; dataIndex++)
            {
                var laneData = RegisteredLaneData[dataIndex];
                if (laneData == null) continue;

                for (int i = 0; i < laneData.Intersections.Count; i++)
                {
                    var intersection = laneData.Intersections[i];

                    // cpp:57 —— 被任务/过场覆盖的交叉口不做任何逻辑
                    if (intersection.bOverrideIntersection) continue;
                    if (intersection.TrafficPeriods == null || intersection.TrafficPeriods.Count == 0) continue;

                    TrafficPeriod currentPeriod;

                    // cpp:66-78 —— 首个周期：推进 + 发 Open 事件
                    if (intersection.CurrentPeriodIndex < 0)
                    {
                        currentPeriod = intersection.IncrementCurrentPeriod();
                        UpdateRegisteredTrafficLights(intersection);
                        SendPeriodEvent(new TrafficPeriodEvent(currentPeriod, ELaneState.Open));
                        continue;
                    }

                    currentPeriod = intersection.GetCurrentPeriod();
                    if (currentPeriod == null) continue;

                    intersection.RemainingPeriodDuration -= deltaTime;

                    // cpp:83-95 —— 提前关闭本周期车道（LaneCloseAdvanceTime）
                    if (intersection.RemainingPeriodDuration <= settings.LaneCloseAdvanceTime)
                    {
                        SendPeriodEvent(new TrafficPeriodEvent(currentPeriod, ELaneState.Closed));
                    }

                    // cpp:99-140 —— 周期到期；若交叉口仍被占用则本轮不切换（防死锁）
                    if (intersection.RemainingPeriodDuration > 0f) continue;

                    if (IsIntersectionOccupied(intersection, settings)) continue;

                    PeriodSwitchCount++;
                    var nextPeriod = intersection.IncrementCurrentPeriod();
                    UpdateRegisteredTrafficLights(intersection);
                    SendPeriodEvent(new TrafficPeriodEvent(nextPeriod, ELaneState.Open));
                }
            }

            ApplyPendingEvents();
        }

        /// <summary>
        /// 交叉口是否仍有车。对应 UE5 cpp:100-130：取各侧位置平均，用 2000 范围查障碍网格，
        /// 命中且其所在车道属于本交叉口 → 视为占用。
        /// </summary>
        private bool IsIntersectionOccupied(TrafficLightIntersection intersection, TrafficLightSettings settings)
        {
            if (VehicleSubsystem == null || intersection.IntersectionSides.Count == 0) return false;

            Vector3 sideAverage = Vector3.Zero;
            foreach (var side in intersection.IntersectionSides)
            {
                sideAverage += side.SideLocation;
            }
            sideAverage /= (float)intersection.IntersectionSides.Count;

            float extent = settings.IntersectionOccupancyQueryExtent;
            var records = VehicleSubsystem.VehicleObstacles.Query(sideAverage, new Vector3(extent));
            if (records == null || records.Count == 0) return false;

            var intersectionLanes = new HashSet<ZoneGraphLaneHandle>(intersection.GetLanes());
            foreach (var record in records)
            {
                if (!record.bIsVehicle) continue;
                if (record.Lane.IsValid && intersectionLanes.Contains(record.Lane)) return true;
            }
            return false;
        }

        // =====================================================================
        // 交叉口构建（对应 cpp:148-262 PostZoneGraphDataAdded + 264-350 BuildLaneData）
        // =====================================================================

        /// <summary>
        /// ZoneGraph 数据添加后处理。对应 UE5 PostZoneGraphDataAdded（cpp:148-215）：
        /// 首次登记该数据时构建交叉口、把所有交叉口车道初始化为关闭，
        /// 再按「四向方形口 → 直行+右转 / 本侧全放行 / 对向全放行」或「逐侧各放一轮」建周期。
        /// </summary>
        public virtual void PostZoneGraphDataAdded(ZoneGraphStorage storage)
        {
            if (storage == null) return;

            int index = storage.DataHandle.Index;
            while (RegisteredLaneData.Count <= index)
            {
                RegisteredLaneData.Add(new TrafficLightData());
            }

            var laneData = RegisteredLaneData[index];
            if (laneData.DataHandle.Index == index && laneData.Intersections.Count > 0)
            {
                return; // 已构建（UE5 cpp:162 的 DataHandle != Storage.DataHandle 短路）
            }

            BuildLaneData(laneData, storage);

            var settings = VehicleSubsystem != null ? VehicleSubsystem.TrafficLightSettings : new TrafficLightSettings();

            for (int i = 0; i < laneData.Intersections.Count; i++)
            {
                var intersection = laneData.Intersections[i];

                // cpp:176 —— 只有 1 侧的不是真交叉口（多半是关卡编辑失误）
                if (intersection.IntersectionSides.Count <= 1) continue;

                // cpp:181-194 —— 初始化时所有交叉口车道置为关闭（Duration=-1 的常驻周期）
                var disabledLanes = new List<ZoneGraphLaneHandle>();
                foreach (var side in intersection.IntersectionSides)
                {
                    disabledLanes.AddRange(side.Lanes);
                }
                SendPeriodEvent(new TrafficPeriodEvent(
                    new TrafficPeriod(disabledLanes, -1f, EIntersectionSideRule.AllClosed), ELaneState.Closed));

                if (intersection.IntersectionSides.Count == 4 && intersection.IsSquareShaped())
                {
                    // cpp:203-253 —— 方形四向口：三周期一组（直行+右转 / 本侧全放 / 对向全放）
                    for (int s = 0; s < intersection.IntersectionSides.Count; s++)
                    {
                        int sLeft = (s + 1) % 4;
                        int sOpposite = (s + 2) % 4;
                        int sRight = (s + 3) % 4;

                        var sideToOppositeAndRight = new List<ZoneGraphLaneHandle>();
                        intersection.GetSidesConnectingLanes(s, sOpposite, storage, sideToOppositeAndRight);
                        intersection.GetSidesConnectingLanes(s, sRight, storage, sideToOppositeAndRight);

                        var oppositeToSideAndLeft = new List<ZoneGraphLaneHandle>();
                        intersection.GetSidesConnectingLanes(sOpposite, s, storage, oppositeToSideAndLeft);
                        intersection.GetSidesConnectingLanes(sOpposite, sLeft, storage, oppositeToSideAndLeft);

                        var sideAll = new List<ZoneGraphLaneHandle>(intersection.IntersectionSides[s].Lanes);
                        var oppositeAll = new List<ZoneGraphLaneHandle>(intersection.IntersectionSides[sOpposite].Lanes);

                        intersection.TrafficPeriods.Add(new TrafficPeriod(sideToOppositeAndRight,
                            settings.ScaleDuration(settings.StraightPeriodDuration),
                            EIntersectionSideRule.StraightOpen | EIntersectionSideRule.RightOpen));
                        intersection.TrafficPeriods.Add(new TrafficPeriod(sideAll,
                            settings.ScaleDuration(settings.AllDirectionsPeriodDuration),
                            EIntersectionSideRule.AllDirectionsOpen));
                        intersection.TrafficPeriods.Add(new TrafficPeriod(oppositeAll,
                            settings.ScaleDuration(settings.AllDirectionsPeriodDuration),
                            EIntersectionSideRule.AllDirectionsOpen));
                    }
                }
                else
                {
                    // cpp:255-261 —— 非方形口：每侧各放行一个周期
                    for (int k = 0; k < intersection.IntersectionSides.Count; k++)
                    {
                        intersection.TrafficPeriods.Add(new TrafficPeriod(
                            new List<ZoneGraphLaneHandle>(intersection.IntersectionSides[k].Lanes),
                            settings.ScaleDuration(settings.SimplePeriodDuration),
                            EIntersectionSideRule.AllDirectionsOpen));
                    }
                }
            }

            // 构建完立即结算初始关闭事件，保证第一帧车道就是关闭态（UE5 由注解子系统同帧处理）
            ApplyPendingEvents();
        }

        /// <summary>
        /// 构建车道数据。对应 UE5 BuildLaneData（cpp:217-262 中的构建段 + 264-350）：
        /// 带交叉口标签的车道 → 建交叉口；普通车道的出向链接若指向交叉口，则该车道构成交叉口的一侧。
        /// </summary>
        public virtual void BuildLaneData(TrafficLightData laneData, ZoneGraphStorage storage)
        {
            if (laneData == null || storage == null) return;

            laneData.DataHandle = storage.DataHandle;
            laneData.Intersections.Clear();
            IntersectionSidesGrid.Clear();

            var settings = VehicleSubsystem != null ? VehicleSubsystem.TrafficLightSettings : new TrafficLightSettings();
            string intersectionTag = settings.IntersectionTag.Name;
            var linked = new List<ZoneGraphLaneHandle>();

            for (int laneIndex = 0; laneIndex < storage.Lanes.Count; laneIndex++)
            {
                var lane = storage.Lanes[laneIndex];
                if (lane == null) continue;

                if (lane.HasTag(intersectionTag))
                {
                    // cpp:270-274 —— 撞到交叉口车道，登记交叉口
                    laneData.FindOrAddIntersection(lane.ZoneIndex);
                    continue;
                }

                // cpp:276-300 —— 普通车道：看它是否接入某个交叉口，接入则成为该交叉口的一侧
                storage.GetLinkedLanes(storage.MakeHandle(laneIndex), true, linked);
                int zoneIndex = -1;
                ZoneGraphLaneHandle firstIntersectionLane = ZoneGraphLaneHandle.Invalid;
                foreach (var linkedLane in linked)
                {
                    var linkedData = storage.GetLane(linkedLane);
                    if (linkedData == null) continue;
                    if (linkedData.HasTag(intersectionTag))
                    {
                        zoneIndex = linkedData.ZoneIndex;
                        if (!firstIntersectionLane.IsValid) firstIntersectionLane = linkedLane;
                        break;
                    }
                }

                if (zoneIndex < 0) continue;

                var intersection = laneData.FindOrAddIntersection(zoneIndex);
                var side = new TrafficIntersectionSide();

                foreach (var linkedLane in linked)
                {
                    var linkedData = storage.GetLane(linkedLane);
                    if (linkedData == null || !linkedData.HasTag(intersectionTag)) continue;

                    side.Lanes.Add(linkedLane);
                    // cpp:300-304 —— 朴素做法：取交叉口车道首点切线/位置作为进入方向与本侧位置
                    if (linkedData.Points.Count > 0)
                    {
                        side.SideLocation = linkedData.Points[0];
                        side.DirectionIntoIntersection = linkedData.Tangents.Count > 0
                            ? linkedData.Tangents[0]
                            : Vector3.Forward;
                    }
                }

                if (side.Lanes.Count == 0) continue;
                intersection.IntersectionSides.Add(side);

                // cpp:306-315 —— 登记到交叉口侧网格，供交通灯按位置找回自己管的侧
                int intersectionIndex = laneData.IndexOfIntersection(zoneIndex);
                var sideHandle = laneData.MakeSideHandle(intersectionIndex, intersection.IntersectionSides.Count - 1);
                IntersectionSidesGrid.Add(sideHandle, side.SideLocation);
            }

            // cpp:320-325 —— 所有交叉口侧按顺时针排序
            foreach (var trafficIntersection in laneData.Intersections)
            {
                trafficIntersection.SortSides();
                trafficIntersection.DrawDebug(storage);
            }
        }

        /// <summary>ZoneGraph 数据移除前处理。对应 UE5 PreZoneGraphDataRemoved（cpp:211-214，UE5 为空实现）。</summary>
        public virtual void PreZoneGraphDataRemoved(ZoneGraphDataHandle dataHandle)
        {
            var data = FindLaneData(dataHandle);
            if (data == null) return;
            data.Intersections.Clear();
            IntersectionSidesGrid.Clear();
            MassVehicleLog.Log($"PreZoneGraphDataRemoved: 数据 {dataHandle.Index} 的交叉口已卸载。");
        }

        // =====================================================================
        // 事件与交通灯联动
        // =====================================================================

        /// <summary>发出周期事件。对应 UE5 <c>ZoneGraphAnnotationSubsystem->SendEvent(PeriodEvent)</c>。</summary>
        public void SendPeriodEvent(TrafficPeriodEvent periodEvent)
        {
            if (periodEvent?.Period == null) return;
            _pendingEvents.Add(periodEvent);
        }

        /// <summary>
        /// 把待发事件落到车道标签并转发给注解组件。
        /// 对应 UE5 ZoneGraphAnnotationSubsystem 的 TickAnnotation + 注解组件 HandleEvents。
        /// </summary>
        private void ApplyPendingEvents()
        {
            if (_pendingEvents.Count == 0) return;
            var storage = VehicleSubsystem != null ? VehicleSubsystem.Storage : null;
            var settings = VehicleSubsystem != null ? VehicleSubsystem.TrafficLightSettings : new TrafficLightSettings();
            string closedTag = settings.ClosedTag.Name;

            foreach (var periodEvent in _pendingEvents)
            {
                if (periodEvent.Period?.Lanes == null || storage == null) continue;

                foreach (var laneHandle in periodEvent.Period.Lanes)
                {
                    var lane = storage.GetLane(laneHandle);
                    if (lane == null) continue;

                    if (periodEvent.State == ELaneState.Closed)
                    {
                        if (!lane.HasTag(closedTag)) lane.AddTag(closedTag);
                    }
                    else
                    {
                        lane.RemoveTag(closedTag);
                    }
                }

                SentEventCount++;
                for (int i = 0; i < _annotationHandlers.Count; i++)
                {
                    _annotationHandlers[i]?.HandlePeriodEvent(periodEvent);
                }
            }

            _pendingEvents.Clear();
        }

        /// <summary>注册交通灯。对应 UE5 RegisterTrafficLight（cpp:352-359）。</summary>
        public virtual void RegisterTrafficLight(TrafficLight trafficLight)
        {
            if (trafficLight == null) return;
            if (!RegisteredTrafficLights.Contains(trafficLight))
            {
                RegisteredTrafficLights.Add(trafficLight);
            }
            // UE5 在 BeginPlay 里先 CacheIntersectionSide 再注册；此处补一次，保证晚于
            // 交叉口构建才启用的交通灯也能拿到侧句柄。
            if (!trafficLight.CachedIntersectionSide.IsValid())
            {
                trafficLight.CacheIntersectionSide();
            }
        }

        /// <summary>注销交通灯（Actor 销毁/禁用时）。</summary>
        public virtual void UnregisterTrafficLight(TrafficLight trafficLight)
        {
            RegisteredTrafficLights.Remove(trafficLight);
        }

        /// <summary>
        /// 更新已注册的交通灯。对应 UE5 UpdateRegisteredTrafficLights（cpp:361-374）：
        /// 缓存侧与变更交叉口相同则回调 OnPeriodUpdated。
        /// </summary>
        public virtual void UpdateRegisteredTrafficLights(TrafficLightIntersection updatedIntersection)
        {
            if (updatedIntersection == null) return;

            for (int i = RegisteredTrafficLights.Count - 1; i >= 0; i--)
            {
                var trafficLight = RegisteredTrafficLights[i];
                if (trafficLight == null)
                {
                    RegisteredTrafficLights.RemoveAt(i);
                    continue;
                }

                var handle = trafficLight.CachedIntersectionSide;
                if (handle == null || !handle.IsValid()) continue;

                var intersection = handle.GetIntersection(this);
                if (ReferenceEquals(intersection, updatedIntersection))
                {
                    trafficLight.OnPeriodUpdated();
                }
            }
        }
    }
}
