using System;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FlaxEngine;
using Horizon.Game.Message.Enums;
using HundunWorld.Game.UI.Events;

namespace Game.UI.Performance
{
    /// <summary>
    /// UI性能监控系统
    /// 监控UI切换性能指标，包括切换时间、内存使用、帧率影响等
    /// </summary>
    public class UIPerformanceMonitor : Script
    {
        #region Events and Delegates

        /// <summary>
        /// 性能警告事件
        /// </summary>
        public event Action<PerformanceWarning> OnPerformanceWarning;

        /// <summary>
        /// 性能报告生成事件
        /// </summary>
        public event Action<PerformanceReport> OnPerformanceReport;

        #endregion

        #region Private Fields

        private readonly Dictionary<string, SwitchPerformanceData> _switchMetrics = new Dictionary<string, SwitchPerformanceData>();
        private readonly List<FramePerformanceData> _frameMetrics = new List<FramePerformanceData>();
        private readonly Queue<MemorySnapshot> _memorySnapshots = new Queue<MemorySnapshot>();

        private Stopwatch _currentSwitchTimer;
        private string _currentSwitchId;
        private bool _isMonitoring = false;

        // 性能阈值配置
        private readonly float _maxSwitchTime = 2.0f; // 最大切换时间(秒)
        private readonly float _minFrameRate = 30.0f; // 最小帧率
        private readonly long _maxMemoryIncrease = 50 * 1024 * 1024; // 最大内存增长(50MB)

        // 监控配置
        private readonly int _maxFrameHistory = 300; // 最多保留300帧数据(约5秒)
        private readonly int _maxMemoryHistory = 60; // 最多保留60个内存快照(约1分钟)

        private float _lastFrameTime;
        private long _lastGcMemory;

        #endregion

        #region Performance Data Structures

        /// <summary>
        /// 切换性能数据
        /// </summary>
        public class SwitchPerformanceData
        {
            public string SwitchId { get; set; }
            public string FromScene { get; set; }
            public string ToScene { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public float Duration { get; set; }
            public float AverageFrameTime { get; set; }
            public float MinFrameRate { get; set; }
            public long MemoryBefore { get; set; }
            public long MemoryAfter { get; set; }
            public long MemoryPeak { get; set; }
            public int FrameDrops { get; set; }
            public bool WasSuccessful { get; set; }
            public string ErrorMessage { get; set; }
        }

        /// <summary>
        /// 帧性能数据
        /// </summary>
        public class FramePerformanceData
        {
            public DateTime Timestamp { get; set; }
            public float FrameTime { get; set; }
            public float FrameRate { get; set; }
            public long MemoryUsage { get; set; }
            public bool IsDuringSwitch { get; set; }
            public string CurrentScene { get; set; }
        }

        /// <summary>
        /// 内存快照
        /// </summary>
        public class MemorySnapshot
        {
            public DateTime Timestamp { get; set; }
            public long TotalMemory { get; set; }
            public long GcMemory { get; set; }
            public int GcCollections { get; set; }
            public string Context { get; set; }
        }

        /// <summary>
        /// 性能警告
        /// </summary>
        public class PerformanceWarning
        {
            public WarningType Type { get; set; }
            public string Message { get; set; }
            public float Value { get; set; }
            public float Threshold { get; set; }
            public DateTime Timestamp { get; set; }
            public string Context { get; set; }
        }

        /// <summary>
        /// 性能报告
        /// </summary>
        public class PerformanceReport
        {
            public DateTime GeneratedAt { get; set; }
            public TimeSpan MonitoringDuration { get; set; }

            // 切换性能统计
            public int TotalSwitches { get; set; }
            public float AverageSwitchTime { get; set; }
            public float MaxSwitchTime { get; set; }
            public float MinSwitchTime { get; set; }
            public int FailedSwitches { get; set; }

            // 帧率统计
            public float AverageFrameRate { get; set; }
            public float MinFrameRate { get; set; }
            public float MaxFrameRate { get; set; }
            public int TotalFrameDrops { get; set; }

            // 内存统计
            public long AverageMemoryUsage { get; set; }
            public long PeakMemoryUsage { get; set; }
            public long MemoryIncrease { get; set; }
            public int GcCollections { get; set; }

            // 警告统计
            public int TotalWarnings { get; set; }
            public Dictionary<WarningType, int> WarningsByType { get; set; }

            // 场景统计
            public Dictionary<string, ScenePerformanceStats> SceneStats { get; set; }
        }

        /// <summary>
        /// 场景性能统计
        /// </summary>
        public class ScenePerformanceStats
        {
            public string SceneName { get; set; }
            public TimeSpan TotalTime { get; set; }
            public float AverageFrameRate { get; set; }
            public long AverageMemoryUsage { get; set; }
            public int SwitchesToScene { get; set; }
            public int SwitchesFromScene { get; set; }
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// 是否正在监控
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        /// <summary>
        /// 当前性能统计
        /// </summary>
        public PerformanceStats CurrentStats { get; private set; } = new PerformanceStats();

        /// <summary>
        /// 性能统计数据
        /// </summary>
        public class PerformanceStats
        {
            public float CurrentFrameRate { get; set; }
            public long CurrentMemoryUsage { get; set; }
            public int TotalSwitches { get; set; }
            public float AverageSwitchTime { get; set; }
            public int ActiveWarnings { get; set; }
        }

        #endregion

        #region Unity Lifecycle

        private string _SwitchStartedEventId;
        private string _SwitchCompletedEventId;
        private string _SwitchFailedEventId;


        public override void OnAwake()
        {
            // 订阅UI事件
            var eventBus = UIEventBus.Instance;
            if (eventBus != null)
            {
                _SwitchStartedEventId = eventBus.Subscribe<SwitchStartedEvent>(OnSwitchStarted);
                _SwitchCompletedEventId = eventBus.Subscribe<SwitchCompletedEvent>(OnSwitchCompleted);
                _SwitchFailedEventId = eventBus.Subscribe<SwitchFailedEvent>(OnSwitchFailed);
            }

            StartMonitoring();
        }

        public override void OnDestroy()
        {
            StopMonitoring();

            // 取消订阅事件
            var eventBus = UIEventBus.Instance;
            if (eventBus != null)
            {
                eventBus.Unsubscribe(_SwitchStartedEventId);
                eventBus.Unsubscribe(_SwitchCompletedEventId);
                eventBus.Unsubscribe(_SwitchFailedEventId);
            }
        }

        public override void OnUpdate()
        {
            if (!_isMonitoring) return;

            UpdateFrameMetrics();
            UpdateMemoryMetrics();
            CheckPerformanceThresholds();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 开始监控
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _isMonitoring = true;
            _lastFrameTime = Time.UnscaledDeltaTime;
            _lastGcMemory = GC.GetTotalMemory(false);

            FlaxEngine.Debug.Log("[UIPerformanceMonitor] 开始性能监控");
        }

        /// <summary>
        /// 停止监控
        /// </summary>
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _isMonitoring = false;

            FlaxEngine.Debug.Log("[UIPerformanceMonitor] 停止性能监控");
        }

        /// <summary>
        /// 生成性能报告
        /// </summary>
        public PerformanceReport GenerateReport()
        {
            var report = new PerformanceReport
            {
                GeneratedAt = DateTime.Now,
                MonitoringDuration = TimeSpan.FromSeconds(Time.UnscaledDeltaTime),
                SceneStats = new Dictionary<string, ScenePerformanceStats>(),
                WarningsByType = new Dictionary<WarningType, int>()
            };

            // 计算切换统计
            if (_switchMetrics.Count > 0)
            {
                var successfulSwitches = _switchMetrics.Values.Where(s => s.WasSuccessful).ToList();

                report.TotalSwitches = _switchMetrics.Count;
                report.FailedSwitches = _switchMetrics.Count - successfulSwitches.Count;

                if (successfulSwitches.Count > 0)
                {
                    report.AverageSwitchTime = successfulSwitches.Average(s => s.Duration);
                    report.MaxSwitchTime = successfulSwitches.Max(s => s.Duration);
                    report.MinSwitchTime = successfulSwitches.Min(s => s.Duration);
                }
            }

            // 计算帧率统计
            if (_frameMetrics.Count > 0)
            {
                report.AverageFrameRate = _frameMetrics.Average(f => f.FrameRate);
                report.MinFrameRate = _frameMetrics.Min(f => f.FrameRate);
                report.MaxFrameRate = _frameMetrics.Max(f => f.FrameRate);
                report.TotalFrameDrops = _frameMetrics.Count(f => f.FrameRate < _minFrameRate);
            }

            // 计算内存统计
            if (_memorySnapshots.Count > 0)
            {
                var snapshots = _memorySnapshots.ToList();
                report.AverageMemoryUsage = (long)snapshots.Average(s => s.TotalMemory);
                report.PeakMemoryUsage = snapshots.Max(s => s.TotalMemory);

                if (snapshots.Count > 1)
                {
                    report.MemoryIncrease = snapshots.Last().TotalMemory - snapshots.First().TotalMemory;
                }

                report.GcCollections = snapshots.Last().GcCollections - snapshots.First().GcCollections;
            }

            OnPerformanceReport?.Invoke(report);
            return report;
        }

        /// <summary>
        /// 清除历史数据
        /// </summary>
        public void ClearHistory()
        {
            _switchMetrics.Clear();
            _frameMetrics.Clear();
            _memorySnapshots.Clear();

            FlaxEngine.Debug.Log("[UIPerformanceMonitor] 清除历史数据");
        }

        /// <summary>
        /// 获取切换性能数据
        /// </summary>
        public IReadOnlyDictionary<string, SwitchPerformanceData> GetSwitchMetrics()
        {
            return _switchMetrics;
        }

        /// <summary>
        /// 获取最近的帧性能数据
        /// </summary>
        public IReadOnlyList<FramePerformanceData> GetRecentFrameMetrics(int count = 60)
        {
            return _frameMetrics.TakeLast(count).ToList();
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 更新帧性能指标
        /// </summary>
        private void UpdateFrameMetrics()
        {
            var currentTime = Time.UnscaledDeltaTime;
            var deltaTime = currentTime - _lastFrameTime;
            var frameRate = 1.0f / deltaTime;

            var frameData = new FramePerformanceData
            {
                Timestamp = DateTime.Now,
                FrameTime = deltaTime,
                FrameRate = frameRate,
                MemoryUsage = GC.GetTotalMemory(false),
                IsDuringSwitch = _currentSwitchTimer != null && _currentSwitchTimer.IsRunning,
                CurrentScene = GetCurrentSceneName()
            };

            _frameMetrics.Add(frameData);

            // 限制历史数据大小
            while (_frameMetrics.Count > _maxFrameHistory)
            {
                _frameMetrics.RemoveAt(0);
            }

            // 更新当前统计
            CurrentStats.CurrentFrameRate = frameRate;
            CurrentStats.CurrentMemoryUsage = frameData.MemoryUsage;

            _lastFrameTime = currentTime;
        }

        /// <summary>
        /// 更新内存指标
        /// </summary>
        private void UpdateMemoryMetrics()
        {
            var currentGcMemory = GC.GetTotalMemory(false);

            // 每秒创建一个内存快照
            if (_memorySnapshots.Count == 0 ||
                (DateTime.Now - _memorySnapshots.Last().Timestamp).TotalSeconds >= 1.0)
            {
                var snapshot = new MemorySnapshot
                {
                    Timestamp = DateTime.Now,
                    TotalMemory = currentGcMemory,
                    GcMemory = currentGcMemory,
                    GcCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2),
                    Context = _currentSwitchTimer?.IsRunning == true ? $"切换中: {_currentSwitchId}" : "正常运行"
                };

                _memorySnapshots.Enqueue(snapshot);

                // 限制历史数据大小
                while (_memorySnapshots.Count > _maxMemoryHistory)
                {
                    _memorySnapshots.Dequeue();
                }
            }

            _lastGcMemory = currentGcMemory;
        }

        /// <summary>
        /// 检查性能阈值
        /// </summary>
        private void CheckPerformanceThresholds()
        {
            if (_frameMetrics.Count == 0) return;

            var latestFrame = _frameMetrics.Last();

            // 检查帧率
            if (latestFrame.FrameRate < _minFrameRate)
            {
                TriggerWarning(WarningType.LowFrameRate,
                    $"帧率过低: {latestFrame.FrameRate:F1} FPS",
                    latestFrame.FrameRate, _minFrameRate);
            }

            // 检查内存使用
            if (_memorySnapshots.Count >= 2)
            {
                var recent = _memorySnapshots.TakeLast(10).ToList();
                if (recent.Count >= 2)
                {
                    var memoryIncrease = recent.Last().TotalMemory - recent.First().TotalMemory;
                    if (memoryIncrease > _maxMemoryIncrease)
                    {
                        TriggerWarning(WarningType.HighMemoryUsage,
                            $"内存增长过快: {memoryIncrease / (1024 * 1024):F1} MB",
                            memoryIncrease, _maxMemoryIncrease);
                    }
                }
            }
        }

        /// <summary>
        /// 触发性能警告
        /// </summary>
        private void TriggerWarning(WarningType type, string message, float value, float threshold)
        {
            var warning = new PerformanceWarning
            {
                Type = type,
                Message = message,
                Value = value,
                Threshold = threshold,
                Timestamp = DateTime.Now,
                Context = GetCurrentSceneName()
            };

            OnPerformanceWarning?.Invoke(warning);
            FlaxEngine.Debug.LogWarning($"[UIPerformanceMonitor] {message}");

            CurrentStats.ActiveWarnings++;
        }

        /// <summary>
        /// 获取当前场景名称
        /// </summary>
        private string GetCurrentSceneName()
        {
            // 这里应该从UI状态管理器获取当前场景
            return "Unknown";
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// 处理切换开始事件
        /// </summary>
        private void OnSwitchStarted(SwitchStartedEvent evt)
        {
            _currentSwitchId = evt.SwitchId;
            _currentSwitchTimer = Stopwatch.StartNew();

            var switchData = new SwitchPerformanceData
            {
                SwitchId = evt.SwitchId,
                FromScene = evt.FromScene,
                ToScene = evt.ToScene,
                StartTime = DateTime.Now,
                MemoryBefore = GC.GetTotalMemory(false)
            };

            _switchMetrics[evt.SwitchId] = switchData;
            CurrentStats.TotalSwitches++;

            FlaxEngine.Debug.Log($"[UIPerformanceMonitor] 开始监控切换: {evt.FromScene} -> {evt.ToScene}");
        }

        /// <summary>
        /// 处理切换完成事件
        /// </summary>
        private void OnSwitchCompleted(SwitchCompletedEvent evt)
        {
            if (_currentSwitchTimer == null || _currentSwitchId != evt.SwitchId) return;

            _currentSwitchTimer.Stop();
            var duration = (float)_currentSwitchTimer.Elapsed.TotalSeconds;

            if (_switchMetrics.TryGetValue(evt.SwitchId, out var switchData))
            {
                switchData.EndTime = DateTime.Now;
                switchData.Duration = duration;
                switchData.MemoryAfter = GC.GetTotalMemory(false);
                switchData.WasSuccessful = true;

                // 计算切换期间的帧率统计
                var switchFrames = _frameMetrics
                    .Where(f => f.IsDuringSwitch && f.Timestamp >= switchData.StartTime)
                    .ToList();

                if (switchFrames.Count > 0)
                {
                    switchData.AverageFrameTime = switchFrames.Average(f => f.FrameTime);
                    switchData.MinFrameRate = switchFrames.Min(f => f.FrameRate);
                    switchData.FrameDrops = switchFrames.Count(f => f.FrameRate < _minFrameRate);
                }

                // 更新统计
                var successfulSwitches = _switchMetrics.Values.Where(s => s.WasSuccessful).ToList();
                if (successfulSwitches.Count > 0)
                {
                    CurrentStats.AverageSwitchTime = successfulSwitches.Average(s => s.Duration);
                }

                // 检查切换时间阈值
                if (duration > _maxSwitchTime)
                {
                    TriggerWarning(WarningType.SlowSwitch,
                        $"切换时间过长: {duration:F2}s",
                        duration, _maxSwitchTime);
                }

                FlaxEngine.Debug.Log($"[UIPerformanceMonitor] 切换完成: {evt.SwitchId}, 耗时: {duration:F2}s");
            }

            _currentSwitchTimer = null;
            _currentSwitchId = null;
        }

        /// <summary>
        /// 处理切换失败事件
        /// </summary>
        private void OnSwitchFailed(SwitchFailedEvent evt)
        {
            if (_currentSwitchTimer == null || _currentSwitchId != evt.SwitchId) return;

            _currentSwitchTimer.Stop();

            if (_switchMetrics.TryGetValue(evt.SwitchId, out var switchData))
            {
                switchData.EndTime = DateTime.Now;
                switchData.Duration = (float)_currentSwitchTimer.Elapsed.TotalSeconds;
                switchData.MemoryAfter = GC.GetTotalMemory(false);
                switchData.WasSuccessful = false;
                switchData.ErrorMessage = evt.Error;

                FlaxEngine.Debug.LogError($"[UIPerformanceMonitor] 切换失败: {evt.SwitchId}, 错误: {evt.Error}");
            }

            _currentSwitchTimer = null;
            _currentSwitchId = null;
        }

        #endregion
    }

    #region Event Types

    /// <summary>
    /// 切换开始事件
    /// </summary>
    public class SwitchStartedEvent : UIEvent
    {
        public string SwitchId { get; set; }
        public string FromScene { get; set; }
        public string ToScene { get; set; }
    }

    /// <summary>
    /// 切换完成事件
    /// </summary>
    public class SwitchCompletedEvent : UIEvent
    {
        public string SwitchId { get; set; }
        public string FromScene { get; set; }
        public string ToScene { get; set; }
    }

    /// <summary>
    /// 切换失败事件
    /// </summary>
    public class SwitchFailedEvent : UIEvent
    {
        public string SwitchId { get; set; }
        public string FromScene { get; set; }
        public string ToScene { get; set; }
        public string Error { get; set; }
    }

    #endregion
}