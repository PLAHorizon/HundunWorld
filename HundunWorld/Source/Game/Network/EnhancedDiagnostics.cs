using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Debug = FlaxEngine.Debug;

namespace HundunWorld.Game.Network
{
    /// <summary>
    /// 增强诊断工具，用于详细记录网络状态和异常信息
    /// </summary>
    public static class EnhancedDiagnostics
    {
        private static readonly object _lock = new object();
        private static readonly List<string> _diagnosticLog = new List<string>();
        private static readonly int _maxLogEntries = 1000;
        
        /// <summary>
        /// 记录诊断信息
        /// </summary>
        public static void LogDiagnostic(string message)
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [诊断] {message}";
            
            lock (_lock)
            {
                _diagnosticLog.Add(logEntry);
                
                // 保持日志条目数量在限制范围内
                if (_diagnosticLog.Count > _maxLogEntries)
                {
                    _diagnosticLog.RemoveAt(0);
                }
            }
            
            Debug.Log($"[增强诊断] {message}");
        }
        
        /// <summary>
        /// 记录异常诊断信息
        /// </summary>
        public static void LogException(Exception ex, string context)
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [异常诊断] [{context}] {ex.Message}\n堆栈跟踪: {ex.StackTrace}";
            
            lock (_lock)
            {
                _diagnosticLog.Add(logEntry);
                
                // 保持日志条目数量在限制范围内
                if (_diagnosticLog.Count > _maxLogEntries)
                {
                    _diagnosticLog.RemoveAt(0);
                }
            }
            
            Debug.LogError($"[增强诊断] [{context}] 发生异常: {ex.Message}");
            Debug.LogError($"[增强诊断] 堆栈跟踪: {ex.StackTrace}");
        }
        
        /// <summary>
        /// 记录网络操作诊断信息
        /// </summary>
        public static void LogNetworkOperation(string operation, string target, bool success, string details = "")
        {
            var status = success ? "成功" : "失败";
            var message = $"网络操作: {operation} -> {target} [{status}]";
            if (!string.IsNullOrEmpty(details))
            {
                message += $" 详情: {details}";
            }
            
            LogDiagnostic(message);
        }
        
        /// <summary>
        /// 获取诊断日志
        /// </summary>
        public static string[] GetDiagnosticLog()
        {
            lock (_lock)
            {
                return _diagnosticLog.ToArray();
            }
        }
        
        /// <summary>
        /// 获取最近的诊断日志条目
        /// </summary>
        /// <param name="count">要获取的条目数量</param>
        /// <returns>最近的诊断日志条目</returns>
        public static string[] GetRecentDiagnosticLog(int count = 50)
        {
            lock (_lock)
            {
                var startIndex = Math.Max(0, _diagnosticLog.Count - count);
                var length = Math.Min(count, _diagnosticLog.Count);
                var result = new string[length];
                
                for (int i = 0; i < length; i++)
                {
                    result[i] = _diagnosticLog[startIndex + i];
                }
                
                return result;
            }
        }
        
        /// <summary>
        /// 清除诊断日志
        /// </summary>
        public static void ClearDiagnosticLog()
        {
            lock (_lock)
            {
                _diagnosticLog.Clear();
            }
        }
        
        /// <summary>
        /// 执行带诊断的网络操作
        /// </summary>
        public static async Task<T> ExecuteWithDiagnostics<T>(Func<Task<T>> operation, string operationName, string target)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                LogDiagnostic($"开始执行网络操作: {operationName} -> {target}");
                
                var result = await operation();
                
                stopwatch.Stop();
                LogNetworkOperation(operationName, target, true, $"耗时: {stopwatch.ElapsedMilliseconds}ms");
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogException(ex, $"{operationName} -> {target}");
                LogNetworkOperation(operationName, target, false, $"耗时: {stopwatch.ElapsedMilliseconds}ms, 错误: {ex.Message}");
                
                throw;
            }
        }
        
        /// <summary>
        /// 执行带诊断的网络操作（无返回值）
        /// </summary>
        public static async Task ExecuteWithDiagnostics(Func<Task> operation, string operationName, string target)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                LogDiagnostic($"开始执行网络操作: {operationName} -> {target}");
                
                await operation();
                
                stopwatch.Stop();
                LogNetworkOperation(operationName, target, true, $"耗时: {stopwatch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogException(ex, $"{operationName} -> {target}");
                LogNetworkOperation(operationName, target, false, $"耗时: {stopwatch.ElapsedMilliseconds}ms, 错误: {ex.Message}");
                
                throw;
            }
        }
    }
}