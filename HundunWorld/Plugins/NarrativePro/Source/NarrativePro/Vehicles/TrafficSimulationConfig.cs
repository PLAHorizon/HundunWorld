using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Data;
using NarrativePro.Vehicles.Mass;

namespace NarrativePro.Vehicles
{
    /// <summary>
    /// 车道描述（磁盘形态）。对应 UE5 由 ZoneGraph 生成资产提供的车道 + 链接 + 标签。
    /// </summary>
    [Serializable]
    public class TrafficLaneJson
    {
        /// <summary>车道名，供 <see cref="NextLanes"/> 与生成器引用。</summary>
        public string Name = "";

        /// <summary>所属路口/路段分区号。交叉口内车道必须给同一个 ZoneIndex。</summary>
        public int ZoneIndex = -1;

        /// <summary>折线点（世界坐标；<see cref="TrafficSimulationJson.bFollowObserver"/> 为真时按观察点偏移）。</summary>
        public List<Vector3> Points = new List<Vector3>();

        /// <summary>车道标签。对应 UE5 FZoneLaneData.Tags（如 "Intersection"）。</summary>
        public List<string> Tags = new List<string>();

        /// <summary>出向链接的车道名。对应 UE5 zonegraph 的 lane links。</summary>
        public List<string> NextLanes = new List<string>();

        /// <summary>是否环形车道（终点接回起点）。</summary>
        public bool bLooping;
    }

    /// <summary>
    /// 载具模板（磁盘形态）。对应 UE5 的 <c>UVehicleTrait</c> 片段集合 + 载具蓝图。
    /// </summary>
    [Serializable]
    public class TrafficVehicleTemplateJson
    {
        /// <summary>模板名（<see cref="TrafficSpawnerJson.TemplateName"/> 引用它）。</summary>
        public string Name = "TrafficVehicle";

        /// <summary>期望最大速度（世界单位/秒）。对应 UE5 VehicleMaxSpeed。</summary>
        public float MaxSpeed = 320f;

        /// <summary>车身半径。对应 UE5 FAgentRadiusFragment::Radius。</summary>
        public float Radius = 60f;

        /// <summary>座位数。对应 UE5 载具蓝图上的交互槽位数。</summary>
        public int SeatCount = 1;

        /// <summary>与前车保持的最小距离。对应 UE5 MinimumDistanceToNext。</summary>
        public float MinimumDistanceToNext = 220f;

        /// <summary>前车接近时的刹车起始距离。对应 UE5 BrakingDistanceFromNext。</summary>
        public float BrakingDistanceFromNext = 600f;

        /// <summary>与障碍（行人）保持的最小距离。对应 UE5 MinimumDistanceToObstacle。</summary>
        public float MinimumDistanceToObstacle = 120f;

        /// <summary>障碍接近时的刹车起始距离。对应 UE5 BrakingDistanceFromObstacle。</summary>
        public float BrakingDistanceFromObstacle = 450f;

        /// <summary>关闭车道/交叉口前的刹车距离。对应 UE5 ClosedLaneBrakingDistance。</summary>
        public float ClosedLaneBrakingDistance = 420f;

        /// <summary>障碍搜索半径。对应 UE5 ObstacleSearchRadius。</summary>
        public float ObstacleSearchRadius = 900f;

        /// <summary>找最近车道的半径。对应 UE5 LaneSearchRadius。</summary>
        public float LaneSearchRadius = 700f;

        /// <summary>油门 PID。对应 UE5 FPIDSettings（ThrottleSettings）。</summary>
        public float ThrottleProportional = 1.2f;
        public float ThrottleIntegral = 0.02f;
        public float ThrottleDerivative = 0.25f;

        /// <summary>转向 PID（驱动姿态收敛速率）。对应 UE5 FPIDSettings（SteeringSettings）。</summary>
        public float SteeringProportional = 4f;
        public float SteeringIntegral = 0f;
        public float SteeringDerivative = 0.5f;

        /// <summary>转向前瞻距离。对应 UE5 LookAheadDistance。</summary>
        public float LookAheadDistance = 400f;

        /// <summary>乘客 NPC 定义路径（相对 Content，交给 <see cref="NarrativePro.AI.NPCDefinition"/> 解析）。</summary>
        public List<string> PassengerDefinitionPaths = new List<string>();

        /// <summary>1 名乘客的权重（对应 PassengerCountProbability，本模板只支持 0/1 两档）。</summary>
        public float PassengerOneWeight = 0.6f;

        /// <summary>把磁盘模板变成运行期特质。</summary>
        public VehicleTrait ToTrait()
        {
            var trait = new VehicleTrait
            {
                TemplateName = string.IsNullOrEmpty(Name) ? "TrafficVehicle" : Name,
                Radius = new AgentRadiusFragment { Radius = Mathf.Max(10f, Radius) },
                SeatCount = Mathf.Clamp(SeatCount, 1, 8),
            };

            var settings = trait.VehicleSettingsFragment;
            settings.VehicleMaxSpeed = Mathf.Max(10f, MaxSpeed);
            settings.MinimumDistanceToNext = MinimumDistanceToNext;
            settings.BrakingDistanceFromNext = BrakingDistanceFromNext;
            settings.NextVehicleAvoidanceBrakingPower = 1.2f;
            settings.MinimumDistanceToObstacle = MinimumDistanceToObstacle;
            settings.BrakingDistanceFromObstacle = BrakingDistanceFromObstacle;
            settings.ObstacleAvoidanceBrakingPower = 1.2f;
            settings.ClosedLaneMinDistance = Mathf.Max(40f, MinimumDistanceToObstacle * 0.5f);
            settings.ClosedLaneBrakingDistance = ClosedLaneBrakingDistance;
            settings.ClosedLaneBrakingPower = 1.5f;
            settings.ObstacleSearchRadius = Mathf.Max(100f, ObstacleSearchRadius);
            settings.LaneSearchRadius = Mathf.Max(100f, LaneSearchRadius);

            trait.PIDControllerSettings.ThrottleSettings = new PIDSettings
            {
                ProportionalGain = ThrottleProportional,
                IntegralGain = ThrottleIntegral,
                DerivativeGain = ThrottleDerivative,
            };
            trait.PIDControllerSettings.SteeringSettings = new PIDSettings
            {
                ProportionalGain = SteeringProportional,
                IntegralGain = SteeringIntegral,
                DerivativeGain = SteeringDerivative,
            };
            trait.PIDControllerSettings.LookAheadDistance = Mathf.Max(50f, LookAheadDistance);

            // 乘客定义（对应 UE5 FPassengerSettingsFragment 的 UNPCDefinition 列表）。
            // 路径按 Content/NarrativePro/NPCs/<id>.json 的既有约定还原成定义 ID 再查库，
            // 与 NPCCrowdSpawner 走的是同一张 NarrativeNPCDatabase。
            if (PassengerDefinitionPaths != null && PassengerDefinitionPaths.Count > 0)
            {
                NarrativePro.Data.NarrativeNPCDatabase.Initialize();
                foreach (var path in PassengerDefinitionPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    string id = Path.GetFileNameWithoutExtension(path);
                    var definition = NarrativePro.Data.NarrativeNPCDatabase.Get(id);
                    if (definition != null)
                    {
                        trait.Passengers.PassengerDefinitions.Add(definition);
                    }
                    else
                    {
                        NarrativeLog.LogWarning($"[Vehicles] 模板 {Name} 的乘客定义无法解析：'{path}'（id '{id}'，乘客不生成，载具仍上路）。");
                    }
                }

                if (trait.Passengers.PassengerDefinitions.Count > 0)
                {
                    float one = Mathf.Clamp(PassengerOneWeight, 0f, 1f);
                    trait.Passengers.PassengerCountProbability = new List<PassengerWeightedProbability>
                    {
                        new PassengerWeightedProbability(1f - one, 0),
                        new PassengerWeightedProbability(one, 1),
                    };
                }
            }

            trait.ApplyPIDDefaults();
            return trait;
        }
    }

    /// <summary>
    /// 生成器描述（磁盘形态）。对应 UE5 关卡里摆放的 <c>AMassVehicleSpawner</c>
    /// 与 <c>AQuestRoadControls</c>（两者在 Flax 都不可能有场景实例，故由 JSON 起）。
    /// </summary>
    [Serializable]
    public class TrafficSpawnerJson
    {
        /// <summary>生成器名（日志/Actor 名）。</summary>
        public string Name = "VehicleSpawner";

        /// <summary>使用的模板名。</summary>
        public string TemplateName = "TrafficVehicle";

        /// <summary>维持的存活车数（本作刻意保持 2-4，目标是可达性而非城市仿真）。</summary>
        public int SpawnCount = 2;

        /// <summary>补员节奏（秒）。</summary>
        public float RefillIntervalSeconds = 3f;

        /// <summary>作用盒中心（相对生成器 Actor）。对应 UE5 的 BoxComponent。</summary>
        public Vector3 BoxCenter = Vector3.Zero;

        /// <summary>作用盒半边长（三轴都必须 &gt; 0，否则该轴方向上任何点都判不到盒内）。</summary>
        public Vector3 BoxExtent = new Vector3(2400f, 600f, 2400f);

        /// <summary>是否自动激活道路控制器。对应 UE5 bAutoActivate。</summary>
        public bool bAutoActivateRoadControls = true;

        /// <summary>激活时写入车道的标签。对应 UE5 TagsToAdd。</summary>
        public List<string> TagsToAdd = new List<string>();

        /// <summary>随机选择种子。</summary>
        public int RandomSelectionSeed = 0;
    }

    /// <summary>
    /// 交通灯描述（磁盘形态）。对应 UE5 关卡里摆放的 <c>ATrafficLight</c>。
    /// </summary>
    [Serializable]
    public class TrafficLightJson
    {
        /// <summary>灯名。</summary>
        public string Name = "TrafficLight";

        /// <summary>灯所站的交叉口侧位置（世界坐标，随观察点偏移）。</summary>
        public Vector3 SideLocation = Vector3.Zero;

        /// <summary>找回交叉口侧的查询范围。</summary>
        public Vector3 QueryExtent = new Vector3(3000f);

        /// <summary>
        /// 本灯默认放行的方向规则。数值与 EIntersectionSideRule 一致：
        /// 2=右转、4=直行、8=左转、14=全放行。
        /// </summary>
        public int DefaultRule = 4;
    }

    /// <summary>
    /// 一份交通配置。对应 UE5「关卡 + ZoneGraph 资产 + Mass 生成器」的组合。
    /// </summary>
    [Serializable]
    public class TrafficSimulationJson
    {
        /// <summary>配置名。</summary>
        public string Name = "";

        /// <summary>是否启用。</summary>
        public bool bEnabled = true;

        /// <summary>
        /// 车道点坐标是否按「首次构建时的观察者位置」平移。
        /// 与 <see cref="NarrativePro.Data.NpcCrowdConfigJson.bFollowLocalPlayer"/> 同一动机：
        /// 本作的玩法场景没有摆放任何插件 Actor，把车道锚到玩家附近才能被看见。
        /// </summary>
        public bool bFollowObserver = true;

        /// <summary>障碍哈希单元边长。</summary>
        public float ObstacleCellSize = 900f;

        /// <summary>交叉口标签名。对应 UE5 UTrafficLightSettings::IntersectionTag。</summary>
        public string IntersectionTagName = TrafficLightSettings.DefaultIntersectionTagName;

        /// <summary>关闭标签名。对应 UE5 UTrafficLightSettings::ClosedTag。</summary>
        public string ClosedTagName = TrafficLightSettings.DefaultClosedTagName;

        /// <summary>提前关闭时间（秒）。对应 UE5 LaneCloseAdvanceTime。</summary>
        public float LaneCloseAdvanceTime = 3f;

        /// <summary>周期时长缩放（对应 UE5 硬编码的 15s/10s，便于演示时加快切换）。</summary>
        public float PeriodDurationScale = 1f;

        /// <summary>非方形路口的每侧放行时长（UE5 硬编码 10s）。</summary>
        public float SimplePeriodDuration = 10f;

        /// <summary>方形路口的「直行+右转」时长 / 单侧全放行时长（UE5 硬编码 15s / 10s）。</summary>
        public float StraightPeriodDuration = 15f;
        public float AllDirectionsPeriodDuration = 10f;

        /// <summary>车道列表。</summary>
        public List<TrafficLaneJson> Lanes = new List<TrafficLaneJson>();

        /// <summary>载具模板列表。</summary>
        public List<TrafficVehicleTemplateJson> Templates = new List<TrafficVehicleTemplateJson>();

        /// <summary>生成器列表。</summary>
        public List<TrafficSpawnerJson> Spawners = new List<TrafficSpawnerJson>();

        /// <summary>交通灯列表。</summary>
        public List<TrafficLightJson> TrafficLights = new List<TrafficLightJson>();

        /// <summary>是否至少有一条可用车道。</summary>
        public bool HasLanes()
        {
            return Lanes != null && Lanes.Count > 0;
        }
    }

    /// <summary>
    /// JSON 驱动的交通配置加载器。磁盘资产：<c>Content/NarrativePro/Vehicles/*.json</c>。
    /// <para>
    /// 绑定模式与 <see cref="NarrativePro.Data.NarrativeCrowdConfig"/> 完全一致：
    /// <c>IncludeFields = true</c>（本项目的配置类是 public 字段而非属性）、
    /// <c>PropertyNameCaseInsensitive = true</c>（磁盘 JSON 用 camelCase、C# 用 PascalCase），
    /// 并允许注释与尾逗号 —— 这两项缺失曾让本项目两次出现「配置读不到但不报错」。
    /// </para>
    /// </summary>
    public static class TrafficSimulationConfig
    {
        /// <summary>交通配置目录（工程相对路径）。</summary>
        public const string DefaultDirectory = "Content/NarrativePro/Vehicles";

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        private static readonly List<TrafficSimulationJson> _configs = new List<TrafficSimulationJson>();
        private static bool _bLoaded;

        /// <summary>已加载的启用配置。</summary>
        public static IReadOnlyList<TrafficSimulationJson> Configs => _configs;

        /// <summary>是否存在任何可用配置（不触发磁盘读取时使用）。</summary>
        public static bool HasAny()
        {
            if (!_bLoaded) LoadAll();
            return _configs.Count > 0;
        }

        /// <summary>幂等加载目录内全部配置。</summary>
        public static List<TrafficSimulationJson> LoadAll(bool force = false)
        {
            if (_bLoaded && !force) return _configs;
            _bLoaded = true;
            if (force) _configs.Clear();

            string resolved = NarrativeDataPaths.ResolveDirectory(DefaultDirectory);
            if (!Directory.Exists(resolved))
            {
                NarrativeLog.Log($"[Vehicles] 交通配置目录不存在: '{resolved}'（配置值: {DefaultDirectory}）——不生成车流。");
                return _configs;
            }

            foreach (var file in Directory.GetFiles(resolved, "*.json"))
            {
                var config = LoadFile(file);
                if (config == null) continue;
                if (!config.bEnabled)
                {
                    NarrativeLog.Log($"[Vehicles] 交通配置 '{Path.GetFileName(file)}' 标记 bEnabled=false，跳过。");
                    continue;
                }
                if (!config.HasLanes())
                {
                    NarrativeLog.LogWarning($"[Vehicles] 交通配置 '{Path.GetFileName(file)}' 没有任何车道，跳过。");
                    continue;
                }
                _configs.Add(config);
            }

            int lanes = 0, spawners = 0, lights = 0;
            foreach (var config in _configs)
            {
                lanes += config.Lanes.Count;
                spawners += config.Spawners?.Count ?? 0;
                lights += config.TrafficLights?.Count ?? 0;
            }
            NarrativeLog.Log($"[Vehicles] TrafficSimulationConfig: 配置 {_configs.Count} 份、车道 {lanes} 条、" +
                             $"生成器 {spawners} 个、交通灯 {lights} 盏（目录 {resolved}）。");
            return _configs;
        }

        /// <summary>加载单个文件（解析失败返回 null）。</summary>
        public static TrafficSimulationJson LoadFile(string jsonFilePath)
        {
            string resolved = NarrativeDataPaths.Resolve(jsonFilePath);
            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
            {
                NarrativeLog.LogError($"[Vehicles] 交通配置 JSON 缺失: '{resolved}'（原始路径: {jsonFilePath}）。");
                return null;
            }

            try
            {
                var config = JsonSerializer.Deserialize<TrafficSimulationJson>(File.ReadAllText(resolved), Options);
                if (config != null && string.IsNullOrEmpty(config.Name))
                {
                    config.Name = Path.GetFileNameWithoutExtension(resolved);
                }
                return config;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[Vehicles] 交通配置 '{resolved}' 解析失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>清缓存（切场景/热重载）。</summary>
        public static void ClearCache()
        {
            _configs.Clear();
            _bLoaded = false;
        }
    }
}
