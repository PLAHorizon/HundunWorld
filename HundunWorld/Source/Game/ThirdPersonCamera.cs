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
        
        /// <summary>上次检测的偏航角（度）</summary>
        public float LastYaw;
        
        /// <summary>上次检测的俯仰角（度）</summary>
        public float LastPitch;
        
        /// <summary>缓存是否有效</summary>
        public bool IsValid(Vector3 currentPosition, float currentDistance, float currentYaw, float currentPitch, float currentTime)
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
            
            // 角度条件：位置容差 50cm 在远距离下允许十几度的角度漂移，
            // 复用一个明显不同视角的结果会让相机穿插或跳动，因此单独校验角度
            if (Mathf.Abs(Mathf.DeltaAngle(currentYaw, LastYaw)) > 2.0f)
                return false;
            if (Mathf.Abs(currentPitch - LastPitch) > 2.0f)
                return false;
            
            return true;
        }
    }
    
    /// <summary>
    /// 相机状态枚举
    /// </summary>
    public enum CameraState
    {
        Normal,
        Combat,
        OverShoulder,
        LockOn,
        BossBattle,
        Climbing,
        Swimming,
        Flying,
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

        [Tooltip("相机聚焦点相对于目标的位置偏移（Y 与下面的聚焦高度同单位）")]
        public Vector3 FocusOffset = new Vector3(0, 170f, 0);

        [Tooltip("启用动态聚焦高度（近距离时聚焦点上移至上半身）")]
        public bool EnableDynamicFocusHeight = true;

        [Tooltip("近距离时聚焦点高度（厘米，约头部高度）")]
        public float CloseRangeFocusHeight = 170f;

        [Tooltip("远距离时聚焦点高度（厘米，约腰部高度）")]
        public float FarRangeFocusHeight = 120f;

        [Tooltip("近距离阈值（厘米）")]
        public float CloseRangeDistance = 500f;

        [Tooltip("远距离阈值（厘米）")]
        public float FarRangeDistance = 1500f;

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
        public float CollisionSmoothSpeed = 12f;

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
        public float AlignDelay = 2.0f;
        
        [Tooltip("自动对齐旋转速度（度/秒）")]
        public float AlignRotationSpeed = 90f;
        
        [Tooltip("角度范围阈值（度，在此范围内开始平滑对齐）")]
        public float AlignSmoothRange = 45f;
        
        [Tooltip("角色最小移动速度阈值（米/秒，低于此值不触发对齐）")]
        public float AlignMinSpeed = 0.1f;
        
        [Tooltip("战斗中禁用自动对齐")]
        public bool DisableAlignInCombat = true;
        
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
        
        [Header("越肩视角设置")]
        [Tooltip("越肩水平偏移量（厘米）")]
        public float ShoulderOffset = 40f;

        [Tooltip("越肩高度偏移量（厘米）")]
        public float ShoulderHeightOffset = 10f;

        [Tooltip("越肩侧边（true=右肩，false=左肩）")]
        public bool ShoulderRightSide = true;

        [Tooltip("越肩切换平滑速度")]
        public float ShoulderSwapSpeed = 8f;

        [Tooltip("越肩碰撞回退阈值（厘米，偏移量低于此值时回退到标准视角）")]
        public float ShoulderMinOffset = 10f;

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

        [Tooltip("判定进入飞行状态的\"离地高度\"阈值（世界单位，与焦点高度等常量同单位）")]
        public float FlyingMinHeightAboveGround = 300f;
        
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
            FOVOverride = 60f,
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

        [Tooltip("OverShoulder状态配置(越肩视角)")]
        public CameraStateConfig OverShoulderStateConfig = new CameraStateConfig
        {
            State = CameraState.OverShoulder,
            TargetDistance = 4f,
            TargetPitch = 5f,
            AllowRotation = true,
            AllowZoom = true,
            FOVOverride = 60f,
            TransitionSpeed = 10f
        };

        [Tooltip("LockOn状态配置(锁定目标)")]
        public CameraStateConfig LockOnStateConfig = new CameraStateConfig
        {
            State = CameraState.LockOn,
            TargetDistance = 5f,
            TargetPitch = 10f,
            AllowRotation = true,
            AllowZoom = false,
            FOVOverride = 60f,
            TransitionSpeed = 10f
        };

        [Header("战斗动态镜头")]
        [Tooltip("启用战斗动态镜头效果")]
        public bool EnableCombatCameraEffects = true;

        [Tooltip("攻击命中推进距离（厘米）")]
        public float HitPushDistance = 30f;

        [Tooltip("攻击命中持续时间（秒）")]
        public float HitPushDuration = 0.15f;

        [Tooltip("重击命中推进距离（厘米）")]
        public float HeavyHitPushDistance = 80f;

        [Tooltip("重击命中持续时间（秒）")]
        public float HeavyHitPushDuration = 0.25f;

        [Tooltip("重击FOV缩放量（度）")]
        public float HeavyHitFOVPunch = -3f;

        [Tooltip("闪避镜头偏移强度")]
        public float DodgeOffsetStrength = 0.3f;

        [Tooltip("闪避镜头恢复速度")]
        public float DodgeRecoverySpeed = 8f;

        [Tooltip("受击镜头偏移强度")]
        public float HitReactionStrength = 0.5f;

        [Tooltip("受击镜头持续时间（秒）")]
        public float HitReactionDuration = 0.3f;

        [Tooltip("处决镜头距离（厘米）")]
        public float ExecutionDistance = 250f;

        [Tooltip("处决镜头俯仰角")]
        public float ExecutionPitch = 5f;

        [Tooltip("处决镜头FOV")]
        public float ExecutionFOV = 50f;

        [Tooltip("处决镜头持续时间（秒）")]
        public float ExecutionDuration = 1.5f;

        [Tooltip("BossBattle状态配置(Boss战)")]
        public CameraStateConfig BossBattleStateConfig = new CameraStateConfig
        {
            State = CameraState.BossBattle,
            TargetDistance = 10f,
            TargetPitch = 15f,
            AllowRotation = true,
            AllowZoom = true,
            FOVOverride = 70f,
            TransitionSpeed = 8f
        };
        
        [Header("锁定目标设置")]
        [Tooltip("启用锁定目标系统")]
        public bool EnableLockOn = true;

        [Tooltip("锁定目标最大距离（厘米）")]
        public float LockOnMaxDistance = 2000f;

        [Tooltip("锁定目标检测角度范围（度）")]
        public float LockOnDetectionAngle = 60f;

        [Tooltip("锁定目标切换检测角度（度）")]
        public float LockOnSwitchAngle = 90f;

        [Tooltip("锁定相机平滑速度")]
        public float LockOnSmoothSpeed = 10f;

        [Tooltip("锁定目标丢失后恢复延迟（秒）")]
        public float LockOnLostDelay = 0.5f;

        [Tooltip("软锁定检测角度范围（度）")]
        public float SoftLockAngle = 45f;

        [Tooltip("软锁定相机偏转强度")]
        public float SoftLockInfluence = 0.3f;

        [Tooltip("Boss检测标签")]
        public string BossTag = "Boss";

        [Tooltip("锁定目标层掩码（掩码为 0 时不做层级过滤）")]
        public LayersMask LockOnTargetLayers = LayersMask.Default;
        
        [Tooltip("启用性能监控统计")]
        public bool EnablePerformanceMonitoring = false;
        
        [Tooltip("性能统计重置间隔（秒）")]
        public float StatsResetInterval = 60f;
        
        [Header("异步检测系统")]
        [Tooltip("启用异步碰撞检测（实验性：在线程池做物理查询，仅在需要极高检测频率时开启）")]
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
        private float _currentCollisionDistance;
        private Vector3[] _collisionRayDirections;
        private Actor _lockOnTarget;
        private bool _isInCombat;
        private Actor _softLockTarget;
        private float _lastSoftLockScanTime; // 上次软锁定候选扫描时间
        private const float SoftLockScanInterval = 0.15f; // 软锁定候选刷新间隔（秒）
        private float _lockOnLostTimer;
        private bool _isLockOnActive;
        private float _currentShoulderOffset;
        private float _targetShoulderOffset;
        private bool _isOverShoulderFallback;
        private float _combatPushDistance;
        private float _combatPushTimer;
        private bool _isCombatPushing;
        private bool _isHeavyHit;
        private float _combatPushFOV;
        private Vector3 _dodgeOffset;
        private bool _isDodging;
        private Vector3 _hitReactionOffset;
        private float _hitReactionTimer;
        private bool _isHitReacting;
        private bool _isExecutionCamera;
        private bool _externalCameraControl; // 外部镜头特效接管中（此时不写相机位姿）
        private float _executionTimer;
        private float _preExecutionDistance;
        private float _preExecutionPitch;
        private float _preExecutionFOV;
        
        // 碰撞缓存相关
        private CollisionCacheData _collisionCache = new CollisionCacheData();
        private float _gameTime = 0f; // 游戏运行时间
        
        // 动态距离调整相关
        private bool _wasColliding = false; // 上一帧是否碰撞
        private float _collisionStateChangeTime = 0f; // 碰撞状态改变时间
        private const float CollisionStateStableTime = 0.1f; // 碰撞状态稳定时间（秒）- 优化：降低延迟
        
        // 性能统计相关
        private CameraPerformanceStats _performanceStats = new CameraPerformanceStats();
        private float _statsResetTimer = 0f;
        private float _frameStartTime = 0f;
        
        // 相机抖动相关
        private float _currentShakeIntensity;
        private float _shakeDecaySpeed; // 当前抖动的衰减速度（由 TriggerShake 的 duration 推导，不覆写配置项）
        private float _shakeTime;
        private Vector3 _shakeOffset;
        
        // 地面托底平滑（探针 0.1s 刷新为台阶值，直接套用会让相机 10Hz 抽动）
        private float _smoothedFloorY;
        private bool _smoothedFloorValid;
        private const float GroundCheckInterval = 0.1f; // 每0.1秒检查一次地面

        // 地面/悬崖探针缓存：向下找地面是静态几何查询，按 GroundCheckInterval 刷新即可，
        // 避免每帧在“安全位置”“地面高度调整”“悬崖检测”等处各发一条射线
        private bool _groundProbeValid;
        private float _groundProbeTime;
        private bool _hasGroundUnderCamera;
        private float _groundHeightUnderCamera;
        private bool _hasGroundUnderCharacter;
        private float _groundHeightUnderCharacter;
        
        // 智能避障相关
        private bool _smartAvoidanceActive;      // 是否已确认切换到避障俯仰角
        private float _smartAvoidanceDistance;   // 上次在避障俯仰角下测得的安全距离
        private float _lastSmartAvoidanceCheckTime; // 上次避障试探时间
        private Vector3 _lastSmartAvoidanceFocus;   // 上次避障试探的聚焦点
        private const float SmartAvoidanceCheckInterval = 0.1f;  // 避障试探复用间隔（秒）
        private const float SmartAvoidanceFocusTolerance = 30f;  // 聚焦点移动超过该距离则强制重测
        // 弹性跟随的速度分档阈值（世界单位/秒；本工程为 cm/s，对应"静止 / 行走 / 跑与冲刺"）
        private const float ElasticityStillSpeed = 50f;
        private const float ElasticitySlowSpeed = 600f;
        
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
        
        // 弹性跟随相关
        private Vector3 _lastTargetPosition; // 上一帧角色位置
        private Vector3 _elasticFocus;       // 上一帧平滑后的聚焦点（不再从相机位置反推，见 OnUpdate 弹性跟随注释）
        private bool _elasticFocusValid;     // _elasticFocus 是否可用（目标切换/基准恢复/关闭弹性后需重新对齐）
        private const float ElasticFocusSnapDistance = 1000f; // 期望焦点与平滑焦点超过该距离（cm）视为传送，直接吸附
        private Actor _lastTarget;           // 上一帧的跟踪目标（用于目标首次就绪时同步基准位置）
        private Vector3 _targetVelocity;     // 角色移动速度
        private Vector3 _cameraInertia;      // 相机惯性速度
        private float _smoothYaw;            // 平滑后的Yaw角
        
        // FOV动态调整相关
        private float _currentFOV;           // 当前FOV值
        private float _targetFOV;            // 目标FOV值
        
        // 状态机相关
        private Dictionary<CameraState, CameraStateConfig> _stateConfigs; // 状态配置字典
        private bool _isTransitioning;       // 是否正在状态切换中
        private float _stateTargetDistance;  // 状态目标距离
        private float _stateTargetPitch;     // 状态目标俯仰角
        private float _stateTargetFOV;       // 状态目标FOV
        
        // 角色控制器引用（用于状态检测）
        private PlayerController _playerController;
        
        // 环境感知相关
        private Dictionary<EnvironmentType, EnvironmentConfig> _environmentConfigs; // 环境配置字典
        private float _environmentDetectionTimer; // 环境检测计时器
        private float _environmentMaxDistance; // 环境限制的最大距离
        private float _environmentMinDistance; // 环境限制的最小距离
        private float _environmentElasticCoefficient = 1.0f; // 环境弹性系数
        private float _weatherVisibilityFactor; // 天气影响的可见度系数
        
        // 异步碰撞检测相关
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


            // 自动计算初始参数（基于场景中相机的当前位置）
            if (AutoCalculateInitialParameters && Target != null)
            {
                CalculateInitialParameters();
            }

            // 初始化碰撞检测
            _currentCollisionDistance = Distance;
            _gameTime = 0f; // 初始化游戏时间
            _wasColliding = false;
            InitializeCollisionRays();
            
            //Debug.Log($"[ThirdPersonCamera] 碰撞检测初始化 - EnableCameraCollision={EnableCameraCollision}, Quality={CollisionDetectionQuality}, EnableSphereCheck={EnableSphereCollisionCheck}, CollisionLayerMask={CollisionLayerMask}, EnableSmartAvoidance={EnableSmartAvoidance}");
            
            // 初始化抖动系统
            _currentShakeIntensity = 0f;
            _shakeTime = 0f;
            _shakeOffset = Vector3.Zero;
            _shakeDecaySpeed = ShakeDecaySpeed;
            
            _isCombatPushing = false;
            _isDodging = false;
            _isHitReacting = false;
            _isExecutionCamera = false;
            _combatPushDistance = 0f;
            _combatPushFOV = 0f;
            _dodgeOffset = Vector3.Zero;
            _hitReactionOffset = Vector3.Zero;
            
            // 初始化自动对齐
            _isAligning = false;
            _currentShoulderOffset = 0f;
            _targetShoulderOffset = 0f;
            _isOverShoulderFallback = false;
            _alignTimer = 0f;
            _wasManualRotating = false;
            _targetAlignYaw = Yaw;
            
            // 注：基准视角不需要保存，将在ResetToBaseline时智能计算
            
            _isLockOnActive = false;
            _lockOnLostTimer = 0f;
            _softLockTarget = null;
            _lastSoftLockScanTime = -SoftLockScanInterval; // 允许首次进入战斗时立即扫描
            
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
            
            //Debug.Log($"[ThirdPersonCamera] 初始化完成 - Distance: {Distance:F2}, Pitch: {Pitch:F2}, Yaw: {Yaw:F2}, State: {CurrentState}, Environment: {CurrentEnvironment}");
        }

        public override void OnUpdate()
        {
            if (Target == null || _camera == null) return;

            // 目标首次就绪（或切换）时同步基准位置：Target 是在场景加载后才由生成流程异步赋值的，
            // OnStart 时多为 null，_lastTargetPosition 会留在零向量上，导致首帧算出一个巨大的移动速度、FOV 突跳。
            if (Target != _lastTarget)
            {
                _lastTarget = Target;
                _lastTargetPosition = Target.Position;
                _elasticFocusValid = false; // 换目标（重生/切换角色）后焦点重新锚定
            }
            
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

            if (EnableCombatCameraEffects)
            {
                UpdateCombatCameraEffects();
            }

            // 4. 处理输入(根据当前状态决定是否允许)
            HandleInput();
            
            if (EnableLockOn)
            {
                UpdateLockOn();
            }
            
            if (EnableAutoAlign && !_isResetting)
            {
                UpdateAutoAlign();
            }
            
            // 4.5 处理基准视角恢复
            if (_isResetting)
            {
                UpdateBaselineReset();
                return; // 恢复期间跳过后续逻辑
            }
            
            // 2. 计算角色移动速度(无论是否启用弹性跟随都需要,用于FOV调整)
            Vector3 targetPosition = Target.Position;
            _targetVelocity = (targetPosition - _lastTargetPosition) / Mathf.Max(Time.DeltaTime, 0.001f);
            _lastTargetPosition = targetPosition;
            
            // 3. 计算聚焦点
            Vector3 focusPoint;
            
            if (EnableElasticFollow && !_isResetting)
            {
                // ✅ 启用弹性跟随
                // 聚焦点必须逐帧持久化，绝不能从 Actor.Position 反推：
                // 实际写入的相机位置在定位后被多处改写（战斗偏移 GetCombatCameraOffset、
                // 地面/天花板/悬崖高度钳制、智能避障的 effectivePitch≠Pitch、抖动偏移、肩部偏移时序差），
                // 反推会把这些修正全部错误吸收进聚焦点，而下一帧的钳制/避障又以该聚焦点发射线，
                // 形成逐帧闭环反馈——表现为遮挡物边缘与坡地附近的持续晃动。
                Vector3 desiredFocus = targetPosition + GetDynamicFocusOffset();
                if (!_elasticFocusValid || Vector3.Distance(_elasticFocus, desiredFocus) > ElasticFocusSnapDistance)
                {
                    // 首帧/目标传送/接管恢复：直接吸附，避免远距离回拉过程
                    _elasticFocus = desiredFocus;
                    _elasticFocusValid = true;
                }
                else
                {
                    // 动态计算弹性系数（基于速度）
                    // 注意：_targetVelocity 是世界单位/秒（本工程 cm/s），而策划口径的移动速度以 m/s 计
                    // （PlayerController：走 5 / 跑 10 / 冲刺 12.5 m/s ⇒ 500 / 1000 / 1250 cm/s）。
                    // 原阈值 1.0 / 10.0 在该单位下等于"几乎静止 / 任何移动"，中档 0.85 永远不可达；
                    // 现按实测速度量级重设阈值，保留作者的三档取值（0.95 / 0.85 / 0.75）。
                    float currentElasticity = PositionElasticity;
                    float velocityLength = _targetVelocity.Length;
                    if (velocityLength < ElasticityStillSpeed)
                    {
                        currentElasticity = 0.95f; // 静止时几乎完全跟随
                    }
                    else if (velocityLength < ElasticitySlowSpeed)
                    {
                        currentElasticity = 0.85f; // 慢速移动（约行走及以下）
                    }
                    else
                    {
                        currentElasticity = 0.75f; // 快速移动（跑/冲刺）
                    }

                    // 应用环境弹性系数调整
                    currentElasticity = Mathf.Clamp(currentElasticity * _environmentElasticCoefficient, 0.1f, 1.0f);

                    // 状态级弹性覆盖（面板 CameraStateConfig.ElasticityOverride；<=0 表示不覆盖，默认为 0 故当前行为不变）
                    float stateElasticity = GetCurrentStateConfig().ElasticityOverride;
                    if (stateElasticity > 0f)
                    {
                        currentElasticity = Mathf.Clamp(stateElasticity, 0.1f, 1f);
                    }

                    _elasticFocus = Vector3.Lerp(
                        _elasticFocus,
                        desiredFocus,
                        currentElasticity
                    );
                }

                focusPoint = _elasticFocus;
                
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
                focusPoint = targetPosition + GetDynamicFocusOffset();
                _elasticFocusValid = false; // 下次回到弹性跟随时重新锚定
                _smoothYaw = Yaw;
                _cameraInertia = Vector3.Zero;
            }

            if (CurrentState == CameraState.OverShoulder || CurrentState == CameraState.LockOn)
            {
                _targetShoulderOffset = ShoulderRightSide ? ShoulderOffset : -ShoulderOffset;
            }
            else
            {
                _targetShoulderOffset = 0f;
            }
            _currentShoulderOffset = Mathf.Lerp(_currentShoulderOffset, _targetShoulderOffset, Time.DeltaTime * ShoulderSwapSpeed);

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
                }
                
                if (CurrentState == CameraState.OverShoulder && hasCollision && collisionDistance <= MinDistance)
                {
                    _isOverShoulderFallback = true;
                }
                else if (!hasCollision || collisionDistance > MinDistance * 1.5f)
                {
                    _isOverShoulderFallback = false;
                }
                if (_isOverShoulderFallback)
                {
                    _currentShoulderOffset = Mathf.Lerp(_currentShoulderOffset, 0f, Time.DeltaTime * ShoulderSwapSpeed);
                }

                // 优先级控制：设置碰撞优先级（已移除优化代码，此处保留空逻辑）

                // 一旦不再受阻，清空避障缓存，下次受阻时立即重新试探
                if (!hasCollision)
                {
                    _smartAvoidanceActive = false;
                    _smartAvoidanceDistance = 0f;
                }
                
                if (hasCollision)
                {
                    //Debug.Log($"[Collision] 检测到碰撞@{Pitch:F1}°, 安全距离:{collisionDistance:F2}cm, 理想距离:{Distance:F2}cm");
                    
                    // ✅ 优先策略：调整距离（避免角度闪烁）
                    // 只有当碰撞距离小于最小距离时，才考虑调整角度
                    if (collisionDistance <= MinDistance && EnableSmartAvoidance)
                    {
                        // 避障试探要再跑一遍完整的多射线检测，按间隔复用结果：
                        // 目标点移动超过阈值、间隔到期、或上次测得的避障距离本身很小（贴墙）时才重测，
                        // 最后一种情况保持逐帧重测以保证安全。
                        bool probeStale = _gameTime - _lastSmartAvoidanceCheckTime >= SmartAvoidanceCheckInterval;
                        bool focusMoved = Vector3.Distance(focusPoint, _lastSmartAvoidanceFocus) > SmartAvoidanceFocusTolerance;
                        bool tooCloseToPersist = _smartAvoidanceDistance <= MinDistance * 1.5f;

                        if (probeStale || focusMoved || tooCloseToPersist)
                        {
                            _lastSmartAvoidanceCheckTime = _gameTime;
                            _lastSmartAvoidanceFocus = focusPoint;

                            // 检查智能避障角度是否能提供更大距离
                            CheckCollision(focusPoint, SmartAvoidancePitch, out float collisionDistanceAtAvoidance, allowCache: false);
                            _smartAvoidanceDistance = collisionDistanceAtAvoidance;
                            _smartAvoidanceActive = collisionDistanceAtAvoidance > collisionDistance * 1.2f; // 至少增加20%
                        }

                        if (_smartAvoidanceActive)
                        {
                            effectivePitch = SmartAvoidancePitch;
                            collisionDistance = Mathf.Max(_smartAvoidanceDistance, collisionDistance);
                            //Debug.Log($"[SmartAvoidance] ✅ 切换到{SmartAvoidancePitch}°视角, 距离:{collisionDistance:F2}cm");
                        }
                    }
                    else
                    {
                        // 距离已足够，无需避障
                        _smartAvoidanceActive = false;
                    }
                }
                
                // === 动态距离调整策略：快速缩短、缓慢恢复 ===
                float smoothSpeed;
                float effectiveCollisionSmoothSpeed = (CurrentState == CameraState.OverShoulder || CurrentState == CameraState.LockOn || CurrentState == CameraState.BossBattle) ? 15f : CollisionSmoothSpeed;
                
                {
                    float distanceChange = Mathf.Abs(collisionDistance - _currentCollisionDistance);
                
                    bool stableCollisionState = hasCollision;
                    
                    if (hasCollision != _wasColliding)
                    {
                        if (_collisionStateChangeTime == 0f)
                        {
                            _collisionStateChangeTime = _gameTime;
                        }
                        
                        float timeSinceChange = _gameTime - _collisionStateChangeTime;
                        if (timeSinceChange < CollisionStateStableTime)
                        {
                            stableCollisionState = _wasColliding;
                        }
                        else
                        {
                            _collisionStateChangeTime = 0f;
                        }
                    }
                    else
                    {
                        _collisionStateChangeTime = 0f;
                    }
                    
                    if (stableCollisionState)
                    {
                        if (!_wasColliding)
                        {
                        }
                        
                        if (distanceChange > 50.0f)
                        {
                            smoothSpeed = effectiveCollisionSmoothSpeed * 5.0f;
                        }
                        else if (distanceChange > 20.0f)
                        {
                            smoothSpeed = effectiveCollisionSmoothSpeed * 2.0f;
                        }
                        else
                        {
                            smoothSpeed = effectiveCollisionSmoothSpeed * 1.5f;
                        }
                        
                    }
                    else
                    {
                        if (_wasColliding)
                        {
                        }
                        
                        smoothSpeed = effectiveCollisionSmoothSpeed * 0.5f;
                    }
                    
                    _wasColliding = stableCollisionState;
                    
                    // t 必须钳位：smoothSpeed 高档可达 60（12*5），低帧率下 dt*60 > 1，
                    // Mathf.Lerp 不钳位会外推过冲，表现为贴墙时距离来回泵动
                    _currentCollisionDistance = Mathf.Lerp(
                        _currentCollisionDistance,
                        collisionDistance,
                        Mathf.Clamp(Time.DeltaTime * smoothSpeed, 0f, 1f)
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
            }

            // 6. 计算相机位置(使用有效俯仰角和平滑Yaw角，避免重影)
            Vector3 cameraPosition = CalculateCameraPosition(focusPoint, effectivePitch, _smoothYaw, effectiveDistance, _currentShoulderOffset);
            
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

            // 8. 设置相机位置和朝向（外部镜头特效接管期间不写入，避免两者逐帧互相覆盖）
            if (!_externalCameraControl)
            {
                Actor.Position = cameraPosition + GetCombatCameraOffset();

                // 计算朝向向量
                Vector3 direction = focusPoint - cameraPosition;
                direction.Normalize();
                Actor.Orientation = Quaternion.LookRotation(direction, Vector3.Up);
            }
            
            // 9. 更新FOV
            if (EnableDynamicFOV)
            {
                UpdateDynamicFOV();
            }
            
            // 10.（已并入第 6.5 步 GetSafeCameraPosition 的托底钳制）
            // 原"地面穿透防护"在 Actor.Position/Orientation 写完后又对 Y 做二次拉高：
            // 下一帧第 8 步又从焦点整体重算位置，二次修正被抹除又重施，且其预测项
            // 在俯视角贴地时逐帧抖动 ±25cm——正是贴地/坡面晃动的主通道。托底改由
            // GetSafeCameraPosition 一次性完成（含相机自身地面探针 + 时间平滑）。
            
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
        /// 刷新地面探针（相机下方 / 角色脚下），按 GroundCheckInterval 节流。
        /// 两条探针都是"向下找地面"的静态几何查询，10Hz 足够；
        /// 相机 X/Z 与最终位置一致（高度钳制只改 Y），因此可用候选位置探测。
        /// </summary>
        private void RefreshGroundProbes(Vector3 cameraPosition)
        {
            if (_groundProbeValid && _gameTime - _groundProbeTime < GroundCheckInterval)
                return;

            _groundProbeValid = true;
            _groundProbeTime = _gameTime;

            _hasGroundUnderCamera = Physics.RayCast(cameraPosition, Vector3.Down, out RayCastHit cameraHit, GroundCheckDistance, GroundLayers);
            _groundHeightUnderCamera = _hasGroundUnderCamera ? cameraHit.Point.Y : 0f;

            if (Target != null)
            {
                _hasGroundUnderCharacter = Physics.RayCast(Target.Position, Vector3.Down, out RayCastHit characterHit, CliffCheckDistance, GroundLayers);
                _groundHeightUnderCharacter = _hasGroundUnderCharacter ? characterHit.Point.Y : 0f;
            }
            else
            {
                _hasGroundUnderCharacter = false;
            }

            // 性能监控：地面检测计数
            if (EnablePerformanceMonitoring)
            {
                _performanceStats.GroundCheckCount++;
            }
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

            // 地面/悬崖探针（按间隔节流，供下方各分支复用）
            RefreshGroundProbes(position);
            
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
            
            // === 3. 地面托底：角色侧与相机侧取更高者，再做时间平滑 ===
            // 原结构是"if 悬崖检测则只看角色脚下 else 才看相机下方"，本项目 EnableCliffDetection
            // 默认开启 ⇒ 相机自身地面钳制分支常年不生效，俯视角贴地时相机扎进坡体，
            // 只能靠（已删除的）第 10 步每帧事后拉高，与第 8 步的整帧重算互相抹除 → 持续晃动。
            float rawFloorY = float.MinValue;
            if (EnableCliffDetection && Target != null)
            {
                if (!_hasGroundUnderCharacter)
                {
                    // 角色下方没有地面，可能是悬崖边缘：以角色当前高度作为最低高度
                    rawFloorY = Target.Position.Y + MinGroundHeight;
                }
                else
                {
                    rawFloorY = _groundHeightUnderCharacter + MinGroundHeight;
                }
            }
            if (_hasGroundUnderCamera)
            {
                rawFloorY = Mathf.Max(rawFloorY, _groundHeightUnderCamera + MinGroundHeight);
            }

            if (rawFloorY > float.MinValue)
            {
                if (!_smoothedFloorValid || Mathf.Abs(rawFloorY - _smoothedFloorY) > 1000f)
                {
                    // 首帧/传送/落差巨大的跳变：直接吸附，不做长途回拉
                    _smoothedFloorY = rawFloorY;
                    _smoothedFloorValid = true;
                }
                else
                {
                    // 探针按 0.1s 节流，原始托底是台阶值；直接套用会以 10Hz 顿挫相机，
                    // 插值把台阶变成连续坡（速率需快于探针节奏，否则上坡时穿模变深）
                    _smoothedFloorY = Mathf.Lerp(_smoothedFloorY, rawFloorY, Mathf.Clamp(Time.DeltaTime * 15f, 0f, 1f));
                }
                if (adjustedPosition.Y < _smoothedFloorY)
                {
                    adjustedPosition.Y = _smoothedFloorY;
                }
            }
            else
            {
                // 角色与相机下方都探不到地面（跳崖/悬空）：撤掉托底并丢弃历史
                _smoothedFloorValid = false;
            }
            
            return adjustedPosition;
        }

        /// <summary>
        /// 处理输入
        /// </summary>
        private void HandleInput()
        {
            if (_isResetting)
            {
                return;
            }
            
            // 检查一键恢复基准视角（外部镜头特效接管时不响应，避免恢复过程与特写抢位姿）
            if (EnableBaselineReset && !_externalCameraControl && Input.GetKeyDown(ResetKey))
            {
                ResetToBaseline();
                return;
            }
            
            if (Input.GetKeyDown(KeyboardKeys.V))
            {
                ShoulderRightSide = !ShoulderRightSide;
            }
            
            if (Input.GetKeyDown(KeyboardKeys.Tab))
            {
                if (_isLockOnActive)
                {
                    DeactivateLockOn();
                }
                else
                {
                    ActivateLockOn();
                }
            }
            if (_isLockOnActive)
            {
                if (Input.GetKeyDown(KeyboardKeys.Q))
                {
                    SwitchLockOnTarget(-1f);
                }
                if (Input.GetKeyDown(KeyboardKeys.E))
                {
                    SwitchLockOnTarget(1f);
                }
            }
            
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
            Vector3 focusPoint = Target.Position + GetDynamicFocusOffset();
            
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
            // 注意：偏移的含义是"视线右方(X) / 视线上方(Y)"，使用处会按当前视线方向展开（见 BuildRayBasis）
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
        /// 构造垂直于射线的横截面基（右/上），供多射线探针在视平面上展开偏移。
        /// 若直接用世界轴偏移，当视线接近某条世界轴时偏移会退化成沿射线的冗余采样，
        /// 导致该朝向下细小障碍漏检、相机穿插。
        /// </summary>
        private static void BuildRayBasis(Vector3 direction, out Vector3 right, out Vector3 up)
        {
            right = Vector3.Cross(Vector3.Up, direction);
            if (right.LengthSquared < 1e-6f)
            {
                // 视线与世界上方平行时的回退
                right = Vector3.Cross(Vector3.Forward, direction);
            }
            right = Vector3.Normalize(right);
            up = Vector3.Normalize(Vector3.Cross(direction, right));
        }

        /// <summary>
        /// 检查指定角度下的碰撞情况
        /// </summary>
        /// <param name="focusPoint">聚焦点</param>
        /// <param name="pitch">检测使用的俯仰角</param>
        /// <param name="safeDistance">输出的安全距离</param>
        /// <param name="allowCache">
        /// 是否允许读写碰撞缓存。缓存不区分俯仰角，智能避障的试探射线必须传 false，
        /// 否则会用一个角度的结果污染另一个角度的检测。
        /// </param>
        /// <returns>是否有碰撞</returns>
        private bool CheckCollision(Vector3 focusPoint, float pitch, out float safeDistance, bool allowCache = true)
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
            Vector3 shoulderAdjustedFocusPoint = focusPoint;
            if (CurrentState == CameraState.OverShoulder || CurrentState == CameraState.LockOn)
            {
                float yawRad = Yaw * Mathf.DegreesToRadians;
                Vector3 right = new Vector3(Mathf.Cos(yawRad), 0, Mathf.Sin(yawRad));
                shoulderAdjustedFocusPoint += right * _currentShoulderOffset;
                shoulderAdjustedFocusPoint.Y += ShoulderHeightOffset;
            }
            Vector3 direction = idealCameraPos - shoulderAdjustedFocusPoint;
            float targetDistance = direction.Length;
            
            // 异常情况检查
            if (targetDistance < 0.001f)
            {
                //Debug.LogWarning("[ThirdPersonCamera] 相机方向计算异常，跳过碰撞检测");
                return false;
            }
            
            direction.Normalize();

            // === 步骤1：检查缓存是否可用 ===
            if (allowCache && _collisionCache.IsValid(idealCameraPos, Distance, Yaw, pitch, _gameTime))
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
            float minDistance = Distance;

            // 探针横截面基：让偏移构成视平面上的十字/米字，而不是固定世界轴
            BuildRayBasis(direction, out Vector3 rayRight, out Vector3 rayUp);
            
            foreach (var offset in _collisionRayDirections)
            {
                // 起始点：聚焦点 + 视平面偏移（offset.X 沿视线右方，offset.Y 沿视线上方）
                Vector3 startPoint = focusPoint + rayRight * offset.X + rayUp * offset.Y;

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

                    // 忽略"视线下方的上表面"（地形/台阶面）：俯视角时视线从焦点斜向后上方扫出，
                    // 擦到身后坡面会产生"假遮挡"，逐帧推动碰撞距离形成沿视线的抽插晃动。
                    // 相机真正入坡由 GetSafeCameraPosition 的地面托底负责，不靠这条视线射线。
                    if (hit.Normal.Y > 0.7f && hit.Point.Y < focusPoint.Y)
                    {
                        continue;
                    }
                    
                    // 关键修复：计算碰撞点到聚焦点的距离（沿着相机方向）
                    Vector3 hitToFocus = hit.Point - focusPoint;
                    float hitDistance = hitToFocus.Length;
                    
                    // 内部碰撞阈值必须是常量：原写法 `Max(_currentCollisionDistance*0.3, 50)`
                    // 引用了被本函数驱动的状态量——贴墙时"命中被忽略→距离恢复→命中又被计入"
                    // 形成自激振荡；角色胶囊半径有限，固定 50cm 足以滤掉身上挂件。
                    float innerCollisionThreshold = 50.0f;
                    
                    if (hitDistance < innerCollisionThreshold)
                    {
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
            if (allowCache)
            {
                _collisionCache.LastPosition = idealCameraPos;
                _collisionCache.LastDistance = Distance;
                _collisionCache.LastYaw = Yaw;
                _collisionCache.LastPitch = pitch;
                _collisionCache.CacheTime = _gameTime;
                _collisionCache.HasObstruction = hasCollision;
                _collisionCache.SafeDistance = safeDistance;
            }
            
            // 调试日志(需要时手动开启,避免每帧构造字符串)
            //Debug.Log($"[Collision@{pitch:F0}°] 碰撞:{hasCollision}, 安全距离:{minDistance:F2}, 理想距离:{Distance:F2}");
            
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
        private Vector3 CalculateCameraPosition(Vector3 focusPoint, float pitch, float yaw, float distance, float shoulderOffset = 0f)
        {
            return focusPoint + CalculateCameraOffsetWithShoulder(pitch, yaw, distance, shoulderOffset);
        }
        
        /// <summary>
        /// 计算相机相对于聚焦点的偏移
        /// </summary>
        private Vector3 GetDynamicFocusOffset()
        {
            if (!EnableDynamicFocusHeight) return FocusOffset;
            float currentDistance = _currentCollisionDistance > 0 ? _currentCollisionDistance : Distance;
            float t = Mathf.Clamp((currentDistance - CloseRangeDistance) / (FarRangeDistance - CloseRangeDistance), 0f, 1f);
            float dynamicHeight = Mathf.Lerp(CloseRangeFocusHeight, FarRangeFocusHeight, t);
            return new Vector3(FocusOffset.X, dynamicHeight, FocusOffset.Z);
        }

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

        private Vector3 CalculateCameraOffsetWithShoulder(float pitch, float yaw, float distance, float shoulderOffset)
        {
            Vector3 baseOffset = CalculateCameraOffset(pitch, yaw, distance);
            if (Mathf.Abs(shoulderOffset) < 0.01f) return baseOffset;
            float yawRad = yaw * Mathf.DegreesToRadians;
            Vector3 right = new Vector3(Mathf.Cos(yawRad), 0, Mathf.Sin(yawRad));
            baseOffset += right * shoulderOffset;
            return baseOffset;
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
                        stateFOVOffset = CombatFOVOffset;
                        break;
                    case CameraState.OverShoulder:
                        stateFOVOffset = CombatFOVOffset;
                        break;
                    case CameraState.LockOn:
                        if (_lockOnTarget != null)
                        {
                            float lockDistance = Vector3.Distance(Target.Position, _lockOnTarget.Position);
                            float distanceRatio = Mathf.Clamp(lockDistance / LockOnMaxDistance, 0f, 1f);
                            stateFOVOffset = Mathf.Lerp(-5f, 10f, distanceRatio);
                        }
                        else
                        {
                            stateFOVOffset = -2.5f;
                        }
                        break;
                    case CameraState.BossBattle:
                        stateFOVOffset = FlyingFOVOffset;
                        break;
                    case CameraState.Flying:
                        stateFOVOffset = FlyingFOVOffset;
                        break;
                    default:
                        // 冲刺判定优先用控制器；只有拿不到控制器时才退回按速度判断。
                        // 原实现在控制器明确回答"未冲刺"后仍会再按速度判一次，而 MaxSpeed(=50) 与
                        // _targetVelocity 的世界单位（本工程为 cm/s，普通行走就有数百）不同量级，
                        // 于是"走路"也被当成冲刺、FOV 无差别 +10。Shift 同时绑定 Run/Sprint，
                        // 因此真冲刺仍会走控制器分支，不受影响。
                        bool isSprinting = _playerController != null
                            ? _playerController.IsSprinting()
                            : _targetVelocity.Length > MaxSpeed * 0.8f;

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
            _targetFOV = baseFOV + totalFOVOffset + GetCombatFOVOffset();
            
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
            _currentShakeIntensity = Mathf.Max(0f, _currentShakeIntensity - _shakeDecaySpeed * Time.DeltaTime);
            if (_currentShakeIntensity <= 0f)
            {
                _shakeDecaySpeed = ShakeDecaySpeed; // 抖动结束后恢复为配置的衰减速度
            }
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
                _shakeDecaySpeed = intensity / duration;
            }
        }

        /// <summary>
        /// 锁定目标候选校验。
        /// 必须排除本地玩家自身及其子物体（模型/挂点/名牌）与相机自身及其子物体，
        /// 否则 Level.GetActors 会把场景里的灯光、天空、导航标记等一并当成可锁定目标；
        /// 同时按 LockOnTargetLayers 掩码过滤（掩码为 0 时不启用层级过滤）。
        /// </summary>
        private bool IsValidLockOnCandidate(Actor actor)
        {
            if (actor == null || actor == Target || actor == Actor)
                return false;

            // 排除目标与相机自身的子物体（玩家模型/挂点/名牌、相机挂点）
            if (IsChildOf(actor, Target) || IsChildOf(actor, Actor))
                return false;

            // 排除目标与相机自身的祖先（挂载容器、脚本宿主、场景根等无实体的容器 Actor）
            if (IsChildOf(Target, actor) || IsChildOf(Actor, actor))
                return false;

            if ((uint)LockOnTargetLayers != 0 && !LockOnTargetLayers.HasLayer(actor.Layer))
                return false;

            return true;
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

        #region 战斗动态镜头

        public void TriggerHitCamera(bool isHeavy = false)
        {
            if (!EnableCombatCameraEffects) return;
            _isCombatPushing = true;
            _isHeavyHit = isHeavy;
            _combatPushDistance = isHeavy ? HeavyHitPushDistance : HitPushDistance;
            _combatPushTimer = isHeavy ? HeavyHitPushDuration : HitPushDuration;
            if (isHeavy)
            {
                _combatPushFOV = HeavyHitFOVPunch;
            }
        }

        public void TriggerDodgeCamera(Vector3 dodgeDirection)
        {
            if (!EnableCombatCameraEffects) return;
            _isDodging = true;
            _dodgeOffset = dodgeDirection * DodgeOffsetStrength;
        }

        public void TriggerHitReactionCamera(Vector3 hitDirection, float damage = 1f)
        {
            if (!EnableCombatCameraEffects) return;
            _isHitReacting = true;
            _hitReactionOffset = hitDirection * HitReactionStrength * Mathf.Clamp(damage, 0.5f, 3f);
            _hitReactionTimer = HitReactionDuration;
        }

        public void TriggerExecutionCamera()
        {
            if (!EnableCombatCameraEffects) return;
            _isExecutionCamera = true;
            _executionTimer = ExecutionDuration;
            _preExecutionDistance = Distance;
            _preExecutionPitch = Pitch;
            _preExecutionFOV = _currentFOV;
            Distance = ExecutionDistance;
            Pitch = ExecutionPitch;
            _targetFOV = ExecutionFOV;
        }

        private void UpdateCombatCameraEffects()
        {
            if (_isCombatPushing)
            {
                _combatPushTimer -= Time.DeltaTime;
                if (_combatPushTimer <= 0f)
                {
                    _isCombatPushing = false;
                    _combatPushDistance = 0f;
                    _combatPushFOV = 0f;
                }
            }
            if (_isDodging)
            {
                _dodgeOffset = Vector3.Lerp(_dodgeOffset, Vector3.Zero, Time.DeltaTime * DodgeRecoverySpeed);
                if (_dodgeOffset.Length < 0.01f)
                {
                    _isDodging = false;
                    _dodgeOffset = Vector3.Zero;
                }
            }
            if (_isHitReacting)
            {
                _hitReactionTimer -= Time.DeltaTime;
                _hitReactionOffset = Vector3.Lerp(_hitReactionOffset, Vector3.Zero, Time.DeltaTime * (1f / HitReactionDuration));
                if (_hitReactionTimer <= 0f)
                {
                    _isHitReacting = false;
                    _hitReactionOffset = Vector3.Zero;
                }
            }
            if (_isExecutionCamera)
            {
                _executionTimer -= Time.DeltaTime;
                if (_executionTimer <= 0f)
                {
                    _isExecutionCamera = false;
                    Distance = _preExecutionDistance;
                    Pitch = _preExecutionPitch;
                    _targetFOV = _preExecutionFOV;
                }
            }
        }

        public Vector3 GetCombatCameraOffset()
        {
            Vector3 offset = Vector3.Zero;
            if (_isCombatPushing)
            {
                float progress = 1f - (_combatPushTimer / (_isHeavyHit ? HeavyHitPushDuration : HitPushDuration));
                float curve = Mathf.Sin(progress * Mathf.Pi);
                offset += Vector3.Forward * _combatPushDistance * curve;
            }
            offset += _dodgeOffset;
            offset += _hitReactionOffset;
            return offset;
        }

        public float GetCombatFOVOffset()
        {
            if (_isCombatPushing && _isHeavyHit)
            {
                float progress = 1f - (_combatPushTimer / HeavyHitPushDuration);
                float curve = Mathf.Sin(progress * Mathf.Pi);
                return _combatPushFOV * curve;
            }
            return 0f;
        }

        #endregion

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
            // 与 UpdateAutoAlign 同一套推导：本文件 offset 水平分量 = (h·sinYaw, −h·cosYaw)，
            // 角色前向 = (sinψ, 0, cosψ)（Flax: Euler(0,ψ,0) 作用于 Forward=(0,0,1)），
            // "相机在角色正后方" ⇔ 偏移 = −(角色前向) ⇒ Yaw = −ψ。
            // 原来的 ψ+180 会把相机放到角色正面（ψ=0 时相机落到 +Z，而角色也朝 +Z）。
            float characterYaw = Target.Orientation.EulerAngles.Y;
            _baselineYaw = (360f - characterYaw) % 360f;
            
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
            Vector3 testFocusPoint = Target.Position + GetDynamicFocusOffset();
            if (CheckCollision(testFocusPoint, _baselinePitch, out float safeDistance, allowCache: false))
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
            
            // Yaw 已被其他系统接管的场合必须退出自动对齐：
            // UpdateLockOn 先于本方法执行，若不排除，自动对齐会在同一帧把 Yaw 拉向角色背后，
            // 锁定结果被覆盖（表现为锁定目标"脱靶"）。
            // 战斗姿态是否排除仍遵循面板开关，锁定状态则一律排除。
            bool isRotationOwnedByOtherSystem = _isLockOnActive
                || _isExecutionCamera
                || CurrentState == CameraState.LockOn
                || CurrentState == CameraState.BossBattle
                || CurrentState == CameraState.Cutscene
                || (DisableAlignInCombat && (CurrentState == CameraState.Combat || CurrentState == CameraState.OverShoulder));
            
            // 检查角色移动速度
            float characterSpeed = _targetVelocity.Length;
            bool isCharacterMoving = characterSpeed > AlignMinSpeed;
            
            // 如果正在手动旋转、Yaw 被其他系统接管或角色静止，重置计时器
            if (isManualRotating || isRotationOwnedByOtherSystem || !isCharacterMoving)
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
                    // "相机在角色正后方" ⇔ 相机相对焦点的水平偏移 = −(角色前向)。
                    // 由本文件 CalculateCameraOffset：offset 水平分量 = (h·sinYaw, −h·cosYaw)；
                    // 由 Flax 定义：角色前向 = (sinψ, 0, cosψ)（Euler(0,ψ,0) 作用于 Forward=(0,0,1)）。
                    // 代入解得 Yaw = −ψ。原先的 ψ+180 会把相机放到角色**正面**
                    // （ψ=0 时相机落到 +Z，而角色正向也是 +Z），与"正后方"的意图相反。
                    float characterYaw = Target.Orientation.EulerAngles.Y;
                    _targetAlignYaw = (360f - characterYaw) % 360f; // 角色正后方
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

            // 外部镜头特效接管期间不要写入位姿，否则会与特写互相覆盖；
            // 保持 _isResetting 为 true，等接管结束后继续恢复
            if (_externalCameraControl) return;
            
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
            Vector3 focusPoint = Target.Position + GetDynamicFocusOffset();
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
                _elasticFocusValid = false; // 恢复期间相机位姿由基准恢复直接写入，回到弹性跟随时需重新锚定
                
                //Debug.Log($"[BaselineReset] ✅ 基准视角恢复完成");
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
                { CameraState.OverShoulder, OverShoulderStateConfig },
                { CameraState.LockOn, LockOnStateConfig },
                { CameraState.BossBattle, BossBattleStateConfig },
                { CameraState.Climbing, ClimbingStateConfig },
                { CameraState.Swimming, SwimmingStateConfig },
                { CameraState.Flying, FlyingStateConfig },
                { CameraState.Cutscene, CutsceneStateConfig }
            };
            
            // 初始化状态
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
            if (_lockOnTarget != null && _lockOnTarget.HasTag(BossTag))
            {
                return CameraState.BossBattle;
            }

            if (_lockOnTarget != null)
            {
                return CameraState.LockOn;
            }

            if (_isInCombat)
            {
                return CameraState.OverShoulder;
            }

            var climbingController = Target.GetScript<ClimbingSystem.ClimbingController>();
            if (climbingController != null && climbingController.IsClimbing())
            {
                return CameraState.Climbing;
            }
            
            // 飞行判定：不能再用"绝对速度 + 绝对高度"。本工程移动速度以 m/s 计（PlayerController.ComputeCurrentMaxSpeed），
            // 而世界坐标单位是 cm（本文件焦点高度 170=1.7m 等常量都按 cm 取值，日志中玩家 Y≈1000），
            // 于是常态奔跑的 _targetVelocity 就有数百单位/秒（>30 恒成立）、Y≈1000（>10 恒成立）：
            // 地面移动一律被判成 Flying，Flying 配置（Pitch 20 / Distance 20 / FOV 70）反复覆盖 Normal 的（30 / 15 / 60），
            // 并在每次起步/停止时把玩家手动设置的 Pitch 拖走。
            // 改为"明显离地且未着地"：地面高度取自相机自身的探针，与本文件其它常量同单位。
            if (_hasGroundUnderCharacter
                && (Target.Position.Y - _groundHeightUnderCharacter) > FlyingMinHeightAboveGround
                && _playerController != null && !_playerController.IsGrounded)
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
            
            CurrentState = newState;
            
            // 获取新状态配置
            CameraStateConfig newConfig = GetCurrentStateConfig();
            
            // 设置目标值
            _stateTargetDistance = newConfig.TargetDistance;
            _stateTargetPitch = newConfig.TargetPitch;
            _stateTargetFOV = newConfig.FOVOverride > 0 ? newConfig.FOVOverride : BaseFOV;
            
            // 开始过渡
            _isTransitioning = true;
            
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

        #region 锁定目标系统

        public void ActivateLockOn()
        {
            if (!EnableLockOn) return;
            Actor bestTarget = FindBestLockOnTarget();
            if (bestTarget != null)
            {
                _lockOnTarget = bestTarget;
                _isLockOnActive = true;
                _lockOnLostTimer = 0f;
                if (bestTarget.HasTag(BossTag))
                {
                    SwitchState(CameraState.BossBattle);
                }
                else
                {
                    SwitchState(CameraState.LockOn);
                }
            }
        }

        public void DeactivateLockOn()
        {
            _lockOnTarget = null;
            _isLockOnActive = false;
            _softLockTarget = null;
            _lockOnLostTimer = 0f;
            if (CurrentState == CameraState.LockOn || CurrentState == CameraState.BossBattle)
            {
                SwitchState(_isInCombat ? CameraState.OverShoulder : CameraState.Normal);
            }
        }

        public void SwitchLockOnTarget(float direction)
        {
            if (!_isLockOnActive || _lockOnTarget == null) return;
            Actor nextTarget = FindAdjacentTarget(direction);
            if (nextTarget != null)
            {
                _lockOnTarget = nextTarget;
                if (nextTarget.HasTag(BossTag) && CurrentState != CameraState.BossBattle)
                {
                    SwitchState(CameraState.BossBattle);
                }
                else if (!nextTarget.HasTag(BossTag) && CurrentState == CameraState.BossBattle)
                {
                    SwitchState(CameraState.LockOn);
                }
            }
        }

        public Actor GetLockOnTarget()
        {
            return _isLockOnActive ? _lockOnTarget : null;
        }

        public Actor GetSoftLockTarget()
        {
            return _softLockTarget;
        }

        public bool IsLockOnActive()
        {
            return _isLockOnActive;
        }

        private Actor FindBestLockOnTarget()
        {
            if (Target == null) return null;
            Vector3 forward = _camera != null ? _camera.Transform.Forward : Vector3.Forward;
            Vector3 cameraPos = Target.Position + GetDynamicFocusOffset();
            float bestScore = float.MinValue;
            Actor bestTarget = null;
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (!IsValidLockOnCandidate(actor)) continue;
                if (!actor.IsActiveInHierarchy) continue;
                Vector3 toTarget = actor.Position - cameraPos;
                float distance = toTarget.Length;
                if (distance > LockOnMaxDistance) continue;
                Vector3 dirToTarget = Vector3.Normalize(toTarget);
                float angle = Mathf.Acos(Vector3.Dot(forward, dirToTarget)) * Mathf.RadiansToDegrees;
                if (angle > LockOnDetectionAngle) continue;
                float score = -distance - angle * 10f;
                if (actor.HasTag(BossTag)) score += 1000f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = actor;
                }
            }
            return bestTarget;
        }

        private Actor FindAdjacentTarget(float direction)
        {
            if (_lockOnTarget == null || Target == null) return null;
            Vector3 toCurrent = _lockOnTarget.Position - Target.Position;
            Vector3 right = Vector3.Cross(Vector3.Up, toCurrent);
            right.Normalize();
            float bestScore = float.MinValue;
            Actor bestTarget = null;
            foreach (var actor in Level.GetActors<Actor>())
            {
                if (actor == _lockOnTarget || !IsValidLockOnCandidate(actor)) continue;
                if (!actor.IsActiveInHierarchy) continue;
                Vector3 toTarget = actor.Position - Target.Position;
                float distance = toTarget.Length;
                if (distance > LockOnMaxDistance) continue;
                float dot = Vector3.Dot(right, toTarget.Normalized);
                if ((direction > 0 && dot < 0.1f) || (direction < 0 && dot > -0.1f)) continue;
                float score = Mathf.Abs(dot) - distance * 0.01f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = actor;
                }
            }
            return bestTarget;
        }

        private void UpdateLockOn()
        {
            if (!_isLockOnActive || _lockOnTarget == null)
            {
                UpdateSoftLock();
                return;
            }
            if (!_lockOnTarget.IsActiveInHierarchy)
            {
                DeactivateLockOn();
                return;
            }
            float distance = Vector3.Distance(Target.Position, _lockOnTarget.Position);
            if (distance > LockOnMaxDistance)
            {
                _lockOnLostTimer += Time.DeltaTime;
                if (_lockOnLostTimer > LockOnLostDelay)
                {
                    DeactivateLockOn();
                    return;
                }
            }
            else
            {
                _lockOnLostTimer = 0f;
            }
            Vector3 targetDir = _lockOnTarget.Position - Target.Position;
            float targetYaw = Mathf.Atan2(targetDir.X, targetDir.Z) * Mathf.RadiansToDegrees;

            // 必须走最短路径：Yaw 归一化在 [0,360)，而 Atan2 返回 (-180,180]，
            // 直接 Lerp 会在 0/360 交界处绕远路（最多 360°，镜头会转一大圈）
            float lockYawDelta = Mathf.DeltaAngle(Yaw, targetYaw);
            Yaw += lockYawDelta * Mathf.Clamp(Time.DeltaTime * LockOnSmoothSpeed, 0f, 1f);

            if (Yaw < 0f) Yaw += 360f;
            else if (Yaw >= 360f) Yaw -= 360f;
        }

        private void UpdateSoftLock()
        {
            if (!EnableLockOn || _isLockOnActive) return;

            // 软锁定只服务于战斗中的准心吸附，非战斗状态无需做全场景扫描
            if (!_isInCombat)
            {
                _softLockTarget = null;
                return;
            }

            if (Target == null || _camera == null) return;

            // 全场景枚举开销大（Level.GetActors 每次都会分配数组），软锁定无需逐帧刷新候选目标
            if (_gameTime - _lastSoftLockScanTime >= SoftLockScanInterval)
            {
                _lastSoftLockScanTime = _gameTime;

                _softLockTarget = null;
                Vector3 forward = _camera.Transform.Forward;
                Vector3 cameraPos = Target.Position + GetDynamicFocusOffset();
                float bestScore = float.MinValue;
                foreach (var actor in Level.GetActors<Actor>())
                {
                    if (!IsValidLockOnCandidate(actor)) continue;
                    if (!actor.IsActiveInHierarchy) continue;
                    Vector3 toTarget = actor.Position - cameraPos;
                    float distance = toTarget.Length;
                    if (distance > LockOnMaxDistance) continue;
                    Vector3 dirToTarget = Vector3.Normalize(toTarget);
                    float angle = Mathf.Acos(Vector3.Dot(forward, dirToTarget)) * Mathf.RadiansToDegrees;
                    if (angle > SoftLockAngle) continue;
                    float score = -distance;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        _softLockTarget = actor;
                    }
                }
            }

            if (_softLockTarget != null)
            {
                Vector3 toSoftTarget = _softLockTarget.Position - Target.Position;
                float softYaw = Mathf.Atan2(toSoftTarget.X, toSoftTarget.Z) * Mathf.RadiansToDegrees;
                float yawDiff = Mathf.DeltaAngle(Yaw, softYaw);
                Yaw += yawDiff * SoftLockInfluence * Time.DeltaTime;

                // 维持 Yaw 在 [0,360) 约定，避免持续单向吸附造成角度漂移出界
                if (Yaw < 0f) Yaw += 360f;
                else if (Yaw >= 360f) Yaw -= 360f;
            }
        }

        /// <summary>
        /// 设置战斗状态。进入/退出战斗时由战斗或技能系统调用，驱动 OverShoulder 相机姿态与软锁定吸附。
        /// </summary>
        public void SetCombatState(bool inCombat)
        {
            _isInCombat = inCombat;
        }

        /// <summary>
        /// 外部镜头特效（击杀特写、剧情演出等）接管相机时调用。
        /// 置为 true 后本相机仍继续做跟随计算，但不再写入位置与朝向，
        /// 避免与外部镜头逐帧互相覆盖（谁是最后写入者不确定）。
        /// </summary>
        public void SetExternalCameraControl(bool active)
        {
            _externalCameraControl = active;

            if (!active)
            {
                // 交还控制权时同步跟随缓存：特写期间角色已移动，
                // 否则恢复首帧会因位置差过大算出巨量的移动速度，导致 FOV 突变
                if (Target != null) _lastTargetPosition = Target.Position;
                _groundProbeValid = false;   // 特写机位的地面探针已作废，交还后重新探测
                _smoothedFloorValid = false; // 托底高度重新锚定，避免从特写机位长距离回拉
            }
        }

        /// <summary>
        /// 当前是否有外部系统接管相机
        /// </summary>
        public bool IsExternalCameraControlActive()
        {
            return _externalCameraControl;
        }

        /// <summary>
        /// 当前是否处于战斗状态
        /// </summary>
        public bool GetCombatState()
        {
            return _isInCombat;
        }

        public Vector3 GetCameraRelativeMoveDirection(Vector2 inputDirection)
        {
            if (inputDirection.Length < 0.01f) return Vector3.Zero;
            Vector3 forward;
            Vector3 right;
            if (CurrentState == CameraState.LockOn && _lockOnTarget != null && Target != null)
            {
                Vector3 toTarget = _lockOnTarget.Position - Target.Position;
                toTarget.Y = 0;
                if (toTarget.Length < 0.01f) toTarget = Vector3.Forward;
                forward = Vector3.Normalize(toTarget);
                right = Vector3.Cross(Vector3.Up, forward);
            }
            else if (CurrentState == CameraState.OverShoulder && _camera != null)
            {
                forward = _camera.Transform.Forward;
                forward.Y = 0;
                if (forward.Length < 0.01f) forward = Vector3.Forward;
                forward = Vector3.Normalize(forward);
                right = Vector3.Cross(Vector3.Up, forward);
                right.Y = 0;
                if (right.Length < 0.01f) right = Vector3.Right;
                right = Vector3.Normalize(right);
            }
            else
            {
                return Vector3.Zero;
            }
            return Vector3.Normalize(forward * inputDirection.Y + right * inputDirection.X);
        }

        public bool ShouldUseCameraRelativeMovement()
        {
            return CurrentState == CameraState.LockOn || CurrentState == CameraState.OverShoulder;
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
            _environmentDetectionTimer = 0f;
            
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
            Vector3[] directions = EnclosedSpaceDirections;
            
            float[] distances = EnclosedSpaceDistances;
            
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
            
            // 与 DetectEnclosedSpace 保持一致：起点抬到头顶之外、带层级掩码、并排除角色自身碰撞体。
            // 否则射线从角色身体内部（脚上 1m 处）出发、且不带掩码，会立刻命中自己（约等于胶囊半径），
            // 使平均距离恒小于 10，于是任何封闭空间都被误判成 Corridor。
            Vector3 targetPos = Target.Position + Vector3.Up * 2.0f;
            
            // 水平四个方向发射射线
            Vector3[] directions = SpaceSizeDirections;
            
            float totalDistance = 0f;
            int hitCount = 0;
            float maxDetectionDistance = 100f;
            
            foreach (var dir in directions)
            {
                // 未命中障碍时按"开阔"计入最大距离
                float distance = maxDetectionDistance;
                
                if (Physics.RayCast(targetPos, dir, out RayCastHit hit, maxDetectionDistance, CollisionLayerMask))
                {
                    var hitActor = hit.Collider?.AttachedRigidBody?.Parent ?? hit.Collider?.Parent;
                    bool selfHit = AutoExcludeTargetCollision && Target != null && hit.Collider != null
                                   && (hitActor == Target || IsChildOf(hitActor, Target));
                    
                    // 命中角色自身不算"墙"
                    if (!selfHit)
                    {
                        distance = hit.Distance;
                    }
                }
                
                totalDistance += distance;
                hitCount++;
            }
            
            // 返回平均距离作为空间大小指标
            return hitCount > 0 ? totalDistance / hitCount : maxDetectionDistance;
        }
        
        // 环境检测方向常量：避免每次检测都分配数组
        private static readonly Vector3[] EnclosedSpaceDirections = new Vector3[]
        {
            Vector3.Up,       // 上(检测天花板/屋顶)
            Vector3.Forward,  // 前
            Vector3.Backward, // 后
            Vector3.Left,     // 左
            Vector3.Right     // 右
        };
        private static readonly float[] EnclosedSpaceDistances = new float[] { 100f, 50f, 50f, 50f, 50f };
        private static readonly Vector3[] SpaceSizeDirections = new Vector3[]
        {
            Vector3.Forward,
            Vector3.Backward,
            Vector3.Left,
            Vector3.Right
        };
        
        /// <summary>
        /// 切换环境
        /// </summary>
        public void SwitchEnvironment(EnvironmentType newEnvironment)
        {
            if (newEnvironment == CurrentEnvironment)
            {
                return;
            }
            
            CurrentEnvironment = newEnvironment;
            
            // 应用新环境配置
            ApplyEnvironmentConfig(newEnvironment);
            
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
            _environmentElasticCoefficient = config.ElasticityMultiplier;
            
            //Debug.Log($"[环境配置] 应用配置 - 距离限制:[{_environmentMinDistance:F1}-{_environmentMaxDistance:F1}], FOV倍数:{config.FOVMultiplier:F2}, 弹性系数:{_environmentElasticCoefficient:F2}");
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
        
        #endregion
        
        #region 异步检测系统
        
        /// <summary>
        /// 初始化异步检测系统
        /// </summary>
        private void InitializeAsyncDetection()
        {
            _lastCollisionResult = new AsyncCollisionResult();

            _lastAsyncCollisionCheckTime = 0f;
        }
        
        /// <summary>
        /// 异步碰撞检测（适用于高质量模式）
        /// </summary>
        private async Task<AsyncCollisionResult> DetectCollisionAsync(Vector3 focusPoint, float pitch, float yaw, float distance)
        {
            var result = new AsyncCollisionResult();

            // 在主线程取时间戳，避免工作线程读取主线程字段
            float detectionTime = _gameTime;

            await Task.Run(() =>
            {
                Vector3 idealCameraPos = CalculateCameraPosition(focusPoint, pitch, yaw, distance);
                Vector3 direction = idealCameraPos - focusPoint;
                float targetDistance = direction.Length;
                
                if (targetDistance < 0.001f)
                {
                    result.HasCollision = false;
                    result.SafeDistance = distance;
                    result.DetectionTime = detectionTime;
                    return;
                }
                
                direction.Normalize();
                
                bool hasCollision = false;
                float minDistance = distance;

                // 与同步检测一致的视平面探针基
                BuildRayBasis(direction, out Vector3 rayRight, out Vector3 rayUp);

                // 执行多重射线检测
                foreach (var offset in _collisionRayDirections)
                {
                    Vector3 startPoint = focusPoint + rayRight * offset.X + rayUp * offset.Y;
                    
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
                result.DetectionTime = detectionTime;
            });
            
            return result;
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
        /// 获取异步碰撞检测结果
        /// </summary>
        private bool TryGetAsyncCollisionResult(out bool hasCollision, out float safeDistance)
        {
            hasCollision = false;
            safeDistance = Distance;
            
            if (_collisionDetectionTask != null && _collisionDetectionTask.IsCompleted)
            {
                _lastCollisionResult = _collisionDetectionTask.Result;
                _collisionDetectionTask = null; // 清理已完成的任务

                // 统计在主线程累加，避免后台线程与 OnUpdate 竞争写 _performanceStats
                if (EnablePerformanceMonitoring)
                {
                    _performanceStats.CollisionCheckCount++;
                }
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
