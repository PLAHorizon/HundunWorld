using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Game.Performance
{
    /// <summary>
    /// 性能监控器
    /// 负责监控和优化客户端性能
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly Dictionary<string, Stopwatch> _timers = new Dictionary<string, Stopwatch>();
        private readonly Dictionary<string, PerformanceMetric> _metrics = new Dictionary<string, PerformanceMetric>();
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        /// <summary>
        /// 开始计时
        /// </summary>
        /// <param name="operationName">操作名称</param>
        public void StartTimer(string operationName)
        {
            lock (_lockObject)
            {
                if (!_timers.ContainsKey(operationName))
                {
                    _timers[operationName] = new Stopwatch();
                }

                _timers[operationName].Restart();
            }
        }

        /// <summary>
        /// 停止计时并记录性能指标
        /// </summary>
        /// <param name="operationName">操作名称</param>
        public void StopTimer(string operationName)
        {
            lock (_lockObject)
            {
                if (_timers.ContainsKey(operationName))
                {
                    _timers[operationName].Stop();
                    var elapsedMilliseconds = _timers[operationName].ElapsedMilliseconds;

                    if (!_metrics.ContainsKey(operationName))
                    {
                        _metrics[operationName] = new PerformanceMetric(operationName);
                    }

                    _metrics[operationName].RecordMeasurement(elapsedMilliseconds);
                }
            }
        }

        /// <summary>
        /// 获取性能指标
        /// </summary>
        /// <param name="operationName">操作名称</param>
        /// <returns>性能指标</returns>
        public PerformanceMetric GetMetric(string operationName)
        {
            lock (_lockObject)
            {
                return _metrics.ContainsKey(operationName) ? _metrics[operationName] : null;
            }
        }

        /// <summary>
        /// 获取所有性能指标
        /// </summary>
        /// <returns>所有性能指标</returns>
        public IEnumerable<PerformanceMetric> GetAllMetrics()
        {
            lock (_lockObject)
            {
                return new List<PerformanceMetric>(_metrics.Values);
            }
        }

        /// <summary>
        /// 重置性能指标
        /// </summary>
        public void ResetMetrics()
        {
            lock (_lockObject)
            {
                _metrics.Clear();
            }
        }

        /// <summary>
        /// 获取内存使用情况
        /// </summary>
        /// <returns>内存使用量（字节）</returns>
        public long GetMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// 强制垃圾回收
        /// </summary>
        public void ForceGarbageCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        
        /// <summary>
        /// 生成性能报告
        /// </summary>
        /// <returns>性能报告字符串</returns>
        public string GenerateReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 性能监控报告 ===");
            sb.AppendLine($"内存使用: {GetMemoryUsage() / 1024 / 1024:F2} MB");
            sb.AppendLine();
            sb.AppendLine("操作性能指标:");
            
            var metrics = GetAllMetrics();
            foreach (var metric in metrics)
            {
                sb.AppendLine($"  {metric.OperationName}:");
                sb.AppendLine($"    调用次数: {metric.TotalCalls}");
                sb.AppendLine($"    总耗时: {metric.TotalTime}ms");
                sb.AppendLine($"    平均耗时: {metric.AverageTime}ms");
                sb.AppendLine($"    最小耗时: {metric.MinTime}ms");
                sb.AppendLine($"    最大耗时: {metric.MaxTime}ms");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // 释放托管资源
                    _timers.Clear();
                    _metrics.Clear();
                }

                // 释放非托管资源

                _disposed = true;
            }
        }

        /// <summary>
        /// 析构函数
        /// </summary>
        ~PerformanceMonitor()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// 性能指标
    /// </summary>
    public class PerformanceMetric
    {
        private readonly List<long> _measurements = new List<long>();
        private readonly object _lockObject = new object();

        public string OperationName { get; }
        public long TotalCalls { get; private set; }
        public long TotalTime { get; private set; }
        public long AverageTime => TotalCalls > 0 ? TotalTime / TotalCalls : 0;
        public long MinTime { get; private set; } = long.MaxValue;
        public long MaxTime { get; private set; } = long.MinValue;

        public PerformanceMetric(string operationName)
        {
            OperationName = operationName;
            MinTime = long.MaxValue;
            MaxTime = long.MinValue;
        }

        /// <summary>
        /// 记录测量值
        /// </summary>
        /// <param name="elapsedMilliseconds">耗时（毫秒）</param>
        public void RecordMeasurement(long elapsedMilliseconds)
        {
            lock (_lockObject)
            {
                _measurements.Add(elapsedMilliseconds);
                TotalCalls++;
                TotalTime += elapsedMilliseconds;

                if (elapsedMilliseconds < MinTime)
                    MinTime = elapsedMilliseconds;

                if (elapsedMilliseconds > MaxTime)
                    MaxTime = elapsedMilliseconds;
            }
        }

        /// <summary>
        /// 获取最近的测量值
        /// </summary>
        /// <param name="count">数量</param>
        /// <returns>最近的测量值</returns>
        public IEnumerable<long> GetRecentMeasurements(int count = 10)
        {
            lock (_lockObject)
            {
                var result = new List<long>();
                var startIndex = Math.Max(0, _measurements.Count - count);
                for (int i = startIndex; i < _measurements.Count; i++)
                {
                    result.Add(_measurements[i]);
                }
                return result;
            }
        }
    }
}