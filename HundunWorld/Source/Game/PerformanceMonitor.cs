using FlaxEngine;
using Game;
using System.Collections.Generic;

namespace HundunWorld.Game
{
    /// <summary>
    /// 性能监控器，用于监控和优化相机系统性能
    /// </summary>
    public class PerformanceMonitor : Script
    {
        #region 性能监控参数

        /// <summary>
        /// 性能监控间隔（秒）
        /// </summary>
        [Tooltip("性能监控间隔（秒）")]
        public float MonitorInterval { get; set; } = 1.0f;

        /// <summary>
        /// 射线检测计数器
        /// </summary>
        private int _raycastCount = 0;

        /// <summary>
        /// 上次监控时间
        /// </summary>
        private float _lastMonitorTime = 0f;

        /// <summary>
        /// 性能统计数据
        /// </summary>
        private Dictionary<string, float> _performanceStats = new Dictionary<string, float>();

        /// <summary>
        /// 帧时间历史记录
        /// </summary>
        private Queue<float> _frameTimeHistory = new Queue<float>();

        /// <summary>
        /// 最大历史记录数量
        /// </summary>
        private const int MaxHistoryCount = 60;

        /// <summary>
        /// 当前帧率
        /// </summary>
        public float CurrentFPS { get; private set; } = 60.0f;

        /// <summary>
        /// 平均帧率
        /// </summary>
        public float AverageFPS { get; private set; } = 60.0f;

        /// <summary>
        /// 射线检测频率（每秒）
        /// </summary>
        public float RaycastFrequency { get; private set; } = 0f;

        #endregion

        #region 自适应性能调整

        /// <summary>
        /// 是否启用自适应性能调整
        /// </summary>
        [Tooltip("是否启用自适应性能调整")]
        public bool EnableAdaptivePerformance { get; set; } = true;

        /// <summary>
        /// 目标帧率
        /// </summary>
        [Tooltip("目标帧率")]
        public float TargetFPS { get; set; } = 60.0f;

        /// <summary>
        /// 低性能阈值
        /// </summary>
        [Tooltip("低性能阈值")]
        public float LowPerformanceThreshold { get; set; } = 30.0f;

        /// <summary>
        /// 性能调整延迟（秒）
        /// </summary>
        [Tooltip("性能调整延迟（秒）")]
        public float PerformanceAdjustDelay { get; set; } = 3.0f;

        /// <summary>
        /// 上次性能调整时间
        /// </summary>
        private float _lastPerformanceAdjustTime = 0f;

        /// <summary>
        /// 当前性能等级
        /// </summary>
        public PerformanceLevel CurrentPerformanceLevel { get; private set; } = PerformanceLevel.High;

        #endregion

        #region 生命周期方法

        public override void OnStart()
        {
            _lastMonitorTime = Time.GameTime;
            _lastPerformanceAdjustTime = Time.GameTime;
        }

        public override void OnUpdate()
        {
            // 更新帧时间统计
            UpdateFrameTimeStats();

            // 定期进行性能监控
            if (Time.GameTime - _lastMonitorTime >= MonitorInterval)
            {
                PerformPerformanceMonitoring();
                _lastMonitorTime = Time.GameTime;
            }

            // 自适应性能调整
            if (EnableAdaptivePerformance && Time.GameTime - _lastPerformanceAdjustTime >= PerformanceAdjustDelay)
            {
                PerformAdaptiveAdjustment();
                _lastPerformanceAdjustTime = Time.GameTime;
            }
        }

        #endregion

        #region 性能监控

        /// <summary>
        /// 更新帧时间统计
        /// </summary>
        private void UpdateFrameTimeStats()
        {
            float deltaTime = Time.DeltaTime;
            
            // 添加到历史记录
            _frameTimeHistory.Enqueue(deltaTime);
            if (_frameTimeHistory.Count > MaxHistoryCount)
            {
                _frameTimeHistory.Dequeue();
            }

            // 计算当前FPS
            CurrentFPS = deltaTime > 0 ? 1.0f / deltaTime : 0f;

            // 计算平均FPS
            if (_frameTimeHistory.Count > 0)
            {
                float totalTime = 0f;
                foreach (float time in _frameTimeHistory)
                {
                    totalTime += time;
                }
                float avgTime = totalTime / _frameTimeHistory.Count;
                AverageFPS = avgTime > 0 ? 1.0f / avgTime : 0f;
            }
        }

        /// <summary>
        /// 执行性能监控
        /// </summary>
        private void PerformPerformanceMonitoring()
        {
            // 计算射线检测频率
            RaycastFrequency = _raycastCount / MonitorInterval;
            _raycastCount = 0;

            // 更新性能统计
            _performanceStats["CurrentFPS"] = CurrentFPS;
            _performanceStats["AverageFPS"] = AverageFPS;
            _performanceStats["RaycastFrequency"] = RaycastFrequency;
            _performanceStats["MemoryUsage"] = GetMemoryUsage();

            // 输出性能日志（可选）
            if (RaycastFrequency > 100) // 如果射线检测过于频繁
            {
                Debug.LogWarning($"高频射线检测警告: {RaycastFrequency:F1} 次/秒");
            }
        }

        /// <summary>
        /// 自适应性能调整
        /// </summary>
        private void PerformAdaptiveAdjustment()
        {
            PerformanceLevel newLevel = DeterminePerformanceLevel();
            
            if (newLevel != CurrentPerformanceLevel)
            {
                ApplyPerformanceLevel(newLevel);
                CurrentPerformanceLevel = newLevel;
                Debug.Log($"性能等级调整为: {newLevel}");
            }
        }

        /// <summary>
        /// 确定性能等级
        /// </summary>
        /// <returns>性能等级</returns>
        private PerformanceLevel DeterminePerformanceLevel()
        {
            if (AverageFPS >= TargetFPS * 0.9f)
            {
                return PerformanceLevel.High;
            }
            else if (AverageFPS >= LowPerformanceThreshold)
            {
                return PerformanceLevel.Medium;
            }
            else
            {
                return PerformanceLevel.Low;
            }
        }

        /// <summary>
        /// 应用性能等级设置
        /// </summary>
        /// <param name="level">性能等级</param>
        private void ApplyPerformanceLevel(PerformanceLevel level)
        {
            // 获取第三人称相机脚本
            var thirdPersonCamera = Actor.GetScript<ThirdPersonCamera>();
            
            if (thirdPersonCamera != null)
            {
                switch (level)
                {
                    case PerformanceLevel.High:
                        // 高性能模式：启用所有功能
                        thirdPersonCamera.CollisionRayCount = 5;
                        thirdPersonCamera.EnableCameraCollision = true;
                        break;
                        
                    case PerformanceLevel.Medium:
                        // 中等性能模式：减少一些检测
                        thirdPersonCamera.CollisionRayCount = 3;
                        thirdPersonCamera.EnableCameraCollision = true;
                        break;
                        
                    case PerformanceLevel.Low:
                        // 低性能模式：最小化检测
                        thirdPersonCamera.CollisionRayCount = 1;
                        thirdPersonCamera.EnableCameraCollision = true;
                        break;
                }
            }
        }

        /// <summary>
        /// 获取内存使用情况
        /// </summary>
        /// <returns>内存使用量（MB）</returns>
        private float GetMemoryUsage()
        {
            // 在实际项目中，这里应该实现真正的内存监控
            // 这里返回一个模拟值
            return System.GC.GetTotalMemory(false) / (1024f * 1024f);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 记录射线检测调用
        /// </summary>
        public void RecordRaycast()
        {
            _raycastCount++;
        }

        /// <summary>
        /// 获取性能统计
        /// </summary>
        /// <param name="statName">统计名称</param>
        /// <returns>统计值</returns>
        public float GetPerformanceStat(string statName)
        {
            return _performanceStats.ContainsKey(statName) ? _performanceStats[statName] : 0f;
        }

        /// <summary>
        /// 获取所有性能统计
        /// </summary>
        /// <returns>性能统计字典</returns>
        public Dictionary<string, float> GetAllPerformanceStats()
        {
            return new Dictionary<string, float>(_performanceStats);
        }

        /// <summary>
        /// 重置性能统计
        /// </summary>
        public void ResetStats()
        {
            _performanceStats.Clear();
            _frameTimeHistory.Clear();
            _raycastCount = 0;
        }

        #endregion
    }

    /// <summary>
    /// 性能等级枚举
    /// </summary>
    public enum PerformanceLevel
    {
        /// <summary>
        /// 低性能
        /// </summary>
        Low,
        
        /// <summary>
        /// 中等性能
        /// </summary>
        Medium,
        
        /// <summary>
        /// 高性能
        /// </summary>
        High
    }
}