using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;

namespace HundunWorld.Game
{
    /// <summary>
    /// 碰撞检测性能等级枚举
    /// </summary>
    public enum CollisionDetectionLevel
    {
        /// <summary>低性能等级（1射线）</summary>
        Low = 1,
        
        /// <summary>中性能等级（5射线）</summary>
        Medium = 5,
        
        /// <summary>高性能等级（9射线）</summary>
        High = 9
    }
    
    /// <summary>
    /// 异步检测结果
    /// </summary>
    [Serializable]
    public class AsyncDetectionResult
    {
        /// <summary>是否检测到地面</summary>
        public bool GroundDetected;
        
        /// <summary>地面高度</summary>
        public float GroundHeight;
        
        /// <summary>检测完成时间</summary>
        public float DetectionTime;
        
        /// <summary>是否有效（未超时）</summary>
        public bool IsValid(float currentTime, float timeout = 0.2f)
        {
            return (currentTime - DetectionTime) < timeout;
        }
    }
    
    /// <summary>
    /// 异步碰撞检测结果
    /// </summary>
    [Serializable]
    public class AsyncCollisionResult
    {
        /// <summary>是否有碰撞</summary>
        public bool HasCollision;
        
        /// <summary>安全距离</summary>
        public float SafeDistance;
        
        /// <summary>检测完成时间</summary>
        public float DetectionTime;
        
        /// <summary>是否有效（未超时）</summary>
        public bool IsValid(float currentTime, float timeout = 0.1f)
        {
            return (currentTime - DetectionTime) < timeout;
        }
    }
    
    /// <summary>
    /// 性能统计数据结构
    /// </summary>
    [Serializable]
    public class CameraPerformanceStats
    {
        /// <summary>碰撞检测调用次数</summary>
        public int CollisionCheckCount;
        
        /// <summary>碰撞缓存命中次数</summary>
        public int CacheHitCount;
        
        /// <summary>碰撞缓存未命中次数</summary>
        public int CacheMissCount;
        
        /// <summary>碰撞检测耗时（毫秒）</summary>
        public float CollisionCheckTime;
        
        /// <summary>地面检测调用次数</summary>
        public int GroundCheckCount;
        
        /// <summary>环境检测调用次数</summary>
        public int EnvironmentCheckCount;
        
        /// <summary>总更新次数</summary>
        public int UpdateCount;
        
        /// <summary>平均帧耗时（毫秒）</summary>
        public float AverageFrameTime;
        
        /// <summary>缓存命中率</summary>
        public float CacheHitRate
        {
            get
            {
                int total = CacheHitCount + CacheMissCount;
                return total > 0 ? (float)CacheHitCount / total * 100f : 0f;
            }
        }
        
        /// <summary>重置统计数据</summary>
        public void Reset()
        {
            CollisionCheckCount = 0;
            CacheHitCount = 0;
            CacheMissCount = 0;
            CollisionCheckTime = 0f;
            GroundCheckCount = 0;
            EnvironmentCheckCount = 0;
            UpdateCount = 0;
            AverageFrameTime = 0f;
        }
    }
    
    /// <summary>
    /// 碰撞缓存数据结构
    /// </summary>
    [Serializable]
    public class CollisionCacheData
    {
        /// <summary>上次检测的相机位置</summary>
        public Vector3 LastPosition;
        
        /// <summary>上次检测的相机距离（厘米）</summary>
        public float LastDistance;
        
        /// <summary>缓存时间戳</summary>
        public float CacheTime;
        
        /// <summary>是否检测到障碍物</summary>
        public bool HasObstruction;
        
        /// <summary>缓存的安全距离（厘米）</summary>
        public float SafeDistance;
        
        /// <summary>缓存是否有效</summary>
        public bool IsValid(Vector3 currentPosition, float currentDistance, float currentTime)
        {
            // 时间条件：当前时间 - CacheTime < 0.1秒
            if (currentTime - CacheTime > 0.1f)
                return false;
            
            // 位置条件：|当前位置 - LastPosition| < 50cm
            if (Vector3.Distance(currentPosition, LastPosition) > 50.0f)
                return false;
            
            // 距离条件：|当前距离 - LastDistance| < 20cm
            if (Mathf.Abs(currentDistance - LastDistance) > 20.0f)
                return false;
            
            return true;
        }
    }
    
    /// <summary>
    /// 相机状态枚举
    /// </summary>
    public enum CameraState
    {
        /// <summary>正常跟随模式</summary>
        Normal,
        
        /// <summary>战斗模式(锁定目标,距离拉近)</summary>
        Combat,
        
        /// <summary>攀爬模式(俯视角度,距离拉近)</summary>
        Climbing,
        
        /// <summary>游泳模式(水平视角)</summary>
        Swimming,
        
        /// <summary>飞行模式(自由视角,距离拉远)</summary>
        Flying,
        
        /// <summary>过场动画模式(禁用用户输入)</summary>
        Cutscene
    }
    
    /// <summary>
    /// 环境类型枚举
    /// </summary>
    public enum EnvironmentType
    {
        /// <summary>室外开阔区域</summary>
        Outdoor,
        
        /// <summary>室内封闭空间</summary>
        Indoor,
        
        /// <summary>水下环境</summary>
        Underwater,
        
        /// <summary>空中/高空环境</summary>
        Aerial,
        
        /// <summary>洞穴/地下环境</summary>
        Cave,
        
        /// <summary>狭窄通道</summary>
        Corridor,
        
        /// <summary>未知/默认</summary>
        Unknown
    }
    
    /// <summary>
    /// 环境配置
    /// </summary>
    [Serializable]
    public class EnvironmentConfig
    {
        /// <summary>环境类型</summary>
        public EnvironmentType Type;
        
        /// <summary>推荐最大距离</summary>
        public float MaxDistanceLimit = 3000f;
        
        /// <summary>推荐最小距离</summary>
        public float MinDistanceLimit = 5f;
        
        /// <summary>FOV调整系数(1.0为默认)</summary>
        public float FOVMultiplier = 1.0f;
        
        /// <summary>弹性系数调整</summary>
        public float ElasticityMultiplier = 1.0f;
        
        /// <summary>碰撞检测频率调整(1.0为默认)</summary>
        public float CollisionFrequency = 1.0f;
    }
    
    /// <summary>
    /// 相机状态配置
    /// </summary>
    [Serializable]
    public class CameraStateConfig
    {
        /// <summary>状态名称</summary>
        public CameraState State;
        
        /// <summary>目标距离</summary>
        public float TargetDistance = 15f;
        
        /// <summary>目标俯仰角</summary>
        public float TargetPitch = 30f;
        
        /// <summary>是否允许用户旋转</summary>
        public bool AllowRotation = true;
        
        /// <summary>是否允许用户缩放</summary>
        public bool AllowZoom = true;
        
        /// <summary>FOV覆盖值(0表示使用默认)</summary>
        public float FOVOverride = 0f;
        
        /// <summary>弹性系数覆盖(0表示使用默认)</summary>
        public float ElasticityOverride = 0f;
        
        /// <summary>状态切换平滑速度</summary>
        public float TransitionSpeed = 5f;
    }

    /// <summary>
    /// 第三人称相机控制器 - 完整版
    /// </summary>
    public class ThirdPersonCamera : Script
    {
        #region 公共配置参数
        
        [Header("目标设置")]
        [Tooltip("相机跟随的目标Actor")]
        public Actor Target;

        [Tooltip("相机聚焦点相对于目标的位置偏移")]
        public Vector3 FocusOffset = new Vector3(0, 1.8f, 0);

        [Header("距离控制")]
        [Range(5.0f,3000.0f)]
        public float Distance = 15.0f;
        public float MinDistance = 5.0f;
        public float MaxDistance = 3000.0f;

        [Header("角度控制")]
        public float Pitch = 30.0f;
        public float MinPitch = -80.0f;
        public float MaxPitch = 80.0f;
        public float Yaw = 0.0f;

        [Header("初始化设置")]
        [Tooltip("是否从场景中当前相机位置自动计算初始参数")]
        public bool AutoCalculateInitialParameters = true;

        [Header("控制灵敏度")]
        public float RotationSpeed = 0.2f;
        public float ZoomSpeed = 5.0f;

        [Header("碰撞检测")]
        [Tooltip("启用相机碰撞检测")]
        public bool EnableCameraCollision = true;

        [Tooltip("碰撞检测性能等级（Low=1射线, Medium=5射线, High=9射线）")]
        public CollisionDetectionLevel CollisionDetectionQuality = CollisionDetectionLevel.Medium;
        
        [Tooltip("碰撞检测射线数量（向后兼容属性，建议使用CollisionDetectionQuality）")]
        public int CollisionRayCount
        {
            get { return (int)CollisionDetectionQuality; }
            set
            {
                // 将int值映射到最接近的枚举值
                if (value <= 1)
                    CollisionDetectionQuality = CollisionDetectionLevel.Low;
                else if (value <= 5)
                    CollisionDetectionQuality = CollisionDetectionLevel.Medium;
                else
                    CollisionDetectionQuality = CollisionDetectionLevel.High;
            }
        }
        
        [Tooltip("启用球形碰撞二次确认（增强边缘检测）")]
        public bool EnableSphereCollisionCheck = true;
        
        [Tooltip("球形碰撞检测半径（厘米，0.3米）")]
        public float CollisionSphereRadius = 30.0f;

        [Tooltip("碰撞检测层级掩码（排除角色层避免自碰撞）\n建议在编辑器中取消勾选Player层")]
        public LayersMask CollisionLayerMask = LayersMask.Default;
        
        [Tooltip("自动排除角色碰撞（如果启用，会忽略Target的碰撞体）")]
        public bool AutoExcludeTargetCollision = true;

        [Tooltip("碰撞球体半径")]
        public float CollisionRadius = 0.3f;

        [Tooltip("碰撞平滑过渡速度")]
        public float CollisionSmoothSpeed = 10f;

        [Tooltip("启用智能避障（碰撞时自动调整到最佳视角）")]
        public bool EnableSmartAvoidance = true;

        [Tooltip("智能避障的目标俯仰角（推荐45°上帝视角）")]
        public float SmartAvoidancePitch = 45f;

        [Tooltip("智能避障的角度平滑速度")]
        public float AngleSmoothSpeed = 5f;

        [Header("地面穿透防护")]
        [Tooltip("启用地面穿透防护")]
        public bool EnableGroundPenetrationProtection = true;

        [Tooltip("地面检测层")]
        public LayersMask GroundLayers = LayersMask.Default;

        [Tooltip("地面检测射线长度（厘米）")]
        public float GroundCheckDistance = 500.0f;

        [Tooltip("地面最小高度（相机与地面的最小距离，厘米）")]
        public float MinGroundHeight = 50.0f;

        [Tooltip("地面高度调整平滑速度")]
        public float GroundHeightAdjustSpeed = 10.0f;

        [Tooltip("启用地面高度预测")]
        public bool EnableGroundHeightPrediction = true;

        [Tooltip("地面高度预测距离（厘米）")]
        public float GroundPredictionDistance = 200.0f;
        
        [Header("天花板检测（室内场景）")]
        [Tooltip("启用天花板检测（防止相机穿透天花板）")]
        public bool EnableCeilingDetection = true;
        
        [Tooltip("天花板检测距离（厘米）")]
        public float CeilingCheckDistance = 300.0f;
        
        [Tooltip("最小天花板高度（相机与天花板的最小距离，厘米）")]
        public float MinCeilingHeight = 50.0f;
        
        [Header("水下场景检测")]
        [Tooltip("启用水面检测（双层检测：水面+水底）")]
        public bool EnableWaterDetection = true;
        
        [Tooltip("水面层级掩码")]
        public LayersMask WaterLayers = LayersMask.Default;
        
        [Tooltip("水面下最小深度（厘米）")]
        public float MinWaterDepth = 50.0f;
        
        [Header("悬崖边缘检测")]
        [Tooltip("启用悬崖边缘检测（从角色脚下检测）")]
        public bool EnableCliffDetection = true;
        
        [Tooltip("悬崖检测距离（厘米）")]
        public float CliffCheckDistance = 1000.0f;

        [Header("相机抖动")]
        [Tooltip("启用相机抖动")]
        public bool EnableCameraShake = true;

        [Tooltip("抖动衰减速度")]
        public float ShakeDecaySpeed = 2.0f;

        [Header("基准视角恢复")]
        [Tooltip("启用一键恢复基准视角功能")]
        public bool EnableBaselineReset = true;

        [Tooltip("恢复基准视角的快捷键")]
        public KeyboardKeys ResetKey = KeyboardKeys.R;

        [Tooltip("视角恢复的平滑速度（越大越快）")]
        public float ResetSmoothSpeed = 5f;
        
        [Header("自动对齐系统")]
        [Tooltip("启用自动对齐功能（停止手动旋转后自动对齐到角色后方）")]
        public bool EnableAutoAlign = true;
        
        [Tooltip("自动对齐延迟时间（秒，停止手动旋转后等待时间）")]
        public float AlignDelay = 5.0f;
        
        [Tooltip("自动对齐旋转速度（度/秒）")]
        public float AlignRotationSpeed = 90f;
        
        [Tooltip("角度范围阈值（度，在此范围内开始平滑对齐）")]
        public float AlignSmoothRange = 45f;
        
        [Tooltip("角色最小移动速度阈值（米/秒，低于此值不触发对齐）")]
        public float AlignMinSpeed = 0.1f;
        
        [Tooltip("战斗中禁用自动对齐")]
        public bool DisableAlignInCombat = true;
        
        [Header("拍照功能")]
        [Tooltip("启用拍照功能")]
        public bool EnablePhotoMode = true;
        
        [Tooltip("拍摄2寸头像的快捷键")]
        public KeyboardKeys TakeHeadshotKey = KeyboardKeys.F9;
        
        [Tooltip("拍摄全身照的快捷键")]
        public KeyboardKeys TakeFullBodyKey = KeyboardKeys.F10;
        
        [Tooltip("游戏截图的快捷键")]
        public KeyboardKeys TakeScreenshotKey = KeyboardKeys.F11;
        
        [Tooltip("截图保存路径（相对于项目根目录）")]
        public string ScreenshotPath = "Screenshots";
        
        [Tooltip("头像照片距离（米）")]
        public float HeadshotDistance = 2.5f;
        
        [Tooltip("头像照片俯仰角")]
        public float HeadshotPitch = 0f;
        
        [Tooltip("全身照距离（米）")]
        public float FullBodyDistance = 5f;
        
        [Tooltip("全身照俯仰角")]
        public float FullBodyPitch = 10f;
        
        [Tooltip("拍照后恢复原视角的速度")]
        public float PhotoReturnSpeed = 8f;
        
        [Header("弹性跟随系统")]
        [Tooltip("启用弹性跟随(角色移动时相机有延迟感,模仿剑侠情缘3/魔兽世界效果)")]
        public bool EnableElasticFollow = true;
        
        [Tooltip("位置跟随弹性系数(0-1,越小延迟越明显,越大越紧跟)")]
        [Range(0.1f, 1f)]
        public float PositionElasticity = 0.8f;
        
        [Tooltip("水平旋转跟随弹性系数")]
        [Range(0.1f, 1f)]
        public float RotationElasticity = 0.9f;
        
        [Tooltip("启用惯性效果(角色突然停止时相机会略微超前)")]
        public bool EnableInertia = true;
        
        [Tooltip("惯性衰减速度")]
        public float InertiaDamping = 5f;
        
        [Header("FOV动态调整")]
        [Tooltip("启用基于速度的FOV动态调整(增强速度感)")]
        public bool EnableDynamicFOV = true;
        
        [Tooltip("基础FOV(静止时)")]
        [Range(30f, 90f)]
        public float BaseFOV = 60f;
        
        [Tooltip("最大FOV(高速移动时)")]
        [Range(60f, 120f)]
        public float MaxFOV = 75f;
        
        [Tooltip("速度阈值(超过此速度开始增加FOV)")]
        public float SpeedThreshold = 10f;
        
        [Tooltip("最大速度(达到此速度时FOV为MaxFOV)")]
        public float MaxSpeed = 50f;
        
        [Tooltip("FOV变化平滑速度")]
        public float FOVSmoothSpeed = 3f;
        
        [Header("状态FOV调整")]
        [Tooltip("启用基于状态的FOV调整（优先级高于速度FOV）")]
        public bool EnableStateFOV = true;
        
        [Tooltip("冲刺状态FOV偏移")]
        public float SprintFOVOffset = 10f;
        
        [Tooltip("战斗状态FOV偏移")]
        public float CombatFOVOffset = -5f;
        
        [Tooltip("瞄准状态FOV偏移")]
        public float AimFOVOffset = -15f;
        
        [Tooltip("骑乘状态FOV偏移")]
        public float RideFOVOffset = 5f;
        
        [Tooltip("飞行状态FOV偏移")]
        public float FlyingFOVOffset = 10f;
        
        [Header("相机状态机系统")]
        [Tooltip("启用相机状态机(根据游戏场景自动切换相机模式)")]
        public bool EnableStateMachine = true;
        
        [Tooltip("当前相机状态")]
        public CameraState CurrentState = CameraState.Normal;
        
        [Tooltip("启用状态自动切换(根据角色行为自动切换)")]
        public bool EnableAutoStateSwitch = true;
        
        [Tooltip("状态切换平滑速度")]
        public float StateTransitionSpeed = 5f;
        
        [Tooltip("Normal状态配置")]
        public CameraStateConfig NormalStateConfig = new CameraStateConfig
        {
            State = CameraState.Normal,
            TargetDistance = 15f,
            TargetPitch = 30f,
            AllowRotation = true,
            AllowZoom = true,
            TransitionSpeed = 5f
        };
        
        [Tooltip("Combat状态配置(战斗模式)")]
        public CameraStateConfig CombatStateConfig = new CameraStateConfig
        {
            State = CameraState.Combat,
            TargetDistance = 10f,
            TargetPitch = 25f,
            AllowRotation = true,
            AllowZoom = false,
            FOVOverride = 65f,
            TransitionSpeed = 8f
        };
        
        [Tooltip("Climbing状态配置(攀爬模式)")]
        public CameraStateConfig ClimbingStateConfig = new CameraStateConfig
        {
            State = CameraState.Climbing,
            TargetDistance = 8f,
            TargetPitch = 50f,
            AllowRotation = true,
            AllowZoom = false,
            TransitionSpeed = 6f
        };
        
        [Tooltip("Swimming状态配置(游泳模式)")]
        public CameraStateConfig SwimmingStateConfig = new CameraStateConfig
        {
            State = CameraState.Swimming,
            TargetDistance = 12f,
            TargetPitch = 15f,
            AllowRotation = true,
            AllowZoom = true,
            TransitionSpeed = 4f
        };
        
        [Tooltip("Flying状态配置(飞行模式)")]
        public CameraStateConfig FlyingStateConfig = new CameraStateConfig
        {
            State = CameraState.Flying,
            TargetDistance = 20f,
            TargetPitch = 20f,
            AllowRotation = true,
            AllowZoom = true,
            FOVOverride = 70f,
            TransitionSpeed = 4f
        };
        
        [Tooltip("Cutscene状态配置(过场动画)")]
        public CameraStateConfig CutsceneStateConfig = new CameraStateConfig
        {
            State = CameraState.Cutscene,
            TargetDistance = 15f,
            TargetPitch = 30f,
            AllowRotation = false,
            AllowZoom = false,
            TransitionSpeed = 2f
        };
        
        [Tooltip("启用性能监控统计")]
        public bool EnablePerformanceMonitoring = false;
        
        [Tooltip("性能统计重置间隔（秒）")]
        public float StatsResetInterval = 60f;
        
        [Header("异步棄测系统")]
        [Tooltip("启用异步地面检测（减少主线程压力）")]
        public bool EnableAsyncGroundDetection = true;
        
        [Tooltip("启用异步环境检测（减少主线程压力）")]
        public bool EnableAsyncEnvironmentDetection = true;
        
        [Tooltip("启用异步碰撞检测（适用于高质量模式）")]
        public bool EnableAsyncCollisionDetection = false;
        
        [Tooltip("异步检测间隔（秒，避免频繁检测）")]
        public float AsyncDetectionInterval = 0.1f;
        
        [Header("性能LOD策略")]
        [Tooltip("启用自动性能LOD调整（基于FPS动态调整检测质量）")]
        public bool EnableAutoLOD = true;
        
        [Tooltip("目标帧率（FPS低于此值时降低质量）")]
        public float TargetFPS = 60f;
        
        [Tooltip("帧率降级阈值（FPS低于此值开始降级）")]
        public float LODDowngradeThreshold = 50f;
        
        [Tooltip("帧率升级阈值（FPS高于此值开始升级）")]
        public float LODUpgradeThreshold = 58f;
        
        [Tooltip("性能LOD调整间隔（秒，避免频繁切换）")]
        public float LODAdjustInterval = 2.0f;
        
        [Header("环境感知系统")]
        [Tooltip("启用环境感知(根据环境自动调整相机参数)")]
        public bool EnableEnvironmentAwareness = true;
        
        [Tooltip("当前环境类型")]
        public EnvironmentType CurrentEnvironment = EnvironmentType.Outdoor;
        
        [Tooltip("环境检测间隔(秒,避免频繁检测)")]
        public float EnvironmentDetectionInterval = 0.5f;
        
        [Tooltip("启用天气系统联动")]
        public bool EnableWeatherIntegration = true;
        
        [Tooltip("当前天气类型")]
        public WeatherType CurrentWeather = WeatherType.Clear;
        
        [Tooltip("雨天视距衰减系数(0-1)")]
        [Range(0.5f, 1f)]
        public float RainVisibilityFactor = 0.85f;
        
        [Tooltip("雾天视距衰减系数(0-1)")]
        [Range(0.3f, 1f)]
        public float FogVisibilityFactor = 0.6f;
        
        [Tooltip("启用光照条件检测")]
        public bool EnableLightDetection = true;
        
        [Tooltip("暗环境阈值(低于此亮度视为暗环境)")]
        [Range(0f, 1f)]
        public float DarkEnvironmentThreshold = 0.3f;
        
        [Tooltip("Outdoor环境配置")]
        public EnvironmentConfig OutdoorConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Outdoor,
            MaxDistanceLimit = 3000f,
            MinDistanceLimit = 5f,
            FOVMultiplier = 1.0f,
            ElasticityMultiplier = 1.0f,
            CollisionFrequency = 1.0f
        };
        
        [Tooltip("Indoor环境配置(室内)")]
        public EnvironmentConfig IndoorConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Indoor,
            MaxDistanceLimit = 30f,
            MinDistanceLimit = 3f,
            FOVMultiplier = 0.95f,
            ElasticityMultiplier = 1.2f,
            CollisionFrequency = 1.5f
        };
        
        [Tooltip("Underwater环境配置(水下)")]
        public EnvironmentConfig UnderwaterConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Underwater,
            MaxDistanceLimit = 50f,
            MinDistanceLimit = 5f,
            FOVMultiplier = 1.1f,
            ElasticityMultiplier = 0.7f,
            CollisionFrequency = 0.8f
        };
        
        [Tooltip("Aerial环境配置(空中)")]
        public EnvironmentConfig AerialConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Aerial,
            MaxDistanceLimit = 5000f,
            MinDistanceLimit = 10f,
            FOVMultiplier = 1.15f,
            ElasticityMultiplier = 0.9f,
            CollisionFrequency = 0.5f
        };
        
        [Tooltip("Cave环境配置(洞穴)")]
        public EnvironmentConfig CaveConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Cave,
            MaxDistanceLimit = 25f,
            MinDistanceLimit = 3f,
            FOVMultiplier = 0.9f,
            ElasticityMultiplier = 1.3f,
            CollisionFrequency = 2.0f
        };
        
        [Tooltip("Corridor环境配置(狭窄通道)")]
        public EnvironmentConfig CorridorConfig = new EnvironmentConfig
        {
            Type = EnvironmentType.Corridor,
            MaxDistanceLimit = 15f,
            MinDistanceLimit = 2f,
            FOVMultiplier = 0.85f,
            ElasticityMultiplier = 1.5f,
            CollisionFrequency = 2.5f
        };
        


        #endregion

        #region 私有字段
        
        private Camera _camera;
        private InputManager _inputManager;
        private float _currentCollisionDistance;
        private Vector3[] _collisionRayDirections;
        
        // 碰撞缓存相关
        private CollisionCacheData _collisionCache = new CollisionCacheData();
        private float _gameTime = 0f; // 游戏运行时间
        
        // 动态距离调整相关
        private float _lastObstructionDistance = float.MaxValue; // 上次障碍物距离
        private bool _wasColliding = false; // 上一帧是否碰撞
        private float _collisionStateChangeTime = 0f; // 碰撞状态改变时间
        private const float CollisionStateStableTime = 0.1f; // 碰撞状态稳定时间（秒）- 优化：降低延迟
        
        // 性能统计相关
        private CameraPerformanceStats _performanceStats = new CameraPerformanceStats();
        private float _statsResetTimer = 0f;
        private float _frameStartTime = 0f;
        
        // 相机抖动相关
        private float _currentShakeIntensity;
        private float _shakeTime;
        private Vector3 _shakeOffset;
        
        // 地面穿透防护相关
        private float _currentGroundHeight;
        private float _targetGroundHeight;
        private bool _isGroundDetected;
        private float _lastGroundCheckTime;
        private Vector3 _lastCameraPosition;
        private const float GroundCheckInterval = 0.1f; // 每0.1秒检查一次地面
        
        // 智能避障相关
        private float _targetPitch; // 目标俯仰角（用于平滑过渡）
        private bool _isColliding; // 当前是否在碰撞状态
        
        // 自动对齐相关
        private bool _isAligning = false;        // 是否正在自动对齐
        private float _alignTimer = 0f;          // 对齐延迟计时器
        private bool _wasManualRotating = false; // 上一帧是否在手动旋转
        private float _targetAlignYaw = 0f;      // 目标对齐角度
        
        // 基准视角相关
        private float _baselineDistance; // 基准距离
        private float _baselinePitch;    // 基准俯仰角
        private float _baselineYaw;      // 基准水平角
        private bool _isResetting;       // 是否正在恢复中
        
        // 拍照模式相关
        private bool _isPhotoMode;       // 是否在拍照模式
        private float _prePhotoDistance; // 拍照前的距离
        private float _prePhotoPitch;    // 拍照前的俯仰角
        private float _prePhotoYaw;      // 拍照前的水平角
        private float _targetPhotoDistance; // 目标拍照距离
        private float _targetPhotoPitch;    // 目标拍照俯仰角
        private bool _photoTransitioning;   // 拍照过渡中
        
        // 弹性跟随相关
        private Vector3 _lastTargetPosition; // 上一帧角色位置
        private Vector3 _targetVelocity;     // 角色移动速度
        private Vector3 _cameraInertia;      // 相机惯性速度
        private float _smoothYaw;            // 平滑后的Yaw角
        
        // FOV动态调整相关
        private float _currentFOV;           // 当前FOV值
        private float _targetFOV;            // 目标FOV值
        
        // 状态机相关
        private CameraState _previousState;  // 上一个状态
        private Dictionary<CameraState, CameraStateConfig> _stateConfigs; // 状态配置字典
        private bool _isTransitioning;       // 是否正在状态切换中
        private float _stateTargetDistance;  // 状态目标距离
        private float _stateTargetPitch;     // 状态目标俯仰角
        private float _stateTargetFOV;       // 状态目标FOV
        
        // 角色控制器引用（用于状态检测）
        private PlayerController _playerController;
        
        // 环境感知相关
        private EnvironmentType _previousEnvironment; // 上一个环境
        private Dictionary<EnvironmentType, EnvironmentConfig> _environmentConfigs; // 环境配置字典
        private float _environmentDetectionTimer; // 环境检测计时器
        private float _currentLightLevel;     // 当前光照级别(0-1)
        private float _environmentMaxDistance; // 环境限制的最大距离
        private float _environmentMinDistance; // 环境限制的最小距离
        private float _weatherVisibilityFactor; // 天气影响的可见度系数
        
        // 异步检测相关
        private Task<AsyncDetectionResult> _groundDetectionTask; // 地面检测任务
        private AsyncDetectionResult _lastGroundResult; // 最后一次地面检测结果
        private float _lastAsyncGroundCheckTime; // 最后一次异步地面检测时间
        
        private Task<AsyncDetectionResult> _environmentDetectionTask; // 环境检测任务
        private AsyncDetectionResult _lastEnvironmentResult; // 最后一次环境检测结果
        private float _lastAsyncEnvironmentCheckTime; // 最后一次异步环境检测时间
        
        private Task<AsyncCollisionResult> _collisionDetectionTask; // 碰撞检测任务
        private AsyncCollisionResult _lastCollisionResult; // 最后一次碰撞检测结果
        private float _lastAsyncCollisionCheckTime; // 最后一次异步碰撞检测时间
        
        // 性能LOD相关
        private float _currentFPS; // 当前FPS
        private float _fpsUpdateTimer; // FPS更新计时器
        private int _frameCount; // 帧计数
        private float _lastLODAdjustTime; // 最后一次LOD调整时间
        private CollisionDetectionLevel _originalQuality; // 原始质量等级（用于自动LOD）
        

        
        #endregion

        public override void OnStart()
        {
            // 获取Camera组件
            _camera = Actor as Camera;
            if (_camera == null) _camera = Actor.GetChild<Camera>();

            if (_camera == null)
            {
                //Debug.LogError("[ThirdPersonCamera] Camera组件未找到！");
                Enabled = false;
                return;
            }

            // 查找InputManager
            _inputManager = Actor.Parent?.GetScript<InputManager>();

            // 自动计算初始参数（基于场景中相机的当前位置）
            if (AutoCalculateInitialParameters && Target != null)
            {
                CalculateInitialParameters();
            }

            // 初始化碰撞检测
            _currentCollisionDistance = Distance;
            _gameTime = 0f; // 初始化游戏时间
            _wasColliding = false;
            _lastObstructionDistance = float.MaxValue;
            InitializeCollisionRays();
            
            //Debug.Log($"[ThirdPersonCamera] 碰撞检测初始化 - EnableCameraCollision={EnableCameraCollision}, Quality={CollisionDetectionQuality}, EnableSphereCheck={EnableSphereCollisionCheck}, CollisionLayerMask={CollisionLayerMask}, EnableSmartAvoidance={EnableSmartAvoidance}");
            
            // 初始化抖动系统
            _currentShakeIntensity = 0f;
            _shakeTime = 0f;
            _shakeOffset = Vector3.Zero;
            
            // 初始化智能避障
            _targetPitch = Pitch;
            _isColliding = false;
            
            // 初始化自动对齐
            _isAligning = false;
            _alignTimer = 0f;
            _wasManualRotating = false;
            _targetAlignYaw = Yaw;
            
            // 注：基准视角不需要保存，将在ResetToBaseline时智能计算
            
            // 初始化拍照模式
            _isPhotoMode = false;
            _photoTransitioning = false;
            
            // 初始化弹性跟随
            if (Target != null)
            {
                _lastTargetPosition = Target.Position;
            }
            _targetVelocity = Vector3.Zero;
            _cameraInertia = Vector3.Zero;
            _smoothYaw = Yaw;
            
            // 初始化FOV动态调整
            _currentFOV = BaseFOV;
            _targetFOV = BaseFOV;
            if (_camera != null)
            {
                _camera.FieldOfView = BaseFOV;
            }
            
            // 初始化状态机系统
            InitializeStateMachine();
            
            // 初始化环境感知系统
            InitializeEnvironmentAwareness();
            
            // 初始化异步检测系统
            InitializeAsyncDetection();
            
            // 初始化性能LOD系统
            InitializePerformanceLOD();
            
            // 确保截图目录存在
            EnsureScreenshotDirectory();

            //Debug.Log($"[ThirdPersonCamera] 初始化完成 - Distance: {Distance:F2}, Pitch: {Pitch:F2}, Yaw: {Yaw:F2}, State: {CurrentState}, Environment: {CurrentEnvironment}");
        }

        public override void OnUpdate()
        {
            if (Target == null || _camera == null) return;
            
            // 性能监控：开始帧计时
            if (EnablePerformanceMonitoring)
            {
                _frameStartTime = Time.UnscaledGameTime;
                _performanceStats.UpdateCount++;
            }
            
            // 更新游戏时间
            _gameTime += Time.DeltaTime;
            
            // 更新FPS监控（用于性能LOD）
            UpdateFPSMonitoring();
            
            // 启动异步检测任务（在后台运行）
            StartAsyncGroundDetection();
            StartAsyncEnvironmentDetection();

            // 1. 环境感知系统更新
            if (EnableEnvironmentAwareness)
            {
                UpdateEnvironmentAwareness();
            }

            // 2. 状态机自动检测和更新
            if (EnableStateMachine && EnableAutoStateSwitch)
            {
                UpdateStateMachine();
            }
            
            // 3. 处理状态过渡
            if (EnableStateMachine && _isTransitioning)
            {
                UpdateStateTransition();
            }

            // 4. 处理输入(根据当前状态决定是否允许)
            HandleInput();
            
            // 4.3 处理自动对齐
            if (EnableAutoAlign && !_isResetting && !_photoTransitioning)
            {
                UpdateAutoAlign();
            }
            
            // 4.5 处理基准视角恢复
            if (_isResetting)
            {
                UpdateBaselineReset();
                return; // 恢复期间跳过后续逻辑
            }
            
            // 4.6 处理拍照模式过渡
            if (_photoTransitioning)
            {
                UpdatePhotoTransition();
                return; // 拍照期间跳过后续逻辑
            }

            // 2. 计算角色移动速度(无论是否启用弹性跟随都需要,用于FOV调整)
            Vector3 targetPosition = Target.Position;
            _targetVelocity = (targetPosition - _lastTargetPosition) / Mathf.Max(Time.DeltaTime, 0.001f);
            _lastTargetPosition = targetPosition;
            
            // 3. 计算聚焦点
            Vector3 focusPoint;
            
            if (EnableElasticFollow && !_photoTransitioning && !_isResetting)
            {
                // ✅ 启用弹性跟随
                Vector3 currentFocusPoint = Actor.Position - CalculateCameraOffset(Pitch, _smoothYaw, _currentCollisionDistance);
                
                // 动态计算弹性系数（基于速度）
                float currentElasticity = PositionElasticity;
                float velocityLength = _targetVelocity.Length;
                if (velocityLength < 1.0f)
                {
                    currentElasticity = 0.95f; // 静止时几乎完全跟随
                }
                else if (velocityLength < 10.0f)
                {
                    currentElasticity = 0.85f; // 慢速移动
                }
                else
                {
                    currentElasticity = 0.75f; // 快速移动
                }
                
                focusPoint = Vector3.Lerp(
                    currentFocusPoint,
                    targetPosition + FocusOffset,
                    currentElasticity
                );
                
                // 应用惯性效果
                if (EnableInertia)
                {
                    _cameraInertia = Vector3.Lerp(_cameraInertia, _targetVelocity * 0.1f, Time.DeltaTime * 2f);
                    _cameraInertia = Vector3.Lerp(_cameraInertia, Vector3.Zero, Time.DeltaTime * InertiaDamping);
                    focusPoint += _cameraInertia * Time.DeltaTime;
                }
                
                // 禁用移动引起的自动旋转，仅在鼠标右键旋转时更新
                _smoothYaw = Yaw;
            }
            else
            {
                // ❌ 禁用弹性跟随,直接使用目标位置
                focusPoint = targetPosition + FocusOffset;
                _smoothYaw = Yaw;
                _cameraInertia = Vector3.Zero;
            }

            // 4. 执行碰撞检测和智能避障
            float effectiveDistance;
            float effectivePitch = Pitch; // 默认使用用户输入的俯仰角
            
            if (EnableCameraCollision)
            {
                bool hasCollision;
                float collisionDistance;
                
                // 尝试使用异步检测结果
                if (EnableAsyncCollisionDetection && TryGetAsyncCollisionResult(out hasCollision, out collisionDistance))
                {
                    // 使用异步结果
                    _isColliding = hasCollision;
                }
                else
                {
                    // 启动异步检测（下一帧使用）
                    if (EnableAsyncCollisionDetection)
                    {
                        StartAsyncCollisionDetection(focusPoint, Pitch);
                    }
                    
                    // 执行同步碰撞检测
                    hasCollision = CheckCollision(focusPoint, Pitch, out collisionDistance);
                    _isColliding = hasCollision;
                }
                
                // 优先级控制：设置碰撞优先级（已移除优化代码，此处保留空逻辑）
                
                if (hasCollision)
                {
                    //Debug.Log($"[Collision] 检测到碰撞@{Pitch:F1}°, 安全距离:{collisionDistance:F2}cm, 理想距离:{Distance:F2}cm");
                    
                    // ✅ 优先策略：调整距离（避免角度闪烁）
                    // 只有当碰撞距离小于最小距离时，才考虑调整角度
                    if (collisionDistance <= MinDistance && EnableSmartAvoidance)
                    {
                        //Debug.LogWarning($"[SmartAvoidance] 碰撞距离过小({collisionDistance:F2} <= {MinDistance:F2}), 尝试调整角度到{SmartAvoidancePitch}°...");
                        
                        // 检查智能避障角度是否能提供更大距离
                        bool hasCollisionAtAvoidance = CheckCollision(focusPoint, SmartAvoidancePitch, out float collisionDistanceAtAvoidance);
                        
                        // 只有当新角度提供的距离明显更大时才切换
                        if (collisionDistanceAtAvoidance > collisionDistance * 1.2f) // 至少增加20%
                        {
                            effectivePitch = SmartAvoidancePitch;
                            collisionDistance = collisionDistanceAtAvoidance;
                            //Debug.Log($"[SmartAvoidance] ✅ 切换到{SmartAvoidancePitch}°视角, 距离从{collisionDistance:F2}cm增加到{collisionDistanceAtAvoidance:F2}cm");
                        }
                        else
                        {
                            //Debug.Log($"[SmartAvoidance] ✖️ {SmartAvoidancePitch}°视角未改善({collisionDistanceAtAvoidance:F2}cm), 保持当前角度");
                        }
                    }
                }
                
                // === 动态距离调整策略：快速缩短、缓慢恢复 ===
                float smoothSpeed;
                
                {
                    // 使用原有逻辑
                    // 计算距离变化
                    float distanceChange = Mathf.Abs(collisionDistance - _currentCollisionDistance);
                
                    // ✅ 关键修复：碰撞状态稳定机制
                    bool stableCollisionState = hasCollision;
                    
                    // 如果碰撞状态发生变化，检查是否达到稳定时间
                    if (hasCollision != _wasColliding)
                    {
                        // 状态变化，记录时间
                        if (_collisionStateChangeTime == 0f)
                        {
                            _collisionStateChangeTime = _gameTime;
                            //Debug.Log($"[碰撞状态] 检测到状态变化: {_wasColliding} → {hasCollision}, 启动稳定计时");
                        }
                        
                        // 检查是否达到稳定时间
                        float timeSinceChange = _gameTime - _collisionStateChangeTime;
                        if (timeSinceChange < CollisionStateStableTime)
                        {
                            // 未达到稳定时间，保持原有状态
                            stableCollisionState = _wasColliding;
                            //Debug.Log($"[碰撞状态] 未达到稳定时间({timeSinceChange:F3}s < {CollisionStateStableTime}s), 保持原状态: {_wasColliding}");
                        }
                        else
                        {
                            // 达到稳定时间，允许状态变化
                            //Debug.Log($"[碰撞状态] 达到稳定时间({timeSinceChange:F3}s), 允许状态变化: {_wasColliding} → {hasCollision}");
                            _collisionStateChangeTime = 0f; // 重置计时器
                        }
                    }
                    else
                    {
                        // 状态未变化，重置计时器
                        _collisionStateChangeTime = 0f;
                    }
                    
                    if (stableCollisionState)
                    {
                        // === 碰撞状态：快速缩短 ===
                        if (!_wasColliding)
                        {
                            // 刚刚进入碰撞状态
                            //Debug.Log($"[距离调整] 进入碰撞状态，快速缩短距离: {_currentCollisionDistance:F2}cm -> {collisionDistance:F2}cm");
                        }
                        
                        // 距离变化自适应速度
                        if (distanceChange > 50.0f) // 距离变化>50cm，快速缩短
                        {
                            smoothSpeed = CollisionSmoothSpeed * 5.0f; // 5倍速
                        }
                        else if (distanceChange > 20.0f) // 距离变化>20cm，中速缩短
                        {
                            smoothSpeed = CollisionSmoothSpeed * 2.0f; // 2倍速
                        }
                        else // 距离变化较小，正常缩短
                        {
                            smoothSpeed = CollisionSmoothSpeed * 1.5f; // 1.5倍速
                        }
                        
                        _lastObstructionDistance = collisionDistance;
                    }
                    else
                    {
                        // === 无碰撞状态：缓慢恢复 ===
                        if (_wasColliding)
                        {
                            // 刚刚离开碰撞状态
                            //Debug.Log($"[距离调整] 离开碰撞状态，缓慢恢复距离: {_currentCollisionDistance:F2}cm -> {Distance:F2}cm");
                        }
                        
                        // 缓慢恢复速度（真正的缓慢恢复，避免快速切换导致震荡）
                        smoothSpeed = CollisionSmoothSpeed * 0.5f; // 0.5倍速（比碰撞时慢，平滑恢复）
                    }
                    
                    _wasColliding = stableCollisionState;
                    
                    _currentCollisionDistance = Mathf.Lerp(
                        _currentCollisionDistance,
                        collisionDistance,
                        Time.DeltaTime * smoothSpeed
                    );
                    
                    // ✅ 关键：碰撞距离可以突破环境限制以确保相机可见性
                    // 只需保证不小于系统绝对最小值(200cm = 2m)
                    float systemMinDistance = 200.0f;
                    _currentCollisionDistance = Mathf.Max(_currentCollisionDistance, systemMinDistance);
                    effectiveDistance = _currentCollisionDistance;
                }
            }
            else
            {
                // 没有启用碰撞检测时,直接使用理想距离
                effectiveDistance = Distance;
                _currentCollisionDistance = Distance;
                _isColliding = false;
            }

            // 6. 计算相机位置(使用有效俯仰角和平滑Yaw角，避免重影)
            Vector3 cameraPosition = CalculateCameraPosition(focusPoint, effectivePitch, _smoothYaw, effectiveDistance);
            
            //Debug.Log($"[CameraPosition] Pitch:{Pitch:F1}° -> effectivePitch:{effectivePitch:F1}°, Distance:{Distance:F2} -> effectiveDistance:{effectiveDistance:F2}, Position:{cameraPosition}");

            // 6.5. 地面穿透防护检查和调整
            if (EnableGroundPenetrationProtection)
            {
                // 确保相机位置不会穿透地面
                cameraPosition = GetSafeCameraPosition(cameraPosition);
            }

            // 7. 应用抖动
            if (EnableCameraShake && _currentShakeIntensity > 0f)
            {
                UpdateCameraShake();
                cameraPosition += _shakeOffset;
            }

            // 8. 设置相机位置和朝向
            Actor.Position = cameraPosition;
            
            // 计算朝向向量
            Vector3 direction = focusPoint - cameraPosition;
            direction.Normalize();
            Actor.Orientation = Quaternion.LookRotation(direction, Vector3.Up);
            
            // 9. 更新FOV
            if (EnableDynamicFOV && !_photoTransitioning)
            {
                UpdateDynamicFOV();
            }
            
            // 10. 更新地面穿透防护系统（在设置相机位置后）
            if (EnableGroundPenetrationProtection)
            {
                UpdateGroundPenetrationProtection();
            }
            
            // 11. 性能监控：结束帧计时和统计更新
            if (EnablePerformanceMonitoring)
            {
                float frameTime = (Time.UnscaledGameTime - _frameStartTime) * 1000f;
                _performanceStats.AverageFrameTime = (_performanceStats.AverageFrameTime * (_performanceStats.UpdateCount - 1) + frameTime) / _performanceStats.UpdateCount;
                
                // 定期重置统计数据
                _statsResetTimer += Time.DeltaTime;
                if (_statsResetTimer >= StatsResetInterval)
                {
                    //Debug.Log($"[性能统计] 缓存命中率:{_performanceStats.CacheHitRate:F1}%, 平均帧耗时:{_performanceStats.AverageFrameTime:F3}ms, 碰撞检测:{_performanceStats.CollisionCheckCount}次");
                    _statsResetTimer = 0f;
                    _performanceStats.Reset();
                }
            }
        }

        /// <summary>
        /// 更新地面穿透防护
        /// </summary>
        private void UpdateGroundPenetrationProtection()
        {
            if (!EnableGroundPenetrationProtection || Target == null || _camera == null)
                return;

            // 限制检测频率，避免性能问题
            float currentTime = Time.DeltaTime;
            if (currentTime - _lastGroundCheckTime < GroundCheckInterval)
                return;

            _lastGroundCheckTime = currentTime;
            
            // 性能监控：地面检测计数
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.GroundCheckCount++;
            }

            // 获取相机当前位置
            Vector3 cameraPosition = Actor.Position;
            
            // 向下发射射线检测地面
            Vector3 rayStart = cameraPosition;
            Vector3 rayDirection = Vector3.Down;
            
            if (Physics.RayCast(rayStart, rayDirection, out RayCastHit hit, GroundCheckDistance, GroundLayers))
            {
                _isGroundDetected = true;
                _targetGroundHeight = hit.Point.Y + MinGroundHeight;
                
                // 如果启用地面高度预测，计算预测位置
                if (EnableGroundHeightPrediction)
                {
                    // 计算相机移动方向
                    Vector3 cameraVelocity = (cameraPosition - _lastCameraPosition) / Mathf.Max(Time.DeltaTime, 0.001f);
                    _lastCameraPosition = cameraPosition;
                    
                    // 如果相机正在向下移动，提前调整高度
                    if (cameraVelocity.Y < -0.1f)
                    {
                        // 预测相机在下一帧的位置
                        Vector3 predictedPosition = cameraPosition + cameraVelocity * Time.DeltaTime * GroundPredictionDistance;
                        
                        // 检查预测位置是否会穿透地面
                        if (WillPenetrateGround(predictedPosition))
                        {
                            // 提前调整高度，提供更大的安全距离
                            _targetGroundHeight += MinGroundHeight * 0.5f;
                        }
                    }
                }
                
                // 平滑调整相机高度
                if (cameraPosition.Y < _targetGroundHeight)
                {
                    float newY = Mathf.Lerp(cameraPosition.Y, _targetGroundHeight, Time.DeltaTime * GroundHeightAdjustSpeed);
                    Actor.Position = new Vector3(cameraPosition.X, newY, cameraPosition.Z);
                }
            }
            else
            {
                _isGroundDetected = false;
            }
            
            // 更新当前地面高度（用于平滑过渡）
            _currentGroundHeight = Mathf.Lerp(_currentGroundHeight, _targetGroundHeight, Time.DeltaTime * GroundHeightAdjustSpeed);
        }

        /// <summary>
        /// 检查指定位置是否会穿透地面
        /// </summary>
        /// <param name="position">要检查的位置</param>
        /// <returns>是否会穿透地面</returns>
        private bool WillPenetrateGround(Vector3 position)
        {
            if (!EnableGroundPenetrationProtection)
                return false;

            // 向下发射射线检测地面
            Vector3 rayStart = position;
            Vector3 rayDirection = Vector3.Down;
            
            if (Physics.RayCast(rayStart, rayDirection, out RayCastHit hit, GroundCheckDistance, GroundLayers))
            {
                // 如果相机位置低于地面加上最小高度，则会穿透
                return position.Y < hit.Point.Y + MinGroundHeight;
            }
            
            return false;
        }

        /// <summary>
        /// 获取安全的相机位置（确保不会穿透地面/天花板/水面）
        /// 支持：
        /// 1. 室内场景天花板检测
        /// 2. 水下场景双层检测（水面+水底）
        /// 3. 悬崖边缘角色脚下检测
        /// </summary>
        /// <param name="position">原始相机位置</param>
        /// <returns>安全的相机位置</returns>
        private Vector3 GetSafeCameraPosition(Vector3 position)
        {
            if (!EnableGroundPenetrationProtection)
                return position;

            Vector3 adjustedPosition = position;
            
            // === 1. 室内场景：天花板检测 ===
            if (EnableCeilingDetection && (CurrentEnvironment == EnvironmentType.Indoor || CurrentEnvironment == EnvironmentType.Cave || CurrentEnvironment == EnvironmentType.Corridor))
            {
                Vector3 ceilingRayStart = adjustedPosition;
                Vector3 ceilingRayDirection = Vector3.Up;
                
                if (Physics.RayCast(ceilingRayStart, ceilingRayDirection, out RayCastHit ceilingHit, CeilingCheckDistance, CollisionLayerMask))
                {
                    // 检测到天花板
                    float safeCeilingY = ceilingHit.Point.Y - MinCeilingHeight;
                    if (adjustedPosition.Y > safeCeilingY)
                    {
                        adjustedPosition.Y = safeCeilingY;
                        //Debug.Log($"[天花板检测] 调整相机高度以避免穿透天花板: {position.Y:F2} -> {safeCeilingY:F2}");
                    }
                }
            }
            
            // === 2. 水下场景：双层检测（水面+水底） ===
            if (EnableWaterDetection && CurrentEnvironment == EnvironmentType.Underwater)
            {
                // 向上检测水面
                Vector3 waterSurfaceRayStart = adjustedPosition;
                Vector3 waterSurfaceRayDirection = Vector3.Up;
                
                bool hasWaterSurface = Physics.RayCast(waterSurfaceRayStart, waterSurfaceRayDirection, out RayCastHit waterSurfaceHit, GroundCheckDistance, WaterLayers);
                
                // 向下检测水底
                Vector3 waterBottomRayStart = adjustedPosition;
                Vector3 waterBottomRayDirection = Vector3.Down;
                
                bool hasWaterBottom = Physics.RayCast(waterBottomRayStart, waterBottomRayDirection, out RayCastHit waterBottomHit, GroundCheckDistance, GroundLayers);
                
                if (hasWaterSurface && hasWaterBottom)
                {// 同时检测到水面和水底
                    float safeWaterSurfaceY = waterSurfaceHit.Point.Y - MinWaterDepth;
                    float safeWaterBottomY = waterBottomHit.Point.Y + MinGroundHeight;
                    
                    // 限制相机在水面下和水底上之间
                    if (adjustedPosition.Y > safeWaterSurfaceY)
                    {
                        adjustedPosition.Y = safeWaterSurfaceY;
                        //Debug.Log($"[水下检测] 调整相机高度以保持在水面下: {position.Y:F2} -> {safeWaterSurfaceY:F2}");
                    }
                    else if (adjustedPosition.Y < safeWaterBottomY)
                    {
                        adjustedPosition.Y = safeWaterBottomY;
                        //Debug.Log($"[水下检测] 调整相机高度以保持在水底上: {position.Y:F2} -> {safeWaterBottomY:F2}");
                    }
                    
                    // 检查水层是否太窄
                    float waterDepth = safeWaterSurfaceY - safeWaterBottomY;
                    if (waterDepth < 100.0f) // 100cm = 1米
                    {
                        //Debug.LogWarning($"[水下检测] 水层太窄 ({waterDepth:F2}cm)，可能影响相机视野");
                    }
                }
                else if (hasWaterBottom)
                {
                    // 只检测到水底，没有水面（可能已经离开水下）
                    float safeWaterBottomY = waterBottomHit.Point.Y + MinGroundHeight;
                    if (adjustedPosition.Y < safeWaterBottomY)
                    {
                        adjustedPosition.Y = safeWaterBottomY;
                    }
                }
            }
            
            // === 3. 悬崖边缘：从角色脚下检测 ===
            if (EnableCliffDetection && Target != null)
            {
                // 从角色脚下位置向下检测
                Vector3 characterFootPosition = Target.Position;
                Vector3 cliffRayDirection = Vector3.Down;
                
                bool hasGroundBelowCharacter = Physics.RayCast(characterFootPosition, cliffRayDirection, out RayCastHit cliffHit, CliffCheckDistance, GroundLayers);
                
                if (!hasGroundBelowCharacter)
                {
                    // 角色下方没有地面，可能是悬崖边缘
                    // 使用角色当前高度作为最低高度
                    float safeCliffY = characterFootPosition.Y + MinGroundHeight;
                    if (adjustedPosition.Y < safeCliffY)
                    {
                        adjustedPosition.Y = safeCliffY;
                        //Debug.LogWarning($"[悬崖检测] 角色下方无地面，调整相机高度: {position.Y:F2} -> {safeCliffY:F2}");
                    }
                }
                else
                {
                    // 检测到地面，使用地面高度
                    float safeGroundY = cliffHit.Point.Y + MinGroundHeight;
                    if (adjustedPosition.Y < safeGroundY)
                    {
                        adjustedPosition.Y = safeGroundY;
                    }
                }
            }
            else
            {
                // === 4. 普通地面检测 ===
                Vector3 rayStart = adjustedPosition;
                Vector3 rayDirection = Vector3.Down;
                
                if (Physics.RayCast(rayStart, rayDirection, out RayCastHit hit, GroundCheckDistance, GroundLayers))
                {
                    // 如果相机位置低于地面加上最小高度，则调整位置
                    float safeY = hit.Point.Y + MinGroundHeight;
                    if (adjustedPosition.Y < safeY)
                    {
                        adjustedPosition.Y = safeY;
                    }
                }
            }
            
            return adjustedPosition;
        }

        /// <summary>
        /// 处理输入
        /// </summary>
        private void HandleInput()
        {
            // 如果正在恢复中或拍照中，不处理用户输入
            if (_isResetting || _photoTransitioning)
            {
                return;
            }
            
            // 检查拍照快捷键
            if (EnablePhotoMode)
            {
                if (Input.GetKeyDown(TakeHeadshotKey))
                {
                    TakeHeadshot();
                    return;
                }
                if (Input.GetKeyDown(TakeFullBodyKey))
                {
                    TakeFullBodyPhoto();
                    return;
                }
                if (Input.GetKeyDown(TakeScreenshotKey))
                {
                    TakeGameScreenshot();
                    return;
                }
            }
            
            // 检查一键恢复基准视角
            if (EnableBaselineReset && Input.GetKeyDown(ResetKey))
            {
                ResetToBaseline();
                return; // 开始恢复，不处理其他输入
            }
            
            // 获取当前状态配置
            CameraStateConfig currentConfig = GetCurrentStateConfig();
            
            // 鼠标右键旋转(检查状态是否允许)
            if (currentConfig.AllowRotation && Input.GetMouseButton(MouseButton.Right))
            {
                Vector2 mouseDelta = Input.MousePositionDelta;
                Yaw += mouseDelta.X * RotationSpeed;
                Pitch -= mouseDelta.Y * RotationSpeed;
                Pitch = Mathf.Clamp(Pitch, MinPitch, MaxPitch);

                // 规范化Yaw
                while (Yaw < 0) Yaw += 360;
                while (Yaw >= 360) Yaw -= 360;
            }

            // 鼠标滚轮缩放(检查状态是否允许)
            if (currentConfig.AllowZoom)
            {
                float scrollDelta = Input.MouseScrollDelta;
                if (Mathf.Abs(scrollDelta) > 0.01f)
                {
                    Distance -= scrollDelta * ZoomSpeed;
                    
                    // ✅ 正确：滚轮缩放受环境限制约束
                    // MinDistance/MaxDistance由ApplyEnvironmentLimits设置为环境建议值
                    Distance = Mathf.Clamp(Distance, MinDistance, MaxDistance);
                    
                    //Debug.Log($"[滚轮缩放] 目标距离: {Distance:F2}m, 环境范围:[{MinDistance:F1}-{MaxDistance:F1}]");
                }
            }
        }

        /// <summary>
        /// 根据场景中相机的当前位置计算初始参数
        /// </summary>
        private void CalculateInitialParameters()
        {
            // 计算聚焦点（角色头部位置）
            Vector3 focusPoint = Target.Position + FocusOffset;
            
            // 当前相机位置
            Vector3 cameraPosition = Actor.Position;
            
            // 计算相机到聚焦点的向量
            Vector3 offset = cameraPosition - focusPoint;
            
            // 计算距离
            float calculatedDistance = offset.Length;
            
            // 只有当计算出的距离在合理范围内时才使用
            if (calculatedDistance >= MinDistance && calculatedDistance <= MaxDistance)
            {
                Distance = calculatedDistance;
            }
            else
            {
                // 距离不合理，使用默认值并警告
                //Debug.LogWarning($"[ThirdPersonCamera] 计算的距离 {calculatedDistance:F2} 超出范围，使用默认距离 {Distance:F2}");
            }
            
            // 归一化偏移向量
            if (offset.LengthSquared > 0.001f)
            {
                offset.Normalize();
                
                // 计算Pitch（俯仰角）
                // offset.Y 是垂直分量，需要转换为角度
                float calculatedPitch = Mathf.Asin(offset.Y) * Mathf.RadiansToDegrees;
                
                // 限制在有效范围内
                Pitch = Mathf.Clamp(calculatedPitch, MinPitch, MaxPitch);
                
                // 计算Yaw（水平角度）
                // 使用X和Z分量计算水平角度
                float calculatedYaw = Mathf.Atan2(offset.X, -offset.Z) * Mathf.RadiansToDegrees;
                
                // 规范化到0-360
                while (calculatedYaw < 0) calculatedYaw += 360;
                while (calculatedYaw >= 360) calculatedYaw -= 360;
                
                Yaw = calculatedYaw;
            }
            
            //Debug.Log($"[ThirdPersonCamera] 自动计算初始参数完成:");
            //Debug.Log($"  焦点位置: {focusPoint}");
            //Debug.Log($"  相机位置: {cameraPosition}");
            //Debug.Log($"  计算距离: {Distance:F2}");
            //Debug.Log($"  计算Pitch: {Pitch:F2}°");
            //Debug.Log($"  计算Yaw: {Yaw:F2}°");
        }

        /// <summary>
        /// 初始化碰撞检测射线方向
        /// 支持三种模式：
        /// - Low (1射线): 仅中心射线
        /// - Medium (5射线): 中心 + 上下左右
        /// - High (9射线): 中心 + 上下左右 + 四对角线
        /// </summary>
        private void InitializeCollisionRays()
        {
            int rayCount = (int)CollisionDetectionQuality;
            _collisionRayDirections = new Vector3[rayCount];
            
            // 中心射线
            _collisionRayDirections[0] = Vector3.Zero;
            
            // 如果只有1条射线，则只使用中心
            if (rayCount <= 1)
            {
                //Debug.Log($"[碰撞检测] 初始化完成 - 模式:Low (1射线)");
                return;
            }
            
            // 5射线模式：中心 + 上下左右
            if (rayCount >= 5)
            {
                _collisionRayDirections[1] = new Vector3(0, CollisionRadius, 0);   // 上
                _collisionRayDirections[2] = new Vector3(0, -CollisionRadius, 0);  // 下
                _collisionRayDirections[3] = new Vector3(-CollisionRadius, 0, 0);  // 左
                _collisionRayDirections[4] = new Vector3(CollisionRadius, 0, 0);   // 右
                
                if (rayCount == 5)
                {
                    //Debug.Log($"[碰撞检测] 初始化完成 - 模式:Medium (5射线)");
                    return;
                }
            }
            
            // 9射线模式：额外增加四个对角线
            if (rayCount >= 9)
            {
                float diagonal = CollisionRadius * 0.707f; // 约 = CollisionRadius / sqrt(2)
                _collisionRayDirections[5] = new Vector3(-diagonal, diagonal, 0);   // 左上
                _collisionRayDirections[6] = new Vector3(diagonal, diagonal, 0);    // 右上
                _collisionRayDirections[7] = new Vector3(-diagonal, -diagonal, 0);  // 左下
                _collisionRayDirections[8] = new Vector3(diagonal, -diagonal, 0);   // 右下
                
                //Debug.Log($"[碰撞检测] 初始化完成 - 模式:High (9射线)");
            }
        }

        /// <summary>
        /// 检查指定角度下的碰撞情况
        /// </summary>
        /// <param name="focusPoint">聚焦点</param>
        /// <param name="pitch">检测使用的俯仰角</param>
        /// <param name="safeDistance">输出的安全距离</param>
        /// <returns>是否有碰撞</returns>
        private bool CheckCollision(Vector3 focusPoint, float pitch, out float safeDistance)
        {
            safeDistance = Distance;
            
            // 性能监控：开始计时
            float checkStartTime = 0f;
            if (EnablePerformanceMonitoring)
            {
                checkStartTime = Time.UnscaledGameTime;
            }
            
            // 计算相机方向
            Vector3 idealCameraPos = CalculateCameraPosition(focusPoint, pitch, Yaw, Distance);
            Vector3 direction = idealCameraPos - focusPoint;
            float targetDistance = direction.Length;
            
            // 异常情况检查
            if (targetDistance < 0.001f)
            {
                //Debug.LogWarning("[ThirdPersonCamera] 相机方向计算异常，跳过碰撞检测");
                return false;
            }
            
            direction.Normalize();

            // === 步骤1：检查缓存是否可用 ===
            if (_collisionCache.IsValid(idealCameraPos, Distance, _gameTime))
            {
                safeDistance = _collisionCache.SafeDistance;
                
                // 性能监控：缓存命中
                if (EnablePerformanceMonitoring)
                {
                    _performanceStats.CacheHitCount++;
                    float checkTime = (Time.UnscaledGameTime - checkStartTime) * 1000f;
                    _performanceStats.CollisionCheckTime += checkTime;
                }
                
                //Debug.Log($"[碰撞缓存] 命中 - 安全距离:{safeDistance:F2}cm");
                return _collisionCache.HasObstruction;
            }
            
            // 性能监控：缓存未命中
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.CacheMissCount++;
            }

            // === 步骤2：执行多重射线检测 ===
            // 多射线检测
            bool hasCollision = false;
            int collisionCount = 0;
            string collisionObject = "";
            float minDistance = Distance;
            
            foreach (var offset in _collisionRayDirections)
            {
                // 起始点：聚焦点 + 偏移
                Vector3 startPoint = focusPoint + offset;

                // 使用Distance作为射线检测距离，使用层级掩码排除角色
                if (Physics.RayCast(startPoint, direction, out RayCastHit hit, Distance, CollisionLayerMask))
                {
                    // 检查是否需要排除Target的碰撞
                    if (AutoExcludeTargetCollision && Target != null && hit.Collider != null)
                    {
                        // 获取碰撞体所属的Actor
                        var hitActor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                        
                        // 如果碰撞的是Target本身或其子对象，则忽略此碰撞
                        if (hitActor == Target || IsChildOf(hitActor, Target))
                        {
                            continue; // 跳过此碰撞
                        }
                    }
                    
                    // 关键修复：计算碰撞点到聚焦点的距离（沿着相机方向）
                    Vector3 hitToFocus = hit.Point - focusPoint;
                    float hitDistance = hitToFocus.Length;
                    
                    // ⚠️ 关键：检查是否是"从内部碰撞"
                    // 动态计算内部碰撞阈值：使用当前相机距离的 30%，但不小于 50cm
                    float innerCollisionThreshold = Mathf.Max(_currentCollisionDistance * 0.3f, 50.0f);
                    
                    if (hitDistance < innerCollisionThreshold)
                    {
                        // 记录内部碰撞但不使用
                        if (hit.Collider != null)
                        {
                            var actor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                            string objName = actor != null ? actor.Name : "Unknown";
                            //Debug.LogWarning($"[Collision] 检测到内部碰撞 {objName} @{hitDistance:F2}cm (阈值:{innerCollisionThreshold:F2}cm, 已忽略)");
                        }
                        continue; // 忽略内部碰撞
                    }
                    
                    // 减去碰撞球体半径，留出安全距离
                    hitDistance -= CollisionRadius;
                    
                    // 严格边界检查，防止负值或过小值
                    hitDistance = Mathf.Max(hitDistance, MinDistance);
                    
                    // 找到最小距离
                    if (hitDistance < minDistance)
                    {
                        minDistance = hitDistance;
                        hasCollision = true;
                        collisionCount++;
                        
                        // 记录碰撞物体名称和距离信息
                        if (hit.Collider != null)
                        {
                            var actor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                            collisionObject = $"{(actor != null ? actor.Name : "Unknown")} @{hitDistance:F2}cm";
                        }
                    }
                }
            }
            
            // === 步骤3：球形碰撞二次确认 ===
            if (EnableSphereCollisionCheck && !hasCollision)
            {
                // 在理想相机位置进行球形碰撞检测
                Vector3 testPosition = focusPoint + direction * minDistance;
                if (Physics.CheckSphere(testPosition, CollisionSphereRadius, CollisionLayerMask))
                {
                    // 球形检测发现碰撞，进一步缩短距离
                    minDistance *= 0.9f; // 缩短10%
                    hasCollision = true;
                    //Debug.Log($"[球形碰撞] 检测到边缘碰撞，距离调整为:{minDistance:F2}cm");
                }
            }

            // === 步骤4：确保距离在合法范围内 ===
            // 确保距离在合法范围内
            // ✅ 关键：碰撞检测只需要确保安全距离大于系统绝对最小值
            // 不额外限制最大距离，让环境限制和用户输入去控制
            float systemMinDistance = 200.0f; // 系统绝对最小值 200cm = 2米
            minDistance = Mathf.Max(minDistance, systemMinDistance);
            safeDistance = minDistance;
            
            // === 步骤5：更新缓存 ===
            _collisionCache.LastPosition = idealCameraPos;
            _collisionCache.LastDistance = Distance;
            _collisionCache.CacheTime = _gameTime;
            _collisionCache.HasObstruction = hasCollision;
            _collisionCache.SafeDistance = safeDistance;
            
            // 调试日志(总是输出,方便调试)
            string hitInfo = hasCollision ? $", 碰撞物:{collisionObject}" : "";
            //Debug.Log($"[Collision@{pitch:F0}°] 碰撞:{hasCollision}, 碰撞数:{collisionCount}{hitInfo}, 安全距离:{minDistance:F2}, 理想距离:{Distance:F2}");
            
            // 性能监控：结束计时
            if (EnablePerformanceMonitoring)
            {
                float checkTime = (Time.UnscaledGameTime - checkStartTime) * 1000f;
                _performanceStats.CollisionCheckTime += checkTime;
            }
            
            return hasCollision;
        }

        /// <summary>
        /// 计算相机位置
        /// </summary>
        private Vector3 CalculateCameraPosition(Vector3 focusPoint, float pitch, float yaw, float distance)
        {
            return focusPoint + CalculateCameraOffset(pitch, yaw, distance);
        }
        
        /// <summary>
        /// 计算相机相对于聚焦点的偏移
        /// </summary>
        private Vector3 CalculateCameraOffset(float pitch, float yaw, float distance)
        {
            // 转换角度为弧度
            float pitchRad = pitch * Mathf.DegreesToRadians;
            float yawRad = yaw * Mathf.DegreesToRadians;

            // 计算相机偏移
            float horizontalDistance = distance * Mathf.Cos(pitchRad);
            float verticalDistance = distance * Mathf.Sin(pitchRad);

            return new Vector3(
                horizontalDistance * Mathf.Sin(yawRad),
                verticalDistance,
                -horizontalDistance * Mathf.Cos(yawRad)
            );
        }

        /// <summary>
        /// 更新动态FOV(基于角色移动速度和状态)
        /// 优先级：状态FOV > 速度FOV
        /// </summary>
        private void UpdateDynamicFOV()
        {
            float baseFOV = BaseFOV;
            
            // === 步骤1：计算状态FOV偏移 ===
            float stateFOVOffset = 0f;
            
            if (EnableStateFOV)
            {
                // 根据 PlayerController 状态和相机状态设置FOV偏移
                switch (CurrentState)
                {
                    case CameraState.Combat:
                        stateFOVOffset = CombatFOVOffset; // -5°
                        break;
                    case CameraState.Flying:
                        stateFOVOffset = FlyingFOVOffset; // +10°
                        break;
                    default:
                        // 检查是否在冲刺（通过PlayerController或速度判断）
                        bool isSprinting = _playerController != null && _playerController.IsSprinting();
                        if (!isSprinting)
                        {
                            float currentSpeed = _targetVelocity.Length;
                            isSprinting = currentSpeed > MaxSpeed * 0.8f;
                        }
                        if (isSprinting)
                        {
                            stateFOVOffset = SprintFOVOffset; // +10°
                        }
                        break;
                }
            }
            
            // === 步骤2：计算速度FOV偏移 ===
            float speedFOVOffset = 0f;
            
            if (EnableDynamicFOV && stateFOVOffset == 0f) // 只有在没有状态FOV时才使用速度FOV
            {
                float currentSpeed = _targetVelocity.Length;
                
                // 根据速度计算目标FOV
                if (currentSpeed < SpeedThreshold)
                {            // 低速或静止，使用基础FOV
                    speedFOVOffset = 0f;
                }
                else
                {
                    // 高速移动，根据速度插值FOV
                    float speedRatio = Mathf.Clamp((currentSpeed - SpeedThreshold) / (MaxSpeed - SpeedThreshold), 0f, 1f);
                    speedFOVOffset = (MaxFOV - BaseFOV) * speedRatio;
                }
            }
            
            // === 步骤3：合并FOV偏移（优先级：状态 > 速度） ===
            float totalFOVOffset = stateFOVOffset != 0f ? stateFOVOffset : speedFOVOffset;
            _targetFOV = baseFOV + totalFOVOffset;
            
            // === 步骤4：平滑过渡到目标FOV ===
            _currentFOV = Mathf.Lerp(_currentFOV, _targetFOV, Time.DeltaTime * FOVSmoothSpeed);
            
            // === 步骤5：应用到相机 ===
            if (_camera != null)
            {
                _camera.FieldOfView = _currentFOV;
            }
        }
        
        /// <summary>
        /// 更新相机抖动
        /// </summary>
        private void UpdateCameraShake()
        {
            _shakeTime += Time.DeltaTime;

            // 使用简单的随机抖动
            float x = (RandomUtil.Rand() - 0.5f) * 2f * _currentShakeIntensity;
            float y = (RandomUtil.Rand() - 0.5f) * 2f * _currentShakeIntensity;
            float z = (RandomUtil.Rand() - 0.5f) * 2f * _currentShakeIntensity;

            _shakeOffset = new Vector3(x, y, z);

            // 衰减抖动强度
            _currentShakeIntensity = Mathf.Max(0f, _currentShakeIntensity - ShakeDecaySpeed * Time.DeltaTime);
        }

        /// <summary>
        /// 触发相机抖动
        /// </summary>
        public void TriggerShake(float intensity, float duration = 1.0f)
        {
            if (!EnableCameraShake) return;

            _currentShakeIntensity = Mathf.Max(_currentShakeIntensity, intensity);
            _shakeTime = 0f;

            if (duration > 0f)
            {
                ShakeDecaySpeed = intensity / duration;
            }
        }

        /// <summary>
        /// 检查一个Actor是否是另一个Actor的子对象
        /// </summary>
        private bool IsChildOf(Actor child, Actor parent)
        {
            if (child == null || parent == null)
                return false;
            
            Actor current = child;
            while (current != null)
            {
                if (current == parent)
                    return true;
                current = current.Parent;
            }
            
            return false;
        }

        #region 公共API方法

        /// <summary>
        /// 获取当前实际距离
        /// </summary>
        public float GetCurrentDistance()
        {
            return EnableCameraCollision ? _currentCollisionDistance : Distance;
        }

        /// <summary>
        /// 设置理想距离
        /// </summary>
        public void SetIdealDistance(float distance)
        {
            Distance = Mathf.Clamp(distance, MinDistance, MaxDistance);
        }

        /// <summary>
        /// 重置相机（使用默认参数）
        /// </summary>
        public void ResetCamera()
        {
            Pitch = 30.0f;
            Yaw = 0.0f;
            Distance = 15.0f;
            _currentShakeIntensity = 0f;
        }
        
        /// <summary>
        /// 获取性能统计数据
        /// </summary>
        /// <returns>当前的性能统计数据</returns>
        public CameraPerformanceStats GetPerformanceStats()
        {
            return _performanceStats;
        }
        
        /// <summary>
        /// 重置性能统计数据
        /// </summary>
        public void ResetPerformanceStats()
        {
            _performanceStats.Reset();
            _statsResetTimer = 0f;
        }
        
        /// <summary>
        /// 保存当前视角为基准视角
        /// ⚠️ 已废弃：基准视角应为智能计算的标准视角，不应保存用户当前视角
        /// </summary>
        [Obsolete("基准视角应为智能计算的标准视角，请使用ResetToBaseline直接恢复")]
        public void SaveBaseline()
        {
            // 保留兼容性，但标记为过时
            _baselineDistance = Distance;
            _baselinePitch = Pitch;
            _baselineYaw = Yaw;
            
            //Debug.LogWarning($"[BaselineReset] SaveBaseline已废弃，基准视角应为智能计算的标准视角");
        }
        
        /// <summary>
        /// 一键恢复到基准视角
        /// 基准视角定义：
        /// - 俯视角30-45°（默认35°）
        /// - 位于角色正后方
        /// - 开阔空间距离8-10米，其他环境根据检测调整
        /// </summary>
        public void ResetToBaseline()
        {
            // 计算智能基准视角
            CalculateSmartBaseline();
            
            _isResetting = true;
            //Debug.Log($"[BaselineReset] 开始恢复基准视角 - Distance:{_baselineDistance:F1}m, Pitch:{_baselinePitch:F1}°");
        }
        
        /// <summary>
        /// 计算智能基准视角
        /// 基准视角定义：
        /// - 俯视角30-45°（默认35°）
        /// - 位于角色正后方（水平角度=角色朝向+180°）
        /// - 相机与地面保持水平（Roll=0）
        /// - 开阔空间距离8-10米，其他环境根据检测调整
        /// </summary>
        private void CalculateSmartBaseline()
        {
            if (Target == null) return;
            
            // 1. 固定俯视角（35°，能看到角色全貌）
            _baselinePitch = 35.0f;
            
            // 2. 角色正后方（相机与地面保持水平，Roll=0）
            if (Target != null)
            {
                // 获取角色当前朝向的Yaw角
                float characterYaw = Target.Orientation.EulerAngles.Y;
                // 正后方 = 角色朝向 + 180°
                _baselineYaw = (characterYaw + 180f) % 360f;
            }
            else
            {
                _baselineYaw = 180f; // 默认后方
            }
            
            // 3. 根据环境自适应距离（单位：厝米cm）
            switch (CurrentEnvironment)
            {
                case EnvironmentType.Outdoor:
                case EnvironmentType.Aerial:
                    // 开阔空间：800-1000cm (8-10米)
                    _baselineDistance = 900.0f;
                    break;
                    
                case EnvironmentType.Indoor:
                case EnvironmentType.Cave:
                    // 室内/洞穴：600-700cm (6-7米)
                    _baselineDistance = 650.0f;
                    break;
                    
                case EnvironmentType.Corridor:
                    // 走廊：500cm (5米)
                    _baselineDistance = 500.0f;
                    break;
                    
                case EnvironmentType.Underwater:
                    // 水下：700cm (7米)
                    _baselineDistance = 700.0f;
                    break;
                    
                default:
                    _baselineDistance = 800.0f; // 800cm = 8米
                    break;
            }
            
            // 4. 碰撞检测微调（确保不会被遮挡）
            Vector3 testFocusPoint = Target.Position + FocusOffset;
            if (CheckCollision(testFocusPoint, _baselinePitch, out float safeDistance))
            {
                // 如果基准距离会碰撞，使用安全距离
                _baselineDistance = Mathf.Min(_baselineDistance, safeDistance);
                //Debug.Log($"[BaselineReset] 碰撞调整：{_baselineDistance:F1}cm -> {safeDistance:F1}cm");
            }
            
            //Debug.Log($"[BaselineReset] 智能基准视角 - 环境:{CurrentEnvironment}, Distance:{_baselineDistance:F1}cm, Pitch:{_baselinePitch:F1}°, Yaw:{_baselineYaw:F1}°");
        }
        
        /// <summary>
        /// 更新自动对齐
        /// </summary>
        private void UpdateAutoAlign()
        {
            if (Target == null) return;
            
            // 检查是否正在手动旋转
            bool isManualRotating = Input.GetMouseButton(MouseButton.Right);
            
            // 检查是否在战斗中（如果启用了此选项）
            bool isInCombat = DisableAlignInCombat && CurrentState == CameraState.Combat;
            
            // 检查角色移动速度
            float characterSpeed = _targetVelocity.Length;
            bool isCharacterMoving = characterSpeed > AlignMinSpeed;
            
            // 如果正在手动旋转、处于战斗中或角色静止，重置计时器
            if (isManualRotating || isInCombat || !isCharacterMoving)
            {
                _alignTimer = 0f;
                _isAligning = false;
                _wasManualRotating = isManualRotating;
                return;
            }
            
            // 如果刚刚停止手动旋转，开始计时
            if (_wasManualRotating && !isManualRotating)
            {
                _alignTimer = 0f;
                //Debug.Log($"[自动对齐] 开始计时，{AlignDelay:F1}秒后对齐");
            }
            
            _wasManualRotating = isManualRotating;
            
            // 累加计时器
            _alignTimer += Time.DeltaTime;
            
            // 如果达到延迟时间，开始对齐
            if (_alignTimer >= AlignDelay)
            {
                if (!_isAligning)
                {
                    // 开始对齐，计算目标角度
                    float characterYaw = Target.Orientation.EulerAngles.Y;
                    _targetAlignYaw = (characterYaw + 180f) % 360f; // 角色正后方
                    _isAligning = true;
                    //Debug.Log($"[自动对齐] 开始对齐到角色后方 - 当前:{Yaw:F1}°, 目标:{_targetAlignYaw:F1}°");
                }
                
                // 执行对齐
                PerformAutoAlign();
            }
        }
        
        /// <summary>
        /// 执行自动对齐（平滑旋转到目标角度）
        /// </summary>
        private void PerformAutoAlign()
        {
            if (Target == null) return;
            
            // 计算角度差（选择最短路径）
            float yawDiff = _targetAlignYaw - Yaw;
            while (yawDiff > 180f) yawDiff -= 360f;
            while (yawDiff < -180f) yawDiff += 360f;
            
            // 检查是否已经接近目标
            if (Mathf.Abs(yawDiff) < 0.5f)
            {
                // 对齐完成
                Yaw = _targetAlignYaw;
                _smoothYaw = _targetAlignYaw;
                _isAligning = false;
                //Debug.Log($"[自动对齐] ✅ 对齐完成 - 最终角度:{Yaw:F1}°");
                return;
            }
            
            // 使用EaseInOut曲线平滑过渡
            float rotationStep = AlignRotationSpeed * Time.DeltaTime;
            
            // 在接近目标时减速（AlignSmoothRange范围内）
            if (Mathf.Abs(yawDiff) < AlignSmoothRange)
            {
                float t = Mathf.Abs(yawDiff) / AlignSmoothRange;
                // EaseInOut曲线
                t = t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
                rotationStep *= t;
            }
            
            // 限制每帧旋转角度不超过剩余角度
            rotationStep = Mathf.Min(rotationStep, Mathf.Abs(yawDiff));
            
            // 应用旋转
            Yaw += Mathf.Sign(yawDiff) * rotationStep;
            
            // 规范化Yaw
            while (Yaw < 0) Yaw += 360;
            while (Yaw >= 360) Yaw -= 360;
            
            // 同步到_smoothYaw
            _smoothYaw = Yaw;
            
            //Debug.Log($"[自动对齐] 旋转中... 当前:{Yaw:F1}°, 目标:{_targetAlignYaw:F1}°, 差值:{yawDiff:F1}°");
        }
        
        /// <summary>
        /// 更新基准视角恢复进度
        /// </summary>
        private void UpdateBaselineReset()
        {
            if (Target == null) return;
            
            // 平滑过渡到基准值
            float lerpSpeed = Time.DeltaTime * ResetSmoothSpeed;
            
            Distance = Mathf.Lerp(Distance, _baselineDistance, lerpSpeed);
            Pitch = Mathf.Lerp(Pitch, _baselinePitch, lerpSpeed);
            
            // Yaw需要特殊处理，选择最短路径
            float yawDiff = _baselineYaw - Yaw;
            if (yawDiff > 180f) yawDiff -= 360f;
            if (yawDiff < -180f) yawDiff += 360f;
            Yaw += yawDiff * lerpSpeed;
            
            // 规范化Yaw
            while (Yaw < 0) Yaw += 360;
            while (Yaw >= 360) Yaw -= 360;
            
            // ✅ 关键修复：同步_smoothYaw，避免弹性跟随干扰
            _smoothYaw = Yaw;
            
            // ✅ 关键修复：直接设置_currentCollisionDistance以突破环境限制
            _currentCollisionDistance = Distance;
            
            // ✅ 关键：恢复期间也需要更新相机位置和朝向
            Vector3 focusPoint = Target.Position + FocusOffset;
            Vector3 cameraPosition = CalculateCameraPosition(focusPoint, Pitch, Yaw, Distance);
            Actor.Position = cameraPosition;
            
            // ⚠️ 关键：确保相机与地面保持水平（Roll=0）
            // 计算朝向向量
            Vector3 direction = focusPoint - cameraPosition;
            direction.Normalize();
            
            // 使用Quaternion.LookRotation设置相机朝向，确保Up向量为世界空间的Up（保持水平）
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.Up);
            Actor.Orientation = targetRotation;
            
            // Debug日志：显示当前恢复进度
            //Debug.Log($"[BaselineReset] 恢复中... Distance:{Distance:F2}/{_baselineDistance:F2}, Pitch:{Pitch:F1}/{_baselinePitch:F1}, Yaw:{Yaw:F1}/{_baselineYaw:F1}");
            
            // 检查是否已经接近目标值
            float distanceDiff = Mathf.Abs(Distance - _baselineDistance);
            float pitchDiff = Mathf.Abs(Pitch - _baselinePitch);
            float yawDiffAbs = Mathf.Abs(yawDiff);
            
            if (distanceDiff < 0.1f && pitchDiff < 0.5f && yawDiffAbs < 0.5f)
            {
                // 恢复完成
                Distance = _baselineDistance;
                Pitch = _baselinePitch;
                Yaw = _baselineYaw;
                _smoothYaw = _baselineYaw;
                _currentCollisionDistance = _baselineDistance;
                _isResetting = false;
                
                //Debug.Log($"[BaselineReset] ✅ 基准视角恢复完成");
            }
        }
        
        /// <summary>
        /// 拍摄42寸头像照片
        /// </summary>
        public void TakeHeadshot()
        {
            if (_photoTransitioning || Target == null) return;
            
            //Debug.Log("[拍照模式] 开始拍摄42寸头像...");
            StartPhotoMode(HeadshotDistance, HeadshotPitch, 180f); // 正面
        }
        
        /// <summary>
        /// 拍摄全身正面照
        /// </summary>
        public void TakeFullBodyPhoto()
        {
            if (_photoTransitioning || Target == null) return;
            
            //Debug.Log("[拍照模式] 开始拍摄全身照...");
            StartPhotoMode(FullBodyDistance, FullBodyPitch, 180f); // 正面
        }
        
        /// <summary>
        /// 游戏界面截图
        /// </summary>
        public void TakeGameScreenshot()
        {
            string filename = GenerateScreenshotFilename("Game");
            CaptureScreenshot(filename);
            //Debug.Log($"[截图] 游戏截图已保存: {filename}");
        }
        
        /// <summary>
        /// 开始拍照模式
        /// </summary>
        private void StartPhotoMode(float targetDistance, float targetPitch, float targetYaw)
        {
            // 保存当前视角
            _prePhotoDistance = Distance;
            _prePhotoPitch = Pitch;
            _prePhotoYaw = Yaw;
            
            // 设置目标视角
            _targetPhotoDistance = targetDistance;
            _targetPhotoPitch = targetPitch;
            
            // 计算目标Yaw（角色当前朴向 + 180° = 正面）
            if (Target != null)
            {
                // 获取角色当前朴向的Yaw角（假设角色有Orientation属性）
                float characterYaw = Target.Orientation.EulerAngles.Y;
                _prePhotoYaw = (characterYaw + targetYaw) % 360f;
            }
            
            _isPhotoMode = true;
            _photoTransitioning = true;
        }
        
        /// <summary>
        /// 更新拍照模式过渡
        /// </summary>
        private void UpdatePhotoTransition()
        {
            if (!_photoTransitioning) return;
            
            float lerpSpeed = Time.DeltaTime * PhotoReturnSpeed;
            
            // 平滑过渡到目标视角
            Distance = Mathf.Lerp(Distance, _targetPhotoDistance, lerpSpeed);
            Pitch = Mathf.Lerp(Pitch, _targetPhotoPitch, lerpSpeed);
            
            // Yaw特殊处理
            float yawDiff = _prePhotoYaw - Yaw;
            if (yawDiff > 180f) yawDiff -= 360f;
            if (yawDiff < -180f) yawDiff += 360f;
            Yaw += yawDiff * lerpSpeed;
            
            // 规范化Yaw
            while (Yaw < 0) Yaw += 360;
            while (Yaw >= 360) Yaw -= 360;
            
            // 检查是否到达目标
            float distanceDiff = Mathf.Abs(Distance - _targetPhotoDistance);
            float pitchDiff = Mathf.Abs(Pitch - _targetPhotoPitch);
            
            if (distanceDiff < 0.05f && pitchDiff < 0.5f)
            {
                // 到达目标，拍照
                Distance = _targetPhotoDistance;
                Pitch = _targetPhotoPitch;
                _photoTransitioning = false;
                
                // 优先级控制：拍照完成，开始恢复优先级（在返回时完全降级）
                // 注意：这里不降级，等待返回原视角完成后再降级
                
                // 等待一帧后截图
                Task.Run(async () =>
                {
                    await Task.Delay(100); // 等待100ms确保画面稳定
                    
                    string filename = _targetPhotoDistance == HeadshotDistance 
                        ? GenerateScreenshotFilename("Headshot")
                        : GenerateScreenshotFilename("FullBody");
                    
                    CaptureScreenshot(filename);
                    //Debug.Log($"[拍照模式] ✅ 照片已保存: {filename}");
                    
                    // 开始返回原视角
                    await Task.Delay(200);
                    ReturnFromPhotoMode();
                });
            }
        }
        
        /// <summary>
        /// 从拍照模式返回
        /// </summary>
        private void ReturnFromPhotoMode()
        {
            _targetPhotoDistance = _prePhotoDistance;
            _targetPhotoPitch = _prePhotoPitch;
            // Yaw已经在_prePhotoYaw中
            
            _photoTransitioning = true;
            _isPhotoMode = false;
            
            //Debug.Log("[拍照模式] 返回原视角...");
        }
        
        /// <summary>
        /// 生成截图文件名
        /// </summary>
        private string GenerateScreenshotFilename(string prefix)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return $"{ScreenshotPath}/{prefix}_{timestamp}.png";
        }
        
        /// <summary>
        /// 捕获屏幕截图
        /// </summary>
        private void CaptureScreenshot(string filepath)
        {
            try
            {
                // 使用Flax Engine的截图 API
                Screenshot.Capture(filepath);
            }
            catch (System.Exception ex)
            {
                //Debug.LogError($"[截图] 截图失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 确保截图目录存在
        /// </summary>
        private void EnsureScreenshotDirectory()
        {
            try
            {
                string fullPath = System.IO.Path.Combine(Globals.ProjectFolder, ScreenshotPath);
                if (!System.IO.Directory.Exists(fullPath))
                {
                    System.IO.Directory.CreateDirectory(fullPath);
                    //Debug.Log($"[截图] 创建截图目录: {fullPath}");
                }
            }
            catch (System.Exception ex)
            {
                //Debug.LogError($"[截图] 创建目录失败: {ex.Message}");
            }
        }
        
        #endregion
        
        #region 状态机系统
        
        /// <summary>
        /// 初始化状态机
        /// </summary>
        private void InitializeStateMachine()
        {
            // 创建状态配置字典
            _stateConfigs = new Dictionary<CameraState, CameraStateConfig>
            {
                { CameraState.Normal, NormalStateConfig },
                { CameraState.Combat, CombatStateConfig },
                { CameraState.Climbing, ClimbingStateConfig },
                { CameraState.Swimming, SwimmingStateConfig },
                { CameraState.Flying, FlyingStateConfig },
                { CameraState.Cutscene, CutsceneStateConfig }
            };
            
            // 初始化状态
            _previousState = CurrentState;
            _isTransitioning = false;
            
            // 初始化目标值
            CameraStateConfig initialConfig = GetCurrentStateConfig();
            _stateTargetDistance = initialConfig.TargetDistance;
            _stateTargetPitch = initialConfig.TargetPitch;
            _stateTargetFOV = initialConfig.FOVOverride > 0 ? initialConfig.FOVOverride : BaseFOV;
            
            //Debug.Log($"[状态机] 初始化完成 - 当前状态: {CurrentState}");
        }
        
        /// <summary>
        /// 更新状态机(自动检测和切换)
        /// </summary>
        private void UpdateStateMachine()
        {
            if (Target == null) return;
            
            // 获取 PlayerController 引用（如果尚未获取）
            if (_playerController == null)
            {
                _playerController = Target.GetScript<PlayerController>();
            }
            
            // 根据角色状态自动检测应该切换到哪个相机状态
            CameraState detectedState = DetectCameraState();
            
            if (detectedState != CurrentState)
            {
                SwitchState(detectedState);
            }
        }
        
        /// <summary>
        /// 检测应该使用的相机状态
        /// </summary>
        private CameraState DetectCameraState()
        {
            // 根据 PlayerController 的角色状态检测相机状态
            if (_playerController != null)
            {
                var characterState = _playerController.CurrentState;
                
                // 攀爬状态检测
                if (characterState == Horizon.Game.Message.Enums.CharacterState.Crouching)
                {
                    // 蹲伏可能是攀爬准备，检查是否有攀爬控制器
                    var climbingController = Target.GetScript<ClimbingSystem.ClimbingController>();
                    if (climbingController != null && climbingController.IsClimbing())
                    {
                        return CameraState.Climbing;
                    }
                }
            }
            
            // 速度检测
            float speed = _targetVelocity.Length;
            
            // 高速移动且在高处可能是飞行
            if (speed > 30f && Target.Position.Y > 10f)
            {
                return CameraState.Flying;
            }
            
            // 水下环境检测
            if (CurrentEnvironment == EnvironmentType.Underwater)
            {
                return CameraState.Swimming;
            }
            
            // 默认返回Normal状态
            return CameraState.Normal;
        }
        
        /// <summary>
        /// 切换相机状态
        /// </summary>
        public void SwitchState(CameraState newState)
        {
            if (!EnableStateMachine)
            {
                //Debug.LogWarning("[状态机] 状态机未启用，无法切换状态");
                return;
            }
            
            if (newState == CurrentState)
            {
                return; // 已经是目标状态
            }
            
            _previousState = CurrentState;
            CurrentState = newState;
            
            // 获取新状态配置
            CameraStateConfig newConfig = GetCurrentStateConfig();
            
            // 设置目标值
            _stateTargetDistance = newConfig.TargetDistance;
            _stateTargetPitch = newConfig.TargetPitch;
            _stateTargetFOV = newConfig.FOVOverride > 0 ? newConfig.FOVOverride : BaseFOV;
            
            // 开始过渡
            _isTransitioning = true;
            
            //Debug.Log($"[状态机] 切换状态: {_previousState} → {CurrentState}");
        }
        
        /// <summary>
        /// 更新状态过渡
        /// </summary>
        private void UpdateStateTransition()
        {
            CameraStateConfig currentConfig = GetCurrentStateConfig();
            float transitionSpeed = Time.DeltaTime * currentConfig.TransitionSpeed;
            
            // 平滑过渡距离
            Distance = Mathf.Lerp(Distance, _stateTargetDistance, transitionSpeed);
            
            // 平滑过渡俯仰角
            Pitch = Mathf.Lerp(Pitch, _stateTargetPitch, transitionSpeed);
            
            // 平滑过渡FOV(如果有覆盖值)
            if (currentConfig.FOVOverride > 0)
            {
                _targetFOV = _stateTargetFOV;
                _currentFOV = Mathf.Lerp(_currentFOV, _targetFOV, transitionSpeed);
                if (_camera != null)
                {
                    _camera.FieldOfView = _currentFOV;
                }
            }
            
            // 检查是否完成过渡
            float distanceDiff = Mathf.Abs(Distance - _stateTargetDistance);
            float pitchDiff = Mathf.Abs(Pitch - _stateTargetPitch);
            
            if (distanceDiff < 0.1f && pitchDiff < 0.5f)
            {
                // 过渡完成
                Distance = _stateTargetDistance;
                Pitch = _stateTargetPitch;
                _isTransitioning = false;
                
                //Debug.Log($"[状态机] ✅ 状态过渡完成: {CurrentState}");
            }
        }
        
        /// <summary>
        /// 获取当前状态配置
        /// </summary>
        private CameraStateConfig GetCurrentStateConfig()
        {
            if (_stateConfigs != null && _stateConfigs.ContainsKey(CurrentState))
            {
                return _stateConfigs[CurrentState];
            }
            
            // 如果未初始化或找不到,返回Normal配置
            return NormalStateConfig;
        }
        
        /// <summary>
        /// 获取当前状态
        /// </summary>
        public CameraState GetCurrentState()
        {
            return CurrentState;
        }
        
        /// <summary>
        /// 强制设置状态(用于外部调用,如过场动画)
        /// </summary>
        public void ForceSetState(CameraState state)
        {
            SwitchState(state);
        }

        #endregion
        
        #region 环境感知系统
        
        /// <summary>
        /// 初始化环境感知系统
        /// </summary>
        private void InitializeEnvironmentAwareness()
        {
            // 创建环境配置字典
            _environmentConfigs = new Dictionary<EnvironmentType, EnvironmentConfig>
            {
                { EnvironmentType.Outdoor, OutdoorConfig },
                { EnvironmentType.Indoor, IndoorConfig },
                { EnvironmentType.Underwater, UnderwaterConfig },
                { EnvironmentType.Aerial, AerialConfig },
                { EnvironmentType.Cave, CaveConfig },
                { EnvironmentType.Corridor, CorridorConfig }
            };
            
            // 初始化环境状态
            _previousEnvironment = CurrentEnvironment;
            _environmentDetectionTimer = 0f;
            _currentLightLevel = 1.0f; // 默认明亮
            
            // 应用初始环境配置
            ApplyEnvironmentConfig(CurrentEnvironment);
            
            // 初始化天气系统
            UpdateWeatherEffects();
            
            //Debug.Log($"[环境感知] 初始化完成 - 当前环境: {CurrentEnvironment}, 天气: {CurrentWeather}");
        }
        
        /// <summary>
        /// 更新环境感知
        /// </summary>
        private void UpdateEnvironmentAwareness()
        {
            // 更新检测计时器
            _environmentDetectionTimer += Time.DeltaTime;
            
            // 每隔一段时间检测一次环境
            if (_environmentDetectionTimer >= EnvironmentDetectionInterval)
            {
                _environmentDetectionTimer = 0f;
                
                // 性能监控：环境检测计数
                if (EnablePerformanceMonitoring)
                {
                    _performanceStats.EnvironmentCheckCount++;
                }
                
                // 检测环境类型
                EnvironmentType detectedEnvironment = DetectEnvironmentType();
                
                // 如果环境发生变化
                if (detectedEnvironment != CurrentEnvironment)
                {
                    SwitchEnvironment(detectedEnvironment);
                }
                
                // 检测光照级别
                if (EnableLightDetection)
                {
                    DetectLightLevel();
                }
            }
            
            // 应用环境限制(每帧更新)
            ApplyEnvironmentLimits();
        }
        
        /// <summary>
        /// 检测环境类型
        /// </summary>
        private EnvironmentType DetectEnvironmentType()
        {
            if (Target == null) return EnvironmentType.Unknown;
            
            Vector3 targetPos = Target.Position;
            
            // 优先检测特殊环境(高度判断)
            if (targetPos.Y > 100f)
            {
                //Debug.Log($"[环境检测] 高度{targetPos.Y:F1}m > 100m → Aerial");
                return EnvironmentType.Aerial;
            }
            else if (targetPos.Y < -10f)
            {
                //Debug.Log($"[环境检测] 高度{targetPos.Y:F1}m < -10m → Underwater");
                return EnvironmentType.Underwater;
            }
            
            // 检测封闭度
            bool isEnclosed = DetectEnclosedSpace();
            
            if (!isEnclosed)
            {
                //Debug.Log($"[环境检测] 非封闭空间 → Outdoor");
                return EnvironmentType.Outdoor;
            }
            
            // 封闭空间,检测空间大小
            float spaceSize = EstimateSpaceSize();
            //Debug.Log($"[环境检测] 封闭空间,空间大小:{spaceSize:F1}m");
            
            if (spaceSize < 10f)
            {
                //Debug.Log($"[环境检测] 空间<10m → Corridor");
                return EnvironmentType.Corridor;
            }
            else if (spaceSize < 50f)
            {
                //Debug.Log($"[环境检测] 空间<50m → Indoor");
                return EnvironmentType.Indoor;
            }
            else
            {
                //Debug.Log($"[环境检测] 空间≥50m → Cave");
                return EnvironmentType.Cave;
            }
        }
        
        /// <summary>
        /// 检测是否在封闭空间内
        /// </summary>
        private bool DetectEnclosedSpace()
        {
            if (Target == null) return false;
            
            // 从角色头部偏上一点开始检测,避免检测到角色自身
            Vector3 targetPos = Target.Position + Vector3.Up * 2.0f;
            
            // 多方向射线检测(上、前、后、左、右5个方向)
            // 注意:不检测向下,因为地面总是存在的,不应计入封闭度
            Vector3[] directions = new Vector3[]
            {
                Vector3.Up,       // 上(检测天花板/屋顶)
                Vector3.Forward,  // 前
                Vector3.Backward, // 后
                Vector3.Left,     // 左
                Vector3.Right     // 右
            };
            
            float[] distances = new float[] { 100f, 50f, 50f, 50f, 50f };
            
            int blockedCount = 0;
            float blockedTotalDistance = 0f; // 只统计有障碍物的距离
            
            for (int i = 0; i < directions.Length; i++)
            {
                // 使用层级掩码排除角色自身碰撞
                if (Physics.RayCast(targetPos, directions[i], out RayCastHit hit, distances[i], CollisionLayerMask))
                {
                    // 检查是否需要排除Target的碰撞
                    if (AutoExcludeTargetCollision && Target != null && hit.Collider != null)
                    {
                        // 获取碰撞体所属的Actor
                        var hitActor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                        
                        // 如果碰撞的是Target本身或其子对象,则忽略此碰撞
                        if (hitActor == Target || IsChildOf(hitActor, Target))
                        {
                            continue; // 跳过角色自身
                        }
                    }
                    
                    // 过滤掉距离过近的碰撞(可能是角色身上的装备等)
                    if (hit.Distance < 0.5f)
                    {
                        continue;
                    }
                    
                    blockedCount++;
                    blockedTotalDistance += hit.Distance;
                }
            }
            
            // 计算封闭度(0-1),基于5个方向(根据规范:5个方向中至少3个被遮挡 = 60%)
            float enclosureRatio = (float)blockedCount / directions.Length;
            
            // 计算被阻挡方向的平均距离
            // 修复:当无碰撞时应使用最大探测距离,避免误判开阔空间为封闭空间
            float avgBlockedDistance;
            if (blockedCount > 0)
            {
                avgBlockedDistance = blockedTotalDistance / blockedCount;
            }
            else
            {
                // 所有方向都开阔,使用最大探测距离
                avgBlockedDistance = 100f;
            }
            
            // 调试日志
            //Debug.Log($"[封闭度检测] 阻挡:{blockedCount}/5, 封闭度:{enclosureRatio*100:F0}%, 障碍平均距离:{avgBlockedDistance:F1}m");
            
            // 判断标准(根据记忆规范优化):
            // 1. 封闭度≥60%(5个方向中至少3个被遮挡)
            // 2. 障碍物平均距离<30米(说明障碍物很近,是真正的封闭空间)
            bool isEnclosed = enclosureRatio >= 0.6f && avgBlockedDistance < 30f;
            
            //Debug.Log($"[封闭度检测] 结果: {(isEnclosed ? "封闭" : "开阔")}");
            
            return isEnclosed;
        }
        
        /// <summary>
        /// 估算空间大小
        /// </summary>
        private float EstimateSpaceSize()
        {
            if (Target == null) return 1000f;
            
            Vector3 targetPos = Target.Position + Vector3.Up * 1.0f;
            
            // 水平四个方向发射射线
            Vector3[] directions = new Vector3[]
            {
                Vector3.Forward,
                Vector3.Backward,
                Vector3.Left,
                Vector3.Right
            };
            
            float totalDistance = 0f;
            int hitCount = 0;
            float maxDetectionDistance = 100f;
            
            foreach (var dir in directions)
            {
                if (Physics.RayCast(targetPos, dir, out RayCastHit hit, maxDetectionDistance))
                {
                    totalDistance += hit.Distance;
                    hitCount++;
                }
                else
                {
                    // 未检测到障碍,认为是开阔空间
                    totalDistance += maxDetectionDistance;
                    hitCount++;
                }
            }
            
            // 返回平均距离作为空间大小指标
            return hitCount > 0 ? totalDistance / hitCount : maxDetectionDistance;
        }
        
        /// <summary>
        /// 检测光照级别
        /// </summary>
        private void DetectLightLevel()
        {
            // 尝试采样场景中的光源来估算光照级别
            if (Target != null)
            {
                var lights = Level.FindActors<Light>();
                if (lights != null && lights.Length > 0)
                {
                    float totalIntensity = 0f;
                    int nearbyLights = 0;
                    var targetPos = Target.Position;
                    
                    foreach (var light in lights)
                    {
                        if (light is DirectionalLight dirLight)
                        {
                            // 方向光影响全场景
                            totalIntensity += dirLight.Color.A * 0.5f;
                            nearbyLights++;
                        }
                        else
                        {
                            // 点光源/聚光灯：根据距离衰减
                            float dist = Vector3.Distance(targetPos, light.Position);
                            if (dist < 50f)
                            {
                                float attenuation = Mathf.Clamp01(1f - dist / 50f);
                                totalIntensity += attenuation;
                                nearbyLights++;
                            }
                        }
                    }
                    
                    if (nearbyLights > 0)
                    {
                        _currentLightLevel = Mathf.Clamp01(totalIntensity / nearbyLights);
                        return;
                    }
                }
            }
            
            // 回退：根据环境类型设置默认亮度
            switch (CurrentEnvironment)
            {
                case EnvironmentType.Outdoor:
                    _currentLightLevel = 1.0f;
                    break;
                case EnvironmentType.Indoor:
                    _currentLightLevel = 0.7f;
                    break;
                case EnvironmentType.Cave:
                    _currentLightLevel = 0.2f;
                    break;
                case EnvironmentType.Underwater:
                    _currentLightLevel = 0.5f;
                    break;
                default:
                    _currentLightLevel = 0.8f;
                    break;
            }
        }
        
        /// <summary>
        /// 切换环境
        /// </summary>
        public void SwitchEnvironment(EnvironmentType newEnvironment)
        {
            if (newEnvironment == CurrentEnvironment)
            {
                return;
            }
            
            _previousEnvironment = CurrentEnvironment;
            CurrentEnvironment = newEnvironment;
            
            // 应用新环境配置
            ApplyEnvironmentConfig(newEnvironment);
            
            //Debug.Log($"[环境感知] 环境切换: {_previousEnvironment} → {CurrentEnvironment}");
        }
        
        /// <summary>
        /// 应用环境配置
        /// </summary>
        private void ApplyEnvironmentConfig(EnvironmentType environment)
        {
            if (!_environmentConfigs.ContainsKey(environment))
            {
                return;
            }
            
            EnvironmentConfig config = _environmentConfigs[environment];
            
            // 设置环境限制
            _environmentMaxDistance = config.MaxDistanceLimit;
            _environmentMinDistance = config.MinDistanceLimit;
            
            // 应用FOV调整
            if (config.FOVMultiplier != 1.0f)
            {
                _targetFOV = BaseFOV * config.FOVMultiplier;
            }
            
            // 应用弹性系数调整
            // TODO: 可以添加一个临时弹性系数字段
            
            //Debug.Log($"[环境配置] 应用配置 - 距离限制:[{_environmentMinDistance:F1}-{_environmentMaxDistance:F1}], FOV倍数:{config.FOVMultiplier:F2}");
        }
        
        /// <summary>
        /// 应用环境限制(每帧调用)
        /// ⚠️ 关键修改：相机可见性优先级高于环境限制
        /// 环境限制仅作为"建议"，不强制截断Distance
        /// </summary>
        private void ApplyEnvironmentLimits()
        {
            // 注意：这个方法在UpdateCameraPosition之前调用
            // 它只是更新MinDistance和MaxDistance的范围建议
            // 不直接修改Distance（用户滚轮输入的目标距离）
            
            // 更新MinDistance和MaxDistance为环境建议值
            // 但不强制Clamp Distance
            MaxDistance = _environmentMaxDistance;
            MinDistance = _environmentMinDistance;
            
            // //Debug.Log($"[环境限制] 建议范围:[{MinDistance:F1}-{MaxDistance:F1}], 当前Distance:{Distance:F1}");
        }
        
        /// <summary>
        /// 更新天气效果
        /// </summary>
        private void UpdateWeatherEffects()
        {
            if (!EnableWeatherIntegration)
            {
                _weatherVisibilityFactor = 1.0f;
                return;
            }
            
            // 根据天气设置可见度系数
            switch (CurrentWeather)
            {
                case WeatherType.Clear:
                    _weatherVisibilityFactor = 1.0f;
                    break;
                case WeatherType.Rain:
                    _weatherVisibilityFactor = RainVisibilityFactor;
                    break;
                case WeatherType.Fog:
                    _weatherVisibilityFactor = FogVisibilityFactor;
                    break;
                case WeatherType.Snow:
                    _weatherVisibilityFactor = 0.9f;
                    break;
                case WeatherType.Sandstorm:
                    _weatherVisibilityFactor = 0.5f;
                    break;
            }
            
            // 应用天气影响到最大距离
            float weatherAffectedMaxDistance = _environmentMaxDistance * _weatherVisibilityFactor;
            MaxDistance = Mathf.Min(MaxDistance, weatherAffectedMaxDistance);
        }
        
        /// <summary>
        /// 设置天气(供外部调用)
        /// </summary>
        public void SetWeather(WeatherType weather)
        {
            if (CurrentWeather == weather)
            {
                return;
            }
            
            CurrentWeather = weather;
            UpdateWeatherEffects();
            
            //Debug.Log($"[环境感知] 天气变化: {weather}, 可见度系数: {_weatherVisibilityFactor:F2}");
        }
        
        /// <summary>
        /// 获取当前环境
        /// </summary>
        public EnvironmentType GetCurrentEnvironment()
        {
            return CurrentEnvironment;
        }
        
        /// <summary>
        /// 获取当前光照级别
        /// </summary>
        public float GetCurrentLightLevel()
        {
            return _currentLightLevel;
        }
        
        /// <summary>
        /// 是否处于暗环境
        /// </summary>
        public bool IsInDarkEnvironment()
        {
            return _currentLightLevel < DarkEnvironmentThreshold;
        }

        #endregion
        
        #region 异步检测系统
        
        /// <summary>
        /// 初始化异步检测系统
        /// </summary>
        private void InitializeAsyncDetection()
        {
            _lastGroundResult = new AsyncDetectionResult();
            _lastEnvironmentResult = new AsyncDetectionResult();
            _lastCollisionResult = new AsyncCollisionResult();
            
            _lastAsyncGroundCheckTime = 0f;
            _lastAsyncEnvironmentCheckTime = 0f;
            _lastAsyncCollisionCheckTime = 0f;
            
            //Debug.Log($"[异步检测] 初始化完成 - Ground:{EnableAsyncGroundDetection}, Environment:{EnableAsyncEnvironmentDetection}, Collision:{EnableAsyncCollisionDetection}");
        }
        
        /// <summary>
        /// 异步地面检测
        /// </summary>
        private async Task<AsyncDetectionResult> DetectGroundAsync(Vector3 position, float checkDistance)
        {
            var result = new AsyncDetectionResult();
            
            // 在后台线程执行射线检测
            await Task.Run(() =>
            {
                result.GroundDetected = Physics.RayCast(position, Vector3.Down, out RayCastHit hit, checkDistance, GroundLayers);
                result.GroundHeight = result.GroundDetected ? hit.Point.Y : 0f;
                result.DetectionTime = _gameTime;
            });
            
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.GroundCheckCount++;
            }
            
            return result;
        }
        
        /// <summary>
        /// 异步环境检测（天花板、水面等）
        /// </summary>
        private async Task<AsyncDetectionResult> DetectEnvironmentAsync(Vector3 position)
        {
            var result = new AsyncDetectionResult();
            
            await Task.Run(() =>
            {
                // 检测天花板
                if (EnableCeilingDetection)
                {
                    result.GroundDetected = Physics.RayCast(position, Vector3.Up, out RayCastHit hit, CeilingCheckDistance, CollisionLayerMask);
                    result.GroundHeight = result.GroundDetected ? hit.Point.Y : float.MaxValue;
                }
                
                result.DetectionTime = _gameTime;
            });
            
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.EnvironmentCheckCount++;
            }
            
            return result;
        }
        
        /// <summary>
        /// 异步碰撞检测（适用于高质量模式）
        /// </summary>
        private async Task<AsyncCollisionResult> DetectCollisionAsync(Vector3 focusPoint, float pitch, float yaw, float distance)
        {
            var result = new AsyncCollisionResult();
            
            await Task.Run(() =>
            {
                Vector3 idealCameraPos = CalculateCameraPosition(focusPoint, pitch, yaw, distance);
                Vector3 direction = idealCameraPos - focusPoint;
                float targetDistance = direction.Length;
                
                if (targetDistance < 0.001f)
                {
                    result.HasCollision = false;
                    result.SafeDistance = distance;
                    result.DetectionTime = _gameTime;
                    return;
                }
                
                direction.Normalize();
                
                bool hasCollision = false;
                float minDistance = distance;
                
                // 执行多重射线检测
                foreach (var offset in _collisionRayDirections)
                {
                    Vector3 startPoint = focusPoint + offset;
                    
                    if (Physics.RayCast(startPoint, direction, out RayCastHit hit, distance, CollisionLayerMask))
                    {
                        // 排除角色碰撞
                        if (AutoExcludeTargetCollision && Target != null && hit.Collider != null)
                        {
                            var hitActor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                            if (hitActor == Target || IsChildOf(hitActor, Target))
                            {
                                continue;
                            }
                        }
                        
                        Vector3 hitToFocus = hit.Point - focusPoint;
                        float hitDistance = hitToFocus.Length;
                        
                        // 忽略内部碰撞
                        if (hitDistance < 100.0f)
                        {
                            continue;
                        }
                        
                        hitDistance -= CollisionRadius;
                        hitDistance = Mathf.Max(hitDistance, MinDistance);
                        
                        if (hitDistance < minDistance)
                        {
                            minDistance = hitDistance;
                            hasCollision = true;
                        }
                    }
                }
                
                result.HasCollision = hasCollision;
                result.SafeDistance = minDistance;
                result.DetectionTime = _gameTime;
            });
            
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.CollisionCheckCount++;
            }
            
            return result;
        }
        
        /// <summary>
        /// 启动异步地面检测任务
        /// </summary>
        private void StartAsyncGroundDetection()
        {
            if (!EnableAsyncGroundDetection || Target == null)
                return;
            
            // 检查是否需要重新检测
            if (_gameTime - _lastAsyncGroundCheckTime < AsyncDetectionInterval)
                return;
            
            // 检查是否有正在运行的任务
            if (_groundDetectionTask != null && !_groundDetectionTask.IsCompleted)
                return;
            
            // 启动新的检测任务
            Vector3 cameraPos = Actor.Position;
            _groundDetectionTask = DetectGroundAsync(cameraPos, GroundCheckDistance);
            _lastAsyncGroundCheckTime = _gameTime;
        }
        
        /// <summary>
        /// 启动异步环境检测任务
        /// </summary>
        private void StartAsyncEnvironmentDetection()
        {
            if (!EnableAsyncEnvironmentDetection || Target == null)
                return;
            
            if (_gameTime - _lastAsyncEnvironmentCheckTime < AsyncDetectionInterval)
                return;
            
            if (_environmentDetectionTask != null && !_environmentDetectionTask.IsCompleted)
                return;
            
            Vector3 cameraPos = Actor.Position;
            _environmentDetectionTask = DetectEnvironmentAsync(cameraPos);
            _lastAsyncEnvironmentCheckTime = _gameTime;
        }
        
        /// <summary>
        /// 启动异步碰撞检测任务
        /// </summary>
        private void StartAsyncCollisionDetection(Vector3 focusPoint, float pitch)
        {
            if (!EnableAsyncCollisionDetection || !EnableCameraCollision)
                return;
            
            if (_gameTime - _lastAsyncCollisionCheckTime < AsyncDetectionInterval)
                return;
            
            if (_collisionDetectionTask != null && !_collisionDetectionTask.IsCompleted)
                return;
            
            _collisionDetectionTask = DetectCollisionAsync(focusPoint, pitch, Yaw, Distance);
            _lastAsyncCollisionCheckTime = _gameTime;
        }
        
        /// <summary>
        /// 获取异步地面检测结果
        /// </summary>
        private bool TryGetAsyncGroundResult(out float groundHeight)
        {
            groundHeight = 0f;
            
            // 检查任务是否完成
            if (_groundDetectionTask != null && _groundDetectionTask.IsCompleted)
            {
                _lastGroundResult = _groundDetectionTask.Result;
                _groundDetectionTask = null; // 清理已完成的任务
            }
            
            // 检查结果是否有效
            if (_lastGroundResult.IsValid(_gameTime))
            {
                groundHeight = _lastGroundResult.GroundHeight;
                return _lastGroundResult.GroundDetected;
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取异步环境检测结果
        /// </summary>
        private bool TryGetAsyncEnvironmentResult(out float ceilingHeight)
        {
            ceilingHeight = float.MaxValue;
            
            if (_environmentDetectionTask != null && _environmentDetectionTask.IsCompleted)
            {
                _lastEnvironmentResult = _environmentDetectionTask.Result;
                _environmentDetectionTask = null;
            }
            
            if (_lastEnvironmentResult.IsValid(_gameTime))
            {
                ceilingHeight = _lastEnvironmentResult.GroundHeight;
                return _lastEnvironmentResult.GroundDetected;
            }
            
            return false;
        }
        
        /// <summary>
        /// 获取异步碰撞检测结果
        /// </summary>
        private bool TryGetAsyncCollisionResult(out bool hasCollision, out float safeDistance)
        {
            hasCollision = false;
            safeDistance = Distance;
            
            if (_collisionDetectionTask != null && _collisionDetectionTask.IsCompleted)
            {
                _lastCollisionResult = _collisionDetectionTask.Result;
                _collisionDetectionTask = null;
            }
            
            if (_lastCollisionResult.IsValid(_gameTime))
            {
                hasCollision = _lastCollisionResult.HasCollision;
                safeDistance = _lastCollisionResult.SafeDistance;
                return true;
            }
            
            return false;
        }
        
        #endregion
        
        #region 性能LOD策略
        
        /// <summary>
        /// 初始化性能LOD系统
        /// </summary>
        private void InitializePerformanceLOD()
        {
            _currentFPS = TargetFPS;
            _fpsUpdateTimer = 0f;
            _frameCount = 0;
            _lastLODAdjustTime = 0f;
            _originalQuality = CollisionDetectionQuality;
            
            //Debug.Log($"[性能LOD] 初始化完成 - 自动LOD:{EnableAutoLOD}, 目标FPS:{TargetFPS}, 初始质量:{_originalQuality}");
        }
        
        /// <summary>
        /// 更新FPS统计
        /// </summary>
        private void UpdateFPSMonitoring()
        {
            if (!EnableAutoLOD)
                return;
            
            _frameCount++;
            _fpsUpdateTimer += Time.DeltaTime;
            
            // 每0.5秒更新一次FPS
            if (_fpsUpdateTimer >= 0.5f)
            {
                _currentFPS = _frameCount / _fpsUpdateTimer;
                _frameCount = 0;
                _fpsUpdateTimer = 0f;
                
                // 尝试调整LOD
                TryAdjustPerformanceLOD();
            }
        }
        
        /// <summary>
        /// 尝试调整性能LOD
        /// </summary>
        private void TryAdjustPerformanceLOD()
        {
            if (!EnableAutoLOD)
                return;
            
            // 检查是否需要调整（避免频繁切换）
            if (_gameTime - _lastLODAdjustTime < LODAdjustInterval)
                return;
            
            CollisionDetectionLevel targetQuality = CollisionDetectionQuality;
            
            // FPS过低，降低质量
            if (_currentFPS < LODDowngradeThreshold)
            {
                if (CollisionDetectionQuality == CollisionDetectionLevel.High)
                {
                    targetQuality = CollisionDetectionLevel.Medium;
                    //Debug.LogWarning($"[性能LOD] FPS过低({_currentFPS:F1}), 降低质量: High → Medium");
                }
                else if (CollisionDetectionQuality == CollisionDetectionLevel.Medium)
                {
                    targetQuality = CollisionDetectionLevel.Low;
                    //Debug.LogWarning($"[性能LOD] FPS过低({_currentFPS:F1}), 降低质量: Medium → Low");
                }
            }
            // FPS良好，尝试恢复质量
            else if (_currentFPS > LODUpgradeThreshold)
            {
                // 只恢复到原始质量，不超过
                if (CollisionDetectionQuality == CollisionDetectionLevel.Low && _originalQuality >= CollisionDetectionLevel.Medium)
                {
                    targetQuality = CollisionDetectionLevel.Medium;
                    //Debug.Log($"[性能LOD] FPS良好({_currentFPS:F1}), 提升质量: Low → Medium");
                }
                else if (CollisionDetectionQuality == CollisionDetectionLevel.Medium && _originalQuality >= CollisionDetectionLevel.High)
                {
                    targetQuality = CollisionDetectionLevel.High;
                    //Debug.Log($"[性能LOD] FPS良好({_currentFPS:F1}), 提升质量: Medium → High");
                }
            }
            
            // 应用质量变化
            if (targetQuality != CollisionDetectionQuality)
            {
                CollisionDetectionQuality = targetQuality;
                InitializeCollisionRays(); // 重新初始化射线
                _lastLODAdjustTime = _gameTime;
            }
        }
        
        /// <summary>
        /// 获取当前FPS
        /// </summary>
        public float GetCurrentFPS()
        {
            return _currentFPS;
        }
        
        /// <summary>
        /// 手动设置性能LOD等级
        /// </summary>
        public void SetPerformanceLOD(CollisionDetectionLevel level)
        {
            if (CollisionDetectionQuality == level)
                return;
            
            CollisionDetectionQuality = level;
            _originalQuality = level; // 更新原始质量
            InitializeCollisionRays();
            
            //Debug.Log($"[性能LOD] 手动设置质量等级: {level}");
        }
        
        /// <summary>
        /// 重置到原始性能等级
        /// </summary>
        public void ResetPerformanceLOD()
        {
            SetPerformanceLOD(_originalQuality);
        }
        
        #endregion
    }
}
