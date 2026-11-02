using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.UnrealFramework;
using NarrativePro.Vehicles.Mass;
using NarrativePro.Vehicles.TrafficLights;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 交通模拟宿主 Script（Flax 侧的「Mass 模拟子系统 + 关卡生成器」替身）。
    /// <para>
    /// 为什么必须有它：UE5 侧 <c>UMassVehicleSubsystem</c> / <c>UTrafficLightSubsystem</c> /
    /// <c>UVehicleSpawnerSubsystem</c> / <c>UVehicleRepresentationSubsystem</c> 都是引擎世界子系统，
    /// 由引擎创建并每帧 Tick；车道来自关卡的 ZoneGraph 资产，车流来自关卡摆放的
    /// <c>AMassVehicleSpawner</c> / <c>AQuestRoadControls</c> / <c>ATrafficLight</c>。
    /// 本作的玩法场景不实例化任何插件 Actor（与行人客流同一前提，见
    /// <c>Spawners/NPCCrowdSpawner.cs</c> 与 <c>AI/Mass/Peds/MassPedSimulation.cs</c> 的先例），
    /// 所以「谁创建子系统、谁每帧驱动」必须由这个 Script 承担：
    /// 它持有全部载具子系统与处理器，并在 <see cref="OnUpdate"/> 里按 UE5 的执行顺序推进一帧。
    /// </para>
    /// <para>
    /// 每帧顺序（与 UE5 的 Mass 处理器组顺序对齐）：
    /// ① <see cref="VehicleObstacleProcessor"/>（障碍网格 Add/Move，含行人）；
    /// ② <see cref="TrafficLightSubsystem"/>.<c>Tick</c>（周期推进 + 提前关闭车道 + 交叉口占用判定）；
    /// ③ <see cref="VehicleMovementProcessor"/>（前车距离 → 期望速度 → 选道/切道 → 沿道推进）；
    /// ④ <see cref="VehicleRepresentationSubsystem"/>（LOD 升降 + Actor 申请/归还）；
    /// ⑤ <see cref="VehicleVisualizationProcessor"/>（速度/油门/泥污写回 Actor）；
    /// ⑥ <see cref="AutoDestroyProcessor"/>（远处/超时车流结算）。
    /// </para>
    /// </summary>
    public class VehicleTrafficSimulation : Script
    {
        /// <summary>单例（运行期宿主上唯一实例）。</summary>
        public static VehicleTrafficSimulation Instance { get; private set; }

        /// <summary>交通配置目录（留空则用默认目录）。</summary>
        [Serialize]
        public string ConfigDirectory = TrafficSimulationConfig.DefaultDirectory;

        /// <summary>是否在 OnUpdate 中自动完成首次构建。</summary>
        [Serialize]
        public bool bAutoStart = true;

        /// <summary>是否启用行人在障碍网格中的登记（关闭则载具只避让载具）。</summary>
        [Serialize]
        public bool bRegisterPedsAsObstacles = true;

        /// <summary>构建/启动的重试节奏（本地玩家可能晚于本脚本创建）。</summary>
        private const float RetryIntervalSeconds = 0.25f;
        private const int MaxRetryAttempts = 120;

        private float _retryTimer;
        private int _retryAttempts;
        private bool _bBuilt;
        private bool _bStopped;

        /// <summary>构建锚点（首次构建时解析到的观察者位置）。车道点、灯位、作用盒都以它为原点平移。</summary>
        private Vector3 _buildObserver;

        private readonly Dictionary<string, VehicleTrait> _traits = new Dictionary<string, VehicleTrait>(StringComparer.Ordinal);
        private readonly List<Actor> _ownedActors = new List<Actor>();
        private VehicleTrait _defaultTrait;

        // ===== 子系统与处理器（全部由本 Script 拥有并驱动） =====

        /// <summary>载具子系统（车道占用 + 障碍网格 + 车道存储）。</summary>
        public MassVehicleSubsystem VehicleSubsystem { get; private set; }

        /// <summary>交通灯子系统（周期状态机）。</summary>
        public TrafficLightSubsystem TrafficLights { get; private set; }

        /// <summary>Actor 生成子系统（池化/挂载乘员）。</summary>
        public VehicleSpawnerSubsystem ActorSpawner { get; private set; }

        /// <summary>表现子系统（LOD → Actor）。</summary>
        public VehicleRepresentationSubsystem Representation { get; private set; }

        /// <summary>移动处理器。</summary>
        public VehicleMovementProcessor MovementProcessor { get; private set; }

        /// <summary>障碍处理器。</summary>
        public VehicleObstacleProcessor ObstacleProcessor { get; private set; }

        /// <summary>初始化处理器。</summary>
        public InitVehicleProcessor InitProcessor { get; private set; }

        /// <summary>自动销毁处理器。</summary>
        public AutoDestroyProcessor AutoDestroy { get; private set; }

        /// <summary>可视化同步处理器。</summary>
        public VehicleVisualizationProcessor VisualizationProcessor { get; private set; }

        /// <summary>生成点生成器。</summary>
        public RoadControlsSpawnPointsGenerator SpawnPointGenerator { get; private set; }

        /// <summary>本模拟创建的生成器脚本。</summary>
        public List<MassVehicleSpawner> Spawners { get; } = new List<MassVehicleSpawner>();

        /// <summary>本模拟创建的交通灯脚本。</summary>
        public List<TrafficLight> TrafficLightScripts { get; } = new List<TrafficLight>();

        /// <summary>本模拟创建的道路控制器脚本。</summary>
        public List<QuestRoadControls> RoadControlsScripts { get; } = new List<QuestRoadControls>();

        /// <summary>默认特质（供初始化处理器兜底读取）。</summary>
        public VehicleTrait Trait => _defaultTrait;

        /// <summary>是否已完成首次构建并开始逐帧模拟。</summary>
        public bool IsBuilt => _bBuilt;

        /// <summary>已推进的帧数（诊断自证：>0 即证明本模拟真在 OnUpdate 里跑）。</summary>
        public int UpdateCount { get; private set; }

        /// <summary>最近一次阶段（诊断/自证用）。</summary>
        public string LastPhase { get; private set; } = "等待本地玩家/车道配置";

        // =====================================================================
        // 引导入口
        // =====================================================================

        /// <summary>
        /// 供运行期引导调用：把交通模拟挂到宿主 Actor 上（幂等）。
        /// 对应 UE5「子系统随世界自动创建 + 关卡摆放生成器」的替代入口。
        /// </summary>
        /// <param name="host">运行期宿主 Actor。</param>
        /// <returns>挂载成功返回脚本实例，否则 null。</returns>
        public static VehicleTrafficSimulation AttachToRuntimeHost(Actor host)
        {
            if (host == null) return null;

            var existing = host.GetScript<VehicleTrafficSimulation>();
            if (existing != null) return existing;

            var simulation = host.AddScript<VehicleTrafficSimulation>();
            if (simulation == null)
            {
                NarrativeLog.LogError("[Vehicles] VehicleTrafficSimulation 挂载失败，车流/交通灯不可达。");
                return null;
            }
            return simulation;
        }

        /// <summary>磁盘上是否存在可用车道配置（引导侧门禁，不触发完整构建）。</summary>
        public static bool HasConfiguration()
        {
            return TrafficSimulationConfig.HasAny();
        }

        /// <summary>
        /// 让本地玩家上最近一辆有空位的车。
        /// 这是「交互/输入侧」唯一需要调用车流层的入口（对应 UE5 玩家直接走进车按 E 上车，
        /// 由载具蓝图上的 Interactable 槽位提供；Flax 侧槽位由本插件生成，交互侧只需这一行）。
        /// </summary>
        /// <param name="maxDistance">搜索半径（世界单位）。</param>
        /// <returns>是否成功上车。</returns>
        public static bool TryMountLocalPlayerNearestVehicle(float maxDistance = 400f)
        {
            var simulation = Instance;
            NarrativeCharacter player = ResolveLocalPlayerCharacter();
            if (simulation == null || player?.Actor == null) return false;

            // 缺陷5（IsOccupyingInteractable 的消费方）：已在别的交互槽位上（占着座位/椅子）不再二次登车 ——
            // UE5 一个交互者同一时刻只能占用一个交互槽。
            if (NarrativePro.Interaction.InteractionFunctionLibrary.IsOccupyingInteractable(player.Actor)) return false;

            TrafficVehicleActor best = null;
            float bestDistance = maxDistance;
            foreach (var vehicleActor in TrafficVehicleActor.ActiveVehicles)
            {
                if (vehicleActor == null || vehicleActor.Actor == null) continue;
                if (!vehicleActor.HasFreeSeat()) continue;

                // 槽位层可用性复核（交互槽是 UE5 语义权威；_occupants 只是 Actor 侧视图）
                if (!NarrativePro.Interaction.InteractionFunctionLibrary.HasFreeSlot(vehicleActor.GetMountComponent(), false)) continue;

                float distance = Vector3.Distance(player.Actor.Position, vehicleActor.Actor.Position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = vehicleActor;
                }
            }

            if (best == null) return false;
            return best.TryMountCharacter(player);
        }

        /// <summary>
        /// 让本地玩家从所乘载具下车（TryMountLocalPlayerNearestVehicle 的对称出口，缺陷2）。
        /// 对应 UE5 UMountComponent 卸载流：座位释放、碰撞恢复、移动模式回 Walking、
        /// 按座位出口变换落位（见 TrafficVehicleActor.TryDismountCharacter）。
        /// </summary>
        /// <returns>是否成功下车。</returns>
        public static bool TryDismountLocalPlayerVehicle()
        {
            var player = ResolveLocalPlayerCharacter();
            if (player == null) return false;

            foreach (var vehicleActor in TrafficVehicleActor.ActiveVehicles)
            {
                if (vehicleActor == null) continue;
                var occupants = vehicleActor.Occupants;
                for (int i = 0; i < occupants.Count; i++)
                {
                    var o = occupants[i];
                    if (o == player || (o != null && o.Actor != null && o.Actor == player.Actor))
                    {
                        return vehicleActor.TryDismountCharacter(player);
                    }
                }
            }
            return false;
        }

        private static NarrativeCharacter ResolveLocalPlayerCharacter()
        {
            var player = Level.FindScript<NarrativePlayerCharacter>();
            if (player != null) return player;

            return Level.FindScript<NarrativeCharacter>();
        }

        // =====================================================================
        // 生命周期
        // =====================================================================

        public override void OnEnable()
        {
            base.OnEnable();
            if (Instance != null && Instance != this)
            {
                NarrativeLog.LogWarning("已存在 VehicleTrafficSimulation，当前实例不再生效");
                return;
            }
            Instance = this;
            _bBuilt = false;
            _bStopped = false;
            _retryAttempts = 0;
            _retryTimer = RetryIntervalSeconds; // 首轮立即尝试
        }

        public override void OnDisable()
        {
            Teardown();
            if (Instance == this) Instance = null;
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            if (!bAutoStart || _bStopped) return;

            if (!_bBuilt)
            {
                _retryTimer += (float)Time.DeltaTime;
                if (_retryTimer < RetryIntervalSeconds) return;
                _retryTimer = 0f;

                if (_retryAttempts++ >= MaxRetryAttempts)
                {
                    if (_retryAttempts == MaxRetryAttempts)
                    {
                        NarrativeLog.LogWarning($"[Vehicles] {MaxRetryAttempts} 次尝试后仍未完成交通构建（缺本地玩家或缺车道配置），车流不启用。");
                        _bStopped = true;
                    }
                    return;
                }

                TryBuild();
                return;
            }

            TickSimulation((float)Time.DeltaTime);
        }

        // =====================================================================
        // 构建
        // =====================================================================

        /// <summary>读取配置、构建车道存储与全部子系统（幂等）。</summary>
        public bool TryBuild()
        {
            if (_bBuilt) return true;

            Vector3 observer = ResolveObserverLocation();
            if (observer == Vector3.Zero && Level.FindScript<NarrativePlayerCharacter>() == null)
            {
                LastPhase = "等待本地玩家就绪";
                return false;
            }

            var configs = TrafficSimulationConfig.LoadAll();
            if (configs.Count == 0)
            {
                LastPhase = "无交通配置";
                return false;
            }

            try
            {
                BuildSubsystemsWithConfigs(configs, observer);
            }
            catch (Exception ex)
            {
                LastPhase = "构建失败";
                NarrativeLog.LogError("[Vehicles] 交通模拟构建异常：" + ex);
                return false;
            }

            _bBuilt = true;
            LastPhase = $"车道 {VehicleSubsystem.Storage.Lanes.Count} 条 / 生成器 {Spawners.Count} 个 / 灯 {TrafficLightScripts.Count} 盏";
            NarrativeLog.Log($"[Vehicles] 交通模拟已启动：{LastPhase}（更新计数从本帧开始累加）。");
            return true;
        }

        private void BuildSubsystemsWithConfigs(List<TrafficSimulationJson> configs, Vector3 observer)
        {
            _buildObserver = observer;

            // ① 载具子系统 + 车道存储
            VehicleSubsystem = new MassVehicleSubsystem();
            VehicleSubsystem.Storage = new ZoneGraphStorage
            {
                DataHandle = new ZoneGraphDataHandle { Index = 0 },
            };
            VehicleSubsystem.TrafficLightSettings = new TrafficLightSettings();
            BuildRoadNetwork(configs, observer, VehicleSubsystem);
            VehicleSubsystem.Initialize();

            // ② 模板/特质
            BuildTraits(configs);

            // ③ 其余子系统与处理器（顺序对应 UE5 的依赖初始化）
            ActorSpawner = new VehicleSpawnerSubsystem(VehicleSubsystem);
            ActorSpawner.Initialize();
            Representation = new VehicleRepresentationSubsystem(VehicleSubsystem, ActorSpawner);
            Representation.Initialize();
            TrafficLights = new TrafficLightSubsystem();
            TrafficLights.Initialize(VehicleSubsystem);

            MovementProcessor = new VehicleMovementProcessor(VehicleSubsystem, _defaultTrait);
            ObstacleProcessor = new VehicleObstacleProcessor(VehicleSubsystem);
            ObstacleProcessor.ObstacleTrait.bRegisterPedsAsObstacles = bRegisterPedsAsObstacles;
            InitProcessor = new InitVehicleProcessor(VehicleSubsystem);
            VisualizationProcessor = new VehicleVisualizationProcessor();
            AutoDestroy = new AutoDestroyProcessor(VehicleSubsystem, Representation);
            SpawnPointGenerator = new RoadControlsSpawnPointsGenerator(VehicleSubsystem)
            {
                RandomSelectionSeed = Environment.TickCount,
            };

            // ④ 生成器 / 道路控制器 / 交通灯脚本（对应 UE5 关卡摆放的那些 Actor）
            foreach (var config in configs)
            {
                CreateSpawnersAndRoadControls(config);
                CreateTrafficLights(config);
            }
        }

        private void BuildRoadNetwork(List<TrafficSimulationJson> configs, Vector3 observer, MassVehicleSubsystem subsystem)
        {
            var storage = subsystem.Storage;
            var settings = subsystem.TrafficLightSettings;
            var nameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var config in configs)
            {
                if (config.ObstacleCellSize > 1f)
                {
                    subsystem.VehicleObstacles.CellSize = config.ObstacleCellSize;
                }
                if (!string.IsNullOrEmpty(config.IntersectionTagName)) settings.IntersectionTag = new ZoneGraphTag(config.IntersectionTagName);
                if (!string.IsNullOrEmpty(config.ClosedTagName)) settings.ClosedTag = new ZoneGraphTag(config.ClosedTagName);
                if (config.LaneCloseAdvanceTime > 0f) settings.LaneCloseAdvanceTime = config.LaneCloseAdvanceTime;
                settings.SimplePeriodDuration = Mathf.Max(1f, config.SimplePeriodDuration);
                settings.StraightPeriodDuration = Mathf.Max(1f, config.StraightPeriodDuration);
                settings.AllDirectionsPeriodDuration = Mathf.Max(1f, config.AllDirectionsPeriodDuration);
                settings.PeriodDurationScale = config.PeriodDurationScale > 0f ? config.PeriodDurationScale : 1f;

                Vector3 offset = config.bFollowObserver ? observer : Vector3.Zero;

                foreach (var laneJson in config.Lanes)
                {
                    if (laneJson == null || laneJson.Points == null || laneJson.Points.Count < 2) continue;

                    var lane = new TrafficLaneData
                    {
                        ZoneIndex = laneJson.ZoneIndex,
                        Name = laneJson.Name,
                        bLooping = laneJson.bLooping,
                    };
                    foreach (var point in laneJson.Points)
                    {
                        lane.Points.Add(point + offset);
                    }
                    if (laneJson.Tags != null)
                    {
                        foreach (var tag in laneJson.Tags) lane.AddTag(tag);
                    }

                    int index = storage.Lanes.Count;
                    lane.Index = index;
                    storage.Lanes.Add(lane);
                    if (!string.IsNullOrEmpty(lane.Name) && !nameToIndex.ContainsKey(lane.Name))
                    {
                        nameToIndex[lane.Name] = index;
                    }
                }
            }

            // 链接成网（对应 UE5 ZoneGraph 烘焙的 lane links）
            foreach (var config in configs)
            {
                if (config.Lanes == null) continue;
                foreach (var laneJson in config.Lanes)
                {
                    if (laneJson == null || string.IsNullOrEmpty(laneJson.Name)) continue;
                    if (!nameToIndex.TryGetValue(laneJson.Name, out int self)) continue;
                    if (laneJson.NextLanes == null) continue;

                    var lane = storage.Lanes[self];
                    foreach (var nextName in laneJson.NextLanes)
                    {
                        if (string.IsNullOrEmpty(nextName)) continue;
                        if (!nameToIndex.TryGetValue(nextName, out int next))
                        {
                            NarrativeLog.LogError($"[Vehicles] 车道 '{laneJson.Name}' 的出向链接指向不存在的车道 '{nextName}'。");
                            continue;
                        }
                        if (!lane.NextLanes.Contains(next))
                        {
                            lane.NextLanes.Add(next);
                            storage.Lanes[next].PreviousLanes.Add(self);
                        }
                    }
                }
            }

            storage.Build();

            if (storage.Lanes.Count == 0)
            {
                NarrativeLog.LogError("[Vehicles] 配置里没有任何可用车道（每条车道至少需要 2 个点）。");
            }
        }

        private void BuildTraits(List<TrafficSimulationJson> configs)
        {
            _traits.Clear();
            foreach (var config in configs)
            {
                if (config.Templates == null) continue;
                foreach (var template in config.Templates)
                {
                    if (template == null) continue;
                    var trait = template.ToTrait();
                    string name = string.IsNullOrEmpty(trait.TemplateName) ? "TrafficVehicle" : trait.TemplateName;
                    if (!_traits.ContainsKey(name)) _traits[name] = trait;
                }
            }

            _defaultTrait = null;
            foreach (var trait in _traits.Values)
            {
                _defaultTrait = trait;
                break;
            }
            _defaultTrait ??= new VehicleTrait();

            if (_traits.Count == 0)
            {
                NarrativeLog.LogWarning("[Vehicles] 配置未提供任何载具模板，使用内置默认模板。");
            }
        }

        private void CreateSpawnersAndRoadControls(TrafficSimulationJson config)
        {
            if (config.Spawners == null) return;

            foreach (var spawnerJson in config.Spawners)
            {
                if (spawnerJson == null) continue;

                string templateName = string.IsNullOrEmpty(spawnerJson.TemplateName) ? "TrafficVehicle" : spawnerJson.TemplateName;
                if (!_traits.ContainsKey(templateName))
                {
                    NarrativeLog.LogWarning($"[Vehicles] 生成器 '{spawnerJson.Name}' 引用了不存在的模板 '{templateName}'，已改用默认模板。");
                    templateName = _defaultTrait.TemplateName;
                }

                var spawnerActor = CreateOwnedChildActor($"VehicleSpawner_{spawnerJson.Name}");
                if (spawnerActor == null) continue;

                var spawner = spawnerActor.AddScript<MassVehicleSpawner>();
                if (spawner == null) continue;
                spawner.TemplateName = templateName;
                spawner.SpawnCount = Mathf.Clamp(spawnerJson.SpawnCount, 0, 8);
                spawner.RefillIntervalSeconds = Mathf.Max(0.25f, spawnerJson.RefillIntervalSeconds);
                spawner.RandomSelectionSeed = spawnerJson.RandomSelectionSeed;

                // 交叉口注解（对应 UE5 AMassVehicleSpawner 构造函数里的注解组件）
                var annotations = spawnerActor.AddScript<TrafficIntersectionAnnotations>();
                spawner.IntersectionAnnotations = annotations;

                Spawners.Add(spawner);

                // 道路控制器：先关掉自动激活，配置完成后再显式激活（AddScript 会立即执行 OnEnable）
                var controlsActor = CreateOwnedChildActor($"RoadControls_{spawnerJson.Name}");
                if (controlsActor == null) continue;

                var controls = controlsActor.AddScript<QuestRoadControls>();
                if (controls == null) continue;
                controls.bAutoActivate = false;
                // 作用盒中心以「构建锚点（= 车道原点）」为基准，故这里存的是相对中心的偏移，
                // QuestRoadControls.ContainsPoint 会再加上自身 Actor 位置（同等于构建锚点）。
                controls.BoxCenter = spawnerJson.BoxCenter;
                controls.BoxExtent = spawnerJson.BoxExtent;
                controls.NewSpawnCount = Mathf.Clamp(spawnerJson.SpawnCount, 0, 8);
                controls.bAutoActivate = spawnerJson.bAutoActivateRoadControls;
                if (spawnerJson.TagsToAdd != null)
                {
                    foreach (var tag in spawnerJson.TagsToAdd) controls.TagsToAdd.Add(tag);
                }
                controls.BindSpawner(spawner);
                RoadControlsScripts.Add(controls);

                if (controls.IsActive()) controls.SetActive(false);
                if (controls.bAutoActivate) controls.SetActive(true);
            }
        }

        private void CreateTrafficLights(TrafficSimulationJson config)
        {
            if (config.TrafficLights == null) return;

            foreach (var lightJson in config.TrafficLights)
            {
                if (lightJson == null) continue;

                var lightActor = CreateOwnedChildActor($"TrafficLight_{lightJson.Name}");
                if (lightActor == null) continue;

                var light = lightActor.AddScript<TrafficLight>();
                if (light == null) continue;

                light.SideLocation = lightJson.SideLocation;
                light.QueryExtent = lightJson.QueryExtent;
                light.DefaultRule = (EIntersectionSideRule)(byte)Mathf.Clamp(lightJson.DefaultRule, 0, 14);
                light.CacheIntersectionSide();
                TrafficLightScripts.Add(light);
            }
        }

        private Actor CreateOwnedChildActor(string name)
        {
            if (Actor == null) return null;

            var actor = Actor.AddChild<EmptyActor>();
            if (actor == null)
            {
                NarrativeLog.LogError($"[Vehicles] 子 Actor 创建失败：{name}");
                return null;
            }
            actor.Name = name;
            // 车道点已按构建时的观察者位置平移，因此这些承载脚本的 Actor 也必须落在同一锚点上，
            // 否则交通灯的 SideLocation / 道路控制器的作用盒会与车道坐标系错位。
            actor.Position = _buildObserver;
            _ownedActors.Add(actor);
            return actor;
        }

        // =====================================================================
        // 每帧
        // =====================================================================

        /// <summary>推进一帧。对应 UE5 Mass 模拟子系统的一个 execution group 序列。</summary>
        private void TickSimulation(float deltaTime)
        {
            if (VehicleSubsystem == null || deltaTime <= 0f) return;

            deltaTime = Mathf.Min(deltaTime, 0.1f); // 卡顿帧保护（与行人模拟同一处理）
            VehicleSubsystem.ObserverLocation = ResolveObserverLocation();

            // ① 障碍网格（载具 + 行人）
            if (ObstacleProcessor != null)
            {
                ObstacleProcessor.UpdateTrafficObstacles(VehicleSubsystem.Vehicles, deltaTime);
            }

            // ② 交通灯周期推进（会写入车道 Closed 标签）
            TrafficLights?.Tick(deltaTime);

            // ③ 移动：前车/来车/关闭车道 → 期望速度 → 选道 → 沿道推进
            MovementProcessor?.UpdateTraffic(VehicleSubsystem.Vehicles, deltaTime);

            // ④ LOD 与 Actor 申请/归还
            Representation?.UpdateRepresentation(VehicleSubsystem.Vehicles);

            // ⑤ 可视数据同步
            VisualizationProcessor?.UpdateVisualization(VehicleSubsystem.Vehicles, deltaTime);

            // ⑥ 远处/超时车流销毁
            AutoDestroy?.Execute();

            UpdateCount++;
        }

        /// <summary>观察者位置：本地玩家 → 任意 NarrativeCharacter → 宿主 Actor。</summary>
        public Vector3 ResolveObserverLocation()
        {
            var player = Level.FindScript<NarrativePlayerCharacter>();
            if (player?.Actor != null) return player.Actor.Position;

            var character = Level.FindScript<NarrativeCharacter>();
            if (character?.Actor != null) return character.Actor.Position;

            return Actor != null ? Actor.Position : Vector3.Zero;
        }

        // =====================================================================
        // 供生成器调用
        // =====================================================================

        /// <summary>按模板名取特质。</summary>
        public VehicleTrait GetTrait(string templateName)
        {
            if (string.IsNullOrEmpty(templateName)) return _defaultTrait;
            if (_traits.TryGetValue(templateName, out var trait) && trait != null) return trait;
            return _defaultTrait;
        }

        /// <summary>生成候选出生点。对应 UE5 MassSpawner 的 SpawnDataGenerator。</summary>
        public List<RoadControlsSpawnPointsGenerator.SpawnPoint> GenerateSpawnPoints(int count, int seed)
        {
            if (SpawnPointGenerator == null) return new List<RoadControlsSpawnPointsGenerator.SpawnPoint>();
            if (seed != 0) SpawnPointGenerator.RandomSelectionSeed = seed;
            return SpawnPointGenerator.Generate(count);
        }

        /// <summary>初始化新生成的实体（落车道 + 种子 + 进占用表）。</summary>
        public bool InitializeVehicle(TrafficVehicle vehicle, ZoneGraphLaneHandle preferredLane,
                                       float distanceAlongLane, int requestedSeed)
        {
            return InitProcessor != null && InitProcessor.InitializeVehicle(vehicle, preferredLane, distanceAlongLane, requestedSeed);
        }

        /// <summary>销毁一个实体（生成器停用/关模拟时）。</summary>
        public bool DespawnVehicle(MassEntityHandle handle)
        {
            if (VehicleSubsystem == null) return false;
            if (!VehicleSubsystem.TryGetVehicle(handle, out var vehicle)) return false;
            Representation?.ReleaseActor(vehicle);
            ObstacleProcessor?.DestroyObstacle(vehicle);
            return VehicleSubsystem.RemoveVehicle(vehicle.Handle);
        }

        /// <summary>当前存活载具数。</summary>
        public int AliveVehicleCount => VehicleSubsystem?.Vehicles.Count ?? 0;

        // =====================================================================
        // 收尾
        // =====================================================================

        /// <summary>拆除全部子系统与本体创建的 Actor（顺序：先脚本后 Actor）。</summary>
        public void Teardown()
        {
            _bBuilt = false;
            _bStopped = true;

            for (int i = 0; i < _ownedActors.Count; i++)
            {
                var actor = _ownedActors[i];
                if (actor != null) FlaxEngine.Object.Destroy(actor);
            }
            _ownedActors.Clear();
            Spawners.Clear();
            TrafficLightScripts.Clear();
            RoadControlsScripts.Clear();
            _traits.Clear();
            _defaultTrait = null;

            Representation?.Shutdown();
            TrafficLights?.Shutdown();
            ActorSpawner?.Shutdown();
            ObstacleProcessor = null;
            MovementProcessor = null;
            InitProcessor = null;
            AutoDestroy = null;
            VisualizationProcessor = null;
            SpawnPointGenerator = null;

            VehicleSubsystem?.Shutdown();
            LastPhase = "已拆除";
        }
    }
}
