using FlaxEngine;
using Game;
using System.Collections.Generic;

namespace HundunWorld.Game.Performance
{
    /// <summary>
    /// 系统性能优化器，负责动态调整系统参数以维持最佳性能
    /// </summary>
    public class SystemOptimizer : Script
    {
        #region 优化配置

        /// <summary>
        /// 是否启用自动优化
        /// </summary>
        [Tooltip("是否启用自动优化")]
        public bool EnableAutoOptimization { get; set; } = true;

        /// <summary>
        /// 优化检查间隔（秒）
        /// </summary>
        [Tooltip("优化检查间隔（秒）")]
        public float OptimizationInterval { get; set; } = 2.0f;

        /// <summary>
        /// 目标帧率
        /// </summary>
        [Tooltip("目标帧率")]
        public float TargetFrameRate { get; set; } = 60.0f;

        /// <summary>
        /// 性能下降阈值
        /// </summary>
        [Tooltip("性能下降阈值")]
        public float PerformanceThreshold { get; set; } = 0.8f;

        /// <summary>
        /// 上次优化时间
        /// </summary>
        private float _lastOptimizationTime = 0f;

        /// <summary>
        /// 当前优化级别
        /// </summary>
        public OptimizationLevel CurrentOptimizationLevel { get; private set; } = OptimizationLevel.High;

        #endregion

        #region 组件引用

        /// <summary>
        /// 性能监控器引用
        /// </summary>
        private PerformanceMonitor _performanceMonitor;

        /// <summary>
        /// 第三人称相机引用
        /// </summary>
        private ThirdPersonCamera _thirdPersonCamera;

        /// <summary>
        /// Player Controller引用
        /// </summary>
        private PlayerController _playerController;

        /// <summary>
        /// 相机震动系统引用
        /// </summary>
        private CameraShakeSystem _cameraShakeSystem;

        /// <summary>
        /// 动态相机调整器引用
        /// </summary>
        private DynamicCameraAdjuster _dynamicCameraAdjuster;

        #endregion

        #region 优化记录

        /// <summary>
        /// 优化历史记录
        /// </summary>
        private Queue<OptimizationRecord> _optimizationHistory = new Queue<OptimizationRecord>();

        /// <summary>
        /// 最大历史记录数量
        /// </summary>
        private const int MaxHistoryCount = 50;

        /// <summary>
        /// 优化统计
        /// </summary>
        private OptimizationStats _stats = new OptimizationStats();

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            // 获取组件引用
            _performanceMonitor = Actor.GetScript<PerformanceMonitor>();
            _thirdPersonCamera = Actor.GetScript<ThirdPersonCamera>();
            _playerController = Actor.GetScript<PlayerController>();
            _cameraShakeSystem = Actor.GetScript<CameraShakeSystem>();
            _dynamicCameraAdjuster = Actor.GetScript<DynamicCameraAdjuster>();

            Debug.Log("系统性能优化器已初始化");
        }

        public override void OnUpdate()
        {
            if (!EnableAutoOptimization) return;

            // 定期进行优化检查
            if (Time.GameTime - _lastOptimizationTime >= OptimizationInterval)
            {
                PerformOptimizationCheck();
                _lastOptimizationTime = Time.GameTime;
            }
        }

        #endregion

        #region 性能优化

        /// <summary>
        /// 执行优化检查
        /// </summary>
        private void PerformOptimizationCheck()
        {
            if (_performanceMonitor == null) return;

            // 获取当前性能指标
            float currentFPS = _performanceMonitor.CurrentFPS;
            float averageFPS = _performanceMonitor.AverageFPS;
            float raycastFrequency = _performanceMonitor.RaycastFrequency;

            // 计算性能比率
            float performanceRatio = averageFPS / TargetFrameRate;

            // 确定需要的优化级别
            OptimizationLevel requiredLevel = DetermineOptimizationLevel(performanceRatio, raycastFrequency);

            // 应用优化
            if (requiredLevel != CurrentOptimizationLevel)
            {
                ApplyOptimizationLevel(requiredLevel);
                RecordOptimization(CurrentOptimizationLevel, requiredLevel, performanceRatio);
                CurrentOptimizationLevel = requiredLevel;
            }

            // 更新统计
            UpdateOptimizationStats(performanceRatio);
        }

        /// <summary>
        /// 确定优化级别
        /// </summary>
        /// <param name="performanceRatio">性能比率</param>
        /// <param name="raycastFrequency">射线检测频率</param>
        /// <returns>优化级别</returns>
        private OptimizationLevel DetermineOptimizationLevel(float performanceRatio, float raycastFrequency)
        {
            // 基于性能比率确定级别
            if (performanceRatio >= 1.0f)
            {
                return OptimizationLevel.High;
            }
            else if (performanceRatio >= PerformanceThreshold)
            {
                return OptimizationLevel.Medium;
            }
            else if (performanceRatio >= 0.5f)
            {
                return OptimizationLevel.Low;
            }
            else
            {
                return OptimizationLevel.Minimal;
            }
        }

        /// <summary>
        /// 应用优化级别
        /// </summary>
        /// <param name="level">优化级别</param>
        private void ApplyOptimizationLevel(OptimizationLevel level)
        {
            Debug.Log($"应用优化级别: {level}");

            // 相机系统优化
            OptimizeCameraSystem(level);

            // 碰撞检测优化
            OptimizeCollisionDetection(level);

            // 震动系统优化
            OptimizeShakeSystem(level);

            // 动态调整器优化
            OptimizeDynamicAdjuster(level);

            // 角色控制器优化
            OptimizePlayerController(level);
        }

        /// <summary>
        /// 优化相机系统
        /// </summary>
        /// <param name="level">优化级别</param>
        private void OptimizeCameraSystem(OptimizationLevel level)
        {
            if (_thirdPersonCamera == null) return;

            // 新版ThirdPersonCamera已移除平滑跟随功能，这里保留空实现以兼容
            // 相机的平滑效果现在由CollisionSmoothSpeed等其他参数控制
            switch (level)
            {
                case OptimizationLevel.High:
                    _thirdPersonCamera.CollisionSmoothSpeed = 10f;
                    break;

                case OptimizationLevel.Medium:
                    _thirdPersonCamera.CollisionSmoothSpeed = 8f;
                    break;

                case OptimizationLevel.Low:
                    _thirdPersonCamera.CollisionSmoothSpeed = 5f;
                    break;

                case OptimizationLevel.Minimal:
                    _thirdPersonCamera.CollisionSmoothSpeed = 3f;
                    break;
            }
        }

        /// <summary>
        /// 优化碰撞检测
        /// </summary>
        /// <param name="level">优化级别</param>
        private void OptimizeCollisionDetection(OptimizationLevel level)
        {
            if (_thirdPersonCamera == null) return;

            switch (level)
            {
                case OptimizationLevel.High:
                    _thirdPersonCamera.CollisionRayCount = 5;
                    _thirdPersonCamera.EnableCameraCollision = true;
                    break;

                case OptimizationLevel.Medium:
                    _thirdPersonCamera.CollisionRayCount = 3;
                    _thirdPersonCamera.EnableCameraCollision = true;
                    break;

                case OptimizationLevel.Low:
                    _thirdPersonCamera.CollisionRayCount = 1;
                    _thirdPersonCamera.EnableCameraCollision = true;
                    break;

                case OptimizationLevel.Minimal:
                    _thirdPersonCamera.CollisionRayCount = 1;
                    _thirdPersonCamera.EnableCameraCollision = false;
                    break;
            }
        }

        /// <summary>
        /// 优化震动系统
        /// </summary>
        /// <param name="level">优化级别</param>
        private void OptimizeShakeSystem(OptimizationLevel level)
        {
            if (_cameraShakeSystem == null) return;

            switch (level)
            {
                case OptimizationLevel.High:
                    _cameraShakeSystem.EnableShake = true;
                    _cameraShakeSystem.ShakeIntensityMultiplier = 1.0f;
                    break;

                case OptimizationLevel.Medium:
                    _cameraShakeSystem.EnableShake = true;
                    _cameraShakeSystem.ShakeIntensityMultiplier = 0.7f;
                    break;

                case OptimizationLevel.Low:
                    _cameraShakeSystem.EnableShake = true;
                    _cameraShakeSystem.ShakeIntensityMultiplier = 0.5f;
                    break;

                case OptimizationLevel.Minimal:
                    _cameraShakeSystem.EnableShake = false;
                    _cameraShakeSystem.ShakeIntensityMultiplier = 0f;
                    break;
            }
        }

        /// <summary>
        /// 优化动态调整器
        /// </summary>
        /// <param name="level">优化级别</param>
        private void OptimizeDynamicAdjuster(OptimizationLevel level)
        {
            if (_dynamicCameraAdjuster == null) return;

            switch (level)
            {
                case OptimizationLevel.High:
                    _dynamicCameraAdjuster.FOVAdjustSpeed = 30.0f;
                    _dynamicCameraAdjuster.ObstacleDetectionRadius = 1.0f;
                    break;

                case OptimizationLevel.Medium:
                    _dynamicCameraAdjuster.FOVAdjustSpeed = 20.0f;
                    _dynamicCameraAdjuster.ObstacleDetectionRadius = 0.8f;
                    break;

                case OptimizationLevel.Low:
                    _dynamicCameraAdjuster.FOVAdjustSpeed = 10.0f;
                    _dynamicCameraAdjuster.ObstacleDetectionRadius = 0.5f;
                    break;

                case OptimizationLevel.Minimal:
                    _dynamicCameraAdjuster.FOVAdjustSpeed = 5.0f;
                    _dynamicCameraAdjuster.ObstacleDetectionRadius = 0.3f;
                    break;
            }
        }

        /// <summary>
        /// 优化角色控制器
        /// </summary>
        /// <param name="level">优化级别</param>
        private void OptimizePlayerController(OptimizationLevel level)
        {
            if (_playerController == null) return;

            switch (level)
            {
                case OptimizationLevel.High:
                    _playerController.RotationSmoothing = 0.1f;
                    _playerController.Acceleration = 20.0f;
                    _playerController.Deceleration = 25.0f;
                    break;

                case OptimizationLevel.Medium:
                    _playerController.RotationSmoothing = 0.05f;
                    _playerController.Acceleration = 15.0f;
                    _playerController.Deceleration = 20.0f;
                    break;

                case OptimizationLevel.Low:
                    _playerController.RotationSmoothing = 0f;
                    _playerController.Acceleration = 10.0f;
                    _playerController.Deceleration = 15.0f;
                    break;

                case OptimizationLevel.Minimal:
                    _playerController.RotationSmoothing = 0f;
                    _playerController.Acceleration = 5.0f;
                    _playerController.Deceleration = 10.0f;
                    break;
            }
        }

        #endregion

        #region 优化记录和统计

        /// <summary>
        /// 记录优化操作
        /// </summary>
        /// <param name="fromLevel">原始级别</param>
        /// <param name="toLevel">目标级别</param>
        /// <param name="performanceRatio">性能比率</param>
        private void RecordOptimization(OptimizationLevel fromLevel, OptimizationLevel toLevel, float performanceRatio)
        {
            var record = new OptimizationRecord
            {
                Timestamp = Time.GameTime,
                FromLevel = fromLevel,
                ToLevel = toLevel,
                PerformanceRatio = performanceRatio,
                Reason = DetermineOptimizationReason(fromLevel, toLevel)
            };

            _optimizationHistory.Enqueue(record);

            // 限制历史记录数量
            while (_optimizationHistory.Count > MaxHistoryCount)
            {
                _optimizationHistory.Dequeue();
            }

            Debug.Log($"优化记录: {fromLevel} -> {toLevel} (性能比率: {performanceRatio:F2})");
        }

        /// <summary>
        /// 确定优化原因
        /// </summary>
        /// <param name="fromLevel">原始级别</param>
        /// <param name="toLevel">目标级别</param>
        /// <returns>优化原因</returns>
        private string DetermineOptimizationReason(OptimizationLevel fromLevel, OptimizationLevel toLevel)
        {
            if ((int)toLevel < (int)fromLevel)
            {
                return "性能下降，降低质量设置";
            }
            else if ((int)toLevel > (int)fromLevel)
            {
                return "性能提升，恢复质量设置";
            }
            else
            {
                return "维持当前设置";
            }
        }

        /// <summary>
        /// 更新优化统计
        /// </summary>
        /// <param name="performanceRatio">性能比率</param>
        private void UpdateOptimizationStats(float performanceRatio)
        {
            _stats.TotalChecks++;
            _stats.AveragePerformanceRatio = (_stats.AveragePerformanceRatio * (_stats.TotalChecks - 1) + performanceRatio) / _stats.TotalChecks;

            if (performanceRatio < PerformanceThreshold)
            {
                _stats.LowPerformanceEvents++;
            }

            _stats.CurrentPerformanceRatio = performanceRatio;
        }

        #endregion

        #region 公共接口

        /// <summary>
        /// 手动设置优化级别
        /// </summary>
        /// <param name="level">优化级别</param>
        public void SetOptimizationLevel(OptimizationLevel level)
        {
            if (level != CurrentOptimizationLevel)
            {
                OptimizationLevel previousLevel = CurrentOptimizationLevel;
                ApplyOptimizationLevel(level);
                RecordOptimization(previousLevel, level, _stats.CurrentPerformanceRatio);
                CurrentOptimizationLevel = level;
            }
        }

        /// <summary>
        /// 获取优化历史
        /// </summary>
        /// <returns>优化历史记录</returns>
        public List<OptimizationRecord> GetOptimizationHistory()
        {
            return new List<OptimizationRecord>(_optimizationHistory);
        }

        /// <summary>
        /// 获取优化统计
        /// </summary>
        /// <returns>优化统计</returns>
        public OptimizationStats GetOptimizationStats()
        {
            return _stats;
        }

        /// <summary>
        /// 重置优化统计
        /// </summary>
        public void ResetStats()
        {
            _stats = new OptimizationStats();
            _optimizationHistory.Clear();
        }

        /// <summary>
        /// 强制进行优化检查
        /// </summary>
        public void ForceOptimizationCheck()
        {
            PerformOptimizationCheck();
        }

        #endregion
    }

    #region 优化相关结构和枚举

    /// <summary>
    /// 优化级别
    /// </summary>
    public enum OptimizationLevel
    {
        /// <summary>
        /// 最小化（最低质量）
        /// </summary>
        Minimal = 0,
        
        /// <summary>
        /// 低质量
        /// </summary>
        Low = 1,
        
        /// <summary>
        /// 中等质量
        /// </summary>
        Medium = 2,
        
        /// <summary>
        /// 高质量
        /// </summary>
        High = 3
    }

    /// <summary>
    /// 优化记录
    /// </summary>
    public struct OptimizationRecord
    {
        public float Timestamp;
        public OptimizationLevel FromLevel;
        public OptimizationLevel ToLevel;
        public float PerformanceRatio;
        public string Reason;
    }

    /// <summary>
    /// 优化统计
    /// </summary>
    public struct OptimizationStats
    {
        public int TotalChecks;
        public int LowPerformanceEvents;
        public float AveragePerformanceRatio;
        public float CurrentPerformanceRatio;
    }

    #endregion
}